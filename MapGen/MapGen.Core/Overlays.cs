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
}
