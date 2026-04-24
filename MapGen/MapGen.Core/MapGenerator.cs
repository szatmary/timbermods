using System.Collections.Generic;

namespace MapGen;

public sealed class MapGenerator
{
    private readonly Catalog _catalog;

    public MapGenerator(Catalog catalog)
    {
        _catalog = catalog;
    }

    public GenerationResult Generate(GenerationConfig config)
    {
        config.Validate();
        var log = new List<string>();
        for (int attempt = 0; attempt <= config.PipelineRetryBudget; attempt++)
        {
            uint effectiveSeed = config.Seed + (uint)attempt;
            var result = TryGenerate(config, effectiveSeed, log);
            if (result != null)
                return new GenerationResult(GenerationStatus.Success, result, effectiveSeed, attempt, log);
        }
        return new GenerationResult(GenerationStatus.Failed, null, config.Seed, config.PipelineRetryBudget, log,
            "Failed to produce a playable map within retry budget.");
    }

    private MapData? TryGenerate(GenerationConfig config, uint seed, List<string> log)
    {
        var rng = new Rng(seed);
        var map = new MapData(config.Width, config.Height, seed);
        map.MetaWidth = config.Width / config.MetaCellSize;
        map.MetaHeight = config.Height / config.MetaCellSize;
        map.Columns = new List<VoxelSpan>[config.Width * config.Height];
        for (int i = 0; i < map.Columns.Length; i++) map.Columns[i] = new List<VoxelSpan>();
        map.WaterDepths = new byte[config.Width * config.Height];

        // --- Biome WFC ---
        var biomes = BiomeGrid.Solve(map.MetaWidth, map.MetaHeight, ref rng);
        if (biomes == null) { log.Add($"seed={seed}: WFC contradiction"); return null; }
        BiomeGrid.RewriteEdgeCraters(biomes, map.MetaWidth, map.MetaHeight);
        map.Biomes = biomes;

        // --- Start selection ---
        var pick = StartSelection.Pick(biomes, map.MetaWidth, map.MetaHeight, ref rng);
        if (pick == null) { log.Add($"seed={seed}: no valid Start"); return null; }
        StartSelection.Apply(biomes, map.MetaWidth, pick.Value);
        map.StartMeta = pick.Value;

        // --- Heightmap ---
        Heightmap.Build(map, ref rng);

        // --- Hydrology ---
        Hydrology.Build(map, ref rng);

        // --- Overlays ---
        Overlays.PlaceTrees(map, _catalog, ref rng);
        Overlays.PlaceResources(map, _catalog, ref rng);
        Overlays.PlaceThorns(map, _catalog, ref rng);
        Overlays.PlaceRuins(map, _catalog, ref rng);
        Overlays.PlaceBlockages(map, _catalog, ref rng);
        Overlays.PlaceRelics(map, _catalog, ref rng);
        Overlays.PlaceUnstableCores(map, _catalog, ref rng);
        Overlays.PlaceGeothermalVents(map, _catalog, ref rng);
        Overlays.PlaceStartMarker(map, _catalog);

        // --- Access validation ---
        if (!map.StartMeta.HasValue) { log.Add($"seed={seed}: StartMeta missing"); return null; }
        var sm = map.StartMeta.Value;
        int sx = sm.X * config.MetaCellSize + config.MetaCellSize / 2;
        int sy = sm.Y * config.MetaCellSize + config.MetaCellSize / 2;
        int sz = map.TopHeight(sx, sy) + 1;
        var report = AccessValidation.FloodFillReachable(map, new VoxelCoord(sx, sy, sz));
        if (!report.MeetsMinimums)
        {
            AccessValidation.TopUp(map, _catalog, report, ref rng);
            if (!report.MeetsMinimums)
            {
                log.Add($"seed={seed}: minimums unmet (trees={report.TreeCount} resources={report.ResourceCount} folk={report.FolktailsFood} it={report.IronTeethFood} water={report.WaterAccessCount})");
                return null;
            }
        }

        log.Add($"seed={seed}: success (entities={map.Entities.Count}, reachable={report.Cells.Count})");
        return map;
    }
}
