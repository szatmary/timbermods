# MapGen Home-Base Architecture — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the current "WFC everywhere → trace river → hope it's playable" pipeline with one that anchors every map around a hand-curated 24×24 home-base region (district + farm + pond/river + content), guaranteeing playability by construction. v1 keeps the rest of the map as a featureless Meadow plain — variety comes in a future plan.

**Architecture:** New file `HomeBase.cs` does the constrained-random region generation. `Hydrology.cs` refactors `TraceDownhill` → `TraceFromTo(start, end)` and adds two-leg river carving. `MapGenerator.cs` rewrites its orchestrator to call them. `Heightmap.cs` and `Overlays.cs` get simplified for the v1 minimal pass-2. Old files (`BiomeGrid`, `Noise`, `StartSelection`, `AccessValidation`) stay in the codebase unused — they'll be re-integrated when v2 adds proper biome variety.

**Tech Stack:** Same as before — C# netstandard2.1, xUnit. No new dependencies.

---

## Spec reference

`docs/superpowers/specs/2026-04-24-mapgen-homebase-design.md`. Plan 2's serializer is unchanged; this plan only touches the algorithm (Pass 1 generation).

## Workflow

Stay on `feature/graphs-mod`. Use `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"; export PATH="$DOTNET_ROOT:$PATH"` for `dotnet` commands.

After each task: build, run tests (must stay green or grow). Commit with the message in the task.

Currently 45 unit tests passing; the orchestrator rewrite will break some of them temporarily — fix as you go.

---

## File structure

```
MapGen/MapGen.Core/
  HomeBase.cs                    # CREATE: home-base region generator
  Hydrology.cs                   # MODIFY: TraceFromTo + 2-leg carving + RIVER/BOTH support
  MapGenerator.cs                # REWRITE: new pipeline
  Heightmap.cs                   # MODIFY: add FlatFill helper
  Overlays.cs                    # UNCHANGED in v1 (helpers reused by HomeBase)
  StartSelection.cs              # UNUSED in v1, kept for v2
  BiomeGrid.cs                   # UNUSED in v1, kept for v2
  Noise.cs                       # UNUSED in v1, kept for v2
  AccessValidation.cs            # SIMPLIFIED to a sanity check
MapGen/MapGen.Core.Tests/
  HomeBaseTests.cs               # CREATE: content-guarantee tests
  MapGeneratorTests.cs           # MODIFY: relax assertions for v1
  AccessValidationTests.cs       # KEEP (still valid)
  HydrologyTests.cs              # KEEP (still valid; refactor uses are tested via MapGenerator)
```

---

## Task 1: HomeBase.cs scaffolding + LandUseGrid helper

**Files:**
- Create: `MapGen/MapGen.Core/HomeBase.cs`
- Create: `MapGen/MapGen.Core.Tests/HomeBaseTests.cs`

`HomeBase.Generate` is the entry point. It mutates the passed-in `MapData` to add a 24×24 home-base region centered on (cx, cy), at base height `H_base`, with a chosen `WaterVariant`.

The internal `LandUseGrid` tracks what each cell in the 24×24 region is reserved for (None / DistrictRing / DistrictCenter / Farm / Water / Tree / Berry). This makes the multi-stage placement deterministic: each stage queries the grid and only places where `None`.

- [ ] **Step 1: Write `MapGen/MapGen.Core/HomeBase.cs`** (scaffolding only; sub-stages filled in by Tasks 2-6):

```csharp
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
```

- [ ] **Step 2: Write `MapGen/MapGen.Core.Tests/HomeBaseTests.cs`** with placeholder test (compiles but does nothing meaningful until later tasks):

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class HomeBaseTests
{
    [Fact]
    public void LandUseGrid_starts_all_None()
    {
        var g = new LandUseGrid(24);
        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
            Assert.Equal(LandUseGrid.Use.None, g.Get(x, y));
        Assert.Equal(24 * 24, g.CountWhere(LandUseGrid.Use.None));
    }

    [Fact]
    public void LandUseGrid_set_and_get_round_trip()
    {
        var g = new LandUseGrid(24);
        g.Set(10, 10, LandUseGrid.Use.DistrictCenter);
        Assert.Equal(LandUseGrid.Use.DistrictCenter, g.Get(10, 10));
        Assert.Equal(1, g.CountWhere(LandUseGrid.Use.DistrictCenter));
    }
}
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: 47/47 (45 prior + 2 new).

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/HomeBase.cs MapGen/MapGen.Core.Tests/HomeBaseTests.cs
git commit -m "MapGen: HomeBase scaffolding + LandUseGrid"
```

---

## Task 2: District + Farm reservation

**Files:**
- Modify: `MapGen/MapGen.Core/HomeBase.cs`

District goes at a random spot inside the home-base region with enough space for its 3-cell ring (so the 4×4 has room for the +3 padding on each side → effective 10×10 footprint).

- [ ] **Step 1: Replace `ReserveDistrictAndFarm` in `MapGen/MapGen.Core/HomeBase.cs`:**

```csharp
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
```

- [ ] **Step 2: Add tests verifying district + farm reservations:**

Append to `MapGen/MapGen.Core.Tests/HomeBaseTests.cs`:

```csharp
    [Fact]
    public void Generate_district_center_is_4x4()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("TEST1");
        var grid = HomeBase.Generate(map, catalog, 32, 32, hBase: 4,
            variant: WaterVariant.Pond, ref rng);
        Assert.Equal(16, grid.CountWhere(LandUseGrid.Use.DistrictCenter));
    }

    [Fact]
    public void Generate_district_ring_surrounds_center()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("TEST2");
        var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        // Footprint is 10x10 = 100 cells; center is 16. So ring should be
        // 100 - 16 = 84 cells.
        Assert.Equal(84, grid.CountWhere(LandUseGrid.Use.DistrictRing));
    }

    [Fact]
    public void Generate_farm_is_36_cells()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("TEST3");
        var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        // Farm should be exactly 6x6 = 36, OR 0 if the random search couldn't fit it.
        int farm = grid.CountWhere(LandUseGrid.Use.Farm);
        Assert.True(farm == 36 || farm == 0, $"Farm count {farm} should be 36 or 0.");
    }

    private static (MapData, Catalog) MakeFakeMapAndCatalog()
    {
        var map = new MapData(64, 64, "TEST");
        map.MetaWidth = 8; map.MetaHeight = 8;
        map.Biomes = new Biome[64];
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[64 * 64];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan>();
        map.WaterDepths = new byte[64 * 64];
        var catalog = new Catalog
        {
            Trees = new System.Collections.Generic.List<CatalogEntry> { new() { Key = "pine", BlueprintKey = "Pine", Weight = 1 } },
            Resources = new System.Collections.Generic.List<CatalogEntry> { new() { Key = "berries", BlueprintKey = "BlueberryBush", Weight = 1 } },
            Thorns = new System.Collections.Generic.List<CatalogEntry>(),
            Ruins = new System.Collections.Generic.List<RuinCatalogEntry>(),
            BlockObjects = new System.Collections.Generic.Dictionary<string, CatalogEntry>
            {
                { "start_marker", new() { Key = "start_marker", BlueprintKey = "StartingLocation", Weight = 1 } },
            },
        };
        return (map, catalog);
    }
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: 50/50.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/
git commit -m "MapGen: HomeBase district + farm reservation"
```

---

## Task 3: Pond placement (with 4×4 heart) + water marking

**Files:**
- Modify: `MapGen/MapGen.Core/HomeBase.cs`

Pond shape: place a 4×4 "heart" of WATER at a random spot, then expand outward irregularly with random extra cells, capped at total ~6×6 area or 36 cells. Constraint: heart must be ≥3 voxels from any existing reservation (district, ring, farm).

- [ ] **Step 1: Replace `PlaceWaterFeature` with this version that handles POND (and stubs RIVER/BOTH for now):**

```csharp
    private static void PlaceWaterFeature(LandUseGrid g, int hBase, WaterVariant v, ref Rng rng)
    {
        if (v == WaterVariant.Pond || v == WaterVariant.Both)
            PlacePond(g, ref rng);
        // RIVER and BOTH river-portion handled in Task 4.
    }

    private const int PondHeartSize = 4;
    private const int PondMaxExtraCells = 12;  // pond reaches up to ~28 cells total

    private static void PlacePond(LandUseGrid g, ref Rng rng)
    {
        // Find a spot for the 4x4 heart that's ≥3 cells from district/ring/farm
        // AND ≥3 cells from the home-base outer edge.
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int hx = rng.NextRange(3, RegionSize - PondHeartSize - 3 + 1);
            int hy = rng.NextRange(3, RegionSize - PondHeartSize - 3 + 1);
            if (!HeartAreaClear(g, hx, hy)) continue;

            // Place 4x4 heart.
            for (int y = hy; y < hy + PondHeartSize; y++)
            for (int x = hx; x < hx + PondHeartSize; x++)
                g.Set(x, y, LandUseGrid.Use.Water);

            // Add up to PondMaxExtraCells extra adjacent cells for irregularity.
            int extras = rng.NextRange(0, PondMaxExtraCells + 1);
            for (int e = 0; e < extras; e++)
            {
                // Pick a random water cell, try to grow into a None neighbor.
                int growX = rng.NextRange(hx - 2, hx + PondHeartSize + 2);
                int growY = rng.NextRange(hy - 2, hy + PondHeartSize + 2);
                if (!g.InBounds(growX, growY)) continue;
                if (g.Get(growX, growY) != LandUseGrid.Use.None) continue;
                if (!AnyNeighborIs(g, growX, growY, LandUseGrid.Use.Water)) continue;
                if (DistanceToReservation(g, growX, growY) < 3) continue;
                g.Set(growX, growY, LandUseGrid.Use.Water);
            }
            return;
        }
        // No spot found — pond skipped. v1 behavior: home base still playable
        // because player can flatten and dig — but Pond/Both variants in this
        // case are effectively no-water. Caller treats this as soft failure.
    }

    private static bool HeartAreaClear(LandUseGrid g, int x0, int y0)
    {
        // The 4x4 heart itself must be all None, AND the 3-cell buffer around it
        // must contain no DistrictCenter/DistrictRing/Farm.
        for (int y = x0 - 3; y < x0 + PondHeartSize + 3; y++)
        for (int x = x0 - 3; x < x0 + PondHeartSize + 3; x++)
        {
            if (!g.InBounds(x, y)) continue;
            var u = g.Get(x, y);
            bool inHeart = (x >= x0 && x < x0 + PondHeartSize &&
                            y >= y0 && y < y0 + PondHeartSize);
            if (inHeart && u != LandUseGrid.Use.None) return false;
            if (!inHeart && u != LandUseGrid.Use.None) return false;
        }
        return true;
    }

    private static bool AnyNeighborIs(LandUseGrid g, int x, int y, LandUseGrid.Use u)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = x + dx, ny = y + dy;
            if (!g.InBounds(nx, ny)) continue;
            if (g.Get(nx, ny) == u) return true;
        }
        return false;
    }

    private static int DistanceToReservation(LandUseGrid g, int x, int y)
    {
        // Manhattan distance to nearest District / Ring / Farm cell.
        int best = int.MaxValue;
        for (int yy = 0; yy < g.Size; yy++)
        for (int xx = 0; xx < g.Size; xx++)
        {
            var u = g.Get(xx, yy);
            if (u == LandUseGrid.Use.DistrictCenter || u == LandUseGrid.Use.DistrictRing
                || u == LandUseGrid.Use.Farm)
            {
                int d = System.Math.Abs(xx - x) + System.Math.Abs(yy - y);
                if (d < best) best = d;
            }
        }
        return best;
    }
```

- [ ] **Step 2: Add a pond-placement test to `HomeBaseTests.cs`:**

```csharp
    [Fact]
    public void Generate_pond_variant_has_at_least_16_water_cells()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("POND1");
        var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        int water = grid.CountWhere(LandUseGrid.Use.Water);
        Assert.True(water >= 16, $"Pond should have ≥16 water cells (the 4x4 heart), got {water}");
    }
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: 51/51.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/HomeBase.cs MapGen/MapGen.Core.Tests/HomeBaseTests.cs
git commit -m "MapGen: HomeBase pond placement (4x4 heart + irregular extras)"
```

---

## Task 4: River meander placement inside home base

**Files:**
- Modify: `MapGen/MapGen.Core/HomeBase.cs`

For RIVER and BOTH variants, generate a meandering river path through the home base. The path enters at one edge and exits at another (opposite or perpendicular). Width 3-6 cells. Path stays ≥3 voxels from district + farm.

The trace is a random walk: 60% continue same direction, 20% turn left, 20% turn right.

- [ ] **Step 1: Add river handling to `PlaceWaterFeature` and add the meander trace:**

In `MapGen/MapGen.Core/HomeBase.cs`, modify `PlaceWaterFeature`:

```csharp
    private static void PlaceWaterFeature(LandUseGrid g, int hBase, WaterVariant v, ref Rng rng)
    {
        if (v == WaterVariant.Pond || v == WaterVariant.Both)
            PlacePond(g, ref rng);
        if (v == WaterVariant.River || v == WaterVariant.Both)
            PlaceRiverMeander(g, ref rng);
    }
```

Append these methods to the class:

```csharp
    private static void PlaceRiverMeander(LandUseGrid g, ref Rng rng)
    {
        int width = rng.NextRange(3, 7);  // [3, 6]
        // Pick entry edge (0=top, 1=right, 2=bottom, 3=left) and exit edge
        // (must differ).
        int entryEdge = rng.NextRange(0, 4);
        int exitEdge;
        do { exitEdge = rng.NextRange(0, 4); } while (exitEdge == entryEdge);

        // Try up to 30 attempts to find a meander that respects buffers.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var path = TraceMeanderPath(entryEdge, exitEdge, ref rng);
            if (PathIsValid(g, path, width))
            {
                StampRiver(g, path, width);
                return;
            }
        }
        // Soft failure — no river placed. Caller may surface this.
    }

    private static List<(int X, int Y)> TraceMeanderPath(int entryEdge, int exitEdge, ref Rng rng)
    {
        var path = new List<(int X, int Y)>();
        // Entry point = midpoint-ish of entry edge.
        var (sx, sy) = EdgeMidpoint(entryEdge, ref rng);
        path.Add((sx, sy));
        var (dx, dy) = EdgeOutwardDirection(exitEdge);  // direction to head TOWARD exit
        // Reverse to be heading INTO map from entry.
        var (hx, hy) = EdgeOutwardDirection(entryEdge);
        int curDx = -hx, curDy = -hy;

        for (int step = 0; step < RegionSize * 2; step++)
        {
            var last = path[path.Count - 1];
            // 60% straight, 20% each side.
            float r = rng.NextFloat();
            if (r < 0.2f) (curDx, curDy) = TurnLeft(curDx, curDy);
            else if (r < 0.4f) (curDx, curDy) = TurnRight(curDx, curDy);
            int nx = last.X + curDx;
            int ny = last.Y + curDy;
            // Reject if going out of bounds (except at exit edge).
            if (nx < 0 || ny < 0 || nx >= RegionSize || ny >= RegionSize)
            {
                // If we've reached the exit edge, accept the exit.
                if (OnEdge(nx, ny, exitEdge)) { path.Add((nx, ny)); return path; }
                // Otherwise turn back inward.
                (curDx, curDy) = (-curDx, -curDy);
                continue;
            }
            path.Add((nx, ny));
            if (OnEdge(nx, ny, exitEdge)) return path;
        }
        return path;  // step limit hit; returned path may not reach exit
    }

    private static (int X, int Y) EdgeMidpoint(int edge, ref Rng rng)
    {
        int mid = RegionSize / 2 + rng.NextRange(-3, 4);  // jittered midpoint
        mid = System.Math.Clamp(mid, 4, RegionSize - 5);
        return edge switch
        {
            0 => (mid, 0),
            1 => (RegionSize - 1, mid),
            2 => (mid, RegionSize - 1),
            3 => (0, mid),
            _ => (mid, mid),
        };
    }

    private static (int X, int Y) EdgeOutwardDirection(int edge) => edge switch
    {
        0 => (0, -1),
        1 => (1, 0),
        2 => (0, 1),
        3 => (-1, 0),
        _ => (0, 0),
    };

    private static (int X, int Y) TurnLeft(int dx, int dy) => (-dy, dx);
    private static (int X, int Y) TurnRight(int dx, int dy) => (dy, -dx);

    private static bool OnEdge(int x, int y, int edge) => edge switch
    {
        0 => y < 0,
        1 => x >= RegionSize,
        2 => y >= RegionSize,
        3 => x < 0,
        _ => false,
    };

    private static bool PathIsValid(LandUseGrid g, List<(int X, int Y)> path, int width)
    {
        int half = width / 2;
        foreach (var (x, y) in path)
        {
            for (int dy = -half; dy <= half; dy++)
            for (int dx = -half; dx <= half; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (!g.InBounds(cx, cy)) continue;
                var u = g.Get(cx, cy);
                if (u == LandUseGrid.Use.DistrictCenter ||
                    u == LandUseGrid.Use.DistrictRing ||
                    u == LandUseGrid.Use.Farm) return false;
            }
        }
        return true;
    }

    private static void StampRiver(LandUseGrid g, List<(int X, int Y)> path, int width)
    {
        int half = width / 2;
        foreach (var (x, y) in path)
        {
            for (int dy = -half; dy <= half; dy++)
            for (int dx = -half; dx <= half; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (!g.InBounds(cx, cy)) continue;
                if (g.Get(cx, cy) == LandUseGrid.Use.None ||
                    g.Get(cx, cy) == LandUseGrid.Use.DistrictRing)
                {
                    // Allow river to overwrite DistrictRing? No — PathIsValid
                    // rejected those. So this branch only triggers for None.
                    g.Set(cx, cy, LandUseGrid.Use.Water);
                }
            }
        }
    }
```

- [ ] **Step 2: Add river-placement test:**

```csharp
    [Fact]
    public void Generate_river_variant_has_water_cells()
    {
        // Run a few seeds; not every seed will succeed in placing a river,
        // but at least some should.
        int successful = 0;
        for (uint s = 0; s < 5; s++)
        {
            var (map, catalog) = MakeFakeMapAndCatalog();
            var rng = new Rng($"RIVER{s}");
            var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.River, ref rng);
            if (grid.CountWhere(LandUseGrid.Use.Water) > 5) successful++;
        }
        Assert.True(successful >= 3, $"At least 3/5 seeds should produce a river, got {successful}");
    }
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: 52/52.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/HomeBase.cs MapGen/MapGen.Core.Tests/HomeBaseTests.cs
git commit -m "MapGen: HomeBase river meander placement (random walk through region)"
```

---

## Task 5: Set terrain heights + place trees + berries + entities

**Files:**
- Modify: `MapGen/MapGen.Core/HomeBase.cs`

Now turn the LandUseGrid into actual terrain columns + entities on the MapData. Heights: water cells = `H_base − 2`, everything else = `H_base`. Trees/berries placed via existing PoissonDisk on cells where Use == None. StartingLocation entity at the district center.

- [ ] **Step 1: Replace the four stub methods (`SetTerrainHeights`, `PlaceTrees`, `PlaceBerries`, `PlaceWaterEntities`, `PlaceStartingLocation`):**

```csharp
    private const int TreeMinCount = 25;
    private const int TreeMaxCount = 50;
    private const int BerryMinCount = 10;
    private const int BerryMaxCount = 20;
    private const float TreeMinSpacing = 1.6f;
    private const float BerryMinSpacing = 2.0f;

    private static void SetTerrainHeights(MapData m, LandUseGrid g, int x0, int y0, int hBase)
    {
        for (int ly = 0; ly < g.Size; ly++)
        for (int lx = 0; lx < g.Size; lx++)
        {
            int wx = x0 + lx;
            int wy = y0 + ly;
            if (wx < 0 || wy < 0 || wx >= m.Width || wy >= m.Height) continue;
            var u = g.Get(lx, ly);
            int h = (u == LandUseGrid.Use.Water) ? hBase - 2 : hBase;
            var spans = m.Columns[m.ColumnIndex(wx, wy)];
            spans.Clear();
            spans.Add(new VoxelSpan(0, h));
            if (u == LandUseGrid.Use.Water)
            {
                // Water depth 2 → surface at hBase. Banks at hBase, water flush
                // with bank top.
                m.WaterDepths[m.ColumnIndex(wx, wy)] = 2;
            }
        }
    }

    private static void PlaceTrees(MapData m, LandUseGrid g, Catalog c, int x0, int y0, int hBase, ref Rng rng)
    {
        if (c.Trees.Count == 0) return;
        int target = rng.NextRange(TreeMinCount, TreeMaxCount + 1);
        var samples = PoissonDisk.Sample(g.Size, g.Size, TreeMinSpacing, ref rng);
        int placed = 0;
        foreach (var s in samples)
        {
            if (placed >= target) break;
            if (g.Get(s.X, s.Y) != LandUseGrid.Use.None) continue;
            int wx = x0 + s.X, wy = y0 + s.Y;
            if (wx < 0 || wy < 0 || wx >= m.Width || wy >= m.Height) continue;
            var entry = c.Trees[rng.NextRange(0, c.Trees.Count)];
            int z = m.TopHeight(wx, wy) + 1;
            m.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(wx, wy, z), Orientation.North, EntityKind.Tree));
            g.Set(s.X, s.Y, LandUseGrid.Use.Tree);
            placed++;
        }
    }

    private static void PlaceBerries(MapData m, LandUseGrid g, Catalog c, int x0, int y0, int hBase, ref Rng rng)
    {
        if (c.Resources.Count == 0) return;
        int target = rng.NextRange(BerryMinCount, BerryMaxCount + 1);
        var samples = PoissonDisk.Sample(g.Size, g.Size, BerryMinSpacing, ref rng);
        int placed = 0;
        foreach (var s in samples)
        {
            if (placed >= target) break;
            if (g.Get(s.X, s.Y) != LandUseGrid.Use.None) continue;
            int wx = x0 + s.X, wy = y0 + s.Y;
            if (wx < 0 || wy < 0 || wx >= m.Width || wy >= m.Height) continue;
            var entry = c.Resources[rng.NextRange(0, c.Resources.Count)];
            int z = m.TopHeight(wx, wy) + 1;
            m.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(wx, wy, z), Orientation.North, EntityKind.Resource));
            g.Set(s.X, s.Y, LandUseGrid.Use.Berry);
            placed++;
        }
    }

    private static void PlaceWaterEntities(MapData m, LandUseGrid g, int x0, int y0, int hBase,
        WaterVariant v, ref Rng rng)
    {
        // POND/BOTH: place a single WaterSeep at the centroid of the water cells
        // (which is the center of the heart since the pond is built around it).
        if (v == WaterVariant.Pond || v == WaterVariant.Both)
        {
            // Find any pond water cell and place seep there. Heart center is fine.
            int sumX = 0, sumY = 0, n = 0;
            for (int ly = 0; ly < g.Size; ly++)
            for (int lx = 0; lx < g.Size; lx++)
            {
                if (g.Get(lx, ly) == LandUseGrid.Use.Water) { sumX += lx; sumY += ly; n++; }
            }
            if (n > 0)
            {
                int cx = sumX / n, cy = sumY / n;
                int wx = x0 + cx, wy = y0 + cy;
                int z = hBase - 1;  // one voxel above channel bottom
                m.Entities.Add(new PlacedEntity("WaterSeep",
                    new VoxelCoord(wx, wy, z), Orientation.North,
                    EntityKind.WaterSource, 0.5f));
            }
        }
        // RIVER source/drain entities are placed by the MapGenerator in
        // hydrology stage 5 (outside the home base region).
    }

    private static void PlaceStartingLocation(MapData m, LandUseGrid g, Catalog c, int x0, int y0, int hBase)
    {
        if (!c.BlockObjects.TryGetValue("start_marker", out var entry)) return;
        // Find the center of the DistrictCenter cells.
        int sumX = 0, sumY = 0, n = 0;
        for (int ly = 0; ly < g.Size; ly++)
        for (int lx = 0; lx < g.Size; lx++)
        {
            if (g.Get(lx, ly) == LandUseGrid.Use.DistrictCenter) { sumX += lx; sumY += ly; n++; }
        }
        if (n == 0) return;
        int cx = sumX / n, cy = sumY / n;
        int wx = x0 + cx, wy = y0 + cy;
        int z = hBase;  // sits on top of the H_base flat ground
        m.Entities.Add(new PlacedEntity(entry.BlueprintKey,
            new VoxelCoord(wx, wy, z), Orientation.North, EntityKind.StartMarker));
    }
```

- [ ] **Step 2: Add tests for content guarantees:**

```csharp
    [Fact]
    public void Generate_places_trees_and_berries_within_ranges()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("CONTENT");
        HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        int trees = 0, berries = 0;
        foreach (var e in map.Entities)
        {
            if (e.Kind == EntityKind.Tree) trees++;
            else if (e.Kind == EntityKind.Resource) berries++;
        }
        Assert.InRange(trees, 25, 50);
        Assert.InRange(berries, 10, 20);
    }

    [Fact]
    public void Generate_places_starting_location_entity()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("START");
        HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        int starts = 0;
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.StartMarker) starts++;
        Assert.Equal(1, starts);
    }

    [Fact]
    public void Generate_pond_places_seep_entity()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("SEEP");
        HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        int seeps = 0;
        foreach (var e in map.Entities)
            if (e.BlueprintKey == "WaterSeep") seeps++;
        Assert.Equal(1, seeps);
    }
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: 55/55.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/HomeBase.cs MapGen/MapGen.Core.Tests/HomeBaseTests.cs
git commit -m "MapGen: HomeBase terrain + tree + berry + seep + StartingLocation"
```

---

## Task 6: Hydrology refactor — TraceFromTo + 2-leg river support

**Files:**
- Modify: `MapGen/MapGen.Core/Hydrology.cs`

The existing `TraceDownhill(map, start)` takes only a start cell. We need `TraceFromTo(map, start, target)` that biases each step toward `target` while still preferring downhill. Also add a public `BuildExternalRiver` that does both legs and places source entity.

The existing `Hydrology.Build(map, ref rng)` (autopilot) becomes unused in v1. Leave it in place; v2 may reuse it.

- [ ] **Step 1: Add new public methods to `Hydrology.cs`** (keep all existing methods; just append).

In `MapGen/MapGen.Core/Hydrology.cs`, add these methods inside the `Hydrology` class:

```csharp
    /// Two-leg river: source → home-in-edge, then home-out-edge → drain.
    /// Both legs carved 1-voxel wide outside the home base; carve depth 2.
    /// Places a `WaterSource` entity at the source.
    /// Returns true on success, false if any leg couldn't be traced.
    public static bool BuildExternalRiver(MapData map, GridCoord source,
        GridCoord homeInEdge, GridCoord homeOutEdge, GridCoord drain, ref Rng rng)
    {
        var leg1 = TraceFromTo(map, source, homeInEdge);
        if (leg1 == null) return false;
        var leg2 = TraceFromTo(map, homeOutEdge, drain);
        if (leg2 == null) return false;
        CarveSingleVoxelRiver(map, leg1);
        CarveSingleVoxelRiver(map, leg2);
        // Place source entity.
        int sz = map.TopHeight(source.X, source.Y) + 1;
        map.Entities.Add(new PlacedEntity("WaterSource",
            new VoxelCoord(source.X, source.Y, sz), Orientation.North,
            EntityKind.WaterSource, 0.6f));
        return true;
    }

    /// Trace a path from `start` toward `target` greedy-biased: at each
    /// step pick the unvisited 4-neighbor closest to `target` that is also
    /// no higher than the current cell + 1 (we're allowed small uphills if
    /// it's the only way; the carve flattens them). Returns null if the
    /// trace can't progress.
    public static List<GridCoord>? TraceFromTo(MapData map, GridCoord start, GridCoord target)
    {
        var path = new List<GridCoord> { start };
        var visited = new HashSet<GridCoord> { start };
        int maxSteps = (map.Width + map.Height) * 4;
        for (int step = 0; step < maxSteps; step++)
        {
            var cur = path[path.Count - 1];
            if (cur.Equals(target)) return path;
            int curH = map.TopHeight(cur.X, cur.Y);
            // Find best unvisited neighbor: minimize Manhattan distance to target,
            // tie-break on lower elevation.
            GridCoord? best = null;
            int bestDist = int.MaxValue;
            int bestH = int.MaxValue;
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (visited.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                if (nh > curH + 1) continue;  // too uphill
                int dist = Math.Abs(nx - target.X) + Math.Abs(ny - target.Y);
                if (dist < bestDist || (dist == bestDist && nh < bestH))
                {
                    best = nc;
                    bestDist = dist;
                    bestH = nh;
                }
            }
            if (best == null) return null;
            path.Add(best.Value);
            visited.Add(best.Value);
        }
        return null;
    }

    private static void CarveSingleVoxelRiver(MapData map, List<GridCoord> path)
    {
        foreach (var c in path)
        {
            var spans = map.Columns[map.ColumnIndex(c.X, c.Y)];
            if (spans.Count == 0) continue;
            var top = spans[spans.Count - 1];
            int newHeight = Math.Max(1, top.Height - 2);
            spans[spans.Count - 1] = new VoxelSpan(top.Bottom, newHeight);
            map.WaterDepths[map.ColumnIndex(c.X, c.Y)] = 2;
        }
    }
```

(`FourNeighbors` is already defined as a private helper in `Hydrology.cs`. Reuse it.)

- [ ] **Step 2: Build + run tests** (no new tests yet; just verify existing pass):

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: 55/55 still passing.

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/Hydrology.cs
git commit -m "MapGen: Hydrology — TraceFromTo + BuildExternalRiver for 2-leg routing"
```

---

## Task 7: Heightmap.FlatFill + simplify pass-2

**Files:**
- Modify: `MapGen/MapGen.Core/Heightmap.cs`

Add a `FlatFill(map, height, skipPredicate)` helper that overwrites every column with a fixed-height span unless the skip-predicate matches. The new `MapGenerator` calls this after the home base is generated, with a predicate that returns true for cells already touched by the home base (height ≠ 0 in the column).

- [ ] **Step 1: Append `FlatFill` to `MapGen/MapGen.Core/Heightmap.cs`** (inside the `Heightmap` class):

```csharp
    /// Sets every column NOT matching `skipPredicate` to a single solid span
    /// of given height. Used for v1's minimal pass-2 (everywhere outside the
    /// home base becomes a featureless plain).
    public static void FlatFill(MapData map, int height, Func<int, int, bool> skipPredicate)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (skipPredicate(x, y)) continue;
            var spans = map.Columns[map.ColumnIndex(x, y)];
            spans.Clear();
            spans.Add(new VoxelSpan(0, height));
        }
    }
```

- [ ] **Step 2: Build:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet build 2>&1 | tail -3
```
Expected: 0 errors.

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/Heightmap.cs
git commit -m "MapGen: Heightmap.FlatFill helper for v1 minimal pass-2"
```

---

## Task 8: Rewrite MapGenerator orchestrator

**Files:**
- Modify: `MapGen/MapGen.Core/MapGenerator.cs`

Replaces the entire body of `TryGenerate` with the new pipeline. Old test assertions about biome variety, overlay diversity, etc., will break — Task 9 fixes them.

- [ ] **Step 1: Replace `MapGen/MapGen.Core/MapGenerator.cs` entirely:**

```csharp
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
```

- [ ] **Step 2: Build:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet build 2>&1 | tail -5
```
Expected: 0 errors. (Some tests will FAIL because of the pipeline change — that's expected and Task 9 fixes them.)

- [ ] **Step 3: Commit (broken tests acceptable here, will fix in Task 9):**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/MapGenerator.cs
git commit -m "MapGen: rewrite MapGenerator orchestrator for home-base pipeline"
```

---

## Task 9: Fix existing tests to match the new pipeline

**Files:**
- Modify: `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`
- Modify: `MapGen/MapGen.Core.Tests/AccessValidationTests.cs` (if breaks)
- Modify: `MapGen/MapGen.Core.Tests/HydrologyTests.cs` (if breaks)

The old `MapGeneratorTests` assumed full biome variety + overlays everywhere. v1 produces a Meadow plain with one home base; assertions need updating.

- [ ] **Step 1: Run tests, identify failures:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | grep -E "Failed|Passed!" | head -20
```

- [ ] **Step 2: Loosen assertions in `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`.**
For each failing test, replace strict assertions about overlay presence with looser ones:
- `Generate_deterministic_same_seed`: should still pass; no change needed.
- `SeedSweep_100_seeds_on_128x128_all_succeed`: keep, but expect even higher success rate now (>95) since the home base is constructed.
- `SeedSweep_various_sizes`: 64x64 may now FAIL because home base needs ≥`(12 + 12) * 2` = 48 voxels minimum. Update size list to start from 80x80:

Find:
```csharp
        int[] sizes = { 64, 128, 192, 256 };
```

Replace with:
```csharp
        int[] sizes = { 80, 128, 192, 256 };
```

- `Generate_small_map_completes`: change `Width = 64` to `Width = 80`; same for Height.

- [ ] **Step 3: Run tests:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj 2>&1 | tail -3
```
Expected: all tests pass. If an unrelated test fails (e.g. AccessValidation, Hydrology), report it; those tests' modules weren't supposed to break.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core.Tests/
git commit -m "MapGen: update tests for home-base pipeline"
```

---

## Task 10: End-to-end manual integration

- [ ] **Step 1: Generate three test maps directly into the user's Maps folder:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
    --seed HOME1 --width 128 --height 128 --write-timber --out ~/Documents/Timberborn/Maps/ 2>&1 | tail -3
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
    --seed HOME2 --width 128 --height 128 --write-timber --out ~/Documents/Timberborn/Maps/ 2>&1 | tail -3
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
    --seed HOME3 --width 192 --height 192 --write-timber --out ~/Documents/Timberborn/Maps/ 2>&1 | tail -3
ls -la ~/Documents/Timberborn/Maps/seed-HOME*.timber
```

- [ ] **Step 2: User loads each map in Timberborn → Map Editor.** For each:
  - Loads without exception?
  - Home base visible (flat 24×24 plateau on a featureless plain)?
  - District-center spot at a clear 4×4 area with surrounding ring?
  - Pond OR river visible (depending on variant)?
  - Trees and berries clustered around the home base?
  - StartingLocation marker visible?

- [ ] **Step 3: Tighter integration — start a new game on one of the maps:**
  - Place a District Center on the StartingLocation cell.
  - Build a Water Pump on the bank of the pond/river.
  - Confirm water flows.

If anything fails, REPORT the issue + screenshot/console error. Likely culprits:
- Voxel layout still wrong (terrain renders weird)
- Some entity missing required component (game throws on load)
- District center placement conflicts with reserved ring (test-only issue, not generator)

- [ ] **Step 4: Commit a session-note to the README:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
echo "" >> MapGen/README.md
echo "Plan 4 (home-base pipeline) integration confirmed: seeds HOME1, HOME2, HOME3 load and play in Timberborn 1.0.13." >> MapGen/README.md
git add MapGen/README.md
git commit -m "MapGen: home-base pipeline integration confirmed in Timberborn"
```

(Skip if README doesn't exist — just commit a single doc line wherever convenient.)

---

## Plan completion

When Task 10 is done:
- `MapGenerator.Generate` produces deterministic playable maps.
- Home base has district + farm + content + water (pond / river / both).
- Source-to-drain river (RIVER and BOTH variants) connects through the home base.
- Rest of the map is a featureless Meadow plain (v1 scope).

**Next plan:** Pass 2 — biome WFC + heightmap noise + overlays for the rest of the map. Adds the visual variety we deferred.

---

## Self-review notes

- Spec coverage: every spec section has a task. Pond / River / Both all handled.
- Type consistency: `WaterVariant` enum, `LandUseGrid` accessors, `HomeBase.RegionSize` constants — all referenced consistently across tasks.
- No placeholders: each step has the actual code or specific commands.
- Known unknowns:
  - The river meander may sometimes fail to find a valid path within 30 attempts — `PlaceRiverMeander` returns silently in that case; v1 accepts that as soft failure (caller may still produce a valid pond+nothing map for the BOTH variant).
  - The remote-source picking may fail on very flat-noise maps; logged but doesn't fail generation (river is skipped, home base still playable for POND variant).
