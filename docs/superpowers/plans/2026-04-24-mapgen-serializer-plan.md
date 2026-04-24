# MapGen Serializer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `MapSerializer` that turns a `MapData` into a Timberborn-loadable `.timber` file, plus the small Plan-1 refactors needed to make generated maps reference real templates and use alphanumeric seeds.

**Architecture:** Single new file `MapGen.Core/MapSerializer.cs` writing a 4-entry ZIP archive. Five existing files get small surgical edits (catalogs simplified, faction-food check dropped, seed becomes string). Pure C# — no SkiaSharp / native deps in Core. The Preview CLI gains a `--write-timber` flag for end-to-end testing.

**Tech Stack:** C# (netstandard2.1 for Core), `System.IO.Compression.ZipArchive`, `System.Text.Json`, xUnit. No new dependencies.

---

## Spec reference

`docs/superpowers/specs/2026-04-24-mapgen-serializer-design.md`. The spec has the full reverse-engineered file format, per-template Components observed in the harvested sample, and the cleanup list.

## Workflow

Stay on branch `feature/graphs-mod`. Use `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"; export PATH="$DOTNET_ROOT:$PATH"` before any `dotnet` command if not already in `PATH`.

After each task: build, run all tests (currently 32 passing — that should never go down), commit with the message in the task.

---

## File structure (target)

```
MapGen/MapGen.Core/
  Catalog.cs                  # MODIFY: drop Faction enum + field
  Primitives.cs               # MODIFY: add Rng(string) ctor + helper
  GenerationConfig.cs         # MODIFY: Seed becomes string
  GenerationResult.cs         # MODIFY: ActualSeedUsed becomes string
  MapGenerator.cs             # MODIFY: retry pattern uses "-N" suffix
  AccessValidation.cs         # MODIFY: drop FolktailsFood/IronTeethFood
  Overlays.cs                 # MODIFY: Start-resource loop simplified
  MapSerializer.cs            # CREATE: the whole serializer
MapGen/MapGen.Catalogs/
  Trees.json                  # MODIFY: real template names
  Resources.json              # MODIFY: BlueberryBush only
  Thorns.json                 # MODIFY: Thorns
  Ruins.json                  # MODIFY: RuinColumnH1..H8 + others
  BlockObjects.json           # MODIFY: real template names
MapGen/MapGen.Core.Tests/
  Samples/
    EVERYTHING.timber         # CREATE: sanitized reference fixture
  MapGeneratorTests.cs        # MODIFY: Seed string updates
  AccessValidationTests.cs    # MODIFY: drop faction-food assertions
  OverlaysTests.cs            # MODIFY: drop faction-food assertions
  MapSerializerTests.cs       # CREATE: serializer unit tests
MapGen/MapGen.Preview/
  Program.cs                  # MODIFY: --count + --write-timber, drop --seed-range
```

---

## Task 1: Drop `Faction` from Catalog DTOs

The file format doesn't distinguish factions; faction differences happen at game-load. Drop the enum + field; downstream code that branches on it gets simplified in later tasks.

**Files:**
- Modify: `MapGen/MapGen.Core/Catalog.cs`

- [ ] **Step 1: Replace `MapGen/MapGen.Core/Catalog.cs` with this version (Faction enum and Faction field removed):**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MapGen;

public class CatalogEntry
{
    public string Key { get; set; } = "";
    public string BlueprintKey { get; set; } = "";
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
        return new Catalog
        {
            Trees = Read<List<CatalogEntry>>(Path.Combine(dir, "Trees.json"), opt),
            Resources = Read<List<CatalogEntry>>(Path.Combine(dir, "Resources.json"), opt),
            Thorns = Read<List<CatalogEntry>>(Path.Combine(dir, "Thorns.json"), opt),
            Ruins = Read<List<RuinCatalogEntry>>(Path.Combine(dir, "Ruins.json"), opt),
            BlockObjects = Read<Dictionary<string, CatalogEntry>>(Path.Combine(dir, "BlockObjects.json"), opt),
        };
    }

    private static T Read<T>(string path, JsonSerializerOptions opt) where T : new()
    {
        if (!File.Exists(path)) return new T();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), opt) ?? new T();
    }
}
```

- [ ] **Step 2: Build — expect compile errors in any file that references `Faction`:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet build 2>&1 | grep -E "error|Faction" | head -30
```

The next 4 tasks will fix the consumers (Overlays.cs, AccessValidation.cs, MapGeneratorTests.cs, OverlaysTests.cs). For now, leave the build broken; it will be fixed by Task 5. **Skip the build verification on this single task.**

- [ ] **Step 3: Commit (broken-build commit, fixed by next 4 tasks):**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/Catalog.cs
git commit -m "MapGen: drop Faction from CatalogEntry (file format doesn't distinguish)"
```

---

## Task 2: Update Catalog JSONs to use real template names

**Files:**
- Modify: `MapGen/MapGen.Catalogs/Trees.json`
- Modify: `MapGen/MapGen.Catalogs/Resources.json`
- Modify: `MapGen/MapGen.Catalogs/Thorns.json`
- Modify: `MapGen/MapGen.Catalogs/Ruins.json`
- Modify: `MapGen/MapGen.Catalogs/BlockObjects.json`

- [ ] **Step 1: Overwrite `MapGen/MapGen.Catalogs/Trees.json`:**

```json
[
  { "key": "maple", "blueprintKey": "Maple", "weight": 1.0 },
  { "key": "pine", "blueprintKey": "Pine", "weight": 1.0 },
  { "key": "birch", "blueprintKey": "Birch", "weight": 1.0 },
  { "key": "oak", "blueprintKey": "Oak", "weight": 1.0 },
  { "key": "dead_stump", "blueprintKey": "Pine", "weight": 0.3 }
]
```

(`dead_stump` reuses the `Pine` template; the Map Editor doesn't have a separate dead-tree template. The `IsDead` property gets set true at serialization time when we see `Key == "dead_stump"`.)

- [ ] **Step 2: Overwrite `MapGen/MapGen.Catalogs/Resources.json`:**

```json
[
  { "key": "berries", "blueprintKey": "BlueberryBush", "weight": 1.0 }
]
```

- [ ] **Step 3: Overwrite `MapGen/MapGen.Catalogs/Thorns.json`:**

```json
[
  { "key": "thorns", "blueprintKey": "Thorns", "weight": 1.0 }
]
```

- [ ] **Step 4: Overwrite `MapGen/MapGen.Catalogs/Ruins.json`:**

```json
[
  { "key": "ruin_h1", "blueprintKey": "RuinColumnH1", "weight": 1.0, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h2", "blueprintKey": "RuinColumnH2", "weight": 1.0, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h3", "blueprintKey": "RuinColumnH3", "weight": 1.0, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h4", "blueprintKey": "RuinColumnH4", "weight": 0.7, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h5", "blueprintKey": "RuinColumnH5", "weight": 0.5, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h6", "blueprintKey": "RuinColumnH6", "weight": 0.3, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h7", "blueprintKey": "RuinColumnH7", "weight": 0.2, "footprintW": 1, "footprintH": 1 },
  { "key": "ruin_h8", "blueprintKey": "RuinColumnH8", "weight": 0.1, "footprintW": 1, "footprintH": 1 },
  { "key": "underground_ruins", "blueprintKey": "UndergroundRuins", "weight": 0.5, "footprintW": 1, "footprintH": 1 }
]
```

- [ ] **Step 5: Overwrite `MapGen/MapGen.Catalogs/BlockObjects.json`:**

```json
{
  "slope": { "key": "slope", "blueprintKey": "Slope", "weight": 1.0 },
  "unstable_core": { "key": "unstable_core", "blueprintKey": "UnstableCore", "weight": 1.0 },
  "geothermal_vent": { "key": "geothermal_vent", "blueprintKey": "GeothermalField", "weight": 1.0 },
  "relic": { "key": "relic", "blueprintKey": "SmallRelic", "weight": 1.0 },
  "blockage": { "key": "blockage", "blueprintKey": "Blockage", "weight": 1.0 },
  "start_marker": { "key": "start_marker", "blueprintKey": "StartingLocation", "weight": 1.0 }
}
```

- [ ] **Step 6: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Catalogs/
git commit -m "MapGen: catalogs use real Timberborn template names"
```

---

## Task 3: Simplify `AccessValidation` (drop faction food)

**Files:**
- Modify: `MapGen/MapGen.Core/AccessValidation.cs`
- Modify: `MapGen/MapGen.Core.Tests/AccessValidationTests.cs`

- [ ] **Step 1: Replace `MapGen/MapGen.Core/AccessValidation.cs` with:**

```csharp
using System;
using System.Collections.Generic;

namespace MapGen;

public sealed class ReachabilityReport
{
    public HashSet<GridCoord> Cells { get; } = new();
    public int TreeCount { get; set; }
    public int ResourceCount { get; set; }
    public int WaterAccessCount { get; set; }

    public bool MeetsMinimums =>
        TreeCount >= 30 && ResourceCount >= 15 && WaterAccessCount >= 1;
}

public static class AccessValidation
{
    public static ReachabilityReport FloodFillReachable(MapData map, VoxelCoord start)
    {
        var report = new ReachabilityReport();
        var queue = new Queue<GridCoord>();
        var startCell = new GridCoord(start.X, start.Y);
        report.Cells.Add(startCell);
        queue.Enqueue(startCell);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int curH = map.TopHeight(cur.X, cur.Y);
            foreach (var (nx, ny) in FourNeighbors(cur.X, cur.Y, map.Width, map.Height))
            {
                var nc = new GridCoord(nx, ny);
                if (report.Cells.Contains(nc)) continue;
                int nh = map.TopHeight(nx, ny);
                if (nh != curH)
                {
                    byte wd = map.WaterDepths[map.ColumnIndex(nx, ny)];
                    if (wd > 0 && nh + wd == curH + 1)
                    {
                        report.WaterAccessCount++;
                        continue;
                    }
                    continue;
                }
                report.Cells.Add(nc);
                queue.Enqueue(nc);
            }
        }

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
            else if (e.Kind == EntityKind.Resource) report.ResourceCount++;
            else if (e.Kind == EntityKind.WaterSource || e.Kind == EntityKind.BadwaterSource)
                report.WaterAccessCount++;
        }
    }

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
            if (catalog.Trees.Count == 0) break;
            var tree = catalog.Trees[rng.NextRange(0, catalog.Trees.Count)];
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(tree.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Tree));
            report.TreeCount++;
        }
        while (report.ResourceCount < 15 && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            empty.Remove(c);
            if (catalog.Resources.Count == 0) break;
            var res = catalog.Resources[rng.NextRange(0, catalog.Resources.Count)];
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity(res.BlueprintKey,
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.Resource));
            report.ResourceCount++;
        }
        if (report.WaterAccessCount < 1 && empty.Count > 0)
        {
            var c = empty[rng.NextRange(0, empty.Count)];
            int z = map.TopHeight(c.X, c.Y) + 1;
            map.Entities.Add(new PlacedEntity("WaterSource",
                new VoxelCoord(c.X, c.Y, z), Orientation.North, EntityKind.WaterSource, 1f));
            report.WaterAccessCount++;
        }
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

(Note: the `wd <= 2` water-edge cap was already removed in Plan 1's Task 17 fix; preserving that.)

- [ ] **Step 2: AccessValidationTests.cs is unchanged** — the existing test (`BFS_reaches_flat_cells_only`) doesn't reference faction-food fields. Verify:

```bash
grep -E "FolktailsFood|IronTeethFood" /Users/matthewszatmary/Projects/timbermods/MapGen/MapGen.Core.Tests/AccessValidationTests.cs
```
Expected: no output. (If output appears, remove those references.)

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/AccessValidation.cs
git commit -m "MapGen: AccessValidation drops per-faction food (single resource template)"
```

---

## Task 4: Simplify `Overlays.PlaceResources` Start guarantee

**Files:**
- Modify: `MapGen/MapGen.Core/Overlays.cs`

- [ ] **Step 1: Find and replace the Start-metacell guarantee block in `MapGen/MapGen.Core/Overlays.cs`.**

Locate this block (inside `PlaceResources`):

```csharp
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
```

Replace with:

```csharp
        if (map.StartMeta.HasValue && catalog.Resources.Count > 0)
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
                var entry = catalog.Resources[rng.NextRange(0, catalog.Resources.Count)];
                int z = map.TopHeight(vx, vy) + 1;
                map.Entities.Add(new PlacedEntity(entry.BlueprintKey,
                    new VoxelCoord(vx, vy, z), Orientation.North, EntityKind.Resource));
                planted++;
            }
        }
```

- [ ] **Step 2: Update `OverlaysTests.cs` if it references `Faction`.** Locate `MapGen/MapGen.Core.Tests/OverlaysTests.cs` and remove every `Faction = Faction.X` initializer from the `DefaultCatalog()` helper (replace `new() { Key = "x", BlueprintKey = "y", Faction = Faction.Folktails, Weight = 1 }` with `new() { Key = "x", BlueprintKey = "y", Weight = 1 }`). Apply this to ALL CatalogEntry initializers in the test file.

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/Overlays.cs MapGen/MapGen.Core.Tests/OverlaysTests.cs
git commit -m "MapGen: Overlays Start guarantee no longer faction-aware"
```

---

## Task 5: Alphanumeric `Seed` end-to-end

Touches `Rng`, `GenerationConfig`, `GenerationResult`, `MapGenerator`, plus tests.

**Files:**
- Modify: `MapGen/MapGen.Core/Primitives.cs`
- Modify: `MapGen/MapGen.Core/GenerationConfig.cs`
- Modify: `MapGen/MapGen.Core/GenerationResult.cs`
- Modify: `MapGen/MapGen.Core/MapData.cs`
- Modify: `MapGen/MapGen.Core/MapGenerator.cs`
- Modify: `MapGen/MapGen.Core.Tests/PrimitivesTests.cs`
- Modify: `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`

- [ ] **Step 1: Add a string constructor to `Rng` and a small helper.** In `MapGen/MapGen.Core/Primitives.cs`, replace the `Rng` struct with:

```csharp
public struct Rng
{
    private uint _state;

    public Rng(uint seed)
    {
        _state = seed == 0u ? 0x9E3779B9u : seed;
    }

    public Rng(string seed) : this(HashSeed(seed)) { }

    /// FNV-1a 32-bit hash. Same string → same uint, regardless of runtime.
    public static uint HashSeed(string seed)
    {
        if (string.IsNullOrEmpty(seed)) return 0x9E3779B9u;
        uint h = 2166136261u;
        for (int i = 0; i < seed.Length; i++)
        {
            h ^= seed[i];
            h *= 16777619u;
        }
        return h;
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

    public float NextFloat() => (NextUInt() & 0x00FFFFFFu) / (float)(1 << 24);

    public int NextRange(int min, int max)
    {
        if (max <= min) return min;
        return min + (int)(NextUInt() % (uint)(max - min));
    }

    public bool NextBool(float probability) => NextFloat() < probability;

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
```

- [ ] **Step 2: Update `PrimitivesTests.cs`** to verify the new constructor. Append after the existing `Rng_range_returns_value_in_half_open_interval` test:

```csharp
    [Fact]
    public void Rng_string_seed_deterministic()
    {
        var a = new Rng("HELLO");
        var b = new Rng("HELLO");
        for (int i = 0; i < 5; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Rng_different_string_seeds_diverge()
    {
        var a = new Rng("ABC");
        var b = new Rng("XYZ");
        Assert.NotEqual(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Rng_empty_string_uses_fallback()
    {
        var r = new Rng("");
        // Just verify it produces some output without throwing
        var v = r.NextUInt();
        Assert.True(v != 0u);
    }
```

- [ ] **Step 3: Replace `MapGen/MapGen.Core/GenerationConfig.cs`:**

```csharp
namespace MapGen;

public sealed class GenerationConfig
{
    public int Width { get; init; } = 128;
    public int Height { get; init; } = 128;

    /// Alphanumeric seed (A-Z, a-z, 0-9), max 32 chars. Empty/null → a
    /// random 8-char seed is generated by Validate().
    public string Seed { get; set; } = "";

    public int PipelineRetryBudget { get; init; } = 5;
    public int MetaCellSize { get; init; } = 8;

    public void Validate()
    {
        if (Width < 16 || Height < 16)
            throw new System.ArgumentOutOfRangeException(
                nameof(Width), "Map dimensions must be at least 16x16.");
        if (Width % MetaCellSize != 0 || Height % MetaCellSize != 0)
            throw new System.ArgumentException(
                $"Width and Height must be multiples of MetaCellSize ({MetaCellSize}).");
        if (Seed == null) Seed = "";
        if (Seed.Length == 0) Seed = GenerateRandomSeed();
        if (Seed.Length > 32)
            throw new System.ArgumentException("Seed length must be <= 32 chars.");
        for (int i = 0; i < Seed.Length; i++)
        {
            char c = Seed[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            if (!ok) throw new System.ArgumentException(
                $"Seed contains non-alphanumeric character: '{c}'");
        }
    }

    private static string GenerateRandomSeed()
    {
        const string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rng = new System.Random();
        var chars = new char[8];
        for (int i = 0; i < 8; i++) chars[i] = alpha[rng.Next(alpha.Length)];
        return new string(chars);
    }
}
```

- [ ] **Step 4: Replace `MapGen/MapGen.Core/GenerationResult.cs`:**

```csharp
using System.Collections.Generic;

namespace MapGen;

public enum GenerationStatus { Success, Failed }

public sealed class GenerationResult
{
    public GenerationStatus Status { get; }
    public MapData? Map { get; }
    public string ActualSeedUsed { get; }
    public int RetryCount { get; }
    public IReadOnlyList<string> Log { get; }
    public string? FailureReason { get; }

    public GenerationResult(GenerationStatus status, MapData? map, string actualSeed,
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

- [ ] **Step 5: Update `MapData` Seed type.** In `MapGen/MapGen.Core/MapData.cs`, change:

```csharp
public uint Seed { get; }
```
to
```csharp
public string Seed { get; }
```

And update the constructor:

```csharp
public MapData(int width, int height, string seed)
{
    Width = width;
    Height = height;
    Seed = seed;
}
```

- [ ] **Step 6: Update `MapGenerator.cs` to use the new types and seed-suffix retry pattern.** Replace `MapGen/MapGen.Core/MapGenerator.cs`:

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

        var biomes = BiomeGrid.Solve(map.MetaWidth, map.MetaHeight, ref rng);
        if (biomes == null) { log.Add($"seed={seed}: WFC contradiction"); return null; }
        BiomeGrid.RewriteEdgeCraters(biomes, map.MetaWidth, map.MetaHeight);
        map.Biomes = biomes;

        var pick = StartSelection.Pick(biomes, map.MetaWidth, map.MetaHeight, ref rng);
        if (pick == null) { log.Add($"seed={seed}: no valid Start"); return null; }
        StartSelection.Apply(biomes, map.MetaWidth, pick.Value);
        map.StartMeta = pick.Value;

        Heightmap.Build(map, ref rng);
        Hydrology.Build(map, ref rng);

        Overlays.PlaceTrees(map, _catalog, ref rng);
        Overlays.PlaceResources(map, _catalog, ref rng);
        Overlays.PlaceThorns(map, _catalog, ref rng);
        Overlays.PlaceRuins(map, _catalog, ref rng);
        Overlays.PlaceBlockages(map, _catalog, ref rng);
        Overlays.PlaceRelics(map, _catalog, ref rng);
        Overlays.PlaceUnstableCores(map, _catalog, ref rng);
        Overlays.PlaceGeothermalVents(map, _catalog, ref rng);
        Overlays.PlaceStartMarker(map, _catalog);

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

- [ ] **Step 7: Update `MapGeneratorTests.cs` for string seeds.** Replace every `Seed = <number>` with `Seed = "<string>"`. Specifically:

In `MapGen/MapGen.Core.Tests/MapGeneratorTests.cs`:
- `Seed = 42` → `Seed = "42"`
- `Seed = 1` → `Seed = "1"`
- `for (uint s = 0; s < 100; s++) ... Seed = s` → `for (int s = 0; s < 100; s++) ... Seed = s.ToString()`

Also remove the `Faction = Faction.X` initializers from `EmptyCatalog()` (same change as Task 4 step 2).

- [ ] **Step 8: Build + run all tests:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj
```
Expected: build clean (Task 1's Faction-removal compile errors should now be gone since we cleaned up consumers in Tasks 4 + 7). All previously-passing tests still pass plus 3 new Rng-string tests.

If any test fails, the most likely culprits:
- A `Faction = Faction.X` reference still in some test file. Grep for it and remove.
- A `Seed = 42u` (uint) still anywhere. Grep `Seed = ` and find non-string assignments.

- [ ] **Step 9: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/
git commit -m "MapGen: alphanumeric string seeds end-to-end"
```

---

## Task 6: Preview CLI seed/count refactor

**Files:**
- Modify: `MapGen/MapGen.Preview/Program.cs`

- [ ] **Step 1: Replace `MapGen/MapGen.Preview/Program.cs`** with this version (drops `--seed-range`, adds `--count`):

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
        string? singleSeed = null;
        int count = 1;
        string outDir = "previews";
        string? catalogDir = null;

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
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    Console.Error.WriteLine("Usage: preview [--width N] [--height N] [--seed STR] [--count N] [--out DIR] [--catalog DIR]");
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
            // empty seed → Validate() generates a random one
            var result = gen.Generate(cfg);
            if (result.Status == GenerationStatus.Success && result.Map != null)
            {
                var safeSeed = result.ActualSeedUsed.Replace("/", "_").Replace("\\", "_");
                var outPath = Path.Combine(outDir, $"seed-{safeSeed}-{width}x{height}.png");
                Renderer.RenderToPng(result.Map, outPath);
                Console.WriteLine($"OK  seed={result.ActualSeedUsed} -> {outPath}");
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
```

- [ ] **Step 2: Smoke test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- --seed HELLO --width 64 --height 64 --out /tmp/mapgen-cli-test
```
Expected: `seed-HELLO-64x64.png` written to `/tmp/mapgen-cli-test/`. Inspect that the PNG file exists with size > 1 KB.

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Preview/Program.cs
git commit -m "MapGen: Preview CLI uses --seed STR + --count N"
```

---

## Task 7: Commit reference fixture

**Files:**
- Create: `MapGen/MapGen.Core.Tests/Samples/EVERYTHING.timber`

The user's harvested sample at `~/Documents/Timberborn/Maps/EVERYTHING.timber` is ~290 KB (the JPEG dominates). For repo hygiene we ship a sanitized smaller version with the JPEG replaced by a 1×1 stub.

- [ ] **Step 1: Create the Samples directory:**

```bash
mkdir -p /Users/matthewszatmary/Projects/timbermods/MapGen/MapGen.Core.Tests/Samples
```

- [ ] **Step 2: Sanitize and copy the sample:**

```bash
cd /tmp
rm -rf sanitize-sample
mkdir sanitize-sample && cd sanitize-sample
unzip -q ~/Documents/Timberborn/Maps/EVERYTHING.timber

# Replace the thumbnail with a 1x1 stub (33 bytes minimum JPEG)
python3 -c "
import struct
# minimal valid JPEG (1x1 white pixel)
jpg = bytes.fromhex(
    'ffd8ffe000104a46494600010100000100010000ffdb004300080606070605080707070909080a0c140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30313434341f27393d38323c2e333432ffdb0043010909090c0b0c180d0d1832211c213232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232ffc00011080001000103012200021101031101ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffc4001f0100030101010101010101010000000000000102030405060708090a0bffc400b51100020102040403040705040400010277000102031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a35363738393a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9eaf2f3f4f5f6f7f8f9faffda000c03010002110311003f00fbfeffd9'
)
open('map_thumbnail.jpg', 'wb').write(jpg)
"

zip -q /Users/matthewszatmary/Projects/timbermods/MapGen/MapGen.Core.Tests/Samples/EVERYTHING.timber map_metadata.json map_thumbnail.jpg version.txt world.json
ls -la /Users/matthewszatmary/Projects/timbermods/MapGen/MapGen.Core.Tests/Samples/EVERYTHING.timber
```
Expected: file size around 30-50 KB (down from 300 KB original).

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core.Tests/Samples/EVERYTHING.timber
git commit -m "MapGen: commit sanitized .timber reference fixture"
```

---

## Task 8: MapSerializer scaffolding + version.txt + thumbnail + metadata

Three small writers, the entry-point, and the stub JPEG. World JSON writer comes in Tasks 9–11.

**Files:**
- Create: `MapGen/MapGen.Core/MapSerializer.cs`
- Create: `MapGen/MapGen.Core.Tests/MapSerializerTests.cs`

- [ ] **Step 1: Add `System.IO.Compression` package reference if not already there.** Check current packages:

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen/MapGen.Core
grep -E "System.IO.Compression|System.Text.Json" MapGen.Core.csproj
```

If `System.IO.Compression` isn't listed, add it (note: in netstandard2.1 it's a separate package). Add this `<PackageReference>` to `MapGen/MapGen.Core/MapGen.Core.csproj` inside the existing `<ItemGroup>`:

```xml
    <PackageReference Include="System.IO.Compression" Version="4.3.0" />
```

(If the build already provides it implicitly, skip.)

- [ ] **Step 2: Write `MapGen/MapGen.Core/MapSerializer.cs`:**

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace MapGen;

public static class MapSerializer
{
    /// Writes `map` to `path` as a Timberborn-compatible .timber ZIP file.
    /// `gameVersion` is written verbatim into version.txt and world.json.
    /// `thumbnailJpeg` if null embeds a 1x1 stub JPEG.
    public static void Write(MapData map, string path, string gameVersion,
        byte[]? thumbnailJpeg = null)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (gameVersion is null) throw new ArgumentNullException(nameof(gameVersion));

        if (File.Exists(path)) File.Delete(path);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        WriteEntry(zip, "version.txt", s => WriteVersionTxt(s, gameVersion));
        WriteEntry(zip, "map_metadata.json", s => WriteMapMetadata(s, map));
        WriteEntry(zip, "map_thumbnail.jpg", s =>
        {
            byte[] bytes = thumbnailJpeg ?? StubJpegBytes;
            s.Write(bytes, 0, bytes.Length);
        });
        WriteEntry(zip, "world.json", s => WriteWorldJson(s, map, gameVersion));
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<Stream> writer)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        writer(s);
    }

    private static void WriteVersionTxt(Stream s, string gameVersion)
    {
        var bytes = Encoding.UTF8.GetBytes(gameVersion);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteMapMetadata(Stream s, MapData map)
    {
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteNumber("Width", map.Width);
        w.WriteNumber("Height", map.Height);
        w.WriteString("MapNameLocKey", "");
        w.WriteString("MapDescriptionLocKey", "");
        w.WriteString("MapDescription", "");
        w.WriteBoolean("IsRecommended", false);
        w.WriteBoolean("IsUnconventional", false);
        w.WriteBoolean("IsDev", false);
        w.WriteEndObject();
    }

    private static void WriteWorldJson(Stream s, MapData map, string gameVersion)
    {
        // Stub for now — Tasks 9-11 will fill this in.
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("GameVersion", gameVersion);
        w.WriteString("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        w.WriteStartObject("Singletons");
        w.WriteEndObject();
        w.WriteStartArray("Entities");
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// 1x1 white-pixel JPEG, 631 bytes. Same bytes as Task 7's sanitization stub.
    private static readonly byte[] StubJpegBytes = HexToBytes(
        "ffd8ffe000104a46494600010100000100010000ffdb004300080606070605080707070909080a0c140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30313434341f27393d38323c2e333432ffdb0043010909090c0b0c180d0d1832211c213232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232ffc00011080001000103012200021101031101ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffc4001f0100030101010101010101010000000000000102030405060708090a0bffc400b51100020102040403040705040400010277000102031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a35363738393a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9eaf2f3f4f5f6f7f8f9faffda000c03010002110311003f00fbfeffd9");

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        return bytes;
    }
}
```

- [ ] **Step 3: Write the unit tests `MapGen/MapGen.Core.Tests/MapSerializerTests.cs`:**

```csharp
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class MapSerializerTests
{
    [Fact]
    public void Write_produces_zip_with_four_entries()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "map_metadata.json", "map_thumbnail.jpg", "version.txt", "world.json" }, names);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_includes_supplied_thumbnail()
    {
        var map = MakeMinimalMap();
        var custom = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };  // marker JPEG SOI/EOI
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test", custom);
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("map_thumbnail.jpg")!;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(custom, ms.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_with_null_thumbnail_uses_stub()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("map_thumbnail.jpg")!;
            Assert.True(entry.Length > 0 && entry.Length < 2048);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_metadata_size_matches_map()
    {
        var map = MakeMinimalMap(64, 32);
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("map_metadata.json")!;
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            Assert.Equal(64, doc.RootElement.GetProperty("Width").GetInt32());
            Assert.Equal(32, doc.RootElement.GetProperty("Height").GetInt32());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_version_txt_matches_supplied_string()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-1e60728-xsm");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("version.txt")!;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal("1.0.13.0-1e60728-xsm", Encoding.UTF8.GetString(ms.ToArray()));
        }
        finally { File.Delete(path); }
    }

    private static MapData MakeMinimalMap(int w = 32, int h = 32)
    {
        var map = new MapData(w, h, "TEST");
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan> { new VoxelSpan(0, 4) };
        map.WaterDepths = new byte[w * h];
        return map;
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"mapgen-test-{System.Guid.NewGuid():N}.timber");
}
```

- [ ] **Step 4: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj --filter "FullyQualifiedName~MapSerializerTests"
```
Expected: 5/5 MapSerializerTests passing.

- [ ] **Step 5: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/MapSerializer.cs MapGen/MapGen.Core/MapGen.Core.csproj MapGen/MapGen.Core.Tests/MapSerializerTests.cs
git commit -m "MapGen: MapSerializer scaffolding — ZIP + version.txt + metadata + thumbnail"
```

---

## Task 9: WriteWorldJson — Singletons (MapSize, TerrainMap voxels)

The voxel array layout is observed in the sample as a flat space-separated string. We have to determine the (x, y, z) ordering empirically — the implementer writes a known-shape map, compares against a hand-crafted minimal `EVERYTHING.timber`-style sample, and confirms the layout.

**Working assumption:** order is z-major then y then x. That is, `array[z * (W*H) + y * W + x]`. Z-planes stacked low-to-high, each plane row-major. **The implementer must verify this and adjust if wrong.**

**Files:**
- Modify: `MapGen/MapGen.Core/MapSerializer.cs`

- [ ] **Step 1: Replace the placeholder `WriteWorldJson` and add `WriteSingletons` + voxel-array helpers.** In `MapGen/MapGen.Core/MapSerializer.cs`, replace the existing `WriteWorldJson` method with:

```csharp
    private static void WriteWorldJson(Stream s, MapData map, string gameVersion)
    {
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("GameVersion", gameVersion);
        w.WriteString("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        WriteSingletons(w, map);
        w.WriteStartArray("Entities");
        w.WriteEndArray();  // entities filled in Task 11
        w.WriteEndObject();
    }

    private static void WriteSingletons(Utf8JsonWriter w, MapData map)
    {
        w.WriteStartObject("Singletons");

        // MapSize
        w.WriteStartObject("MapSize");
        w.WriteStartObject("Size");
        w.WriteNumber("X", map.Width);
        w.WriteNumber("Y", map.Height);
        w.WriteEndObject();
        w.WriteEndObject();

        // TerrainMap.Voxels.Array
        WriteTerrainMap(w, map);

        // Other singletons added in Task 10.

        w.WriteEndObject();
    }

    private static void WriteTerrainMap(Utf8JsonWriter w, MapData map)
    {
        int zmax = ComputeMaxZ(map);
        w.WriteStartObject("TerrainMap");
        w.WriteStartObject("Voxels");
        w.WriteString("Array", BuildVoxelArrayString(map, zmax));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static int ComputeMaxZ(MapData map)
    {
        int max = 1;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var spans = map.Columns[map.ColumnIndex(x, y)];
            for (int i = 0; i < spans.Count; i++)
            {
                int top = spans[i].TopExclusive;
                if (top > max) max = top;
            }
        }
        // Add a small headroom; cap at 32 per spec.
        int withRoom = max + 4;
        if (withRoom > 32) withRoom = 32;
        return withRoom;
    }

    /// Layout assumption: z-major, then y-major, then x. The implementer
    /// MUST verify by comparing against a known reference sample. If the
    /// game refuses to load with this layout, swap to (y, x, z) or (x, y, z).
    private static string BuildVoxelArrayString(MapData map, int zmax)
    {
        var sb = new StringBuilder(map.Width * map.Height * zmax * 2);
        bool first = true;
        for (int z = 0; z < zmax; z++)
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (!first) sb.Append(' ');
            first = false;
            sb.Append(IsSolidAt(map, x, y, z) ? '1' : '0');
        }
        return sb.ToString();
    }

    private static bool IsSolidAt(MapData map, int x, int y, int z)
    {
        var spans = map.Columns[map.ColumnIndex(x, y)];
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (z >= span.Bottom && z < span.TopExclusive) return true;
        }
        return false;
    }
```

- [ ] **Step 2: Add a unit test for the voxel-array shape.** Append to `MapGen/MapGen.Core.Tests/MapSerializerTests.cs`:

```csharp
    [Fact]
    public void WorldJson_terrain_voxel_count_matches_W_H_Zmax()
    {
        var map = MakeMinimalMap(16, 16);
        // All columns height-4 → zmax = 4 + 4 headroom = 8
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var arr = doc.RootElement
                .GetProperty("Singletons")
                .GetProperty("TerrainMap")
                .GetProperty("Voxels")
                .GetProperty("Array")
                .GetString()!;
            var tokens = arr.Split(' ');
            int expected = 16 * 16 * 8;
            Assert.Equal(expected, tokens.Length);
            // For all-height-4 terrain: first 4 z-planes are all 1s, rest are 0s.
            int onesInFirstFour = tokens.Take(16 * 16 * 4).Count(t => t == "1");
            Assert.Equal(16 * 16 * 4, onesInFirstFour);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_top_level_has_required_fields()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            Assert.Equal("1.0.13.0-test", root.GetProperty("GameVersion").GetString());
            Assert.True(root.TryGetProperty("Timestamp", out _));
            Assert.True(root.TryGetProperty("Singletons", out _));
            Assert.True(root.TryGetProperty("Entities", out _));
        }
        finally { File.Delete(path); }
    }
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj --filter "FullyQualifiedName~MapSerializerTests"
```
Expected: 7 MapSerializer tests passing.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/MapSerializer.cs MapGen/MapGen.Core.Tests/MapSerializerTests.cs
git commit -m "MapGen: WriteWorldJson — Singletons.MapSize + TerrainMap.Voxels.Array"
```

---

## Task 10: Defaulted Singletons (water + soil + weather + wind)

These singletons are required in `world.json` for the game to load — most can use safe empty defaults. Field shapes below come from the harvested EVERYTHING.timber sample.

**Files:**
- Modify: `MapGen/MapGen.Core/MapSerializer.cs`

- [ ] **Step 1: Add the rest of the singletons to `WriteSingletons`.** In `MapGen/MapGen.Core/MapSerializer.cs`, modify `WriteSingletons` to call additional sub-writers after `WriteTerrainMap`. Replace `WriteSingletons` with:

```csharp
    private static void WriteSingletons(Utf8JsonWriter w, MapData map)
    {
        w.WriteStartObject("Singletons");

        // MapSize
        w.WriteStartObject("MapSize");
        w.WriteStartObject("Size");
        w.WriteNumber("X", map.Width);
        w.WriteNumber("Y", map.Height);
        w.WriteEndObject();
        w.WriteEndObject();

        WriteTerrainMap(w, map);
        WriteWaterMapNew(w, map);
        WriteWaterEvaporationMap(w, map);
        WriteWaterSimulationMigrator(w);
        WriteSoilMoistureSimulator(w, map);
        WriteSoilContaminationSimulator(w, map);
        WriteHazardousWeatherHistory(w);
        WriteNumberedEntityNamerService(w);
        WriteWindService(w);
        WriteMapThumbnailCameraMover(w);

        w.WriteEndObject();
    }

    private static string ZeroArrayString(int count)
    {
        var sb = new StringBuilder(count * 2);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('0');
        }
        return sb.ToString();
    }

    private static void WriteWaterMapNew(Utf8JsonWriter w, MapData map)
    {
        // Levels=2, WaterColumns + ColumnOutflows are width*height arrays of 0.
        // The placed WaterSource entities will fill these at sim start.
        int n = map.Width * map.Height;
        w.WriteStartObject("WaterMapNew");
        w.WriteNumber("Levels", 2);
        w.WriteStartObject("WaterColumns");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteStartObject("ColumnOutflows");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteWaterEvaporationMap(Utf8JsonWriter w, MapData map)
    {
        int n = map.Width * map.Height;
        w.WriteStartObject("WaterEvaporationMap");
        w.WriteStartObject("Levels");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteStartObject("EvaporationModifiers");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteWaterSimulationMigrator(Utf8JsonWriter w)
    {
        w.WriteStartObject("WaterSimulationMigrator");
        w.WriteBoolean("IsMigrated", true);
        w.WriteEndObject();
    }

    private static void WriteSoilMoistureSimulator(Utf8JsonWriter w, MapData map)
    {
        int n = map.Width * map.Height;
        w.WriteStartObject("SoilMoistureSimulator");
        w.WriteStartObject("Size");
        w.WriteNumber("X", map.Width);
        w.WriteNumber("Y", map.Height);
        w.WriteEndObject();
        w.WriteStartObject("MoistureLevels");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteSoilContaminationSimulator(Utf8JsonWriter w, MapData map)
    {
        int n = map.Width * map.Height;
        w.WriteStartObject("SoilContaminationSimulator");
        w.WriteStartObject("Size");
        w.WriteNumber("X", map.Width);
        w.WriteNumber("Y", map.Height);
        w.WriteEndObject();
        w.WriteStartArray("ContaminationCandidates");
        w.WriteEndArray();
        w.WriteStartObject("ContaminationLevels");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteHazardousWeatherHistory(Utf8JsonWriter w)
    {
        w.WriteStartObject("HazardousWeatherHistory");
        w.WriteStartArray("HistoryData");
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteNumberedEntityNamerService(Utf8JsonWriter w)
    {
        w.WriteStartObject("NumberedEntityNamerService");
        w.WriteStartObject("NextNumbers");
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteWindService(Utf8JsonWriter w)
    {
        w.WriteStartObject("WindService");
        w.WriteNumber("WindStrength", 1.0f);
        w.WriteString("WindDirection", "East");
        w.WriteNumber("NextWindChangeTime", 0.0f);
        w.WriteEndObject();
    }

    private static void WriteMapThumbnailCameraMover(Utf8JsonWriter w)
    {
        w.WriteStartObject("MapThumbnailCameraMover");
        w.WriteStartObject("CurrentConfiguration");
        w.WriteEndObject();
        w.WriteEndObject();
    }
```

- [ ] **Step 2: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj --filter "FullyQualifiedName~MapSerializerTests"
```
Expected: 7 tests still passing (no new tests, just verifying the old ones still pass).

- [ ] **Step 3: Diff against the harvested sample for sanity check.** Generate a serialized map and compare singletons against `EVERYTHING.timber`:

```bash
cd /tmp && rm -rf compare-singletons && mkdir compare-singletons && cd compare-singletons
unzip -q /Users/matthewszatmary/Projects/timbermods/MapGen/MapGen.Core.Tests/Samples/EVERYTHING.timber
mv world.json sample-world.json
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- --seed CHK --width 32 --height 32 --out /tmp/compare-singletons-out 2>/dev/null
# (Preview doesn't write .timber yet — wait until Task 12. For now just verify the unit tests passed.)
```
This step is exploratory; if you can do it, look for missing/extra singleton keys vs. the sample. Don't block on this.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/MapSerializer.cs
git commit -m "MapGen: WriteWorldJson — defaulted singletons (water/soil/weather/wind)"
```

---

## Task 11: Entities writer with per-template Components

**Files:**
- Modify: `MapGen/MapGen.Core/MapSerializer.cs`

- [ ] **Step 1: Replace the empty `WriteStartArray("Entities")` block in `WriteWorldJson` with a call to `WriteEntities`, and add the writer + components builder.** In `MapGen/MapGen.Core/MapSerializer.cs`:

In `WriteWorldJson`, change:
```csharp
        w.WriteStartArray("Entities");
        w.WriteEndArray();  // entities filled in Task 11
```
to:
```csharp
        WriteEntities(w, map);
```

Then add at the bottom of the class (before the closing brace):

```csharp
    private static void WriteEntities(Utf8JsonWriter w, MapData map)
    {
        w.WriteStartArray("Entities");
        for (int i = 0; i < map.Entities.Count; i++)
        {
            var e = map.Entities[i];
            w.WriteStartObject();
            w.WriteString("Id", System.Guid.NewGuid().ToString());
            w.WriteString("Template", e.BlueprintKey);
            WriteComponents(w, e);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteComponents(Utf8JsonWriter w, PlacedEntity e)
    {
        w.WriteStartObject("Components");
        // Every entity has a BlockObject.
        w.WriteStartObject("BlockObject");
        w.WriteStartObject("Coordinates");
        w.WriteNumber("X", e.Coord.X);
        w.WriteNumber("Y", e.Coord.Y);
        w.WriteNumber("Z", e.Coord.Z);
        w.WriteEndObject();
        if (e.Facing != Orientation.North)
            w.WriteString("Orientation", FacingToOrientation(e.Facing));
        w.WriteEndObject();

        switch (e.Kind)
        {
            case EntityKind.Tree:
                WriteCoordinatesOffsetter(w);
                WriteLivingNaturalResource(w, isDead: false);
                WriteGrowable(w, 1.0f);
                WriteYielderCuttable(w, "Log", 2);
                if (e.BlueprintKey == "Pine") WriteYielderGatherable(w, "PineResin", 0);
                break;
            case EntityKind.Resource:
                WriteCoordinatesOffsetter(w);
                WriteLivingNaturalResource(w, isDead: false);
                WriteYielderGatherable(w, "Berries", 6);
                break;
            case EntityKind.Ruin:
                WriteYielderRuin(w, "ScrapMetal", 45);
                WriteRuinModels(w, "A");
                break;
            case EntityKind.UnstableCore:
                WriteTimeActivatedComponent(w, isEnabled: true);
                w.WriteStartObject("UnstableCore");
                w.WriteNumber("ExplosionRadius", 5);
                w.WriteEndObject();
                break;
            case EntityKind.WaterSource:
            case EntityKind.BadwaterSource:
                w.WriteStartObject("WaterSource");
                w.WriteNumber("SpecifiedStrength", e.Param > 0 ? e.Param : 1.0f);
                w.WriteNumber("CurrentStrength", e.Param > 0 ? e.Param : 1.0f);
                w.WriteEndObject();
                WriteTimeActivatedComponent(w, isEnabled: false);
                if (e.BlueprintKey == "WaterSeep" || e.BlueprintKey == "BadwaterSeep")
                {
                    w.WriteStartObject("WaterDepthStrengthModifier");
                    w.WriteNumber("CurrentModifier", 0.0f);
                    w.WriteEndObject();
                }
                break;
            // Thorn / Slope / Blockage / Relic / GeothermalVent / StartMarker:
            // BlockObject only — no extra components.
        }
        w.WriteEndObject();  // Components
    }

    private static void WriteCoordinatesOffsetter(Utf8JsonWriter w)
    {
        w.WriteStartObject("CoordinatesOffsetter");
        w.WriteBoolean("Random", true);
        w.WriteEndObject();
    }

    private static void WriteLivingNaturalResource(Utf8JsonWriter w, bool isDead)
    {
        w.WriteStartObject("LivingNaturalResource");
        w.WriteBoolean("IsDead", isDead);
        w.WriteEndObject();
    }

    private static void WriteGrowable(Utf8JsonWriter w, float progress)
    {
        w.WriteStartObject("Growable");
        w.WriteNumber("GrowthProgress", progress);
        w.WriteEndObject();
    }

    private static void WriteYielderCuttable(Utf8JsonWriter w, string good, int amount)
    {
        w.WriteStartObject("Yielder:Cuttable");
        w.WriteStartObject("Yield");
        w.WriteString("Good", good);
        w.WriteNumber("Amount", amount);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteYielderGatherable(Utf8JsonWriter w, string good, int amount)
    {
        w.WriteStartObject("Yielder:Gatherable");
        w.WriteStartObject("Yield");
        w.WriteString("Good", good);
        w.WriteNumber("Amount", amount);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteYielderRuin(Utf8JsonWriter w, string good, int amount)
    {
        w.WriteStartObject("Yielder:Ruin");
        w.WriteStartObject("Yield");
        w.WriteString("Good", good);
        w.WriteNumber("Amount", amount);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteRuinModels(Utf8JsonWriter w, string variantId)
    {
        w.WriteStartObject("RuinModels");
        w.WriteString("VariantId", variantId);
        w.WriteEndObject();
    }

    private static void WriteTimeActivatedComponent(Utf8JsonWriter w, bool isEnabled)
    {
        w.WriteStartObject("TimeActivatedComponent");
        w.WriteBoolean("IsEnabled", isEnabled);
        w.WriteNumber("CyclesUntilCountdownActivation", 5);
        w.WriteNumber("DaysUntilActivation", 10.0f);
        w.WriteNumber("DaysPassed", 0.0f);
        w.WriteEndObject();
    }

    private static string FacingToOrientation(Orientation o) => o switch
    {
        Orientation.East => "Cw90",
        Orientation.South => "Cw180",
        Orientation.West => "Cw270",
        _ => "Cw0",
    };
```

- [ ] **Step 2: Add an entity-count test and a template-name test.** Append to `MapGen/MapGen.Core.Tests/MapSerializerTests.cs`:

```csharp
    [Fact]
    public void WorldJson_entity_per_placed()
    {
        var map = MakeMinimalMap();
        map.Entities.Add(new PlacedEntity("Pine", new VoxelCoord(5, 5, 5), Orientation.North, EntityKind.Tree));
        map.Entities.Add(new PlacedEntity("BlueberryBush", new VoxelCoord(7, 7, 5), Orientation.North, EntityKind.Resource));
        map.Entities.Add(new PlacedEntity("StartingLocation", new VoxelCoord(10, 10, 5), Orientation.North, EntityKind.StartMarker));

        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var entities = doc.RootElement.GetProperty("Entities");
            Assert.Equal(3, entities.GetArrayLength());
            Assert.Equal("Pine", entities[0].GetProperty("Template").GetString());
            Assert.Equal("BlueberryBush", entities[1].GetProperty("Template").GetString());
            Assert.Equal("StartingLocation", entities[2].GetProperty("Template").GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_tree_has_components()
    {
        var map = MakeMinimalMap();
        map.Entities.Add(new PlacedEntity("Pine", new VoxelCoord(5, 5, 5), Orientation.North, EntityKind.Tree));
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var components = doc.RootElement.GetProperty("Entities")[0].GetProperty("Components");
            Assert.True(components.TryGetProperty("BlockObject", out _));
            Assert.True(components.TryGetProperty("LivingNaturalResource", out _));
            Assert.True(components.TryGetProperty("Growable", out _));
            Assert.True(components.TryGetProperty("Yielder:Cuttable", out _));
            // Pine specifically also gets resin
            Assert.True(components.TryGetProperty("Yielder:Gatherable", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_water_source_has_strength()
    {
        var map = MakeMinimalMap();
        map.Entities.Add(new PlacedEntity("WaterSource", new VoxelCoord(3, 3, 5),
            Orientation.North, EntityKind.WaterSource, 1.5f));
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var ws = doc.RootElement.GetProperty("Entities")[0]
                .GetProperty("Components").GetProperty("WaterSource");
            Assert.Equal(1.5f, ws.GetProperty("SpecifiedStrength").GetSingle());
            Assert.Equal(1.5f, ws.GetProperty("CurrentStrength").GetSingle());
        }
        finally { File.Delete(path); }
    }
```

- [ ] **Step 3: Build + test:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj
```
Expected: 10 MapSerializer tests + all prior = at least 40 total. All passing.

- [ ] **Step 4: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/MapSerializer.cs MapGen/MapGen.Core.Tests/MapSerializerTests.cs
git commit -m "MapGen: WriteEntities — per-template Components builder"
```

---

## Task 12: Preview CLI `--write-timber` flag

**Files:**
- Modify: `MapGen/MapGen.Preview/Program.cs`

- [ ] **Step 1: Add `writeTimber` flag, wire to MapSerializer.** In `MapGen/MapGen.Preview/Program.cs`, add a new option:

After the `bool` declarations near the top of `Main`, add:
```csharp
        bool writeTimber = false;
        string gameVersion = "1.0.13.0-1e60728-xsm";
```

In the argument-parsing switch, add new cases:
```csharp
                case "--write-timber": writeTimber = true; break;
                case "--game-version": gameVersion = args[++i]; break;
```

In the success branch (after `Renderer.RenderToPng(...)`), add:
```csharp
                if (writeTimber)
                {
                    var timberPath = Path.Combine(outDir, $"seed-{safeSeed}-{width}x{height}.timber");
                    MapSerializer.Write(result.Map, timberPath, gameVersion);
                    Console.WriteLine($"   .timber -> {timberPath}");
                }
```

Update the usage string in the error case to:
```csharp
                    Console.Error.WriteLine("Usage: preview [--width N] [--height N] [--seed STR] [--count N] [--out DIR] [--catalog DIR] [--write-timber] [--game-version STR]");
```

- [ ] **Step 2: Smoke test — generate a .timber and verify it's a valid ZIP:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- --seed PREVIEW1 --width 64 --height 64 --write-timber --out /tmp/mapgen-write-timber-test
ls -la /tmp/mapgen-write-timber-test/
unzip -l /tmp/mapgen-write-timber-test/seed-PREVIEW1-64x64.timber
```
Expected: a `.timber` file, plus `unzip -l` shows the 4 expected entries (`map_metadata.json`, `map_thumbnail.jpg`, `version.txt`, `world.json`).

- [ ] **Step 3: Commit:**

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Preview/Program.cs
git commit -m "MapGen: Preview --write-timber for end-to-end .timber generation"
```

---

## Task 13: Manual integration test in Timberborn

This is the only validation that matters: does the game actually load our generated maps?

- [ ] **Step 1: Generate three maps directly into the user's Maps folder:**

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
    --seed GENMAP1 --width 64 --height 64 --write-timber \
    --out ~/Documents/Timberborn/Maps/
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
    --seed GENMAP2 --width 128 --height 128 --write-timber \
    --out ~/Documents/Timberborn/Maps/
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
    --seed GENMAP3 --width 192 --height 192 --write-timber \
    --out ~/Documents/Timberborn/Maps/
ls -la ~/Documents/Timberborn/Maps/seed-GENMAP*.timber
```

- [ ] **Step 2: Manually load each in Timberborn.**

1. Launch Timberborn.
2. Map Editor → Load → see `seed-GENMAP1-64x64`, `seed-GENMAP2-128x128`, `seed-GENMAP3-192x192` in the list.
3. Load each one. For each, observe:
   - Does the editor open without an exception popup?
   - Is the terrain visible (not all-air or all-bricked)?
   - Are the placed water sources visible at expected locations?
   - Are trees and berries visible?
   - Are slopes / ruins / cores visible?

- [ ] **Step 3: If any map fails to load, triage:**

If you see a JSON parse error or an exception about a missing field:
1. Save an empty map from the editor as `~/Documents/Timberborn/Maps/EMPTY.timber`.
2. Unzip both `EMPTY.timber` (game-saved empty) and one of our generated `.timber` files.
3. Diff the `world.json` files. Look for:
   - Singletons present in EMPTY but missing from ours → add them.
   - Field types/shapes that differ → fix the writer.
4. Iterate.

If a specific entity fails (e.g., a UnstableCore crashes the editor):
1. Compare that entity's `Components` block against the same template in `EVERYTHING.timber`.
2. Adjust the template's `WriteComponents` switch case.

After any fix:
```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/MapGen.Core/MapSerializer.cs
git commit -m "MapGen: serializer fix — <describe what was wrong>"
```

- [ ] **Step 4: Voxel array layout verification.**

Almost certainly the first thing that will fail is the terrain. Our assumed layout is `(z, y, x)` row-major. If terrain renders all-flat or all-air, swap the iteration order in `BuildVoxelArrayString` (`MapGen.Core/MapSerializer.cs`):

Try alternative orderings until terrain renders correctly:
- `(z, y, x)` — current attempt
- `(z, x, y)` — swap inner two
- `(y, x, z)` — z innermost
- `(x, y, z)` — Timberborn uses (x, y, z) for entity coords; the array might match

To debug: write a 16×16 map where columns at (0,0), (1,0), (0,1) have heights 1, 2, 3 respectively (set this up manually in the test harness). Inspect the output array — the first few non-zero indices reveal the layout.

Once verified, commit the fixed layout:
```bash
git add MapGen/MapGen.Core/MapSerializer.cs
git commit -m "MapGen: voxel array layout corrected (was ZYX, now <correct>)"
```

- [ ] **Step 5: Final smoke confirmation.**

Once a map loads cleanly and looks reasonable:
1. Save a session note about what worked / what was fixed in `MapGen/README.md` (or create one) — reference the `EVERYTHING.timber` fixture and which templates / singletons mattered.
2. Run the full test suite one more time:

```bash
cd /Users/matthewszatmary/Projects/timbermods/MapGen
dotnet test MapGen.Core.Tests/MapGen.Core.Tests.csproj
```
Expected: all unit tests still passing. Manual integration confirmation noted in commit message.

```bash
cd /Users/matthewszatmary/Projects/timbermods
git add MapGen/  # only if README was created/updated
git commit -m "MapGen: integration confirmed — generated maps load in Timberborn 1.0.13.0" --allow-empty
```

---

## Plan completion

Once Task 13 passes:
- `MapSerializer.Write(...)` produces `.timber` files Timberborn can load.
- All catalog template names match real templates.
- Seeds are alphanumeric strings.
- Preview CLI can both render PNGs and write `.timber` files in one invocation.

**Next plan (Plan 3):** Timberborn mod wrapper + UI integration (main menu "Random Map" button, map editor "Generate" template, blueprint resolver).

---

## Self-review notes

**Spec coverage check:**
- §Container ZIP with 4 entries → Tasks 8–11
- §map_metadata.json → Task 8
- §version.txt → Task 8
- §map_thumbnail.jpg with optional override → Task 8
- §world.json header → Task 9
- §Singletons (MapSize + TerrainMap voxels) → Task 9
- §Singletons (water/soil/weather/wind defaults) → Task 10
- §Entities array + per-template Components → Task 11
- §Catalog real template names → Task 2
- §Drop Faction → Task 1
- §AccessValidation simplified → Task 3
- §Overlays Start guarantee simplified → Task 4
- §Alphanumeric seed → Task 5
- §Preview CLI updates → Tasks 6, 12
- §Reference fixture committed → Task 7
- §Manual integration test → Task 13

**Type consistency check:**
- `Rng(string)` constructor used in MapGenerator (Task 5 step 6) matches definition in Task 5 step 1.
- `MapData.Seed` is `string` everywhere after Task 5.
- `GenerationResult.ActualSeedUsed` is `string` after Task 5.
- `MapSerializer.Write` signature `(MapData, string, string, byte[]?)` matches throughout Tasks 8–12.
- `EntityKind` switch in Task 11 covers all kinds defined in Plan 1's `MapData.cs`.

**Known unknowns documented (NOT placeholders — they're real lookups the implementer does):**
- Voxel array (x,y,z) ordering — Task 9 assumes ZYX, Task 13 step 4 has the recovery procedure.
- Singletons defaults: if any singleton field shape is wrong or a required singleton is missing, Task 13 step 3 walks through the diagnose-from-EMPTY.timber procedure.
- RuinModels.VariantId — fixed at "A" in Task 11; cosmetic only.
