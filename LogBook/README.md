# LogBook

A Timberborn mod that adds a window of line graphs over your settlement's
history — goods, population, science, wellbeing, employment — on a shared
time axis with weather bands behind the lines.

## Opening the window

Two entry points:

- The chart-icon button in the **top-right toggle strip** (next to
  Construction Guidelines / Stockpile Overlay / Natural Resources).
- The **Shift+G** hotkey.

Close with Esc or the X button.

## What it tracks

- **Goods** — every registered good in the save (including modded ones).
  Totals come from the game's `ResourceCountingService`, so they match
  the top-bar numbers exactly. Grouped by the game's own good groups
  (Food, Ingredients, Logs, Water, etc.); Water and Badwater share a
  group.
- **Science** — current science points.
- **Population** — total beavers, adults, kits, plus bots, housing stats
  (homeless, occupied/free beds), and contamination/infection counts.
- **Employment** — beaver and bot jobs filled, unemployed, vacancies.
- **Wellbeing** — average wellbeing, plus hunger and thirst (plotted as
  unmet need, so a rising line means worsening conditions).

## Chart behaviour

- **Per-scale-group y-axis** — metrics that share a `ScaleGroup` share an
  axis so their lines compare directly. Goods ride one scale, beaver
  counts another, 0..1 satisfaction values their own.
- **Soft bounds** — declared `FixedMin` / `FixedMax` anchor an axis at
  natural endpoints, but observed samples outside that range expand it.
  Real data is never clipped.
- **Weather bands** behind the lines: a warm gold tint during drought,
  deep burgundy during badtide. Both colors come from the game's own
  weather progress-bar pills. Only drawn during the hazardous phase of
  a cycle, not the temperate lead-in.
- **Hover tooltip** — a vertical cursor line plus a panel listing every
  visible metric's value at the cursor's nearest sample.
- **Range selector** in the title bar: Week (7 d), Month (28 d), Year
  (13 four-week months = 364 d), or All Time. No leap day — it's a
  video game.
- **District filter** dropdown above the legend. "All districts"
  aggregates settlement-wide; picking one narrows every per-district
  metric.

## Sampling and history

Samples are taken 24 times per in-game day (one per in-game hour). Each
finished district plus a settlement-wide "global" entry has its own
three-tier history:

| Tier   | Resolution | Capacity                          |
|--------|------------|-----------------------------------|
| Recent | 24 / day   | 28 days   (672 samples — Month)   |
| Mid    | 4 / day    | 364 days  (1 456 samples — Year)  |
| Old    | 1 / day    | 10 000 days (10 000 samples — All)|

Coarser tiers store running averages so an Old-tier sample summarizes a
full day of activity. The chart picks the finest tier that still covers
the requested lookback. Histories of removed districts are pruned.

The chart only repaints when a sample lands, a metric is toggled, the
district changes, or the range changes — no per-frame work.

## Persistence

The set of checked metrics is stored in `PlayerPrefs` and restored on
launch. The sample history itself is saved with the game save, so
graphs survive save/reload across all three tiers.

## Defaults

First-time defaults (no saved preference): Logs, Berries, Water, Total
beavers, Science stored, Average wellbeing.

## Build and install

From the repo root:

```bash
./build.sh LogBook
```

The build script sets `DOTNET_ROOT` and deploys the mod to
`~/Documents/Timberborn/Mods/LogBook/` using the flat mod layout.

## Requirements

- Timberborn 1.0.0.0 or later
- macOS (Apple Silicon) uses the bundled `AppleSiliconHarmony` shim
  alongside Harmony 2.4.2.
