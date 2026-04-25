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

    private static void ReserveDistrictAndFarm(LandUseGrid g, ref Rng rng)
    {
        // District 4x4 + 3-cell ring on each side → 10x10 footprint.
        // Pick a top-left for the FOOTPRINT such that it stays inside
        // the 24x24 region.
        int footprint = DistrictSize + 2 * DistrictRingPadding;  // 10
        int maxStart = RegionSize - footprint;
        int dx = rng.NextRange(0, maxStart + 1);  // top-left of FOOTPRINT
        int dy = rng.NextRange(0, maxStart + 1);
        // District 4x4 sits at the center of the footprint.
        int dCx = dx + DistrictRingPadding;
        int dCy = dy + DistrictRingPadding;

        // Mark 3-ring around district as DistrictRing.
        for (int y = dy; y < dy + footprint; y++)
        for (int x = dx; x < dx + footprint; x++)
            g.Set(x, y, LandUseGrid.Use.DistrictRing);

        // Overwrite the inner 4x4 as DistrictCenter.
        for (int y = dCy; y < dCy + DistrictSize; y++)
        for (int x = dCx; x < dCx + DistrictSize; x++)
            g.Set(x, y, LandUseGrid.Use.DistrictCenter);

        // Farm 6x6 placed at a random spot that doesn't overlap district
        // footprint (10x10) AND has 3-voxel buffer to water (no water yet
        // — placing water happens next, which respects existing reservations).
        for (int attempt = 0; attempt < 100; attempt++)
        {
            int fx = rng.NextRange(0, RegionSize - FarmSize + 1);
            int fy = rng.NextRange(0, RegionSize - FarmSize + 1);
            if (RegionAnyOverlap(g, fx, fy, FarmSize, LandUseGrid.Use.None) ==
                FarmSize * FarmSize)
            {
                for (int y = fy; y < fy + FarmSize; y++)
                for (int x = fx; x < fx + FarmSize; x++)
                    g.Set(x, y, LandUseGrid.Use.Farm);
                return;
            }
        }
        // Fallback: the farm sometimes can't fit cleanly. For v1 just
        // accept that. The home base remains playable without a farm
        // reserved (player can flatten ground later).
    }

    private static int RegionAnyOverlap(LandUseGrid g, int x0, int y0, int size,
        LandUseGrid.Use mustEqual)
    {
        int matching = 0;
        for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
            if (g.Get(x, y) == mustEqual) matching++;
        return matching;
    }

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
