# Timbermods — Claude Instructions

## Project Layout
- One mod per subdirectory. Each mod has a `.csproj`, `ModStarter.cs`, `manifest.json`, `lib/` (Harmony libs).
- Build: `./build.sh ModName` (or `./build.sh` for all). Sets `DOTNET_ROOT` automatically.
- Deploy: flat layout in `~/Documents/Timberborn/Mods/<ModName>/` — NO `version-x/` subdirs.

## C# Project Setup
- Target: `netstandard2.1`, LangVersion: `preview`, ImplicitUsings, Nullable
- References (all `Private=false`): `Timberborn.*.dll`, `Bindito.*.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `0Harmony`
- Do NOT wildcard all `Managed/*.dll` — System.* conflicts. Only Timberborn.*, Bindito.*, UnityEngine.

## Entry Point
```csharp
public class ModStarter : IModStarter {
    public void StartMod(IModEnvironment modEnvironment) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            Anatawa12.AppleSiliconHarmony.Patcher.Patch();
        new Harmony("ModId").PatchAll();
    }
}
```

## DI / EventBus Pattern (no Harmony needed)
```csharp
[Context("Game")]
public class MyConfigurator : Configurator {
    protected override void Configure() { Bind<MyService>().AsSingleton(); }
}
public class MyService : ILoadableSingleton {
    public MyService(EventBus eventBus) { _eventBus = eventBus; }
    public void Load() { _eventBus.Register(this); }
    [OnEvent] public void OnSomeEvent(SomeEvent e) { ... }
}
```

## Key API Facts
- **Priority enum**: `Timberborn.PrioritySystem.Priority` — values: `VeryLow, Low, Normal, High, VeryHigh`
  - `Priorities` (same namespace) is a STATIC CLASS (not the enum). Use `Priority` not `Priorities`.
  - `Priority` conflicts with `HarmonyLib.Priority` — alias: `using TBPriority = Timberborn.PrioritySystem.Priority;`
- **Placement event**: `EnteredUnfinishedStateEvent` (in `Timberborn.BlockSystem`) — fires when a building is placed as a blueprint (NOT on save load). Has `.BlockObject` property.
- **BlockObject**: Has `GetComponent<T>()` (acts like MonoBehaviour). Use to find sibling components.
- **BaseComponent types** (e.g. `UnconnectedBuildingBlocker`): NOT MonoBehaviours — NO `GetComponent`/`gameObject`. Access sibling components via reflection: find field typed as `BlockObject`, then call `blockObject.GetComponent<T>()`.
- **Private methods**: Use `GetMethod("Name", BindingFlags.NonPublic | BindingFlags.Instance)` + `Invoke`.
- **Harmony + TargetMethod()**: For private/internal targets use `static MethodBase? TargetMethod() => typeof(T).GetMethod("Name", BindingFlags.NonPublic | BindingFlags.Instance);`
- **Priorities (builder)**: `BuilderPrioritizable.SetPriority(Priority p)` — `BuilderPrioritizable` in `Timberborn.BuilderPrioritySystem`.
- **Science unlock**: `ScienceNeedingBuilding.UnlockIgnoringCost()` is private/internal — invoke via reflection.
- **Automatable**: `Timberborn.Automation.Automatable` — component that marks buildings controllable by automation system (floodgates, sensors, etc.).
- **UnconnectedBuildingBlocker**: In `Timberborn.BuildingsReachability` — blocks building from operating when not road-connected. Has `Activate()` method.

## Mods in this Repo
| Folder | Display Name | Purpose |
|--------|-------------|---------|
| `LowPriorityDynamite/` | Low Priority Dynamite | Placed dynamite defaults to Low builder priority |
| `FreeAutomation/` | Free Automation | Automation buildings: no science needed, no road required |
| `DynamiteRubble/` | Dynamite Rubble | Explosions drop Dirt based on terrain destroyed |
| `GeneticLottery/` | Genetic Lottery | Each beaver born gets random ±10% lifespan modifier |
| `PowerZipline/` | Power Zipline | Ziplines transfer mechanical power |
