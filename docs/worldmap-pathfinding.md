# World Map Pathfinding — Technical Documentation

## Status: IMPLEMENTED AND TESTED (2026-03-07)

This document explains why the world map navigation system uses the approach it does.
It records every alternative that was tested and why each failed, so future maintainers
understand the constraints and don't re-explore dead ends.

---

## 1. The Problem

On field maps (towns, dungeons), the navigation system uses Unity's NavMesh for two things:
- **Reachability filtering**: `NavMesh.CalculatePath()` determines if the player can walk to a target. Unreachable items are hidden from the nav list.
- **Auto-walk routing**: The NavMesh path provides waypoints the player follows to reach the target.

On the world map (Expel/Nede overworld), **there is no NavMesh**. The game uses a custom
A*-based grid pathfinding system instead. This meant:
- Every item picked up by `FindObjectsOfType` appeared in the nav list (50 chests, 46 exits, 8 enemies across the entire map — most behind oceans and mountains)
- Auto-walk moved in a straight line, clipping through terrain and ocean
- Stored waypoints became stale due to coordinate wrapping (see section 5)

---

## 2. The Game's Pathfinding Architecture

### API Chain
```
FieldPlayer -> FieldAIController -> AIParameter<FieldCharacter> -> AIPathFinder<FieldCharacter>
```
The `AIPathFinder` has two pathfinding methods and an inner `AstarNodeManager`:

### AIPathFinder.WorldmapFindPath(ref Vector3 from, ref Vector3 to)
- **CallerCount: 0** — called only from native C++ code, not managed code
- Returns `bool` and populates `pf.routes` (an `Il2CppStructArray<Vector3>`) and `pf.routeCount`
- **Always returns True** for every target tested (nearest chest at 29 units, farthest at 1270 units)
- **Always returns routeCount=1** — a single "next A* step" waypoint, not a full route
- For nearby targets, `routes[0]` equals the target position
- For distant targets, `routes[0]` is an intermediate A* grid node
- This is designed for per-frame NPC movement: call each frame, move toward `routes[0]`, repeat

### AIPathFinder.FindPath(ref Vector3 from, ref Vector3 to, out Vector3 result)
- **CallerCount: 1** — the game's AI uses this for NPC pathfinding
- Higher-level wrapper around AstarNodeManager
- **Also always returns True** — useless for reachability filtering
- The `result` output gives a next-step waypoint similar to WorldmapFindPath

### AstarNodeManager.FindPath(ref Vector3 from, ref Vector3 to, Il2CppStructArray<Vector3> routes, int currentIndex, out int routeCount)
- **CallerCount: 1** — called by AIPathFinder internally
- The full A* grid pathfinder that outputs a complete route
- **Always returns False** for every target (see section 3 for why)

### AstarNodeManager.CheckStraight(ref Vector3 from, ref Vector3 to)
- Line-of-sight check on the A* grid
- **Always returns False** — useless

---

## 3. Why AstarNodeManager.FindPath Always Returns False

The `AstarNodeManager` has a `MaxFindCount` property (default: **30**) that limits the
number of A* iterations per call. This is by design — the game calls it incrementally,
30 iterations per frame, continuing via `RefindPath()` on subsequent frames. It is not
designed for single-call full-path queries.

**We tested boosting MaxFindCount to 5000** before calling FindPath. It still returned
False for every target, including the nearest chest at 29 units. This is because the
grid's internal state (`minMovePosition`, `movePositionScale`, `moveScale`) is volatile
and managed by the game's native code. The grid parameters read as (0,0) on first access
and change to valid values only after the game's own code initializes them. Our external
calls don't trigger this initialization, so the coordinate mapping is broken and the
pathfinder can never find a valid path.

---

## 4. Why Iterative WorldmapFindPath Simulation Failed

Since WorldmapFindPath gives a single next-step per call, we tried simulating a full
walk by calling it in a loop:
```
simPos = playerPos
for each step:
    WorldmapFindPath(simPos, target) -> nextStep = routes[0]
    simPos = nextStep
    if simPos near target -> ARRIVED (reachable)
    if simPos didn't move -> STUCK (unreachable)
```

**Results were wildly inconsistent across runs:**
- Run 1: All targets -> first step jumped to (784.8, 48.8, -353.5), a random far-off point. All STUCK.
- Runs 2-3: All targets -> routes[0] = exact target position (even 1270 units away). All "ARRIVED" in 1 step. Obviously wrong.
- Run 4: Nearest target arrived, distant targets got stuck at nearest target's position.

**Root cause**: WorldmapFindPath only works correctly when called with the **real player
position** as the `from` parameter. When called with an arbitrary simulated position that
doesn't match the actual player transform, the pathfinder's internal state (which is
coupled to the real player's position in the A* grid) produces garbage results.

This makes iterative simulation impossible — the pathfinder is stateful and tied to the
actual player, not a hypothetical position.

---

## 5. World Map Coordinate Wrapping

The world map uses a seamless wrapping system. As the player moves, the game shifts ALL
object transforms via `worldMapLoopTotalTranslate` to create the illusion of an infinite
map. This means:
- A chest's `transform.position` at time T may be completely different at time T+10
- Stored waypoints (Vector3 arrays from a past pathfinding call) become invalid as the
  coordinate space shifts
- `LiveTransform.position` always returns the current wrapped position (still valid)

**Impact on auto-walk**: We cannot store a path as waypoints and follow them over time.
By the time the player reaches waypoint 3, waypoints 4-10 may be in a completely
different coordinate space.

**Solution**: Call WorldmapFindPath every frame with fresh positions. Both `playerPos`
(from `player.transform.position`) and `targetPos` (from `_autoWalkTransform.position`
or `_autoWalkTarget`) are in the current coordinate space each frame.

---

## 6. What Actually Works — The Final Implementation

### 6a. Reachability Filtering: CalcHeight Path Sampling

`GameUtility.CalcHeight(Vector3 position, out bool isSuccess)` casts a ray downward from
above the given position. If there is ground (land), `isSuccess = true` and it returns
the height. If there is no ground (ocean, void), `isSuccess = false`.

We sample CalcHeight at 10 evenly-spaced points along the straight line from the player
to the target. If **any** sample returns `isSuccess = false`, there is ocean between the
player and the target — it is unreachable.

**Test results (consistent across multiple runs):**
- Nearest chest (29-46 units): 0/11 samples failed -> all ground -> REACHABLE
- Mid-range chest (484-489 units): 4/11 failed -> ocean barrier -> FILTERED
- Far chest (847-856 units): 8/11 failed -> mostly ocean -> FILTERED
- Farthest chest (1270-1278 units): 10/11 failed -> almost all ocean -> FILTERED

**Limitations:**
- Does NOT detect mountain barriers (mountains have ground, so CalcHeight succeeds)
- A straight-line check can miss winding land paths (a target reachable by going around
  a bay might be falsely filtered if the straight line crosses water)
- For this reason, CalcHeight filtering is applied ONLY to chests and enemies (which also
  have distance caps), NOT to locations. A location falsely filtered would be permanently
  invisible to a blind player who relies solely on the nav list.

### 6b. Distance Caps

In addition to CalcHeight filtering, the world map applies distance caps:
- Chests: max 200 units (`WorldmapChestMaxDistance`)
- Enemies: max 150 units (`WorldmapEnemyMaxDistance`)

These reduce the 50+ chests across the entire map to a handful of nearby ones. Combined
with CalcHeight, most remaining items are genuinely reachable on foot.

### 6c. Locations Category (No Reachability Filter)

Cities and dungeons use `ConstWorldmapSymbolParameter` from the game's database:
- Filtered by `mapIconType == CITY || DUNGEON`
- Filtered by scenario progress: `startScenarioProgress <= current <= endScenarioProgress`
- Names resolved via: `localityID` -> `GetLocalityParameter()` -> `localityNameID` -> `TextManager.GetMessage()`
- All qualifying locations shown regardless of distance (they are the player's map)

**Why no CalcHeight filter on locations**: A location reachable via a winding coastal
path might have ocean in the straight line between player and target. Filtering it would
make it permanently invisible to a blind player. Showing an unreachable location (where
auto-walk eventually gets stuck and cancels) is far less harmful than hiding a reachable
one. Sighted players can see cities on the horizon and navigate around obstacles; blind
players cannot discover what they cannot see in the nav list.

### 6d. Auto-Walk: Per-Frame WorldmapFindPath

During auto-walk on the world map, every frame:
1. Get fresh `playerPos` from `player.transform.position`
2. Get fresh `targetPos` from `_autoWalkTarget` (updated from LiveTransform if available)
3. Call `pf.WorldmapFindPath(ref playerPos, ref targetPos)`
4. If successful, move toward `pf.routes[0]` (the next A* grid step)
5. If pathfinder unavailable, fall back to moving directly toward the target

This works because WorldmapFindPath produces correct results when called with the real
player position (see section 4). The A* grid navigates around obstacles automatically.

**Stuck detection**: If the player moves less than 2 units over 3 seconds, auto-walk
cancels with "Cannot reach [target]." This is a safety net for cases where the A* grid
can't find a viable path (e.g., the target is across an ocean that CalcHeight didn't
catch, or behind an impassable mountain range).

### 6e. Arrival Radius

World map uses `WorldmapArrivalRadius = 15` units (vs 1.8 for field maps) because world
map location symbols and objects are physically larger than field NPCs/chests.

---

## 7. Summary of All Approaches Tested

- **Unity NavMesh** — N/A: No NavMesh exists on the world map
- **WorldmapFindPath return value** — Always True: Useless for reachability, never returns False
- **AstarNodeManager.FindPath (default MaxFindCount=30)** — Always False: Iteration limit too low for any path
- **AstarNodeManager.FindPath (boosted MaxFindCount=5000)** — Always False: Grid coordinate state not initialized for external calls
- **AIPathFinder.FindPath (3-param)** — Always True: Same as WorldmapFindPath, useless for filtering
- **AstarNodeManager.CheckStraight** — Always False: Useless
- **Iterative WorldmapFindPath simulation** — Inconsistent: Pathfinder is stateful, only works from real player position
- **GameUtility.CheckCollisionHit (raycast)** — Always True, hitDist ~0-6: Hits nearby geometry immediately, useless for long-range
- **GameUtility.CalcHeight path sampling** — **WORKS**: Detects ocean barriers reliably

---

## 8. Files

- `NavigationHandler.Worldmap.cs` — CalcHeight reachability, pathfinder cache, stuck detection
- `NavigationHandler.cs` — World map constants, ScanAndOpenList branching, per-frame auto-walk
- `NavigationHandler.Build.cs` — BuildWorldmapLocations, distance caps in BuildChests/BuildEnemies
- `NavigationHandler.AutoWalk.cs` — IsExitCategory includes CAT_LOCATION, IsReachable delegates to CalcHeight
- `Loc.cs` — `nav_location_dungeon` string

## 9. Game API Paths (verified in decompiled code)

- Pathfinder: `player.FieldAIController.aiParameter.aiPathFinder` -> `.WorldmapFindPath(ref from, ref to)` -> `.routes[0]`
- AstarNodeManager: `aiPathFinder.astarNodeManager` -> `.FindPath(...)`, `.CheckStraight(...)`, `.MaxFindCount`
- Height sampling: `GameUtility.CalcHeight(Vector3 pos, out bool isSuccess)`
- Collision raycast: `GameUtility.CheckCollisionHit(pos, dir, radius, dist, out result, out resultDist)`
- Scenario progress: `ParameterManager.Instance.UserParameter.MainScenarioProgress`
- Location data: `ParameterManager.Instance.GetWorldmapSymbolParameter(WorldmapID)` -> `List<ConstWorldmapSymbolParameter>`
- Locality names: `ParameterManager.Instance.GetLocalityParameter(LocalityID)` -> `.localityNameID` -> `TextManager.Instance.GetMessage(key, MessageType.System)`
- World map detection: `FieldManager.Instance.IsWorldmap()`, `FieldManager.Instance.WorldmapID`

---

## UPDATE: SESSION 2026-03-26 FINDINGS

### ROOT CAUSE CONFIRMED

Town models on the world map (e.g., Wall_Salba) are L22 obstacle rings. The A* was routing
through visual gaps in these rings that the player cannot physically traverse.

Wall_Salba confirmed dimensions: 40 colliders, X=[-174.2,-147.6] Z=[-327.7,-295.1],
size 26.6x32.6m.

### Flood Fill Solution

Flood fill implemented to seal town model interiors — works correctly. Town entrance
triggers (FieldMapjumpCollision) are baked in as passable cells before the fill, creating
safe approach waypoints for town entry.

### CharaWall Boundaries (L23) — New Blocker

CharaWall region boundaries (L23) also block the player. These are large invisible walls
separating world map regions with narrow gaps for passage.

- CharaWall_ArliaSalba: ~80x80m with narrow gaps (0.51m clearance)
- CharaWall_SalvaKrosse: wider gaps (1.6-1.9m clearance)

The A* routes east through ArliaSalba narrow gaps instead of west through SalvaKrosse
wider gaps.

### MinPassableClearance Threshold Testing

Raising MinPassableClearance was tested at multiple values:
- 1.01m: blocks too many corridors
- 0.75m: blocks too many corridors
- 0.55m: still blocks too many corridors — the Krosse-Salva corridor has pinch points that fail even at 0.55m

Reverted to 0.50m for stability.

### Current State

- Flood fill + entrance clearing: WORKING
- Safe exit waypoints when leaving towns: implemented but needs refinement
- Krosse south side TO Salva: WORKS
- Salva northward TO Krosse: FAILS — the pathfinder cannot find a route
- The problem is asymmetric (direction-dependent)

### Known Issue

Need a sighted person to examine the world map terrain around Salva and identify the
viable route from Salva northward to Krosse. The route exists (sighted players use it)
but the pathfinder cannot find it with current grid data. The CharaWall_ArliaSalba
region blocks the eastern approach, and corridor threshold tuning cannot resolve this
without breaking other passages.
