# Advanced Zip Line Station

A Timberborn mod that adds an upgraded zipline station with 3 connection points and mechanical power transfer through zipline cables.

## Features

- **3 Zipline Connections** — vanilla stations support 2; this one supports 3
- **Mechanical Shaft** — acts as a passive power connector for adjacent buildings via transputs on all 6 ground blocks
- **Power Through Cables** — Harmony patches bridge the zipline and mechanical systems so power flows through zipline connections between stations
- **Same Footprint** — 3x2x4, reuses the vanilla Folktails zipline station model

## Building Cost

| Resource | Amount |
|----------|--------|
| Log | 30 |
| Plank | 60 |
| Metal Block | 30 |
| Science | 1050 |

## How Power Transfer Works

When two Advanced Zip Line Stations are connected via a zipline cable, their mechanical graphs are automatically merged. Power flows through the cable just like it would through a physical shaft connection. Disconnecting the cable splits the graphs back apart.

This works for:
- New connections between built stations
- Connections restored from save files
- Stations that finish construction with existing cable connections

## Technical Details

- Targets `netstandard2.1`
- Requires Harmony (patched via `IModStarter`)
- Patches `ZiplineConnectionService.Connect`, `ActivateConnection`, and `Disconnect`
- Uses reflection to access `MechanicalGraphFactory.Join()` and `MechanicalGraphReorganizer.Reorganize()`
- Tool order 51 (appears right after vanilla zipline station)

## Installation

Build with `dotnet build` — the post-build target automatically deploys to `~/Documents/Timberborn/Mods/AdvancedZipLineStation/`.
