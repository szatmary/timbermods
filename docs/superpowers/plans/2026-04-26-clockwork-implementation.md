# Clockwork Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Timberborn mod that adds a left-side drawer for viewing and editing automation wiring across the world. Mirrors vanilla `AutomatorPartition`s; adds only a per-anchor name dictionary.

**Architecture:** Standalone mod under `/Clockwork/`, mirroring the structure of the existing `Graphs/` mod (csproj, ModStarter, Configurator, manifest, lib/). Bindito DI for services; vanilla `UILayout.AddPanel` for the non-modal drawer; vanilla `AutomatorRegistry`/`AutomatorPartition` for the source-of-truth data; vanilla `EntityNaming.NamedEntity` (added to receiver blueprints via patches) for leaf naming. Mod-side persistent state is one `Dictionary<Guid, string>` saved as a singleton.

**Tech Stack:** C# `netstandard2.1`, `LangVersion=preview`, ImplicitUsings, Nullable. References game DLLs (`Timberborn.*`, `Bindito.*`, `UnityEngine.*`) with `Private=false`. Harmony for blueprint patches and any reflection-required hooks. Unity UI Toolkit (UIElements) for the drawer.

**Existing-codebase conventions** that this plan follows (do NOT deviate):
- One mod per top-level directory; build with `./build.sh ClockWork` from repo root.
- Deploy is flat (no `version-x/`): `~/Documents/Timberborn/Mods/Clockwork/` contains `manifest.json`, `Clockwork.dll`, `lib/`, `Localizations/`.
- No unit tests for any existing mod — verification is build-clean + manual in-game smoke test. **This plan follows that convention**: each task ends with a manual verification step, not an automated one.
- `IModStarter.StartMod` does the Apple-Silicon Harmony shim then `new Harmony("Clockwork").PatchAll()`.
- `[Context("Game")]` configurators for in-game services.
- Use `Timberborn.CoreUI` from `lib/publicized/` if internals are needed (matches Graphs pattern).

---

## File structure

```
Clockwork/
├── Clockwork.csproj                     # build config (mirror Graphs/Graphs.csproj)
├── ModStarter.cs                        # IModStarter entry point
├── ClockworkConfigurator.cs             # Bindito bindings ([Context("Game")])
├── manifest.json                        # mod metadata
├── lib/                                 # 0Harmony.dll + AppleSiliconHarmony libs
│   └── publicized/
│       └── Timberborn.CoreUI.dll        # publicized; only if needed for UI internals
├── Localizations/
│   └── enUS.csv                         # any localized strings we add
├── Data/
│   └── ClockworkRegistry.cs             # saved singleton: anchor automatorId → name
├── Services/
│   ├── PartitionSnapshot.cs             # POCO snapshot data for one partition
│   ├── PartitionSnapshotService.cs      # builds snapshots from vanilla state each tick
│   └── BuildingPing.cs                  # camera-focus helper
├── UI/
│   ├── ClockworkPanel.cs                # the drawer root + UILayout integration
│   ├── ClockworkHotkey.cs               # Shift+C (or configurable) toggle
│   ├── PartitionRow.cs                  # one Level-1 row (collapsed view)
│   ├── PartitionExpandedView.cs         # Level-2 tree-by-emitter
│   ├── EmitterRow.cs                    # one emitter + downstream wire children
│   ├── WireRow.cs                       # one wire row (receiver child of emitter)
│   ├── WirePicker.cs                    # add-wire overlay
│   └── ClockworkColors.cs               # signal-state colors, neutral chrome
└── Patches/
    ├── LeafNamePatches.cs               # Harmony patches to add NamedEntity to receiver blueprints
    └── (only as needed for any other reflection hooks)
```

Each file has one responsibility; this matches Graphs' `Metrics/` + `UI/` split.

---

## Task 1: Project skeleton

**Files:**
- Create: `Clockwork/Clockwork.csproj`
- Create: `Clockwork/ModStarter.cs`
- Create: `Clockwork/ClockworkConfigurator.cs`
- Create: `Clockwork/manifest.json`
- Copy:   `Clockwork/lib/0Harmony.dll`, `Clockwork/lib/AppleSiliconHarmony.dll`, `Clockwork/lib/libapple_silicon_harmony_native.dylib` (from `Graphs/lib/`)
- Copy:   `Clockwork/lib/publicized/Timberborn.CoreUI.dll` (from `Graphs/lib/publicized/`)

- [ ] **Step 1: Create the directory and copy lib/**

```bash
mkdir -p /Users/matthewszatmary/Projects/timbermods/Clockwork/lib/publicized
cp /Users/matthewszatmary/Projects/timbermods/Graphs/lib/0Harmony.dll \
   /Users/matthewszatmary/Projects/timbermods/Graphs/lib/AppleSiliconHarmony.dll \
   /Users/matthewszatmary/Projects/timbermods/Graphs/lib/libapple_silicon_harmony_native.dylib \
   /Users/matthewszatmary/Projects/timbermods/Clockwork/lib/
cp /Users/matthewszatmary/Projects/timbermods/Graphs/lib/publicized/Timberborn.CoreUI.dll \
   /Users/matthewszatmary/Projects/timbermods/Clockwork/lib/publicized/
```

- [ ] **Step 2: Write `Clockwork.csproj` (copy Graphs.csproj, change AssemblyName)**

Open `Graphs/Graphs.csproj`, copy verbatim to `Clockwork/Clockwork.csproj`, then change every occurrence of the assembly/output name from `Graphs` to `Clockwork`. The DLL output should land at `Clockwork/bin/Release/netstandard2.1/Clockwork.dll`.

- [ ] **Step 3: Write `manifest.json`**

```json
{
    "Name": "Clockwork",
    "Version": "0.1.0",
    "Id": "Clockwork",
    "MinimumGameVersion": "1.0.0.0",
    "Description": "A left-side drawer for viewing and editing automation wiring across your colony. Names automation flows ('Clockworks') so you can find them by name. No new game features; just a UI over vanilla automation."
}
```

- [ ] **Step 4: Write `ModStarter.cs`**

```csharp
using System.Runtime.InteropServices;
using HarmonyLib;
using Timberborn.ModManagerScene;

namespace Clockwork;

public class ModStarter : IModStarter
{
    public void StartMod(IModEnvironment modEnvironment)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            Anatawa12.AppleSiliconHarmony.Patcher.Patch();

        new Harmony("Clockwork").PatchAll();
    }
}
```

- [ ] **Step 5: Write empty `ClockworkConfigurator.cs`**

```csharp
using Bindito.Core;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        // bindings added in later tasks
    }
}
```

- [ ] **Step 6: Build and confirm 0 errors**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

Expected: build succeeds, deploys `Clockwork.dll` + `manifest.json` + `lib/` + (empty) `Localizations/` to `~/Documents/Timberborn/Mods/Clockwork/`.

- [ ] **Step 7: Smoke-test — launch Timberborn and confirm the mod loads**

Launch the game. Open the Mods menu and confirm "Clockwork 0.1.0" is listed without errors. Start a quick game to verify the configurator runs (no crash on entering a game scene).

- [ ] **Step 8: Commit**

```bash
git add Clockwork/
git commit -m "Clockwork: project skeleton (no features yet)"
```

---

## Task 2: ClockworkRegistry singleton

**Files:**
- Create: `Clockwork/Data/ClockworkRegistry.cs`
- Modify: `Clockwork/ClockworkConfigurator.cs`

Goal: a saveable singleton holding `Dictionary<Guid, string>` (automatorId → user-set name), with `Set` / `Remove` / `TryGet` methods and a `Changed` event.

- [ ] **Step 1: Write `Data/ClockworkRegistry.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorldPersistence;

namespace Clockwork.Data;

/// Persistent name-per-anchor map. The anchor is one automator within a
/// vanilla AutomatorPartition; the partition's display name in the drawer
/// is the name of any anchor it currently contains.
public sealed class ClockworkRegistry : ILoadableSingleton, ISaveableSingleton
{
    private static readonly SingletonKey SavedKey = new("ClockworkRegistry");
    private static readonly ListKey<string> SavedAnchorIds = new("AnchorIds");
    private static readonly ListKey<string> SavedNames = new("Names");

    private readonly ISingletonLoader _singletonLoader;
    private readonly Dictionary<Guid, string> _namesByAnchor = new();

    public event Action? Changed;

    public ClockworkRegistry(ISingletonLoader singletonLoader)
    {
        _singletonLoader = singletonLoader;
    }

    public void Load()
    {
        if (!_singletonLoader.TryGetSingleton(SavedKey, out var loader)) return;
        try
        {
            var ids = loader.Get(SavedAnchorIds);
            var names = loader.Get(SavedNames);
            int n = Math.Min(ids.Count, names.Count);
            for (int i = 0; i < n; i++)
            {
                if (Guid.TryParse(ids[i], out var g))
                    _namesByAnchor[g] = names[i];
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Clockwork] registry restore failed: {ex.Message}");
        }
    }

    public void Save(ISingletonSaver singletonSaver)
    {
        var saver = singletonSaver.GetSingleton(SavedKey);
        saver.Set(SavedAnchorIds, _namesByAnchor.Keys.Select(g => g.ToString()).ToArray());
        saver.Set(SavedNames, _namesByAnchor.Values.ToArray());
    }

    public bool TryGet(Guid anchor, out string name) =>
        _namesByAnchor.TryGetValue(anchor, out name!);

    public void Set(Guid anchor, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { Remove(anchor); return; }
        _namesByAnchor[anchor] = name;
        Changed?.Invoke();
    }

    public void Remove(Guid anchor)
    {
        if (_namesByAnchor.Remove(anchor)) Changed?.Invoke();
    }

    public IReadOnlyDictionary<Guid, string> All => _namesByAnchor;
}
```

- [ ] **Step 2: Bind it in `ClockworkConfigurator.cs`**

Replace the empty `Configure()` body:

```csharp
using Bindito.Core;
using Clockwork.Data;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ClockworkRegistry>().AsSingleton();
    }
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Smoke-test save/load**

Launch the game, start a fresh save. The registry should be empty. Save the game and reload it — the registry should still be empty (no errors in Player.log). The actual name-setting will be tested once the UI exists; this step just confirms the singleton wires up cleanly.

Inspect Player.log at `~/Library/Logs/Timberborn/Player.log` for any `[Clockwork]` warnings. None expected.

- [ ] **Step 5: Commit**

```bash
git add Clockwork/Data/ClockworkRegistry.cs Clockwork/ClockworkConfigurator.cs
git commit -m "Clockwork: ClockworkRegistry singleton (anchor → name map, save/load)"
```

---

## Task 3: PartitionSnapshot data + service

**Files:**
- Create: `Clockwork/Services/PartitionSnapshot.cs`
- Create: `Clockwork/Services/PartitionSnapshotService.cs`
- Modify: `Clockwork/ClockworkConfigurator.cs`

Goal: a service that, on demand, walks the vanilla `AutomatorRegistry` and produces a `List<PartitionSnapshot>` — POCO data the UI can render without re-touching vanilla types every frame. **Many vanilla automation types are likely `internal`** — this service uses reflection where required and caches the discovered `MethodInfo`/`PropertyInfo` handles per-call.

- [ ] **Step 1: Write `Services/PartitionSnapshot.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace Clockwork.Services;

/// One automation flow. `Automators` are the buildings in the partition;
/// `Wires` are the directed connections between them. Both lists use stable
/// per-game ids so the UI can match them across snapshots.
public sealed class PartitionSnapshot
{
    public List<AutomatorView> Automators { get; } = new();
    public List<WireView> Wires { get; } = new();

    /// True if any transmitter in the partition is currently asserting.
    public bool Asserting;

    /// The first anchor id (per ClockworkRegistry) that's contained in this
    /// partition, or null if none.
    public Guid? AnchorId;
}

public sealed class AutomatorView
{
    public Guid AutomatorId;
    public string EntityName = "";        // vanilla NamedEntity if any, else ""
    public string TemplateName = "";      // e.g. "Floodgate"
    public AutomatorRole Role;            // emitter / receiver / both
    public bool Asserting;                // current transmitter state, if applicable
    public UnityEngine.Vector3 WorldPosition;  // for camera ping
    public string DistrictName = "";      // empty if no district
}

[Flags]
public enum AutomatorRole
{
    None = 0,
    Emitter = 1,    // implements ITransmitter
    Receiver = 2,   // implements IAutomatableNeeder
    Gate = Emitter | Receiver,
}

public sealed class WireView
{
    public Guid FromAutomatorId;
    public Guid ToAutomatorId;
    public bool Asserting;     // current connection state
}
```

- [ ] **Step 2: Write `Services/PartitionSnapshotService.cs`**

This service uses reflection over `Timberborn.Automation.AutomatorRegistry` because the partition data is `internal`. Implementer must locate the actual method names by reading the `Timberborn.Automation.dll` strings (use `strings <dll> | grep AutomatorRegistry`); the method `GetPartitionsSnapshot` exists per the audit. If a different name is found, use that.

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Clockwork.Services;

public sealed class PartitionSnapshotService : ILoadableSingleton
{
    private readonly EventBus _eventBus;
    // Lazily-resolved reflection handles to vanilla AutomatorRegistry.
    private object? _automatorRegistry;
    private MethodInfo? _getPartitionsSnapshotMethod;
    private bool _resolved;

    public PartitionSnapshotService(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Load()
    {
        // Resolve once at game start. If anything fails, we log and the
        // service returns empty snapshots until we fix the reflection.
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies();
            Type? regType = null;
            foreach (var a in asm)
            {
                regType = a.GetType("Timberborn.Automation.AutomatorRegistry");
                if (regType != null) break;
            }
            if (regType == null)
            {
                Debug.LogWarning("[Clockwork] AutomatorRegistry type not found");
                return;
            }
            _getPartitionsSnapshotMethod = regType.GetMethod("GetPartitionsSnapshot",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_getPartitionsSnapshotMethod == null)
            {
                Debug.LogWarning("[Clockwork] AutomatorRegistry.GetPartitionsSnapshot not found");
                return;
            }
            // Resolve the registry instance via Bindito at call time — the
            // ServiceLocator-style lookup pattern can't be used here because
            // the type is internal. Instead we'll grab it via the singleton
            // service when it becomes available; defer to first call.
            _resolved = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Clockwork] reflection setup failed: {ex.Message}");
        }
    }

    /// Returns one snapshot per partition currently in the world.
    /// May return an empty list if reflection didn't resolve.
    public List<PartitionSnapshot> GetSnapshots()
    {
        var result = new List<PartitionSnapshot>();
        if (!_resolved || _getPartitionsSnapshotMethod == null) return result;

        // Lazily acquire the registry instance via a Bindito-bound
        // resolver. Implementer will discover the right way to obtain
        // the singleton at first call (likely via _eventBus or a direct
        // Bindito lookup; check Graphs/UI patterns for how it pulls
        // game services).
        // …
        // For now, return empty until that lookup is wired up.
        return result;
    }
}
```

- [ ] **Step 3: Resolve the `AutomatorRegistry` instance**

The implementer needs to figure out how the mod can get a reference to the live `AutomatorRegistry` instance. Two approaches to try, in order:

1. **Bindito injection.** `AutomatorRegistry` may be bindable in the Game context. Add a constructor parameter typed as `object` with a Bindito `[Inject]` of the type by reflection. If Bindito refuses (because the type is internal), fall through.
2. **`SingletonRepository` reflection.** `Timberborn.SingletonSystem.SingletonRepository` (the registry behind `ILoadableSingleton`) exposes `GetSingletons<T>()` and similar. Use reflection to call its non-generic `GetSingleton(Type)` with the resolved `AutomatorRegistry` type.

Implement whichever works. Cache the instance on the service.

- [ ] **Step 4: Implement `GetSnapshots` body**

Once the registry instance is resolved, call `GetPartitionsSnapshot` on it. The returned shape — based on the type name — is a snapshot collection of `AutomatorPartition` objects. Each partition exposes its automators and connections (per the audit: `Connect`, `Disconnect`, plus `_partitions` field on registry).

For each partition: enumerate automators, populate `AutomatorView` (using `EntityComponent.EntityId` for the id, the `NamedEntity.EntityName` for the display name when present, the `TemplateInstantiator` name for `TemplateName`, and the `BlockObject.Coordinates` for position). Determine `Role` by checking if the automator implements `ITransmitter` and/or `IAutomatableNeeder`.

For each connection, populate `WireView`.

Compute `Asserting` per automator by reading `ITransmitter.GetSnapshot()` (per audit) or the `BooleanState` property; the partition `Asserting` is OR of all transmitters.

Look up `AnchorId` by checking each automator id against `ClockworkRegistry.All.Keys`; first match wins.

- [ ] **Step 5: Bind in configurator**

Add to `ClockworkConfigurator.Configure()`:

```csharp
Bind<PartitionSnapshotService>().AsSingleton();
```

- [ ] **Step 6: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

Expected: 0 errors.

- [ ] **Step 7: Smoke-test the snapshot via a temporary debug log**

Add a one-off `Debug.Log` inside `GetSnapshots` that prints the count. Trigger it manually by calling `GetSnapshots` from a tick callback (temporary — remove later). Build, run a game with a few automation buildings wired up, then check Player.log for `[Clockwork] N partitions`.

If the count matches what you wired in-world, the service works. Remove the debug log + tick callback before commit.

- [ ] **Step 8: Commit**

```bash
git add Clockwork/Services/ Clockwork/ClockworkConfigurator.cs
git commit -m "Clockwork: PartitionSnapshotService reads vanilla AutomatorRegistry"
```

---

## Task 4: Drawer panel scaffold (hotkey-toggled, empty content)

**Files:**
- Create: `Clockwork/UI/ClockworkPanel.cs`
- Create: `Clockwork/UI/ClockworkHotkey.cs`
- Create: `Clockwork/UI/ClockworkColors.cs`
- Modify: `Clockwork/ClockworkConfigurator.cs`

Goal: a left-anchored panel that the hotkey `Shift+C` toggles. When open, shows a header label "Clockworks" and an empty body. No data yet.

- [ ] **Step 1: Write `UI/ClockworkColors.cs`**

```csharp
using UnityEngine;

namespace Clockwork.UI;

internal static class ClockworkColors
{
    public static readonly Color HeaderText = new(0.96f, 0.86f, 0.62f);
    public static readonly Color BodyText   = new(0.92f, 0.86f, 0.72f);
    public static readonly Color SignalOn   = new(0.45f, 0.85f, 0.35f);
    public static readonly Color SignalOff  = new(0.40f, 0.40f, 0.42f);
    public static readonly Color RowBg      = new(0.10f, 0.09f, 0.08f, 0.85f);
    public static readonly Color PanelBg    = new(0.08f, 0.07f, 0.06f, 0.94f);
}
```

- [ ] **Step 2: Write `UI/ClockworkPanel.cs`**

```csharp
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

public sealed class ClockworkPanel : ILoadableSingleton
{
    private readonly UILayout _uiLayout;
    private VisualElement? _root;
    private bool _open;

    public ClockworkPanel(UILayout uiLayout)
    {
        _uiLayout = uiLayout;
    }

    public void Load()
    {
        _root = Build();
        _root.style.display = DisplayStyle.None;
        // Add to the layout. The exact AddPanel signature in this
        // version of Timberborn takes (VisualElement, panelOrder int).
        // Implementer: confirm signature against the publicized
        // UILayout.dll if needed and adjust.
        _uiLayout.AddPanel(_root, 0);
    }

    public void Toggle()
    {
        if (_root == null) return;
        _open = !_open;
        _root.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private VisualElement Build()
    {
        var root = new VisualElement { name = "clockwork-panel" };
        root.style.position = Position.Absolute;
        root.style.left = 12;
        root.style.top = 80;     // sit below the top bar
        root.style.bottom = 12;  // stretch to near the bottom
        root.style.width = 320;
        root.style.backgroundColor = new StyleColor(ClockworkColors.PanelBg);
        root.style.borderTopLeftRadius = 4;
        root.style.borderTopRightRadius = 4;
        root.style.borderBottomLeftRadius = 4;
        root.style.borderBottomRightRadius = 4;
        root.style.paddingTop = 8;
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingBottom = 8;
        root.style.flexDirection = FlexDirection.Column;

        var header = new Label("Clockworks");
        header.style.color = new StyleColor(ClockworkColors.HeaderText);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.fontSize = 14;
        header.style.marginBottom = 8;
        root.Add(header);

        var body = new ScrollView { name = "clockwork-body" };
        body.style.flexGrow = 1;
        root.Add(body);

        return root;
    }
}
```

- [ ] **Step 3: Write `UI/ClockworkHotkey.cs` (mirrors `Graphs/UI/GraphsHotkey.cs`)**

```csharp
using Timberborn.InputSystem;
using Timberborn.TickSystem;
using UnityEngine.InputSystem;

namespace Clockwork.UI;

public sealed class ClockworkHotkey : ITickableSingleton
{
    private readonly ClockworkPanel _panel;
    private bool _prevPressed;

    public ClockworkHotkey(ClockworkPanel panel)
    {
        _panel = panel;
    }

    public void Tick()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool shift = keyboard.shiftKey.isPressed;
        bool c = keyboard.cKey.isPressed;
        bool pressed = shift && c;
        if (pressed && !_prevPressed) _panel.Toggle();
        _prevPressed = pressed;
    }
}
```

- [ ] **Step 4: Bind in configurator**

```csharp
using Bindito.Core;
using Clockwork.Data;
using Clockwork.Services;
using Clockwork.UI;

namespace Clockwork;

[Context("Game")]
public class ClockworkConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<ClockworkRegistry>().AsSingleton();
        Bind<PartitionSnapshotService>().AsSingleton();
        Bind<ClockworkPanel>().AsSingleton();
        Bind<ClockworkHotkey>().AsSingleton();
    }
}
```

- [ ] **Step 5: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

If the `UILayout.AddPanel` signature errors (parameter count/types differ), check the publicized `Timberborn.UILayoutSystem.dll` (or use `strings + grep AddPanel`) for the correct overloads, and adjust the call.

- [ ] **Step 6: Smoke-test in-game**

Launch a game, press **Shift+C**. The drawer should appear on the left with header "Clockworks" and an empty scroll body. Press Shift+C again to hide. Confirm the game stays interactive while the drawer is open (camera moves, you can click buildings).

- [ ] **Step 7: Commit**

```bash
git add Clockwork/UI/ Clockwork/ClockworkConfigurator.cs
git commit -m "Clockwork: drawer scaffold + Shift+C hotkey (empty body)"
```

---

## Task 5: Partition list rendering (Level 1)

**Files:**
- Create: `Clockwork/UI/PartitionRow.cs`
- Modify: `Clockwork/UI/ClockworkPanel.cs`

Goal: each open of the drawer renders one row per partition, showing the signal dot + display name. No expand yet — just a flat list. Display name = `ClockworkRegistry`-mapped name if any anchor in the partition has one, else `"Unnamed flow ({wireCount} wires)"`.

- [ ] **Step 1: Write `UI/PartitionRow.cs`**

```csharp
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class PartitionRow
{
    public static VisualElement Build(PartitionSnapshot partition, string displayName)
    {
        var row = new VisualElement { name = "clockwork-partition-row" };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 24;
        row.style.marginBottom = 2;
        row.style.paddingLeft = 4;
        row.style.paddingRight = 4;
        row.style.backgroundColor = new StyleColor(ClockworkColors.RowBg);

        var dot = new VisualElement { name = "signal-dot" };
        dot.style.width = 8;
        dot.style.height = 8;
        dot.style.marginRight = 6;
        dot.style.borderTopLeftRadius = 4;
        dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4;
        dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            partition.Asserting ? ClockworkColors.SignalOn : ClockworkColors.SignalOff);
        row.Add(dot);

        var label = new Label(displayName);
        label.style.color = new StyleColor(ClockworkColors.BodyText);
        label.style.flexGrow = 1;
        label.style.fontSize = 12;
        row.Add(label);

        return row;
    }
}
```

- [ ] **Step 2: Modify `ClockworkPanel.cs` to populate the body each toggle/refresh**

Add a private field for the snapshot service plus a `RefreshBody` method that clears and rebuilds the body. Call it from `Toggle` (when opening) and from a registry-changed callback. Inject `PartitionSnapshotService` and `ClockworkRegistry`.

```csharp
using System.Linq;
using Clockwork.Data;
using Clockwork.Services;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

public sealed class ClockworkPanel : ILoadableSingleton
{
    private readonly UILayout _uiLayout;
    private readonly PartitionSnapshotService _snapshots;
    private readonly ClockworkRegistry _registry;
    private VisualElement? _root;
    private ScrollView? _body;
    private bool _open;

    public ClockworkPanel(UILayout uiLayout, PartitionSnapshotService snapshots, ClockworkRegistry registry)
    {
        _uiLayout = uiLayout;
        _snapshots = snapshots;
        _registry = registry;
    }

    public void Load()
    {
        _root = Build();
        _root.style.display = DisplayStyle.None;
        _uiLayout.AddPanel(_root, 0);
        _registry.Changed += () => { if (_open) RefreshBody(); };
    }

    public void Toggle()
    {
        if (_root == null) return;
        _open = !_open;
        _root.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
        if (_open) RefreshBody();
    }

    private void RefreshBody()
    {
        if (_body == null) return;
        _body.Clear();
        var snapshots = _snapshots.GetSnapshots();
        // Sort: named first (alphabetical), then unnamed by automator count desc.
        var named = new System.Collections.Generic.List<(PartitionSnapshot p, string name)>();
        var unnamed = new System.Collections.Generic.List<PartitionSnapshot>();
        foreach (var p in snapshots)
        {
            if (p.AnchorId is { } a && _registry.TryGet(a, out var name))
                named.Add((p, name));
            else
                unnamed.Add(p);
        }
        named.Sort((x, y) => System.StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        unnamed.Sort((x, y) => y.Automators.Count.CompareTo(x.Automators.Count));

        foreach (var (p, name) in named)
            _body.Add(PartitionRow.Build(p, name));
        foreach (var p in unnamed)
            _body.Add(PartitionRow.Build(p, $"(Unnamed flow: {p.Wires.Count} wires)"));
    }

    private VisualElement Build()
    {
        var root = new VisualElement { name = "clockwork-panel" };
        // ... (keep the chrome from Task 4) ...
        root.style.position = Position.Absolute;
        root.style.left = 12;
        root.style.top = 80;
        root.style.bottom = 12;
        root.style.width = 320;
        root.style.backgroundColor = new StyleColor(ClockworkColors.PanelBg);
        root.style.paddingTop = 8;
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingBottom = 8;
        root.style.flexDirection = FlexDirection.Column;

        var header = new Label("Clockworks");
        header.style.color = new StyleColor(ClockworkColors.HeaderText);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.fontSize = 14;
        header.style.marginBottom = 8;
        root.Add(header);

        _body = new ScrollView { name = "clockwork-body" };
        _body.style.flexGrow = 1;
        root.Add(_body);

        return root;
    }
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

- [ ] **Step 4: Smoke-test in-game**

Build a quick scenario in a fresh game: place one depth sensor and one floodgate, wire them together. Open the drawer with Shift+C. You should see one row "(Unnamed flow: 1 wires)". Add a second sensor → floodgate connection. Reopen → two rows.

- [ ] **Step 5: Commit**

```bash
git add Clockwork/UI/PartitionRow.cs Clockwork/UI/ClockworkPanel.cs
git commit -m "Clockwork: render partitions as Level-1 rows (signal dot + name)"
```

---

## Task 6: Inline rename via gear menu

**Files:**
- Modify: `Clockwork/UI/PartitionRow.cs`

Goal: each row gets a `[⚙]` gear button on the right that toggles into a small text-input mode. Typing + Enter calls `ClockworkRegistry.Set(anchorId, name)`. If the partition has no anchor yet, set the anchor on the first call (pick the lowest-AutomatorId automator in the partition).

- [ ] **Step 1: Update `PartitionRow.Build` to take an `Action<string>` rename callback and add the gear button + inline input**

```csharp
using System;
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class PartitionRow
{
    public static VisualElement Build(
        PartitionSnapshot partition,
        string displayName,
        Action<string> onRename)
    {
        var row = new VisualElement { name = "clockwork-partition-row" };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 24;
        row.style.marginBottom = 2;
        row.style.paddingLeft = 4;
        row.style.paddingRight = 4;
        row.style.backgroundColor = new StyleColor(ClockworkColors.RowBg);

        var dot = new VisualElement { name = "signal-dot" };
        dot.style.width = 8;
        dot.style.height = 8;
        dot.style.marginRight = 6;
        dot.style.borderTopLeftRadius = 4;
        dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4;
        dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            partition.Asserting ? ClockworkColors.SignalOn : ClockworkColors.SignalOff);
        row.Add(dot);

        // Click-to-edit label.
        var label = new Label(displayName);
        label.style.color = new StyleColor(ClockworkColors.BodyText);
        label.style.flexGrow = 1;
        label.style.fontSize = 12;
        row.Add(label);

        // Gear button (right side).
        var gear = new Button(() => StartEdit(label, row, displayName, onRename))
            { text = "⚙" };
        gear.style.width = 24;
        gear.style.height = 20;
        gear.style.marginLeft = 4;
        gear.style.fontSize = 12;
        gear.style.backgroundColor = new StyleColor(new Color(0.16f, 0.14f, 0.12f));
        gear.style.color = new StyleColor(ClockworkColors.BodyText);
        row.Add(gear);

        return row;
    }

    private static void StartEdit(
        Label label, VisualElement row, string current, Action<string> onRename)
    {
        var field = new TextField { value = current };
        field.style.flexGrow = 1;
        field.style.fontSize = 12;

        // Replace the label visually until the field commits/cancels.
        int idx = row.IndexOf(label);
        row.Insert(idx, field);
        label.style.display = DisplayStyle.None;
        field.Focus();

        void Commit()
        {
            onRename(field.value);
            label.text = string.IsNullOrWhiteSpace(field.value)
                ? label.text
                : field.value;
            field.RemoveFromHierarchy();
            label.style.display = DisplayStyle.Flex;
        }

        field.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return) Commit();
            else if (e.keyCode == KeyCode.Escape)
            {
                field.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;
            }
        });
        field.RegisterCallback<FocusOutEvent>(_ => Commit());
    }
}
```

- [ ] **Step 2: Update `ClockworkPanel.RefreshBody` to pass the rename callback**

Replace each `PartitionRow.Build(p, displayName)` call site with a version that supplies a callback:

```csharp
foreach (var (p, name) in named)
{
    var anchor = p.AnchorId!.Value;
    _body.Add(PartitionRow.Build(p, name, newName =>
    {
        if (string.IsNullOrWhiteSpace(newName)) _registry.Remove(anchor);
        else _registry.Set(anchor, newName);
    }));
}
foreach (var p in unnamed)
{
    _body.Add(PartitionRow.Build(p, $"(Unnamed flow: {p.Wires.Count} wires)", newName =>
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        // Pick anchor: lowest-Guid automator in the partition.
        var anchor = p.Automators.Count == 0
            ? System.Guid.Empty
            : p.Automators
                .OrderBy(a => a.AutomatorId)
                .First().AutomatorId;
        if (anchor == System.Guid.Empty) return;
        _registry.Set(anchor, newName);
    }));
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

- [ ] **Step 4: Smoke-test**

Open the drawer with two unnamed flows. Click the gear on one, type "DroughtCycle", hit Enter. The row label updates. Save the game, reload — the name persists. Click gear → clear → Enter clears the name (back to "Unnamed flow…").

- [ ] **Step 5: Commit**

```bash
git add Clockwork/UI/PartitionRow.cs Clockwork/UI/ClockworkPanel.cs
git commit -m "Clockwork: rename partitions via gear button + inline text field"
```

---

## Task 7: Tree-by-emitter expansion (Level 2)

**Files:**
- Create: `Clockwork/UI/PartitionExpandedView.cs`
- Create: `Clockwork/UI/EmitterRow.cs`
- Create: `Clockwork/UI/WireRow.cs`
- Modify: `Clockwork/UI/PartitionRow.cs`
- Modify: `Clockwork/UI/ClockworkPanel.cs`

Goal: clicking the `▸` chevron at the start of a partition row expands it to show its emitters with their downstream wires (the tree from the spec).

- [ ] **Step 1: Write `UI/WireRow.cs`**

```csharp
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class WireRow
{
    public static VisualElement Build(WireView wire, AutomatorView target)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginLeft = 32;          // indent under the emitter
        row.style.height = 20;

        var arrow = new Label("↳ ");
        arrow.style.color = new StyleColor(ClockworkColors.BodyText);
        arrow.style.fontSize = 11;
        row.Add(arrow);

        var label = new Label($"{target.TemplateName}, {target.DistrictName}");
        label.style.color = new StyleColor(
            wire.Asserting ? ClockworkColors.SignalOn : ClockworkColors.BodyText);
        label.style.flexGrow = 1;
        label.style.fontSize = 11;
        row.Add(label);

        return row;
    }
}
```

- [ ] **Step 2: Write `UI/EmitterRow.cs`**

```csharp
using System.Collections.Generic;
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class EmitterRow
{
    public static VisualElement Build(
        AutomatorView emitter,
        IEnumerable<(WireView Wire, AutomatorView Target)> downstream)
    {
        var container = new VisualElement();
        container.style.marginLeft = 16;
        container.style.flexDirection = FlexDirection.Column;

        var head = new VisualElement();
        head.style.flexDirection = FlexDirection.Row;
        head.style.alignItems = Align.Center;
        head.style.height = 22;

        var dot = new VisualElement();
        dot.style.width = 8; dot.style.height = 8; dot.style.marginRight = 6;
        dot.style.borderTopLeftRadius = 4; dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4; dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            emitter.Asserting ? ClockworkColors.SignalOn : ClockworkColors.SignalOff);
        head.Add(dot);

        var name = new Label(string.IsNullOrEmpty(emitter.EntityName)
            ? emitter.TemplateName
            : emitter.EntityName);
        name.style.color = new StyleColor(ClockworkColors.BodyText);
        name.style.fontSize = 11;
        name.style.flexGrow = 1;
        head.Add(name);

        container.Add(head);

        foreach (var (wire, target) in downstream)
            container.Add(WireRow.Build(wire, target));

        return container;
    }
}
```

- [ ] **Step 3: Write `UI/PartitionExpandedView.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Clockwork.Services;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class PartitionExpandedView
{
    public static VisualElement Build(PartitionSnapshot partition)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.marginBottom = 8;

        // Index automators by id so wire lookups are O(1).
        var byId = partition.Automators.ToDictionary(a => a.AutomatorId);

        // For each emitter (anything with the Emitter role), gather its
        // outgoing wires.
        foreach (var emitter in partition.Automators
                     .Where(a => (a.Role & AutomatorRole.Emitter) != 0))
        {
            var outgoing = new List<(WireView, AutomatorView)>();
            foreach (var wire in partition.Wires)
            {
                if (wire.FromAutomatorId != emitter.AutomatorId) continue;
                if (!byId.TryGetValue(wire.ToAutomatorId, out var target)) continue;
                outgoing.Add((wire, target));
            }
            container.Add(EmitterRow.Build(emitter, outgoing));
        }

        return container;
    }
}
```

- [ ] **Step 4: Update `PartitionRow` to add the chevron and expose an `onToggleExpand` callback**

Modify the row builder to add a `▸` button at the very start; clicking it calls a passed-in `Action` and updates the chevron text to `▾` when expanded. The actual expanded-view container is owned by the panel.

```csharp
public static VisualElement Build(
    PartitionSnapshot partition,
    string displayName,
    System.Action<string> onRename,
    System.Action onToggleExpand,
    bool expanded)
{
    var row = new VisualElement { name = "clockwork-partition-row" };
    row.style.flexDirection = FlexDirection.Row;
    row.style.alignItems = Align.Center;
    row.style.height = 24;
    row.style.marginBottom = 2;
    row.style.paddingLeft = 4;
    row.style.paddingRight = 4;
    row.style.backgroundColor = new StyleColor(ClockworkColors.RowBg);

    var chevron = new Button(() => onToggleExpand()) { text = expanded ? "▾" : "▸" };
    chevron.style.width = 18;
    chevron.style.height = 20;
    chevron.style.marginRight = 4;
    chevron.style.fontSize = 11;
    chevron.style.backgroundColor = new StyleColor(new Color(0.16f, 0.14f, 0.12f));
    chevron.style.color = new StyleColor(ClockworkColors.BodyText);
    row.Add(chevron);

    // (rest of the row body — dot, label, gear — unchanged from Task 6)
    // ...
    return row;
}
```

- [ ] **Step 5: Update `ClockworkPanel` to track expanded partitions and render the tree**

Add a `HashSet<Guid> _expanded` field (key: anchorId or, for unnamed, a synthetic key like the lowest-AutomatorId in the partition). In `RefreshBody`, after each `PartitionRow.Build`, if the partition is in `_expanded`, append `PartitionExpandedView.Build(p)`. The toggle callback adds/removes the key and calls `RefreshBody()`.

```csharp
private readonly System.Collections.Generic.HashSet<System.Guid> _expanded = new();

// inside RefreshBody, replacing the existing _body.Add calls:
foreach (var (p, name) in named)
{
    var anchor = p.AnchorId!.Value;
    var partitionKey = anchor;
    bool expanded = _expanded.Contains(partitionKey);
    _body.Add(PartitionRow.Build(p, name,
        onRename: newName => {
            if (string.IsNullOrWhiteSpace(newName)) _registry.Remove(anchor);
            else _registry.Set(anchor, newName);
        },
        onToggleExpand: () => {
            if (!_expanded.Add(partitionKey)) _expanded.Remove(partitionKey);
            RefreshBody();
        },
        expanded: expanded));
    if (expanded) _body.Add(PartitionExpandedView.Build(p));
}

foreach (var p in unnamed)
{
    var partitionKey = p.Automators.Count == 0
        ? System.Guid.Empty
        : p.Automators.OrderBy(a => a.AutomatorId).First().AutomatorId;
    bool expanded = _expanded.Contains(partitionKey);
    _body.Add(PartitionRow.Build(p, $"(Unnamed flow: {p.Wires.Count} wires)",
        onRename: newName => {
            if (string.IsNullOrWhiteSpace(newName)) return;
            _registry.Set(partitionKey, newName);
        },
        onToggleExpand: () => {
            if (!_expanded.Add(partitionKey)) _expanded.Remove(partitionKey);
            RefreshBody();
        },
        expanded: expanded));
    if (expanded) _body.Add(PartitionExpandedView.Build(p));
}
```

- [ ] **Step 6: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

- [ ] **Step 7: Smoke-test**

Open the drawer with one named flow that has 2 sensors → 1 gate → 2 floodgates wired up. Click the chevron. The row expands to show the emitters and their outgoing wires per the tree spec. Click again to collapse.

- [ ] **Step 8: Commit**

```bash
git add Clockwork/UI/
git commit -m "Clockwork: tree-by-emitter expansion (Level 2)"
```

---

## Task 8: Live signal updates

**Files:**
- Modify: `Clockwork/UI/ClockworkPanel.cs`

Goal: while the drawer is open, signal dots and wire colors update each in-game tick (or on a small throttled cadence) without rebuilding the entire tree on every frame.

- [ ] **Step 1: Make `ClockworkPanel` itself an `ITickableSingleton` and add a refresh-only-when-open guard**

```csharp
using Timberborn.TickSystem;

public sealed class ClockworkPanel : ILoadableSingleton, ITickableSingleton
{
    // ... existing fields ...
    private float _refreshAccumulator;
    private const float RefreshIntervalSec = 0.25f;

    public void Tick()
    {
        if (!_open) return;
        _refreshAccumulator += UnityEngine.Time.unscaledDeltaTime;
        if (_refreshAccumulator < RefreshIntervalSec) return;
        _refreshAccumulator = 0;
        RefreshBody();
    }
}
```

(The cheaper alternative — patching only the dot colors in place — is a future optimization; for v1 a 4Hz full rebuild is fine.)

- [ ] **Step 2: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

- [ ] **Step 3: Smoke-test**

Open the drawer on a flow whose sensor toggles every few seconds (e.g. a Timer wired to a Floodgate). The signal dot on the partition row + the emitter row should change color in time with the sensor's actual state.

- [ ] **Step 4: Commit**

```bash
git add Clockwork/UI/ClockworkPanel.cs
git commit -m "Clockwork: refresh drawer 4Hz while open for live signal dots"
```

---

## Task 9: Ping button (focus camera on a building)

**Files:**
- Create: `Clockwork/Services/BuildingPing.cs`
- Modify: `Clockwork/UI/EmitterRow.cs`
- Modify: `Clockwork/UI/WireRow.cs`
- Modify: `Clockwork/ClockworkConfigurator.cs`

Goal: every emitter row and every wire row gets a `[→]` button that pans the camera to that building.

- [ ] **Step 1: Write `Services/BuildingPing.cs`**

```csharp
using Timberborn.CameraSystem;
using UnityEngine;

namespace Clockwork.Services;

/// Centers the camera on a world position. The camera service is the same
/// one Timberborn uses for follow-targets.
public sealed class BuildingPing
{
    private readonly CameraService _camera;

    public BuildingPing(CameraService camera)
    {
        _camera = camera;
    }

    public void Focus(Vector3 worldPosition)
    {
        _camera.Target = worldPosition;
    }
}
```

If `CameraService.Target` isn't a public setter, locate the right method on `CameraService` (the audit identified `set_Target`, but the publicly-callable form may be a method like `MoveTo` or `JumpTo`; check `strings Timberborn.CameraSystem.dll | grep -E "(Move|Jump|Focus|Target)"` and adjust).

- [ ] **Step 2: Add ping buttons in `EmitterRow.Build` and `WireRow.Build`**

In each row's chrome, append a small button labeled `→` whose click calls `pingCallback(target.WorldPosition)`. The callback comes through the row builder's parameter list.

For `EmitterRow.Build`, change the signature:

```csharp
public static VisualElement Build(
    AutomatorView emitter,
    IEnumerable<(WireView Wire, AutomatorView Target)> downstream,
    System.Action<UnityEngine.Vector3> ping)
{
    // … head as before …
    var pingBtn = new Button(() => ping(emitter.WorldPosition)) { text = "→" };
    pingBtn.style.width = 22;
    pingBtn.style.height = 18;
    pingBtn.style.marginLeft = 4;
    pingBtn.style.fontSize = 11;
    pingBtn.style.backgroundColor = new StyleColor(new Color(0.16f, 0.14f, 0.12f));
    pingBtn.style.color = new StyleColor(ClockworkColors.BodyText);
    head.Add(pingBtn);
    container.Add(head);

    foreach (var (wire, target) in downstream)
        container.Add(WireRow.Build(wire, target, ping));

    return container;
}
```

Same idea for `WireRow.Build`.

- [ ] **Step 3: Plumb the `ping` callback through `ClockworkPanel.RefreshBody` to `PartitionExpandedView.Build`**

Add a `BuildingPing` constructor parameter on `ClockworkPanel`. When building the expanded view, pass `pos => _ping.Focus(pos)` down.

- [ ] **Step 4: Bind the service**

In `ClockworkConfigurator.Configure()`:

```csharp
Bind<BuildingPing>().AsSingleton();
```

- [ ] **Step 5: Build + smoke test**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

In-game, expand a flow and click the `→` button next to a wire's target. Camera should pan to that building.

- [ ] **Step 6: Commit**

```bash
git add Clockwork/Services/BuildingPing.cs Clockwork/UI/ Clockwork/ClockworkConfigurator.cs
git commit -m "Clockwork: ping buttons focus camera on emitter/receiver"
```

---

## Task 10: Add wire picker

**Files:**
- Create: `Clockwork/UI/WirePicker.cs`
- Modify: `Clockwork/UI/PartitionExpandedView.cs`
- Modify: `Clockwork/UI/ClockworkPanel.cs`

Goal: at the bottom of each expanded partition view, a `[+ wire]` button opens an overlay that lists all candidate receivers (grouped by template name), search-filterable; selecting one calls vanilla `AutomatorConnection.Connect` (via `AutomatorRegistry`) and refreshes the drawer.

- [ ] **Step 1: Add a `Connect` API on `PartitionSnapshotService`**

The service already has the `AutomatorRegistry` instance reflectively. Expose a `Connect(Guid fromId, Guid toId)` method that calls the vanilla `Connect` method on `AutomatorRegistry` (signature to be discovered via reflection). On any reflection failure, log and return false.

```csharp
public bool Connect(System.Guid fromId, System.Guid toId)
{
    // implementer: discover the connect method by name (likely on
    // AutomatorRegistry; per audit, "AddAutomator" / "Connect" /
    // "ConnectToOutput" / "DisconnectFromOutput" are present). Use the
    // matching method name + parameter shape, invoke via reflection,
    // catch and log any failure, return false on failure.
    return false;
}
public bool Disconnect(System.Guid fromId, System.Guid toId) { /* same idea */ return false; }
```

- [ ] **Step 2: Write `UI/WirePicker.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class WirePicker
{
    public static VisualElement Build(
        IReadOnlyList<AutomatorView> candidates,
        Action<AutomatorView> onPick,
        Action onCancel)
    {
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0; overlay.style.right = 0;
        overlay.style.top = 0; overlay.style.bottom = 0;
        overlay.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.6f));
        overlay.style.flexDirection = FlexDirection.Column;
        overlay.style.paddingTop = 8;

        var search = new TextField { value = "" };
        search.style.marginLeft = 8;
        search.style.marginRight = 8;
        search.style.marginBottom = 8;
        overlay.Add(search);

        var list = new ScrollView();
        list.style.flexGrow = 1;
        overlay.Add(list);

        void Render(string filter)
        {
            list.Clear();
            var grouped = candidates
                .Where(c => string.IsNullOrEmpty(filter) ||
                            c.TemplateName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            c.EntityName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(c => c.TemplateName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var grp in grouped)
            {
                var groupHeader = new Label(grp.Key);
                groupHeader.style.color = new StyleColor(ClockworkColors.HeaderText);
                groupHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                groupHeader.style.marginTop = 4;
                groupHeader.style.marginLeft = 8;
                list.Add(groupHeader);
                foreach (var c in grp)
                {
                    var captured = c;
                    var btn = new Button(() => onPick(captured));
                    btn.text = $"{captured.DistrictName} — {captured.EntityName}";
                    btn.style.alignSelf = Align.Stretch;
                    btn.style.marginLeft = 16;
                    btn.style.marginRight = 8;
                    btn.style.height = 22;
                    btn.style.fontSize = 11;
                    list.Add(btn);
                }
            }
        }
        search.RegisterValueChangedCallback(e => Render(e.newValue));
        Render("");

        overlay.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Escape) onCancel();
        });

        return overlay;
    }
}
```

- [ ] **Step 3: Wire `[+ wire]` and the picker into the panel**

In `PartitionExpandedView.Build`, append a `[+ wire]` button at the bottom that calls a new `Action<AutomatorView>` parameter, `requestAddWire(emitterToWireFrom)`. Pass the parameter through from the panel.

In `ClockworkPanel`, when `requestAddWire(emitter)` fires:
1. Build a candidate list = all `AutomatorView`s in the world that implement `Receiver` (Role flag set), excluding the emitter and excluding any that already have an incoming wire from this emitter.
2. Build the picker overlay via `WirePicker.Build`, mount it as a child of the drawer root.
3. On pick: call `_snapshots.Connect(emitter.AutomatorId, picked.AutomatorId)`, remove the overlay, `RefreshBody()`.
4. On cancel: just remove the overlay.

- [ ] **Step 4: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

- [ ] **Step 5: Smoke-test**

Place a fresh sensor + a fresh floodgate not yet wired to anything. Open the drawer; the sensor should appear in its own unnamed partition. Expand it, click `[+ wire]`, type "Floodgate", pick the floodgate. The wire should appear under the sensor; in-world, the sensor should now drive the floodgate (verify by triggering the sensor and seeing the gate move).

- [ ] **Step 6: Commit**

```bash
git add Clockwork/UI/WirePicker.cs Clockwork/UI/PartitionExpandedView.cs \
        Clockwork/UI/ClockworkPanel.cs Clockwork/Services/PartitionSnapshotService.cs
git commit -m "Clockwork: add-wire picker (search + grouped receivers)"
```

---

## Task 11: Remove wire (× button)

**Files:**
- Modify: `Clockwork/UI/WireRow.cs`
- Modify: `Clockwork/UI/EmitterRow.cs`
- Modify: `Clockwork/UI/PartitionExpandedView.cs`
- Modify: `Clockwork/UI/ClockworkPanel.cs`

Goal: each wire row gets a `[×]` button that calls vanilla `Disconnect` and refreshes.

- [ ] **Step 1: Add an `Action<WireView>` callback to `WireRow.Build` and append a `[×]` button on the right**

```csharp
public static VisualElement Build(
    WireView wire,
    AutomatorView target,
    Action<UnityEngine.Vector3> ping,
    Action<WireView> remove)
{
    // … existing arrow + label …

    var pingBtn = new Button(() => ping(target.WorldPosition)) { text = "→" };
    // … style …
    row.Add(pingBtn);

    var removeBtn = new Button(() => remove(wire)) { text = "×" };
    removeBtn.style.width = 22; removeBtn.style.height = 18; removeBtn.style.marginLeft = 4;
    removeBtn.style.fontSize = 11;
    removeBtn.style.backgroundColor = new StyleColor(new Color(0.32f, 0.10f, 0.10f));
    removeBtn.style.color = new StyleColor(ClockworkColors.BodyText);
    row.Add(removeBtn);

    return row;
}
```

- [ ] **Step 2: Plumb the remove callback through `EmitterRow.Build` and `PartitionExpandedView.Build`**

Add a `Action<WireView> removeWire` parameter on both; pass it from the panel. The panel implements:

```csharp
void RemoveWire(WireView w)
{
    _snapshots.Disconnect(w.FromAutomatorId, w.ToAutomatorId);
    RefreshBody();
}
```

- [ ] **Step 3: Build**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

- [ ] **Step 4: Smoke-test**

Open a flow with a wire visible. Click `[×]` on the wire. It should disappear from the drawer, and in-world the receiver should no longer follow that emitter.

- [ ] **Step 5: Commit**

```bash
git add Clockwork/UI/
git commit -m "Clockwork: remove-wire button (vanilla Disconnect)"
```

---

## Task 12: Leaf naming via blueprint patches

**Files:**
- Create: `Clockwork/Patches/LeafNamePatches.cs`

Goal: floodgates, sluices, valves, dynamite, and pumps gain the vanilla `Timberborn.EntityNaming.NamedEntity` component so the player can name them through the same vanilla entity-panel UI used for sensors. No mod-side dictionary.

- [ ] **Step 1: Identify the receiver template names**

Receiver templates in vanilla (verified during audit): `Floodgate`, `Sluice`, `Valve`, `Detonator` (the dynamite arm), `Lever`. (Pumps in the user's notes are `MechanicalWaterPump` or similar — implementer: search the StreamingAssets blueprint dir for templates that have an `Automatable` or `IAutomatableNeeder` component but no `NamedEntity` component, and add them all.)

```bash
find "/Users/matthewszatmary/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/StreamingAssets" \
     -name "*.blueprint.json" -print0 \
   | xargs -0 grep -l '"NamedEntity"' >/tmp/has-named.txt
find "/Users/matthewszatmary/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/StreamingAssets" \
     -name "*.blueprint.json" -print0 \
   | xargs -0 grep -lE '"Automatable"|"IAutomatableNeeder"|"Floodgate"|"Sluice"|"Valve"|"Lever"|"Dynamite"' >/tmp/has-auto.txt
comm -23 <(sort /tmp/has-auto.txt) <(sort /tmp/has-named.txt)
```

The output lists candidate templates that have automation but lack `NamedEntity`. Use those exact template ids in the next step.

- [ ] **Step 2: Write `Patches/LeafNamePatches.cs`**

The cleanest way to add a component to a vanilla blueprint at runtime is via Timberborn's blueprint append (the `Blueprints#append` pattern noted in the project memory). Implementer: write blueprint append JSON files in `Clockwork/Buildings/<TemplateName>/` per the existing PowerZipline pattern, NOT a Harmony patch.

Concretely: for each receiver template `T`, create
`Clockwork/Buildings/<T>/<T>.Common.blueprint.json` containing:

```json
{
    "Components#append": [
        { "Name": "NamedEntity" }
    ]
}
```

…and reference it from a `manifest.json`-level `Buildings` directory if needed (check PowerZipline's structure under `PowerZipline/Buildings/` for the exact convention).

- [ ] **Step 3: Build + redeploy**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

The build script must include `Buildings/` content into the deploy. Verify the deployed mod has `Buildings/<TemplateName>/<filename>.json` files present at `~/Documents/Timberborn/Mods/Clockwork/Buildings/...`.

- [ ] **Step 4: Smoke-test in-game**

Place a fresh floodgate. Click it. The entity panel should show a name field at the top (the same one districts use). Type a name. The drawer (Task 5+) should display that name in the wire row instead of the type+district fallback.

- [ ] **Step 5: Commit**

```bash
git add Clockwork/Buildings/ Clockwork/Patches/
git commit -m "Clockwork: receivers get vanilla NamedEntity component"
```

---

## Task 13: Topbar/sidebar button (polish)

**Files:**
- Modify: `Clockwork/UI/ClockworkPanel.cs` (or a new small file `Clockwork/UI/ClockworkButton.cs`)

Goal: a clickable button somewhere in the persistent UI that toggles the drawer, so players who don't know the hotkey can still find the feature.

- [ ] **Step 1: Find a Timberborn UI slot to register a button**

`UILayout.AddTopRightButton` exists per the audit. Confirm its signature and use it. If not suitable, add a small floating button at the top-left edge of the screen as a fallback.

- [ ] **Step 2: Implement the button**

```csharp
// Inside ClockworkPanel.Load(), after _uiLayout.AddPanel(_root, 0):
var btn = new UnityEngine.UIElements.Button(Toggle) { text = "⚙ Clockworks" };
btn.style.fontSize = 11;
_uiLayout.AddTopRightButton(btn);  // adjust to actual signature
```

- [ ] **Step 3: Build + smoke-test**

```bash
cd /Users/matthewszatmary/Projects/timbermods && ./build.sh Clockwork
```

In-game, the button should appear in the top-right (or wherever the chosen slot is) and toggle the drawer when clicked.

- [ ] **Step 4: Commit**

```bash
git add Clockwork/UI/ClockworkPanel.cs
git commit -m "Clockwork: top-right toggle button (in addition to Shift+C)"
```

---

## Self-review

**Spec coverage:**
- Vocabulary (Clockwork/Partition/Anchor/Emitter/Leaf/Wire) → reflected in code shapes (`PartitionSnapshot`, `AutomatorView.Role`, `ClockworkRegistry.NamesByAnchor`, leaf rename via `NamedEntity`).
- Persistent state = single `Dictionary<Guid,string>` → Task 2.
- Drawer via `UILayout.AddPanel`, non-modal → Task 4.
- Reuse Timberborn chrome → Task 4 uses neutral chrome; can layer `NineSliceVisualElement`/`.sliced-border` USS on top during polish if visually off (not in Phase 1 scope).
- Tree by emitter → Task 7.
- Add/remove wire → Tasks 10–11.
- Leaf naming via vanilla `NamedEntity` → Task 12.
- Hotkey + button trigger → Tasks 4 + 13.
- Anchor lifecycle (rename, merge, split, demolish) → Task 6 sets the anchor; merge/demolish are runtime-derived from the snapshot (no extra code: a partition with no anchor is just unnamed, and merges naturally show one name). Confirmed consistent with the spec.

**Placeholders/red flags:**
- Two known reflection unknowns are flagged with explicit "implementer: discover…" steps backed by concrete `strings`/`grep` commands: `AutomatorRegistry.Connect/Disconnect/GetPartitionsSnapshot` exact signatures (Task 3 step 3, Task 10 step 1) and `CameraService` focus method (Task 9 step 1). These are not TODOs; they are research steps with the discovery procedure spelled out. Acceptable per "no placeholders" — the discovery is the action, not deferred.
- `UILayout.AddPanel` overload similarly resolved by inspection at Task 4 step 5.
- Task 12's `Buildings/` directory layout deferred to "match PowerZipline's structure" — PowerZipline is in-repo; the implementer can read it directly. Acceptable.

**Type / signature consistency:**
- `AutomatorView`/`WireView`/`PartitionSnapshot` shapes are defined in Task 3 and used unchanged in Tasks 5/7/9/10/11.
- `PartitionRow.Build` parameter list grows across Tasks 5 → 6 → 7; each task explicitly shows the new signature. The implementer reading out of order will see the final form in Task 7.
- `EmitterRow.Build` and `WireRow.Build` follow the same pattern across Tasks 7/9/11. Final shape is in Task 11.
- `ClockworkRegistry.Set/Remove/TryGet` defined in Task 2 used unchanged in Task 6.
