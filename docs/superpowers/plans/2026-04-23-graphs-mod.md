# Graphs Mod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Timberborn mod that shows a full-screen line-chart window of all settlement metrics (goods, population, science, wellbeing) over in-game time, triggered by Shift+G, with weather cycles as background bands and a per-district filter.

**Spec:** `docs/superpowers/specs/2026-04-23-graphs-mod-design.md`

**Architecture:** Bindito-registered services under `[Context("Game")]`. A single `MetricSampler` (ITickableSingleton) writes one sample per in-game hour into a ring-buffer `MetricHistory`. Each metric is registered via an `IMetricProvider` so categories (goods, population, science, wellbeing) live in separate files. UI is UIToolkit: a `GraphsWindow` assembled on hotkey press, containing a custom-drawn `GraphsChart` (via `VisualElement.generateVisualContent`), a scrollable `GraphsLegend` of checkboxes, a district dropdown, and a bottom range selector.

**Tech Stack:** C# 12 (preview), .NET Standard 2.1, Timberborn 1.0 + Bindito DI, Unity UIToolkit (`UnityEngine.UIElements`), Harmony 2.4.2 (for hotkey listener only; no game-logic patching). Build via `./build.sh Graphs`.

## File layout (produced by this plan)

```
Graphs/
├── Graphs.csproj
├── manifest.json
├── ModStarter.cs
├── README.md
├── NOTES.md                            (internal API notes — resolved in Task 2)
├── lib/
│   ├── AppleSiliconHarmony.dll
│   └── libapple_silicon_harmony_native.dylib
├── Localizations/
│   └── enUS.csv
├── GraphsConfigurator.cs               ([Context("Game")])
├── Metrics/
│   ├── MetricScope.cs                  (enum)
│   ├── MetricCategory.cs               (enum)
│   ├── MetricDefinition.cs             (class)
│   ├── MetricHistory.cs                (ring buffer)
│   ├── MetricRegistry.cs               (ILoadableSingleton; collects providers)
│   ├── MetricSampler.cs                (ITickableSingleton)
│   ├── WeatherStateSampler.cs          (resolves current weather enum)
│   ├── DistrictFilter.cs               (shared state: which district is selected)
│   ├── IMetricProvider.cs              (interface)
│   └── Providers/
│       ├── ScienceMetricProvider.cs
│       ├── PopulationMetricProvider.cs
│       ├── GoodsMetricProvider.cs
│       └── WellbeingMetricProvider.cs
└── UI/
    ├── GraphsHotkey.cs                 (TickableSingleton; listens for Shift+G)
    ├── GraphsWindow.cs                 (dialog root; built on open)
    ├── GraphsChart.cs                  (custom mesh-drawn chart)
    ├── GraphsLegend.cs
    ├── GraphsRangeSelector.cs
    ├── GraphsDistrictSelector.cs
    └── GraphColors.cs                  (stable HSL palette)
```

**Total tasks: 28.** Each task ends with a commit and, where relevant, a manual in-game verification.

---

## Task 1: Scaffold empty mod

Establishes a buildable, deployable empty mod so subsequent tasks can iterate on in-game behavior immediately.

**Files:**
- Create: `Graphs/Graphs.csproj`
- Create: `Graphs/manifest.json`
- Create: `Graphs/ModStarter.cs`
- Create: `Graphs/README.md`
- Create: `Graphs/lib/AppleSiliconHarmony.dll` (copy from `FreeAutomation/lib/`)
- Create: `Graphs/lib/libapple_silicon_harmony_native.dylib` (copy from `FreeAutomation/lib/`)

- [ ] **Step 1: Create directory**

```bash
mkdir -p Graphs/lib Graphs/Localizations Graphs/Metrics/Providers Graphs/UI
```

- [ ] **Step 2: Copy Apple Silicon Harmony shim libs**

```bash
cp FreeAutomation/lib/AppleSiliconHarmony.dll Graphs/lib/
cp FreeAutomation/lib/libapple_silicon_harmony_native.dylib Graphs/lib/
```

- [ ] **Step 3: Write `Graphs/Graphs.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>preview</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>embedded</DebugType>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <PropertyGroup>
    <GameManagedDir>$(HOME)/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/Managed</GameManagedDir>
    <HarmonyDir>$(NuGetPackageRoot)lib.harmony/2.4.2/lib/net48</HarmonyDir>
  </PropertyGroup>

  <ItemGroup>
    <Using Include="HarmonyLib" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="$(GameManagedDir)/Timberborn.*.dll">
      <Private>false</Private>
    </Reference>
    <Reference Include="$(GameManagedDir)/Bindito.*.dll">
      <Private>false</Private>
    </Reference>
    <Reference Include="$(GameManagedDir)/UnityEngine.dll">
      <Private>false</Private>
    </Reference>
    <Reference Include="$(GameManagedDir)/UnityEngine.CoreModule.dll">
      <Private>false</Private>
    </Reference>
    <Reference Include="$(GameManagedDir)/UnityEngine.UIElementsModule.dll">
      <Private>false</Private>
    </Reference>
    <Reference Include="$(GameManagedDir)/UnityEngine.InputLegacyModule.dll">
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(HarmonyDir)/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="AppleSiliconHarmony">
      <HintPath>lib/AppleSiliconHarmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <None Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <Target Name="DeployMod" AfterTargets="Build">
    <PropertyGroup>
      <DeployDir>$(HOME)/Documents/Timberborn/Mods/Graphs</DeployDir>
    </PropertyGroup>

    <Copy SourceFiles="$(OutputPath)/manifest.json" DestinationFolder="$(DeployDir)" />
    <Copy SourceFiles="$(OutputPath)/Graphs.dll" DestinationFolder="$(DeployDir)" />
    <Copy SourceFiles="$(HarmonyDir)/0Harmony.dll" DestinationFolder="$(DeployDir)" />
    <Copy SourceFiles="lib/AppleSiliconHarmony.dll" DestinationFolder="$(DeployDir)" />
    <Copy SourceFiles="lib/libapple_silicon_harmony_native.dylib" DestinationFolder="$(DeployDir)" />
    <Copy SourceFiles="@(LocalizationFiles)" DestinationFolder="$(DeployDir)/Localizations" Condition="'@(LocalizationFiles)' != ''" />
  </Target>

  <ItemGroup>
    <LocalizationFiles Include="Localizations/*.csv" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Write `Graphs/manifest.json`**

```json
{
    "Name": "Graphs",
    "Version": "0.1.0",
    "Id": "Graphs",
    "MinimumGameVersion": "1.0.0.0",
    "Description": "Shift+G opens a full-screen window with line graphs of all settlement metrics (goods, population, science, wellbeing) over time, with weather cycles as background bands."
}
```

- [ ] **Step 5: Write `Graphs/ModStarter.cs`**

```csharp
using System.Runtime.InteropServices;
using HarmonyLib;
using Timberborn.ModManagerScene;

namespace Graphs;

public class ModStarter : IModStarter
{
    public void StartMod(IModEnvironment modEnvironment)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            Anatawa12.AppleSiliconHarmony.Patcher.Patch();

        new Harmony("Graphs").PatchAll();
    }
}
```

- [ ] **Step 6: Write `Graphs/README.md`**

```markdown
# Graphs

Shift+G opens a full-screen window with line graphs of all settlement metrics
(goods, population, science, wellbeing) over time. Each line is normalized
independently so wildly different magnitudes are visually comparable.
Weather cycles (drought, badtide) render as translucent background bands.

## Build

```bash
./build.sh Graphs
```

Deploys to `~/Documents/Timberborn/Mods/Graphs/`.
```

- [ ] **Step 7: Build and verify**

Run: `./build.sh Graphs`

Expected: `=== Building Graphs ===` then `All mods built and deployed successfully.`

Verify deploy: `ls ~/Documents/Timberborn/Mods/Graphs/`

Expected to contain: `Graphs.dll`, `manifest.json`, `0Harmony.dll`, `AppleSiliconHarmony.dll`, `libapple_silicon_harmony_native.dylib`.

- [ ] **Step 8: Launch game, verify mod loads cleanly**

Launch Timberborn. Go to Main Menu → Mods. Expected: "Graphs 0.1.0" appears, is enabled, no error badge.

- [ ] **Step 9: Commit**

```bash
git add Graphs/
git commit -m "Graphs: scaffold empty mod"
```

---

## Task 2: Reconnaissance — resolve game APIs into `NOTES.md`

Before writing provider code, confirm the exact API signatures we need and record them. This prevents compile-error churn in later tasks.

**Files:**
- Create: `Graphs/NOTES.md`

- [ ] **Step 1: Run DLL probe for each needed API group**

```bash
MANAGED="$HOME/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/Managed"

# Dump type names and public members to /tmp/graphs-probe.txt for reference
for d in KeyBindingSystem InputSystem TimeSystem WeatherSystem HazardousWeatherSystem \
         GameDistricts InventorySystem Goods GameGoods Characters Beavers Bots \
         ScienceSystem Wellbeing NeedSystem BeaverContaminationSystem Healthcare \
         PopulationStatisticsSystem CoreUI SingletonSystem; do
  echo "===== $d =====" >> /tmp/graphs-probe.txt
  strings "$MANAGED/Timberborn.$d.dll" \
    | grep -E "^(get_|Get|[A-Z][A-Za-z0-9]*(Service|Provider|Registry|Statistics|Cycle|Event|Controller|Factory|Stack|Dialog|Spec|Manager)?)$" \
    | sort -u >> /tmp/graphs-probe.txt
done
wc -l /tmp/graphs-probe.txt
```

- [ ] **Step 2: Write `Graphs/NOTES.md` documenting resolved APIs**

The contents below are what the engineer should *confirm* and record. If any item below turns out to be wrong on probing, correct it here and reference the corrected name in later tasks.

```markdown
# Graphs — Internal API Notes

Resolved from decompiling the Timberborn 1.0 DLLs. Update this file when any
API assumption below is invalidated.

## Time

- `Timberborn.TimeSystem.IDayNightCycle` — DI-registered, inject directly.
  - Properties: `int DayNumber`, `float DayProgress` (0..1), `float DaysSinceStart`, `float DayLengthInSeconds`.
  - Computed "game hour" used for sampling cadence:
    `float partialDay = cycle.DayNumber + cycle.DayProgress;`
    `int hourIndex = (int)Math.Floor(partialDay * 24f);`

## Weather

- `Timberborn.WeatherSystem.WeatherService` (or equivalent — confirm at probe).
  The spec calls for three states: Temperate / Drought / Badtide.
- `Timberborn.HazardousWeatherSystem.HazardousWeatherService.CurrentCycleHazardousWeather`
  gives the hazardous weather for the current cycle; actual "is the hazardous
  period active right now?" requires checking whether the cycle is in its
  hazardous phase — see `WeatherStateSampler` (Task 13).
- If the exact "is hazardous weather active now" API cannot be located, fall
  back to: sample the current weather label from the game's own `WeatherHUD`
  state object via reflection.

## Districts

- `Timberborn.GameDistricts.DistrictCenterRegistry`
  - `IReadOnlyCollection<DistrictCenter> FinishedDistrictCenters`
  - `IReadOnlyCollection<DistrictCenter> AllDistrictCenters`
- `Timberborn.GameDistricts.DistrictCenter`
  - `string DistrictName`
  - `int DistrictPopulation`
  - `int NumberOfAdults`, `int NumberOfBots`
  - `DistrictBuilding` (sibling component)
  - The set of beavers/bots is reachable via the `DistrictPopulation`
    subsystem (see below).

## Inventory / goods

- `Timberborn.InventorySystem.DistrictInventoryRegistry`
  - `IReadOnlyCollection<Inventory> AllInventories { get; }`
  - Each `Inventory` has `Amount` (may return per-good via `int GetAmount(string goodId)`, confirm).
- `Timberborn.Goods.GoodSpecService` or `StorableGoodRegistry` — iterate
  registered good ids at startup. If `StorableGoodRegistry` is internal,
  enumerate via the `GoodSpec` multi-binding.
- Per-district good total = sum of matching-good-id amounts across that
  district's inventories. The simplest path is:
  ```csharp
  foreach (var inv in districtInventoryRegistry.AllInventories)
      if (inv.District == districtCenter.DistrictBuilding.District)
          total += inv.Stock.Get(goodId);
  ```
  (Exact property names pending probe — `Inventory.Stock` is a GoodAmountStock.)

## Science

- `Timberborn.ScienceSystem.ScienceService`
  - `int SciencePoints` (settlement-wide)

## Wellbeing

- `Timberborn.Wellbeing.WellbeingService` (district-scoped)
  - Exact method for "average wellbeing for district X" is likely on
    `UpdateDistrictWellbeingTrackerRegistry` or a per-district tracker.
    During implementation, iterate beavers in the district and average
    their `Wellbeing` component value.

## Beavers / bots

- `Timberborn.Beavers.Beaver` — component on every adult beaver.
- `Timberborn.Characters.Character` — base; has `GetComponent<T>()`.
- To enumerate all beavers/bots in a district, query
  `DistrictCenter.GetComponent<DistrictPopulation>()` or iterate
  `entityRegistry.GetEntities<Beaver>()` and filter by assigned district.
  The exact `DistrictPopulation` component name pending probe.

## Injured / infected / contaminated

- Injured: per-beaver `Injury` / `Injurable` component exposing `IsInjured`
  (from `isInjured` string seen in Healthcare.dll). Count beavers with
  `IsInjured == true`.
- Infected (incubating): `Timberborn.BeaverContaminationSystem.ContaminationIncubator`
  — component on beavers currently in incubation. Count presence.
- Contaminated: `Timberborn.BeaverContaminationSystem.IContaminationStatisticsProvider`
  — inject `DistrictBeaverContaminationStatisticsProvider` (per-district) and
  `GlobalBeaverContaminationStatisticsProvider` (settlement-wide).

## Homeless / unemployed

- `Timberborn.PopulationStatisticsSystem.IDwellingStatisticsProvider` →
  dwelling statistics (has-home vs homeless).
- `Timberborn.PopulationStatisticsSystem.IEmploymentStatisticsProvider` →
  employment statistics (employed vs unemployed adults).
- Both have per-district variants.

## Hotkey

- `Timberborn.InputSystem.InputService` — DI-registered; inject it.
  - Supports polling: `IsKeyDown(KeyCode)`, `IsKeyHeld(KeyCode)`.
- We do NOT use `KeyBindingSystem` for user-rebindable hotkeys in phase 1
  (its registration API requires a specification asset we'd have to ship as
  a Resource). Instead: poll `InputService` each tick for Shift+G. This
  matches how simple mods do it and keeps phase 1 tight.

## UI

- Panels live in `UnityEngine.UIElements.VisualElement` tree.
- Root visual element from `Timberborn.CoreUI.RootVisualElementProvider`.
- For a full-screen dialog, the simplest path is to append a child directly
  to the root (absolute-positioned, stretched). Closing = removing that
  child. The game's `PanelStack` is more idiomatic, but requires implementing
  `IPanelController`; keep that as a polish refinement (Task 27).

## ITickableSingleton / ILoadableSingleton / EventBus

- `Timberborn.SingletonSystem.ITickableSingleton` — `void Tick()`.
- `Timberborn.SingletonSystem.ILoadableSingleton` — `void Load()`.
- `Timberborn.SingletonSystem.EventBus` — `Register(object)`, `Unregister(object)`,
  and the `[OnEvent]` attribute on methods.
```

- [ ] **Step 3: Verify NOTES content against probe output**

Read through `/tmp/graphs-probe.txt` and correct any type/member in `NOTES.md`
that doesn't appear there verbatim. In particular:
- confirm `DistrictCenterRegistry.FinishedDistrictCenters` exists;
- confirm `ScienceService.SciencePoints` exists;
- confirm the injury / incubator component names;
- locate the dwelling/employment provider exact names.

- [ ] **Step 4: Commit**

```bash
git add Graphs/NOTES.md
git commit -m "Graphs: add internal API notes from DLL probe"
```

---

## Task 3: Define `MetricScope` and `MetricCategory` enums

Plain C# enums the rest of the code will reference.

**Files:**
- Create: `Graphs/Metrics/MetricScope.cs`
- Create: `Graphs/Metrics/MetricCategory.cs`

- [ ] **Step 1: Write `Graphs/Metrics/MetricScope.cs`**

```csharp
namespace Graphs.Metrics;

/// Scope a metric's value function operates at.
public enum MetricScope
{
    /// Value is settlement-wide; the district filter is ignored.
    Settlement,

    /// Value is per-district; "All districts" aggregates, a specific district filters.
    District,

    /// Value works either way; provider handles both.
    Either,
}
```

- [ ] **Step 2: Write `Graphs/Metrics/MetricCategory.cs`**

```csharp
namespace Graphs.Metrics;

/// Grouping used by the legend's category sections.
public enum MetricCategory
{
    Goods,
    Population,
    Science,
    Wellbeing,
}
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add Graphs/Metrics/MetricScope.cs Graphs/Metrics/MetricCategory.cs
git commit -m "Graphs: add MetricScope and MetricCategory enums"
```

---

## Task 4: Define `MetricDefinition` and `IMetricProvider`

Metric definitions are the atomic unit: id, display key, category, scope, and a `Func<string?, float>` — the argument is the district name (`null` = "all districts"). Providers publish a collection of definitions.

**Files:**
- Create: `Graphs/Metrics/MetricDefinition.cs`
- Create: `Graphs/Metrics/IMetricProvider.cs`

- [ ] **Step 1: Write `Graphs/Metrics/MetricDefinition.cs`**

```csharp
namespace Graphs.Metrics;

/// A single trackable metric. The value function is called once per sample
/// (per in-game hour) with the active district-filter value.
public sealed class MetricDefinition
{
    public string Id { get; }
    public string NameLocKey { get; }
    public MetricCategory Category { get; }
    public MetricScope Scope { get; }

    /// <param name="districtName">
    /// Specific district name when the filter is set to one district, or
    /// null for "all districts" (settlement-wide aggregation).
    /// </param>
    public Func<string?, float> ValueFn { get; }

    public MetricDefinition(
        string id,
        string nameLocKey,
        MetricCategory category,
        MetricScope scope,
        Func<string?, float> valueFn)
    {
        Id = id;
        NameLocKey = nameLocKey;
        Category = category;
        Scope = scope;
        ValueFn = valueFn;
    }
}
```

- [ ] **Step 2: Write `Graphs/Metrics/IMetricProvider.cs`**

```csharp
using System.Collections.Generic;

namespace Graphs.Metrics;

/// Implemented by each category-specific provider. Providers are collected
/// by the registry at Load(). Providers whose backing game services aren't
/// available (e.g. Iron Teeth-only in a Folktails save) can return an empty
/// list and the registry will ignore them.
public interface IMetricProvider
{
    IEnumerable<MetricDefinition> GetMetrics();
}
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add Graphs/Metrics/MetricDefinition.cs Graphs/Metrics/IMetricProvider.cs
git commit -m "Graphs: add MetricDefinition and IMetricProvider"
```

---

## Task 5: Implement `MetricHistory` ring buffer

Pure C#, no game dependencies. Holds `capacity` samples, each being
`(float[] values, byte weather, float gameDayTimestamp)`. Wraps on overflow.

**Files:**
- Create: `Graphs/Metrics/MetricHistory.cs`

- [ ] **Step 1: Write `Graphs/Metrics/MetricHistory.cs`**

```csharp
using System;

namespace Graphs.Metrics;

/// Fixed-capacity ring buffer of metric samples.
/// Layout: values is a [capacity][metricCount] 2D layout stored in a single
/// flat float array for cache locality. Weather and timestamps are parallel arrays.
public sealed class MetricHistory
{
    public const byte WeatherTemperate = 0;
    public const byte WeatherDrought = 1;
    public const byte WeatherBadtide = 2;

    private readonly int _capacity;
    private readonly int _metricCount;
    private readonly float[] _values;        // length = capacity * metricCount
    private readonly byte[] _weather;        // length = capacity
    private readonly float[] _timestamps;    // length = capacity (DaysSinceStart at sample time)

    private int _head;    // index of the next write slot
    private int _count;   // number of valid samples (caps at _capacity)

    public int MetricCount => _metricCount;
    public int Count => _count;
    public int Capacity => _capacity;

    public MetricHistory(int capacity, int metricCount)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (metricCount < 0) throw new ArgumentOutOfRangeException(nameof(metricCount));
        _capacity = capacity;
        _metricCount = metricCount;
        _values = new float[capacity * metricCount];
        _weather = new byte[capacity];
        _timestamps = new float[capacity];
    }

    /// Append a new sample. `values.Length` must equal `MetricCount`.
    public void Append(ReadOnlySpan<float> values, byte weather, float timestamp)
    {
        if (values.Length != _metricCount)
            throw new ArgumentException(
                $"Expected {_metricCount} values, got {values.Length}.", nameof(values));

        int offset = _head * _metricCount;
        values.CopyTo(new Span<float>(_values, offset, _metricCount));
        _weather[_head] = weather;
        _timestamps[_head] = timestamp;

        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    /// Oldest-to-newest enumeration of sample indices in the logical buffer.
    /// Returns the physical slot index for each step.
    public int PhysicalIndex(int logicalIndex)
    {
        if ((uint)logicalIndex >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(logicalIndex));
        int start = _count < _capacity ? 0 : _head;
        return (start + logicalIndex) % _capacity;
    }

    /// Read sample values at a logical index (0 = oldest).
    public void ReadValues(int logicalIndex, Span<float> dest)
    {
        if (dest.Length != _metricCount)
            throw new ArgumentException("dest size mismatch");
        int phys = PhysicalIndex(logicalIndex);
        new ReadOnlySpan<float>(_values, phys * _metricCount, _metricCount).CopyTo(dest);
    }

    public float ReadValue(int logicalIndex, int metricIndex)
    {
        int phys = PhysicalIndex(logicalIndex);
        return _values[phys * _metricCount + metricIndex];
    }

    public byte ReadWeather(int logicalIndex) => _weather[PhysicalIndex(logicalIndex)];
    public float ReadTimestamp(int logicalIndex) => _timestamps[PhysicalIndex(logicalIndex)];

    /// Returns the lowest logical index whose timestamp >= threshold, or
    /// Count if all samples are older. Timestamps are monotonic.
    public int FindFirstAtOrAfter(float timestamp)
    {
        // Linear scan — Count is at most 48 000, called at most ~60 times/sec
        // during chart redraw — fine.
        for (int i = 0; i < _count; i++)
            if (ReadTimestamp(i) >= timestamp) return i;
        return _count;
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/Metrics/MetricHistory.cs
git commit -m "Graphs: add MetricHistory ring buffer"
```

---

## Task 6: Implement `DistrictFilter` shared state

A tiny singleton holding the currently-selected district filter value (null = "all districts"). The sampler reads it each sample; the UI writes to it when the dropdown changes.

**Files:**
- Create: `Graphs/Metrics/DistrictFilter.cs`

- [ ] **Step 1: Write `Graphs/Metrics/DistrictFilter.cs`**

```csharp
using System;

namespace Graphs.Metrics;

/// Shared mutable state: which district the user has selected in the legend.
/// `null` means "all districts" (settlement-wide aggregation).
/// The UI mutates this; the sampler reads it at sample time.
public sealed class DistrictFilter
{
    public string? DistrictName { get; private set; }
    public event Action? Changed;

    public void Set(string? districtName)
    {
        if (DistrictName == districtName) return;
        DistrictName = districtName;
        Changed?.Invoke();
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/Metrics/DistrictFilter.cs
git commit -m "Graphs: add DistrictFilter shared state"
```

---

## Task 7: Implement `MetricRegistry` collector

Collects all `IMetricProvider` instances via constructor-injected `IEnumerable<IMetricProvider>`, flattens their metrics, and publishes the stable ordered list.

**Files:**
- Create: `Graphs/Metrics/MetricRegistry.cs`

- [ ] **Step 1: Write `Graphs/Metrics/MetricRegistry.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Flattens all registered IMetricProviders into a single ordered metric list
/// available to both the sampler and the UI. Order is:
///   1. By category enum order
///   2. Within category, by provider iteration order (providers append in
///      the order they want; goods provider sorts by display key).
public sealed class MetricRegistry : ILoadableSingleton
{
    private readonly IEnumerable<IMetricProvider> _providers;
    private readonly List<MetricDefinition> _metrics = new();
    private readonly Dictionary<string, int> _idToIndex = new();

    public IReadOnlyList<MetricDefinition> Metrics => _metrics;
    public int Count => _metrics.Count;

    public MetricRegistry(IEnumerable<IMetricProvider> providers)
    {
        _providers = providers;
    }

    public void Load()
    {
        var byCategory = new SortedDictionary<MetricCategory, List<MetricDefinition>>();

        foreach (var provider in _providers)
        {
            IEnumerable<MetricDefinition> defs;
            try
            {
                defs = provider.GetMetrics();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Graphs] Provider {provider.GetType().Name} failed: {ex.Message}");
                continue;
            }

            foreach (var def in defs)
            {
                if (!byCategory.TryGetValue(def.Category, out var list))
                {
                    list = new List<MetricDefinition>();
                    byCategory[def.Category] = list;
                }
                list.Add(def);
            }
        }

        foreach (var list in byCategory.Values)
            foreach (var def in list)
            {
                if (_idToIndex.ContainsKey(def.Id))
                {
                    Debug.LogWarning($"[Graphs] Duplicate metric id ignored: {def.Id}");
                    continue;
                }
                _idToIndex[def.Id] = _metrics.Count;
                _metrics.Add(def);
            }

        Debug.Log($"[Graphs] MetricRegistry loaded {_metrics.Count} metrics.");
    }

    public int IndexOf(string id) =>
        _idToIndex.TryGetValue(id, out var i) ? i : -1;
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/Metrics/MetricRegistry.cs
git commit -m "Graphs: add MetricRegistry"
```

---

## Task 8: Implement `WeatherStateSampler`

Returns the current weather byte for each sample. Directly injects the
hazardous-weather service so the configurator stays simple.

**Files:**
- Create: `Graphs/Metrics/WeatherStateSampler.cs`

- [ ] **Step 1: Write `Graphs/Metrics/WeatherStateSampler.cs`**

```csharp
using System;
using Timberborn.HazardousWeatherSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Resolves the active weather state for a sample. Isolated so that the
/// exact game-API binding lives in one place — if the weather API shape
/// changes across game versions, only this class needs updating.
public sealed class WeatherStateSampler
{
    private readonly HazardousWeatherService _weather;

    public WeatherStateSampler(HazardousWeatherService weather)
    {
        _weather = weather;
    }

    /// Returns one of MetricHistory.Weather{Temperate,Drought,Badtide}.
    public byte Sample()
    {
        try
        {
            var hw = _weather.CurrentCycleHazardousWeather;
            if (hw == null) return MetricHistory.WeatherTemperate;
            string name = hw.GetType().Name;
            if (name.IndexOf("Badtide", StringComparison.OrdinalIgnoreCase) >= 0)
                return MetricHistory.WeatherBadtide;
            if (name.IndexOf("Drought", StringComparison.OrdinalIgnoreCase) >= 0)
                return MetricHistory.WeatherDrought;
            return MetricHistory.WeatherTemperate;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] Weather sample failed: {ex.Message}");
            return MetricHistory.WeatherTemperate;
        }
    }
}
```

Note: `CurrentCycleHazardousWeather` gives the hazardous weather scheduled
for the current cycle. If the game exposes a cleaner "is hazardous weather
actually active right now" API, swap to that — update `NOTES.md` too. Doing
type-name sniffing is acceptable for phase 1 because it's confined to this
one file.

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/Metrics/WeatherStateSampler.cs
git commit -m "Graphs: add WeatherStateSampler"
```

---

## Task 9: Implement `MetricSampler`

Ticks every frame; decides if a full game-hour has passed since last sample; if so, walks the registry, calls each `ValueFn`, writes one row into the history.

**Files:**
- Create: `Graphs/Metrics/MetricSampler.cs`

- [ ] **Step 1: Write `Graphs/Metrics/MetricSampler.cs`**

```csharp
using System;
using System.Collections.Generic;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Samples every registered metric once per in-game hour.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton
{
    public const int MaxSamples = 48_000; // 2000 days * 24 hours

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly WeatherStateSampler _weather;

    private MetricHistory? _history;
    private int _lastHourIndex = int.MinValue;
    private float[]? _scratch;

    // Metrics that have already logged a failure this session — so we log each once.
    private readonly HashSet<string> _loggedFailures = new();

    public MetricHistory History
        => _history ?? throw new InvalidOperationException("MetricSampler not loaded yet.");

    public event Action? OnSampled;

    public MetricSampler(
        IDayNightCycle dayNightCycle,
        MetricRegistry registry,
        DistrictFilter filter,
        WeatherStateSampler weather)
    {
        _dayNightCycle = dayNightCycle;
        _registry = registry;
        _filter = filter;
        _weather = weather;
    }

    public void Load()
    {
        _history = new MetricHistory(MaxSamples, _registry.Count);
        _scratch = new float[_registry.Count];
    }

    public void Tick()
    {
        if (_history is null || _scratch is null) return;

        // Synthetic continuous day count.
        float partialDay = _dayNightCycle.DayNumber + _dayNightCycle.DayProgress;
        int hourIndex = (int)Math.Floor(partialDay * 24f);

        if (hourIndex == _lastHourIndex) return;

        // Take one sample regardless of how many hours actually passed since
        // last tick — we don't try to backfill missed hours (e.g. after game speed up).
        _lastHourIndex = hourIndex;
        TakeSample(partialDay);
    }

    private void TakeSample(float partialDay)
    {
        var metrics = _registry.Metrics;
        string? district = _filter.DistrictName;

        for (int i = 0; i < metrics.Count; i++)
        {
            var def = metrics[i];
            try { _scratch![i] = def.ValueFn(district); }
            catch (Exception ex)
            {
                _scratch![i] = float.NaN;
                if (_loggedFailures.Add(def.Id))
                    Debug.LogWarning(
                        $"[Graphs] Metric '{def.Id}' threw on first sample: {ex.Message}");
            }
        }

        byte weather = _weather.Sample();
        _history!.Append(_scratch, weather, partialDay);

        try { OnSampled?.Invoke(); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] OnSampled handler threw: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/Metrics/MetricSampler.cs
git commit -m "Graphs: add MetricSampler"
```

---

## Task 10: Implement `ScienceMetricProvider`

Simplest provider, settlement-scope. Proves end-to-end flow.

**Files:**
- Create: `Graphs/Metrics/Providers/ScienceMetricProvider.cs`

- [ ] **Step 1: Write `Graphs/Metrics/Providers/ScienceMetricProvider.cs`**

```csharp
using System.Collections.Generic;
using Timberborn.ScienceSystem;

namespace Graphs.Metrics.Providers;

public sealed class ScienceMetricProvider : IMetricProvider
{
    private readonly ScienceService _scienceService;

    public ScienceMetricProvider(ScienceService scienceService)
    {
        _scienceService = scienceService;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return new MetricDefinition(
            id: "science.stored",
            nameLocKey: "Graphs.Metric.ScienceStored",
            category: MetricCategory.Science,
            scope: MetricScope.Settlement,
            valueFn: _ => _scienceService.SciencePoints);
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success. If `SciencePoints` is misnamed, consult `NOTES.md` and
correct both the provider and `NOTES.md`.

- [ ] **Step 3: Commit**

```bash
git add Graphs/Metrics/Providers/ScienceMetricProvider.cs
git commit -m "Graphs: add ScienceMetricProvider"
```

---

## Task 11: Implement `GraphsConfigurator` wiring data layer + verify sampling

Bindito configurator that registers everything built so far, wires the weather function, and registers the `ScienceMetricProvider`. First in-game verification: logs the number of metrics and that samples are being taken.

**Files:**
- Create: `Graphs/GraphsConfigurator.cs`

- [ ] **Step 1: Write `Graphs/GraphsConfigurator.cs`**

```csharp
using Bindito.Core;
using Graphs.Metrics;
using Graphs.Metrics.Providers;

namespace Graphs;

[Context("Game")]
public class GraphsConfigurator : Configurator
{
    protected override void Configure()
    {
        // Shared state
        Bind<DistrictFilter>().AsSingleton();

        // Weather + registry + sampler
        Bind<WeatherStateSampler>().AsSingleton();
        Bind<MetricRegistry>().AsSingleton();
        Bind<MetricSampler>().AsSingleton();

        // Providers (multibind as IMetricProvider)
        MultiBind<IMetricProvider>().To<ScienceMetricProvider>().AsSingleton();
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Launch game with a save, verify log output**

Launch Timberborn, load a save. Open the Player.log
(`~/Library/Logs/Mechanistry/Timberborn/Player.log` on macOS).

Expected entries (may take a few seconds to appear):
- `[Graphs] MetricRegistry loaded 1 metrics.`

Let the game run for 30+ in-game seconds at fastest speed. No errors related
to Graphs should appear.

If `MetricRegistry loaded N metrics` never appears, the configurator is not
being picked up — verify the namespace, `[Context("Game")]`, and that the
`.dll` is actually deployed.

- [ ] **Step 4: Commit**

```bash
git add Graphs/GraphsConfigurator.cs
git commit -m "Graphs: wire data layer through GraphsConfigurator"
```

---

## Task 12: Implement `PopulationMetricProvider` — core counts

Adds Total Beavers, Adults, Kits, Bots, Homeless, Unemployed. Injured/Infected/Contaminated come in Task 13 (they use different subsystems — keeping them separate so this task is provable).

**Files:**
- Create: `Graphs/Metrics/Providers/PopulationMetricProvider.cs`
- Modify: `Graphs/GraphsConfigurator.cs:22` (add MultiBind)

- [ ] **Step 1: Write `Graphs/Metrics/Providers/PopulationMetricProvider.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Timberborn.GameDistricts;
using Timberborn.PopulationStatisticsSystem;

namespace Graphs.Metrics.Providers;

public sealed class PopulationMetricProvider : IMetricProvider
{
    private readonly DistrictCenterRegistry _districts;
    private readonly IDwellingStatisticsProvider _dwelling;
    private readonly IEmploymentStatisticsProvider _employment;

    public PopulationMetricProvider(
        DistrictCenterRegistry districts,
        IDwellingStatisticsProvider dwelling,
        IEmploymentStatisticsProvider employment)
    {
        _districts = districts;
        _dwelling = dwelling;
        _employment = employment;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return Total("pop.total", "Graphs.Metric.TotalBeavers", d => d.DistrictPopulation);
        yield return Total("pop.adults", "Graphs.Metric.Adults", d => d.NumberOfAdults);
        yield return Total("pop.kits",   "Graphs.Metric.Kits",   d => d.DistrictPopulation - d.NumberOfAdults);
        yield return Total("pop.bots",   "Graphs.Metric.Bots",   d => d.NumberOfBots);

        yield return new MetricDefinition(
            "pop.homeless", "Graphs.Metric.Homeless",
            MetricCategory.Population, MetricScope.District,
            districtName => CountDwelling(districtName, homelessOnly: true));

        yield return new MetricDefinition(
            "pop.unemployed", "Graphs.Metric.Unemployed",
            MetricCategory.Population, MetricScope.District,
            districtName => CountEmployment(districtName, unemployedOnly: true));
    }

    private MetricDefinition Total(
        string id, string locKey, System.Func<DistrictCenter, int> countPerDistrict) =>
        new(id, locKey, MetricCategory.Population, MetricScope.District,
            districtName =>
            {
                if (districtName is null)
                    return _districts.FinishedDistrictCenters.Sum(countPerDistrict);

                var d = _districts.FinishedDistrictCenters
                    .FirstOrDefault(x => x.DistrictName == districtName);
                return d == null ? 0 : countPerDistrict(d);
            });

    private float CountDwelling(string? districtName, bool homelessOnly)
    {
        // Exact provider method resolved in NOTES.md (Task 2). Likely shape:
        //   var stats = _dwelling.GetDwellingStatistics(district) -> (Homed, Homeless) counts
        // Falls back to iterating all finished districts when districtName is null.
        int sum = 0;
        foreach (var d in _districts.FinishedDistrictCenters)
        {
            if (districtName != null && d.DistrictName != districtName) continue;
            var stats = _dwelling.GetDwellingStatistics(d);
            sum += homelessOnly ? stats.Homeless : stats.Homed;
        }
        return sum;
    }

    private float CountEmployment(string? districtName, bool unemployedOnly)
    {
        int sum = 0;
        foreach (var d in _districts.FinishedDistrictCenters)
        {
            if (districtName != null && d.DistrictName != districtName) continue;
            var stats = _employment.GetEmploymentStatistics(d);
            sum += unemployedOnly ? stats.Unemployed : stats.Employed;
        }
        return sum;
    }
}
```

Note: property names `stats.Homeless` / `stats.Unemployed` / `stats.Homed`
and the method name `GetDwellingStatistics(DistrictCenter)` are the
expected shape. If probing in Task 2 showed different exact names,
update this provider accordingly.

- [ ] **Step 2: Register provider in configurator**

Modify `Graphs/GraphsConfigurator.cs` — add inside `Configure()` after the
`ScienceMetricProvider` bind:

```csharp
MultiBind<IMetricProvider>().To<PopulationMetricProvider>().AsSingleton();
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`

Expected: success. If the dwelling/employment statistics API shape differs,
Task 2 (NOTES.md) should be updated and this provider adapted to match.

- [ ] **Step 4: Launch game, verify log**

Load a save. Expected: `[Graphs] MetricRegistry loaded 7 metrics.`

- [ ] **Step 5: Commit**

```bash
git add Graphs/Metrics/Providers/PopulationMetricProvider.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add PopulationMetricProvider for core counts"
```

---

## Task 13: Extend `PopulationMetricProvider` with injured / infected / contaminated

These three use different game subsystems (Healthcare, ContaminationIncubator, ContaminationStatistics) so they're a separate change on the same file.

**Files:**
- Modify: `Graphs/Metrics/Providers/PopulationMetricProvider.cs`

- [ ] **Step 1: Add constructor-injected dependencies**

At the top of the class, extend constructor parameters and fields:

```csharp
using Timberborn.BeaverContaminationSystem;
using Timberborn.Characters;
using Timberborn.EntitySystem;    // for EntityRegistry; adjust to correct namespace per NOTES.md
using Timberborn.Healthcare;      // for Injurable; adjust per NOTES.md
```

Inside the class, add:

```csharp
private readonly EntityRegistry _entities;
private readonly IContaminationStatisticsProvider _contamination;
```

And extend the constructor:

```csharp
public PopulationMetricProvider(
    DistrictCenterRegistry districts,
    IDwellingStatisticsProvider dwelling,
    IEmploymentStatisticsProvider employment,
    EntityRegistry entities,
    IContaminationStatisticsProvider contamination)
{
    _districts = districts;
    _dwelling = dwelling;
    _employment = employment;
    _entities = entities;
    _contamination = contamination;
}
```

- [ ] **Step 2: Extend `GetMetrics()` with three new entries**

After the `pop.unemployed` yield, add:

```csharp
yield return new MetricDefinition(
    "pop.injured", "Graphs.Metric.Injured",
    MetricCategory.Population, MetricScope.District,
    districtName => CountBeavers(districtName, b =>
    {
        var injurable = b.GetComponent<Injurable>();
        return injurable != null && injurable.IsInjured;
    }));

yield return new MetricDefinition(
    "pop.infected", "Graphs.Metric.Infected",
    MetricCategory.Population, MetricScope.District,
    districtName => CountBeavers(districtName, b =>
    {
        var inc = b.GetComponent<ContaminationIncubator>();
        return inc != null && inc.IsIncubating;
    }));

yield return new MetricDefinition(
    "pop.contaminated", "Graphs.Metric.Contaminated",
    MetricCategory.Population, MetricScope.District,
    districtName => CountContaminated(districtName));
```

- [ ] **Step 3: Add `CountBeavers` and `CountContaminated` helpers**

```csharp
private float CountBeavers(string? districtName, System.Func<Character, bool> predicate)
{
    int count = 0;
    foreach (var entity in _entities.Entities)
    {
        var character = entity.GetComponent<Character>();
        if (character is null) continue;
        if (districtName != null)
        {
            // Each Character has an AssignedDistrict via DistrictBuilding;
            // resolve to the district name and filter.
            var district = character.GetComponent<DistrictBuilding>()?.District;
            if (district == null || district.DistrictName != districtName) continue;
        }
        try { if (predicate(character)) count++; }
        catch { /* per-beaver component resolution may throw on edge cases */ }
    }
    return count;
}

private float CountContaminated(string? districtName)
{
    if (districtName is null)
        return _contamination.GetContaminationStatistics().Contaminated;

    foreach (var d in _districts.FinishedDistrictCenters)
        if (d.DistrictName == districtName)
            return _contamination.GetContaminationStatistics(d).Contaminated;

    return 0;
}
```

Exact method names (`IsIncubating`, `IsInjured`, `GetContaminationStatistics`,
`.Contaminated`) to be confirmed in Task 2's NOTES.md and corrected here if
different.

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`

Expected: success. If the `Injurable` or `ContaminationIncubator` types are
in different namespaces/have different member names, update here and in
NOTES.md.

- [ ] **Step 5: Launch game, verify log**

Load a save. Expected: `[Graphs] MetricRegistry loaded 10 metrics.`

- [ ] **Step 6: Commit**

```bash
git add Graphs/Metrics/Providers/PopulationMetricProvider.cs
git commit -m "Graphs: track injured, infected, contaminated beavers"
```

---

## Task 14: Implement `GoodsMetricProvider`

Enumerates all registered good ids at Load() and creates one metric per good. Value = total stockpile of that good (all districts or a specific district).

**Files:**
- Create: `Graphs/Metrics/Providers/GoodsMetricProvider.cs`
- Modify: `Graphs/GraphsConfigurator.cs` (MultiBind)

- [ ] **Step 1: Write `Graphs/Metrics/Providers/GoodsMetricProvider.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;

namespace Graphs.Metrics.Providers;

public sealed class GoodsMetricProvider : IMetricProvider
{
    private readonly GoodSpecService _goodSpecs;
    private readonly DistrictInventoryRegistry _inventories;
    private readonly DistrictCenterRegistry _districts;

    public GoodsMetricProvider(
        GoodSpecService goodSpecs,
        DistrictInventoryRegistry inventories,
        DistrictCenterRegistry districts)
    {
        _goodSpecs = goodSpecs;
        _inventories = inventories;
        _districts = districts;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        // Sort goods alphabetically by id so the legend is deterministic.
        var goodIds = _goodSpecs.Specs
            .Select(s => s.Id)
            .OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var goodId in goodIds)
        {
            string captured = goodId; // avoid closure-capture pitfall
            yield return new MetricDefinition(
                id: $"good.{captured}",
                nameLocKey: $"Good.{captured}",   // reuses game's good-name locs
                category: MetricCategory.Goods,
                scope: MetricScope.Either,
                valueFn: districtName => TotalStock(captured, districtName));
        }
    }

    private float TotalStock(string goodId, string? districtName)
    {
        float total = 0;

        if (districtName is null)
        {
            foreach (var inv in _inventories.AllInventories)
                total += StockOf(inv, goodId);
            return total;
        }

        // Filter inventories by their owning district's name.
        foreach (var inv in _inventories.AllInventories)
        {
            if (inv.District?.DistrictName != districtName) continue;
            total += StockOf(inv, goodId);
        }
        return total;
    }

    private static float StockOf(Inventory inv, string goodId)
    {
        // Each Inventory has a `Stock` of type GoodAmountStock with `Get(goodId)` -> int.
        // If the actual shape differs, adjust per NOTES.md.
        try { return inv.Stock.Get(goodId); }
        catch { return 0; }
    }
}
```

- [ ] **Step 2: Register provider in configurator**

Add to `GraphsConfigurator.Configure()`:

```csharp
MultiBind<IMetricProvider>().To<GoodsMetricProvider>().AsSingleton();
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`

Expected: success. If `Inventory.District` or `Stock.Get(goodId)` shapes
differ, adjust this provider and NOTES.md.

- [ ] **Step 4: Launch game, verify log**

Load a mid-game save. Expected:
`[Graphs] MetricRegistry loaded ~50+ metrics.` (exactly 10 + count of
registered goods in the loaded save's faction mix.)

- [ ] **Step 5: Commit**

```bash
git add Graphs/Metrics/Providers/GoodsMetricProvider.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add GoodsMetricProvider with per-district totals"
```

---

## Task 15: Implement `WellbeingMetricProvider`

Average + min wellbeing, average hunger + thirst. These are per-beaver values averaged per district.

**Files:**
- Create: `Graphs/Metrics/Providers/WellbeingMetricProvider.cs`
- Modify: `Graphs/GraphsConfigurator.cs` (MultiBind)

- [ ] **Step 1: Write `Graphs/Metrics/Providers/WellbeingMetricProvider.cs`**

```csharp
using System;
using System.Collections.Generic;
using Timberborn.Characters;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.NeedSystem;
using Timberborn.Wellbeing;

namespace Graphs.Metrics.Providers;

public sealed class WellbeingMetricProvider : IMetricProvider
{
    private const string HungerNeedId = "Hunger";
    private const string ThirstNeedId = "Thirst";

    private readonly EntityRegistry _entities;

    public WellbeingMetricProvider(EntityRegistry entities)
    {
        _entities = entities;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return Aggregate("wellbeing.avg", "Graphs.Metric.AvgWellbeing",
            Aggregation.Average, BeaverWellbeing);
        yield return Aggregate("wellbeing.min", "Graphs.Metric.MinWellbeing",
            Aggregation.Minimum, BeaverWellbeing);
        yield return Aggregate("need.hunger.avg", "Graphs.Metric.AvgHunger",
            Aggregation.Average, b => NeedSatisfaction(b, HungerNeedId));
        yield return Aggregate("need.thirst.avg", "Graphs.Metric.AvgThirst",
            Aggregation.Average, b => NeedSatisfaction(b, ThirstNeedId));
    }

    private enum Aggregation { Average, Minimum }

    private MetricDefinition Aggregate(
        string id, string locKey, Aggregation aggregation, Func<Character, float?> extract) =>
        new(id, locKey, MetricCategory.Wellbeing, MetricScope.District,
            districtName =>
            {
                float sum = 0;
                float min = float.PositiveInfinity;
                int count = 0;

                foreach (var entity in _entities.Entities)
                {
                    var character = entity.GetComponent<Character>();
                    if (character is null) continue;
                    if (districtName != null)
                    {
                        var district = character.GetComponent<DistrictBuilding>()?.District;
                        if (district == null || district.DistrictName != districtName) continue;
                    }
                    var v = extract(character);
                    if (!v.HasValue) continue;
                    sum += v.Value;
                    if (v.Value < min) min = v.Value;
                    count++;
                }

                if (count == 0) return float.NaN;
                return aggregation switch
                {
                    Aggregation.Average => sum / count,
                    Aggregation.Minimum => min,
                    _ => float.NaN,
                };
            });

    private static float? BeaverWellbeing(Character c)
    {
        var w = c.GetComponent<Wellbeing>();
        return w == null ? (float?)null : w.Points;
    }

    private static float? NeedSatisfaction(Character c, string needId)
    {
        var nm = c.GetComponent<NeedManager>();
        if (nm == null) return null;
        try { return nm.GetSatisfaction(needId); }
        catch { return null; }
    }
}
```

- [ ] **Step 2: Register provider in configurator**

Add to `GraphsConfigurator.Configure()`:

```csharp
MultiBind<IMetricProvider>().To<WellbeingMetricProvider>().AsSingleton();
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`

Expected: success. `Wellbeing.Points`, `NeedManager.GetSatisfaction(needId)`,
and the `"Hunger"`/`"Thirst"` need ids are the expected shape — adjust per
NOTES.md if different.

- [ ] **Step 4: Launch game, verify log**

Load a save. Expected: `[Graphs] MetricRegistry loaded ~60+ metrics.`

- [ ] **Step 5: Commit**

```bash
git add Graphs/Metrics/Providers/WellbeingMetricProvider.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add WellbeingMetricProvider"
```

---

## Task 16: Implement `GraphColors`

Stable HSL palette: deterministic colors per metric id so the legend swatch and the chart line always match.

**Files:**
- Create: `Graphs/UI/GraphColors.cs`

- [ ] **Step 1: Write `Graphs/UI/GraphColors.cs`**

```csharp
using UnityEngine;

namespace Graphs.UI;

/// Deterministic color palette keyed on a metric id. Uses a hash of the id
/// to pick a hue, then fixes S and V per-category to keep related metrics
/// visually coherent.
public static class GraphColors
{
    public static Color ColorFor(string metricId, Graphs.Metrics.MetricCategory category)
    {
        float hue = (Hash(metricId) % 360u) / 360f;

        (float sat, float val) = category switch
        {
            Graphs.Metrics.MetricCategory.Goods      => (0.70f, 0.95f),
            Graphs.Metrics.MetricCategory.Population => (0.80f, 0.85f),
            Graphs.Metrics.MetricCategory.Science    => (0.55f, 0.95f),
            Graphs.Metrics.MetricCategory.Wellbeing  => (0.60f, 0.80f),
            _                                        => (0.60f, 0.90f),
        };

        return Color.HSVToRGB(hue, sat, val);
    }

    private static uint Hash(string s)
    {
        // Deterministic FNV-1a across processes/runs; Unity colors from
        // the same id always match.
        const uint fnvOffset = 2166136261u;
        const uint fnvPrime = 16777619u;
        uint h = fnvOffset;
        foreach (char c in s)
        {
            h ^= c;
            h *= fnvPrime;
        }
        return h;
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/UI/GraphColors.cs
git commit -m "Graphs: add deterministic GraphColors palette"
```

---

## Task 17: Implement `GraphsHotkey`

Polls each tick for Shift+G to toggle the window. Injects `GraphsWindow`
directly so we avoid needing a ToProvider lambda in the configurator.
(Forward declaration: `GraphsWindow` is implemented in Task 18; this task
references it by name but doesn't break the build because DI resolves at
runtime — until Task 18 is done, just leave the file referring to
`GraphsWindow` and the compile will succeed as long as `GraphsWindow`
exists even as an empty class. We scaffold the empty class in Step 1.)

**Files:**
- Create: `Graphs/UI/GraphsHotkey.cs`
- Create: `Graphs/UI/GraphsWindow.cs` (empty stub, filled in Task 18)

- [ ] **Step 1: Scaffold empty `GraphsWindow` stub**

```csharp
namespace Graphs.UI;

/// Stub filled in by Task 18. Keeps the hotkey compilable.
public sealed class GraphsWindow
{
    public bool IsOpen { get; private set; }
    public void Toggle() { IsOpen = !IsOpen; }
    public void Open()  { IsOpen = true;  }
    public void Close() { IsOpen = false; }
}
```

- [ ] **Step 2: Write `Graphs/UI/GraphsHotkey.cs`**

```csharp
using Timberborn.InputSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Graphs.UI;

/// Polls for the Shift+G chord each tick and toggles the graphs window.
/// We debounce on the G key's rising edge so holding the chord doesn't
/// rapid-fire the toggle.
public sealed class GraphsHotkey : ITickableSingleton
{
    private readonly InputService _input;
    private readonly GraphsWindow _window;
    private bool _prevPressed;

    public GraphsHotkey(InputService input, GraphsWindow window)
    {
        _input = input;
        _window = window;
    }

    public void Tick()
    {
        // Shift + G. We use Unity's `Input` directly — matches how the
        // in-game dev console detects its hotkey chord. Task 27 tightens
        // this by checking InputService to suppress hotkeys during text
        // input.
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool g = Input.GetKey(KeyCode.G);
        bool pressed = shift && g;
        if (pressed && !_prevPressed) _window.Toggle();
        _prevPressed = pressed;
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Graphs/UI/GraphsHotkey.cs
git commit -m "Graphs: add GraphsHotkey"
```

---

## Task 18: Implement `GraphsWindow` shell + controller wiring

Full-screen panel: title bar with X button, close on Esc or Shift+G, else empty body (we'll fill it in Tasks 19–22). Overwrites the stub from Task 17.

**Files:**
- Modify: `Graphs/UI/GraphsWindow.cs` (overwrite stub from Task 17)
- Modify: `Graphs/GraphsConfigurator.cs`

- [ ] **Step 1: Overwrite `Graphs/UI/GraphsWindow.cs`**

```csharp
using System;
using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Controller owning the graphs window lifecycle (build / show / hide /
/// dispose). One instance, DI-registered as a singleton. Holds the open
/// state so the hotkey can toggle it.
public sealed class GraphsWindow
{
    private readonly RootVisualElementProvider _rootProvider;
    private VisualElement? _root;

    public bool IsOpen => _root != null;

    public GraphsWindow(RootVisualElementProvider rootProvider)
    {
        _rootProvider = rootProvider;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;

        _root = Build();
        _rootProvider.GetRootVisualElement().Add(_root);
        _root.Focus();
    }

    public void Close()
    {
        if (_root == null) return;
        _root.RemoveFromHierarchy();
        _root = null;
    }

    private VisualElement Build()
    {
        var root = new VisualElement { name = "graphs-window" };
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        root.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.10f, 0.96f));
        root.focusable = true;
        root.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
        });

        var titleBar = new VisualElement { name = "graphs-title" };
        titleBar.style.height = 44;
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.justifyContent = Justify.SpaceBetween;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 16;
        titleBar.style.paddingRight = 8;
        titleBar.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.18f));

        var title = new Label("Graphs");
        title.style.color = Color.white;
        title.style.fontSize = 20;
        titleBar.Add(title);

        var closeBtn = new Button(Close) { text = "X" };
        closeBtn.style.width = 32; closeBtn.style.height = 32;
        titleBar.Add(closeBtn);

        root.Add(titleBar);

        var body = new VisualElement { name = "graphs-body" };
        body.style.flexGrow = 1;
        body.style.flexDirection = FlexDirection.Row;
        root.Add(body);

        var chartSlot = new VisualElement { name = "graphs-chart-slot" };
        chartSlot.style.flexGrow = 1;
        chartSlot.style.marginLeft = 12;
        chartSlot.style.marginTop = 12;
        chartSlot.style.marginBottom = 12;
        body.Add(chartSlot);

        var legendSlot = new VisualElement { name = "graphs-legend-slot" };
        legendSlot.style.width = 320;
        legendSlot.style.marginRight = 8;
        legendSlot.style.marginTop = 12;
        legendSlot.style.marginBottom = 12;
        body.Add(legendSlot);

        var bottom = new VisualElement { name = "graphs-bottom" };
        bottom.style.height = 48;
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.justifyContent = Justify.Center;
        bottom.style.alignItems = Align.Center;
        root.Add(bottom);

        return root;
    }
}
```

- [ ] **Step 2: Wire into configurator**

In `Graphs/GraphsConfigurator.cs`, add:

```csharp
using Graphs.UI;
```

and inside `Configure()`:

```csharp
Bind<GraphsWindow>().AsSingleton();
Bind<GraphsHotkey>().AsSingleton();
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 4: In-game verification**

Load a save. Press Shift+G. Expected: a dark near-black full-screen panel
appears with a title bar saying "Graphs" and an "X" button top-right. The
rest of the panel is empty.

Press Esc. Window closes.
Press Shift+G again. Window opens.
Click X. Window closes.

- [ ] **Step 5: Commit**

```bash
git add Graphs/UI/GraphsWindow.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add window shell + hotkey wiring"
```

---

## Task 19: Implement `GraphsRangeSelector`

Three mutually-exclusive buttons at the bottom: 5 days / 30 days / All. Exposes a `CurrentRange` + `Changed` event that the chart subscribes to.

**Files:**
- Create: `Graphs/UI/GraphsRangeSelector.cs`
- Modify: `Graphs/UI/GraphsWindow.cs`

- [ ] **Step 1: Write `Graphs/UI/GraphsRangeSelector.cs`**

```csharp
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public enum GraphRange { FiveDays, ThirtyDays, All }

public sealed class GraphsRangeSelector
{
    public GraphRange CurrentRange { get; private set; } = GraphRange.ThirtyDays;
    public event Action? Changed;

    public VisualElement Build()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        Button Make(string label, GraphRange range)
        {
            var btn = new Button(() => Set(range)) { text = label };
            btn.style.width = 100; btn.style.height = 36;
            btn.style.marginLeft = 6; btn.style.marginRight = 6;
            btn.userData = range;
            return btn;
        }

        row.Add(Make("5 days", GraphRange.FiveDays));
        row.Add(Make("30 days", GraphRange.ThirtyDays));
        row.Add(Make("All", GraphRange.All));

        foreach (var child in row.Children())
            ((Button)child).style.opacity = (GraphRange)child.userData == CurrentRange ? 1f : 0.6f;

        return row;

        void Set(GraphRange r)
        {
            if (r == CurrentRange) return;
            CurrentRange = r;
            foreach (var child in row.Children())
                ((Button)child).style.opacity = (GraphRange)child.userData == r ? 1f : 0.6f;
            Changed?.Invoke();
        }
    }

    /// How many in-game days the current range spans relative to "now".
    /// For All, returns null — meaning "plot all buffered samples".
    public float? LookbackDays() => CurrentRange switch
    {
        GraphRange.FiveDays   => 5f,
        GraphRange.ThirtyDays => 30f,
        GraphRange.All        => null,
        _                     => 30f,
    };
}
```

- [ ] **Step 2: Inject into `GraphsWindow`**

In `GraphsWindow.cs`, add field and constructor arg:

```csharp
private readonly GraphsRangeSelector _rangeSelector;

public GraphsWindow(
    RootVisualElementProvider rootProvider,
    GraphsRangeSelector rangeSelector)
{
    _rootProvider = rootProvider;
    _rangeSelector = rangeSelector;
}
```

At the end of `Build()`, replace the empty `bottom.Add` block with:

```csharp
bottom.Add(_rangeSelector.Build());
```

- [ ] **Step 3: Bind in configurator**

In `GraphsConfigurator.Configure()`:

```csharp
Bind<GraphsRangeSelector>().AsSingleton();
```

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 5: In-game verification**

Load save, press Shift+G. Expected: three buttons at the bottom — 5 days,
30 days (highlighted), All. Click between them — highlight switches.

- [ ] **Step 6: Commit**

```bash
git add Graphs/UI/GraphsRangeSelector.cs Graphs/UI/GraphsWindow.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add range selector"
```

---

## Task 20: Implement `GraphsDistrictSelector`

Dropdown above the legend. Reads finished districts; writes to `DistrictFilter`.

**Files:**
- Create: `Graphs/UI/GraphsDistrictSelector.cs`
- Modify: `Graphs/UI/GraphsWindow.cs`

- [ ] **Step 1: Write `Graphs/UI/GraphsDistrictSelector.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Graphs.Metrics;
using Timberborn.GameDistricts;
using UnityEngine.UIElements;

namespace Graphs.UI;

public sealed class GraphsDistrictSelector
{
    private const string AllDistrictsLabel = "All districts";

    private readonly DistrictCenterRegistry _districts;
    private readonly DistrictFilter _filter;

    public GraphsDistrictSelector(DistrictCenterRegistry districts, DistrictFilter filter)
    {
        _districts = districts;
        _filter = filter;
    }

    public VisualElement Build()
    {
        var names = new List<string> { AllDistrictsLabel };
        names.AddRange(_districts.FinishedDistrictCenters
            .Select(d => d.DistrictName)
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase));

        var dropdown = new DropdownField("District", names,
            _filter.DistrictName is null ? 0 : System.Math.Max(0, names.IndexOf(_filter.DistrictName)));
        dropdown.style.marginBottom = 8;
        dropdown.RegisterValueChangedCallback(evt =>
        {
            _filter.Set(evt.newValue == AllDistrictsLabel ? null : evt.newValue);
        });
        return dropdown;
    }
}
```

- [ ] **Step 2: Inject into `GraphsWindow`**

Add to ctor parameters + field:

```csharp
private readonly GraphsDistrictSelector _districtSelector;

public GraphsWindow(
    RootVisualElementProvider rootProvider,
    GraphsRangeSelector rangeSelector,
    GraphsDistrictSelector districtSelector)
{
    _rootProvider = rootProvider;
    _rangeSelector = rangeSelector;
    _districtSelector = districtSelector;
}
```

In `Build()`, before adding the legendSlot, add:

```csharp
legendSlot.Add(_districtSelector.Build());
```

- [ ] **Step 3: Bind in configurator**

Add to `GraphsConfigurator.Configure()`:

```csharp
Bind<GraphsDistrictSelector>().AsSingleton();
```

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 5: In-game verification**

Load a save with 1+ districts (or a fresh save with one district). Open
the window. Expected: a "District" dropdown appears in the right pane
with "All districts" + each district name. Changing it updates
`DistrictFilter` (we'll see the lines change once the chart exists).

- [ ] **Step 6: Commit**

```bash
git add Graphs/UI/GraphsDistrictSelector.cs Graphs/UI/GraphsWindow.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add district selector"
```

---

## Task 21: Implement `GraphsLegend`

Scrollable list of category sections. Each section is collapsible; each row is `[swatch] [checkbox] [name] [current value]`. Reads `MetricRegistry`; writes to an internal `HashSet<string> VisibleMetricIds`. Exposes `Changed` event.

**Files:**
- Create: `Graphs/UI/GraphsLegend.cs`
- Modify: `Graphs/UI/GraphsWindow.cs`

- [ ] **Step 1: Write `Graphs/UI/GraphsLegend.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Graphs.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Scrollable category-grouped list of per-metric checkbox rows.
/// Phase 1: curated default-visible set hard-coded.
public sealed class GraphsLegend
{
    private static readonly HashSet<string> DefaultVisible = new()
    {
        "good.Log", "good.Plank", "good.Water", "good.Berry",
        "good.MapleSyrup", "good.Gear", "good.Biofuel",
        "pop.total", "science.stored", "wellbeing.avg",
    };

    private readonly MetricRegistry _registry;

    public HashSet<string> VisibleMetricIds { get; } = new();
    public event Action? Changed;

    private readonly Dictionary<string, Label> _valueLabels = new();

    public GraphsLegend(MetricRegistry registry)
    {
        _registry = registry;
        foreach (var m in registry.Metrics)
            if (DefaultVisible.Contains(m.Id))
                VisibleMetricIds.Add(m.Id);
    }

    public VisualElement Build()
    {
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;

        foreach (var categoryGroup in _registry.Metrics
                     .GroupBy(m => m.Category)
                     .OrderBy(g => (int)g.Key))
        {
            scroll.Add(BuildCategorySection(categoryGroup.Key, categoryGroup));
        }

        return scroll;
    }

    /// Refresh the "current value" label next to each visible metric.
    /// Called by the window on new samples.
    public void UpdateCurrentValues(Func<string, float> valueOf)
    {
        foreach (var pair in _valueLabels)
        {
            var v = valueOf(pair.Key);
            pair.Value.text = float.IsNaN(v) ? "—" : v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private VisualElement BuildCategorySection(
        MetricCategory category, IEnumerable<MetricDefinition> metrics)
    {
        var section = new VisualElement();
        section.style.marginBottom = 8;

        var header = new Label(category.ToString().ToUpperInvariant());
        header.style.color = new Color(0.75f, 0.75f, 0.80f);
        header.style.fontSize = 12;
        header.style.marginBottom = 2;
        section.Add(header);

        foreach (var def in metrics)
            section.Add(BuildRow(def));

        return section;
    }

    private VisualElement BuildRow(MetricDefinition def)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 22;

        var swatch = new VisualElement();
        swatch.style.width = 10; swatch.style.height = 10;
        swatch.style.marginRight = 6;
        swatch.style.backgroundColor = new StyleColor(GraphColors.ColorFor(def.Id, def.Category));
        row.Add(swatch);

        var toggle = new Toggle { value = VisibleMetricIds.Contains(def.Id) };
        toggle.style.marginRight = 4;
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue) VisibleMetricIds.Add(def.Id);
            else VisibleMetricIds.Remove(def.Id);
            Changed?.Invoke();
        });
        row.Add(toggle);

        var name = new Label(def.NameLocKey); // Localization pass in Task 26 resolves this key.
        name.style.flexGrow = 1;
        name.style.color = Color.white;
        name.style.fontSize = 12;
        row.Add(name);

        var value = new Label("—");
        value.style.color = new Color(0.80f, 0.80f, 0.80f);
        value.style.fontSize = 12;
        value.style.minWidth = 52;
        value.style.unityTextAlign = TextAnchor.MiddleRight;
        _valueLabels[def.Id] = value;
        row.Add(value);

        return row;
    }
}
```

- [ ] **Step 2: Inject into `GraphsWindow`**

Add ctor param, field, and insert into `legendSlot`:

```csharp
private readonly GraphsLegend _legend;

public GraphsWindow(
    RootVisualElementProvider rootProvider,
    GraphsRangeSelector rangeSelector,
    GraphsDistrictSelector districtSelector,
    GraphsLegend legend)
{
    _rootProvider = rootProvider;
    _rangeSelector = rangeSelector;
    _districtSelector = districtSelector;
    _legend = legend;
}
```

In `Build()` — after the district selector:

```csharp
legendSlot.Add(_legend.Build());
```

- [ ] **Step 3: Bind in configurator**

```csharp
Bind<GraphsLegend>().AsSingleton();
```

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 5: In-game verification**

Load save, press Shift+G. Expected: right pane now shows category sections
(GOODS, POPULATION, SCIENCE, WELLBEING) with checkbox rows. The curated
defaults (Logs, Planks, Total Beavers, etc.) are pre-checked. Clicking a
checkbox toggles it. No crashes.

- [ ] **Step 6: Commit**

```bash
git add Graphs/UI/GraphsLegend.cs Graphs/UI/GraphsWindow.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: add legend with category-grouped checkbox rows"
```

---

## Task 22: Implement `GraphsChart` — weather bands background

Chart is a `VisualElement` that custom-draws via `generateVisualContent`. This task adds ONLY the weather background rendering. Lines come in Task 23.

**Files:**
- Create: `Graphs/UI/GraphsChart.cs`
- Modify: `Graphs/UI/GraphsWindow.cs`

- [ ] **Step 1: Write `Graphs/UI/GraphsChart.cs`**

```csharp
using System;
using Graphs.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public sealed class GraphsChart
{
    private static readonly Color DroughtColor   = new(1.00f, 0.55f, 0.15f, 0.15f);
    private static readonly Color BadtideColor   = new(0.60f, 0.25f, 0.80f, 0.15f);
    private static readonly Color TemperateColor = new(0, 0, 0, 0); // no fill

    private readonly MetricSampler _sampler;
    private readonly GraphsRangeSelector _range;

    private VisualElement? _element;

    public GraphsChart(MetricSampler sampler, GraphsRangeSelector range)
    {
        _sampler = sampler;
        _range = range;
    }

    public VisualElement Build()
    {
        _element = new VisualElement { name = "graphs-chart" };
        _element.style.flexGrow = 1;
        _element.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.06f));
        _element.generateVisualContent += Draw;

        _range.Changed += () => _element?.MarkDirtyRepaint();

        return _element;
    }

    public void Repaint() => _element?.MarkDirtyRepaint();

    private void Draw(MeshGenerationContext ctx)
    {
        var rect = ctx.visualElement.contentRect;
        if (rect.width <= 0 || rect.height <= 0) return;

        var history = _sampler.History;
        if (history.Count == 0) return;

        // Compute window: latest sample timestamp and lookback days.
        float latestTimestamp = history.ReadTimestamp(history.Count - 1);
        float? lookback = _range.LookbackDays();
        float startTimestamp = lookback.HasValue
            ? latestTimestamp - lookback.Value
            : history.ReadTimestamp(0);

        int startIdx = history.FindFirstAtOrAfter(startTimestamp);
        int endIdx = history.Count;
        if (startIdx >= endIdx) return;

        DrawWeatherBands(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);
    }

    private static void DrawWeatherBands(
        MeshGenerationContext ctx, Rect rect, MetricHistory history,
        int startIdx, int endIdx, float startT, float endT)
    {
        float span = endT - startT;
        if (span <= 0) return;

        int? runStart = null;
        byte runWeather = MetricHistory.WeatherTemperate;

        for (int i = startIdx; i <= endIdx; i++)
        {
            byte w = i < endIdx ? history.ReadWeather(i) : (byte)255;
            if (!runStart.HasValue) { runStart = i; runWeather = w; continue; }
            if (w == runWeather && i < endIdx) continue;

            // Emit rect for the run [runStart, i).
            if (runWeather != MetricHistory.WeatherTemperate)
            {
                float t0 = history.ReadTimestamp(runStart.Value);
                float t1 = i < endIdx ? history.ReadTimestamp(i) : endT;
                float x0 = rect.x + ((t0 - startT) / span) * rect.width;
                float x1 = rect.x + ((t1 - startT) / span) * rect.width;
                var color = runWeather switch
                {
                    MetricHistory.WeatherDrought => DroughtColor,
                    MetricHistory.WeatherBadtide => BadtideColor,
                    _                            => TemperateColor,
                };
                FillRect(ctx, new Rect(x0, rect.y, x1 - x0, rect.height), color);
            }
            runStart = i;
            runWeather = w;
        }
    }

    private static void FillRect(MeshGenerationContext ctx, Rect rect, Color color)
    {
        if (color.a <= 0 || rect.width <= 0 || rect.height <= 0) return;

        var mesh = ctx.Allocate(4, 6);
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color });
        mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
        mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
    }
}
```

- [ ] **Step 2: Inject into `GraphsWindow`**

Add ctor param + field:

```csharp
private readonly GraphsChart _chart;

public GraphsWindow(
    RootVisualElementProvider rootProvider,
    GraphsRangeSelector rangeSelector,
    GraphsDistrictSelector districtSelector,
    GraphsLegend legend,
    GraphsChart chart)
{
    _rootProvider = rootProvider;
    _rangeSelector = rangeSelector;
    _districtSelector = districtSelector;
    _legend = legend;
    _chart = chart;
}
```

In `Build()`, replace the empty `chartSlot` setup with:

```csharp
chartSlot.Add(_chart.Build());
```

- [ ] **Step 3: Bind in configurator**

```csharp
Bind<GraphsChart>().AsSingleton();
```

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 5: In-game verification**

Load a save. Trigger dev-mode (Alt+Shift+Z) → force a drought cycle. Let a
few in-game hours pass. Open window. Expected: chart area is dark; if the
game has been through a drought during the sampled hours, that x-range
shows an orange translucent band. Temperate hours have no band.

- [ ] **Step 6: Commit**

```bash
git add Graphs/UI/GraphsChart.cs Graphs/UI/GraphsWindow.cs Graphs/GraphsConfigurator.cs
git commit -m "Graphs: chart renders weather bands"
```

---

## Task 23: Extend `GraphsChart` with line rendering

Draw a polyline per visible metric, normalized per-line against its min/max over the visible window.

**Files:**
- Modify: `Graphs/UI/GraphsChart.cs`

- [ ] **Step 1: Add legend dependency and field**

Update constructor and fields:

```csharp
private readonly MetricSampler _sampler;
private readonly GraphsRangeSelector _range;
private readonly MetricRegistry _registry;
private readonly GraphsLegend _legend;

public GraphsChart(
    MetricSampler sampler,
    GraphsRangeSelector range,
    MetricRegistry registry,
    GraphsLegend legend)
{
    _sampler = sampler;
    _range = range;
    _registry = registry;
    _legend = legend;
}
```

In `Build()`, add after the existing subscription:

```csharp
_legend.Changed += () => _element?.MarkDirtyRepaint();
_sampler.OnSampled += () => _element?.MarkDirtyRepaint();
```

- [ ] **Step 2: Extend `Draw()` to draw gridlines + lines after bands**

Inside `Draw()`, immediately after the existing
`DrawWeatherBands(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);`
call, add two more lines so the method body ends with:

```csharp
DrawWeatherBands(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);
DrawGridlines(ctx, rect);
DrawLines(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);
```

- [ ] **Step 3: Add `DrawGridlines`**

```csharp
private static void DrawGridlines(MeshGenerationContext ctx, Rect rect)
{
    var color = new Color(0.25f, 0.25f, 0.28f, 0.5f);
    // 4 horizontal lines at 20/40/60/80 % height
    for (int i = 1; i <= 4; i++)
    {
        float y = rect.y + rect.height * i / 5f;
        FillRect(ctx, new Rect(rect.x, y, rect.width, 1), color);
    }
}
```

- [ ] **Step 4: Add `DrawLines`**

```csharp
private void DrawLines(
    MeshGenerationContext ctx, Rect rect, MetricHistory history,
    int startIdx, int endIdx, float startT, float endT)
{
    float span = endT - startT;
    if (span <= 0) return;
    int sampleCount = endIdx - startIdx;
    if (sampleCount < 2) return;

    var metrics = _registry.Metrics;
    for (int m = 0; m < metrics.Count; m++)
    {
        var def = metrics[m];
        if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;

        // Min/max over the visible window, ignoring NaN.
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = startIdx; i < endIdx; i++)
        {
            float v = history.ReadValue(i, m);
            if (float.IsNaN(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (float.IsInfinity(min) || float.IsInfinity(max)) continue;

        float range = max - min;
        Color color = GraphColors.ColorFor(def.Id, def.Category);

        // Draw as consecutive line segments, breaking on NaN.
        bool havePrev = false;
        Vector2 prev = default;
        for (int i = startIdx; i < endIdx; i++)
        {
            float v = history.ReadValue(i, m);
            if (float.IsNaN(v)) { havePrev = false; continue; }
            float t = history.ReadTimestamp(i);
            float x = rect.x + ((t - startT) / span) * rect.width;
            float norm = range > 0 ? (v - min) / range : 0.5f;
            float y = rect.y + rect.height - norm * rect.height;

            if (havePrev)
                DrawSegment(ctx, prev, new Vector2(x, y), color, thickness: 2f);
            prev = new Vector2(x, y);
            havePrev = true;
        }
    }
}

private static void DrawSegment(
    MeshGenerationContext ctx, Vector2 a, Vector2 b, Color color, float thickness)
{
    Vector2 dir = b - a;
    float len = dir.magnitude;
    if (len < 0.0001f) return;
    Vector2 normal = new Vector2(-dir.y, dir.x) / len * thickness * 0.5f;

    Vector3 v0 = new(a.x + normal.x, a.y + normal.y, Vertex.nearZ);
    Vector3 v1 = new(b.x + normal.x, b.y + normal.y, Vertex.nearZ);
    Vector3 v2 = new(b.x - normal.x, b.y - normal.y, Vertex.nearZ);
    Vector3 v3 = new(a.x - normal.x, a.y - normal.y, Vertex.nearZ);

    var mesh = ctx.Allocate(4, 6);
    mesh.SetNextVertex(new Vertex { position = v0, tint = color });
    mesh.SetNextVertex(new Vertex { position = v1, tint = color });
    mesh.SetNextVertex(new Vertex { position = v2, tint = color });
    mesh.SetNextVertex(new Vertex { position = v3, tint = color });
    mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
    mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
}
```

- [ ] **Step 5: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 6: In-game verification**

Load a mid-game save. Wait in-game 1+ day so there are 24+ samples in
history. Press Shift+G. Expected: lines appear for the curated defaults
(logs/planks/etc.), each colored distinctly, spanning the visible window.
Toggle a checkbox — line appears/disappears. Switch to "5 days" → lines
re-normalize against a narrower window.

- [ ] **Step 7: Commit**

```bash
git add Graphs/UI/GraphsChart.cs
git commit -m "Graphs: chart renders normalized lines and gridlines"
```

---

## Task 24: Wire legend current-value refresh + district-filter redraw

Legend rows currently show "—" for value. Hook them up to the most recent sample and update on redraw. Also wire the `DistrictFilter.Changed` event to repaint the chart.

**Files:**
- Modify: `Graphs/UI/GraphsWindow.cs`

- [ ] **Step 1: Add injected deps**

In `GraphsWindow`, add fields + ctor params:

```csharp
private readonly MetricSampler _sampler;
private readonly MetricRegistry _registry;
private readonly DistrictFilter _filter;

public GraphsWindow(
    RootVisualElementProvider rootProvider,
    GraphsRangeSelector rangeSelector,
    GraphsDistrictSelector districtSelector,
    GraphsLegend legend,
    GraphsChart chart,
    MetricSampler sampler,
    MetricRegistry registry,
    DistrictFilter filter)
{
    _rootProvider = rootProvider;
    _rangeSelector = rangeSelector;
    _districtSelector = districtSelector;
    _legend = legend;
    _chart = chart;
    _sampler = sampler;
    _registry = registry;
    _filter = filter;
}
```

- [ ] **Step 2: Subscribe to sampler events in `Open()` and unsubscribe in `Close()`**

In `Open()` after `Focus()`:

```csharp
_sampler.OnSampled += RefreshValues;
_filter.Changed += _chart.Repaint;
RefreshValues();
```

In `Close()` before `RemoveFromHierarchy()`:

```csharp
_sampler.OnSampled -= RefreshValues;
_filter.Changed -= _chart.Repaint;
```

- [ ] **Step 3: Implement `RefreshValues`**

```csharp
private void RefreshValues()
{
    var history = _sampler.History;
    if (history.Count == 0) return;
    int last = history.Count - 1;
    _legend.UpdateCurrentValues(id =>
    {
        int idx = _registry.IndexOf(id);
        return idx < 0 ? float.NaN : history.ReadValue(last, idx);
    });
}
```

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 5: In-game verification**

Load save, open window. Expected: each legend row's right-hand value
updates every in-game hour (after next sample tick). Change district
dropdown: values update; chart re-renders. Close and reopen the window:
no exceptions in the log (subscription cleanup works).

- [ ] **Step 6: Commit**

```bash
git add Graphs/UI/GraphsWindow.cs
git commit -m "Graphs: live-update legend values + district-filter redraw"
```

---

## Task 25: Register remaining singletons (hotkey tickable, sampler tickable)

The sampler already declares `ITickableSingleton`. Confirm the DI container
ticks it. Some Timberborn DI setups require explicit registration with
`MultiBind<ITickableSingleton>`. Add those bindings to be safe.

**Files:**
- Modify: `Graphs/GraphsConfigurator.cs`

- [ ] **Step 1: Add multi-bindings**

In `GraphsConfigurator.Configure()`, after all other binds:

```csharp
MultiBind<ITickableSingleton>().ToExisting<MetricSampler>();
MultiBind<ITickableSingleton>().ToExisting<GraphsHotkey>();
MultiBind<ILoadableSingleton>().ToExisting<MetricRegistry>();
MultiBind<ILoadableSingleton>().ToExisting<MetricSampler>();
```

Add required `using`:

```csharp
using Timberborn.SingletonSystem;
```

- [ ] **Step 2: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 3: In-game verification**

Restart game, load a save. Watch Player.log for the "MetricRegistry loaded
N metrics." line. Let game run 5+ in-game minutes (at max speed, a few
real seconds per in-game hour). Open window — chart should have new
samples compared to a prior open-close cycle.

If sampling appears frozen (`MetricSampler` never ticks), check that the
`MultiBind<ITickableSingleton>().ToExisting<MetricSampler>()` line is
present — without it, `ITickableSingleton` implementations may not be
invoked by the game's tick loop.

- [ ] **Step 4: Commit**

```bash
git add Graphs/GraphsConfigurator.cs
git commit -m "Graphs: register tickable + loadable singletons explicitly"
```

---

## Task 26: Localizations CSV

Replace raw loc keys in the legend with the localized strings.

**Files:**
- Create: `Graphs/Localizations/enUS.csv`
- Modify: `Graphs/UI/GraphsLegend.cs`

- [ ] **Step 1: Write `Graphs/Localizations/enUS.csv`**

```csv
ID,Text,Comment
Graphs.WindowTitle,Graphs,
Graphs.Range.FiveDays,5 days,
Graphs.Range.ThirtyDays,30 days,
Graphs.Range.All,All,
Graphs.District.All,All districts,
Graphs.Category.Goods,Goods,
Graphs.Category.Population,Population,
Graphs.Category.Science,Science,
Graphs.Category.Wellbeing,Wellbeing,
Graphs.Metric.ScienceStored,Science stored,
Graphs.Metric.TotalBeavers,Total beavers,
Graphs.Metric.Adults,Adult beavers,
Graphs.Metric.Kits,Kits,
Graphs.Metric.Bots,Bots,
Graphs.Metric.Homeless,Homeless,
Graphs.Metric.Unemployed,Unemployed,
Graphs.Metric.Injured,Injured,
Graphs.Metric.Infected,Infected,
Graphs.Metric.Contaminated,Contaminated,
Graphs.Metric.AvgWellbeing,Average wellbeing,
Graphs.Metric.MinWellbeing,Minimum wellbeing,
Graphs.Metric.AvgHunger,Average hunger satisfaction,
Graphs.Metric.AvgThirst,Average thirst satisfaction,
```

- [ ] **Step 2: Inject `ILoc` and translate labels in legend**

Modify `GraphsLegend.cs`:

```csharp
using Timberborn.Localization;
// ...
private readonly MetricRegistry _registry;
private readonly ILoc _loc;

public GraphsLegend(MetricRegistry registry, ILoc loc)
{
    _registry = registry;
    _loc = loc;
    // ... unchanged default visible set
}
```

And in `BuildRow`, replace:

```csharp
var name = new Label(def.NameLocKey);
```

with:

```csharp
string text = _loc.T(def.NameLocKey);
if (string.IsNullOrEmpty(text) || text == def.NameLocKey) text = def.Id;
var name = new Label(text);
```

The fallback (`def.Id`) avoids showing raw loc keys for goods whose
NameLocKey (`Good.Log`) is not in our CSV — those fall back to the game's
own locs when the game is queried instead. If the game's `ILoc` doesn't
auto-fallback, the id is still a decent label.

- [ ] **Step 3: Update GraphsLegend category header to use loc too**

In `BuildCategorySection`, replace:

```csharp
var header = new Label(category.ToString().ToUpperInvariant());
```

with:

```csharp
var headerText = _loc.T($"Graphs.Category.{category}");
if (string.IsNullOrEmpty(headerText)) headerText = category.ToString();
var header = new Label(headerText.ToUpperInvariant());
```

- [ ] **Step 4: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 5: In-game verification**

Load save, open window. Expected: legend rows display translated text
("Total beavers", "Science stored", etc.) instead of raw loc keys. Goods
rows display good names (game's own locs) where available.

- [ ] **Step 6: Commit**

```bash
git add Graphs/Localizations/enUS.csv Graphs/UI/GraphsLegend.cs
git commit -m "Graphs: localize legend labels"
```

---

## Task 27: Polish pass — InputService hotkey, UI styling, log-warn cleanup

Small refinements that don't change architecture.

**Files:**
- Modify: `Graphs/UI/GraphsHotkey.cs`
- Modify: `Graphs/UI/GraphsWindow.cs`

- [ ] **Step 1: Route the hotkey through `InputService`**

Replace the body of `GraphsHotkey.Tick()` with:

```csharp
public void Tick()
{
    // Ignore when any text field has focus; InputService tracks this.
    if (_input.MouseOverUI) { _prevPressed = false; return; }

    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    bool g = Input.GetKey(KeyCode.G);
    bool pressed = shift && g;
    if (pressed && !_prevPressed) _toggle();
    _prevPressed = pressed;
}
```

`InputService.MouseOverUI` is the shape if it exists (pending probe); if
not, drop the guard — the chord is specific enough that the downside of
firing while over UI is small.

- [ ] **Step 2: Dim the chart background when no samples exist**

In `GraphsChart.Draw()`, replace the early-return:

```csharp
if (history.Count == 0) return;
```

with:

```csharp
if (history.Count == 0)
{
    // Draw a centered "no data yet" hint via mesh not available; leave
    // the chart solid-colored. Caller adds a Label overlay if needed.
    return;
}
```

And in `GraphsWindow.Build()`, inside `chartSlot`, add a hint label that
hides once data is available:

```csharp
var hint = new Label("Collecting data… the chart populates after the first in-game hour.");
hint.style.position = Position.Absolute;
hint.style.left = 16; hint.style.top = 16;
hint.style.color = new Color(0.6f, 0.6f, 0.6f);
hint.style.unityFontStyleAndWeight = FontStyle.Italic;
hint.style.display = _sampler.History.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
chartSlot.Add(hint);

_sampler.OnSampled += () =>
    hint.style.display = _sampler.History.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
```

- [ ] **Step 3: Build**

Run: `./build.sh Graphs`
Expected: success.

- [ ] **Step 4: In-game verification**

Open a brand-new save. Press Shift+G immediately (before the first in-game
hour). Expected: the hint is visible. Wait one in-game hour — hint disappears
as the first sample arrives; a single point (invisible, since you need 2+
samples for a segment) is in the buffer. Wait a second hour — a short line
appears.

- [ ] **Step 5: Commit**

```bash
git add Graphs/UI/GraphsHotkey.cs Graphs/UI/GraphsWindow.cs
git commit -m "Graphs: polish — input service guard, empty-state hint"
```

---

## Task 28: End-to-end manual test pass + README update

Work through the full test checklist from the spec and note any bugs.

**Files:**
- Modify: `Graphs/README.md`

- [ ] **Step 1: Run manual test checklist** (from spec §Testing)

Record PASS/FAIL for each:

1. Load mid-game save. Shift+G opens window within 1 second. ☐
2. Let 3+ in-game days elapse. Chart populates smoothly for enabled metrics. ☐
3. Cycle through range buttons. Lines re-normalize each time. ☐
4. Toggle several legend checkboxes. Lines appear/disappear per toggle. ☐
5. Switch district filter (requires save with 2+ districts). Goods/Pop/Wellbeing
   lines update; Science stays; settlement metrics behave sensibly. ☐
6. Force drought via dev mode. After a few hours, orange band appears in the
   sampled x-range. Force badtide. Purple band appears. ☐
7. Close with Shift+G. Esc. Click X. All work. ☐
8. Save game, reload. History resets (phase 1 behavior). Sampler resumes.
   No errors in Player.log. ☐

For any FAIL, open a follow-up issue or fix before committing.

- [ ] **Step 2: Update `Graphs/README.md` with install + usage**

```markdown
# Graphs

Shift+G opens a full-screen window with line graphs of all settlement metrics
(goods, population, science, wellbeing) over in-game time. Each line is
normalized independently so very-large (logs) and very-small (beaver counts)
metrics are visually comparable. Weather cycles (drought, badtide) render as
translucent background bands.

## Features

- **Shift+G** toggles the window (phase 1: hard-coded, not rebindable)
- Metric categories: Goods, Population, Science, Wellbeing
- Per-metric checkbox toggles with curated defaults
- Per-district filter dropdown
- Range selector: 5 days / 30 days / All
- Weather bands as chart background
- Esc or X to close

## Phase 1 limitations

- History is in-memory only; resets on save load
- Hotkey binding is hard-coded
- No cursor-position tooltip for exact values

These are scheduled for phase 2.

## Build / install

```bash
./build.sh Graphs
```

Deploys to `~/Documents/Timberborn/Mods/Graphs/`.

## Troubleshooting

If the chart stays empty long after opening, check
`~/Library/Logs/Mechanistry/Timberborn/Player.log` for lines beginning with
`[Graphs]`. Metric-specific warnings are logged once per session.
```

- [ ] **Step 3: Commit**

```bash
git add Graphs/README.md
git commit -m "Graphs: phase 1 complete — manual test pass, README"
```

---

## Out-of-scope follow-ups (track separately — DO NOT IMPLEMENT)

- Persist `MetricHistory` + toggle state to the save file.
- Cursor-position tooltip showing exact values.
- User-rebindable hotkey via `KeyBindingSystem`.
- `PanelStack`-based dialog for proper stacking behavior.
