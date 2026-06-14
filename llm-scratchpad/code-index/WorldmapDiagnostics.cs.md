# WorldmapDiagnostics.cs (1443 lines)

Comprehensive world map diagnostics for investigating pathfinding issues.
Triggered by F11 on the world map in debug mode. Runs flood fill, terrain profile,
obstacle census, grid accuracy check, and slope analysis.
namespace: SO2RAccess (line 7)
usings (non-System / notable only): Il2CppGame, UnityEngine

## static class WorldmapDiagnostics (line 15)
Comprehensive world map diagnostics for pathfinding investigation; debug-only, triggered by F11.

fields/properties (declaration order):
- MaxClimbCm : const int (line 17)  — max height difference in cm for passable grid step
- ObstacleSearchRadius : const float (line 18)
- PlayerRadius : const float (line 19)
- ObstacleLayerMask : static readonly int (line 21)  — both wall layers (22 and 23) for diagnostics
- GridTerrainMask : static readonly int (line 24)  — layer 22 only, matches grid generator tier 1
- GridCharaWallMask : static readonly int (line 25)  — layer 23 only, matches grid generator tier 2

methods (declaration order):
- void RunAll(Vector3 playerPos) (line 31)
  - note: Entry point (F11). Runs all 5 sub-analyses in order: FloodFillAnalysis, TerrainProfile, ObstacleCensus, GridAccuracyCheck, SlopeAnalysis. Reads NavigationHandler.LastAutoWalkTarget as the target. Loads cached grid via LoadGrid. Announces summary via ScreenReader.
- void FloodFillAnalysis(WorldmapGridGenerator.CachedGrid grid, Vector3 playerPos, Vector3? target, StringBuilder summary) (line 95)
  - note: BFS from player cell over 8 directions. Classifies border cells as ocean/obstacle/slope/OOB. Checks if target is reachable and finds nearest reachable cell if not. Calls ScanLineBrief for intermediate cell detail.
- void ScanLineBrief(StringBuilder sb, WorldmapGridGenerator.CachedGrid grid, int sx, int sz, int ex, int ez) (line 274)
  - note: Bresenham line scan between two grid cells, logging each cell's ocean/obstacle/slope status. Used by FloodFillAnalysis to explain why the target is unreachable.
- void TerrainProfile(WorldmapGridGenerator.CachedGrid grid, Vector3 playerPos, Vector3 targetPos) (line 321)
  - note: Enhanced terrain profile along Bresenham line from player to target. For each cell: reads cached grid value, calls fresh CalcHeight, runs Physics.OverlapSphere for solid/trigger counts. Detects grid-vs-fresh mismatches. Tracks barrier runs and logs every non-walkable cell or every 40th cell.
- void ObstacleCensus(Vector3 playerPos, float radius) (line 511)
  - note: OverlapSphere with both wall layers around player. Separates triggers (passthrough) from solid (blocking). Logs individual colliders within 5m with full bounds info. Grouped summary by name.
- void GridAccuracyCheck(WorldmapGridGenerator.CachedGrid grid, Vector3 playerPos) (line 591)
  - note: 20x20 grid cell sample around player. For each cell runs fresh CalcHeight + two-tier OverlapSphere (matching grid generator logic). Compares to cached grid value and reports mismatches.
- void SlopeAnalysis(Vector3 playerPos, Vector3 targetPos, StringBuilder summary) (line 714)
  - note: Samples CalcHeight every CellSize meters from player to target. Tracks max slope, counts slopes over 50cm and 100cm, logs top 10 steepest, reports CalcHeight failures.
- void ScanCharaWalls(Vector3 playerPos) (line 816)
  - note: Public entry point (F8). Sweeps layer 23 (CharaWall) colliders along the player-to-target path using 50m radius OverlapSphere steps. Groups by parent, computes group bounding boxes, runs gap analysis projecting wall extents onto path/perp axes, then calls TraceRoutesToGaps.
- void TraceRoutesToGaps(StringBuilder sb, Vector3 playerPos, Vector3 targetPos) (line 1147)
  - note: Traces Bresenham lines from player to several hardcoded gap positions and to the target. For each blocked cell (grid==1) runs layer-separated OverlapSphere (layer 22 vs 23 independently) and reports which layer blocks the approach. Hardcoded gap offsets (along/perp) from a specific recorded session.
- void AddSphereCols(Dictionary<int, Collider> dict, Vector3 center, float radius, int layerMask) (line 1406)
  - note: Deduplicates OverlapSphere results into a dictionary keyed by instance ID.
- WorldmapGridGenerator.CachedGrid LoadGrid(FieldManager fm) (line 1437)

## struct WallSegment (line 1421)
Data for a single CharaWall collider segment.

fields/properties (declaration order):
- Name : string (line 1423)
- Parent : string (line 1424)
- Grandparent : string (line 1425)
- IsTrigger : bool (line 1426)
- ColType : string (line 1427)
- BoundsMin : Vector3 (line 1428)
- BoundsMax : Vector3 (line 1429)
- BoundsCenter : Vector3 (line 1430)
- BoundsSize : Vector3 (line 1431)
- Position : Vector3 (line 1432)
- DistToPlayer : float (line 1433)
