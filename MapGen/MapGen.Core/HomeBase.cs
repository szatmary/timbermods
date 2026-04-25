using System;
using System.Collections.Generic;

namespace MapGen;

public enum WaterVariant : byte { Pond, River, Both }

public static class HomeBase
{
    public const int RegionSize = 24;
    public const int DistrictSize = 4;
    public const int DistrictRingPadding = 3;  // 3-cell ring around district
    public const int FarmSize = 6;

    /// Generates a 24x24 home-base region centered on (cx, cy) at H_base.
    /// Mutates `map`: writes terrain columns, water depths, and entities.
    /// Returns the LandUseGrid for the caller (so MapGenerator can know
    /// which cells are reserved when filling the rest of the map).
    public static LandUseGrid Generate(MapData map, Catalog catalog,
        int cx, int cy, int hBase, WaterVariant variant, ref Rng rng)
    {
        var grid = new LandUseGrid(RegionSize);
        int x0 = cx - RegionSize / 2;
        int y0 = cy - RegionSize / 2;

        // Sub-stages are filled in by Tasks 2-6.
        ReserveDistrictAndFarm(grid, ref rng);
        PlaceWaterFeature(grid, hBase, variant, ref rng);
        SetTerrainHeights(map, grid, x0, y0, hBase);
        PlaceTrees(map, grid, catalog, x0, y0, hBase, ref rng);
        PlaceBerries(map, grid, catalog, x0, y0, hBase, ref rng);
        PlaceWaterEntities(map, grid, x0, y0, hBase, variant, ref rng);
        PlaceStartingLocation(map, grid, catalog, x0, y0, hBase);

        return grid;
    }

    // Stub implementations — Tasks 2-6 replace these.
    private static void ReserveDistrictAndFarm(LandUseGrid g, ref Rng rng) { }
    private static void PlaceWaterFeature(LandUseGrid g, int hBase, WaterVariant v, ref Rng rng) { }
    private static void SetTerrainHeights(MapData m, LandUseGrid g, int x0, int y0, int hBase) { }
    private static void PlaceTrees(MapData m, LandUseGrid g, Catalog c, int x0, int y0, int hBase, ref Rng rng) { }
    private static void PlaceBerries(MapData m, LandUseGrid g, Catalog c, int x0, int y0, int hBase, ref Rng rng) { }
    private static void PlaceWaterEntities(MapData m, LandUseGrid g, int x0, int y0, int hBase, WaterVariant v, ref Rng rng) { }
    private static void PlaceStartingLocation(MapData m, LandUseGrid g, Catalog c, int x0, int y0, int hBase) { }
}

/// Per-cell reservation tracker for a 24x24 home-base region. Indexed
/// in local region coords (0..23, 0..23).
public sealed class LandUseGrid
{
    public enum Use : byte
    {
        None,
        DistrictCenter,   // the 4x4 starting building spot
        DistrictRing,     // 3-cell buffer around DistrictCenter
        Farm,             // 6x6 reserved for the player's farm
        Water,            // pond cell or river cell
        Tree,             // tree placed
        Berry,            // berry bush placed
    }

    public int Size { get; }
    private readonly Use[] _cells;

    public LandUseGrid(int size)
    {
        Size = size;
        _cells = new Use[size * size];
    }

    public Use Get(int lx, int ly) => _cells[ly * Size + lx];
    public void Set(int lx, int ly, Use u) => _cells[ly * Size + lx] = u;
    public bool InBounds(int lx, int ly) =>
        lx >= 0 && ly >= 0 && lx < Size && ly < Size;

    public int CountWhere(Use u)
    {
        int n = 0;
        for (int i = 0; i < _cells.Length; i++) if (_cells[i] == u) n++;
        return n;
    }
}
