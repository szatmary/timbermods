# Power Zipline

A Timberborn mod that adds power zipline buildings with mechanical power transfer through zipline cables.

## Features

- **Power Zipline Station** — 3 connections, acts as a passive power connector via transputs on all 6 ground blocks
- **Power Zipline Pylon** — relay tower with power transfer
- **Power Zipline Beam** — horizontal relay with power transfer
- **Power Through Cables** — Harmony patches bridge the zipline and mechanical systems so power flows through zipline connections
- **Replaces Vanilla** — vanilla zipline buildings are hidden from the toolbar

## Building Costs

### Station (3x2x4)
| Resource | Amount |
|----------|--------|
| Log | 35 |
| Plank | 30 |
| Gear | 15 |
| Metal Block | 15 |
| Science | 1000 |

### Pylon (1x1x4) / Beam (1x3x1)
| Resource | Amount |
|----------|--------|
| Plank | 20 |
| Gear | 10 |
| Metal Block | 10 |
| Science | 750 / 900 |

## How Power Transfer Works

When two power zipline buildings are connected via a zipline cable, their mechanical graphs are automatically merged. Power flows through the cable just like it would through a physical shaft connection. Disconnecting the cable splits the graphs back apart.

This works for:
- New connections between built stations
- Connections restored from save files
- Stations that finish construction with existing cable connections

## Technical Details

- Targets `netstandard2.1`
- Requires Harmony (patched via `IModStarter`)
- Patches `ZiplineConnectionService.Connect`, `ActivateConnection`, and `Disconnect`
- Uses reflection to access `MechanicalGraphFactory.Join()` and `MechanicalGraphReorganizer.Reorganize()`

## Installation

Build with `dotnet build` — the post-build target automatically deploys to `~/Documents/Timberborn/Mods/PowerZipline/`.
