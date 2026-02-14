# Timberborn Mechanical System Notes

## Architecture

| DLL | Responsibility |
|-----|---------------|
| `Timberborn.MechanicalSystem` | Core: graphs, nodes, transputs, power calculations |
| `Timberborn.MechanicalSystemUI` | Visual shaft/gear model updates, traversal |
| `Timberborn.MechanicalSystemHighlighting` | Selection highlighting of connected buildings |
| `Timberborn.MechanicalConnectorSystem` | Visual shaft/gear connectors between buildings |

## MechanicalGraph

```csharp
// Key properties
IEnumerable<MechanicalNode> Nodes { get; }
ReadOnlyList<MechanicalNode> Generators { get; }
ReadOnlyList<MechanicalNode> Batteries { get; }
int PowerSupply { get; set; }
int PowerDemand { get; set; }
int BatteryCharge { get; set; }
int BatteryCapacity { get; set; }
int PowerSurplus { get; }
float PowerEfficiency { get; }
bool Powered { get; }
bool RequiresPower { get; }
bool Valid { get; }
int NumberOfGenerators { get; }
```

## MechanicalNode

- Component on entities with `MechanicalNodeSpec`
- `Graph` property returns current `MechanicalGraph`
- `Transputs` returns `ImmutableArray<Transput>` (check `.IsDefault` before iterating!)

## MechanicalGraphManager (internal)

- `_mechanicalGraphFactory` — has `Join(MechanicalGraph[])` to merge graphs
- `_mechanicalGraphReorganizer` — has `Reorganize(MechanicalGraph)` to split/restructure

## Network Highlighting Flow

1. User clicks building → `SelectableObjectSelectedEvent` on EventBus
2. `MechanicalGraphHighlightService.OnSelectableObjectSelected()` → gets `MechanicalNode`
3. `MechanicalGraphIterator.Iterate(rootNodes, graphNodes)` — DFS through **transputs** (physical connections only)
4. For each found node: `Highlighter.HighlightSecondary(node, color)`

**Key insight**: The iterator walks transputs spatially via `IBlockService`, NOT graph membership. Non-adjacent virtual connections (like zipline bridges) must be injected via postfix patch on `Iterate`.

## MechanicalGraphModelUpdater

Manages 3D shaft/gear model variants. Uses `TraverseToNextIntersection` to walk linear shaft chains. **Crashes with NullReferenceException** if transputs connect non-adjacent blocks.

## ZiplineTower Save/Load

- `ZiplineTower.Save()` serializes `_connectionTargets` under `"ZiplineTower"/"ConnectionTargets"`
- `ZiplineTower.Load()` deserializes into `_loadedConnectionTargets`
- `ZiplineTower.PostInitializeEntity()` restores connections: iterates loaded targets, calls `CanBeConnected` then `Connect`
- `ZiplineTower.OnEnterFinishedState()` calls `ActivateConnection` for each connection target

## Singleton Lifecycle Interfaces

```
ILoadableSingleton          — fires during singleton loading
IPostLoadableSingleton      — fires after ALL entities loaded
IUpdatableSingleton         — fires every game tick
ILateUpdatableSingleton     — fires every game tick (late)
IUnloadableSingleton        — fires on unload
```

## Bindito DI Pattern

```csharp
using Bindito.Core;

[Context("Game")]
public class MyConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<MySingleton>().AsSingleton();
    }
}
```
