# MapGen.Core — Algorithm + Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a pure-C# library that generates Timberborn map data deterministically from `(seed, width, height)`, plus a CLI preview tool that emits a PNG visualization of each generated map.

**Architecture:** Two projects — `MapGen.Core` (netstandard2.1, zero external deps, future-consumable by a Timberborn mod) and `MapGen.Preview` (net8.0, uses SkiaSharp for rendering). Plus `MapGen.Core.Tests` (net8.0, xUnit). A `MapGenerator` orchestrator runs a pipeline of stages — biome grid → start label → heightmap → water → overlays → validation — each writing to a shared `MapData` value object that the preview tool (and later plans) serialize.

**Tech Stack:** C# (netstandard2.1 for Core, net8.0 for tests and preview), xUnit, SkiaSharp for PNG, System.Text.Json for catalog files. No other dependencies.

---

## Spec reference

This plan implements sections §1–6 and §10 of `docs/superpowers/specs/2026-04-24-timberborn-map-generator-design.md`. Serializer (§7) and UI (§8) are separate plans to be written after this is done.

## File structure (target)

```
MapGen/
  MapGen.sln
  MapGen.Core/
    MapGen.Core.csproj
    Primitives.cs             # Rng, GridCoord, VoxelCoord, Span struct
    MapData.cs                # All output value types
    GenerationConfig.cs
    GenerationResult.cs
    Biome.cs                  # Biome enum + adjacency + weights tables
    BiomeGrid.cs              # WFC solver + grid storage
    StartSelection.cs
    Column.cs                 # Voxel column type + utilities
    Noise.cs                  # Deterministic fBM value-noise
    Heightmap.cs              # Biome profiles + blending + crater + apron + overhangs
    Hydrology.cs              # River count + source + trace + underground + water entities
    PoissonDisk.cs            # Bridson sampler
    Catalog.cs                # Tree/Resource/Ruin/etc. catalog DTOs + loader
    Overlays.cs               # Trees + Resources + Thorns + Ruins + Blockages + Relics + Cores + Vents
    AccessValidation.cs       # BFS + top-up + slope placement
    MapGenerator.cs           # Pipeline orchestrator
  MapGen.Core.Tests/
    MapGen.Core.Tests.csproj
    PrimitivesTests.cs
    BiomeGridTests.cs
    StartSelectionTests.cs
    HeightmapTests.cs
    HydrologyTests.cs
    PoissonDiskTests.cs
    OverlaysTests.cs
    AccessValidationTests.cs
    MapGeneratorTests.cs
  MapGen.Preview/
    MapGen.Preview.csproj
    Renderer.cs
    Program.cs
  MapGen.Catalogs/             # Shipped JSON data
    Trees.json
    Resources.json
    Thorns.json
    Ruins.json
    BlockObjects.json
```

---

## Task 1: Repo scaffolding

**Files:**
- Create: `MapGen/MapGen.sln`
- Create: `MapGen/MapGen.Core/MapGen.Core.csproj`
- Create: `MapGen/MapGen.Core.Tests/MapGen.Core.Tests.csproj`
- Create: `MapGen/MapGen.Preview/MapGen.Preview.csproj`

- [ ] **Step 1: Create directory tree**

```bash
mkdir -p MapGen/MapGen.Core MapGen/MapGen.Core.Tests MapGen/MapGen.Preview MapGen/MapGen.Catalogs
```

- [ ] **Step 2: Write `MapGen/MapGen.Core/MapGen.Core.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <RootNamespace>MapGen</RootNamespace>
    <AssemblyName>MapGen.Core</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write `MapGen/MapGen.Core.Tests/MapGen.Core.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <RootNamespace>MapGen.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MapGen.Core\MapGen.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Write `MapGen/MapGen.Preview/MapGen.Preview.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <RootNamespace>MapGen.Preview</RootNamespace>
    <AssemblyName>MapGen.Preview</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SkiaSharp" Version="2.88.7" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MapGen.Core\MapGen.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Write `MapGen/MapGen.sln`**

Run: `cd MapGen && dotnet new sln -n MapGen && dotnet sln add MapGen.Core/MapGen.Core.csproj MapGen.Core.Tests/MapGen.Core.Tests.csproj MapGen.Preview/MapGen.Preview.csproj`

- [ ] **Step 6: Verify build**

Run: `cd MapGen && dotnet build`
Expected: Build succeeds (all projects are empty so should just pass). Warnings about "no code in project" are acceptable for Core and Preview.

- [ ] **Step 7: Commit**

```bash
git add MapGen/
git commit -m "MapGen: sln + three project skeletons"
```

---

## Task 2: Primitives (Rng, GridCoord, VoxelCoord, Span)

**Files:**
- Create: `MapGen/MapGen.Core/Primitives.cs`
- Create: `MapGen/MapGen.Core.Tests/PrimitivesTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/PrimitivesTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class PrimitivesTests
{
    [Fact]
    public void Rng_same_seed_produces_same_sequence()
    {
        var a = new Rng(42u);
        var b = new Rng(42u);
        for (int i = 0; i < 10; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Rng_next_float_in_zero_to_one()
    {
        var r = new Rng(1u);
        for (int i = 0; i < 1000; i++)
        {
            var f = r.NextFloat();
            Assert.InRange(f, 0f, 1f);
        }
    }

    [Fact]
    public void Rng_range_returns_value_in_half_open_interval()
    {
        var r = new Rng(7u);
        for (int i = 0; i < 1000; i++)
        {
            var v = r.NextRange(5, 10);
            Assert.InRange(v, 5, 9);
        }
    }

    [Fact]
    public void GridCoord_equality_and_hash_are_consistent()
    {
        var a = new GridCoord(3, 4);
        var b = new GridCoord(3, 4);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void VoxelSpan_with_valid_bounds_computes_height()
    {
        var s = new VoxelSpan(0, 5);
        Assert.Equal(5, s.Height);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: Build fails — Rng, GridCoord, VoxelSpan don't exist yet.

- [ ] **Step 3: Write `MapGen/MapGen.Core/Primitives.cs`**

```csharp
using System;

namespace MapGen;

/// Deterministic, reseedable RNG. xorshift32 — cheap, predictable, no
/// dependency on .NET's System.Random (which differs between runtimes).
public struct Rng
{
    private uint _state;

    public Rng(uint seed)
    {
        // 0 is a fixed point of xorshift; ensure we never start there.
        _state = seed == 0u ? 0x9E3779B9u : seed;
    }

    public uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// Returns a float in [0, 1).
    public float NextFloat() => (NextUInt() & 0x00FFFFFFu) / (float)(1 << 24);

    /// Returns an int in [min, max).
    public int NextRange(int min, int max)
    {
        if (max <= min) return min;
        return min + (int)(NextUInt() % (uint)(max - min));
    }

    /// Returns true with the given probability (0..1).
    public bool NextBool(float probability) => NextFloat() < probability;

    /// Weighted pick: indices[i] has weight weights[i]. Returns i.
    public int WeightedPick(float[] weights)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++) total += weights[i];
        var target = NextFloat() * total;
        float cumulative = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (target < cumulative) return i;
        }
        return weights.Length - 1;
    }
}

/// 2D integer coordinate. Used for metacells and voxel columns alike.
public readonly struct GridCoord : IEquatable<GridCoord>
{
    public readonly int X;
    public readonly int Y;

    public GridCoord(int x, int y) { X = x; Y = y; }

    public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GridCoord gc && Equals(gc);
    public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663);
    public override string ToString() => $"({X},{Y})";

    public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
    public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);
}

/// 3D integer coordinate. Timberborn uses (x, y, z) with z as height.
public readonly struct VoxelCoord : IEquatable<VoxelCoord>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public VoxelCoord(int x, int y, int z) { X = x; Y = y; Z = z; }

    public bool Equals(VoxelCoord other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is VoxelCoord vc && Equals(vc);
    public override int GetHashCode() =>
        (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791);
    public override string ToString() => $"({X},{Y},{Z})";
}

/// Contiguous range of solid voxels in a column: solid from Bottom (inclusive)
/// to Bottom+Height (exclusive). A column's list is sorted by Bottom.
public readonly struct VoxelSpan : IEquatable<VoxelSpan>
{
    public readonly int Bottom;
    public readonly int Height;

    public VoxelSpan(int bottom, int height)
    {
        Bottom = bottom;
        Height = height;
    }

    /// Topmost solid z (inclusive). Example: Bottom=0, Height=5 → Top=4.
    public int Top => Bottom + Height - 1;
    /// Exclusive top (i.e. the z where air starts).
    public int TopExclusive => Bottom + Height;

    public bool Equals(VoxelSpan other) => Bottom == other.Bottom && Height == other.Height;
    public override bool Equals(object? obj) => obj is VoxelSpan vs && Equals(vs);
    public override int GetHashCode() => (Bottom * 31) ^ Height;
    public override string ToString() => $"[{Bottom}..{Top}]";
}
```

- [ ] **Step 4: Run tests — verify pass**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: 5/5 tests passing.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/Primitives.cs MapGen/MapGen.Core.Tests/PrimitivesTests.cs
git commit -m "MapGen: Rng, GridCoord, VoxelCoord, VoxelSpan primitives"
```

---

## Task 3: MapData + GenerationConfig + GenerationResult

**Files:**
- Create: `MapGen/MapGen.Core/MapData.cs`
- Create: `MapGen/MapGen.Core/GenerationConfig.cs`
- Create: `MapGen/MapGen.Core/GenerationResult.cs`

These are plain data containers. No tests needed for pure POCOs — they're verified by use in every downstream stage's tests.

- [ ] **Step 1: Write `MapGen/MapGen.Core/MapData.cs`**

```csharp
using System.Collections.Generic;

namespace MapGen;

/// The generated map. Pipeline stages mutate this as they run.
/// All arrays are sized up-front in MapGenerator before any stage runs.
public sealed class MapData
{
    public int Width { get; }
    public int Height { get; }
    public uint Seed { get; }

    /// Metacell biome grid. Size: (Width/8) * (Height/8). Indexed by
    /// metacell coords: `Biomes[mx + my * MetaWidth]`.
    public Biome[] Biomes = null!;
    public int MetaWidth { get; internal set; }
    public int MetaHeight { get; internal set; }

    /// Which metacell (if any) is the Start. Null means pipeline hasn't
    /// picked yet; after StartSelection this is always non-null.
    public GridCoord? StartMeta;

    /// Per-voxel-column list of solid spans. Length = Width * Height.
    /// Index by `Columns[x + y * Width]`. Each column has at least one
    /// span; most have exactly one `(0, height)`. Rocky overhangs add
    /// a second floating span.
    public List<VoxelSpan>[] Columns = null!;

    /// Water depth per voxel column (top surface of water). 0 means no
    /// water. Positive = standing water above the topmost solid span.
    public byte[] WaterDepths = null!;

    /// Placed entities. Each entity has a blueprint key (catalog id), a
    /// voxel coord, an orientation, and optional per-kind metadata.
    public readonly List<PlacedEntity> Entities = new();
}

public enum Orientation : byte { North, East, South, West }

public readonly struct PlacedEntity
{
    public readonly string BlueprintKey;
    public readonly VoxelCoord Coord;
    public readonly Orientation Facing;
    public readonly EntityKind Kind;
    public readonly float Param;  // Flow rate for water sources, etc.

    public PlacedEntity(string blueprintKey, VoxelCoord coord, Orientation facing,
        EntityKind kind, float param = 0f)
    {
        BlueprintKey = blueprintKey;
        Coord = coord;
        Facing = facing;
        Kind = kind;
        Param = param;
    }
}

public enum EntityKind : byte
{
    Tree,
    Resource,
    Thorn,
    Ruin,
    Blockage,
    Relic,
    UnstableCore,
    GeothermalVent,
    Slope,
    WaterSource,
    BadwaterSource,
    StartMarker,
}

public static class MapDataExtensions
{
    public static int ColumnIndex(this MapData map, int x, int y) => x + y * map.Width;
    public static int ColumnIndex(this MapData map, GridCoord c) => c.X + c.Y * map.Width;
    public static int MetaIndex(this MapData map, int mx, int my) => mx + my * map.MetaWidth;
    public static int MetaIndex(this MapData map, GridCoord c) => c.X + c.Y * map.MetaWidth;

    /// Top solid z at (x, y), from the uppermost span. −1 if no solid span.
    public static int TopHeight(this MapData map, int x, int y)
    {
        var spans = map.Columns[map.ColumnIndex(x, y)];
        if (spans.Count == 0) return -1;
        int top = int.MinValue;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].TopExclusive > top) top = spans[i].TopExclusive;
        return top - 1;
    }
}
```

- [ ] **Step 2: Write `MapGen/MapGen.Core/GenerationConfig.cs`**

```csharp
namespace MapGen;

/// Inputs to MapGenerator. Not hardcoded so future UI / CLI can override
/// particular subsystems' parameters without recompiling.
public sealed class GenerationConfig
{
    public int Width { get; init; } = 128;
    public int Height { get; init; } = 128;
    public uint Seed { get; init; } = 1;

    /// Retry budget — seed-increment restarts allowed before surfacing
    /// a hard error. Per spec §9.
    public int PipelineRetryBudget { get; init; } = 5;

    public int MetaCellSize { get; init; } = 8;

    public void Validate()
    {
        if (Width < 16 || Height < 16)
            throw new System.ArgumentOutOfRangeException(
                nameof(Width), "Map dimensions must be at least 16×16.");
        if (Width % MetaCellSize != 0 || Height % MetaCellSize != 0)
            throw new System.ArgumentException(
                $"Width and Height must be multiples of MetaCellSize ({MetaCellSize}).");
    }
}
```

- [ ] **Step 3: Write `MapGen/MapGen.Core/GenerationResult.cs`**

```csharp
using System.Collections.Generic;

namespace MapGen;

public enum GenerationStatus { Success, Failed }

public sealed class GenerationResult
{
    public GenerationStatus Status { get; }
    public MapData? Map { get; }
    public uint ActualSeedUsed { get; }
    public int RetryCount { get; }
    public IReadOnlyList<string> Log { get; }
    public string? FailureReason { get; }

    public GenerationResult(GenerationStatus status, MapData? map, uint actualSeed,
        int retries, IReadOnlyList<string> log, string? failureReason = null)
    {
        Status = status;
        Map = map;
        ActualSeedUsed = actualSeed;
        RetryCount = retries;
        Log = log;
        FailureReason = failureReason;
    }
}
```

- [ ] **Step 4: Build — verify compiles**

Run: `cd MapGen && dotnet build`
Expected: Build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/MapData.cs MapGen/MapGen.Core/GenerationConfig.cs MapGen/MapGen.Core/GenerationResult.cs
git commit -m "MapGen: MapData value types + config + result DTOs"
```

---

## Task 4: Biome enum + adjacency + weights

**Files:**
- Create: `MapGen/MapGen.Core/Biome.cs`

- [ ] **Step 1: Write `MapGen/MapGen.Core/Biome.cs`**

```csharp
namespace MapGen;

/// Biomes used in WFC. "Start" is a post-WFC label, not a WFC vocabulary
/// entry, but we include it in the enum so downstream stages can switch on
/// the same type. WFC ignores any cell tagged as Start.
public enum Biome : byte
{
    Meadow = 0,
    Forest = 1,
    Badland = 2,
    Rocky = 3,
    Sea = 4,
    Crater = 5,
    Start = 6,
}

public static class Biomes
{
    /// The biome vocabulary WFC operates on (excludes Start).
    public static readonly Biome[] WfcSet =
    {
        Biome.Meadow, Biome.Forest, Biome.Badland,
        Biome.Rocky, Biome.Sea, Biome.Crater,
    };

    /// Target frequency per biome. Index matches WfcSet. Must sum to 1.0.
    /// Values drawn from spec §1.
    public static readonly float[] Weights =
    {
        0.37f,  // Meadow
        0.25f,  // Forest
        0.20f,  // Badland
        0.15f,  // Rocky
        0.02f,  // Sea
        0.01f,  // Crater
    };

    /// Symmetric adjacency table. `Allowed[a,b]` = is biome a allowed to
    /// share an edge with biome b? Indexed by (byte)Biome.
    /// Sourced directly from spec §1.
    public static readonly bool[,] Allowed = BuildAllowed();

    private static bool[,] BuildAllowed()
    {
        var a = new bool[7, 7];
        // Default-allowed pairs (symmetric).
        void Set(Biome x, Biome y, bool v) { a[(byte)x, (byte)y] = v; a[(byte)y, (byte)x] = v; }

        // Meadow row
        Set(Biome.Meadow, Biome.Meadow, true);
        Set(Biome.Meadow, Biome.Forest, true);
        Set(Biome.Meadow, Biome.Badland, true);
        Set(Biome.Meadow, Biome.Rocky, false);
        Set(Biome.Meadow, Biome.Sea, true);
        Set(Biome.Meadow, Biome.Crater, true);
        // Forest row
        Set(Biome.Forest, Biome.Forest, true);
        Set(Biome.Forest, Biome.Badland, false);
        Set(Biome.Forest, Biome.Rocky, true);
        Set(Biome.Forest, Biome.Sea, true);
        Set(Biome.Forest, Biome.Crater, true);
        // Badland row
        Set(Biome.Badland, Biome.Badland, true);
        Set(Biome.Badland, Biome.Rocky, true);
        Set(Biome.Badland, Biome.Sea, true);
        Set(Biome.Badland, Biome.Crater, true);
        // Rocky row
        Set(Biome.Rocky, Biome.Rocky, true);
        Set(Biome.Rocky, Biome.Sea, true);
        Set(Biome.Rocky, Biome.Crater, true);
        // Sea row
        Set(Biome.Sea, Biome.Sea, true);
        Set(Biome.Sea, Biome.Crater, true);
        // Crater row
        Set(Biome.Crater, Biome.Crater, false);   // Craters stay isolated
        return a;
    }

    public static bool IsAllowedAdjacent(Biome a, Biome b) => Allowed[(byte)a, (byte)b];
}
```

- [ ] **Step 2: Build — verify compiles**

Run: `cd MapGen && dotnet build`

- [ ] **Step 3: Commit**

```bash
git add MapGen/MapGen.Core/Biome.cs
git commit -m "MapGen: Biome enum + adjacency table + weights"
```

---

## Task 5: BiomeGrid WFC solver — adjacency invariant test + solver

**Files:**
- Create: `MapGen/MapGen.Core/BiomeGrid.cs`
- Create: `MapGen/MapGen.Core.Tests/BiomeGridTests.cs`

- [ ] **Step 1: Write the failing test**

Write `MapGen/MapGen.Core.Tests/BiomeGridTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class BiomeGridTests
{
    [Fact]
    public void Solve_16x16_produces_fully_collapsed_grid()
    {
        var rng = new Rng(42u);
        var grid = BiomeGrid.Solve(metaWidth: 16, metaHeight: 16, ref rng);
        Assert.NotNull(grid);
        Assert.Equal(16 * 16, grid!.Length);
        foreach (var b in grid) Assert.Contains(b, Biomes.WfcSet);
    }

    [Fact]
    public void Solve_same_seed_same_grid()
    {
        var rng1 = new Rng(1u);
        var g1 = BiomeGrid.Solve(8, 8, ref rng1);
        var rng2 = new Rng(1u);
        var g2 = BiomeGrid.Solve(8, 8, ref rng2);
        Assert.NotNull(g1);
        Assert.NotNull(g2);
        Assert.Equal(g1, g2);
    }

    [Fact]
    public void Solve_all_adjacent_pairs_are_allowed()
    {
        var rng = new Rng(99u);
        var grid = BiomeGrid.Solve(16, 16, ref rng);
        Assert.NotNull(grid);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            var here = grid![x + y * 16];
            if (x + 1 < 16) Assert.True(Biomes.IsAllowedAdjacent(here, grid[(x + 1) + y * 16]));
            if (y + 1 < 16) Assert.True(Biomes.IsAllowedAdjacent(here, grid[x + (y + 1) * 16]));
        }
    }

    [Fact]
    public void Solve_over_many_seeds_no_contradiction_retries_exhaust()
    {
        // Sanity: across 50 seeds, most or all should succeed on first try.
        int successes = 0;
        for (uint s = 0; s < 50; s++)
        {
            var rng = new Rng(s);
            if (BiomeGrid.Solve(16, 16, ref rng) != null) successes++;
        }
        Assert.True(successes >= 45, $"Only {successes}/50 seeds produced a grid — too many contradictions.");
    }
}
```

- [ ] **Step 2: Run test — fails (BiomeGrid doesn't exist)**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: Build failure — `BiomeGrid` unknown.

- [ ] **Step 3: Write `MapGen/MapGen.Core/BiomeGrid.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

/// Wave Function Collapse solver for the biome metacell grid.
/// Tiled-WFC model: each cell starts with all 6 WFC biomes possible, we
/// observe the lowest-entropy cell (breaking ties with seeded RNG), propagate
/// constraints, repeat.
public static class BiomeGrid
{
    private const int MaxAttempts = 5;

    /// Returns the collapsed grid, or null if every attempt contradicted.
    /// The grid is a row-major array: `grid[x + y*metaWidth]`.
    public static Biome[]? Solve(int metaWidth, int metaHeight, ref Rng rng)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var result = TrySolve(metaWidth, metaHeight, ref rng);
            if (result != null) return result;
        }
        return null;
    }

    private static Biome[]? TrySolve(int metaWidth, int metaHeight, ref Rng rng)
    {
        int n = metaWidth * metaHeight;
        // Domains[i] is a bitmask over WfcSet indices (0..5).
        var domains = new int[n];
        int allMask = (1 << Biomes.WfcSet.Length) - 1;  // 0b00111111 = 63
        for (int i = 0; i < n; i++) domains[i] = allMask;

        var output = new Biome[n];
        int collapsedCount = 0;

        while (collapsedCount < n)
        {
            int idx = PickLowestEntropy(domains, ref rng);
            if (idx < 0) return null;  // contradiction

            int pick = ObservationPick(domains[idx], ref rng);
            domains[idx] = 1 << pick;
            output[idx] = Biomes.WfcSet[pick];
            collapsedCount++;

            if (!Propagate(domains, metaWidth, metaHeight, idx)) return null;
        }

        // Fill output from singleton domains for any cell we didn't directly
        // set (shouldn't happen, but guards against propagation-only collapse).
        for (int i = 0; i < n; i++)
        {
            int mask = domains[i];
            for (int b = 0; b < Biomes.WfcSet.Length; b++)
            {
                if ((mask & (1 << b)) != 0) { output[i] = Biomes.WfcSet[b]; break; }
            }
        }
        return output;
    }

    private static int PickLowestEntropy(int[] domains, ref Rng rng)
    {
        int bestCount = int.MaxValue;
        int bestIdx = -1;
        var ties = new List<int>();
        for (int i = 0; i < domains.Length; i++)
        {
            int mask = domains[i];
            if (mask == 0) return -1;  // contradiction
            int count = System.Numerics.BitOperations.PopCount((uint)mask);
            if (count == 1) continue;  // already collapsed
            if (count < bestCount)
            {
                bestCount = count;
                ties.Clear();
                ties.Add(i);
            }
            else if (count == bestCount)
            {
                ties.Add(i);
            }
        }
        if (ties.Count == 0) return -2;  // no uncollapsed cells
        return ties[rng.NextRange(0, ties.Count)];
    }

    private static int ObservationPick(int mask, ref Rng rng)
    {
        // Weighted pick among remaining options.
        var kept = new List<int>();
        var weights = new List<float>();
        for (int b = 0; b < Biomes.WfcSet.Length; b++)
        {
            if ((mask & (1 << b)) != 0)
            {
                kept.Add(b);
                weights.Add(Biomes.Weights[b]);
            }
        }
        int w = rng.WeightedPick(weights.ToArray());
        return kept[w];
    }

    private static bool Propagate(int[] domains, int metaWidth, int metaHeight, int start)
    {
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % metaWidth;
            int y = i / metaWidth;
            int mask = domains[i];

            foreach (var (nx, ny) in Neighbors(x, y, metaWidth, metaHeight))
            {
                int ni = nx + ny * metaWidth;
                int before = domains[ni];
                int after = before;
                for (int nb = 0; nb < Biomes.WfcSet.Length; nb++)
                {
                    if ((before & (1 << nb)) == 0) continue;  // not in neighbor's domain
                    // Check: is there ANY option in `i`'s domain that allows nb?
                    bool anyAllowed = false;
                    for (int b = 0; b < Biomes.WfcSet.Length; b++)
                    {
                        if ((mask & (1 << b)) == 0) continue;
                        if (Biomes.IsAllowedAdjacent(Biomes.WfcSet[b], Biomes.WfcSet[nb]))
                        {
                            anyAllowed = true;
                            break;
                        }
                    }
                    if (!anyAllowed) after &= ~(1 << nb);
                }
                if (after != before)
                {
                    if (after == 0) return false;  // contradiction
                    domains[ni] = after;
                    stack.Push(ni);
                }
            }
        }
        return true;
    }

    private static IEnumerable<(int X, int Y)> Neighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }

    /// Crater-on-edge rewrite. Spec §1 post-WFC fix.
    public static void RewriteEdgeCraters(Biome[] grid, int metaWidth, int metaHeight)
    {
        for (int y = 0; y < metaHeight; y++)
        for (int x = 0; x < metaWidth; x++)
        {
            bool onEdge = x == 0 || y == 0 || x == metaWidth - 1 || y == metaHeight - 1;
            if (!onEdge) continue;
            int i = x + y * metaWidth;
            if (grid[i] != Biome.Crater) continue;

            // Majority vote of in-bounds neighbors. Count each biome occurrence.
            var counts = new int[7];
            foreach (var (nx, ny) in Neighbors(x, y, metaWidth, metaHeight))
            {
                var nb = grid[nx + ny * metaWidth];
                if (nb != Biome.Crater) counts[(byte)nb]++;
            }
            int bestB = 0, bestN = -1;
            for (int b = 0; b < counts.Length; b++)
            {
                if (counts[b] > bestN) { bestN = counts[b]; bestB = b; }
            }
            grid[i] = (Biome)bestB;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: 4/4 BiomeGridTests passing + previous 5 passing. 9 total.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/BiomeGrid.cs MapGen/MapGen.Core.Tests/BiomeGridTests.cs
git commit -m "MapGen: WFC biome grid solver + crater-edge rewrite"
```

---

## Task 6: Start selection (post-WFC labeling)

**Files:**
- Create: `MapGen/MapGen.Core/StartSelection.cs`
- Create: `MapGen/MapGen.Core.Tests/StartSelectionTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/StartSelectionTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class StartSelectionTests
{
    [Fact]
    public void Pick_never_selects_rocky_sea_or_crater()
    {
        for (uint s = 0; s < 30; s++)
        {
            var rng = new Rng(s);
            var grid = BiomeGrid.Solve(16, 16, ref rng)!;
            BiomeGrid.RewriteEdgeCraters(grid, 16, 16);
            var pick = StartSelection.Pick(grid, 16, 16, ref rng);
            Assert.NotNull(pick);
            int idx = pick!.Value.X + pick.Value.Y * 16;
            Assert.DoesNotContain(grid[idx], new[] { Biome.Rocky, Biome.Sea, Biome.Crater });
        }
    }

    [Fact]
    public void Pick_is_deterministic()
    {
        var r1 = new Rng(5u);
        var g1 = BiomeGrid.Solve(16, 16, ref r1)!;
        BiomeGrid.RewriteEdgeCraters(g1, 16, 16);
        var rngA = new Rng(5u);  // reset stream before selection
        var aPick = StartSelection.Pick(g1, 16, 16, ref rngA);

        var r2 = new Rng(5u);
        var g2 = BiomeGrid.Solve(16, 16, ref r2)!;
        BiomeGrid.RewriteEdgeCraters(g2, 16, 16);
        var rngB = new Rng(5u);
        var bPick = StartSelection.Pick(g2, 16, 16, ref rngB);

        Assert.Equal(aPick, bPick);
    }
}
```

- [ ] **Step 2: Run — fails (StartSelection doesn't exist)**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/StartSelection.cs`**

```csharp
using System.Collections.Generic;

namespace MapGen;

/// Post-WFC Start labeling. Picks exactly one metacell and relabels it
/// with Biome.Start, based on the scoring table in spec §5.
public static class StartSelection
{
    /// Returns the metacell coord picked as Start, or null if no candidate
    /// passed the disqualifier (shouldn't happen on a reasonable grid).
    /// The caller should then mutate grid[idx] = Biome.Start.
    public static GridCoord? Pick(Biome[] grid, int metaWidth, int metaHeight, ref Rng rng)
    {
        int bestScore = int.MinValue;
        var ties = new List<GridCoord>();

        for (int y = 0; y < metaHeight; y++)
        for (int x = 0; x < metaWidth; x++)
        {
            int idx = x + y * metaWidth;
            int score = ScoreCell(grid, metaWidth, metaHeight, x, y);
            if (score == int.MinValue) continue;  // disqualified
            if (score > bestScore)
            {
                bestScore = score;
                ties.Clear();
                ties.Add(new GridCoord(x, y));
            }
            else if (score == bestScore)
            {
                ties.Add(new GridCoord(x, y));
            }
        }
        if (ties.Count == 0) return null;
        return ties[rng.NextRange(0, ties.Count)];
    }

    /// Apply the pick — call after Pick returns non-null.
    public static void Apply(Biome[] grid, int metaWidth, GridCoord pick)
    {
        grid[pick.X + pick.Y * metaWidth] = Biome.Start;
    }

    private static int ScoreCell(Biome[] grid, int mw, int mh, int x, int y)
    {
        var here = grid[x + y * mw];
        int baseScore = here switch
        {
            Biome.Meadow => 3,
            Biome.Forest => 2,
            Biome.Badland => 0,
            _ => int.MinValue,  // Rocky / Sea / Crater disqualified
        };
        if (baseScore == int.MinValue) return int.MinValue;

        int score = baseScore;
        bool onBorder = x == 0 || y == 0 || x == mw - 1 || y == mh - 1;
        if (onBorder) score -= 2;

        bool seaNeighborDirect = false;
        int meadowForestNeighbors = 0;
        foreach (var (nx, ny) in Neighbors(x, y, mw, mh))
        {
            var nb = grid[nx + ny * mw];
            if (nb == Biome.Sea) seaNeighborDirect = true;
            if (nb == Biome.Meadow || nb == Biome.Forest) meadowForestNeighbors++;
        }
        if (seaNeighborDirect) score += 3;
        else if (HasSeaWithin(grid, mw, mh, x, y, radius: 2)) score += 1;
        score += 2 * meadowForestNeighbors;

        return score;
    }

    private static bool HasSeaWithin(Biome[] grid, int mw, int mh, int cx, int cy, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
            if (grid[nx + ny * mw] == Biome.Sea) return true;
        }
        return false;
    }

    private static IEnumerable<(int X, int Y)> Neighbors(int x, int y, int mw, int mh)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < mw) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < mh) yield return (x, y + 1);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: 2/2 StartSelection + all prior pass.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/StartSelection.cs MapGen/MapGen.Core.Tests/StartSelectionTests.cs
git commit -m "MapGen: Start selection post-WFC scoring"
```

---

## Task 7: Deterministic fBM value-noise

**Files:**
- Create: `MapGen/MapGen.Core/Noise.cs`
- Create: `MapGen/MapGen.Core.Tests/NoiseTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/NoiseTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class NoiseTests
{
    [Fact]
    public void Noise_deterministic_for_same_seed()
    {
        var a = Noise.Sample(1.5f, 2.5f, seed: 42u);
        var b = Noise.Sample(1.5f, 2.5f, seed: 42u);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Noise_in_zero_to_one_range()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x = i * 0.13f;
            float y = i * 0.27f;
            var v = Noise.Sample(x, y, seed: 99u);
            Assert.InRange(v, 0f, 1f);
        }
    }

    [Fact]
    public void Fbm_deterministic()
    {
        var a = Noise.Fbm(0.5f, 0.5f, octaves: 4, frequency: 0.1f, seed: 7u);
        var b = Noise.Fbm(0.5f, 0.5f, octaves: 4, frequency: 0.1f, seed: 7u);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Fbm_varies_with_position()
    {
        // Ensure the noise field isn't trivially constant.
        var a = Noise.Fbm(0f, 0f, 4, 0.1f, 1u);
        var b = Noise.Fbm(10f, 10f, 4, 0.1f, 1u);
        Assert.NotEqual(a, b);
    }
}
```

- [ ] **Step 2: Run — fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/Noise.cs`**

```csharp
using System;

namespace MapGen;

/// Deterministic value-noise + fBM. Pure function of (x, y, seed) — no
/// hidden state, no .NET Random. Used by Heightmap for per-biome noise.
public static class Noise
{
    /// Single-octave value-noise sample at (x, y). Returns value in [0, 1].
    public static float Sample(float x, float y, uint seed)
    {
        int xi = Floor(x);
        int yi = Floor(y);
        float xf = x - xi;
        float yf = y - yi;
        // Smoothstep easing — classic value-noise bilinear interp with S-curve.
        float u = Smoothstep(xf);
        float v = Smoothstep(yf);
        float n00 = Hash01(xi,     yi,     seed);
        float n10 = Hash01(xi + 1, yi,     seed);
        float n01 = Hash01(xi,     yi + 1, seed);
        float n11 = Hash01(xi + 1, yi + 1, seed);
        float nx0 = Lerp(n00, n10, u);
        float nx1 = Lerp(n01, n11, u);
        return Lerp(nx0, nx1, v);
    }

    /// Fractional-Brownian-Motion: stack of octaves of value-noise with
    /// each octave's frequency doubled and amplitude halved. Classic fBM.
    public static float Fbm(float x, float y, int octaves, float frequency, uint seed)
    {
        float sum = 0f;
        float amplitude = 1f;
        float totalAmp = 0f;
        float freq = frequency;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Sample(x * freq, y * freq, seed + (uint)i);
            totalAmp += amplitude;
            amplitude *= 0.5f;
            freq *= 2f;
        }
        return sum / totalAmp;  // normalized to [0, 1]
    }

    private static int Floor(float v) => (int)Math.Floor(v);

    private static float Smoothstep(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// Hash (xi, yi, seed) → float in [0, 1]. Same inputs → same output.
    private static float Hash01(int xi, int yi, uint seed)
    {
        uint h = (uint)xi * 0x9E3779B1u;
        h ^= (uint)yi * 0x85EBCA6Bu;
        h ^= seed * 0xC2B2AE35u;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h & 0x00FFFFFFu) / (float)(1 << 24);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: 4/4 NoiseTests passing.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/Noise.cs MapGen/MapGen.Core.Tests/NoiseTests.cs
git commit -m "MapGen: deterministic value-noise + fBM"
```

---

## Task 8: Heightmap — per-biome profiles, blending, crater, apron

**Files:**
- Create: `MapGen/MapGen.Core/Heightmap.cs`
- Create: `MapGen/MapGen.Core.Tests/HeightmapTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/HeightmapTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class HeightmapTests
{
    [Fact]
    public void Build_produces_one_column_per_cell()
    {
        var map = MakeBareMap(64, 64, seedBiomes: Biome.Meadow);
        var rng = new Rng(1u);
        Heightmap.Build(map, ref rng);
        Assert.Equal(64 * 64, map.Columns.Length);
        foreach (var col in map.Columns) Assert.NotEmpty(col);
    }

    [Fact]
    public void Build_heights_within_biome_amplitude()
    {
        var map = MakeBareMap(32, 32, seedBiomes: Biome.Meadow);
        var rng = new Rng(1u);
        Heightmap.Build(map, ref rng);
        int minBase = 4 - 2;  // Meadow base 4, amplitude ±2
        int maxBase = 4 + 2;
        for (int i = 0; i < map.Columns.Length; i++)
        {
            var top = map.Columns[i][0].TopExclusive - 1;
            // Allow blending inaccuracy at the edges — all same biome so should be tight
            Assert.InRange(top, minBase - 1, maxBase + 1);
        }
    }

    [Fact]
    public void Build_apron_around_start_is_flat()
    {
        // Place Start at metacell (1,1) with Meadow surroundings. Heights in
        // Start metacell + adjacent 3-voxel apron should be very nearly equal.
        var map = MakeBareMap(24, 24, seedBiomes: Biome.Meadow);
        var mstart = new GridCoord(1, 1);
        map.Biomes[map.MetaIndex(mstart)] = Biome.Start;
        map.StartMeta = mstart;
        var rng = new Rng(1u);
        Heightmap.Build(map, ref rng);

        int expected = 4;  // Start base height
        for (int vy = 8; vy < 16; vy++)
        for (int vx = 8; vx < 16; vx++)
        {
            var top = map.TopHeight(vx, vy);
            Assert.InRange(top, expected - 1, expected + 1);
        }
    }

    private static MapData MakeBareMap(int w, int h, Biome seedBiomes)
    {
        var map = new MapData(w, h, 1u);
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        for (int i = 0; i < map.Biomes.Length; i++) map.Biomes[i] = seedBiomes;
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan>();
        map.WaterDepths = new byte[w * h];
        return map;
    }
}
```

**Blocker:** MapData has no constructor — add one.

- [ ] **Step 2: Add MapData constructor**

Edit `MapGen/MapGen.Core/MapData.cs` — add at the top of the class:

```csharp
public MapData(int width, int height, uint seed)
{
    Width = width;
    Height = height;
    Seed = seed;
}
```

- [ ] **Step 3: Write `MapGen/MapGen.Core/Heightmap.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

public static class Heightmap
{
    /// Per-biome heightmap profile. Parameters match spec §2.
    private readonly struct Profile
    {
        public readonly float BaseHeight;
        public readonly float Amplitude;
        public readonly int Octaves;
        public readonly float Frequency;

        public Profile(float b, float a, int o, float f)
        { BaseHeight = b; Amplitude = a; Octaves = o; Frequency = f; }
    }

    private static Profile ProfileFor(Biome b) => b switch
    {
        Biome.Sea     => new Profile(1f, 1f, 2, 0.05f),
        Biome.Meadow  => new Profile(4f, 2f, 3, 0.08f),
        Biome.Forest  => new Profile(5f, 3f, 4, 0.10f),
        Biome.Badland => new Profile(6f, 4f, 3, 0.06f),
        Biome.Rocky   => new Profile(14f, 6f, 5, 0.18f),
        Biome.Start   => new Profile(4f, 0f, 1, 0.01f),
        _             => new Profile(4f, 2f, 3, 0.08f),  // Crater: special, see below
    };

    private const int MetaSize = 8;
    private const int BlendRadius = 2;
    private const int StartApronWidth = 3;

    public static void Build(MapData map, ref Rng rng)
    {
        uint noiseSeed = rng.NextUInt();

        // Precompute per-metacell crater info (if any).
        var craters = BuildCraterInfo(map, ref rng);

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            float height = SampleHeight(map, x, y, noiseSeed, craters);
            int zTop = Math.Max(1, (int)MathF.Round(height));
            var spans = map.Columns[map.ColumnIndex(x, y)];
            spans.Clear();
            spans.Add(new VoxelSpan(0, zTop));
        }

        // Overhangs (Rocky biome only) — second pass.
        AddRockyOverhangs(map, noiseSeed);
    }

    private static float SampleHeight(MapData map, int x, int y, uint noiseSeed,
        Dictionary<GridCoord, CraterInfo> craters)
    {
        // Identify the home metacell.
        int mx = x / MetaSize;
        int my = y / MetaSize;

        // Crater override: if (x,y) falls inside a Crater metacell, compute
        // the bowl-shape height directly and blend with the noise just near
        // the metacell edges.
        var homeBiome = map.Biomes[map.MetaIndex(mx, my)];
        if (homeBiome == Biome.Crater &&
            craters.TryGetValue(new GridCoord(mx, my), out var ci))
        {
            float craterHeight = EvalCrater(x, y, ci);
            // Simple blend with noise-based average at the edge of the metacell
            // (within 2 voxels of the border).
            int localX = x - mx * MetaSize;
            int localY = y - my * MetaSize;
            int distFromEdge = Math.Min(Math.Min(localX, localY),
                Math.Min(MetaSize - 1 - localX, MetaSize - 1 - localY));
            if (distFromEdge < BlendRadius)
            {
                float blended = SampleBlendedNoise(map, x, y, noiseSeed, skipCraterCells: true);
                float t = distFromEdge / (float)BlendRadius;
                return Lerp(blended, craterHeight, t);
            }
            return craterHeight;
        }

        // Start biome: always flat at base height, no noise.
        if (homeBiome == Biome.Start)
            return ProfileFor(Biome.Start).BaseHeight;

        // Start apron: if within StartApronWidth voxels of a Start metacell's
        // border, clamp height to Start's base. Spec §2.
        if (WithinStartApron(map, x, y, out float apronRamp))
        {
            float startH = ProfileFor(Biome.Start).BaseHeight;
            float naturalH = SampleBlendedNoise(map, x, y, noiseSeed, skipCraterCells: false);
            return Lerp(startH, naturalH, apronRamp);
        }

        return SampleBlendedNoise(map, x, y, noiseSeed, skipCraterCells: false);
    }

    /// Distance-weighted average of per-biome profile evaluations over the
    /// 4-metacell neighborhood. Produces smooth biome border transitions.
    private static float SampleBlendedNoise(MapData map, int x, int y, uint noiseSeed,
        bool skipCraterCells)
    {
        float sum = 0f, weight = 0f;
        int mx = x / MetaSize, my = y / MetaSize;

        // 3x3 neighborhood of metacells (corners + self + sides).
        for (int dmy = -1; dmy <= 1; dmy++)
        for (int dmx = -1; dmx <= 1; dmx++)
        {
            int nmx = mx + dmx, nmy = my + dmy;
            if (nmx < 0 || nmy < 0 || nmx >= map.MetaWidth || nmy >= map.MetaHeight) continue;
            var b = map.Biomes[map.MetaIndex(nmx, nmy)];
            if (skipCraterCells && b == Biome.Crater) continue;
            if (b == Biome.Start || b == Biome.Crater) continue;

            // Weight = 1 at metacell center, falling to 0 at 2 metacells away.
            float cx = (nmx + 0.5f) * MetaSize;
            float cy = (nmy + 0.5f) * MetaSize;
            float dist = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float w = MathF.Max(0f, 1f - dist / (MetaSize * 1.5f));
            if (w <= 0f) continue;

            var prof = ProfileFor(b);
            float n = Noise.Fbm(x, y, prof.Octaves, prof.Frequency, noiseSeed);
            float h = prof.BaseHeight + (n * 2f - 1f) * prof.Amplitude;
            sum += h * w;
            weight += w;
        }
        if (weight <= 0f) return 4f;  // fallback — shouldn't happen
        return sum / weight;
    }

    private static bool WithinStartApron(MapData map, int x, int y, out float ramp)
    {
        ramp = 0f;
        if (!map.StartMeta.HasValue) return false;
        var sm = map.StartMeta.Value;
        int sxMin = sm.X * MetaSize;
        int syMin = sm.Y * MetaSize;
        int sxMax = sxMin + MetaSize - 1;
        int syMax = syMin + MetaSize - 1;
        if (x >= sxMin && x <= sxMax && y >= syMin && y <= syMax)
        {
            ramp = 0f;  // inside Start metacell → fully clamped to Start height
            return true;
        }
        // Check distance to Start metacell in voxels.
        int dx = x < sxMin ? sxMin - x : (x > sxMax ? x - sxMax : 0);
        int dy = y < syMin ? syMin - y : (y > syMax ? y - syMax : 0);
        int dist = Math.Max(dx, dy);
        if (dist > StartApronWidth) return false;

        // Apron only applies if the containing metacell is Meadow or Forest.
        int mx = x / MetaSize, my = y / MetaSize;
        var b = map.Biomes[map.MetaIndex(mx, my)];
        if (b != Biome.Meadow && b != Biome.Forest) return false;

        ramp = dist / (float)StartApronWidth;  // 0 = at border, 1 = at apron edge
        return true;
    }

    // ---------- Crater ----------

    private readonly struct CraterInfo
    {
        public readonly float Cx, Cy;  // center in voxel coords
        public readonly float Radius;
        public readonly float BaseH;
        public CraterInfo(float cx, float cy, float r, float b)
        { Cx = cx; Cy = cy; Radius = r; BaseH = b; }
    }

    private static Dictionary<GridCoord, CraterInfo> BuildCraterInfo(MapData map, ref Rng rng)
    {
        var result = new Dictionary<GridCoord, CraterInfo>();
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            if (map.Biomes[map.MetaIndex(mx, my)] != Biome.Crater) continue;
            float cx = mx * MetaSize + rng.NextRange(2, 6);
            float cy = my * MetaSize + rng.NextRange(2, 6);
            float r = rng.NextRange(3, 7);
            result[new GridCoord(mx, my)] = new CraterInfo(cx, cy, r, 6f);
        }
        return result;
    }

    private static float EvalCrater(int x, int y, CraterInfo ci)
    {
        float dx = x - ci.Cx;
        float dy = y - ci.Cy;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > ci.Radius) return ci.BaseH;
        // Raised rim between 0.7r and r, pit from 0 to 0.7r.
        float pitZone = 0.7f * ci.Radius;
        if (d >= pitZone)
        {
            // Rim: peak at 0.85r.
            float rimCenter = 0.85f * ci.Radius;
            float rimWidth = 0.15f * ci.Radius;
            float rimAmp = MathF.Max(0f, 1f - MathF.Abs(d - rimCenter) / rimWidth);
            return ci.BaseH + rimAmp * 2.5f;
        }
        // Pit: −4 at center, easing up to 0 at 0.7r.
        float t = d / pitZone;
        return ci.BaseH - (1f - t) * 4f;
    }

    // ---------- Overhangs (Rocky only) ----------

    private static void AddRockyOverhangs(MapData map, uint noiseSeed)
    {
        // For every Rocky column, look for a neighbor with height delta >= 4.
        // If found, 30% roll to place an overhang.
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int mx = x / MetaSize, my = y / MetaSize;
            if (map.Biomes[map.MetaIndex(mx, my)] != Biome.Rocky) continue;
            int h = map.Columns[map.ColumnIndex(x, y)][0].TopExclusive;

            int bestDelta = 0;
            (int nx, int ny) bestN = (x, y);
            foreach (var (nx, ny) in FourNeighbors(x, y, map.Width, map.Height))
            {
                int nh = map.Columns[map.ColumnIndex(nx, ny)][0].TopExclusive;
                int delta = nh - h;
                if (delta > bestDelta) { bestDelta = delta; bestN = (nx, ny); }
            }
            if (bestDelta < 4) continue;

            // Seeded per-cell roll.
            if (CellHashFloat(x, y, noiseSeed) < 0.7f) continue;  // 30% pass
            int overhangTop = map.Columns[map.ColumnIndex(bestN.nx, bestN.ny)][0].TopExclusive;
            int gap = 2;
            int thickness = 2;
            int slabBottom = overhangTop - thickness;
            if (slabBottom <= h + gap) continue;  // no room
            map.Columns[map.ColumnIndex(x, y)].Add(new VoxelSpan(slabBottom, thickness));
        }
    }

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }

    private static float CellHashFloat(int x, int y, uint seed)
    {
        uint h = (uint)x * 0x9E3779B1u;
        h ^= (uint)y * 0x85EBCA6Bu;
        h ^= seed;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        return (h & 0x00FFFFFFu) / (float)(1 << 24);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: 3/3 Heightmap + all prior tests passing.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/Heightmap.cs MapGen/MapGen.Core/MapData.cs MapGen/MapGen.Core.Tests/HeightmapTests.cs
git commit -m "MapGen: Heightmap with biome profiles, blending, crater, apron, overhangs"
```

---

## Task 9: Hydrology — river count + source pick + downhill trace

**Files:**
- Create: `MapGen/MapGen.Core/Hydrology.cs`
- Create: `MapGen/MapGen.Core.Tests/HydrologyTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/HydrologyTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class HydrologyTests
{
    [Fact]
    public void Build_places_at_least_one_water_source()
    {
        var map = MakePreparedMap(64, 64, ref _seedRng);
        var rng = new Rng(1u);
        Hydrology.Build(map, ref rng);
        int sources = 0;
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.WaterSource || e.Kind == EntityKind.BadwaterSource)
                sources++;
        Assert.True(sources >= 1, "Expected at least one water source entity.");
    }

    [Fact]
    public void Build_carves_river_cells_reaching_map_edge_or_sea()
    {
        // A 64x64 map with Meadow biomes guarantees hydrology will pick a
        // source and trace to an edge. Assert the trace leaves the map.
        var map = MakePreparedMap(64, 64, ref _seedRng);
        var rng = new Rng(2u);
        Hydrology.Build(map, ref rng);
        bool reachesEdge = false;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.WaterDepths[map.ColumnIndex(x, y)] > 0 &&
                (x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1))
            {
                reachesEdge = true; break;
            }
        }
        Assert.True(reachesEdge, "Expected river to reach a map edge drain.");
    }

    private Rng _seedRng = new Rng(100u);

    private static MapData MakePreparedMap(int w, int h, ref Rng seedRng)
    {
        var map = new MapData(w, h, 1u);
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        // Alternate Meadow and Forest so hydrology has valid source biomes.
        for (int i = 0; i < map.Biomes.Length; i++)
            map.Biomes[i] = (i % 3 == 0) ? Biome.Forest : Biome.Meadow;
        // Put Start somewhere nominal.
        var sm = new GridCoord(map.MetaWidth / 2, map.MetaHeight / 2);
        map.Biomes[map.MetaIndex(sm)] = Biome.Start;
        map.StartMeta = sm;
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan>();
        map.WaterDepths = new byte[w * h];
        var rng2 = new Rng(seedRng.NextUInt());
        Heightmap.Build(map, ref rng2);
        return map;
    }
}
```

- [ ] **Step 2: Run — fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/Hydrology.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

public static class Hydrology
{
    private const int UndergroundChancePercent = 25;
    private const float SeepChancePerEdge = 0.15f;
    private const float BadwaterChanceSecondary = 0.25f;
    private const float BadwaterChancePerBadland = 0.05f;

    public static void Build(MapData map, ref Rng rng)
    {
        int target = TargetRiverCount(map.Width, map.Height, ref rng);

        var rivers = new List<List<GridCoord>>();
        for (int i = 0; i < target; i++)
        {
            bool isDelta = i > 0 && rng.NextFloat() < 0.4f;
            List<GridCoord>? river = null;
            if (isDelta && rivers.Count > 0)
                river = TraceDeltaBranch(map, rivers[rng.NextRange(0, rivers.Count)], ref rng);
            if (river == null)
                river = TraceFreshRiver(map, ref rng);
            if (river != null) rivers.Add(river);
        }

        foreach (var r in rivers) CarveRiver(map, r);

        // Underground promotion
        foreach (var r in rivers)
        {
            if (rng.NextRange(0, 100) < UndergroundChancePercent)
                PromoteUnderground(map, r, ref rng);
        }

        // Sea/crater fills — detect Sea metacells and flood water depth 3.
        FillSeasAndCraters(map);

        // Source entities
        if (rivers.Count > 0)
        {
            for (int i = 0; i < rivers.Count; i++)
            {
                var head = rivers[i][0];
                bool badwater = (i > 0) && rng.NextFloat() < BadwaterChanceSecondary;
                map.Entities.Add(new PlacedEntity(
                    blueprintKey: badwater ? "WaterSource.Badwater" : "WaterSource.Water",
                    coord: new VoxelCoord(head.X, head.Y, map.TopHeight(head.X, head.Y) + 1),
                    facing: Orientation.North,
                    kind: badwater ? EntityKind.BadwaterSource : EntityKind.WaterSource,
                    param: PickFlowRate(ref rng)));
            }
        }

        // Seeps: roll per biome-transition edge. Limited to Rocky<->others.
        PlaceSeeps(map, ref rng);
        // Badwater seeps in Badland biomes
        PlaceBadlandBadwater(map, ref rng);
    }

    private static int TargetRiverCount(int w, int h, ref Rng rng)
    {
        int target = Math.Max(1, (int)MathF.Round(Math.Max(w, h) / 100f));
        int jitter = rng.NextRange(0, 2) == 0 ? -1 : 0;
        return Math.Max(1, target + jitter);
    }

    // ---------- Source picking ----------

    private static GridCoord? PickSource(MapData map, ref Rng rng)
    {
        // Valid source: voxel cell whose biome is Forest/Rocky, on the uphill
        // third (top 33% by height), not water, not Sea/Crater.
        var heights = new List<(int x, int y, int h)>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.WaterDepths[map.ColumnIndex(x, y)] > 0) continue;
            var b = BiomeAt(map, x, y);
            if (b == Biome.Sea || b == Biome.Crater || b == Biome.Badland || b == Biome.Start)
                continue;
            heights.Add((x, y, map.TopHeight(x, y)));
        }
        if (heights.Count == 0) return null;

        heights.Sort((a, b) => b.h.CompareTo(a.h));
        int topThird = Math.Max(1, heights.Count / 3);
        var candidates = new List<(int x, int y)>();
        for (int i = 0; i < topThird; i++)
        {
            var (x, y, _) = heights[i];
            var b = BiomeAt(map, x, y);
            if (b == Biome.Forest || b == Biome.Rocky) candidates.Add((x, y));
        }
        if (candidates.Count == 0)
        {
            // Fallback: any uphill-third cell.
            for (int i = 0; i < topThird; i++)
                candidates.Add((heights[i].x, heights[i].y));
        }
        if (candidates.Count == 0) return null;
        var pick = candidates[rng.NextRange(0, candidates.Count)];
        return new GridCoord(pick.x, pick.y);
    }

    // ---------- Downhill trace ----------

    private static List<GridCoord>? TraceFreshRiver(MapData map, ref Rng rng)
    {
        var src = PickSource(map, ref rng);
        if (src == null) return null;
        return TraceDownhill(map, src.Value);
    }

    private static List<GridCoord>? TraceDeltaBranch(MapData map, List<GridCoord> parent, ref Rng rng)
    {
        if (parent.Count < 3) return null;
        int splitAt = rng.NextRange(1, parent.Count - 1);
        return TraceDownhill(map, parent[splitAt], parent);
    }

    private static List<GridCoord>? TraceDownhill(MapData map, GridCoord start,
        List<GridCoord>? avoid = null)
    {
        var path = new List<GridCoord> { start };
        var visited = new HashSet<GridCoord> { start };
        if (avoid != null) foreach (var a in avoid) visited.Add(a);
        int maxSteps = map.Width * map.Height;
        for (int step = 0; step < maxSteps; step++)
        {
            var cur = path[path.Count - 1];
            // Map edge = drain reached.
            if (cur.X == 0 || cur.Y == 0 || cur.X == map.Width - 1 || cur.Y == map.Height - 1)
                return path;
            // Sea biome = drain reached.
            if (BiomeAt(map, cur.X, cur.Y) == Biome.Sea) return path;

            int curH = map.TopHeight(cur.X, cur.Y);
            int bestH = int.MaxValue;
            GridCoord? bestN = null;
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (visited.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                if (nh < bestH) { bestH = nh; bestN = nc; }
            }
            if (bestN == null) return null;  // basin; treat as failure for v1
            if (bestH > curH) return null;   // fully enclosed, same issue
            path.Add(bestN.Value);
            visited.Add(bestN.Value);
        }
        return path;
    }

    // ---------- Carving & Underground ----------

    private static void CarveRiver(MapData map, List<GridCoord> path)
    {
        foreach (var c in path)
        {
            var spans = map.Columns[map.ColumnIndex(c.X, c.Y)];
            if (spans.Count == 0) continue;
            var top = spans[spans.Count - 1];
            int newHeight = Math.Max(1, top.Height - 2);
            spans[spans.Count - 1] = new VoxelSpan(top.Bottom, newHeight);
            map.WaterDepths[map.ColumnIndex(c.X, c.Y)] = 3;
        }
    }

    private static void PromoteUnderground(MapData map, List<GridCoord> path, ref Rng rng)
    {
        if (path.Count < 8) return;
        int len = rng.NextRange(4, 9);
        int start = rng.NextRange(1, path.Count - len - 1);
        for (int i = start; i < start + len; i++)
        {
            var c = path[i];
            var spans = map.Columns[map.ColumnIndex(c.X, c.Y)];
            if (spans.Count == 0) continue;
            var top = spans[spans.Count - 1];
            // Re-add a 2-voxel rock roof. Water below remains; WaterDepths stays.
            spans.Add(new VoxelSpan(top.TopExclusive + 2, 2));
        }
    }

    // ---------- Sea / Crater fills ----------

    private static void FillSeasAndCraters(MapData map)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (b != Biome.Sea && b != Biome.Crater) continue;
            byte depth = (b == Biome.Sea) ? (byte)4 : (byte)2;
            int x0 = mx * 8, y0 = my * 8;
            for (int vy = y0; vy < y0 + 8; vy++)
            for (int vx = x0; vx < x0 + 8; vx++)
                map.WaterDepths[map.ColumnIndex(vx, vy)] = depth;
        }
    }

    // ---------- Seeps ----------

    private static void PlaceSeeps(MapData map, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight - 1; my++)
        for (int mx = 0; mx < map.MetaWidth - 1; mx++)
        {
            var a = map.Biomes[map.MetaIndex(mx, my)];
            var b = map.Biomes[map.MetaIndex(mx + 1, my)];
            if (a == Biome.Rocky ^ b == Biome.Rocky)
            {
                if (rng.NextFloat() < SeepChancePerEdge)
                {
                    int vx = (mx + 1) * 8;
                    int vy = my * 8 + rng.NextRange(0, 8);
                    map.Entities.Add(new PlacedEntity(
                        "WaterSource.Seep",
                        new VoxelCoord(vx, vy, map.TopHeight(vx, vy) + 1),
                        Orientation.North, EntityKind.WaterSource, 0.3f));
                }
            }
        }
    }

    private static void PlaceBadlandBadwater(MapData map, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            if (map.Biomes[map.MetaIndex(mx, my)] != Biome.Badland) continue;
            if (rng.NextFloat() >= BadwaterChancePerBadland) continue;
            int vx = mx * 8 + rng.NextRange(1, 7);
            int vy = my * 8 + rng.NextRange(1, 7);
            map.Entities.Add(new PlacedEntity(
                "WaterSource.Badwater",
                new VoxelCoord(vx, vy, map.TopHeight(vx, vy) + 1),
                Orientation.North, EntityKind.BadwaterSource, 0.5f));
        }
    }

    // ---------- Helpers ----------

    private static float PickFlowRate(ref Rng rng)
    {
        float r = rng.NextFloat();
        if (r < 0.33f) return 0.5f;    // trickle
        if (r < 0.67f) return 1.2f;    // stream
        return 2.5f;                   // strong
    }

    private static Biome BiomeAt(MapData map, int x, int y) =>
        map.Biomes[map.MetaIndex(x / 8, y / 8)];

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: Hydrology tests pass. All prior tests still pass.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/Hydrology.cs MapGen/MapGen.Core.Tests/HydrologyTests.cs
git commit -m "MapGen: Hydrology — river count, source pick, downhill trace, underground, seeps"
```

---

## Task 10: Poisson-disk sampler

**Files:**
- Create: `MapGen/MapGen.Core/PoissonDisk.cs`
- Create: `MapGen/MapGen.Core.Tests/PoissonDiskTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/PoissonDiskTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class PoissonDiskTests
{
    [Fact]
    public void Sample_respects_min_spacing()
    {
        var rng = new Rng(7u);
        var samples = PoissonDisk.Sample(width: 50, height: 50, minDistance: 5f, ref rng);
        for (int i = 0; i < samples.Count; i++)
        for (int j = i + 1; j < samples.Count; j++)
        {
            int dx = samples[i].X - samples[j].X;
            int dy = samples[i].Y - samples[j].Y;
            float d = System.MathF.Sqrt(dx * dx + dy * dy);
            Assert.True(d >= 5f - 0.001f, $"Points {samples[i]} and {samples[j]} too close: {d}");
        }
    }

    [Fact]
    public void Sample_is_deterministic()
    {
        var r1 = new Rng(1u);
        var s1 = PoissonDisk.Sample(30, 30, 3f, ref r1);
        var r2 = new Rng(1u);
        var s2 = PoissonDisk.Sample(30, 30, 3f, ref r2);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Sample_produces_nontrivial_count()
    {
        var rng = new Rng(2u);
        var samples = PoissonDisk.Sample(100, 100, 5f, ref rng);
        Assert.True(samples.Count > 20, $"Expected >20 samples, got {samples.Count}");
    }
}
```

- [ ] **Step 2: Run — fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/PoissonDisk.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

/// Bridson's fast Poisson-disk sampling (2D). Returns integer coords
/// with minimum Euclidean distance `minDistance` between any pair.
public static class PoissonDisk
{
    public static List<GridCoord> Sample(int width, int height, float minDistance, ref Rng rng)
    {
        if (minDistance <= 0) throw new ArgumentOutOfRangeException(nameof(minDistance));

        float cellSize = minDistance / MathF.Sqrt(2f);
        int gw = (int)MathF.Ceiling(width / cellSize);
        int gh = (int)MathF.Ceiling(height / cellSize);
        var grid = new int[gw * gh];
        for (int i = 0; i < grid.Length; i++) grid[i] = -1;

        var points = new List<(float X, float Y)>();
        var active = new List<int>();

        // Seed point — center of map.
        float sx = width / 2f, sy = height / 2f;
        points.Add((sx, sy));
        active.Add(0);
        grid[(int)(sy / cellSize) * gw + (int)(sx / cellSize)] = 0;

        int maxAttempts = 30;
        while (active.Count > 0)
        {
            int activeIdx = rng.NextRange(0, active.Count);
            int pIdx = active[activeIdx];
            var (px, py) = points[pIdx];
            bool found = false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float ang = rng.NextFloat() * MathF.PI * 2f;
                float r = minDistance * (1f + rng.NextFloat());
                float nx = px + r * MathF.Cos(ang);
                float ny = py + r * MathF.Sin(ang);
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int gx = (int)(nx / cellSize);
                int gy = (int)(ny / cellSize);
                bool ok = true;
                for (int dy = -2; dy <= 2 && ok; dy++)
                for (int dx = -2; dx <= 2 && ok; dx++)
                {
                    int cx = gx + dx, cy = gy + dy;
                    if (cx < 0 || cy < 0 || cx >= gw || cy >= gh) continue;
                    int neighborIdx = grid[cy * gw + cx];
                    if (neighborIdx == -1) continue;
                    var (ox, oy) = points[neighborIdx];
                    float ddx = nx - ox, ddy = ny - oy;
                    if (ddx * ddx + ddy * ddy < minDistance * minDistance) ok = false;
                }
                if (ok)
                {
                    points.Add((nx, ny));
                    int newIdx = points.Count - 1;
                    grid[gy * gw + gx] = newIdx;
                    active.Add(newIdx);
                    found = true;
                    break;
                }
            }
            if (!found) active.RemoveAt(activeIdx);
        }

        var result = new List<GridCoord>(points.Count);
        foreach (var (x, y) in points) result.Add(new GridCoord((int)x, (int)y));
        return result;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/PoissonDisk.cs MapGen/MapGen.Core.Tests/PoissonDiskTests.cs
git commit -m "MapGen: Poisson-disk sampler (Bridson)"
```

---

## Task 11: Catalog DTOs + JSON loader + default data

**Files:**
- Create: `MapGen/MapGen.Core/Catalog.cs`
- Create: `MapGen/MapGen.Catalogs/Trees.json`
- Create: `MapGen/MapGen.Catalogs/Resources.json`
- Create: `MapGen/MapGen.Catalogs/Thorns.json`
- Create: `MapGen/MapGen.Catalogs/Ruins.json`
- Create: `MapGen/MapGen.Catalogs/BlockObjects.json`

- [ ] **Step 1: Write `MapGen/MapGen.Core/Catalog.cs`**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MapGen;

public enum Faction : byte { Folktails, IronTeeth, Both }

public sealed class CatalogEntry
{
    public string Key { get; set; } = "";       // internal id (e.g. "maple")
    public string BlueprintKey { get; set; } = ""; // resolved at mod load time later
    public Faction Faction { get; set; } = Faction.Both;
    public float Weight { get; set; } = 1f;
}

public sealed class RuinCatalogEntry : CatalogEntry
{
    public int FootprintW { get; set; } = 1;
    public int FootprintH { get; set; } = 1;
}

public sealed class Catalog
{
    public IReadOnlyList<CatalogEntry> Trees { get; set; } = new List<CatalogEntry>();
    public IReadOnlyList<CatalogEntry> Resources { get; set; } = new List<CatalogEntry>();
    public IReadOnlyList<CatalogEntry> Thorns { get; set; } = new List<CatalogEntry>();
    public IReadOnlyList<RuinCatalogEntry> Ruins { get; set; } = new List<RuinCatalogEntry>();
    public Dictionary<string, CatalogEntry> BlockObjects { get; set; } = new();

    public static Catalog LoadFromDirectory(string dir)
    {
        var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cat = new Catalog
        {
            Trees = Read<List<CatalogEntry>>(Path.Combine(dir, "Trees.json"), opt),
            Resources = Read<List<CatalogEntry>>(Path.Combine(dir, "Resources.json"), opt),
            Thorns = Read<List<CatalogEntry>>(Path.Combine(dir, "Thorns.json"), opt),
            Ruins = Read<List<RuinCatalogEntry>>(Path.Combine(dir, "Ruins.json"), opt),
            BlockObjects = Read<Dictionary<string, CatalogEntry>>(Path.Combine(dir, "BlockObjects.json"), opt),
        };
        return cat;
    }

    private static T Read<T>(string path, JsonSerializerOptions opt) where T : new()
    {
        if (!File.Exists(path)) return new T();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), opt) ?? new T();
    }
}
```

- [ ] **Step 2: Write `MapGen/MapGen.Catalogs/Trees.json`**

```json
[
  { "key": "maple", "blueprintKey": "NaturalResource.Maple.Folktails", "faction": "Folktails", "weight": 1.0 },
  { "key": "pine", "blueprintKey": "NaturalResource.Pine.Folktails", "faction": "Both", "weight": 1.0 },
  { "key": "birch", "blueprintKey": "NaturalResource.Birch.IronTeeth", "faction": "IronTeeth", "weight": 1.0 },
  { "key": "dead_stump", "blueprintKey": "NaturalResource.DeadTree", "faction": "Both", "weight": 0.5 }
]
```

- [ ] **Step 3: Write `MapGen/MapGen.Catalogs/Resources.json`**

```json
[
  { "key": "berries", "blueprintKey": "NaturalResource.Berries.Folktails", "faction": "Folktails", "weight": 1.0 },
  { "key": "blueberries", "blueprintKey": "NaturalResource.Blueberries.IronTeeth", "faction": "IronTeeth", "weight": 1.0 },
  { "key": "carrots", "blueprintKey": "NaturalResource.Carrots", "faction": "Both", "weight": 0.7 },
  { "key": "chestnuts", "blueprintKey": "NaturalResource.Chestnuts", "faction": "Both", "weight": 0.5 },
  { "key": "mushrooms", "blueprintKey": "NaturalResource.Mushrooms", "faction": "Both", "weight": 0.5 },
  { "key": "cactus", "blueprintKey": "NaturalResource.Cactus", "faction": "Both", "weight": 0.3 },
  { "key": "dandelion", "blueprintKey": "NaturalResource.Dandelion", "faction": "Both", "weight": 0.2 }
]
```

- [ ] **Step 4: Write `MapGen/MapGen.Catalogs/Thorns.json`**

```json
[
  { "key": "thorns", "blueprintKey": "NaturalResource.Thorns", "faction": "Both", "weight": 1.0 }
]
```

- [ ] **Step 5: Write `MapGen/MapGen.Catalogs/Ruins.json`**

```json
[
  { "key": "ruined_shelter", "blueprintKey": "Ruin.Shelter", "faction": "Both", "weight": 1.0, "footprintW": 1, "footprintH": 1 },
  { "key": "collapsed_wall", "blueprintKey": "Ruin.Wall", "faction": "Both", "weight": 1.0, "footprintW": 2, "footprintH": 1 },
  { "key": "broken_platform", "blueprintKey": "Ruin.Platform", "faction": "Both", "weight": 0.7, "footprintW": 2, "footprintH": 2 },
  { "key": "ruined_pipe", "blueprintKey": "Ruin.Pipe", "faction": "Both", "weight": 0.5, "footprintW": 1, "footprintH": 1 }
]
```

- [ ] **Step 6: Write `MapGen/MapGen.Catalogs/BlockObjects.json`**

```json
{
  "slope": { "key": "slope", "blueprintKey": "BlockObject.Slope", "faction": "Both", "weight": 1.0 },
  "unstable_core": { "key": "unstable_core", "blueprintKey": "BlockObject.UnstableCore", "faction": "Both", "weight": 1.0 },
  "geothermal_vent": { "key": "geothermal_vent", "blueprintKey": "BlockObject.GeothermalVent", "faction": "Both", "weight": 1.0 },
  "relic": { "key": "relic", "blueprintKey": "BlockObject.Relic", "faction": "Both", "weight": 1.0 },
  "blockage": { "key": "blockage", "blueprintKey": "BlockObject.Blockage", "faction": "Both", "weight": 1.0 },
  "start_marker": { "key": "start_marker", "blueprintKey": "BlockObject.StartingLocation", "faction": "Both", "weight": 1.0 }
}
```

- [ ] **Step 7: Build — verify compiles**

Run: `cd MapGen && dotnet build`

- [ ] **Step 8: Commit**

```bash
git add MapGen/MapGen.Core/Catalog.cs MapGen/MapGen.Catalogs/
git commit -m "MapGen: Catalog DTOs + default Trees/Resources/Ruins/BlockObjects JSON"
```

**Note on blueprint keys:** the `blueprintKey` values above are placeholders. They'll be resolved to actual Timberborn blueprint IDs during the mod-wrapper plan (Plan 3). The algorithm just propagates the keys through — nothing in MapGen.Core cares about them being "real."

---

## Task 12: Overlays — Trees + Resources + Thorns

**Files:**
- Create: `MapGen/MapGen.Core/Overlays.cs`
- Create: `MapGen/MapGen.Core.Tests/OverlaysTests.cs`

- [ ] **Step 1: Write the failing tests (partial — only trees & resources for this task)**

Write `MapGen/MapGen.Core.Tests/OverlaysTests.cs`:

```csharp
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
        Assert.True(keys.Count >= 2, $"Expected ≥2 resource species, got {keys.Count}");
    }

    [Fact]
    public void Thorns_appear_in_badland_more_than_meadow()
    {
        // This is a stochastic test; run over several seeds.
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
            // Half Badland, half Meadow
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
```

- [ ] **Step 2: Run — fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/Overlays.cs` (trees/resources/thorns only; other placers in next tasks)**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

public static class Overlays
{
    // Per-biome tree placement params (spec §5.3).
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

    private const int MetaSize = 8;

    public static void PlaceTrees(MapData map, Catalog catalog, ref Rng rng)
    {
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (!TreeParamsByBiome.TryGetValue(b, out var tp)) continue;

            // Local Poisson-disk inside the 8×8 metacell.
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

    // Resource placement: per-biome density table (spec §5.4).
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
            // Start biome: guaranteed 10 berries — handled separately below.
            if (b == Biome.Start) continue;
            if (!ResourceParamsByBiome.TryGetValue(b, out var rp)) continue;

            var local = PoissonDisk.Sample(MetaSize, MetaSize, rp.spacing, ref rng);
            foreach (var p in local)
            {
                if (rng.NextFloat() > rp.density * MetaSize) continue;  // density over area
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

        // Start metacell guarantee: ≥10 berries (mix both factions).
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
                // Alternate Folktails / IronTeeth preferred species.
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

    // Thorns (spec §5.3).
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

    // ---------- shared helpers ----------

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
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: Overlays tests pass.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/Overlays.cs MapGen/MapGen.Core.Tests/OverlaysTests.cs
git commit -m "MapGen: Overlays — Trees, Resources (with Start guarantee), Thorns"
```

---

## Task 13: Overlays — Ruins + Blockages + Relics

**Files:**
- Modify: `MapGen/MapGen.Core/Overlays.cs` (add methods)

- [ ] **Step 1: Append to `MapGen/MapGen.Core/Overlays.cs`** — add below the thorns section, inside the Overlays class:

```csharp
    // ---------- Ruins (spec §5.5) ----------

    // Density per 256 cells per biome (not per metacell).
    private static readonly Dictionary<Biome, float> RuinDensityBySector = new()
    {
        { Biome.Badland, 8f },
        { Biome.Rocky,   3f },
        { Biome.Meadow,  2f },
        { Biome.Forest,  1f },
    };

    public static void PlaceRuins(MapData map, Catalog catalog, ref Rng rng)
    {
        if (catalog.Ruins.Count == 0) return;

        var forbidden = BuildStartExclusionSet(map);

        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            var b = map.Biomes[map.MetaIndex(mx, my)];
            if (!RuinDensityBySector.TryGetValue(b, out var density)) continue;
            if (forbidden.Contains(new GridCoord(mx, my))) continue;

            // Metacell has 64 cells; density per 256 cells → expected density * 64/256.
            float expected = density * 64f / 256f;
            int count = (int)MathF.Round(expected + (rng.NextFloat() - 0.5f));
            for (int i = 0; i < count; i++)
            {
                int vx = mx * MetaSize + rng.NextRange(0, MetaSize);
                int vy = my * MetaSize + rng.NextRange(0, MetaSize);
                if (!IsPlaceableCell(map, vx, vy)) continue;
                var entry = PickRuin(catalog.Ruins, ref rng);
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z),
                    RandomOrientation(ref rng), EntityKind.Ruin));
            }
        }
    }

    // ---------- Blockages ----------

    public static void PlaceBlockages(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("blockage", out var entry)) return;
        int ruinCount = 0;
        foreach (var e in map.Entities) if (e.Kind == EntityKind.Ruin) ruinCount++;
        int blockCount = Math.Max(0, ruinCount / 3);

        // Place near existing ruins or at narrow chokepoints.
        var ruinPositions = new List<GridCoord>();
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.Ruin)
                ruinPositions.Add(new GridCoord(e.Coord.X, e.Coord.Y));

        for (int i = 0; i < blockCount && ruinPositions.Count > 0; i++)
        {
            var anchor = ruinPositions[rng.NextRange(0, ruinPositions.Count)];
            int vx = anchor.X + rng.NextRange(-2, 3);
            int vy = anchor.Y + rng.NextRange(-2, 3);
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z),
                RandomOrientation(ref rng), EntityKind.Blockage));
        }
    }

    // ---------- Relics ----------

    public static void PlaceRelics(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("relic", out var entry)) return;
        int area = map.Width * map.Height;
        int count = (int)MathF.Round(area / 25000f) + 1;
        var ruinPositions = new List<GridCoord>();
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.Ruin)
                ruinPositions.Add(new GridCoord(e.Coord.X, e.Coord.Y));

        for (int i = 0; i < count; i++)
        {
            int vx, vy;
            if (ruinPositions.Count > 0 && rng.NextFloat() < 0.7f)
            {
                var anchor = ruinPositions[rng.NextRange(0, ruinPositions.Count)];
                vx = anchor.X + rng.NextRange(-3, 4);
                vy = anchor.Y + rng.NextRange(-3, 4);
            }
            else
            {
                vx = rng.NextRange(0, map.Width);
                vy = rng.NextRange(0, map.Height);
            }
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Relic));
        }
    }

    // ---------- Shared helpers (adding to existing class) ----------

    private static HashSet<GridCoord> BuildStartExclusionSet(MapData map)
    {
        var ex = new HashSet<GridCoord>();
        if (!map.StartMeta.HasValue) return ex;
        var sm = map.StartMeta.Value;
        // Exclude Start metacell plus 4 direct neighbors (spec §5 Start biome rules).
        ex.Add(sm);
        if (sm.X > 0) ex.Add(new GridCoord(sm.X - 1, sm.Y));
        if (sm.X + 1 < map.MetaWidth) ex.Add(new GridCoord(sm.X + 1, sm.Y));
        if (sm.Y > 0) ex.Add(new GridCoord(sm.X, sm.Y - 1));
        if (sm.Y + 1 < map.MetaHeight) ex.Add(new GridCoord(sm.X, sm.Y + 1));
        return ex;
    }

    private static RuinCatalogEntry PickRuin(IReadOnlyList<RuinCatalogEntry> ruins, ref Rng rng)
    {
        var weights = new float[ruins.Count];
        for (int i = 0; i < ruins.Count; i++) weights[i] = ruins[i].Weight;
        return ruins[rng.WeightedPick(weights)];
    }

    private static Orientation RandomOrientation(ref Rng rng) =>
        (Orientation)rng.NextRange(0, 4);
```

- [ ] **Step 2: Build — verify compiles**

Run: `cd MapGen && dotnet build`

- [ ] **Step 3: Commit**

```bash
git add MapGen/MapGen.Core/Overlays.cs
git commit -m "MapGen: Overlays — Ruins + Blockages + Relics"
```

---

## Task 14: Overlays — Unstable cores + Geothermal vents

**Files:**
- Modify: `MapGen/MapGen.Core/Overlays.cs` (add methods)

- [ ] **Step 1: Append to the `Overlays` class in `MapGen/MapGen.Core/Overlays.cs`:**

```csharp
    // ---------- Unstable cores (spec §5.7) ----------

    public static void PlaceUnstableCores(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("unstable_core", out var entry)) return;
        int area = map.Width * map.Height;
        int count = (int)MathF.Round(area / 20000f) + rng.NextRange(-1, 2);
        if (count <= 0) return;

        int attempts = count * 20;
        int placed = 0;
        const float MinSpacing = 20f;
        var positions = new List<GridCoord>();
        for (int a = 0; a < attempts && placed < count; a++)
        {
            int vx = rng.NextRange(0, map.Width);
            int vy = rng.NextRange(0, map.Height);
            var b = map.Biomes[map.MetaIndex(vx / 8, vy / 8)];
            if (b != Biome.Rocky && b != Biome.Badland) continue;
            if (WithinOfStart(map, vx, vy, radius: 15)) continue;
            if (TooClose(positions, vx, vy, MinSpacing)) continue;
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.UnstableCore));
            positions.Add(new GridCoord(vx, vy));
            placed++;
        }
    }

    // ---------- Geothermal vents ----------

    public static void PlaceGeothermalVents(MapData map, Catalog catalog, ref Rng rng)
    {
        if (!catalog.BlockObjects.TryGetValue("geothermal_vent", out var entry)) return;
        int area = map.Width * map.Height;
        int count = (int)MathF.Round(area / 30000f) + rng.NextRange(-1, 2);
        if (count <= 0) return;

        int attempts = count * 20;
        int placed = 0;
        const float MinSpacing = 25f;
        var positions = new List<GridCoord>();
        for (int a = 0; a < attempts && placed < count; a++)
        {
            int vx = rng.NextRange(0, map.Width);
            int vy = rng.NextRange(0, map.Height);
            var b = map.Biomes[map.MetaIndex(vx / 8, vy / 8)];
            if (b != Biome.Badland && b != Biome.Rocky) continue;
            if (WithinOfStart(map, vx, vy, radius: 20)) continue;
            if (TooClose(positions, vx, vy, MinSpacing)) continue;
            if (!IsPlaceableCell(map, vx, vy)) continue;
            int z = map.TopHeight(vx, vy) + 1;
            map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.GeothermalVent));
            positions.Add(new GridCoord(vx, vy));
            placed++;
        }
    }

    private static bool WithinOfStart(MapData map, int vx, int vy, int radius)
    {
        if (!map.StartMeta.HasValue) return false;
        var sm = map.StartMeta.Value;
        int sx = sm.X * MetaSize + MetaSize / 2;
        int sy = sm.Y * MetaSize + MetaSize / 2;
        int dx = vx - sx, dy = vy - sy;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool TooClose(List<GridCoord> positions, int vx, int vy, float minSpacing)
    {
        float sq = minSpacing * minSpacing;
        foreach (var p in positions)
        {
            int dx = vx - p.X, dy = vy - p.Y;
            if (dx * dx + dy * dy < sq) return true;
        }
        return false;
    }
```

- [ ] **Step 2: Build**

Run: `cd MapGen && dotnet build`

- [ ] **Step 3: Commit**

```bash
git add MapGen/MapGen.Core/Overlays.cs
git commit -m "MapGen: Overlays — UnstableCores + GeothermalVents"
```

---

## Task 15: Start marker emission

**Files:**
- Modify: `MapGen/MapGen.Core/Overlays.cs`

- [ ] **Step 1: Append a `PlaceStartMarker` method to Overlays:**

```csharp
    // ---------- Start marker ----------

    public static void PlaceStartMarker(MapData map, Catalog catalog)
    {
        if (!map.StartMeta.HasValue) return;
        if (!catalog.BlockObjects.TryGetValue("start_marker", out var entry)) return;
        var sm = map.StartMeta.Value;
        int vx = sm.X * MetaSize + MetaSize / 2;
        int vy = sm.Y * MetaSize + MetaSize / 2;
        int z = map.TopHeight(vx, vy) + 1;
        map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
            new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.StartMarker));
    }
```

- [ ] **Step 2: Build**

Run: `cd MapGen && dotnet build`

- [ ] **Step 3: Commit**

```bash
git add MapGen/MapGen.Core/Overlays.cs
git commit -m "MapGen: Start marker placement"
```

---

## Task 16: Access validation — BFS + top-up + slope repair

**Files:**
- Create: `MapGen/MapGen.Core/AccessValidation.cs`
- Create: `MapGen/MapGen.Core.Tests/AccessValidationTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/AccessValidationTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class AccessValidationTests
{
    [Fact]
    public void BFS_reaches_flat_cells_only()
    {
        var map = FlatMapWithCliff();
        var start = new VoxelCoord(8, 8, 5);
        var reach = AccessValidation.FloodFillReachable(map, start);
        // Cells at z=5 within the flat zone should be reachable.
        Assert.Contains(new GridCoord(10, 10), reach.Cells);
        // Cells behind the cliff (z=15) should NOT.
        Assert.DoesNotContain(new GridCoord(20, 20), reach.Cells);
    }

    private static MapData FlatMapWithCliff()
    {
        var map = new MapData(32, 32, 1u);
        map.MetaWidth = 4; map.MetaHeight = 4;
        map.Biomes = new Biome[16];
        for (int i = 0; i < 16; i++) map.Biomes[i] = Biome.Meadow;
        map.Biomes[map.MetaIndex(1, 1)] = Biome.Start;
        map.StartMeta = new GridCoord(1, 1);
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[32 * 32];
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            int h = (x >= 16 && y >= 16) ? 15 : 5;  // cliff east-south
            map.Columns[map.ColumnIndex(x, y)] =
                new System.Collections.Generic.List<VoxelSpan> { new VoxelSpan(0, h) };
        }
        map.WaterDepths = new byte[32 * 32];
        return map;
    }
}
```

- [ ] **Step 2: Run — fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/AccessValidation.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

public sealed class ReachabilityReport
{
    public HashSet<GridCoord> Cells { get; } = new();
    public int TreeCount { get; set; }
    public int ResourceCount { get; set; }
    public int FolktailsFood { get; set; }
    public int IronTeethFood { get; set; }
    public int WaterAccessCount { get; set; }

    public bool MeetsMinimums =>
        TreeCount >= 30 && ResourceCount >= 15 &&
        FolktailsFood >= 7 && IronTeethFood >= 7 &&
        WaterAccessCount >= 1;
}

public static class AccessValidation
{
    /// BFS from the start voxel using same-height + water-edge rules (spec §6).
    public static ReachabilityReport FloodFillReachable(MapData map, VoxelCoord start)
    {
        var report = new ReachabilityReport();
        var queue = new Queue<GridCoord>();
        var startCell = new GridCoord(start.X, start.Y);
        report.Cells.Add(startCell);
        queue.Enqueue(startCell);
        int startHeight = map.TopHeight(start.X, start.Y);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int curH = map.TopHeight(cur.X, cur.Y);
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (report.Cells.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                // Flat edge: same height as current.
                if (nh != curH)
                {
                    // Water-edge: neighbor is water of ≤2 depth and current height matches water surface.
                    byte wd = map.WaterDepths[map.ColumnIndex(nx, ny)];
                    if (wd > 0 && wd <= 2 && nh + wd == curH + 1)
                    {
                        // Drinking access — counted but don't continue flood through.
                        report.WaterAccessCount++;
                        continue;
                    }
                    continue;
                }
                report.Cells.Add(nc);
                queue.Enqueue(nc);
            }
        }

        // Count overlays in reachable set.
        CountOverlays(map, report);
        return report;
    }

    private static void CountOverlays(MapData map, ReachabilityReport report)
    {
        foreach (var e in map.Entities)
        {
            var cell = new GridCoord(e.Coord.X, e.Coord.Y);
            if (!report.Cells.Contains(cell)) continue;
            if (e.Kind == EntityKind.Tree) report.TreeCount++;
            else if (e.Kind == EntityKind.Resource)
            {
                report.ResourceCount++;
                // Heuristic: blueprint keys containing "Folktails" count as Folktails,
                // "IronTeeth" count as IronTeeth, others count as Both.
                if (e.BlueprintKey.Contains("Folktails"))
                {
                    report.FolktailsFood++;
                    report.IronTeethFood++;  // "Both" in catalog — count for both
                }
                else if (e.BlueprintKey.Contains("IronTeeth"))
                {
                    report.IronTeethFood++;
                }
                else
                {
                    report.FolktailsFood++;
                    report.IronTeethFood++;
                }
            }
        }
    }

    /// Top-up repair: plant additional trees/resources in reachable cells
    /// until the minimums are met. Runs after initial placement.
    public static void TopUp(MapData map, Catalog catalog, ReachabilityReport report, ref Rng rng)
    {
        if (report.MeetsMinimums) return;
        var empty = new List<GridCoord>();
        foreach (var c in report.Cells)
        {
            bool occupied = false;
            foreach (var e in map.Entities)
            {
                if (e.Coord.X == c.X && e.Coord.Y == c.Y) { occupied = true; break; }
            }
            if (!occupied && Overlays.IsPlaceableCell(map, c.X, c.Y)) empty.Add(c);
        }

        while (report.TreeCount < 30 && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            empty.Remove(c);
            var tree = PickFirstFactionMixed(catalog.Trees, ref rng);
            if (tree == null) break;
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(tree.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Tree));
            report.TreeCount++;
        }
        while ((report.FolktailsFood < 7 || report.IronTeethFood < 7 || report.ResourceCount < 15)
               && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            empty.Remove(c);
            bool needFolk = report.FolktailsFood < 7;
            CatalogEntry? res = null;
            foreach (var e in catalog.Resources)
            {
                if (needFolk && (e.Faction == Faction.Folktails || e.Faction == Faction.Both))
                { res = e; break; }
                if (!needFolk && (e.Faction == Faction.IronTeeth || e.Faction == Faction.Both))
                { res = e; break; }
            }
            if (res == null) break;
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(res.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Resource));
            report.ResourceCount++;
            if (res.Faction == Faction.Folktails || res.Faction == Faction.Both) report.FolktailsFood++;
            if (res.Faction == Faction.IronTeeth || res.Faction == Faction.Both) report.IronTeethFood++;
        }
    }

    private static CatalogEntry? PickFirstFactionMixed(IReadOnlyList<CatalogEntry> src, ref Rng rng)
    {
        if (src.Count == 0) return null;
        return src[rng.NextRange(0, src.Count)];
    }

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/AccessValidation.cs MapGen/MapGen.Core.Tests/AccessValidationTests.cs
git commit -m "MapGen: AccessValidation — BFS + top-up repair pass"
```

**Note:** Slope-placement repair (Repair pass B) is deferred — it requires knowledge of the slope block-object's exact emission format which we won't have until Plan 2's reference-sample harvest. For this plan, maps that can't be topped up within the flat zone get rejected and retried with `seed+1`. Once the slope format is known, Plan 3 can add the repair pass to `AccessValidation`.

---

## Task 17: MapGenerator orchestrator

**Files:**
- Create: `MapGen/MapGen.Core/MapGenerator.cs`
- Create: `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`:

```csharp
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class MapGeneratorTests
{
    [Fact]
    public void Generate_deterministic_same_seed()
    {
        var config = new GenerationConfig { Width = 128, Height = 128, Seed = 42 };
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
        var config = new GenerationConfig { Width = 60, Height = 60, Seed = 1 };
        var gen = new MapGenerator(EmptyCatalog());
        Assert.Throws<System.ArgumentException>(() => gen.Generate(config));
    }

    [Fact]
    public void Generate_small_map_completes()
    {
        var config = new GenerationConfig { Width = 64, Height = 64, Seed = 1 };
        var gen = new MapGenerator(EmptyCatalog());
        var result = gen.Generate(config);
        Assert.Equal(GenerationStatus.Success, result.Status);
        Assert.NotNull(result.Map);
        Assert.Equal(64, result.Map!.Width);
    }

    private static Catalog EmptyCatalog()
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
```

- [ ] **Step 2: Run — fails**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`

- [ ] **Step 3: Write `MapGen/MapGen.Core/MapGenerator.cs`**

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
                log.Add($"seed={seed}: minimums unmet (trees={report.TreeCount} food={report.ResourceCount})");
                return null;
            }
        }

        log.Add($"seed={seed}: success (entities={map.Entities.Count}, reachable={report.Cells.Count})");
        return map;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: All MapGenerator tests pass plus every prior test.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Core/MapGenerator.cs MapGen/MapGen.Core.Tests/MapGeneratorTests.cs
git commit -m "MapGen: MapGenerator orchestrator with retry budget"
```

---

## Task 18: Seed sweep regression test

**Files:**
- Modify: `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`

- [ ] **Step 1: Append seed-sweep test** at the end of the `MapGeneratorTests` class:

```csharp
    [Fact]
    public void SeedSweep_100_seeds_on_128x128_all_succeed()
    {
        var gen = new MapGenerator(EmptyCatalog());
        int successes = 0;
        for (uint s = 0; s < 100; s++)
        {
            var result = gen.Generate(new GenerationConfig { Width = 128, Height = 128, Seed = s });
            if (result.Status == GenerationStatus.Success) successes++;
        }
        Assert.True(successes >= 95, $"Only {successes}/100 seeds succeeded.");
    }

    [Fact]
    public void SeedSweep_various_sizes()
    {
        var gen = new MapGenerator(EmptyCatalog());
        int[] sizes = { 64, 128, 192, 256 };
        foreach (var size in sizes)
        {
            var result = gen.Generate(new GenerationConfig { Width = size, Height = size, Seed = 1 });
            Assert.Equal(GenerationStatus.Success, result.Status);
        }
    }
```

- [ ] **Step 2: Run tests**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorTests"`
Expected: All tests pass. Sweep test takes a few seconds.

- [ ] **Step 3: Commit**

```bash
git add MapGen/MapGen.Core.Tests/MapGeneratorTests.cs
git commit -m "MapGen: 100-seed regression sweep"
```

---

## Task 19: Preview tool — SkiaSharp renderer

**Files:**
- Create: `MapGen/MapGen.Preview/Renderer.cs`

- [ ] **Step 1: Write `MapGen/MapGen.Preview/Renderer.cs`**

```csharp
using MapGen;
using SkiaSharp;

namespace MapGen.Preview;

public static class Renderer
{
    private static readonly SKColor[] BiomeColors =
    {
        new(120, 180,  90, 255),  // Meadow
        new( 40, 100,  60, 255),  // Forest
        new(180, 150, 100, 255),  // Badland
        new(120, 110, 100, 255),  // Rocky
        new( 40,  80, 160, 255),  // Sea
        new(140, 100, 160, 255),  // Crater
        new(240, 210, 140, 255),  // Start
    };

    public static void RenderToPng(MapData map, string outputPath, int scale = 4)
    {
        int w = map.Width * scale;
        int h = map.Height * scale;
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);

        using var biomePaint = new SKPaint();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var b = map.Biomes[map.MetaIndex(x / 8, y / 8)];
            var baseColor = BiomeColors[(int)b];
            int height = map.TopHeight(x, y);
            float shade = 0.5f + 0.5f * (height / 20f);
            shade = System.Math.Clamp(shade, 0.4f, 1f);
            var c = new SKColor(
                (byte)(baseColor.Red * shade),
                (byte)(baseColor.Green * shade),
                (byte)(baseColor.Blue * shade));
            biomePaint.Color = c;
            canvas.DrawRect(x * scale, y * scale, scale, scale, biomePaint);
        }

        // Water overlay (blue).
        using var waterPaint = new SKPaint { Color = new SKColor(60, 120, 200, 180) };
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.WaterDepths[map.ColumnIndex(x, y)] > 0)
                canvas.DrawRect(x * scale, y * scale, scale, scale, waterPaint);
        }

        // Entities.
        foreach (var e in map.Entities)
        {
            var color = e.Kind switch
            {
                EntityKind.Tree => new SKColor(20, 80, 30),
                EntityKind.Resource => new SKColor(255, 100, 100),
                EntityKind.Thorn => new SKColor(120, 50, 50),
                EntityKind.Ruin => new SKColor(120, 100, 80),
                EntityKind.Blockage => new SKColor(80, 60, 40),
                EntityKind.Relic => new SKColor(255, 220, 100),
                EntityKind.UnstableCore => new SKColor(255, 60, 60),
                EntityKind.GeothermalVent => new SKColor(255, 150, 50),
                EntityKind.WaterSource => new SKColor(100, 180, 255),
                EntityKind.BadwaterSource => new SKColor(200, 100, 220),
                EntityKind.Slope => new SKColor(200, 200, 200),
                EntityKind.StartMarker => new SKColor(255, 255, 0),
                _ => SKColors.Magenta,
            };
            using var p = new SKPaint { Color = color };
            int r = e.Kind == EntityKind.StartMarker ? scale * 3 : System.Math.Max(1, scale / 2);
            canvas.DrawCircle(e.Coord.X * scale + scale / 2, e.Coord.Y * scale + scale / 2, r, p);
        }

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = System.IO.File.Create(outputPath);
        data.SaveTo(fs);
    }
}
```

- [ ] **Step 2: Build — verify compiles**

Run: `cd MapGen && dotnet build MapGen.Preview/MapGen.Preview.csproj`

- [ ] **Step 3: Commit**

```bash
git add MapGen/MapGen.Preview/Renderer.cs
git commit -m "MapGen: Preview tool — SkiaSharp renderer"
```

---

## Task 20: Preview tool — CLI

**Files:**
- Create: `MapGen/MapGen.Preview/Program.cs`

- [ ] **Step 1: Write `MapGen/MapGen.Preview/Program.cs`**

```csharp
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
                Console.WriteLine($"OK  seed={s} (actual={result.ActualSeedUsed}) → {outPath}");
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
        // Minimal built-in catalog for quick testing when no --catalog path is given.
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
```

- [ ] **Step 2: Build — verify compiles**

Run: `cd MapGen && dotnet build MapGen.Preview/MapGen.Preview.csproj`

- [ ] **Step 3: Manual smoke test — generate 5 previews**

Run: `cd MapGen && dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- --seed-range 1-5 --width 128 --height 128 --out ../dist/previews`
Expected: 5 PNG files written to `dist/previews/`, each named `seed-<N>-128x128.png`. Visually inspect — look for: biomes present, rivers visible as blue lines reaching map edges, Start marker (yellow) on Meadow/Forest, resources/trees scattered.

- [ ] **Step 4: Batch-render for tuning check**

Run: `cd MapGen && dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- --seed-range 1-20 --out ../dist/previews-sweep`
Expected: 20 maps rendered. Open the dir, scroll through. Rivers should always reach an edge. Start marker always in a Meadow/Forest pocket. Sea/crater features appear on some.

- [ ] **Step 5: Commit**

```bash
git add MapGen/MapGen.Preview/Program.cs
git commit -m "MapGen: Preview CLI — seed/range/size args, batch render"
```

---

## Task 21: Catalog loader integration + final smoke

**Files:**
- None created (verification task).

- [ ] **Step 1: Generate via shipped Catalogs**

Run: `cd MapGen && dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- --seed-range 1-3 --catalog MapGen.Catalogs --out ../dist/catalog-test`
Expected: 3 PNGs. Entities in them should reference the blueprint keys from the Catalogs JSON files.

- [ ] **Step 2: Run all tests one more time**

Run: `cd MapGen && dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 3: Update repo root READMEs if needed**

No changes needed — this is a standalone module under `MapGen/`. Future plans (serializer, mod wrapper) will surface user-facing docs.

- [ ] **Step 4: Final commit (if anything changed)**

```bash
git status
# If no changes, skip the commit.
# Otherwise:
# git add <files>
# git commit -m "MapGen: final touches before serializer plan"
```

---

## Plan completion

At this point:
- `MapGen.Core` produces a `MapData` deterministically from a seed + W + H.
- All pipeline stages are unit-tested with property-based assertions.
- A 100-seed regression sweep passes.
- The `MapGen.Preview` CLI renders map PNGs to visually verify the algorithm.

**Next plan (Plan 2):** Serializer — `MapData` → `.timber` file.
**Plan 3:** Timberborn mod wrapper + UI integration.

---

## Self-review notes

Reading back:

**Spec coverage — every section in the spec has a task:**
- §1 Biome WFC → Tasks 4, 5
- §2 Heightmap with overhangs + apron + crater → Task 8
- §3 Hydrology (rivers, underground, seeps, badwater) → Task 9
- §4 + §5.3–§5.4 Trees + Resources + Thorns → Task 12
- §5.5 Ruins → Task 13
- §5.7 Blockages + Relics + Cores + Vents → Tasks 13, 14
- §5 Start label → Task 6
- §6 Access validation + top-up → Task 16 (slope repair deferred to Plan 3, note in task)
- §10 Testing: unit (all tasks), seed sweep (Task 18), headless preview (Tasks 19–20)

**Known deferrals (documented in-plan):**
- **Slope placement repair pass (Repair Pass B)** — the slope-block-object emission format isn't known until Plan 2's reference-sample harvest. Plan 3 will add it. For Plan 1, maps that can't be topped up within the flat zone get rejected/retried. Noted in Task 16.
- **Serializer** — Plan 2.
- **UI + blueprint resolution** — Plan 3.

**Type consistency:**
- `MapData.Columns` is `List<VoxelSpan>[]` everywhere.
- `MapData.WaterDepths` is `byte[]`.
- `MetaIndex`/`ColumnIndex` extension methods used consistently.
- `Biome` enum including `Start` used through every module.
- `PlacedEntity` + `EntityKind` used for every entity emission.

**No placeholders:** every code step has the complete code the engineer types. No "similar to Task N" shortcuts.
