using System;
using System.IO;
using MapGen;

namespace MapGen.Preview;

public static class Program
{
    public static int Main(string[] args)
    {
        int width = 128, height = 128;
        string? singleSeed = null;
        int count = 1;
        string outDir = "previews";
        string? catalogDir = null;
        bool writeTimber = false;
        string gameVersion = "1.0.13.0-1e60728-xsm";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--width": case "-w": width = int.Parse(args[++i]); break;
                case "--height": case "-h": height = int.Parse(args[++i]); break;
                case "--seed": case "-s": singleSeed = args[++i]; break;
                case "--count": case "-n": count = int.Parse(args[++i]); break;
                case "--out": case "-o": outDir = args[++i]; break;
                case "--catalog": case "-c": catalogDir = args[++i]; break;
                case "--write-timber": writeTimber = true; break;
                case "--game-version": gameVersion = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    Console.Error.WriteLine("Usage: preview [--width N] [--height N] [--seed STR] [--count N] [--out DIR] [--catalog DIR] [--write-timber] [--game-version STR]");
                    return 1;
            }
        }

        Directory.CreateDirectory(outDir);
        var catalog = catalogDir != null
            ? Catalog.LoadFromDirectory(catalogDir)
            : BuiltInCatalog();
        var gen = new MapGenerator(catalog);

        int successes = 0;
        int runs = singleSeed != null ? 1 : count;
        for (int i = 0; i < runs; i++)
        {
            var cfg = new GenerationConfig { Width = width, Height = height };
            if (singleSeed != null) cfg.Seed = singleSeed;
            var result = gen.Generate(cfg);
            if (result.Status == GenerationStatus.Success && result.Map != null)
            {
                var safeSeed = result.ActualSeedUsed.Replace("/", "_").Replace("\\", "_");
                var outPath = Path.Combine(outDir, $"seed-{safeSeed}-{width}x{height}.png");
                Renderer.RenderToPng(result.Map, outPath);
                Console.WriteLine($"OK  seed={result.ActualSeedUsed} -> {outPath}");
                if (writeTimber)
                {
                    var timberPath = Path.Combine(outDir, $"seed-{safeSeed}-{width}x{height}.timber");
                    MapSerializer.Write(result.Map, timberPath, gameVersion);
                    Console.WriteLine($"   .timber -> {timberPath}");
                }
                successes++;
            }
            else
            {
                Console.Error.WriteLine($"FAIL: {result.FailureReason}");
            }
        }
        Console.WriteLine($"Done: {successes}/{runs} succeeded.");
        return successes > 0 ? 0 : 2;
    }

    private static Catalog BuiltInCatalog()
    {
        return new Catalog
        {
            Trees = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "maple", BlueprintKey = "Maple", Weight = 1 },
                new() { Key = "pine", BlueprintKey = "Pine", Weight = 1 },
                new() { Key = "birch", BlueprintKey = "Birch", Weight = 1 },
                new() { Key = "oak", BlueprintKey = "Oak", Weight = 1 },
                new() { Key = "dead_stump", BlueprintKey = "Pine", Weight = 0.3f },
            },
            Resources = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "berries", BlueprintKey = "BlueberryBush", Weight = 1 },
            },
            Thorns = new System.Collections.Generic.List<CatalogEntry>
            {
                new() { Key = "thorns", BlueprintKey = "Thorns", Weight = 1 },
            },
            Ruins = new System.Collections.Generic.List<RuinCatalogEntry>
            {
                new() { Key = "ruin_h3", BlueprintKey = "RuinColumnH3", Weight = 1 },
            },
            BlockObjects = new System.Collections.Generic.Dictionary<string, CatalogEntry>
            {
                { "blockage", new() { Key = "blockage", BlueprintKey = "Blockage", Weight = 1 } },
                { "relic", new() { Key = "relic", BlueprintKey = "SmallRelic", Weight = 1 } },
                { "unstable_core", new() { Key = "unstable_core", BlueprintKey = "UnstableCore", Weight = 1 } },
                { "geothermal_vent", new() { Key = "geothermal_vent", BlueprintKey = "GeothermalField", Weight = 1 } },
                { "slope", new() { Key = "slope", BlueprintKey = "Slope", Weight = 1 } },
                { "start_marker", new() { Key = "start_marker", BlueprintKey = "StartingLocation", Weight = 1 } },
            },
        };
    }
}
