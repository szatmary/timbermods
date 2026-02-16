# Genetic Lottery

A Timberborn mod that gives each beaver a random lifespan modifier at birth.

## Features

- Every beaver born receives a random Life Expectancy bonus between -10% and +10%
- Some beavers live longer, some shorter — adding natural variation to your colony
- The bonus is visible in the beaver's bonus panel as "Life Expectancy"
- Applied through the game's existing bonus system and persists for the beaver's entire life

## Technical Details

- Listens for `CharacterCreatedEvent` via EventBus (filters for beavers only)
- Calls `BonusManager.AddBonus("LifeExpectancy", delta)` with a random value
- No Harmony patches needed — all APIs are public

## Installation

Build with `dotnet build` — the post-build target automatically deploys to `~/Documents/Timberborn/Mods/GeneticLottery/`.

## Compatibility

- Timberborn v1.0.0.0+
- Works with all factions
