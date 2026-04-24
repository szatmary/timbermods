# MapGen Serializer — Design Spec

**Date:** 2026-04-24
**Status:** Draft, ready for review
**Predecessor:** [MapGen.Core algorithm + preview](2026-04-24-timberborn-map-generator-design.md)

## Goal

A pure-C# serializer that turns a `MapData` value object into a Timberborn `.timber` file the game can load via its Map Editor. Replaces the placeholder `.timber` writing in §7 of the parent spec, now grounded in real reverse-engineering of the file format.

## Scope

In scope for this plan:
- ZIP container with 4 entries (`version.txt`, `map_metadata.json`, `map_thumbnail.jpg`, `world.json`).
- Full `world.json` writing — singletons + entities — sufficient to load and play in the in-game Map Editor.
- Optional caller-supplied JPEG thumbnail; null → 1×1 stub embedded.
- Catalog-template-name updates so generated maps reference real Timberborn templates.
- Cascade fixes to existing Plan-1 code where the simplifications drop now-irrelevant complexity (per-faction food check, etc.).
- Alphanumeric seeds (string, hashed to uint internally).
- Round-trip tests + a manual in-game integration test path.

Out of scope (Plan 3 territory):
- The Timberborn mod wrapper.
- In-game UI integration (main-menu button, map-editor "Generate" template).
- Slope-placement repair pass (deferred from Plan 1; needs slope template name confirmation but that's now done — could even be added here, but kept deferred to avoid scope creep).

## Reverse-engineered format reference

Harvested from a user-saved sample (`~/Documents/Timberborn/Maps/EVERYTHING.timber`, game version 1.0.13.0).

### Container

ZIP archive (deflate). Four entries:
- `version.txt` — plain text, single line, e.g., `"1.0.13.0-1e60728-xsm"`
- `map_metadata.json` — small JSON header
- `map_thumbnail.jpg` — JPEG preview, ~290 KB in sample (size varies)
- `world.json` — main map content

### `map_metadata.json`

```json
{
  "Width": 32,
  "Height": 32,
  "MapNameLocKey": "",
  "MapDescriptionLocKey": "",
  "MapDescription": "",
  "IsRecommended": false,
  "IsUnconventional": false,
  "IsDev": false
}
```

### `world.json` top level

```json
{
  "GameVersion": "1.0.13.0-1e60728-xsm",
  "Timestamp": "2026-04-24 23:22:49",
  "Singletons": { ... },
  "Entities": [ ... ]
}
```

### Singletons (keys observed in sample)

```
MapSize.Size.{X, Y}
TerrainMap.Voxels.Array       (space-separated 0/1 string, length = W*H*Zmax)
WaterMapNew.{Levels, WaterColumns.Array, ColumnOutflows.Array}
MapThumbnailCameraMover.CurrentConfiguration
HazardousWeatherHistory.HistoryData
WaterEvaporationMap.{Levels, EvaporationModifiers}
WaterSimulationMigrator.IsMigrated
SoilMoistureSimulator.{Size, MoistureLevels}
SoilContaminationSimulator.{Size, ContaminationCandidates, ContaminationLevels}
NumberedEntityNamerService.NextNumbers
WindService.{WindStrength, WindDirection, NextWindChangeTime}
```

For 32×32 sample, `TerrainMap.Voxels.Array` had 23552 entries = 32 × 32 × 23 (z-levels). The exact (x, y, z) ordering of the flat array is determined experimentally during implementation by writing a known-shape map and inspecting. `Zmax` clamped at 32 per parent spec.

### Entities

```json
{
  "Id": "<GUID>",
  "Template": "<template name>",
  "Components": { ... }
}
```

Every entity has at minimum a `BlockObject` component:
```json
"BlockObject": { "Coordinates": { "X": 10, "Y": 13, "Z": 4 } }
```
Optionally `Orientation: "Cw180"` / `"Cw90"` / `"Cw270"` for rotated placements.

### Template names (the complete set the Map Editor offers)

| Category | Templates |
|----------|-----------|
| Trees | `Pine`, `Birch`, `Oak`, `Maple` |
| Resources | `BlueberryBush` (sole food template) |
| Thorns | `Thorns` |
| Slopes | `Slope`, `NaturalOverhang2x1`, `NaturalOverhang3x1`, `NaturalOverhang4x1` |
| Ruins | `RuinColumnH1`..`RuinColumnH8`, `UndergroundRuins`, `AncientAquiferDrill` |
| Blockages | `Blockage`, `NaturalDam` |
| Hazards | `UnstableCore`, `GeothermalField` |
| Relics | `SmallRelic`, `MediumRelic`, `LargeRelic` |
| Water sources | `WaterSource`, `BadwaterSource`, `Aquifer`, `WaterSeep`, `BadwaterSeep`, `BadtideDrain` |
| Start | `StartingLocation` |

### Per-template Components observed

| Template | Components beyond `BlockObject` |
|----------|---------------------------------|
| Tree (any) | `CoordinatesOffsetter:{Random:true}`, `LivingNaturalResource:{IsDead:false}`, `Growable:{GrowthProgress:1.0}`, `Yielder:Cuttable:{Yield:{Good:"Log",Amount:2}}`, optional `Yielder:Gatherable:{Yield:{Good:"PineResin",Amount:0}}` for Pine |
| BlueberryBush | `CoordinatesOffsetter`, `LivingNaturalResource:{IsDead:false}`, `Yielder:Gatherable:{Yield:{Good:"Berries",Amount:6}}` |
| Thorns / Slope / Overhang / Blockage / NaturalDam / GeothermalField / Relic / StartingLocation / UndergroundRuins / AncientAquiferDrill | `BlockObject` only |
| RuinColumnH<N> | `Yielder:Ruin:{Yield:{Good:"ScrapMetal",Amount:45}}`, `RuinModels:{VariantId:"<A/B/C>"}` |
| UnstableCore | `TimeActivatedComponent:{IsEnabled:true,CyclesUntilCountdownActivation:5,DaysUntilActivation:10.5,DaysPassed:0.0}`, `UnstableCore:{ExplosionRadius:5}` |
| WaterSource / BadwaterSource / Aquifer / BadtideDrain | `WaterSource:{SpecifiedStrength,CurrentStrength}`, `TimeActivatedComponent` (disabled by default) |
| WaterSeep / BadwaterSeep | adds `WaterDepthStrengthModifier:{CurrentModifier:0.0}` |

## Architecture

Single new file: `MapGen/MapGen.Core/MapSerializer.cs`. No external project added.

### Public API

```csharp
public static class MapSerializer
{
    public static void Write(MapData map, string path,
        string gameVersion, byte[]? thumbnailJpeg = null);
}
```

- `map` — output of `MapGenerator.Generate(...)`.
- `path` — absolute path to write `.timber` file. Existing file overwritten.
- `gameVersion` — string written verbatim into both `version.txt` and `world.json:GameVersion`. Caller's responsibility to pass the right value (Preview CLI hardcodes; future mod queries the running game).
- `thumbnailJpeg` — bytes of a JPEG file. Null → embed a built-in 1×1 stub.

### Internal organization

Inside `MapSerializer.cs`:
- `Write` opens a `ZipArchive`, calls four entry writers.
- `WriteVersionTxt(stream, version)` — UTF-8 text, no BOM.
- `WriteMapMetadata(stream, map)` — fixed JSON shape.
- `WriteThumbnail(stream, jpegBytes ?? StubJpegBytes)`.
- `WriteWorldJson(stream, map, version)`:
  - `WriteSingletons(map)` — composes the singletons block
  - `WriteEntities(map)` — composes the entities array
- `BuildComponents(PlacedEntity)` — switch on `EntityKind` returns the per-template component bag

### No external dependencies

Container: `System.IO.Compression.ZipArchive` (netstandard2.1).
JSON: `System.Text.Json` (already a dependency from Catalog loading in Plan 1).
Stub JPEG: tiny byte array embedded as a `static readonly byte[]`.

### Determinism

Output is deterministic given the same `(MapData, gameVersion, thumbnailJpeg)` inputs **except** for:
- `Timestamp` field (uses `DateTime.UtcNow`)
- Entity `Id` GUIDs (generated per write)

If round-trip byte-equality matters for testing, Timestamp can be parameterized as an optional argument and GUIDs derived from a seeded namespace + entity index. Default: real timestamp + random GUIDs.

## Plan-1 cleanup rolled into this plan

Five small edits to existing files. They're prerequisites because the serializer expects real template names and the simplified resource model.

### 1. Catalog JSON files use real template names

| File | Change |
|---|---|
| `Trees.json` | Use `Pine`, `Birch`, `Oak`, `Maple` as `blueprintKey` |
| `Resources.json` | Single entry: `BlueberryBush` |
| `Thorns.json` | `Thorns` |
| `Ruins.json` | `RuinColumnH1`..`H8` (random pick at placement time) |
| `BlockObjects.json` | `Slope`, `Blockage`, `UnstableCore`, `GeothermalField`, `SmallRelic`/`MediumRelic`/`LargeRelic`, `StartingLocation` |

### 2. Drop `Faction` field from Catalog

The file format doesn't distinguish factions. Faction differences happen at game-load. Drop `Faction` enum from `CatalogEntry`. All code paths that branched on faction become unconditional.

### 3. Simplify `AccessValidation`

- `ReachabilityReport.FolktailsFood` and `IronTeethFood` removed.
- `MeetsMinimums` becomes: `TreeCount >= 30 && ResourceCount >= 15 && WaterAccessCount >= 1`.
- `TopUp`'s faction-balanced food planting becomes a single "plant more BlueberryBush" loop.

### 4. Simplify `Overlays.PlaceResources` Start guarantee

Drop the "alternate Folktails/IronTeeth" loop. Plant 10× `BlueberryBush` directly.

### 5. Alphanumeric `Seed`

- `GenerationConfig.Seed: string` (was `uint`). Default = empty → random 8-char `[A-Z0-9]+` generated by config init.
- `Validate()` checks `^[A-Za-z0-9]{1,32}$`.
- `Rng` gets a `Rng(string seed)` constructor that hashes via FNV-1a 32-bit to a `uint`.
- Retry pattern: instead of `seed + (uint)attempt`, build the next attempt's seed string by appending `-N` (e.g., `"X4Q7K2WB"`, `"X4Q7K2WB-1"`, `"X4Q7K2WB-2"`).
- `GenerationResult.ActualSeedUsed: string`.
- Preview CLI changes: `--seed STR` for a specific seed; `--count N` for N random seeds. `--seed-range` removed.

## Test strategy

### Unit (`MapGen.Core.Tests/MapSerializerTests.cs`)

- `Write_produces_zip_with_four_entries` — verify entries by name.
- `Write_includes_supplied_thumbnail` — bytes round-trip exactly.
- `Write_with_null_thumbnail_uses_stub` — small JPEG present (<2 KB).
- `WorldJson_top_level_has_required_fields` — `GameVersion`, `Timestamp`, `Singletons`, `Entities`.
- `WorldJson_metadata_size_matches_map`.
- `WorldJson_terrain_voxel_count_matches` — for known map shape, count tokens in `Voxels.Array` and assert `== W * H * Zmax`.
- `WorldJson_entity_per_placed` — `Entities.Count == map.Entities.Count`.
- `WorldJson_entity_template_matches_blueprintkey`.

### Reference sample

`MapGen/MapGen.Core.Tests/Samples/EVERYTHING.timber` — sanitized copy of the user's harvested sample (thumbnail removed to keep <50 KB) committed as a fixture. Used for shape comparisons in tests; not a round-trip target.

### Integration (manual, gated on Timberborn install)

Preview CLI gains `--write-timber` flag (alongside the existing PNG output):

```
dotnet run --project MapGen.Preview/MapGen.Preview.csproj -- \
  --count 3 --width 128 --height 128 --write-timber \
  --out ~/Documents/Timberborn/Maps/
```

Then in Timberborn → Map Editor → Load → see the maps and load each. Pass criteria:
- Loads without exception.
- Terrain renders.
- Water sources placed at the right spots.
- Beavers can be placed (for full game) or the editor displays without crash (for editor preview).

Failure modes and triage:
- `JsonReaderException` → JSON shape mismatch. Diff our `world.json` against the harvested sample, find the offending field.
- Out-of-range coordinate → entity placed outside terrain bounds; bug in MapGen.
- Missing required field on a singleton → harvest an empty/minimal map sample, find the missing fields, add to the writer.

## Failure handling

Serializer throws on:
- File-write errors (caller's path is bad / permission denied).
- `MapData` invariant violations (e.g., null arrays, mismatched dimensions).

It does NOT validate the game can load the output — that's an integration concern. Caller is responsible for catching IO exceptions.

## Open implementation lookups

These are deferred until the implementer hits them; pre-listed so they're not surprises:

1. **Voxel array (x, y, z) ordering** — write a 3-cell-tall corner test map, inspect output, determine ordering.
2. **Singletons defaults** — for any singleton our generator doesn't have data for (HazardousWeatherHistory, WaterEvaporationMap, etc.), the implementer determines the minimum fields by harvesting a 2nd reference sample (an empty Map Editor save).
3. **`RuinModels.VariantId`** — observed values were `"A"`, `"B"`, `"C"`. Implementer picks at random per ruin or always uses `"A"`. Cosmetic.
4. **`Yielder` "good" amounts** — observed sample had `Amount: 0` because the user just placed (not configured). For our generated maps we want full yields (e.g., `Berries: 6`, `Log: 2`, `ScrapMetal: 45`). Use observed defaults; tweak after first integration test.
