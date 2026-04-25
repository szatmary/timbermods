# MapGen — Home-Base-First Architecture

**Date:** 2026-04-24
**Status:** Draft, ready for review
**Predecessors:** [algorithm + preview](2026-04-24-timberborn-map-generator-design.md), [serializer](2026-04-24-mapgen-serializer-design.md)

## Goal

Replace the current "WFC everywhere → trace river → hope it's playable" pipeline with one that **constructs playability** by anchoring every map around a home-base region with guaranteed water, trees, berries, and a flat district-center spot. WFC and noise still drive variety in the rest of the map.

## Why this rewrite

Current pipeline produces structurally valid `.timber` files (Plan 2 confirmed they load) but maps are not playable: the district center may have no nearby water, river sources may flood the player's start, terrain near the start may have impassable cliffs, etc. Manual play-testing in Timberborn made the gap obvious.

The fix is architectural, not a tuning issue: instead of generating freely and validating, we **anchor** the playable parts of the map (home base + river) and let randomness fill the periphery.

## Pipeline (new)

```
1. Pick home-base location
2. Generate home-base region (terrain + river segment + content + slopes)
3. Pick remote water source (random, elevation ≥ home base)
4. Pick drain edge (random map-edge cell, elevation < home base)
5. Carve connected river: source → home base in-edge → home base out-edge → drain
6. WFC biomes for the remaining metacells (home-base + river-corridor metacells locked)
7. Generate heightmap noise for non-home-base, non-river-corridor cells
8. Place overlays (trees, resources, ruins, hazards) outside the home-base safe buffer
9. Serialize
```

Stage 2 produces a region that's guaranteed-playable by construction. Stage 5 guarantees water flows through it. Stages 6–8 give the rest of the map variety without disturbing the playable zone.

## What we keep from prior plans

- **WFC solver** (`BiomeGrid.cs`) — still used in stage 6, but with locked tiles for the home base and river corridor.
- **Heightmap noise** (`Heightmap.cs`) — still used in stage 7 for non-locked cells.
- **Overlays** (`Overlays.cs`) — still place trees, resources, ruins, hazards outside the home-base buffer.
- **Hydrology helpers** (`Hydrology.cs`) — downhill trace, water entity placement; refactored to be 2-leg (source→home, home→drain) with width parameter.
- **AccessValidation** (`AccessValidation.cs`) — still BFS from district center; now mostly a sanity check rather than a load-bearing repair pass.
- **MapSerializer** (`MapSerializer.cs`) — unchanged, exactly as Plan 2 produced.

## What changes

- `MapGenerator` orchestrator rewritten with the new pipeline.
- New file `HomeBase.cs` — generates the constrained-random home-base region.
- `Hydrology.cs` — refactored from "free trace" to "trace from A to B" used twice (source→home, home→drain), with a configurable width parameter for the home-base segment.
- `StartSelection.cs` — repurposed: picks the home-base CENTER, not a biome metacell.
- `Heightmap.cs` — limited to non-locked cells; the home base owns its own terrain shape.
- `Overlays.cs` — adds a "skip cells inside home-base buffer" rule for ruins, blockages, hazards.

## Section 1 — Home-base location selection

**Goal:** pick a (cx, cy) center for the home base.

**Constraints:**
- Not within 12 voxels of any map edge (room for water + buffer + variety beyond)
- Not in metacells the WFC pre-pass would tag as Sea or Crater (we may not run WFC yet, so use a coarser check: pick from the inner 70% of the map by area)

**Algorithm:**
- Constrained random with seeded RNG. No scoring — just reject candidates that violate the edge-distance constraint, retry up to N=20 times, then expand the bounds.

## Section 2 — Home-base region generation

**Region size:** 32×32 voxels centered on (cx, cy). Larger than the prior draft so the home base can fit a 4×4 district-center spot, a 6×6 farm spot, the 3–6-wide river, and content (trees, berries) without crowding.

### 2.1 Terrain shape

- Base elevation `H_base` = random integer in [4, 8] per map.
- For each cell in the 24×24, height = `H_base ± rand(0, 1)` (so each cell is `H_base`, `H_base + 1`, or `H_base - 1`).
- The 4×4 district-center sub-area is **forced** to `H_base` (perfectly flat).

### 2.2 Slopes

- After terrain shape is decided, scan all adjacent-cell pairs in the home base. Where heights differ by 1, place a `Slope` block-object oriented uphill at the lower cell.
- This guarantees beavers can traverse anywhere within the home base.
- (Height differences > 1 don't happen by construction; we cap variation at ±1.)

### 2.3 Water feature at home base

**Per-map random pick from three variants** (uniform):

- **POND** — sealed water area, filled by a `WaterSeep` (self-regulating, stable level, no overflow).
- **RIVER** — flow-through river (regular `WaterSource` upstream of home base, river carves through, drain edge downstream).
- **BOTH** — pond + flow-through river coexisting in the home base.

**General rules (apply to all variants):**
- Water cells carved 2 voxels deep below `H_base`. Channel bottom at `H_base − 2`. Water depth 2 → surface at `H_base`. Banks at `H_base` are dry; beavers can drink/pump from the bank without falling in. The 2-voxel drop is the "edge."
- District-center 4×4 spot placed ≥3 voxels from nearest water cell.
- Farm 6×6 spot also ≥3 voxels from nearest water cell, can be adjacent to the district center.

**POND-specific:**
- Pond shape: 4×4 to 6×6 contiguous water cells, randomly sized per map.
- Pond placed at a random spot in the home base, ≥4 voxels from the home-base outer edge (so it stays inside) and ≥3 voxels from district-center + farm spots.
- A single `WaterSeep` entity is placed inside the pond at `H_base − 1` (one voxel above the channel bottom) with `SpecifiedStrength = 0.5`. The seep tops up evaporation but cannot overflow.

**RIVER-specific:**
- River enters at one of the four 32-cell home-base edges and exits at another (opposite or perpendicular).
- Width: random per map, in [3, 6] voxels.
- River carving connects to the home base's in-corridor (from leg-1 of the source-to-drain trace) and out-corridor (to leg-2). The home-base river segment is wider than the corridor segments outside (which are 1 voxel wide).

**BOTH-specific:**
- Pond + river both present, placed independently with the same constraints.
- Pond uses a seep; river uses a regular `WaterSource` upstream as in RIVER mode.
- District + farm spots stay ≥3 voxels from EITHER water feature.

### 2.4 District center spot

- 4×4 contiguous flat cells at `H_base`, ≥3 voxels from any water cell (pond or river depending on variant).
- A `StartingLocation` entity is placed at the center.

### 2.4b Farm spot

- 6×6 contiguous flat cells at `H_base`, ≥3 voxels from any water cell, may be adjacent to the district-center spot.
- No entity placed; the player builds the farm. The spot is just reserved (excluded from tree / berry / overlay placement).

### 2.5 Content (trees / berries)

- **Trees**: random count in [40, 80]. Poisson-disk sampled within the 32×32 region. Skip:
  - District-center spot + 1-cell buffer around it
  - Farm spot + 1-cell buffer around it
  - Water cells (pond and/or river)
  - Slope cells
- **Berries (BlueberryBush)**: random count in [15, 25]. Same exclusions as trees.

### 2.6 Safe buffer (no hazards)

- A 3-voxel ring around the home-base 24×24 is marked as "buffer". Stages 8 (overlays) skip placing thorns, blockages, unstable cores, geothermal vents, ruins inside this buffer.

### 2.7 Locked metacells

- The 32×32 voxels span 4×4 metacells (8×8 each). Mark these 16 metacells as "locked" so WFC in stage 6 doesn't try to place biomes there. After WFC, hand-tag the 16 metacells as `Biome.Meadow` (or a special `Biome.HomeBase` if we want differentiated heightmap noise — but Meadow works fine).

## Section 3 — Remote water source (only for RIVER and BOTH variants)

Skip this entire stage if the home-base water variant is POND.

- Random voxel coord in non-home-base cells.
- Constraint: elevation ≥ `H_base + 4` (must be uphill so river can flow down to the home-base entry).
- If no candidate found in 50 random tries, expand to ≥ `H_base + 2`. If still none, accept any non-home-base cell with elevation ≥ `H_base` (flat-ish source).
- Source cell becomes the start of leg 1 of the river trace.

## Section 4 — Drain edge (only for RIVER and BOTH variants)

Skip this entire stage if the home-base water variant is POND.

- Pick a random map-edge cell with elevation ≤ `H_base − 1` (downhill from the home base).
- If no such edge cell exists (rare, only on extremely flat noise), accept any edge cell.
- Drain cell becomes the end of leg 2.

## Section 5 — Two-leg river carving (only for RIVER and BOTH variants)

Skip this entire stage if the home-base water variant is POND.

- **Leg 1: source → home-base in-edge.**
  - Use refactored `Hydrology.TraceFromTo(start, end)` from source toward the home-base entry voxel.
  - If trace gets stuck (basin), retry from a different source up to 5 times.
- **Leg 2: home-base out-edge → drain edge.**
  - Same trace from home-base out-edge to drain.
  - Same retry budget.

- Both legs carved 1-voxel wide outside the home base (narrower than the home-base segment, but still a river). Carve depth 2 → channel bottom = local-bank − 2, water surface = local-bank.
- After carving: place the source `WaterSource` entity at the source voxel. Flow rate: `0.6f` (generous but not overflowing).
- If both legs succeed for the chosen home-base water variant (RIVER or BOTH), we have a complete source→home→drain path.

## Section 5b — POND seep entity placement

For the POND and BOTH variants only.

- A single `WaterSeep` entity placed inside the pond at `H_base − 1` (one voxel above channel bottom).
- `SpecifiedStrength = 0.5`. Self-regulating; tops up evaporation only.

## Section 6 — WFC biomes for non-locked metacells

- Run the existing `BiomeGrid.Solve` but with the 3×3 home-base metacells PRE-COLLAPSED to `Meadow`. The WFC propagates outward respecting that constraint.
- Crater-edge rewrite still applies.

## Section 7 — Heightmap for non-locked cells

- Run existing `Heightmap.Build` BUT skip cells whose metacell is locked OR is on the river corridor (single voxel-wide cells along the leg-1 + leg-2 paths).
- For locked cells: their heights are already set by stage 2.
- For river-corridor cells: their heights are already set by leg-1 / leg-2 carving.

## Section 8 — Overlays outside the home base

- Existing `Overlays.PlaceTrees`, `PlaceResources`, etc. — modified to skip cells inside the home-base 24×24 region OR its 3-voxel buffer.
- Trees / berries placed inside the home base were handled by stage 2.5; overlays handle the rest of the map.
- Ruins, blockages, hazards: skip if cell is inside `home_base + buffer`.

## Section 9 — Final access validation

- BFS from the district-center voxel. Confirm trees ≥ 30, berries ≥ 15, water reachable.
- These should ALWAYS pass given stage 2's content guarantees. If not, the home-base content guarantees were violated — restart pipeline at seed+1.
- The expensive top-up + slope-place repair passes from Plan 1 are no longer needed (they exist to compensate for the broken old pipeline).

## Out of scope (deferred)

- **Multiple home-base templates** — for v1 we have one shape (24×24 flat with river through). Expand to a small library later.
- **District scoring** — no scoring or weighting; pick is uniform random within constraints.
- **Multi-river systems** — pipeline supports exactly one river (source → home base → drain). Delta branches and multiple sources removed for simplicity (can re-add later).
- **Underground river segments** — kept available as an optional roll outside the home base, but not on the home-base segment itself.

## Test strategy

- Unit tests for `HomeBase.Generate` — assert content guarantees (4×4 flat zone, river width in [3,6], trees in [40,80], berries in [15,25]).
- Unit tests for `Hydrology.TraceFromTo` (refactored) — given start + end, returns a connected downhill path or null.
- Integration: 20-seed sweep through `MapGenerator.Generate` — assert all succeed and produce valid `MapData`.
- Manual: load 3 generated maps in Timberborn, place a District Center on the StartingLocation cell, build a Water Pump on the river bank, confirm water flows.

## Implementation cost estimate

Smaller than Plan 1 because most components are reusable:
- `HomeBase.cs` — new file, ~200 lines.
- `Hydrology.cs` — refactor `TraceDownhill` to `TraceFromTo(start, end)`, add 2-leg carving.
- `MapGenerator.cs` — rewrite the orchestrator (mostly reordering).
- `Overlays.cs` — add home-base-skip predicate.
- `Heightmap.cs` — add skip-locked-cells predicate.
- `BiomeGrid.cs` — add pre-collapse parameter (locked metacells with fixed biome).
- New tests for the constrained guarantees.

Probably 8-12 implementation tasks.
