using System;
using System.Collections.Generic;

namespace MapGen;

public static class Overlays
{
    private readonly struct TreeParams
    {
        public readonly float MinSpacing;
        public readonly int Cap;
        public readonly string[] AllowedSpeciesKeys;

        public TreeParams(float sp, int cap, string[] sp2)
        { MinSpacing = sp; Cap = cap; AllowedSpeciesKeys = sp2; }
    }

    private static readonly Dictionary<Biome, TreeParams> TreeParamsByBiome = new()
    {
        { Biome.Forest,  new TreeParams(2f, int.MaxValue, new[] { "pine", "birch", "maple" }) },
        { Biome.Meadow,  new TreeParams(3f, 8, new[] { "maple", "birch" }) },
        { Biome.Badland, new TreeParams(5f, 3, new[] { "dead_stump" }) },
        { Biome.Rocky,   new TreeParams(4f, 4, new[] { "pine" }) },
    };

    internal const int MetaSize = 8;

    public static void PlaceTrees(MapData map, Catalog catalog, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (!TreeParamsByBiome.TryGetValue(b, out var tp)) continue;

            var local = PoissonDisk.Sample(MetaSize, MetaSize, tp.MinSpacing, ref rng);
            int placed = 0;
            foreach (var p in local)
            {
                if (placed >= tp.Cap) break;
                int vx = mx * MetaSize + p.X;
                int vy = my * MetaSize + p.Y;
                if (!IsPlaceableCell(map, vx, vy)) continue;
                var entry = PickSpecies(catalog.Trees, tp.AllowedSpeciesKeys, ref rng);
                if (entry == null) continue;
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Tree));
                placed++;
            }
        }
    }

    private static readonly Dictionary<Biome, (float density, float spacing, string[] allowed)>
        ResourceParamsByBiome = new()
    {
        { Biome.Meadow,  (0.08f, 4f, new[] { "berries", "blueberries", "carrots", "chestnuts" }) },
        { Biome.Forest,  (0.06f, 4f, new[] { "blueberries", "mushrooms" }) },
        { Biome.Badland, (0.03f, 5f, new[] { "cactus", "dandelion" }) },
        { Biome.Rocky,   (0.02f, 5f, new[] { "blueberries" }) },
        { Biome.Crater,  (0.05f, 4f, new[] { "mushrooms" }) },
    };

    public static void PlaceResources(MapData map, Catalog catalog, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (b == Biome.Start) continue;
            if (!ResourceParamsByBiome.TryGetValue(b, out var rp)) continue;

            var local = PoissonDisk.Sample(MetaSize, MetaSize, rp.spacing, ref rng);
            foreach (var p in local)
            {
                if (rng.NextFloat() > rp.density * MetaSize) continue;
                int vx = mx * MetaSize + p.X;
                int vy = my * MetaSize + p.Y;
                if (!IsPlaceableCell(map, vx, vy)) continue;
                var entry = PickSpecies(catalog.Resources, rp.allowed, ref rng);
                if (entry == null) continue;
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Resource));
            }
        }

        // Guarantee some starting resources near the Start metacell (one per faction).
        if (map.StartMeta.HasValue)
        {
            var sm = map.StartMeta.Value;
            var local = PoissonDisk.Sample(MetaSize, MetaSize, 1.8f, ref rng);
            int planted = 0, target = 10;
            foreach (var p in local)
            {
                if (planted >= target) break;
                int vx = sm.X * MetaSize + p.X;
                int vy = sm.Y * MetaSize + p.Y;
                if (!IsPlaceableCell(map, vx, vy)) continue;
                bool folk = (planted % 2 == 0);
                var pool = new List<CatalogEntry>();
                foreach (var e in catalog.Resources)
                {
                    if (folk && (e.Faction == Faction.Folktails || e.Faction == Faction.Both)) pool.Add(e);
                    else if (!folk && (e.Faction == Faction.IronTeeth || e.Faction == Faction.Both)) pool.Add(e);
                }
                if (pool.Count == 0) continue;
                var entry = pool[rng.NextRange(0, pool.Count)];
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Resource));
                planted++;
            }
        }
    }

    private static readonly Dictionary<Biome, (float density, float spacing)>
        ThornParamsByBiome = new()
    {
        { Biome.Badland, (0.05f, 3f) },
        { Biome.Rocky,   (0.02f, 3f) },
        { Biome.Forest,  (0.01f, 4f) },
        { Biome.Meadow,  (0.005f, 5f) },
    };

    public static void PlaceThorns(MapData map, Catalog catalog, ref Rng rng)
    {
        if (catalog.Thorns.Count == 0) return;
        var entry = catalog.Thorns[0];
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (!ThornParamsByBiome.TryGetValue(b, out var tp)) continue;

            var local = PoissonDisk.Sample(MetaSize, MetaSize, tp.spacing, ref rng);
            foreach (var p in local)
            {
                if (rng.NextFloat() > tp.density * MetaSize) continue;
                int vx = mx * MetaSize + p.X;
                int vy = my * MetaSize + p.Y;
                if (!IsPlaceableCell(map, vx, vy)) continue;
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Thorn));
            }
        }
    }

    internal static bool IsPlaceableCell(MapData map, int vx, int vy)
    {
        if (vx < 0 || vy < 0 || vx >= map.Width || vy >= map.Height) return false;
        if (map.WaterDepths[map.ColumnIndex(vx, vy)] > 0) return false;
        if (map.Columns[map.ColumnIndex(vx, vy)].Count == 0) return false;
        return true;
    }

    internal static CatalogEntry? PickSpecies(IReadOnlyList<CatalogEntry> catalog,
        string[] allowed, ref Rng rng)
    {
        var pool = new List<CatalogEntry>();
        foreach (var e in catalog)
        {
            foreach (var a in allowed)
            {
                if (e.Key == a) { pool.Add(e); break; }
            }
        }
        if (pool.Count == 0) return null;
        var weights = new float[pool.Count];
        for (int i = 0; i < pool.Count; i++) weights[i] = pool[i].Weight;
        return pool[rng.WeightedPick(weights)];
    }

    // ---------- Ruins (spec section 5.5) ----------

    private static readonly Dictionary<Biome, float> RuinDensityBySector = new()
    {
        { Biome.Badland, 8f },
        { Biome.Rocky,   3f },
        { Biome.Meadow,  2f },
        { Biome.Forest,  1f },
    };

    public static void PlaceRuins(MapData map, Catalog catalog, ref Rng rng)
    {
        if (catalog.Ruins.Count == 0) return;

        var forbidden = BuildStartExclusionSet(map);

        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (!RuinDensityBySector.TryGetValue(b, out var density)) continue;
            if (forbidden.Contains(new GridCoord(mx, my))) continue;

            float expected = density * 64f / 256f;
            int count = (int)MathF.Round(expected + (rng.NextFloat() - 0.5f));
            for (int i = 0; i < count; i++)
            {
                int vx = mx * MetaSize + rng.NextRange(0, MetaSize);
                int vy = my * MetaSize + rng.NextRange(0, MetaSize);
                if (!IsPlaceableCell(map, vx, vy)) continue;
                var entry = PickRuin(catalog.Ruins, ref rng);
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z),
                    RandomOrientation(ref rng), EntityKind.Ruin));
            }
        }
    }

    public static void PlaceBlockages(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("blockage", out var entry)) return;
        int ruinCount = 0;
        foreach (var e in map.Entities) if (e.Kind == EntityKind.Ruin) ruinCount++;
        int blockCount = Math.Max(0, ruinCount / 3);

        var ruinPositions = new List<GridCoord>();
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.Ruin)
                ruinPositions.Add(new GridCoord(e.Coord.X, e.Coord.Y));

        for (int i = 0; i < blockCount && ruinPositions.Count > 0; i++)
        {
            var anchor = ruinPositions[rng.NextRange(0, ruinPositions.Count)];
            int vx = anchor.X + rng.NextRange(-2, 3);
            int vy = anchor.Y + rng.NextRange(-2, 3);
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z),
                RandomOrientation(ref rng), EntityKind.Blockage));
        }
    }

    public static void PlaceRelics(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("relic", out var entry)) return;
        int area = map.Width * map.Height;
        int count = (int)MathF.Round(area / 25000f) + 1;
        var ruinPositions = new List<GridCoord>();
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.Ruin)
                ruinPositions.Add(new GridCoord(e.Coord.X, e.Coord.Y));

        for (int i = 0; i < count; i++)
        {
            int vx, vy;
            if (ruinPositions.Count > 0 && rng.NextFloat() < 0.7f)
            {
                var anchor = ruinPositions[rng.NextRange(0, ruinPositions.Count)];
                vx = anchor.X + rng.NextRange(-3, 4);
                vy = anchor.Y + rng.NextRange(-3, 4);
            }
            else
            {
                vx = rng.NextRange(0, map.Width);
                vy = rng.NextRange(0, map.Height);
            }
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Relic));
        }
    }

    // ---------- Shared helpers ----------

    private static HashSet<GridCoord> BuildStartExclusionSet(MapData map)
    {
        var ex = new HashSet<GridCoord>();
        if (!map.StartMeta.HasValue) return ex;
        var sm = map.StartMeta.Value;
        ex.Add(sm);
        if (sm.X > 0) ex.Add(new GridCoord(sm.X - 1, sm.Y));
        if (sm.X + 1 < map.MetaWidth) ex.Add(new GridCoord(sm.X + 1, sm.Y));
        if (sm.Y > 0) ex.Add(new GridCoord(sm.X, sm.Y - 1));
        if (sm.Y + 1 < map.MetaHeight) ex.Add(new GridCoord(sm.X, sm.Y + 1));
        return ex;
    }

    private static RuinCatalogEntry PickRuin(IReadOnlyList<RuinCatalogEntry> ruins, ref Rng rng)
    {
        var weights = new float[ruins.Count];
        for (int i = 0; i < ruins.Count; i++) weights[i] = ruins[i].Weight;
        return ruins[rng.WeightedPick(weights)];
    }

    private static Orientation RandomOrientation(ref Rng rng) =>
        (Orientation)rng.NextRange(0, 4);

    // ---------- Unstable cores (spec section 5.7) ----------

    public static void PlaceUnstableCores(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("unstable_core", out var entry)) return;
        int area = map.Width * map.Height;
        int count = (int)MathF.Round(area / 20000f) + rng.NextRange(-1, 2);
        if (count <= 0) return;

        int attempts = count * 20;
        int placed = 0;
        const float MinSpacing = 20f;
        var positions = new List<GridCoord>();
        for (int a = 0; a < attempts && placed < count; a++)
        {
            int vx = rng.NextRange(0, map.Width);
            int vy = rng.NextRange(0, map.Height);
            var b = map.Biomes[map.MetaIndex(vx / 8, vy / 8)];
            if (b != Biome.Rocky && b != Biome.Badland) continue;
            if (WithinOfStart(map, vx, vy, radius: 15)) continue;
            if (TooClose(positions, vx, vy, MinSpacing)) continue;
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.UnstableCore));
            positions.Add(new GridCoord(vx, vy));
            placed++;
        }
    }

    public static void PlaceGeothermalVents(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("geothermal_vent", out var entry)) return;
        int area = map.Width * map.Height;
        int count = (int)MathF.Round(area / 30000f) + rng.NextRange(-1, 2);
        if (count <= 0) return;

        int attempts = count * 20;
        int placed = 0;
        const float MinSpacing = 25f;
        var positions = new List<GridCoord>();
        for (int a = 0; a < attempts && placed < count; a++)
        {
            int vx = rng.NextRange(0, map.Width);
            int vy = rng.NextRange(0, map.Height);
            var b = map.Biomes[map.MetaIndex(vx / 8, vy / 8)];
            if (b != Biome.Badland && b != Biome.Rocky) continue;
            if (WithinOfStart(map, vx, vy, radius: 20)) continue;
            if (TooClose(positions, vx, vy, MinSpacing)) continue;
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.GeothermalVent));
            positions.Add(new GridCoord(vx, vy));
            placed++;
        }
    }

    private static bool WithinOfStart(MapData map, int vx, int vy, int radius)
    {
        if (!map.StartMeta.HasValue) return false;
        var sm = map.StartMeta.Value;
        int sx = sm.X * MetaSize + MetaSize / 2;
        int sy = sm.Y * MetaSize + MetaSize / 2;
        int dx = vx - sx, dy = vy - sy;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool TooClose(List<GridCoord> positions, int vx, int vy, float minSpacing)
    {
        float sq = minSpacing * minSpacing;
        foreach (var p in positions)
        {
            int dx = vx - p.X, dy = vy - p.Y;
            if (dx * dx + dy * dy < sq) return true;
        }
        return false;
    }

    // ---------- Start marker ----------

    public static void PlaceStartMarker(MapData map, Catalog catalog)
    {
        if (!map.StartMeta.HasValue) return;
        if (!catalog.BlockObjects.TryGetValue("start_marker", out var entry)) return;
        var sm = map.StartMeta.Value;
        int vx = sm.X * MetaSize + MetaSize / 2;
        int vy = sm.Y * MetaSize + MetaSize / 2;
        int z = map.TopHeight(vx, vy) + 1;
        map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
            new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.StartMarker));
    }
}
