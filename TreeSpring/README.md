# TreeSpring

A Timberborn mod that adds a tree-based mechanical power storage building. A winch bends a tree to store energy, which is slowly released back as power.

## Concept

A 1x3 building with three sections: a winch at the front, a power shaft through the middle, and a flexible tree at the back. The winch bends the tree over to store mechanical energy, and the tree's natural spring-back releases it. The central shaft passes power straight through.

## Features

- **1x3x4 Footprint** — winch (front), power shaft (middle), tree (back), 4 blocks tall
- **1500 hp Storage** — stores 1500 horsepower-hours of energy
- **Power Shaft** — the middle tile acts as a shaft, passing power through the building
- **Directional Power** — transputs on the winch and shaft tiles only, not the tree side
- **Animated Tree** (planned) — the tree bends proportionally to stored energy:
  - 0% charge: tree stands straight up
  - 100% charge: tree bent in a 90° arc
- **Ground Placement** — must be built on the ground

## Building Cost

| Resource | Amount |
|----------|--------|
| Gear | 20 |
| Treated Plank | 20 |
| Log | 1 |

## How It Works

1. Connect the **winch or shaft side** to your mechanical power network
2. Excess power in the network bends the tree, storing energy
3. When power demand exceeds supply, the tree releases stored energy back into the network
4. The central shaft allows power to pass through to buildings behind

## Technical Details

- Targets `netstandard2.1`
- Custom `IBattery` implementation (no Harmony, pure DI)
- `IsShaft: true` with transputs on winch (Y=0) and shaft (Y=1) tiles
- Tree tile (Y=2) has no transputs
- Bindito `[Context("Game")]` configurator with template decorators

## Installation

Build with `./build.sh TreeSpring` from the repo root, or `dotnet build` from this directory.
