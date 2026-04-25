using System;
using System.Collections.Generic;

namespace MapGen;

public static class Hydrology
{
    private const int UndergroundChancePercent = 25;
    private const float SeepChancePerEdge = 0.15f;
    private const float BadwaterChanceSecondary = 0.25f;
    private const float BadwaterChancePerBadland = 0.05f;

    public static void Build(MapData map, ref Rng rng)
    {
        int target = TargetRiverCount(map.Width, map.Height, ref rng);

        var rivers = new List<List<GridCoord>>();
        for (int i = 0; i < target; i++)
        {
            bool isDelta = i > 0 && rng.NextFloat() < 0.4f;
            List<GridCoord>? river = null;
            if (isDelta && rivers.Count > 0)
                river = TraceDeltaBranch(map, rivers[rng.NextRange(0, rivers.Count)], ref rng);
            if (river == null)
                river = TraceFreshRiver(map, ref rng);
            if (river != null) rivers.Add(river);
        }

        foreach (var r in rivers) CarveRiver(map, r);

        foreach (var r in rivers)
        {
            if (rng.NextRange(0, 100) < UndergroundChancePercent)
                PromoteUnderground(map, r, ref rng);
        }

        FillSeasAndCraters(map);

        if (rivers.Count > 0)
        {
            for (int i = 0; i < rivers.Count; i++)
            {
                var head = rivers[i][0];
                bool badwater = (i > 0) && rng.NextFloat() < BadwaterChanceSecondary;
                map.Entities.Add(new PlacedEntity(
                    blueprintKey: badwater ? "BadwaterSource" : "WaterSource",
                    coord: new VoxelCoord(head.X, head.Y, map.TopHeight(head.X, head.Y) + 1),
                    facing: Orientation.North,
                    kind: badwater ? EntityKind.BadwaterSource : EntityKind.WaterSource,
                    param: PickFlowRate(ref rng)));
            }
        }

        PlaceSeeps(map, ref rng);
        PlaceBadlandBadwater(map, ref rng);
    }

    private static int TargetRiverCount(int w, int h, ref Rng rng)
    {
        int target = Math.Max(1, (int)MathF.Round(Math.Max(w, h) / 100f));
        int jitter = rng.NextRange(0, 2) == 0 ? -1 : 0;
        return Math.Max(1, target + jitter);
    }

    // ---------- Source picking ----------

    private static GridCoord? PickSource(MapData map, ref Rng rng)
    {
        var heights = new List<(int x, int y, int h)>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.WaterDepths[map.ColumnIndex(x, y)] > 0) continue;
            var b = BiomeAt(map, x, y);
            if (b == Biome.Sea || b == Biome.Crater || b == Biome.Badland || b == Biome.Start)
                continue;
            heights.Add((x, y, map.TopHeight(x, y)));
        }
        if (heights.Count == 0) return null;

        heights.Sort((a, b) => b.h.CompareTo(a.h));
        int topThird = Math.Max(1, heights.Count / 3);
        var candidates = new List<(int x, int y)>();
        for (int i = 0; i < topThird; i++)
        {
            var (x, y, _) = heights[i];
            var b = BiomeAt(map, x, y);
            if (b == Biome.Forest || b == Biome.Rocky) candidates.Add((x, y));
        }
        if (candidates.Count == 0)
        {
            for (int i = 0; i < topThird; i++)
                candidates.Add((heights[i].x, heights[i].y));
        }
        if (candidates.Count == 0) return null;
        var pick = candidates[rng.NextRange(0, candidates.Count)];
        return new GridCoord(pick.x, pick.y);
    }

    // ---------- Downhill trace ----------

    private static List<GridCoord>? TraceFreshRiver(MapData map, ref Rng rng)
    {
        var src = PickSource(map, ref rng);
        if (src == null) return null;
        return TraceDownhill(map, src.Value);
    }

    private static List<GridCoord>? TraceDeltaBranch(MapData map, List<GridCoord> parent, ref Rng rng)
    {
        if (parent.Count < 3) return null;
        int splitAt = rng.NextRange(1, parent.Count - 1);
        return TraceDownhill(map, parent[splitAt], parent);
    }

    private static List<GridCoord>? TraceDownhill(MapData map, GridCoord start,
        List<GridCoord>? avoid = null)
    {
        var path = new List<GridCoord> { start };
        var visited = new HashSet<GridCoord> { start };
        if (avoid != null) foreach (var a in avoid) visited.Add(a);
        int maxSteps = map.Width * map.Height;
        for (int step = 0; step < maxSteps; step++)
        {
            var cur = path[path.Count - 1];
            if (cur.X == 0 || cur.Y == 0 || cur.X == map.Width - 1 || cur.Y == map.Height - 1)
                return path;
            if (BiomeAt(map, cur.X, cur.Y) == Biome.Sea) return path;

            int curH = map.TopHeight(cur.X, cur.Y);
            int bestH = int.MaxValue;
            GridCoord? bestN = null;
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (visited.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                if (nh < bestH) { bestH = nh; bestN = nc; }
            }
            // If all neighbors are visited we're fully enclosed — give up.
            // If bestH > curH this is a basin; we still step to the lowest exit
            // (basin-fill semantics: water rises until it spills over the rim).
            if (bestN == null) return null;
            path.Add(bestN.Value);
            visited.Add(bestN.Value);
        }
        return path;
    }

    // ---------- Carving ----------

    private static void CarveRiver(MapData map, List<GridCoord> path)
    {
        foreach (var c in path)
        {
            var spans = map.Columns[map.ColumnIndex(c.X, c.Y)];
            if (spans.Count == 0) continue;
            var top = spans[spans.Count - 1];
            int newHeight = Math.Max(1, top.Height - 2);
            spans[spans.Count - 1] = new VoxelSpan(top.Bottom, newHeight);
            map.WaterDepths[map.ColumnIndex(c.X, c.Y)] = 3;
        }
    }

    private static void PromoteUnderground(MapData map, List<GridCoord> path, ref Rng rng)
    {
        if (path.Count < 8) return;
        int len = rng.NextRange(4, 9);
        int start = rng.NextRange(1, path.Count - len - 1);
        for (int i = start; i < start + len; i++)
        {
            var c = path[i];
            var spans = map.Columns[map.ColumnIndex(c.X, c.Y)];
            if (spans.Count == 0) continue;
            var top = spans[spans.Count - 1];
            spans.Add(new VoxelSpan(top.TopExclusive + 2, 2));
        }
    }

    private static void FillSeasAndCraters(MapData map)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (b != Biome.Sea && b != Biome.Crater) continue;
            byte depth = (b == Biome.Sea) ? (byte)4 : (byte)2;
            int x0 = mx * 8, y0 = my * 8;
            for (int vy = y0; vy < y0 + 8; vy++)
            for (int vx = x0; vx < x0 + 8; vx++)
                map.WaterDepths[map.ColumnIndex(vx, vy)] = depth;
        }
    }

    private static void PlaceSeeps(MapData map, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight - 1; my++)
        for (int mx = 0; mx < map.MetaWidth - 1; mx++)
        {
            var a = map.Biomes[map.MetaIndex(mx, my)];
            var b = map.Biomes[map.MetaIndex(mx + 1, my)];
            if (a == Biome.Rocky ^ b == Biome.Rocky)
            {
                if (rng.NextFloat() < SeepChancePerEdge)
                {
                    int vx = (mx + 1) * 8;
                    int vy = my * 8 + rng.NextRange(0, 8);
                    map.Entities.Add(new PlacedEntity(
                        "WaterSeep",
                        new VoxelCoord(vx, vy, map.TopHeight(vx, vy) + 1),
                        Orientation.North, EntityKind.WaterSource, 0.3f));
                }
            }
        }
    }

    private static void PlaceBadlandBadwater(MapData map, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            if (map.Biomes[map.MetaIndex(mx, my)] != Biome.Badland) continue;
            if (rng.NextFloat() >= BadwaterChancePerBadland) continue;
            int vx = mx * 8 + rng.NextRange(1, 7);
            int vy = my * 8 + rng.NextRange(1, 7);
            map.Entities.Add(new PlacedEntity(
                "BadwaterSource",
                new VoxelCoord(vx, vy, map.TopHeight(vx, vy) + 1),
                Orientation.North, EntityKind.BadwaterSource, 0.5f));
        }
    }

    private static float PickFlowRate(ref Rng rng)
    {
        // Conservative — strong rivers flood the carved 3-deep channel.
        float r = rng.NextFloat();
        if (r < 0.33f) return 0.3f;
        if (r < 0.67f) return 0.6f;
        return 1.0f;
    }

    private static Biome BiomeAt(MapData map, int x, int y) =>
        map.Biomes[map.MetaIndex(x / 8, y / 8)];

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }

    // ---------- Directed tracing (home-base pipeline) ----------

    /// Two-leg river: source → home-in-edge, then home-out-edge → drain.
    /// Both legs carved 1-voxel wide outside the home base; carve depth 2.
    /// Places a `WaterSource` entity at the source.
    /// Returns true on success, false if any leg couldn't be traced.
    public static bool BuildExternalRiver(MapData map, GridCoord source,
        GridCoord homeInEdge, GridCoord homeOutEdge, GridCoord drain, ref Rng rng)
    {
        var leg1 = TraceFromTo(map, source, homeInEdge);
        if (leg1 == null) return false;
        var leg2 = TraceFromTo(map, homeOutEdge, drain);
        if (leg2 == null) return false;
        CarveSingleVoxelRiver(map, leg1);
        CarveSingleVoxelRiver(map, leg2);
        // Place source entity.
        int sz = map.TopHeight(source.X, source.Y) + 1;
        map.Entities.Add(new PlacedEntity("WaterSource",
            new VoxelCoord(source.X, source.Y, sz), Orientation.North,
            EntityKind.WaterSource, 0.6f));
        return true;
    }

    /// Trace a path from `start` toward `target` greedy-biased: at each
    /// step pick the unvisited 4-neighbor closest to `target` that is also
    /// no higher than the current cell + 1 (we're allowed small uphills if
    /// it's the only way; the carve flattens them). Returns null if the
    /// trace can't progress.
    public static List<GridCoord>? TraceFromTo(MapData map, GridCoord start, GridCoord target)
    {
        var path = new List<GridCoord> { start };
        var visited = new HashSet<GridCoord> { start };
        int maxSteps = (map.Width + map.Height) * 4;
        for (int step = 0; step < maxSteps; step++)
        {
            var cur = path[path.Count - 1];
            if (cur.Equals(target)) return path;
            int curH = map.TopHeight(cur.X, cur.Y);
            // Find best unvisited neighbor: minimize Manhattan distance to target,
            // tie-break on lower elevation.
            GridCoord? best = null;
            int bestDist = int.MaxValue;
            int bestH = int.MaxValue;
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (visited.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                if (nh > curH + 1) continue;  // too uphill
                int dist = Math.Abs(nx - target.X) + Math.Abs(ny - target.Y);
                if (dist < bestDist || (dist == bestDist && nh < bestH))
                {
                    best = nc;
                    bestDist = dist;
                    bestH = nh;
                }
            }
            if (best == null) return null;
            path.Add(best.Value);
            visited.Add(best.Value);
        }
        return null;
    }

    private static void CarveSingleVoxelRiver(MapData map, List<GridCoord> path)
    {
        foreach (var c in path)
        {
            var spans = map.Columns[map.ColumnIndex(c.X, c.Y)];
            if (spans.Count == 0) continue;
            var top = spans[spans.Count - 1];
            int newHeight = Math.Max(1, top.Height - 2);
            spans[spans.Count - 1] = new VoxelSpan(top.Bottom, newHeight);
            map.WaterDepths[map.ColumnIndex(c.X, c.Y)] = 2;
        }
    }
}
