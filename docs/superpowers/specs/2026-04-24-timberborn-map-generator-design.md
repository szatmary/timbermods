# Timberborn Map Generator — Design Spec

**Date:** 2026-04-24
**Status:** Draft, ready for review

## Goal

A Timberborn mod that generates playable `.timber` map files procedurally, with two in-game entry points:
- **Main menu → New Game → "Random Map"** — quick path from idle to playing.
- **Map Editor → New Map → "Generate" template** — generate a map, then hand-tweak before saving.

Produces varied, visually distinct maps with authored-feeling biome layouts, guaranteed-playable starting areas, and natural river systems. Works for both Folktails and Iron Teeth factions from a single generated map.

## Success Criteria

- Generated maps load without crash in Timberborn, same version the mod was tested against.
- Every generated map has: a flat buildable starting area with ≥30 trees, ≥15 edible resources (both factions represented), ≥1 water access cell within flat-reachable range.
- Every generated map has at least one river (surface or underground); rivers always reach a drain (map edge or sea).
- Seeded reproducibility: same width × height × seed always produces the same map.
- Generation time ≤ 10 seconds on a 300×300 map.

## Architecture

Hard boundary between a pure-C# core and the Timberborn mod wrapper.

### `MapGen.Core/` — pure C# (no Unity, no Timberborn deps)

Takes `(seed, width, height)` plus a config struct, returns a `MapData` value object, and can also serialize that `MapData` directly to a `.timber` file on disk. Deterministic, unit-testable, runs headlessly.

Components:
- `BiomeGrid` — 2D WFC on a coarse metacell grid. Authored adjacency table.
- `HeightMap` — biome-aware fBM noise with per-biome profiles blended at borders.
- `Hydrology` — pick river sources, trace downhill to drain, carve channels, optionally promote stretches to underground.
- `Overlays` — per-biome Poisson-disk sampling for resources, trees, thorns, ruins, blockages, relics, unstable cores, geothermal vents.
- `StartSelection` — post-WFC pass that relabels one metacell as Start based on biome scoring.
- `AccessValidation` — BFS from Start; top-up + slope-placement repair; rejects and retries if minimums can't be met.
- `MapSerializer` — `MapData` → gzipped JSON `.timber` file.
- `MapGenerator` — orchestrates the pipeline, handles retries, surfaces errors.

### `MapGen.Timberborn/` — Unity + Timberborn deps

Thin mod wrapper.
- `MainMenuIntegration` — inserts "Random Map" button into the New Game flow.
- `MapEditorIntegration` — adds a "Generate" template to the Map Editor's New Map dialog.
- `BlueprintResolver` — on mod load, resolves internal catalog ids (`"pine"`, `"slope-v1"`, etc.) to the game's runtime blueprint ids via `SpecService`. Caches results. Disables the generator if critical ids are missing.
- `MapGenConfigurator` — DI + startup.

### Why the split

The core algorithm is the iterate-heavy part. Keeping it Unity-free means we can write a headless CLI that renders generated maps as PNGs without launching Timberborn — effectively a 100× iteration speedup during biome and terrain tuning. The Timberborn-specific piece stays small and focused on UI wiring + catalog resolution.

## Pipeline

Sequential stages, each reads prior state:

```
WFC biomes → start-label → heightmap (with apron) → water → trees → resources →
  thorns → ruins → blockages → relics → unstable-cores → geothermal-vents →
  access-validation → serialize
```

Start label must be assigned before heightmap because the heightmap's flat-apron pass (§2) needs to know which metacell is Start.

## Section 1 — Biome Grid (2D WFC)

### Vocabulary

Six biomes used in WFC:

| biome | weight | notes |
|-------|--------|-------|
| Meadow | 37% | Default grassland. Flat, green, easy start. |
| Forest | 25% | Denser trees, rolling terrain. |
| Badland | 20% | Dry, cracked, ruin-heavy, mesa profile. |
| Rocky | 15% | Tall, jagged, supports overhangs. |
| Sea | 2% | Large body of water. Nearly flat, submerged. |
| Crater | 1% | Isolated bowl feature, raised rim + deep pit. |

**Start** is a post-WFC *label*, not a WFC vocabulary entry. Exactly one metacell gets relabeled.

### Grid sizing

- Map voxel dimensions: user-specified width × height.
- Biome metacell size: 8 voxels fixed.
- Biome grid: `⌈W/8⌉ × ⌈H/8⌉`.

### Adjacency table (symmetric)

| pair | allowed? | reasoning |
|------|---|---|
| Meadow↔Meadow | ✓ | |
| Meadow↔Forest | ✓ | Forests grow in grasslands. |
| Meadow↔Badland | ✓ | Dry edges of grassland. |
| Meadow↔Rocky | ✗ | Mountains need foothills; no abrupt transition. |
| Meadow↔Sea | ✓ | Coastal grassland. |
| Meadow↔Crater | ✓ | |
| Forest↔Forest | ✓ | |
| Forest↔Badland | ✗ | Forests don't grow in wasteland. |
| Forest↔Rocky | ✓ | Forested mountains. |
| Forest↔Sea | ✓ | Coastal forest. |
| Forest↔Crater | ✓ | |
| Badland↔Badland | ✓ | |
| Badland↔Rocky | ✓ | Harsh terrain cluster. |
| Badland↔Sea | ✓ | Dry coastal cliffs. |
| Badland↔Crater | ✓ | |
| Rocky↔Rocky | ✓ | |
| Rocky↔Sea | ✓ | Rocky coast. |
| Rocky↔Crater | ✓ | |
| Sea↔Sea | ✓ | Clustering when present. |
| Sea↔Crater | ✓ | |
| Crater↔Crater | ✗ | Craters stay isolated. |

### Algorithm

Standard tiled WFC:
1. Every metacell starts with all 6 options in its domain.
2. Pick the lowest-entropy cell (ties broken by seeded RNG).
3. Observe it — pick a biome weighted by frequency.
4. Propagate: remove incompatible options from 4-neighbors; queue neighbors until no more changes.
5. Repeat until fully collapsed or contradiction.
6. **Contradiction:** restart WFC with same seed + attempt counter. Cap 5 attempts before restarting pipeline at `seed+1`.

### Post-WFC fixes

- **Crater-on-edge rewrite:** any Crater landing on a border metacell is rewritten to the dominant adjacent biome (majority vote of in-bounds neighbors). Cheap; sidesteps the "no crater at edge" limitation of local WFC rules.
- **Start selection** (§5) runs next.

## Section 2 — Heightmap

### Output type

Columns of solid voxel ranges, to accommodate overhangs:

```
Column = List<(int bottom, int top)>
HeightMap output: Column[W, H]
```

Most cells: one span `(0, baseHeight)`. Overhang cells: two spans, with an air gap between.

### Per-biome profile

| biome | base height | amplitude | octaves | freq | notes |
|-------|-------------|-----------|---------|------|-------|
| Sea | 1 | ±1 | 2 | low | Near-flat, submerged. |
| Meadow | 4 | ±2 | 3 | medium | Gentle rolls. |
| Forest | 5 | ±3 | 4 | medium | Rolling with variation. |
| Badland | 6 | ±4 | 3 | medium-low | Mesa-like; amplitude clipped into plateaus. |
| Rocky | 14 | ±6 | 5 | high | Jagged, high-freq, supports overhangs. |
| Crater | special | — | — | — | Procedural bowl shape, see below. |
| Start | 4 | ±0 | — | — | Flat at base height; apron extends into neighbors. |

Shared fBM noise function; per-biome parameters change how it reads.

### Border blending

Noise sampler at voxel (x, y) queries the biome at (x, y) **and** biomes within a 2-voxel blend radius. Output is distance-weighted average of per-biome profile evaluations. No hard transitions.

### Crater shape

For each Crater metacell:
- Pick random center within the metacell, random radius 3–6 voxels.
- Height = base terrain height + `rim(r)` where `rim(r)` is a bowl function: positive (raised rim) between `0.7r` and `r`, strongly negative (pit) between `0` and `0.7r`, zero outside.
- Blends into surrounding biomes via the same distance-weighted blend.
- Optional: fill the pit with water at generate time for a crater-lake. Default on.

### Start apron

- The Start metacell is flat at Start base height (4).
- Each adjacent Meadow or Forest metacell gets a 3-voxel-wide apron along the shared border flattened to Start's base height, blended on the far side into that neighbor's natural profile.
- Rocky/Badland/Sea/Crater neighbors don't get an apron (incompatible base heights). Those borders remain cliffs (crossed by slope placement if needed).
- Produces a reliable 160–220 cell flat-connected zone around Start on typical maps.

### Overhangs (Rocky only)

- After base heightmap computed, detect "cliff edges": adjacent cell pairs where `|height_delta| ≥ 4`. The lower-side cell is a candidate.
- In Rocky biome only, 30% of cliff-edge candidates get an overhang:
  - 1–2 voxel thick block, 1–2 voxels out from the cliff top.
  - Air gap of 2–3 voxels between overhang and terrain below.
  - Top matches cliff top height (reads as a shelf).
  - Tapers over 1–2 cells as it extends from the cliff.
- Free-floating rocks elsewhere are forbidden — all overhangs must attach to a cliff.

### Edge margin

Optional 2-voxel frame around the map clamped to Meadow profile (prevents border-wall cliffs). Configurable.

### Downstream representation

Hydrology, overlays, access validation all treat "height" as the top of the topmost solid span per column. Overhang caves are navigable only by traversing the top of the upper span.

## Section 3 — Water (Hydrology)

### River count (scales with map size)

- `target_rivers = max(1, round(max(W, H) / 100))`
- Random jitter: `actual = target + rand(-1, 0)`, floored at 1.

Typical counts:
| map size | typical actual rivers |
|----------|-----------------------|
| 50×50 | 1 |
| 128×128 | 1 |
| 200×200 | 1–2 |
| 256×256 | 2–3 |
| 300×300 | 2–3 |
| 384×384 | 3–4 |

### Per-river archetype roll

- First river: always a fresh source → drain trace.
- Each additional river:
  - 60% chance: independent new source.
  - 40% chance: delta branch off an existing river midpoint.

### Source selection

Valid source = on the uphill third of the map (highest mean-elevation zone). Reject Sea, Crater, Badland. Prefer Forest, Rocky.

### Drain selection

- If any Sea metacell exists: drain to it.
- Else: lowest-elevation map edge cell.

### Downhill trace

Priority-flood descent:
- At each step, pick lowest-elevation 4-neighbor not already a river cell.
- Local minimum (basin): fill water until rim is overtopped; continue from the overflow point. Creates natural in-path lakes.
- Stop when reaching Sea cell or map edge.

### Carving

For each river-path cell: replace top 1–2 voxels of solid with air; add water 2–3 voxels deep. Width: 1 at source, widens to 2–3 toward drain.

### Underground variant

25% roll per river. Pick a contiguous 4–8 cell stretch; re-add solid voxels (1–2 voxels thick) on top of the water. Water underneath still simulates. Visual: river disappears under a hill and reappears downstream.

### Water entity placement

- **Main source:** one regular water source at each river head. Flow rate: trickle / stream / strong picked from variety table.
- **Sea / crater fills:** static water blocks pre-filled in the map data, no source entity.
- **Seeps:** 15% roll per valid biome-transition edge (Rocky↔anything is a common source). Places a small low-flow water source.
- **Badwater:**
  - 25% roll on each secondary river — head converted to badwater source.
  - 5% roll per Badland biome metacell — standalone badwater seep.

### Priority drain attraction

Hydrology ensures at least one river passes within 3 metacells of the Start label.

### Failure retries

- No valid source: retry with relaxed constraints (any non-Sea non-Water cell on uphill half).
- Trace dead-ends even after basin-filling: retry from different source, up to 3 attempts.
- All archetypes exhausted: restart pipeline at `seed+1`.

## Section 4 — Overlays

Start label has already been assigned (§5) and the heightmap apron applied (§2) by this point. Overlays run in order, each pass reads prior state and doesn't rewind:

1. Trees
2. Natural resources (berries, mushrooms, etc.)
3. Thorns
4. Ruins
5. Blockages
6. Relics
7. Unstable cores
8. Geothermal vents
9. Access validation (§6)

### Trees

Poisson-disk sampling per metacell. Species drawn from a data-driven `TreeCatalog.json`.

| biome | min spacing | species (faction-mixed) | cap/metacell |
|-------|-------------|-------------------------|--------------|
| Forest | 2 | Pine + Birch + Maple | none |
| Meadow | 3 | Maple + Birch | 8 |
| Badland | 5 | Dead stumps | 3 |
| Rocky | 4 | Pine at low altitudes (z ≤ 8) | 4 |
| Sea | — | none | 0 |
| Crater | — | Outer rim only | 2 |
| Start | — | none (inside Start metacell — for buildability) | 0 |

Species mix ensures both Folktails and Iron Teeth have woodcutting options in every tree-bearing biome.

### Natural resources

Per-biome table (drawn from `ResourceCatalog.json`):

| biome | resources (faction-mixed) | density |
|-------|---------------------------|---------|
| Meadow | Berries, Blueberries, Carrots, Chestnuts | ~8% of cells |
| Forest | Blueberries, Mushrooms | ~6% |
| Badland | Cactus, Dandelion | ~3% |
| Rocky | Blueberries (on shelves) | ~2% |
| Sea | — | 0 |
| Crater | Mushrooms (on inner slopes) | ~5% |
| Start | Berries + Blueberries mix | guaranteed ≥10 |

### Thorns

| biome | density (min spacing) |
|-------|----------------------|
| Badland | 5% (min 3) |
| Rocky | 2% (min 3) lower slopes only |
| Forest | 1% (min 4) |
| Meadow | 0.5% (min 5) |
| Sea / Crater / Start | 0% |

### Ruins

Density per 256 cells of biome:

| biome | ruin density |
|-------|--------------|
| Badland | 8 |
| Rocky | 3 |
| Meadow | 2 |
| Forest | 1 |
| Sea / Crater / Start + 4-neighbors | 0 |

Each placed ruin picks an entry from `RuinCatalog.json` (starter library: ruined shelter, collapsed wall, broken platform, ruined pipe fragment). Entry specifies footprint, block layout, and blueprint id.

### Blockages

Rubble piles that block pathing until cleared. Count: one per ~3 ruins. Placed near ruins or at narrow chokepoints. **Never** placed on a path that is the sole access route from Start to a required minimum resource (access validation pass verifies this after placement and removes if needed).

### Relics

Collectible items. Count: `round(mapArea / 25000) + 1`. Poisson-disk min spacing 30. Biased toward within 4 cells of a placed ruin (feel: "found in old settlements"). Biomes: Badland primary, Rocky moderate, ruin-adjacent anywhere.

### Unstable cores

Explosive hazards. Count: `round(mapArea / 20000)` ± 1. Min spacing 20. Never within 15 cells of Start. Biomes: Rocky, Badland.

### Geothermal vents

Heat / power source. Count: `round(mapArea / 30000)` ± 1. Min spacing 25. Not within 20 of Start. Prefer visually exposed cells (plateau tops).

## Section 5 — Start Location

### Why a label, not a WFC biome

Getting "exactly 1 Start metacell per map" from WFC requires very low weight (~1%) which has high variance (~37% of maps generate zero). Post-WFC labeling is deterministic and avoids the promote/demote cleanup that would be needed anyway.

### Scoring

After WFC completes (and Crater-edge rewrite), compute a score per metacell:

| signal | score |
|--------|-------|
| Is Meadow | +3 |
| Is Forest | +2 |
| Is Badland | 0 |
| Is Rocky / Sea / Crater | −∞ (disqualified) |
| Any 4-neighbor is Sea | +3 (coastal start) |
| Sea within 2 metacells | +1 |
| Each adjacent Meadow/Forest neighbor | +2 |
| On outer border of the map | −2 |

### Pick

Highest-scoring cell wins (ties broken by seeded RNG). That cell is relabeled "Start." The heightmap apron (§2) applies.

### Start biome overlay rules

- No trees inside the Start metacell (buildability).
- Guaranteed ≥10 edible resources inside the Start metacell, mixed between faction-preferred species.
- No ruins inside Start metacell or within a 1-metacell ring (clean build zone).
- No unstable cores within 15 cells.
- No geothermal vents within 20 cells.
- Hydrology routes at least one river within 3 metacells.

### Start marker entity

Placed at the center voxel of the Start metacell.

## Section 6 — Access Validation + Slope Placement

Runs as the final overlay pass.

### Beaver traversal rules

- Flat same-height edges only.
- Step into water-edge cell OK (drinking / fishing).
- **Can never step up** via terrain.
- Step down: one-way, useless for gathering, ignored in the reachability graph.
- Slope block-objects count as bidirectional edges between their lower and upper endpoints.

### BFS from Start

Flood fill using only the rules above. Collect counts of reachable trees, berries, water cells.

### Minimums (faction-aware)

- ≥ 30 trees total, with ≥ 2 species represented and both factions having woodcutting options.
- ≥ 15 edible resources total, with ≥ 7 usable by each faction (Folktails-primary + Iron Teeth-primary).
- ≥ 1 river or sea water cell on the reachable boundary.

### Repair pass A: top-up (cheap)

If reachable counts are below minimum AND the flat-reachable zone has capacity:
- Run a supplementary Poisson-disk pass restricted to reachable cells.
- Relaxed spacing: trees min 2, resources min 3.
- Plant the specific faction's species that's short (not a random species).
- Only empty cells (no existing overlay) are candidates.

### Repair pass B: slope placement

If the flat-reachable zone is too small for top-up (needs ≥ 200 cells of planting capacity and has less):

1. Find the closest unreachable cluster of the missing resource.
2. Identify the thinnest cliff face between the reachable region and that cluster.
3. Place slope block-object entities along that face, one per voxel of elevation change, oriented to ascend. *(Terrain is not modified — we emit slope assets.)*
4. Re-BFS.
5. Re-run top-up if still short.

### Final restart

If still below minimum after one B-pass attempt: reject map, restart pipeline at `seed+1`.

## Section 7 — Serializer

`MapData` → gzipped JSON `.timber` file. Owned by `MapGen.Core`.

### Approach

1. **Check existing documentation first:** TimberAPI docs (already cloned at `TimberAPI_Docs/`), Mechanistry modding docs, community wiki. Whatever schema info exists, use it.
2. **Harvest reference samples for gaps:** build a minimal test map in the map editor (one of each feature), save, unzip, inspect JSON structure. Document schema in a local reference.
3. **Write serializer in pure C#:** `System.Text.Json` + `System.IO.Compression.GZipStream`. Plain C# DTOs that mirror the JSON shape.
4. **Round-trip validate:** generate → load in Timberborn → confirm it renders and plays.

### Components

- `MapHeader` — size, version, save format version, map seed, generator-version tag.
- `TerrainWriter` — column-based voxel output from `Column[W, H]`.
- `WaterWriter` — river cells + sea/crater fills with depth.
- `EntityWriter` — generic block-object serializer: `(blueprint_id, position, orientation, optional state)`. Used for trees, resources, thorns, ruins, slopes, cores, vents, relics, blockages, water sources, badwater sources, start marker.
- `CatalogLookup` — maps internal names to game blueprint ids. Populated from reference-sample harvest.

### Version handling

On mod load, inspect `Timberborn.WorldPersistence` schema version. Refuse to generate if schema differs from what's been tested. Golden samples kept per game version.

## Section 8 — UI Integration

### Main menu entry

- New "Random Map" button in the New Game flow, adjacent to the map picker.
- On click: dialog with width, height, seed (blank = random), Generate button.
- Progress indicator while running (2–10s typical).
- On success: writes `~/Documents/Timberborn/Maps/<seed>-<WxH>.timber`, auto-advances to faction/difficulty selection with this map pre-selected.

### Map editor entry

- New "Generate" template in the Map Editor's New Map dialog.
- Piggybacks on the editor's existing size fields; adds seed input + Generate button.
- Populates the editor scene directly with generated map data, allowing hand-edits before save.

### Shared

- `GeneratorParamsPanel` VisualElement reused between both dialogs.
- Seed shown on completion; copyable.

### Blueprint resolution on mod load

`BlueprintResolver` resolves catalog entries via `SpecService` at startup. Missing critical ids (slope, start marker, water source) → button disabled, tooltip explains which id is missing.

## Section 9 — Errors & Failure Handling

| failure | stage | response |
|---------|-------|----------|
| WFC contradiction | biome | retry WFC same seed, cap 5 attempts |
| No valid Start candidate | start selection | restart pipeline at `seed+1` |
| Hydrology can't find drain | water | relax source constraint; 3 tries; else next archetype; else restart at `seed+1` |
| Top-up can't hit minimums + slope can't bridge | validation | restart at `seed+1` |
| Serializer write error | serialize | surface to UI; preserve `MapData` for debugging |
| Missing blueprint id on load | startup | disable button, tooltip error |
| Schema version mismatch | startup | disable button, tooltip ("update the mod") |

### Pipeline retry budget

Up to 5 `seed+k` restarts per user request. Beyond that, surface "failed to generate a playable map — try different size or seed."

### Determinism preserved

All retries increment the seed visibly. User ends up playing seed `N+k`; dialog shows which seed actually produced the map.

### Logging

Every stage emits a log line with duration and counts (WFC attempts, river archetype, tree count, etc.).

## Section 10 — Testing Strategy

### Unit tests (`MapGen.Core.Tests`, xUnit)

- WFC: deterministic output for fixed seed + adjacency.
- Heightmap: per-cell heights in-range, borders blend smoothly.
- Hydrology: every river has source and drain; no stranded water.
- Start selection: chosen cell meets constraints.
- Minimums: 100-seed sweep validates reachable counts meet thresholds.
- Serializer: minimal `MapData` round-trips through gzipped JSON.

### Headless preview tool (`MapGen.Core.PreviewTool`)

Console app. Runs the generator, emits PNG: biomes colored, heights shaded, rivers blue, Start marker, overlay dots. Batch mode for 50+ maps → inspect visually. Primary iteration tool for tuning biome weights, heightmap profiles, river archetypes.

### Integration (manual, Timberborn-in-loop)

- 5 golden seeds regenerated after every significant change.
- Each loaded in-game, played ~5 minutes.
- Checks: loads without crash; terrain renders; water flows; beavers path from Start to water/trees/berries; features behave correctly.

### Regression sweep

Script runs headless generator across 5 sizes × 100 seeds (= 500 runs), asserts no crashes / rejected maps / missed minimums within retry budget. Runs in seconds.

## Section 11 — Scope

### In scope for v1

- 6 biomes: Meadow, Forest, Badland, Rocky, Sea, Crater + Start label.
- All features: trees, resources (berries, blueberries, etc.), thorns, mushrooms, ruins, slopes, unstable cores, geothermal vents, relics, blockages.
- Water: archetype-varied rivers, sea/crater fills, seeps, badwater, underground segments.
- Both UI entry points.
- Seeded reproducibility, seed visible in UI.
- **Both Folktails and Iron Teeth catalogs.** Maps are faction-agnostic; minimums enforce both-factions-can-start.

### Deferred to v2

- Player-tunable knobs (more rivers, dry map, ruins density).
- Map preview thumbnail in the New Game dialog.
- Stamp-based biomes (authored ruin/feature chunks composed via WFC sockets — future aesthetic upgrade).
- Advanced river networks (confluences that re-merge, seasonal dynamics).
- Additional biomes (Jungle, Snowy) — content authoring, not core generator work.

### Explicitly not in scope

- Real-time regeneration during play.
- Multiplayer-specific behavior.
- Importing heightmaps from external sources.

## Open implementation questions

These don't change the design but need resolution early:

1. **Slope asset exact id** — user confirmed natural slopes exist in the map editor; DLL string search surfaced "Slope" in `Timberborn.Terraforming` but couldn't confirm the blueprint spec. Look up during implementation.
2. **Relic asset id** — DLL search didn't confirm a "Relic" type by that exact name. May be called "Artifact" or have a faction prefix.
3. **Geothermal vent id** — similarly unconfirmed in the exact spelling.
4. **`.timber` schema version detection** — the specific API on `Timberborn.WorldPersistence` to read the save-format version is TBD during serializer implementation.

These are pinned as lookup tasks for the implementation plan.
