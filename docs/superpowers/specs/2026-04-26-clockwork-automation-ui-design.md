# Clockwork: Automation Wiring UI — Design

**Status:** approved 2026-04-26.

## Goal

A Timberborn mod that adds a left-side popout drawer for viewing and editing
automation wiring across an entire colony. The mod adds **no new gameplay
features** — every wire it creates uses vanilla `AutomatorConnection`s and
every signal is evaluated by vanilla's `AutomationRunner`. The only thing
the mod stores is a **name per automation flow** so the player can label and
locate flows ("DroughtCycle", "Pump A", etc.).

The drawer must coexist with active gameplay; you can wire things while the
game runs and watch signals propagate.

## Vocabulary

- **Clockwork** — what the player calls one named automation flow.
- **Partition** — vanilla's `AutomatorPartition`: a connected component of
  the `Automator` graph. The mod treats each partition as a candidate
  Clockwork. Naming a partition turns it into a named Clockwork.
- **Anchor** — the automator that carries a Clockwork's name. One per named
  Clockwork. Survives wire edits as long as the anchor stays in the same
  partition.
- **Emitter** — a building whose automator implements `ITransmitter` (sensor,
  timer, gate output, lever). Has a vanilla `EntityName` that the player can
  set in-game.
- **Leaf** — a building that implements `IAutomatableNeeder` and has no
  outputs (floodgate, sluice, valve, dynamite, pump). Vanilla doesn't
  support naming these; the mod adds an in-mod name field for them.
- **Wire** — a vanilla `AutomatorConnection` from a transmitter to a
  terminal.

## Mirror the game's structure

Vanilla already partitions the automation graph for evaluation. We mirror
that: the drawer's top level is partitions. There is no mod-side concept of
"membership" — a building is in a Clockwork iff its automator is in the
named partition.

## Persistent state added by the mod

```
class ClockworkRegistry  (saved singleton, key "GraphsClockworkRegistry"
                         — to be confirmed; lives in the same mod or its
                         own; see "Mod boundary" below)
{
    Dictionary<Guid, string> NamesByAnchor;   // automatorId → user name
    Dictionary<Guid, string> LeafNames;       // entityId of leaf bldg → name
}
```

That's the entirety of new persistent state. Vanilla owns the wires, the
emitters' names, the partition graph, the building inventory.

## Anchor lifecycle

When the player creates a new Clockwork:
- Choose one automator in the partition as anchor (default: the automator
  the player first added when they typed the name; fallback: lowest
  `AutomatorId` in the partition).
- Store `(anchorId, name)` in `NamesByAnchor`.

Each render frame, for each partition the runtime currently knows about:
- Look at every automator in the partition; if any of their IDs is in
  `NamesByAnchor`, the partition's display name is that mapping.
- If two anchors land in the same partition (because the player wired two
  named Clockworks together), the merged partition takes the alphabetically
  earlier name and the other entry is dropped after a confirmation prompt.

When a partition splits (a wire is removed):
- The half containing the anchor keeps the name. The other half is unnamed.
- No automatic re-anchor.

When the anchor automator is demolished:
- Drop the name. Optional polish: transfer to the lowest-AutomatorId
  remaining automator in the partition, but defer that to a follow-on.

## UI

### The drawer

- Left-side popout, similar pattern to Timberborn's building-toolbar
  flyouts. Stays open while game runs. Toggleable.
- Hotkey: configurable (default to be picked when implementing — start with
  `C`). Plus a sidebar/topbar button.
- Width target: ~300px. Tree-style content; no node-graph view.

### Top-level: partition list

Each row is one partition:

```
[●]  DroughtCycle              [⚙]      ← named partition
[○]  (Unnamed: 3 wires)        [⚙]      ← unnamed partition
[●]  Pump A                    [⚙]
```

- Leading dot: live aggregate state of the partition (any transmitter
  asserting → green; otherwise gray).
- Name: editable on click for named rows. For unnamed rows, the row label
  shows the partition's automator count and a "Name…" affordance.
- Trailing gear: per-Clockwork actions menu — rename, ping a representative
  building, delete name (orphan the partition), merge / unmerge prompts.

The player only sees partitions that contain at least one automator the
mod recognizes. Empty partitions (every automator demolished) disappear.

Sort: named Clockworks first, alphabetical; then unnamed partitions by
size desc.

### Inside a partition: tree by emitter

Expanding a partition row shows its emitters as children, with each
emitter's downstream targets nested under it:

```
▾ ●  DroughtCycle              [⚙]
  ▾ ●  East drought sensor              ← emitter (vanilla EntityName)
       ↳ Floodgate, East #3      [→][×]
       ↳ Floodgate, East #4      [→][×]
       ↳ AND Gate, East #1       [→][×]   ← gate as a downstream
  ▾ ○  Reservoir gauge
       ↳ AND Gate, East #1       [→][×]
  ▾ ●  AND Gate, East #1                   ← gate also appears top-level
       ↳ Pump, Central #1        [→][×]
  [+ wire]
```

- A gate appears twice: once as a child under each of its inputs, and once
  as a top-level emitter (because it itself emits). Same automator
  represented in two roles in the same Clockwork view; that redundancy is
  acceptable and intentional (it makes both upstream and downstream
  navigation natural).
- `[→]` is a ping button: focuses the in-world camera on that building.
- `[×]` removes the wire (vanilla `Disconnect`).
- `[+ wire]` opens the picker.

### Add-wire picker

Drawer-driven (no in-world clicking required for the source-of-truth
flow):

1. Player clicks `[+ wire]` under an emitter.
2. A picker overlay slides over the drawer. Search box at top.
3. Below the search box: candidate receivers grouped by receiver type
   (Floodgate, Sluice, Valve, Dynamite, Pump, …). Each group expandable.
4. Each entry: type, district, mod-set name (if any), auto-index, and a
   ping button.
5. Click an entry → vanilla `AutomatorConnection.Connect` is called, the
   wire appears under the emitter, picker closes.

Filtering rules: only show receivers whose terminals can accept this
emitter (the game enforces transmitter-to-terminal compatibility — we just
let it; if a connection fails, surface the game's error).

### Leaf rename

Floodgates / sluices / etc. don't have vanilla naming. The mod adds:

- An entity component (`ClockworkLeafName`) attached on first rename.
- A small text field in the entity panel via a fragment, similar to the
  district rename UI.
- The drawer's leaf rows display this name when present, falling back to
  `<type> #<auto-index>` otherwise.

Stored in `LeafNames` keyed by `EntityComponent.EntityId`.

### Building identification

Emitter rows: vanilla `EntityName` (player-set), or empty string fallback
to type + auto-index.

Leaf rows: `LeafNames` mod-side name if set, otherwise `<type>, <district>
#<auto-index>`. The `district` portion needs a per-leaf district-of-record;
use the building's home district at the time it was placed (already
tracked by Timberborn).

## Mod boundary

Two reasonable hosts:
- (a) Add Clockwork to the Graphs mod. Risk: ties unrelated features.
- (b) New mod `Clockwork`. Cleaner; preferred.

Default: **(b)**. The two mods can share infrastructure conventions but
ship independently.

## Triggering the drawer

- Hotkey: configurable, default `C`. Open / close toggle.
- Sidebar/topbar button: a small icon. Use a pre-existing Timberborn UI
  slot if one exists; otherwise a small floating left-edge button.
- Drawer state is persisted to PlayerPrefs (open/closed across sessions).

## Things explicitly out of scope (for v1)

- Per-Clockwork copy/paste templates — defer.
- Cross-Clockwork visualization (signal flow animations between
  partitions).
- Sub-grouping inside a Clockwork (player-defined sub-Clockworks).
- Auto-rename buildings using the Clockwork prefix scheme — naming is
  manual.
- Validation errors / cycle warnings beyond what vanilla already surfaces.

## Self-review checklist

- **Placeholders / TBD?** One: hotkey default. The mod can pick `C` at
  implementation time; not a design issue.
- **Internal consistency?** Partition mirroring is consistent throughout.
  The "anchor" mechanism handles all wire-edit cases. The drawer view
  derives entirely from runtime partition state plus the two mod
  dictionaries.
- **Scope check?** v1 covers exactly the wiring-editor surface — no new
  gameplay, no templates, no validators. Single implementation plan
  feasible.
- **Ambiguity?** Numbering scheme for leaf auto-indices is left to
  implementation (per-type registry counter, stable per-game). Acceptable.
