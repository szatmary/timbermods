# LogBook mod — internal API notes

Source of truth for implementers. Names verified against the Timberborn
game DLLs in the Steam install's `Timberborn.app/.../Managed/` directory.

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

- **Enumerating good ids (confirmed)**: `Timberborn.Goods.IGoodService.Goods` returns `ReadOnlyList<string>` — strings directly, no spec unwrap needed. Impl is `Timberborn.Goods.GoodService`. Prefer the interface for DI. (`StorableGoodRegistry.Goods` exists too but is per-inventory and element type is `StorableGood` struct with `.GoodId` — the right enumeration for whole-settlement good ids is `IGoodService.Goods`.)
- `IGoodService` also exposes `GetGood(id) -> GoodSpec`, `GetGoodOrNull`, `HasGood`, `GetGoodsForGroup`, `GetGoodsForType`.
- `Timberborn.Goods.StorableGood` is a **struct** with `GoodId / Takeable / Givable` (not `Id`).
- `Timberborn.Goods.GoodSpec` (a `ComponentSpec` blueprint type) has `Id`, `DisplayNameLocKey`, `DisplayName`, `Icon`, etc. — use this when you need a loc key for a good.
- `Timberborn.InventorySystem.DistrictInventoryRegistry`
  - `Inventories` (`get_Inventories`) — `ReadOnlyHashSet<Inventory>`. (No `AllInventories` property — that was a template myth.)
  - `ActiveInventoriesWithStock(string goodId)` / `ActiveInventoriesWithCapacity(string goodId)` — pre-filtered sets for one good (useful when scanning by good, not by inventory).
- `Timberborn.InventorySystem.Inventory` (component, extends `BaseComponent`)
  - `AmountInStock(string goodId) -> int` — **confirmed**; this is the per-good stock lookup. Also `UnreservedAmountInStock(goodId)`, `GoodCapacity(goodId)`, `ReservedCapacity(goodId)`.
  - `Stock` (`get_Stock`) — `ReadOnlyList<?>` (enumeration of stored goods; use `AmountInStock` instead of iterating).
  - `TotalAmountInStock`, `IsEmpty`, `IsFull`, `Capacity`, `AllowedGoods`.
- Inventory -> owning district: `inventory.GetComponent<DistrictBuilding>()?.District?.DistrictName` (`DistrictBuilding` is a sibling `BaseComponent`; `District` returns a `DistrictCenter`, may be null before assignment).
- `Timberborn.Goods.GoodAmount` — struct `(GoodId, Amount)`.

## Science

- `Timberborn.ScienceSystem.ScienceService`
  - `SciencePoints` (`get_SciencePoints`) — int.

## Wellbeing

- `Timberborn.Wellbeing.WellbeingService` — DI-injectable, **but** its public surface is:
  - `AverageGlobalWellbeing` (int property)
  - `AverageDistrictWellbeing` (int property — current switched district only, not arbitrary)
  - `SwitchDistrict(DistrictCenter)` (mutates which district is tracked)
  - ⚠ **Does NOT have `GetAverageWellbeing(DistrictCenter)` or `GetMaxWellbeing(...)`** — those names in the old template doc were wrong.
- `Timberborn.Wellbeing.WellbeingTracker` (component on Character)
  - `Wellbeing` property (`get_Wellbeing`) — the per-beaver score. Type is `int`, not `float`.
- `Timberborn.Wellbeing.WellbeingTrackerRegistry` — **internal** (can't be referenced from a mod). Exposes `GetAverageWellbeing()` + private `_wellbeingTrackers` HashSet. To aggregate without this type, iterate beavers directly via `DistrictPopulation.Beavers` and read each `WellbeingTracker.Wellbeing`.
- `GlobalWellbeingTrackerRegistry`, `DistrictWellbeingTrackerRegistry` — both **internal**; can't be injected either. `DistrictWellbeingTrackerRegistry` is a sibling component on `DistrictCenter` (reachable only through reflection since the type isn't visible).
- Events: `WellbeingChangedEventArgs` (with `OldWellbeing`, `NewWellbeing`), `NewWellbeingHighscoreEvent`.
- No plain `Wellbeing` type — use `WellbeingTracker` component.

## Needs / hunger / thirst

- `Timberborn.NeedSystem.NeedManager` (component on Character) — public surface for probing a need on one beaver:
  - `HasNeed(string needId) -> bool`
  - `NeedIsActive(string needId) -> bool`
  - `GetNeedPoints(string needId) -> float` (current value)
  - `GetNeedSpec(string needId) -> NeedSpec` (null when not present)
  - `NeedSpecs` — `ImmutableArray<NeedSpec>` (all needs on this character)
  - ⚠ **There is NO `GetSatisfaction` method**. Compute satisfaction as `GetNeedPoints(id) / GetNeedSpec(id).MaximumValue` — returns a 0..1 float.
- `Timberborn.NeedSpecs.NeedSpec` — the blueprint:
  - `Id`, `MaximumValue`, `MinimumValue`, `StartingValue`, `DailyDelta`, `DisplayNameLocKey`, etc.
- **Food / water need ids are faction-specific** (Folktails and Iron Teeth use different spec ids). They're stored in `Timberborn.GameFactionSystem.NeedModificationService` as **non-public static readonly string** fields named `FoodNeedId` and `WaterNeedId`, populated at config time. Read them via reflection: `typeof(NeedModificationService).GetField("FoodNeedId", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)`.
- Separate (achievement-only) constants `HungerNeedId` / `ThirstNeedId` exist on `Timberborn.Achievements.BeaverDiesMiserableAchievement` (also private static) — use the `NeedModificationService` pair for satisfaction metrics; those are the live ids the `NeedManager` queries against.
- `FactionNeedService` (public, DI-injectable) exposes `GetBeaverNeeds()`, `GetBotNeeds()`, `IsCurrentFactionNeed(id)`, `GetBeaverOrBotNeedById(id)` — use when you need to enumerate faction-scoped need specs directly.

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

- `Timberborn.RootProviders.RootVisualElementProvider` (type lives in `Timberborn.CoreUI.dll`, namespace is `Timberborn.RootProviders`).
  - **No `rootVisualElement` member.** The earlier `get_rootVisualElement` seen in string probes comes from Unity's `UIDocument`, not this provider.
  - Real API: `CreateEmpty(string name, int sortOrder) -> UIDocument`. Also `Create(GameObject parent, string sourceAssetPath, int sortOrder, string panelSettingsPath)`.
- Open/close pattern: `var doc = provider.CreateEmpty("my-panel", sortOrder);`, then `doc.rootVisualElement.Add(myPanel)`. To close: `myPanel.RemoveFromHierarchy();` **and** `UnityEngine.Object.Destroy(doc.gameObject);` — otherwise the UIDocument host leaks per open.
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

1. Detecting "beaver is injured" — no boolean; walk `Needs.AllNeeds` looking for the injury need id. Confirm the exact constant name/value at impl time (`InjuryNeedId` field found in `Timberborn.Healthcare`).

### Resolved during impl
- Good enumeration: use `IGoodService.Goods` (ReadOnlyList\<string\>) — strings directly.
- Per-inventory stock: `Inventory.AmountInStock(goodId)`.
- Inventory -> district: `inventory.GetComponent<DistrictBuilding>()?.District?.DistrictName`.
- `DistrictInventoryRegistry.Inventories` (not `AllInventories`).

## Deltas from the prior template

- `DistrictCenterRegistry.FinishedDistrictCenters` — confirmed as-is.
- `DistrictCenter.DistrictName / DistrictPopulation / NumberOfAdults / NumberOfBots / DistrictBuilding` — all confirmed; added `NumberOfChildren`. Note `DistrictBuilding` is a sibling component, not a property on `DistrictCenter` itself.
- `ScienceService.SciencePoints` — confirmed.
- `Injurable.IsInjured` — **does not exist**. Replaced with "active need with injury id" pattern.
- `ContaminationIncubator.IsIncubating` — confirmed.
- `Wellbeing` component — **does not exist as bare `Wellbeing`**. The per-character component is `WellbeingTracker`; score is `WellbeingTracker.Wellbeing` (int).
- `WellbeingService.GetAverageWellbeing(...) / GetMaxWellbeing(...)` — **do not exist**. The service only exposes `AverageGlobalWellbeing` and `AverageDistrictWellbeing` (both int properties tied to the currently-switched district). Aggregate averages by walking `DistrictPopulation.Beavers` directly; the tracker-registry types are internal.
- `NeedManager.GetSatisfaction(id)` — **does not exist**. Compute `GetNeedPoints(id) / GetNeedSpec(id).MaximumValue` instead.
- `GoodSpecService` — **does not exist**. Use `IGoodService.Goods` (strings) for enumeration; `IGoodService.GetGood(id)` for the `GoodSpec` blueprint.
- `inv.Stock.Get(id)` — no such API. Use `Inventory.AmountInStock(goodId)` for a single good; `Stock` is a `ReadOnlyList` for iteration.
- `DistrictInventoryRegistry.AllInventories` — **does not exist**. Use `Inventories` (ReadOnlyHashSet\<Inventory\>).
- `RootVisualElementProvider` — lives in `Timberborn.CoreUI.dll` but namespace is `Timberborn.RootProviders`.
