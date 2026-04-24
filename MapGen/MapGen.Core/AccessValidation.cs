using System;
using System.Collections.Generic;

namespace MapGen;

public sealed class ReachabilityReport
{
    public HashSet<GridCoord> Cells { get; } = new();
    public int TreeCount { get; set; }
    public int ResourceCount { get; set; }
    public int WaterAccessCount { get; set; }

    public bool MeetsMinimums =>
        TreeCount >= 30 && ResourceCount >= 15 && WaterAccessCount >= 1;
}

public static class AccessValidation
{
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
                    if (wd > 0 && nh + wd == curH + 1)
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
            else if (e.Kind == EntityKind.Resource) report.ResourceCount++;
            else if (e.Kind == EntityKind.WaterSource || e.Kind == EntityKind.BadwaterSource)
                report.WaterAccessCount++;
        }
    }

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
            if (catalog.Trees.Count == 0) break;
            var tree = catalog.Trees[rng.NextRange(0, catalog.Trees.Count)];
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(tree.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Tree));
            report.TreeCount++;
        }
        while (report.ResourceCount < 15 && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            empty.Remove(c);
            if (catalog.Resources.Count == 0) break;
            var res = catalog.Resources[rng.NextRange(0, catalog.Resources.Count)];
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(res.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Resource));
            report.ResourceCount++;
        }
        if (report.WaterAccessCount < 1 && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity("WaterSource",
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.WaterSource, 1f));
            report.WaterAccessCount++;
        }
    }

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }
}
