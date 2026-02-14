# Timberborn Modding Reference Notes

## Overview
- Timberborn mods are C# projects targeting **netstandard2.1**
- Uses **BepInEx** as the mod loader (required for creating and running mods)
- Uses **Bindito** (in-house dependency injection framework)
- Unity is NOT required for code-only mods
- Mod entry point: implement `IModStarter` from `Timberborn.ModManagerScene`

## Mod Directory Structure
```
Documents/Timberborn/Mods/
└── MyMod/
    ├── manifest.json
    └── version-1.0/          (or version-0.7 for U7)
        ├── Scripts/
        │   └── MyMod.dll
        ├── Localizations/
        │   └── enUS.csv
        ├── Blueprints/
        ├── Specifications/
        ├── Assets/
        ├── AssetBundles/
        ├── Sprites/
        ├── Resources/
        └── Sounds/
```

## manifest.json Format
```json
{
    "Name": "Display Name",
    "Version": "7.0.0",
    "Id": "UniqueModId",
    "MinimumGameVersion": "0.7.0.0",
    "Description": "What the mod does.",
    "RequiredMods": [
        { "Id": "Harmony", "MinimumVersion": "0.6.0.0" }
    ],
    "OptionalMods": [
        { "Id": "AnotherMod" }
    ]
}
```
- **MinimumGameVersion**: "0.7.0.0" for Update 7, "1.0.0.0" for V1
- RequiredMods/OptionalMods are optional arrays

## .csproj Configuration
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

    <!-- Reference Timberborn managed DLLs -->
    <ItemGroup>
        <Reference Include="PATH_TO_TIMBERBORN/Timberborn_Data/Managed/*.dll">
            <Private>false</Private>
        </Reference>
    </ItemGroup>

    <!-- For Harmony mods -->
    <ItemGroup>
        <Using Include="HarmonyLib"/>
        <Reference Include="PATH_TO_HARMONY/0Harmony.dll">
            <Private>false</Private>
        </Reference>
    </ItemGroup>
</Project>
```
- `Private=false` on all game references to avoid copying them to output
- References: game DLLs, System.Collections.Immutable, Newtonsoft.Json

## Mod Entry Point (IModStarter)
```csharp
using Timberborn.ModManagerScene;

namespace MyMod;

public class ModStarter : IModStarter
{
    public void StartMod(IModEnvironment modEnvironment)
    {
        // For Harmony mods:
        new Harmony(nameof(MyMod)).PatchAll();
    }
}
```

## Dependency Injection with Bindito

### Configurator Pattern
```csharp
using Bindito;

[Context("Game")]       // or "MainMenu"
public class MyModConfigurator : Configurator
{
    public override void Configure()
    {
        Bind<MyService>().AsSingleton();
        Bind<ISomeInterface>().As<SomeImplementation>();
        MultiBind<TemplateModule>().ToProvider(() => GetTemplateModule());
    }
}
```

### Context Types
- `"MainMenu"` - Main menu context
- `"Game"` - In-game context

### Binding Types
- `Bind<T>().AsSingleton()` - Single instance
- `Bind<T>().AsTransient()` - New instance each time
- `Bind<IFoo>().As<Foo>()` - Interface to implementation
- `MultiBind<T>().ToExisting<U>()` - Collection bindings

### Injection Methods
```csharp
// Constructor injection (preferred)
public class MyService(MSettings settings, IEnumerable<TemplateModule> modules) { }

// Method injection (for MonoBehaviour/BaseComponent)
[Inject]
public void InjectDependencies(MSettings settings) { }
```

## BaseComponent System
```csharp
public class MyComponent : BaseComponent
{
    WaterSource waterSource;

    public void Awake()
    {
        waterSource = GetComponentFast<WaterSource>();
    }
}
```
- `BaseComponent` is the base class for all game components
- `GetComponentFast<T>()` retrieves sibling components
- Lifecycle: Awake() -> Inject() -> Start()

### TickableComponent
```csharp
public class MyTickable : TickableComponent
{
    public override void Tick()
    {
        // Called each game tick
    }
}
```

## Harmony Patching
```csharp
[HarmonyPatch]
public static class MyPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.MethodName))]
    public static bool PrefixPatch(TargetClass __instance)
    {
        // Runs before original method
        // Return false to skip original
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.MethodName))]
    public static void PostfixPatch(TargetClass __instance, ref ReturnType __result)
    {
        // Runs after original method
    }
}
```

## Mod Settings
```csharp
public class MSettings(
    ISettings settings,
    ModSettingsOwnerRegistry modSettingsOwnerRegistry,
    ModRepository modRepository)
    : ModSettingsOwner(settings, modSettingsOwnerRegistry, modRepository)
{
    public override string ModId { get; } = "YourModId";
    public override ModSettingsContext ChangeableOn { get; } = ModSettingsContext.All;

    public ModSetting<float> MySetting { get; } = new(1f,
        ModSettingDescriptor.CreateLocalized("LocKey"));

    public static MSettings? Instance { get; private set; }

    public override void OnAfterLoad()
    {
        Instance = this;
    }
}
```

## Key Multibindings
- `TemplateModule` - Add components to prefabs
- `EntityPanelModule` - Register UI info panels
- `IDevModule` - Debug commands
- `IWaterStrengthModifier` - Modify water mechanics

## TimberAPI Features (unofficial community API)
- **UIBuilder** - Visual element presets, StylesheetBuilder, FragmentBuilder
- **BuildingSpecifications** - Adjust building aspects
- **ToolSpecifications** - Customize tools
- **EntityLinker** - Connect buildings
- **SpecificationGenerator** - Runtime spec creation

## Useful Tools
- **ILSpy/DnSpy** - Decompile game DLLs to understand internals
- **AssetRipper** - Extract game assets
- **Dev Mode** - Alt+Shift+Z in game for testing
- **AssemblyPublicizer** - Expose private game APIs

## Key Namespaces
- `Timberborn.ModManagerScene` - IModStarter, IModEnvironment
- `Bindito` - Configurator, Context attribute
- `HarmonyLib` - Harmony patching
- `Timberborn.*` - Various game systems

## Sources
- Official modding tools: https://github.com/mechanistry/timberborn-modding
- Official wiki: https://github.com/mechanistry/timberborn-modding/wiki
- TimberAPI: https://github.com/Timberborn-Modding-Central/TimberAPI
- TimberAPI docs: https://timberapi.com
- Luke's guide: https://datvm.github.io/TimberbornMods/ModdingGuide/
- Example mods: https://github.com/datvm/TimberbornMods
