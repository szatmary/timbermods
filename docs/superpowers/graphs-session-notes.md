# Graphs Mod — Session Continuation Notes

Branch: `feature/graphs-mod`. Rollback tag: `graphs-native-ui-good-state` (at `8f79770`).
Spec: `docs/superpowers/specs/2026-04-23-graphs-mod-design.md`. Plan: `docs/superpowers/plans/2026-04-23-graphs-mod.md`. API cheatsheet: `Graphs/NOTES.md` — keep updated.

## Current state (head)

- `3ec9c10` native bed icons for Quarters + fix emp.* icon keys
- `167a8a5` open window without pausing game (Push with lockSpeed:false)
- `8f79770` reserve title-bar height so dropdown can't overlap close X
- `f0f99c8` add .game-toggle class to native checkbox + drop window title
- `273c8c2` padding on content wrapper + drop bad CSV row that crashed localizer
- `ffed8a3` clear LocalizableToggle inline text after init (Graphs.Empty placeholder)
- `245c268` native Dropdown widget + box__content-margin inner wrapper
- `506d331` use NineSliceVisualElement + sliced-border classes for native chrome
- `e71b8a2` use the game's IPanelController + PanelStack for the window
- `a5bf495` native LocalizableToggle + NineSliceButton with proper initializer chain

## Architecture (what's landed)

Native-UI approach finally worked via:
1. **Publicize `Timberborn.CoreUI.dll`** using `BepInEx.AssemblyPublicizer.Cli` (requires `DOTNET_ROLL_FORWARD=Major`). Output: `Graphs/lib/publicized/Timberborn.CoreUI.dll`. `csproj` excludes the real DLL from the wildcard and references the publicized copy (Private=false, runtime uses real DLL).
2. **Implement `IPanelController`** on `GraphsWindow` and push via `PanelStack`. The PanelStack's container already has all theme stylesheets, so USS rules apply to my widgets automatically.
3. Use **`NineSliceVisualElement`** + classes `sliced-border` + `sliced-border--nontransparent` for the window frame (same pattern as Core/DialogBox.uxml).
4. Use **`LocalizableToggle`** + `.game-toggle` class for checkboxes. `_textLocKey` is set to `"Graphs.Empty"` before init; post-init the text is cleared via `_root.Query<LocalizableToggle>().ForEach(t => t.text = "")`.
5. Use **`NineSliceButton`** + `.close-button` class for the close X.
6. Use **`Timberborn.DropdownSystem.Dropdown`** + `IDropdownProvider` for the district dropdown; `DropdownItemsSetter.SetItems` populates items after attach-to-panel.
7. **`VisualElementInitializer.InitializeVisualElement(_root)`** must run in `GetPanel()` to wire up localizer, button clickability, scrollbar, dropdown, etc.
8. Inside the nine-slice frame, an inner **`VisualElement` with `.box__content-margin` class + 20px explicit padding** holds the content so it doesn't overlap the border.
9. Title bar has a fixed height (40px) + `flexShrink: 0` + `margin-bottom: 6` so the dropdown below can't overlap it.
10. **Window opens without pausing** the game — call the publicized private `Push(this, hideTop:false, showOverlay:true, isDialog:false, lockSpeed:false)` instead of `PushDialog`.

## Key game-API facts (learn from NOTES.md first, but TL;DR)

- **Goods totals**: `ResourceCountingService.GetGlobalResourceCount(goodId) -> ResourceCount`. Read `.AllStock` via reflection. Per-district: `GetDistrictResourceCounter(d).GetResourceCount(goodId)`. DO NOT use `DistrictInventoryRegistry.Inventories` (NRE).
- **Population**: `PopulationService.GlobalPopulationData` (global) or `PopulationDataCollector.CollectData(district, scratch)` (per-district). PopulationData exposes TotalPopulation, NumberOfAdults/Children/Bots, BedData.{Homeless,OccupiedBeds,FreeBeds}, BeaverWorkplaceData.{Unemployed,OccupiedWorkslots,FreeWorkslots}, BotWorkplaceData.{...}, ContaminationData.ContaminatedTotal.
- **Wellbeing**: iterate `DistrictPopulation.Beavers` and read `WellbeingTracker.Wellbeing` (int) — WellbeingService can't do per-district except via SwitchDistrict. Hunger/thirst satisfaction = `NeedManager.GetNeedPoints(id) / GetNeedSpec(id).MaximumValue`. Need ids are faction-specific `_foodNeedId`/`_waterNeedId` on `NeedModificationService` (private static; reflect them).
- **Goods sprites**: `IGoodService.GetGood(id).Icon.Asset` (AssetRef<Sprite>, unwrap `.Asset`). `DisplayName.Value` for localized name.
- **Native sprites**: `IAssetLoader.LoadAll<Sprite>("UI/Images/Game/ico-*")[0].Asset`. Known paths live in `Graphs/UI/GameIcons.cs`.
- **Native USS classes**: `.game-toggle`, `.button-cross`/`.close-button`, `.game-scroll-view`, `.game-dropdown`, `.button-game`/`.button-active`, `.sliced-border`/`.sliced-border--nontransparent`, `.box__content-margin`. Game USS defines these — apply via `AddToClassList`.
- **Weather**: `WeatherService.HazardousWeatherStartCycleDay` vs `GameCycleService.CycleDay` for active-phase detection; `HazardousWeatherService.CurrentCycleHazardousWeather` for the enum. Temperate lead-in of each cycle should NOT show the band.
- **Input**: new Input System is active; use `UnityEngine.InputSystem.Keyboard.current.gKey.isPressed`, NOT `UnityEngine.Input.GetKey`.
- **PanelStack overloads**:
  - `Push(ctl)` — normal
  - `PushOverlay(ctl)` — dim backdrop
  - `PushDialog(ctl)` — dim + `isDialog:true` + pauses
  - `HideAndPushWithoutPause(ctl)` — hides top + no pause
  - Publicized private `Push(ctl, hideTop, showOverlay, isDialog, lockSpeed)` — fully configurable.
  - `Pop(ctl)` — pop ours off the stack.

## Pending work (in priority)

1. **Remaining icons** (interrupted by compaction warning):
   - `wellbeing.avg` — no direct "wellbeing" icon in `UI/Images/Game/`; closest candidates: `needview-background`/`needview-progress`. None look right. Could skip.
   - `need.hunger.avg` — probe turned up no food icon in UI/Images/Game/. Need to check UI/Images/ elsewhere or skip.
   - `need.thirst.avg` — same. Probably a water-drop somewhere.
   - Look in `UI/Images/Clock/`, `UI/Images/Branding/`, etc. — not yet probed thoroughly.
2. **Cleanup** — NativeUi.cs is gone; Debug.Logs trimmed. Still some warnings if any.
3. **Original Task 28 final test pass** — never ran the 8-point spec checklist.
4. **Phase 2** (deferred in spec):
   - Save sample history across save/load.
   - Cursor tooltip showing exact values.
   - Rebindable hotkey via KeyBindingSystem.
   - Bring back the Statistics category (beavers exploded etc.) once we can persist counters.

## User preferences captured this session

In `~/.claude/projects/-Users-matthewszatmary-Projects-timbermods/memory/`:
- `feedback_execution_mode.md` — don't ask process questions after direction is approved, just execute.
- `feedback_verify_yourself.md` — don't use the user as a build-and-test loop; research before changing.

## How to build / deploy / test

```bash
cd /Users/matthewszatmary/Projects/timbermods
./build.sh Graphs
```
Deploys to `~/Documents/Timberborn/Mods/Graphs/`. Game must be fully restarted to reload the DLL.

Publicizer (if CoreUI ever needs refreshing after a game update):
```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROLL_FORWARD=Major
assembly-publicizer "$HOME/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/Managed/Timberborn.CoreUI.dll" -o Graphs/lib/publicized/Timberborn.CoreUI.dll
```

Decompiler (for understanding game types — requires prerelease):
```bash
dotnet tool install -g ilspycmd --prerelease
ilspycmd -t Timberborn.Namespace.TypeName "$MANAGED/Timberborn.XXX.dll"
```

UnityPy asset probe in `/tmp/unitypy-venv/`:
```bash
/tmp/unitypy-venv/bin/python /tmp/dump_raw.py   # dumps TextAssets
/tmp/unitypy-venv/bin/python /tmp/find_uxml.py  # dumps UXML-containing MonoBehaviours
```

## Files of note

- `Graphs/GraphsConfigurator.cs` — DI wiring. All providers registered except StatisticsMetricProvider (kept but not bound).
- `Graphs/UI/GraphsWindow.cs` — IPanelController. NineSliceVisualElement panel + LocalizableToggle + NineSliceButton + Dropdown.
- `Graphs/UI/GraphsLegend.cs` — legend with category sections + subgroups.
- `Graphs/UI/GameIcons.cs` — sprite mapping (MetricId → UI/Images/Game/ico-* path).
- `Graphs/Metrics/MetricDefinition.cs` — has optional `SubGroup` and `ScaleGroup` strings.
- `Graphs/NOTES.md` — game API cheat sheet; should stay accurate.
