# Graphs Mod — Session Continuation Notes

Branch: `feature/graphs-mod`. Spec: `docs/superpowers/specs/2026-04-23-graphs-mod-design.md`. Plan: `docs/superpowers/plans/2026-04-23-graphs-mod.md`. Internal API cheatsheet: `Graphs/NOTES.md` (keep updated as discoveries land).

## Deployed state (most recent commits on feature/graphs-mod)

- `581d51f` reorder categories (Science above Population); Statistics unregistered
- `6aca834` Employment as its own category + Population sub-groups (Quarters/Health) + occupied/free beds
- `e8dcbcf` game USS classes applied (game-toggle, button-cross, game-scroll-view, game-dropdown, button-game/active)
- `e968632` native population + science icons via IAssetLoader (UI/Images/Game/ico-*)
- `a58bb33` Jobs and Vacancies metrics
- `b8d9466` StatisticsMetricProvider (currently unregistered)
- `5468b29` chart bottom hard-pinned to 0 (no bottom margin, 0 = category floor)
- `c53db76` weather phase uses CycleDay vs HazardousWeatherStartCycleDay
- `e927a1f` Water + Badwater merged sub-group
- `ceb67f5` PlayerPrefs persistence of visible metric ids
- `fbdaefc` game-styled UI + goods sub-grouped + clean label names (DisplayName.Value)
- `1643a93` goods totals via ResourceCountingService (the key inventory-lookup fix)

## Mod architecture snapshot

All files in `Graphs/`. Single `GraphsConfigurator [Context("Game")]` wires everything.

- `Metrics/` — `MetricScope`, `MetricCategory` (Goods, Science, Population, Employment, Wellbeing, Statistics), `MetricDefinition` (has optional `SubGroup` string), `IMetricProvider`, `MetricHistory` (48k-sample ring buffer), `MetricRegistry` (ILoadableSingleton, collects providers, orders by category then provider output), `MetricSampler` (ITickableSingleton, 240 samples/day → every 6 in-game min), `WeatherStateSampler`, `DistrictFilter`.
- `Metrics/Providers/` — ScienceMetricProvider, PopulationMetricProvider, GoodsMetricProvider, WellbeingMetricProvider, StatisticsMetricProvider (unregistered).
- `UI/` — GameIcons (loads native ico-* sprites via IAssetLoader.LoadAll<Sprite>), GraphColors (Timberborn-palette), GraphsHotkey (polls UnityEngine.InputSystem.Keyboard for Shift+G + Esc), GraphsWindow (modal centered panel with IAssetLoader-backed icons + USS classes), GraphsChart (mesh-drawn, per-category shared y-scale, 0 pinned at bottom, weather background bands), GraphsLegend (category sections, sub-groups via MetricDefinition.SubGroup, PlayerPrefs persistence), GraphsDistrictSelector, GraphsRangeSelector (5/30/All with .button-game + .button-active), NativeUi (legacy helper, not called anywhere — candidate for removal).

## Key game-API facts (corrections to original plan)

Keep `Graphs/NOTES.md` as source of truth — add to it when discoveries happen.

- Goods totals: `Timberborn.ResourceCountingSystem.ResourceCountingService.GetGlobalResourceCount(goodId)` returns `ResourceCount` struct. Use reflection to read `AllStock` property. Per-district: `GetDistrictResourceCounter(DistrictCenter).GetResourceCount(goodId)`. DistrictInventoryRegistry.Inventories throws NRE; DO NOT USE.
- Population totals: `PopulationService.GlobalPopulationData`. Per-district: `PopulationDataCollector.CollectData(district, scratch)`. `PopulationData` exposes `TotalPopulation`, `NumberOfAdults`, `NumberOfChildren`, `NumberOfBots`, `BedData.Homeless/OccupiedBeds/FreeBeds`, `BeaverWorkplaceData.Unemployed/OccupiedWorkslots/FreeWorkslots`, `BotWorkplaceData.Unemployed/OccupiedWorkslots/FreeWorkslots`, `ContaminationData.ContaminatedTotal`.
- Wellbeing: `WellbeingService` only exposes `AverageGlobalWellbeing` (int) and `AverageDistrictWellbeing` (int, tied to currently-switched district). For per-district walk `DistrictPopulation.Beavers` → `beaver.GetComponent<WellbeingTracker>().Wellbeing` (int). Need ids are faction-specific; reflect `NeedModificationService.FoodNeedId` and `WaterNeedId` (private static strings). NeedManager uses `GetNeedPoints(id) / GetNeedSpec(id).MaximumValue` for satisfaction (no GetSatisfaction method).
- Goods sprites: `IGoodService.GetGood(id).Icon.Asset` (the Icon is `AssetRef<Sprite>`, unwrap with .Asset). `DisplayName.Value` gives localized string.
- Population/science sprites: `IAssetLoader.LoadAll<Sprite>(path)` → `LoadedAsset<Sprite>` list, take `.Asset`. Paths are `UI/Images/Game/ico-beavers`, `ico-adult`, `ico-child`, `ico-bot`, `ico-work-*`, `ico-no-work-*`, `ico-work-empty-*`, `ico-contamination`, `homeless`, `science-icon`. Source: grep `sharedassets*.assets` for `^UI/Images/Game/`.
- Native USS classes (applied via `AddToClassList`): `game-toggle`, `button-cross` (close X), `game-scroll-view`, `game-dropdown`, `button-game` / `button-active`. Source: grep `sharedassets*.assets` for kebab-case names.
- Weather: active phase requires `GameCycleService.CycleDay >= WeatherService.HazardousWeatherStartCycleDay`; otherwise it's still temperate even though `CurrentCycleHazardousWeather` is set.
- Input: `UnityEngine.Input.GetKey` is disabled (new Input System active). Use `UnityEngine.InputSystem.Keyboard.current.shiftKey.isPressed` etc.
- `RootVisualElementProvider` is in namespace `Timberborn.RootProviders` (not `Timberborn.CoreUI`). Method: `CreateEmpty(name, sortOrder)` returns a `UIDocument`; add content to `.rootVisualElement`. Must `UnityEngine.Object.Destroy(doc.gameObject)` on close or leak.
- `ITickableSingleton` lives in `Timberborn.TickSystem`, NOT `Timberborn.SingletonSystem`.

## Things that didn't work

- `LocalizableToggle` / `NineSliceButton` — internal, need a `_textLocKey` set via reflection. Eventually abandoned; replaced by USS class approach (`AddToClassList("game-toggle")`) which works without reflection.
- `VisualElementInitializer.InitializeVisualElement` — crashes when any `LocalizableToggle` in tree lacks a loc key. Currently enabled but no LocalizableToggles in tree, so safe.
- `DistrictInventoryRegistry.Inventories` getter throws NRE inside the game's own code. Use ResourceCountingService instead.
- `ActiveInventoriesWithStock(goodId)` returns empty for starter resources not yet in a stockpile (e.g. in a fresh save before storage is built). ResourceCountingService handles this correctly.
- `StatisticCollector` classes are internal; can't `Bind<>` them. StatisticsMetricProvider subscribes to game events via EventBus to count its own values — works but doesn't persist. Currently disabled.

## Outstanding work (priority order)

1. **Cleanup before merge** — remove `Graphs/UI/NativeUi.cs` (dead), strip `Debug.Log`s in MetricSampler/GraphsChart/GoodsMetricProvider/StatisticsMetricProvider, consider moving NOTES.md changes to the top of `Graphs/NOTES.md`.
2. **Localizations CSV** (Task 26) — write `Graphs/Localizations/enUS.csv` with `Graphs.Metric.*` keys. Keep DisplayName.Value path for goods (already localized by the game).
3. **README** (Task 28) — `Graphs/README.md` is a placeholder; write the real one.
4. **Final manual test-pass** (Task 28) — run the 8-point checklist from the spec.
5. **Phase 2** (explicit spec non-goals; user hasn't asked yet) — save/load persistence, cursor tooltip, rebindable hotkey.
6. **User feedback items still open** — none right now; the user has been steadily iterating.

## User-preference reminders for next session

See `~/.claude/projects/-Users-matthewszatmary-Projects-timbermods/memory/feedback_execution_mode.md` — **don't ask process questions after direction is approved, just execute and announce**.

## How to pick up

1. `cd /Users/matthewszatmary/Projects/timbermods`
2. `git checkout feature/graphs-mod` (you should already be there)
3. `./build.sh Graphs` builds and deploys to `~/Documents/Timberborn/Mods/Graphs/`
4. Read `Graphs/NOTES.md` for cross-session API facts.
5. The mod currently works in-game per user's "Startting to look good" confirmation plus successive polish rounds. Next restart needed to see the latest USS class / category ordering / beds changes.
