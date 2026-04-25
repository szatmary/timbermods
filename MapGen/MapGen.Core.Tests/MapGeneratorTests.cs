using Xunit;
using MapGen;

namespace MapGen.Tests;

public class MapGeneratorTests
{
    [Fact]
    public void Generate_deterministic_same_seed()
    {
        var config = new GenerationConfig { Width = 128, Height = 128, Seed = "42" };
        var gen = new MapGenerator(EmptyCatalog());
        var a = gen.Generate(config);
        var b = gen.Generate(config);
        Assert.Equal(GenerationStatus.Success, a.Status);
        Assert.Equal(GenerationStatus.Success, b.Status);
        Assert.Equal(a.Map!.Width, b.Map!.Width);
        Assert.Equal(a.Map!.Biomes, b.Map.Biomes);
        Assert.Equal(a.Map.Entities.Count, b.Map.Entities.Count);
    }

    [Fact]
    public void Generate_width_must_be_multiple_of_8()
    {
        var config = new GenerationConfig { Width = 60, Height = 60, Seed = "1" };
        var gen = new MapGenerator(EmptyCatalog());
        Assert.Throws<System.ArgumentException>(() => gen.Generate(config));
    }

    [Fact]
    public void Generate_small_map_completes()
    {
        var config = new GenerationConfig { Width = 80, Height = 80, Seed = "1" };
        var gen = new MapGenerator(EmptyCatalog());
        var result = gen.Generate(config);
        Assert.Equal(GenerationStatus.Success, result.Status);
        Assert.NotNull(result.Map);
        Assert.Equal(80, result.Map!.Width);
    }

    [Fact]
    public void SeedSweep_100_seeds_on_128x128_all_succeed()
    {
        var gen = new MapGenerator(EmptyCatalog());
        int successes = 0;
        for (int s = 0; s < 100; s++)
        {
            var result = gen.Generate(new GenerationConfig { Width = 128, Height = 128, Seed = s.ToString() });
            if (result.Status == GenerationStatus.Success) successes++;
        }
        Assert.True(successes >= 95, $"Only {successes}/100 seeds succeeded.");
    }

    [Fact]
    public void SeedSweep_various_sizes()
    {
        var gen = new MapGenerator(EmptyCatalog());
        int[] sizes = { 80, 128, 192, 256 };
        foreach (var size in sizes)
        {
            var result = gen.Generate(new GenerationConfig { Width = size, Height = size, Seed = "1" });
            Assert.Equal(GenerationStatus.Success, result.Status);
        }
    }

    public static Catalog EmptyCatalog()
    {
        return new Catalog
        {
            Trees = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "maple", BlueprintKey = "Tree.Maple.Folktails", Weight = 1 },
                new() { Key = "pine", BlueprintKey = "Tree.Pine", Weight = 1 },
                new() { Key = "birch", BlueprintKey = "Tree.Birch.IronTeeth", Weight = 1 },
                new() { Key = "dead_stump", BlueprintKey = "Tree.Dead", Weight = 0.5f },
            },
            Resources = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "berries", BlueprintKey = "Res.Berries.Folktails", Weight = 1 },
                new() { Key = "blueberries", BlueprintKey = "Res.Blueberries.IronTeeth", Weight = 1 },
                new() { Key = "mushrooms", BlueprintKey = "Res.Mushrooms", Weight = 0.5f },
                new() { Key = "carrots", BlueprintKey = "Res.Carrots", Weight = 0.5f },
                new() { Key = "cactus", BlueprintKey = "Res.Cactus", Weight = 0.5f },
                new() { Key = "dandelion", BlueprintKey = "Res.Dandelion", Weight = 0.5f },
                new() { Key = "chestnuts", BlueprintKey = "Res.Chestnuts", Weight = 0.5f },
            },
            Thorns = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "thorns", BlueprintKey = "Res.Thorns", Weight = 1 },
            },
            Ruins = new System.Collections.Generic.List<RuinCatalogEntry>
            {
                new() { Key = "shelter", BlueprintKey = "Ruin.Shelter", Weight = 1 },
            },
            BlockObjects = new System.Collections.Generic.Dictionary<string, CatalogEntry>
            {
                { "blockage", new() { Key = "blockage", BlueprintKey = "BO.Blockage", Weight = 1 } },
                { "relic", new() { Key = "relic", BlueprintKey = "BO.Relic", Weight = 1 } },
                { "unstable_core", new() { Key = "unstable_core", BlueprintKey = "BO.Core", Weight = 1 } },
                { "geothermal_vent", new() { Key = "geothermal_vent", BlueprintKey = "BO.Vent", Weight = 1 } },
                { "slope", new() { Key = "slope", BlueprintKey = "BO.Slope", Weight = 1 } },
                { "start_marker", new() { Key = "start_marker", BlueprintKey = "BO.Start", Weight = 1 } },
            },
        };
    }
}
