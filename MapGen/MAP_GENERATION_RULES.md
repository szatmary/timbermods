# Map Generation Rules — Living Reference

Hard-won knowledge for producing `.timber` files that Timberborn (1.0.13) actually loads and plays. This is **prescriptive** — not "design intent" but the precise rules the game enforces. Update this doc every time we discover a new gotcha.

---

## 1. File container

A `.timber` file is a **ZIP archive** (deflate). It contains exactly four entries:

| Entry | Format | Purpose |
|-------|--------|---------|
| `version.txt` | UTF-8 plain text | Single line, the game version string (e.g. `1.0.13.0-1e60728-xsm`) |
| `map_metadata.json` | JSON | Map header — width, height, name, flags |
| `map_thumbnail.jpg` | JPEG | Preview image shown in the map picker |
| `world.json` | JSON | The actual map content (singletons + entities) |

Missing or extra entries are likely fatal. The game version string is written into BOTH `version.txt` and `world.json:GameVersion` and must match what's actually running (or close enough; minor drift seems tolerated, major version drift breaks).

---

## 2. `world.json` top-level shape

```json
{
  "GameVersion": "1.0.13.0-...",
  "Timestamp": "YYYY-MM-DD HH:MM:SS",
  "Singletons": { ... },
  "Entities": [ ... ]
}
```

Both `Singletons` and `Entities` are required (can be empty objects/arrays but must exist).

---

## 3. Singletons — required fields, exact shapes

Every one of these singletons must be present and have the exact shape below or the game throws on load (typically `InvalidCastException` deep in `Timberborn.SerializationSystem`).

### `MapSize`
```json
{ "Size": { "X": <int>, "Y": <int> } }
```

### `TerrainMap`
```json
{ "Voxels": { "Array": "<space-separated 0/1 string>" } }
```

**Voxel array length must be exactly `W * H * 23`.** Z is fixed at **23** (Timberborn's `MapSizeSpec.MaxMapEditorTerrainHeight`). Bigger or smaller arrays trigger `IndexOutOfRangeException` in `MapIndexService.Unpack3D`.

**Layout order: ZYX** — index = `z * (W*H) + y * W + x`. The first `W*H` entries are the z=0 plane, then the z=1 plane, etc.

### `WaterMapNew`
```json
{
  "Levels": 2,
  "WaterColumns": { "Array": "<space-separated zeros, length W*H*Levels>" },
  "ColumnOutflows": { "Array": "<same length>" }
}
```
`Levels` is the integer **2** (an integer, not an object). Both arrays are length `W * H * 2`.

### `WaterEvaporationMap`
```json
{
  "Levels": 1,
  "EvaporationModifiers": { "Array": "<length W*H>" }
}
```
`Levels` is the integer **1** (not an object). Array length matches.

### `WaterSimulationMigrator`
```json
{ "IsMigrated": true }
```

### `SoilMoistureSimulator`
```json
{
  "Size": 2,
  "MoistureLevels": { "Array": "<length W*H*Size>" }
}
```
**`Size` is the integer 2 — NOT an object with X/Y.** It's the # of vertical strata. Array length is `W * H * 2`.

### `SoilContaminationSimulator`
```json
{
  "Size": 2,
  "ContaminationCandidates": { "Array": "<length W*H*Size>" },
  "ContaminationLevels": { "Array": "<length W*H*Size>" }
}
```
Same `Size: 2` int. `ContaminationCandidates` is an OBJECT with an Array, NOT an empty array.

### `HazardousWeatherHistory`
```json
{ "HistoryData": [] }
```
Empty array works. The game schedules its own weather post-load.

### `NumberedEntityNamerService`
```json
{ "NextNumbers": [] }
```
**Empty ARRAY, not empty object.** The game expects `ListKey<T>`.

### `WindService`
```json
{
  "WindStrength": 0.0,
  "WindDirection": { "X": 0.0, "Y": 0.0 },
  "NextWindChangeTime": 0.0
}
```
**`WindDirection` is a Vector2 (`{X, Y}`), NOT a string.** Sample's WindStrength is 0.0; non-zero might be valid but unconfirmed.

### `MapThumbnailCameraMover`
```json
{ "CurrentConfiguration": {} }
```

---

## 4. Entities — required structure

```json
{
  "Id": "<GUID>",
  "Template": "<canonical template name>",
  "Components": { ... }
}
```

`Id` is any GUID (we generate fresh per write). `Template` must be a **real Timberborn template** (see §6). Every entity needs at minimum a `BlockObject` component.

### Universal `BlockObject` component
```json
"BlockObject": {
  "Coordinates": { "X": <int>, "Y": <int>, "Z": <int> },
  "Orientation": "Cw0" | "Cw90" | "Cw180" | "Cw270"  (optional; defaults to Cw0)
}
```

### Z coordinate convention (CRITICAL)

**For ALL entities (trees, berries, water sources, seeps, ruins, slopes, anything):**
```
Z = (top of the topmost solid voxel span at this column) + 1
```

i.e., the entity sits at the first AIR voxel above the terrain. This convention applies even for water sources placed on carved water cells — those columns have a SHORTER solid span, so `TopHeight + 1` puts the source at the bottom water voxel (with solid directly beneath).

**Wrong Z = "Object had an invalid location and was deleted" on game load.** Common mistakes:
- Placing entities in water cells (Z inside the water column, not at TopHeight+1)
- Using a global H_base for Z when the local column is carved/elevated differently
- Forgetting that water carving reduces TopHeight by the carve depth

### Duplicate-coord rule

**Two entities cannot share the same (X, Y) column** (game rejects the second as "invalid location"). Track placed entities and skip cells already occupied. Different Z on the same X/Y also seems rejected — probably the game treats columns as 1×1 footprints regardless of height.

---

## 5. Per-template Components

Beyond `BlockObject`, each template needs specific component blocks. Anything missing → game throws on load. Wrong type for a field → throws.

### Trees (Pine / Birch / Oak / Maple)
```json
"Components": {
  "BlockObject": { ... },
  "CoordinatesOffsetter": { "Random": true },
  "LivingNaturalResource": { "IsDead": false },
  "Growable": { "GrowthProgress": 1.0 },
  "Yielder:Cuttable": { "Yield": { "Good": "Log", "Amount": 2 } },
  "Yielder:Gatherable": { "Yield": { "Good": "PineResin", "Amount": 0 } }   // Pine only; omit for others
}
```

### `BlueberryBush` (the only food-resource template available in the editor)
```json
"Components": {
  "BlockObject": { ... },
  "CoordinatesOffsetter": { "Random": true },
  "LivingNaturalResource": { "IsDead": false },
  "Yielder:Gatherable": { "Yield": { "Good": "Berries", "Amount": 6 } }
}
```

### `Thorns`, `Slope`, `NaturalOverhang2x1` / `3x1` / `4x1`, `Blockage`, `NaturalDam`, `GeothermalField`, `SmallRelic` / `MediumRelic` / `LargeRelic`, `StartingLocation`, `UndergroundRuins`, `AncientAquiferDrill`
```json
"Components": { "BlockObject": { ... } }
```
Just `BlockObject`. No additional components.

### `RuinColumnH1`..`RuinColumnH8`
```json
"Components": {
  "BlockObject": { ... },
  "Yielder:Ruin": { "Yield": { "Good": "ScrapMetal", "Amount": 45 } },
  "RuinModels": { "VariantId": "A" }   // also "B", "C"
}
```

### `UnstableCore`
```json
"Components": {
  "BlockObject": { ... },
  "TimeActivatedComponent": {
    "IsEnabled": true,
    "CyclesUntilCountdownActivation": 5,
    "DaysUntilActivation": 10.5,
    "DaysPassed": 0.0
  },
  "UnstableCore": { "ExplosionRadius": 5 }
}
```

### `WaterSource`, `BadwaterSource`, `Aquifer`, `BadtideDrain` (constant-flow sources)
```json
"Components": {
  "BlockObject": { ... },
  "WaterSource": {
    "SpecifiedStrength": <float>,
    "CurrentStrength": <float>
  },
  "TimeActivatedComponent": {
    "IsEnabled": false,
    "CyclesUntilCountdownActivation": 5,
    "DaysUntilActivation": 10.0,
    "DaysPassed": 0.0
  }
}
```

### `WaterSeep`, `BadwaterSeep` (self-regulating sources)
Same as above PLUS:
```json
"WaterDepthStrengthModifier": { "CurrentModifier": 0.0 }
```

---

## 6. Canonical template list (what the Map Editor offers)

This is the **complete** set of placeable entity templates the editor supports. Everything else is internal-only or built by the player in-game.

| Category | Templates |
|----------|-----------|
| Trees | `Pine`, `Birch`, `Oak`, `Maple` |
| Resources | `BlueberryBush` (the **only** food template) |
| Thorns | `Thorns` |
| Slopes | `Slope`, `NaturalOverhang2x1`, `NaturalOverhang3x1`, `NaturalOverhang4x1` |
| Ruins | `RuinColumnH1`..`RuinColumnH8`, `UndergroundRuins`, `AncientAquiferDrill` |
| Blockages | `Blockage`, `NaturalDam` |
| Hazards | `UnstableCore`, `GeothermalField` |
| Relics | `SmallRelic`, `MediumRelic`, `LargeRelic` |
| Water | `WaterSource`, `BadwaterSource`, `Aquifer`, `WaterSeep`, `BadwaterSeep`, `BadtideDrain` |
| Start | `StartingLocation` |

---

## 7. Water sources vs seeps — overflow semantics (CRITICAL)

| Entity | Behavior | Use when |
|--------|----------|----------|
| `WaterSource` (and Badwater/Aquifer/BadtideDrain) | Emits constant flow at `SpecifiedStrength`. Does NOT self-regulate. **Will overflow a sealed water body.** | The water body has a real downhill drain (an edge or sea cell at lower elevation than the source). Source-to-drain hydrology only. |
| `WaterSeep` (and Badwater Seep) | Emits only enough to top up evaporation; stops when water level is satisfied. Self-regulating. | A sealed pond, a stagnant water area, anywhere without a drain. Cannot overflow. |

**The lethal bug:** placing a `WaterSource` over a sealed pond floods the map within game-minutes.

**The corollary:** if your generated terrain doesn't have proper downhill drainage (e.g., a v1 flat-fill outside the home base), do NOT use `WaterSource` — use `WaterSeep` everywhere.

---

## 8. Carved water cells — geometry

For a column we want to be a water cell with bank height `H_base`, water depth 2:

- Solid spans: `[(0, H_base - 2)]` — column has solid voxels z=0..H_base-3 (so its `TopHeight = H_base - 3`).
- `WaterDepths[col] = 2` — water occupies z = H_base-2 and z = H_base-1.
- Water surface at z = H_base. The bank cells around it are at `H_base` (their `TopHeight = H_base - 1`, `TopHeight + 1 = H_base`).
- Beavers can drink/pump from the bank without falling in (water surface meets bank top).
- Entities placed in the water cell go at `z = TopHeight + 1 = H_base - 2` (bottom water voxel, with solid directly below).

**Water surface MUST NOT exceed surrounding bank height** or the water spills onto the surrounding cells. For our v1: home-base water at H_base, surrounding plain at H_base - 1 → water surface at H_base is **above** the plain — anywhere a real `WaterSource` exists, water overflows. Safe configurations:
- Surrounding plain at `H_base` or higher (water contained by banks)
- Use only `WaterSeep`s (self-regulating, no overflow)

---

## 9. Beaver pathing rules (used for access validation)

Beavers cannot step UP a height delta via terrain alone. They need:
- **Slope** (placed entity): bidirectional traversal between cells with delta = 1
- **Built stairs** (in-game player construction)
- **No "natural stair"** terrain shape works — you can't carve stair-shaped terrain and have beavers climb it. Slopes are explicit entities.

Beavers CAN:
- Walk to flat (same-height) neighbors
- Step DOWN (any height delta — they fall) — but for round-trip resource gathering this is one-way and useless
- Drink/fish/pump from water-adjacent BANK cells (where water surface = bank-top elevation)

For a generated map to be playable: from the StartingLocation cell, BFS along same-height edges (+ slopes) must reach water and minimum trees/berries.

---

## 10. The District Center is auto-placed

In Timberborn, when a new game starts on a map, the **District Center is automatically placed at the `StartingLocation` entity's coordinates**. The player does not place it manually. Therefore:

- The map MUST have exactly ONE `StartingLocation` entity.
- The cell MUST be on flat ground (the District Center has a footprint — 4×4 last we checked).
- The cells immediately surrounding it should be flat and open (no entities) so beavers spawn into walkable space and aren't immediately stuck.

Our convention: a 4×4 district-center spot at `H_base`, with a 3-voxel ring of LAND around it (entity-free, also at `H_base`).

---

## 11. Map Editor's `MaxMapEditorTerrainHeight` is fixed at 23

The voxel array's z dimension is hardcoded at 23 by the game. Don't try to make it smaller (saves space) or bigger (more vertical room) — `MapIndexService.Unpack3D` rejects everything else. If we want taller terrain in the future we'll need to find where this is set and override it (probably in `Timberborn.MapStateSystem.MapSizeSpec`).

---

## 12. What the schema ISN'T

Things that LOOK like they should be there but aren't:

- **No "biome" field** at the top level. Biomes are an internal-only concept used by the heightmap renderer; the saved file just has terrain voxels.
- **No "starting beavers" entities**. Beavers are spawned by the District Center at game start, not stored in the map.
- **No "weather schedule"**. `HazardousWeatherHistory` is a runtime log; an empty array is fine for a fresh map.
- **No multiple-faction maps**. The map file is faction-agnostic; the player picks Folktails or Iron Teeth at game start.

---

## 13. Seed → output determinism

If a seed produces an identical (Width, Height) twice, the OUTPUT must be identical too, except:
- `Timestamp` (UTC now)
- Entity `Id` GUIDs (regenerated per write)

Everything else — terrain, water, every entity's position and components — must be byte-stable. Hash the seed via FNV-1a 32-bit; never use `System.Random` (its sequence varies between .NET runtimes).

---

## 14. Catalogs vs templates

We use a small JSON catalog (`MapGen.Catalogs/Trees.json` etc.) mapping internal "kind" names to Timberborn template names. The internal name is for our own algorithm convenience (e.g., `dead_stump` reuses the `Pine` template with `IsDead: true`). The `BlueprintKey` field of each catalog entry must be one of the canonical templates from §6.

---

## 15. Open questions / known unknowns

- **Carving depth limits?** We carve 2 voxels deep for water. Deeper carves untested.
- **Negative WaterSource strengths?** Untested. Sample game saves had values like 1.0, 1.5, 3.0.
- **Slope orientation rules?** Slope entity has a default North orientation; we haven't tested rotated slopes for crossing differently-oriented height steps.
- **Maximum entity count per map?** No observed limit; our largest test maps had ~600 entities. Game might slow down with thousands.
- **Multi-cell-footprint entities** (e.g., RuinColumnH8 might be vertically tall but is it 1×1 footprint or larger?). We've been assuming 1×1 for everything; multi-cell ruins might need different handling.

Add to this list as we hit new edges.
