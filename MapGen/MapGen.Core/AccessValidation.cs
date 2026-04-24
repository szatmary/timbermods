using System;
using System.Collections.Generic;

namespace MapGen;

public sealed class ReachabilityReport
{
    public HashSet<GridCoord> Cells { get; } = new();
    public int TreeCount { get; set; }
    public int ResourceCount { get; set; }
    public int FolktailsFood { get; set; }
    public int IronTeethFood { get; set; }
    public int WaterAccessCount { get; set; }

    public bool MeetsMinimums =>
        TreeCount >= 30 && ResourceCount >= 15 &&
        FolktailsFood >= 7 && IronTeethFood >= 7 &&
        WaterAccessCount >= 1;
}

public static class AccessValidation
{
    /// BFS from the start voxel using same-height + water-edge rules (spec §6).
    public static ReachabilityReport FloodFillReachable(MapData map, VoxelCoord start)
    {
        var report = new ReachabilityReport();
        var queue = new Queue<GridCoord>();
        var startCell = new GridCoord(start.X, start.Y);
        report.Cells.Add(startCell);
        queue.Enqueue(startCell);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int curH = map.TopHeight(cur.X, cur.Y);
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (report.Cells.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                if (nh != curH)
                {
                    byte wd = map.WaterDepths[map.ColumnIndex(nx, ny)];
                    if (wd > 0 && wd <= 2 && nh + wd == curH + 1)
                    {
                        report.WaterAccessCount++;
                        continue;
                    }
                    continue;
                }
                report.Cells.Add(nc);
                queue.Enqueue(nc);
            }
        }

        CountOverlays(map, report);
        return report;
    }

    private static void CountOverlays(MapData map, ReachabilityReport report)
    {
        foreach (var e in map.Entities)
        {
            var cell = new GridCoord(e.Coord.X, e.Coord.Y);
            if (!report.Cells.Contains(cell)) continue;
            if (e.Kind == EntityKind.Tree) report.TreeCount++;
            else if (e.Kind == EntityKind.Resource)
            {
                report.ResourceCount++;
                if (e.BlueprintKey.Contains("Folktails"))
                {
                    report.FolktailsFood++;
                }
                else if (e.BlueprintKey.Contains("IronTeeth"))
                {
                    report.IronTeethFood++;
                }
                else
                {
                    // "Both" species — counts for each faction independently.
                    report.FolktailsFood++;
                    report.IronTeethFood++;
                }
            }
        }
    }

    /// Top-up repair: plant additional trees/resources in reachable cells
    /// until the minimums are met. Runs after initial placement.
    public static void TopUp(MapData map, Catalog catalog, ReachabilityReport report, ref Rng rng)
    {
        if (report.MeetsMinimums) return;
        var empty = new List<GridCoord>();
        foreach (var c in report.Cells)
        {
            bool occupied = false;
            foreach (var e in map.Entities)
            {
                if (e.Coord.X == c.X && e.Coord.Y == c.Y) { occupied = true; break; }
            }
            if (!occupied && Overlays.IsPlaceableCell(map, c.X, c.Y)) empty.Add(c);
        }

        while (report.TreeCount < 30 && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            empty.Remove(c);
            var tree = PickFirstMixed(catalog.Trees, ref rng);
            if (tree == null) break;
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(tree.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Tree));
            report.TreeCount++;
        }
        while ((report.FolktailsFood < 7 || report.IronTeethFood < 7 || report.ResourceCount < 15)
               && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            empty.Remove(c);
            bool needFolk = report.FolktailsFood < 7;
            CatalogEntry? res = null;
            foreach (var e in catalog.Resources)
            {
                if (needFolk && (e.Faction == Faction.Folktails || e.Faction == Faction.Both))
                { res = e; break; }
                if (!needFolk && (e.Faction == Faction.IronTeeth || e.Faction == Faction.Both))
                { res = e; break; }
            }
            if (res == null) break;
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(res.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Resource));
            report.ResourceCount++;
            if (res.Faction == Faction.Folktails || res.Faction == Faction.Both) report.FolktailsFood++;
            if (res.Faction == Faction.IronTeeth || res.Faction == Faction.Both) report.IronTeethFood++;
        }
    }

    private static CatalogEntry? PickFirstMixed(IReadOnlyList<CatalogEntry> src, ref Rng rng)
    {
        if (src.Count == 0) return null;
        return src[rng.NextRange(0, src.Count)];
    }

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }
}
