# WorldmapPathfinder.cs (830 lines)

A* pathfinder for the world map using a pre-computed height grid at 0.5m resolution.
Rejects moves between cells where height difference is too steep. Trees (Col_Obstacle)
are passthrough — only terrain geometry blocks. Uses direction-based parent tracking to
save memory on the 0.5m resolution grid.
namespace: SO2RAccess (line 7)
usings (non-System / notable only): Il2CppGame, UnityEngine

## static class WorldmapPathfinder (line 15)
A* pathfinder for the world map using a pre-computed height grid at 0.5m resolution.

fields/properties (declaration order):
- CardinalCost : const float (line 18)  — cost for cardinal movement (1 cell = 0.5m)
- DiagonalCost : const float (line 21)  — cost for diagonal movement
- MaxClimbCm : const int (line 32)  — max height diff in cm between adjacent cells; set high (500cm) because CharaWalls (layer 23) are the real barriers, not slope
- SlopePenaltyStartCm : const int (line 38)  — height diff above which movement gets a cost penalty; steers A* toward flat roads without hard-blocking
- BlockedRadiusCells : const int (line 44)  — radius around stuck position to mark as blocked (4 cells = 2m at 0.5m resolution)
- ComfortableClearance : const float (line 51)  — clearance threshold below which cells get a continuous penalty; above this no penalty
- MaxClearancePenalty : const float (line 61)  — max penalty on tightest passable cells; kept low (3.0) so A* prefers direct routes through gaps
- Directions : static readonly int[,] (line 64)  — 8-directional movement offsets
- _cachedExpel : static WorldmapGridGenerator.CachedGrid (line 69)
- _cachedNede : static WorldmapGridGenerator.CachedGrid (line 70)

methods (declaration order):
- WorldmapGridGenerator.CachedGrid GetCachedGrid(WorldmapID) (line 72)
  - note: lazy-initializes and returns the cached grid for EXPEL or NEDE world map
- void ClearCache() (line 92)
  - note: sets both cached grids to null; call if grid files are regenerated
- Vector3[] FindPath(Vector3, Vector3, List<Vector3>) (line 102)
  - note: main public entry point; converts world positions to grid indices, applies stuck-position blocks, clears small radius around start/end, snaps to terrain, runs AStarSearch, converts path cells to world-space waypoints; returns null on failure; wrapped in try-catch
- List<Vector2Int> AStarSearch(int, int, int, int, ushort[,], int, int, WorldmapGridGenerator.CachedGrid) (line 297)
  - note: private; 8-directional A* with slope check (MaxClimbCm), slope cost penalty (SlopePenaltyStartCm), and continuous clearance penalty (MaxClearancePenalty); uses binary min-heap; direction-based parent tracking saves memory
- void HeapPush(List<(float f, int x, int z)>, (float f, int x, int z)) (line 383)
  - note: standard binary min-heap insert (sift up)
- (float f, int x, int z) HeapPop(List<(float f, int x, int z)>) (line 399)
  - note: standard binary min-heap extract-min (sift down)
- float Heuristic(int, int, int, int) (line 433)
  - note: Euclidean distance heuristic
- List<Vector2Int> ReconstructPath(byte[,], int, int, int, int) (line 440)
  - note: walks parent-direction array from end to start, reverses result; safety break at 500000 steps
- List<Vector3> SimplifyPath(List<Vector3>, List<bool>) (line 466)
  - note: removes collinear waypoints but keeps one at least every 10 cells (5m); never removes clearance-offset waypoints; NOT called in FindPath (raw waypoints are used instead to avoid capsule clipping near obstacles)
- void SnapToTerrain(ref int, ref int, ushort[,], int, int) (line 509)
  - note: BFS-style expanding ring search up to radius 100 to find nearest cell with height >= 2
- Vector2Int? FindNearestReachableToTarget(int, int, int, int, ushort[,], int, int) (line 540)
  - note: BFS from start tracking nearest walkable cell to target; max 1,500,000 visits; early exit within 2 cells; returns null if nothing found
- void RunDiagnostics(Vector3) (line 609)
  - note: public; logs grid size, player grid position, height, and 5x5 height diff table to MelonLogger; announces "Diagnostics complete. Check log."
- void ScanPathLine(Vector3, Vector3) (line 681)
  - note: public; Bresenham line scan between two world positions; logs per-cell walkability, barriers, max slope to MelonLogger; announces summary via ScreenReader
