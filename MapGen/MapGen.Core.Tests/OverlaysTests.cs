using Xunit;
using MapGen;

namespace MapGen.Tests;

public class OverlaysTests
{
    [Fact]
    public void Trees_only_planted_in_allowed_biomes()
    {
        var map = MakePreparedMap(64, 64, out var rng);
        var catalog = DefaultCatalog();
        Overlays.PlaceTrees(map, catalog, ref rng);

        foreach (var e in map.Entities)
        {
            if (e.Kind != EntityKind.Tree) continue;
            var b = map.Biomes[map.MetaIndex(e.Coord.X / 8, e.Coord.Y / 8)];
            Assert.NotEqual(Biome.Sea, b);
            Assert.NotEqual(Biome.Start, b);
        }
    }

    [Fact]
    public void Resources_planted_produces_food_variety()
    {
        var map = MakePreparedMap(64, 64, out var rng);
        var catalog = DefaultCatalog();
        Overlays.PlaceResources(map, catalog, ref rng);

        var keys = new System.Collections.Generic.HashSet<string>();
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.Resource) keys.Add(e.BlueprintKey);
        Assert.True(keys.Count >= 2, $"Expected at least 2 resource species, got {keys.Count}");
    }

    [Fact]
    public void Thorns_appear_in_badland_more_than_meadow()
    {
        int badlandThorns = 0, meadowThorns = 0;
        for (uint s = 0; s < 5; s++)
        {
            var map = MakePreparedMap(64, 64, out var rng, uniformBiome: null);
            var catalog = DefaultCatalog();
            Overlays.PlaceThorns(map, catalog, ref rng);
            foreach (var e in map.Entities)
            {
                if (e.Kind != EntityKind.Thorn) continue;
                var b = map.Biomes[map.MetaIndex(e.Coord.X / 8, e.Coord.Y / 8)];
                if (b == Biome.Badland) badlandThorns++;
                else if (b == Biome.Meadow) meadowThorns++;
            }
        }
        Assert.True(badlandThorns > meadowThorns,
            $"Badland thorns {badlandThorns} should exceed Meadow {meadowThorns}.");
    }

    private static MapData MakePreparedMap(int w, int h, out Rng rng, Biome? uniformBiome = Biome.Meadow)
    {
        rng = new Rng(42u);
        var map = new MapData(w, h, 1u);
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        if (uniformBiome.HasValue)
            for (int i = 0; i < map.Biomes.Length; i++) map.Biomes[i] = uniformBiome.Value;
        else
        {
            for (int i = 0; i < map.Biomes.Length; i++)
                map.Biomes[i] = (i % 2 == 0) ? Biome.Badland : Biome.Meadow;
        }
        map.StartMeta = new GridCoord(0, 0);
        map.Biomes[0] = Biome.Start;
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
        {
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan> { new VoxelSpan(0, 5) };
        }
        map.WaterDepths = new byte[w * h];
        return map;
    }

    private static Catalog DefaultCatalog()
    {
        return new Catalog
        {
            Trees = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "maple", BlueprintKey = "Tree.Maple", Faction = Faction.Folktails, Weight = 1 },
                new() { Key = "pine", BlueprintKey = "Tree.Pine", Faction = Faction.Both, Weight = 1 },
                new() { Key = "birch", BlueprintKey = "Tree.Birch", Faction = Faction.IronTeeth, Weight = 1 },
            },
            Resources = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "berries", BlueprintKey = "Res.Berries", Faction = Faction.Folktails, Weight = 1 },
                new() { Key = "blueberries", BlueprintKey = "Res.Blueberries", Faction = Faction.IronTeeth, Weight = 1 },
            },
            Thorns = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "thorns", BlueprintKey = "Res.Thorns", Faction = Faction.Both, Weight = 1 },
            },
            Ruins = new System.Collections.Generic.List<RuinCatalogEntry>(),
            BlockObjects = new System.Collections.Generic.Dictionary<string, CatalogEntry>(),
        };
    }
}
