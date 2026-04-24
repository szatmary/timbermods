using System;
using System.IO;
using MapGen;

namespace MapGen.Preview;

public static class Program
{
    public static int Main(string[] args)
    {
        int width = 128, height = 128;
        uint seedStart = 1, seedEnd = 1;
        string outDir = "previews";
        string? catalogDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--width": case "-w": width = int.Parse(args[++i]); break;
                case "--height": case "-h": height = int.Parse(args[++i]); break;
                case "--seed": case "-s": seedStart = seedEnd = uint.Parse(args[++i]); break;
                case "--seed-range":
                    var parts = args[++i].Split('-');
                    seedStart = uint.Parse(parts[0]);
                    seedEnd = parts.Length > 1 ? uint.Parse(parts[1]) : seedStart;
                    break;
                case "--out": case "-o": outDir = args[++i]; break;
                case "--catalog": case "-c": catalogDir = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        Directory.CreateDirectory(outDir);
        var catalog = catalogDir != null
            ? Catalog.LoadFromDirectory(catalogDir)
            : BuiltInCatalog();
        var gen = new MapGenerator(catalog);

        int successes = 0;
        for (uint s = seedStart; s <= seedEnd; s++)
        {
            var cfg = new GenerationConfig { Width = width, Height = height, Seed = s };
            var result = gen.Generate(cfg);
            if (result.Status == GenerationStatus.Success && result.Map != null)
            {
                var outPath = Path.Combine(outDir, $"seed-{result.ActualSeedUsed}-{width}x{height}.png");
                Renderer.RenderToPng(result.Map, outPath);
                Console.WriteLine($"OK  seed={s} (actual={result.ActualSeedUsed}) -> {outPath}");
                successes++;
            }
            else
            {
                Console.Error.WriteLine($"FAIL seed={s}: {result.FailureReason}");
            }
        }
        Console.WriteLine($"Done: {successes}/{seedEnd - seedStart + 1} succeeded.");
        return successes > 0 ? 0 : 2;
    }

    private static Catalog BuiltInCatalog()
    {
        return new Catalog
        {
            Trees = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "maple", BlueprintKey = "Tree.Maple.Folktails", Faction = Faction.Folktails, Weight = 1 },
                new() { Key = "pine", BlueprintKey = "Tree.Pine", Faction = Faction.Both, Weight = 1 },
                new() { Key = "birch", BlueprintKey = "Tree.Birch.IronTeeth", Faction = Faction.IronTeeth, Weight = 1 },
                new() { Key = "dead_stump", BlueprintKey = "Tree.Dead", Faction = Faction.Both, Weight = 0.5f },
            },
            Resources = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "berries", BlueprintKey = "Res.Berries.Folktails", Faction = Faction.Folktails, Weight = 1 },
                new() { Key = "blueberries", BlueprintKey = "Res.Blueberries.IronTeeth", Faction = Faction.IronTeeth, Weight = 1 },
                new() { Key = "mushrooms", BlueprintKey = "Res.Mushrooms", Faction = Faction.Both, Weight = 0.5f },
                new() { Key = "carrots", BlueprintKey = "Res.Carrots", Faction = Faction.Both, Weight = 0.5f },
                new() { Key = "cactus", BlueprintKey = "Res.Cactus", Faction = Faction.Both, Weight = 0.5f },
                new() { Key = "dandelion", BlueprintKey = "Res.Dandelion", Faction = Faction.Both, Weight = 0.5f },
                new() { Key = "chestnuts", BlueprintKey = "Res.Chestnuts", Faction = Faction.Both, Weight = 0.5f },
            },
            Thorns = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "thorns", BlueprintKey = "Res.Thorns", Faction = Faction.Both, Weight = 1 },
            },
            Ruins = new System.Collections.Generic.List<RuinCatalogEntry>
            {
                new() { Key = "shelter", BlueprintKey = "Ruin.Shelter", Faction = Faction.Both, Weight = 1 },
            },
            BlockObjects = new System.Collections.Generic.Dictionary<string, CatalogEntry>
            {
                { "blockage", new() { Key = "blockage", BlueprintKey = "BO.Blockage", Faction = Faction.Both, Weight = 1 } },
                { "relic", new() { Key = "relic", BlueprintKey = "BO.Relic", Faction = Faction.Both, Weight = 1 } },
                { "unstable_core", new() { Key = "unstable_core", BlueprintKey = "BO.Core", Faction = Faction.Both, Weight = 1 } },
                { "geothermal_vent", new() { Key = "geothermal_vent", BlueprintKey = "BO.Vent", Faction = Faction.Both, Weight = 1 } },
                { "slope", new() { Key = "slope", BlueprintKey = "BO.Slope", Faction = Faction.Both, Weight = 1 } },
                { "start_marker", new() { Key = "start_marker", BlueprintKey = "BO.Start", Faction = Faction.Both, Weight = 1 } },
            },
        };
    }
}
