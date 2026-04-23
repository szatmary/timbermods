# Graphs Mod — Design

**Date:** 2026-04-23
**Status:** Approved
**Phase 1 scope:** in-memory history only; save-file persistence deferred to phase 2.

## Summary

A new Timberborn mod, `Graphs/`, that adds an in-game full-screen window showing
line graphs of settlement metrics over time. Triggered by a configurable hotkey
(default **Shift+G**). Metrics include every good, population breakdowns, science,
wellbeing, and district-level stats. Weather cycles render as translucent
background bands. Each line is scaled independently so that wildly different
magnitudes (8 000 logs vs 45 beavers) are visually comparable.

## Goals

1. At-a-glance visual history of any metric in the settlement.
2. Compare trends across metrics with very different magnitudes (normalized lines).
3. Visible correlation with weather cycles (background bands).
4. Per-district drill-down when multiple districts exist.
5. Zero performance impact when the window is closed.
6. Match the look of other in-game dialogs (CoreUI styling).

## Non-goals (phase 1)

- Save-file persistence of history or of which lines are toggled on.
- Hover tooltip showing exact values at cursor position.
- CSV / image export.
- Custom date-range scrubber; only three preset range buttons.
- Side-by-side comparison of two districts.

Phase 2 will add save-file persistence and "remember last session's toggle
state per save" (promoting curated defaults from a hard-coded list to a
per-save preference).

## High-level architecture

One mod directory `Graphs/` following the same layout as the other mods in
this repo (`ModStarter.cs`, `manifest.json`, `Graphs.csproj`,
`Localizations/`, optionally `lib/` if Harmony is needed as a hotkey
fallback). The preferred path uses no Harmony patches — all game APIs we
consume are DI-registered or publicly accessible. Harmony is only pulled in
if `KeyBindingSystem` does not expose a public registration API (see
Hotkey section).

Major components:

| Component | Role |
|-----------|------|
| `MetricRegistry` | Static list of all trackable metrics: `(Id, LocalizedNameKey, Category, Scope, ValueFn)`. |
| `MetricSampler` | `ITickableSingleton`. Takes one sample per in-game hour. Publishes `OnSampled`. |
| `MetricHistory` | Ring buffer of samples (metric values + weather state + timestamp). Bounded. |
| `DistrictScopedValueProvider` | Resolves district-scoped metrics based on current district filter. |
| `GraphsHotkey` | Registers a `ToggleGraphs` key binding (default Shift+G). |
| `GraphsWindow` | Full-screen UIToolkit panel; built on demand, destroyed on close. |
| `GraphsChart` | Custom-drawn chart via `VisualElement.generateVisualContent`. |
| `GraphsLegend` | Right-side panel: category-grouped checkboxes + district dropdown. |
| `GraphsRangeSelector` | Bottom button row: 5 days / 30 days / All. |
| `GraphsConfigurator` | Bindito `[Context("Game")]` configurator wiring everything together. |

## Data collection

### Sampling cadence

- `MetricSampler` implements `ITickableSingleton`. On each tick it reads
  `IDayNightCycle.PartialDayNumber` (a `float` count of game days). It tracks
  the last sampled hour index (`floor(partialDay * 24)`). When the current
  hour index differs, it takes a sample.
- This decouples sampling from game speed (fast-forward does not oversample)
  and from frame rate (one sample per in-game hour, always).
- A sample writes: `float[metricCount]` values, `byte` weather state, `float`
  game-day timestamp (the `PartialDayNumber` at sample time).
- Metrics that throw or return `NaN`/infinity are stored as `NaN` and
  rendered as gaps. The sampler itself never throws.

### Storage

- Fixed-capacity ring buffer: **2 000 in-game days × 24 samples = 48 000 samples**.
- Memory footprint at full capacity: `48 000 × (metricCount × 4 + 1 + 4)` bytes.
  For ~80 metrics that's about 15 MB — fine.
- When full, oldest sample is overwritten. "All" range plots everything in the
  buffer (so after 2 000 days, "All" becomes a rolling 2 000-day window; this
  is acceptable and simpler than dynamically resizing).

### Metric registry contents

Metrics are registered at load time and include dynamically enumerated goods.

**Goods (Scope: `Either`)** — enumerated from `GoodSpecification` registry at
load. Value = current stockpile amount for the good (settlement-wide or for
the selected district). Modded goods are included automatically.

**Population (Scope: `District`, aggregable across districts)**
- Total beavers
- Adult beavers
- Kits
- Bots (Iron Teeth golems, when faction supports them)
- Homeless beavers
- Unemployed adults
- Injured (via per-beaver `isInjured`, counted)
- Infected (incubating — via `ContaminationIncubator` count)
- Contaminated (symptomatic — via `BeaverContaminationStatistics`)

**Science (Scope: `Settlement`)**
- Science stored

**Wellbeing (Scope: `District`)**
- Average wellbeing
- Min wellbeing
- Average hunger (need satisfaction)
- Average thirst (need satisfaction)

No cumulative or rate metrics are tracked.

### District scoping

- `DistrictScopedValueProvider` resolves the "for which district?" question
  per sample, based on the current user filter.
- `Scope.Settlement` metrics (like "Science stored") ignore district filter
  entirely and always return the global value.
- `Scope.District` metrics, when the filter is "All districts", are aggregated
  across all districts (sum for counts, weighted average for averages, min
  of mins). When filtered to a specific district, they use that district's
  provider (e.g. `DistrictBeaverContaminationStatisticsProvider`).
- `Scope.Either` metrics (goods) sum per-district stockpiles when "All" is
  selected, or use the specific district's inventory when filtered.

### Weather tracking

- Each sample records the active `HazardousWeather` cycle state as a `byte`
  enum: Temperate / Drought / Badtide.
- This is sampled from the same weather service Timberborn already queries
  for its own HUD indicators.

## UI

### Window layout

Full-screen modal dialog (matches size of in-game menu). Closable via:
- Shift+G (toggle)
- Esc
- X button top-right

```
┌─────────────────────────────────────────────────────────────────────┐
│ Graphs                                                          [X] │
├──────────────────────────────────────────────────┬──────────────────┤
│                                                  │  District: [All▼]│
│                                                  ├──────────────────┤
│                                                  │ ▼ Goods          │
│   [ chart area — weather bands in background,    │   ☑ Logs         │
│     normalized lines over the top ]              │   ☑ Planks       │
│                                                  │   ☐ Metal Blocks │
│                                                  │   …              │
│                                                  │ ▼ Population     │
│                                                  │   ☑ Total Beavers│
│                                                  │ ▼ Science        │
│                                                  │ ▼ Wellbeing      │
├──────────────────────────────────────────────────┴──────────────────┤
│       [ 5 days ] [ 30 days ] [ All ]                                │
└─────────────────────────────────────────────────────────────────────┘
```

### Chart rendering

- `GraphsChart` is a `VisualElement` with `generateVisualContent` wired to
  draw everything in one pass using `MeshGenerationContext`.
- Draw order:
  1. Background weather bands (translucent full-height rects).
     Adjacent same-weather samples merge into single rects for cheap rendering.
     - Temperate → no fill (chart background color)
     - Drought → warm orange, ~15% alpha
     - Badtide → purple, ~15% alpha
  2. Horizontal gridlines (subtle).
  3. One polyline per enabled metric.
- Each line is **normalized independently**: for the samples visible in the
  current range, compute `(v - min) / (max - min)` and map to chart height.
  If `min == max`, line sits at midpoint. `NaN` values produce a gap.
- Y-axis shows no numeric labels (they'd be meaningless with per-line
  normalization). Legend rows show each metric's current actual value.
- X-axis shows day-number labels.

### Colors

- 50+ metrics, so we generate distinct colors procedurally: evenly-spaced HSL
  hues with slight luminance/saturation variation per category. Stable per
  metric id so a line's color doesn't change between sessions.

### Legend (right panel)

- Scrollable list of category sections (Goods, Population, Science, Wellbeing),
  each collapsible.
- Each row: color swatch + checkbox + metric name + current value.
- District dropdown at top: "All districts" plus each named district.
- When a specific district is selected, `Scope.Settlement` metrics (like
  Science stored) grey out and their lines hide — they don't make sense
  filtered to one district.

### Default visible metrics (phase 1)

Hard-coded curated list (option B from brainstorming). The list is defined
by good id so it's faction-agnostic; goods whose ids are not present in the
current save are simply skipped (e.g. Maple Syrup in an Iron Teeth save):
- Logs, Planks, Water, Berries, Maple Syrup, Gear (Iron Teeth), Biofuel
- Total Beavers
- Science stored
- Average wellbeing

All other metrics default to hidden. User can toggle on what they want.

Phase 2 will replace this with a per-save persisted set of toggles.

### Range selector

Three mutually-exclusive buttons at the bottom: **5 days**, **30 days**, **All**.
Default: 30 days. When switching, the chart re-normalizes lines against the
new visible-window min/max and redraws.

## Hotkey

Registered with Timberborn's `KeyBindingSystem`:
- Action id: `ToggleGraphs`
- Default binding: Shift+G
- Appears in the game's keybindings menu (player can rebind).
- Handler toggles the window: if open, close and dispose; else, build and
  open the `GraphsWindow`.

Inside the window, Esc is also bound to close, matching other game dialogs.

## Lifecycle & performance

- `MetricSampler` runs unconditionally in the `Game` context. Cost per
  sample: ~80 metric reads + 1 buffer write, once per in-game hour. Negligible.
- `GraphsWindow` is built on open, destroyed on close — no hidden visual tree
  sitting in the scene.
- While the window is open, the chart re-draws only when:
  - A new sample is published (subscribe to `MetricSampler.OnSampled`).
  - The user changes range, district, or legend toggles.
- The chart does not animate and does not redraw per-frame.

## Error handling

- Per-metric `ValueFn` exceptions are caught in the sampler and recorded as
  `NaN`. A log warning is emitted once per metric per session (not every
  sample) so broken metrics are visible but not log-spamming.
- Missing district providers (e.g. faction without bots) are handled by
  metric registration: the metric is only registered if its provider exists.
- Ring buffer underflow (fewer samples than range requests) is handled by
  plotting only what's available; the chart shows an empty area for missing
  history.

## Testing

- No automated test harness exists for Timberborn mods in this repo and
  none is proposed here — matches the existing pattern.
- Manual verification plan:
  1. Load a mid-game save. Press Shift+G. Window opens, shows a few samples
     of history since load.
  2. Let the game run a few in-game days. Chart populates smoothly.
  3. Toggle range buttons. Lines re-normalize correctly.
  4. Toggle legend checkboxes. Lines appear/disappear.
  5. Switch district filter with multiple districts present. Goods and
     wellbeing lines update; science stays; settlement metrics grey out when
     a specific district is picked.
  6. Trigger a drought via dev mode. Background band appears for the
     drought-period samples after the next few samples arrive.
  7. Close with Shift+G, Esc, and the X button — all work.
  8. Save, load, confirm history starts fresh (phase 1 behavior) and sampler
     resumes.

## File layout (proposed)

```
Graphs/
├── Graphs.csproj
├── ModStarter.cs
├── manifest.json
├── lib/                                   (Harmony DLLs — only if Harmony fallback is used for the hotkey)
├── Localizations/
│   └── enUS.csv                           (strings for metrics, categories, buttons)
├── GraphsConfigurator.cs                  (Bindito [Context("Game")])
├── Metrics/
│   ├── MetricRegistry.cs
│   ├── MetricScope.cs                     (enum: Settlement | District | Either)
│   ├── MetricCategory.cs                  (enum)
│   ├── MetricDefinition.cs                (record)
│   ├── MetricSampler.cs                   (ITickableSingleton)
│   ├── MetricHistory.cs                   (ring buffer)
│   ├── DistrictScopedValueProvider.cs
│   └── Providers/                         (one file per category)
│       ├── GoodsMetricProvider.cs
│       ├── PopulationMetricProvider.cs
│       ├── ScienceMetricProvider.cs
│       └── WellbeingMetricProvider.cs
├── UI/
│   ├── GraphsHotkey.cs                    (KeyBindingSystem registration)
│   ├── GraphsWindow.cs                    (full-screen dialog root)
│   ├── GraphsChart.cs                     (custom-drawn chart)
│   ├── GraphsLegend.cs
│   ├── GraphsRangeSelector.cs
│   └── GraphColors.cs                     (HSL → color per metric id)
└── README.md
```

## Open questions / risks

- **UIToolkit mesh drawing on custom elements**: confirmed supported via
  `generateVisualContent`. Low risk.
- **KeyBindingSystem registration**: need to verify the exact API for
  registering a new named action with a default modifier+key binding.
  Will confirm during implementation; falls back to a Harmony-patched
  `InputAction` listener if needed.
- **`GoodSpecification` enumeration timing**: need to ensure the goods
  registry is populated before the `MetricRegistry` enumerates it. Likely
  requires `ILoadableSingleton` load-order awareness — will address during
  implementation.
- **`ContaminationIncubator` vs "contaminated" distinction**: confirmed two
  distinct runtime states exist. Exact per-district counting API needs to
  be located during implementation.

## Phase 2 (follow-up, not in this design)

- Persist `MetricHistory` to the save file via Timberborn's save system.
- Persist per-save legend toggle state (replacing the hard-coded curated
  defaults).
- Hover tooltip showing exact metric values at the cursor's x-position.
