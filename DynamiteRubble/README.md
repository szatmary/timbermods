# Dynamite Rubble

A Timberborn mod that makes dynamite and tunnel explosions leave behind Dirt rubble.

## Features

- **Dynamite** — Produces 3 Dirt per terrain block actually destroyed
  - Triple dynamite on solid ground drops 9 Dirt
  - Double dynamite on a 1-block overhang drops only 3 Dirt
  - Correctly handles overhangs, thin columns, and the bottom of the map
- **Tunnels** — Produces 3 Dirt when a tunnel finishes construction

## How It Works

The mod counts contiguous terrain blocks below the dynamite before detonation. Only blocks that are actually destroyed produce rubble — floating terrain with air gaps below won't inflate the count. Dirt appears as a recoverable good stack at the explosion site.

## Technical Details

- Harmony prefix on `Dynamite.Detonate()` counts terrain via `GetDestroyedLayersCount` (clamped to avoid negative-z wraparound)
- Harmony postfix on `Tunnel.Explode()` for tunnel rubble
- Captures `RecoveredGoodStackSpawner` via `BuildingGoodsRecoveryService` constructor patch
- Spawns dirt using `AddAwaitingGoods()` via reflection

## Installation

Build with `dotnet build` — the post-build target automatically deploys to `~/Documents/Timberborn/Mods/DynamiteRubble/`.

## Compatibility

- Timberborn v1.0.0.0+
- Works with all factions
