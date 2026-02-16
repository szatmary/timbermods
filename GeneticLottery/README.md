# Genetic Lottery

A Timberborn mod that prevents death waves and evens out colony playability by giving each beaver a random lifespan modifier at birth. In vanilla, beavers born at the same time all die at the same time, creating population waves and boom/bust cycles. This mod spreads out lifespans so deaths occur gradually instead of all at once.

## Features

- Every beaver born receives a random Life Expectancy bonus between -10% and +10%
- Prevents death waves by desynchronizing lifespans across your colony
- Smooths out population fluctuations for more stable, predictable gameplay
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
