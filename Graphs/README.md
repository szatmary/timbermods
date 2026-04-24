# Graphs

A Timberborn mod that shows a centered dialog with line graphs of
settlement metrics over time. Toggle with **Shift+G**. Close with Esc or
the X button.

## What it tracks

- **Goods** — every registered good in the save (including modded ones).
  Totals come from the game's `ResourceCountingService` so they match the
  top-bar numbers exactly. Grouped by the game's own good groups (Food,
  Ingredients, Logs, Water, etc.); Water and Badwater share a group.
- **Science** — current science points.
- **Population** — total beavers, adults, kits, plus housing stats
  (homeless, occupied beds, free beds) and contaminated count.
- **Bots** — bot count (own legend section, plots on the Population
  y-scale so beaver/bot numbers are directly comparable).
- **Employment** — unemployed beavers/bots, filled jobs, vacancies.
- **Wellbeing** — average wellbeing, average hunger/thirst satisfaction.

## Chart behaviour

- **0 pinned to the chart bottom** per scale-group. If Logs is 200 and
  Planks is 100, Planks sits at half height, not at the floor.
- **Shared y-scale per scale-group** so lines in the same group compare
  directly.
- **Weather bands** behind the lines: amber tint for drought, pink for
  badtide (matching the game's theming). Only drawn during the hazardous
  phase of a cycle, not the temperate lead-in.
- **Range selector** at the bottom: 5 days / 30 days / All.
- **District filter** dropdown above the legend. "All districts"
  aggregates; picking one narrows every per-district metric.

## Sampling

One sample every 6 in-game minutes (240/day, ~200 in-game days of
history in a 48 000-sample ring buffer). The sampler runs continuously
once the game is loaded; opening and closing the window is free. No
per-frame redraws — the chart repaints only when a new sample arrives,
a checkbox flips, the district changes, or the range changes.

## Persistence

The set of metrics you have checked is saved to `PlayerPrefs` and
restored on the next launch.

_History_ (the sample buffer itself) is in-memory only and resets on
save reload — deliberately in phase 1.

## Defaults

First-time defaults (no saved preference): Logs, Berries, Water, Total
beavers, Science stored, Average wellbeing.

## Build and install

From the repo root:

```bash
./build.sh Graphs
```

The build script sets `DOTNET_ROOT` and deploys the mod to
`~/Documents/Timberborn/Mods/Graphs/` using the flat mod layout.

## Requirements

- Timberborn 1.0.0.0 or later
- macOS (Apple Silicon) uses the bundled `AppleSiliconHarmony` shim
  alongside Harmony 2.4.2.

## Phase 2 (not yet implemented)

- Persist sample history across save/load.
- Tooltip showing exact values at the cursor position.
- Rebindable hotkey via the game's `KeyBindingSystem`.
- Native-UI styling for scrollbar / dropdown / checkbox / close-X
  (currently plain UIToolkit; blocked on USS selector extraction).
- Re-enable the Statistics category (beavers exploded, chipped teeth,
  trees cut, etc.) once we can persist counters across save/load.
