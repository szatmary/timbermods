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
            string effectiveSeed = attempt == 0 ? config.Seed : $"{config.Seed}-{attempt}";
            var result = TryGenerate(config, effectiveSeed, log);
            if (result != null)
                return new GenerationResult(GenerationStatus.Success, result, effectiveSeed, attempt, log);
        }
        return new GenerationResult(GenerationStatus.Failed, null, config.Seed, config.PipelineRetryBudget, log,
            "Failed to produce a playable map within retry budget.");
    }

    private MapData? TryGenerate(GenerationConfig config, string seed, List<string> log)
    {
        var rng = new Rng(seed);
        var map = new MapData(config.Width, config.Height, seed);
        map.MetaWidth = config.Width / config.MetaCellSize;
        map.MetaHeight = config.Height / config.MetaCellSize;
        map.Columns = new List<VoxelSpan>[config.Width * config.Height];
        for (int i = 0; i < map.Columns.Length; i++) map.Columns[i] = new List<VoxelSpan>();
        map.WaterDepths = new byte[config.Width * config.Height];
        // All metacells default to Meadow in v1 (no biome WFC).
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        for (int i = 0; i < map.Biomes.Length; i++) map.Biomes[i] = Biome.Meadow;

        // --- Step 1: Pick home-base center (≥12 voxels from any edge). ---
        int margin = 12 + HomeBase.RegionSize / 2;
        if (config.Width < margin * 2 || config.Height < margin * 2)
        {
            log.Add($"seed={seed}: map too small for home base (need ≥ {margin * 2} on each axis)");
            return null;
        }
        int cx = rng.NextRange(margin, config.Width - margin);
        int cy = rng.NextRange(margin, config.Height - margin);
        int hBase = rng.NextRange(4, 9);  // [4, 8]
        var variant = (WaterVariant)rng.NextRange(0, 3);  // POND, RIVER, BOTH

        // --- Step 2: Fill the WHOLE map with flat ground at H_base - 1
        // first, so the home base sits on a slight plateau and the rest of
        // the map is consistent. (Done before HomeBase so that hydrology
        // traces have terrain to walk on.) ---
        Heightmap.FlatFill(map, hBase - 1, (x, y) => false);
        map.StartMeta = new GridCoord(cx / config.MetaCellSize, cy / config.MetaCellSize);

        // --- Step 3: Generate the home base region. Mutates map. ---
        var grid = HomeBase.Generate(map, _catalog, cx, cy, hBase, variant, ref rng);

        // --- Step 4: External river (only RIVER and BOTH variants). ---
        if (variant == WaterVariant.River || variant == WaterVariant.Both)
        {
            // Find an in-edge cell (any home-base water cell on the home-base
            // outer boundary) and an out-edge cell (likewise on opposite side).
            // For v1, simplification: pick the leftmost and rightmost water cells
            // touching the home-base perimeter as in/out anchors.
            var (inEdge, outEdge) = FindHomeBaseRiverEdges(map, grid, cx, cy);
            if (inEdge.HasValue && outEdge.HasValue)
            {
                // Pick a remote source: random map cell, elevation ≥ hBase + 2,
                // outside the home-base 24x24.
                var source = PickSource(map, cx, cy, hBase, ref rng);
                var drain = PickDrain(map, hBase, ref rng);
                if (source.HasValue && drain.HasValue)
                {
                    Hydrology.BuildExternalRiver(map, source.Value, inEdge.Value,
                        outEdge.Value, drain.Value, ref rng);
                }
                else
                {
                    log.Add($"seed={seed}: no source/drain found; river skipped");
                }
            }
            else
            {
                log.Add($"seed={seed}: home base has no usable river in/out edge; river skipped");
            }
        }

        log.Add($"seed={seed}: success (entities={map.Entities.Count}, variant={variant})");
        return map;
    }

    private static (GridCoord? In, GridCoord? Out) FindHomeBaseRiverEdges(
        MapData map, LandUseGrid grid, int cx, int cy)
    {
        int x0 = cx - HomeBase.RegionSize / 2;
        int y0 = cy - HomeBase.RegionSize / 2;
        // Scan the perimeter of the 24x24 region for water cells.
        var waters = new List<GridCoord>();
        for (int ly = 0; ly < grid.Size; ly++)
        for (int lx = 0; lx < grid.Size; lx++)
        {
            bool perim = lx == 0 || ly == 0 || lx == grid.Size - 1 || ly == grid.Size - 1;
            if (!perim) continue;
            if (grid.Get(lx, ly) != LandUseGrid.Use.Water) continue;
            waters.Add(new GridCoord(x0 + lx, y0 + ly));
        }
        if (waters.Count < 2) return (null, null);
        // Pick the leftmost-most and rightmost-most as anchors.
        waters.Sort((a, b) => a.X.CompareTo(b.X));
        return (waters[0], waters[waters.Count - 1]);
    }

    private static GridCoord? PickSource(MapData map, int hCx, int hCy, int hBase, ref Rng rng)
    {
        int half = HomeBase.RegionSize / 2;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = rng.NextRange(0, map.Width);
            int y = rng.NextRange(0, map.Height);
            // Outside home base box.
            if (System.Math.Abs(x - hCx) <= half && System.Math.Abs(y - hCy) <= half) continue;
            if (map.TopHeight(x, y) >= hBase + 2) return new GridCoord(x, y);
        }
        // Relaxed: any cell outside home base at elevation ≥ hBase.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = rng.NextRange(0, map.Width);
            int y = rng.NextRange(0, map.Height);
            if (System.Math.Abs(x - hCx) <= half && System.Math.Abs(y - hCy) <= half) continue;
            if (map.TopHeight(x, y) >= hBase) return new GridCoord(x, y);
        }
        return null;
    }

    private static GridCoord? PickDrain(MapData map, int hBase, ref Rng rng)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int side = rng.NextRange(0, 4);
            int x, y;
            switch (side)
            {
                case 0: x = rng.NextRange(0, map.Width);  y = 0; break;
                case 1: x = map.Width - 1;                 y = rng.NextRange(0, map.Height); break;
                case 2: x = rng.NextRange(0, map.Width);  y = map.Height - 1; break;
                default: x = 0;                            y = rng.NextRange(0, map.Height); break;
            }
            if (map.TopHeight(x, y) <= hBase - 1) return new GridCoord(x, y);
        }
        // Fallback: any edge cell.
        return new GridCoord(0, map.Height / 2);
    }
}
