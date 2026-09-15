# World map fishing spots — brief for the next planning session

> **IMPLEMENTED 2026-09-07 (session 16):** the "bake once, look up at runtime" direction below is
> built — `WorldmapFishingStandBaker` (Insert key in debug mode), `WorldmapFishingStands` (file),
> runtime lookup in `CollectWorldmapFishingSpots`, skill-aware arrival in
> `NavigationHandler.Worldmap.Fishing.cs`. Details and the game API: `docs/game-api.md` §23.
> Answers to the investigation list: the paint is `WorldGridData.FishingWaterPlaceID` (pure data
> lookup, no streaming); `IsWorldmapFishingPoint(point, out hitPosition)` exists;
> `disableFishingPointIDList` is flavour-chat de-dup, not a disable; the Fishing skill IS readable.
> Still open until the first bake runs: bake cost, far-tile behaviour of the game test, and whether
> lake water bakes as no-ground or blocked (phase-1 log line answers it).

Written 2026-09-06 at the end of session 14. The goal, in the user's words: travel to well-defined
fishing spots on the world map; know whether each is reachable without slow scans or loops; one
designated spot per body of water that definitely raises the fishing bubble.

## What we learned today (facts, all from the game log)

- The bubble only appears when the party leader has the Fishing skill. Nothing in the mod can see
  that; several afternoon changes were chasing this before the user found it.
- The game decides the bubble with one function, `FieldManager.CheckWorldmapFishingPoint(ref pos, ref dir)`:
  from the player's feet, look `WorldmapFishingFrontDistance` (5 m) ahead along the facing; that point
  must be painted fishing water (`IsWorldmapFishingPoint(point)`), at most `FishingGroundDistance`
  (7 m) below the player, with a collision check scaled by `FishingCollisionDistanceRate` (1.4).
  `WorldmapFishingCharacterHeight` is 10. The function returns true at real spots (proven 18:45–18:46)
  and is skill-independent, so it is the right verifier. It contains a physics ray, so far from the
  player it may fail open.
- `GetContactFishingWaterPlaceID()` reports the water place whose painted footprint the player stands
  in, even 4 m above the water on a bank (contact 30 at Kurik). Contact alone is not "can fish".
- The world map has NO fishing scene objects. Spots come from `ConstFishingWaterPlaceParameter`
  (position + size = an axis-aligned rectangle per body of water; `IsPlacementFishingSpot` false for
  all seen; 40 spots near Krosse/Kurik). The rectangle is only a bounding box: real lakes are
  irregular, so points 1.5 m outside the rectangle edge are often rock or far from water.
- The current stand search (`ComputeWorldmapFishingStands`) samples the rectangle edge, snaps to
  walkable grid cells, rejects far-bank and too-high cells, then applies the game test. It finds
  stands, but at two lakes (Kurik lake, Arlia lake at about (-38, -396)) every stand only had a
  0.50 m floor-tier route: the walker wedged five times and gave up. Planning those attempts froze the
  game for up to 8 s (three stands × A* over ~1M cells + sweeps). One lake (18:46, stand near
  (12.5, 7.6, 330.5)) worked end to end.
- The walkability grid (`WorldmapGridFormat`, 0.5 m cells, foot/bunny passability, heights,
  clearance) knows shorelines (walkable next to blocked) but NOT which blocked cells are fishing
  water. Only the game's per-point query can tell.

## Proposed direction (to be planned, not decided)

Bake once, look up at runtime.

1. A debug-mode bake pass (like the F9 grid bake) that walks only the grid's shoreline cells and asks
   the game per point whether the adjacent blocked cell is fishing water. Output: definitive stands
   per water place — cell position, facing direction, water place ID, grid clearance at the cell, and
   the connected-region ID for foot and bunny. Saved next to the grid, shipped with the mod.
2. Choose ONE designated stand per body of water (user requirement): the stand with the widest
   clearance route class (comfort tier reachable), preferring positions near the water place's
   parameter position; keep a few alternates in the file but present one in the list.
3. Runtime: the list shows the designated stand, reachable-or-not comes from the region map (an O(1)
   lookup that already exists for towns), distance is to the stand, the beacon sits on the stand,
   auto-walk and directions route to it. No edge sampling, no stand loops, no re-verification scans.
4. On arrival, the game's bubble test is the final truth; if it fails, say so and name the skill.

## Things to investigate in the planning session

- Bake cost: how many shoreline cells Expel has (estimate: hundreds of thousands) and the per-call
  cost of `IsWorldmapFishingPoint`; whether it needs the streamed chunks (it reads painted grid data,
  so probably not) — run it on one lake first and time it.
- Whether `IsWorldmapFishingPoint(point, out hitPosition)` returns the water surface height, which
  would give the height rule without the physics ray.
- How to map a shoreline cell to its water place ID (`GetContactFishingWaterPlaceID(ref point)` on the
  water cell, or the rectangle it falls in).
- Why the shore cells at the two failing lakes only have 0.50 m clearance: is the beach genuinely one
  cell wide, or are water cells baked as blocked too generously (the grid bake's ocean rule is
  "CalcHeight finds no ground")? If the latter, the grid bake itself may need a water-aware pass.
- Whether the designated stand should be chosen by route class at bake time (needs the region map,
  available) or at first use (one A*, cached).
- The game's own `disableFishingPointIDList` (FlavorChatManager) — are some water places disabled?
- Field maps already work (collider per spot); keep them untouched.

## What is done and staying

- Beacons/wall tones/directions on the world map (this session), the game stand test as verifier,
  the shore-height rule using the game's 7 m limit, the list kept across menu closes, the cheap chest
  refresh, "Planning a route" notice, the fixed CalcHeight call for world map wall tones, the audit's
  INCONCLUSIVE verdict.
