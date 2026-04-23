# Graphs mod — internal API notes

Source of truth for implementers. All names verified against Timberborn game DLLs via string-probe of
`/Users/matthewszatmary/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/Managed/`.
Raw probe dump: `/tmp/graphs-probe.txt`.

Conventions below: `Namespace.TypeName.Member` when the namespace is load-bearing; otherwise just `Type.Member`.

---

## Time

- `Timberborn.TimeSystem.IDayNightCycle` (interface, DI-injectable)
  - `int DayNumber` (`get_DayNumber`)
  - `float DayProgress` (`get_DayProgress`)
- Impl: `Timberborn.TimeSystem.DayNightCycle`
- Cycle-day boundary event: `Timberborn.TimeSystem.CycleDayStartedEvent` — post via EventBus on each in-game day tick.

## Weather

- `Timberborn.HazardousWeatherSystem.HazardousWeatherService`
  - `CurrentCycleHazardousWeather` (backing-field confirmed: `<CurrentCycleHazardousWeather>k__BackingField`) — returns the selected `IHazardousWeather` for the current cycle.
- `Timberborn.HazardousWeatherSystem.IHazardousWeather` — base interface.
- Concrete subtypes: `Timberborn.HazardousWeatherSystem.BadtideWeather`, `Timberborn.HazardousWeatherSystem.DroughtWeather`.
- There is no clean "is active right now" flag — type-name-sniff the runtime type of the current weather (or inspect `TemperateWeatherDurationService` to know when it's temperate).
- Events: `HazardousWeatherStartedEvent`, `HazardousWeatherEndedEvent`, `HazardousWeatherSelectedEvent`.

## Districts

- `Timberborn.GameDistricts.DistrictCenterRegistry`
  - `FinishedDistrictCenters` (`get_FinishedDistrictCenters`) — enumerable of `DistrictCenter`.
  - Change event: `DistrictCenterRegistryChangedEvent`.
- `Timberborn.GameDistricts.DistrictCenter` (component)
  - `DistrictName` (`get_DistrictName`)
  - `DistrictPopulation` (`get_DistrictPopulation`)
  - `NumberOfAdults` (`get_NumberOfAdults`)
  - `NumberOfBots` (`get_NumberOfBots`)
  - `NumberOfChildren` (`get_NumberOfChildren`)
- `Timberborn.GameDistricts.DistrictBuilding` — sibling component on buildings; use `GetComponent<DistrictBuilding>().District` to reach the owning `DistrictCenter`.

## Inventory / goods

- **Enumerating good ids**: `Timberborn.Goods.StorableGoodRegistry.Goods` (`get_Goods`). No `GoodSpecService` exists. `GoodsGroupSpecService` exists but exposes groups, not good ids. ⚠️ resolve at impl time: confirm that `StorableGoodRegistry.Goods` returns `IEnumerable<StorableGood>` (with `.Id`) vs `IEnumerable<GoodSpec>` — verify via LSP or by reading `<GoodAmount>` + `get_GoodId` shape.
- `Timberborn.InventorySystem.DistrictInventoryRegistry`
  - `AllInventories` (`get_AllInventories`) — enumerable of `Inventory`.
- `Timberborn.InventorySystem.Inventory` (component)
  - `Stock` (`get_Stock`) — collection of `StorableGoodAmount` (from `Timberborn.InventorySystem`; see also `Timberborn.Goods.GoodAmount`).
  - `AllowedGoods`, `Goods`, `Capacity`, `CapacityReservation`, `StockReservation`.
  - Per-good stock amount: no direct `Stock.Get(id)`. The typical pattern is `inv.Stock` returns an enumerable of `(goodId, amount)` pairs — iterate and filter, or `inv.GetAmount(goodId)`-style. ⚠️ resolve at impl time: pick between iterating `Stock` vs a dedicated `GetAmount` helper; the probe shows `GetAmount` and `get_Amount` on stock entries.
- `Timberborn.Goods.GoodAmount` / `Timberborn.InventorySystem.StorableGoodAmount` — pair of (good, amount).

## Science

- `Timberborn.ScienceSystem.ScienceService`
  - `SciencePoints` (`get_SciencePoints`) — int.

## Wellbeing

- `Timberborn.Wellbeing.WellbeingService`
  - `GetAverageWellbeing(DistrictCenter)` / `GetAverageWellbeing()` global
  - `GetMaxWellbeing(...)` / `MaxBeaverWellbeing`
- `Timberborn.Wellbeing.WellbeingTracker` (component on Character)
  - `Wellbeing` property (`get_Wellbeing`) — the per-beaver score.
- Events: `WellbeingChangedEventArgs` (with `OldWellbeing`, `NewWellbeing`), `NewWellbeingHighscoreEvent`.
- For district-wide aggregates: `DistrictWellbeingTrackerRegistry` / `GlobalWellbeingTrackerRegistry`.
- No plain `Wellbeing` type — use `WellbeingTracker` component.

## Beavers / bots / entities

- `Timberborn.Characters.Character` — base component on every beaver/bot.
  - `FirstName`, `Alive`, `Age`, `DayOfBirth`.
- `Timberborn.Characters.CharacterPopulation`
  - `Characters` (`get_Characters`)
  - `NumberOfCharacters` (`get_NumberOfCharacters`)
  - `GetEnabledCharacters()`
- `Timberborn.Beavers.BeaverPopulation` — `Beavers` (`get_Beavers`) — beaver-only view.
- `Timberborn.EntitySystem.EntityRegistry` / `EntityComponentRegistry` — generic registry; use `EntityComponentRegistry.GetEnabled<T>()` to enumerate by component type.
- Events: `CharacterCreatedEvent`, `CharacterKilledEvent` (both in `Timberborn.Characters`), `BeaverBornEvent` (in `Timberborn.Beavers`, no payload fields).

## Injured / infected / contaminated

- **Injured**: no `Injurable.IsInjured` exists. The probe found only `InjurableNeedSpec` (in `Timberborn.NeedApplication`), `InjuryChanceModifierKey`, `InjuryNeedId` (in `Timberborn.Healthcare`). Injury is represented as an active `Need` on the character. ⚠️ resolve at impl time: detect injury by iterating `Timberborn.NeedSystem.Needs.AllNeeds` on a character and checking for an active need whose id equals the `InjuryNeedId` constant (exposed by Healthcare) — or the `ChippedTeethNeedId`. There's no one-line boolean API.
- **Incubating (pre-contamination)**: `Timberborn.BeaverContaminationSystem.ContaminationIncubator` (component on Character)
  - `IsIncubating` (`get_IsIncubating`)
  - `IncubationProgress`, `IncubationFinished`.
- **Contaminated (statistics)**: `Timberborn.PopulationStatisticsSystem.IContaminationStatisticsProvider`
  - Method: `GetContaminationStatistics(DistrictCenter)` (district impl) or `GetContaminationStatistics()` (global impl) — returns `BeaverContaminationStatistics`.
  - `BeaverContaminationStatistics`: `ContaminatedAdults`, `ContaminatedChildren`, `ContaminatedTotal`, `IsAnyoneContaminated` (from `Timberborn.Population`).
  - District impl: `DistrictBeaverContaminationStatisticsProvider` (in `Timberborn.BeaverContaminationSystem`).
  - Global impl: `GlobalBeaverContaminationStatisticsProvider`.

## Homeless / unemployed

- `Timberborn.PopulationStatisticsSystem.IDwellingStatisticsProvider`
  - `GetDwellingStatistics(DistrictCenter)` returns `DwellingStatistics`.
  - `DwellingStatistics` fields: `FreeBeds`, `OccupiedBeds`, `Total`, `Vacancies` (all `int`).
  - District impl: `DistrictDwellingStatisticsProvider` (in `Timberborn.DwellingSystem`).
  - Global impl: `GlobalDwellingStatisticsProvider` (in `Timberborn.DwellingSystem`).
- `Timberborn.PopulationStatisticsSystem.IEmploymentStatisticsProvider`
  - `GetEmploymentStatistics(DistrictCenter)` returns `EmploymentStatistics`.
  - `EmploymentStatistics` fields: `EmployedWorkers`, `Vacancies`, `Total`, `NotRefusingWorkers`, `RefusingWorkers`, `WorkerType` (plus `ContaminatedAdults`, `ContaminatedChildren`).
  - District impl: `DistrictEmploymentStatisticsProvider` (in `Timberborn.PopulationWorkStatistics`).
  - Global impl: `GlobalEmploymentStatisticsProvider` (in `Timberborn.PopulationWorkStatistics`).
- **Homeless / unemployed counts** are also directly exposed on `Timberborn.Population` types (`get_Homeless`, `get_Unemployed`, `get_Employable`, `get_Unemployable`) — these aggregate at the global level (`GlobalPopulationData`) and per-district (`DistrictPopulationData`). Prefer these for headline numbers; fall back to the statistics providers for detail breakdowns.

## Hotkey / input

- `Timberborn.InputSystem.InputService` — DI-injectable.
  - `IsKeyDown(...)` confirmed in the probe.
- Phase-1 fallback: `UnityEngine.Input.GetKey(KeyCode)` / `Input.GetKeyDown(KeyCode)`. Works without DI wiring if we just need a "F9 toggles panel" hotkey before the proper KeyBinding integration lands.
- Proper binding (phase-2): `Timberborn.KeyBindingSystem` — `KeyBindingRegistry`, `KeyBindingFactory`, `InputBindingKey`, `KeyBindingSpecService`. Register a `KeyBindingGroupSpec` + `KeyBindingDefinition`, then listen via `InputBindingListener`.

## UI

- `Timberborn.RootProviders.RootVisualElementProvider` (namespace is `Timberborn.RootProviders`, not `Timberborn.CoreUI` — the type lives in `Timberborn.CoreUI.dll` but its namespace is `RootProviders`).
  - Field/property: `rootVisualElement` (`get_rootVisualElement`) — the `UnityEngine.UIElements.VisualElement` panel root.
- Append / remove pattern: resolve `RootVisualElementProvider` via DI, then `provider.rootVisualElement.Add(myGraphPanel)` on open and `Remove(myGraphPanel)` on close. No dedicated API — just standard UIElements hierarchy manipulation.
- Helpers available: `Timberborn.CoreUI.VisualElementInitializer` (auto-wires child controls), `VisualElementLoader` (loads `.uxml`), `VisualElementExtensions`.
- Dialog / modal helpers: `DialogBoxShower`, `PanelStack` (not needed for our embedded panel).

## Lifecycle / DI

- `Timberborn.TickSystem.ITickableSingleton` — `Tick()` called every game tick. Use for sampling at the tick rate. (Note: lives in `TickSystem`, NOT `SingletonSystem`.)
- `Timberborn.SingletonSystem.ILoadableSingleton` — `Load()` called once after DI wiring. Use for `EventBus.Register(this)`.
- `Timberborn.SingletonSystem.IPostLoadableSingleton` — `PostLoad()` after all `Load()` calls; use if we need to read save-game-populated state.
- `Timberborn.SingletonSystem.ISaveableSingleton` + `ISingletonLoader` / `ISingletonSaver` — for persisting mod data across saves.
- `EventBus` (in `Timberborn.SingletonSystem`):
  - Register: `_eventBus.Register(this)` in `Load()`.
  - Handler attribute: `[OnEvent]` on method taking a single event-arg type.
- DI: Bindito `Configurator` with `[Context("Game")]`. Bind services as singletons: `Bind<MyService>().AsSingleton()`.

---

## Corrections found during implementation

**Population data — the clean path**
- `Timberborn.Population.PopulationDataCollector` — DI-injectable. `CollectData(DistrictCenter, PopulationData)` fills a `PopulationData` struct with every derived count: `BedData.Homeless`, `BeaverWorkplaceData.Unemployed`, `ContaminationData.ContaminatedTotal`, `TotalPopulation`, etc. Single point of access — much cleaner than wiring individual statistics providers.
- `Timberborn.Population.PopulationService` exposes `GlobalPopulationData`. `DistrictPopulationData` only gives the currently-switched district, not arbitrary filtering.

**Per-district head counts**
- `DistrictCenter.DistrictPopulation` is a sibling **component** (`Timberborn.GameDistricts.DistrictPopulation`), NOT an int on `DistrictCenter`. Get it via `districtCenter.GetComponent<DistrictPopulation>()`.
- `DistrictPopulation` component exposes: `NumberOfAdults`, `NumberOfChildren`, `NumberOfBots`, `Beavers` (ReadOnlyList<Beaver>).
- There is no `DistrictPopulation` int on `DistrictCenter` directly.

**Statistics-provider struct shapes (actual)**
- `DwellingStatistics` fields: `OccupiedBeds`, `FreeBeds` (NOT `Homed`, `Homeless`, `Total`, `Vacancies`).
- `EmploymentStatistics` requires a `workerType: string` argument. Fields: `EmployedWorkers`, `Vacancies`, `WorkerType` (no direct `Unemployed`).
- `BeaverContaminationStatistics` fields: `ContaminatedAdults`, `ContaminatedChildren`, `Total` (NOT `ContaminatedTotal` — that name lives on `ContaminationData`).

**Enumerating components**
- `EntityComponentRegistry.GetEnabled<T>()` and `DistrictPopulation.GetEnabledCharacters<T>()` both require `T : BaseComponent, IRegisteredComponent`.
- `ContaminationIncubator` is a `BaseComponent` but NOT `IRegisteredComponent` → can't enumerate directly. Iterate beavers via `DistrictPopulation.Beavers` and call `beaver.GetComponent<ContaminationIncubator>()` on each.
- `Beaver` (in `Timberborn.Beavers`) and `Bot` (in `Timberborn.Bots`) both extend `BaseComponent` → they support `GetComponent<T>()`.
- `Timberborn.GameDistricts.Citizen.AssignedDistrict` — hook from a Character back to its `DistrictCenter`.

## Items deferred to impl time

1. `StorableGoodRegistry.Goods` element type (`StorableGood` vs `GoodSpec`) — inspect via LSP.
2. `Inventory.Stock` per-good lookup shape — iterate the enumerable and match `GoodId`, or use `GetAmount(goodId)` if it exists on `Inventory`.
3. Detecting "beaver is injured" — no boolean; walk `Needs.AllNeeds` looking for the injury need id. Confirm the exact constant name/value at impl time (`InjuryNeedId` field found in `Timberborn.Healthcare`).

## Deltas from the prior template

- `DistrictCenterRegistry.FinishedDistrictCenters` — confirmed as-is.
- `DistrictCenter.DistrictName / DistrictPopulation / NumberOfAdults / NumberOfBots / DistrictBuilding` — all confirmed; added `NumberOfChildren`. Note `DistrictBuilding` is a sibling component, not a property on `DistrictCenter` itself.
- `ScienceService.SciencePoints` — confirmed.
- `Injurable.IsInjured` — **does not exist**. Replaced with "active need with injury id" pattern.
- `ContaminationIncubator.IsIncubating` — confirmed.
- `Wellbeing` component — **does not exist as bare `Wellbeing`**. The per-character component is `WellbeingTracker`; score is `WellbeingTracker.Wellbeing`.
- `GoodSpecService` — **does not exist**. Use `StorableGoodRegistry.Goods` instead.
- `inv.Stock.Get(id)` — no such API. Iterate `Stock` or use per-amount `GetAmount`.
- `RootVisualElementProvider` — lives in `Timberborn.CoreUI.dll` but namespace is `Timberborn.RootProviders`.
