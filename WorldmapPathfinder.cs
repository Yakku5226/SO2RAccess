using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// A* pathfinder for the world map using a pre-computed height grid
    /// at 0.5m resolution. Rejects moves between cells where the height
    /// difference is too steep (rock faces, cliffs). Trees (Col_Obstacle)
    /// are passthrough on the world map — only terrain geometry blocks.
    ///
    /// Performance design (2026-07-05): search buffers are allocated ONCE
    /// and reused across searches via a generation counter, the grid is
    /// mutated in place with an undo journal instead of being cloned per
    /// call, and a precomputed connected-region map answers "is there any
    /// route at all?" instantly — so unreachable targets fail in
    /// microseconds instead of after a full-map search.
    /// </summary>
    public static class WorldmapPathfinder
    {
        /// <summary>Cost for cardinal movement (1 cell = 0.5m).</summary>
        private const float CardinalCost = 1.0f;

        /// <summary>Cost for diagonal movement.</summary>
        private const float DiagonalCost = 1.414f;

        /// <summary>
        /// Max height difference in centimeters between adjacent cells.
        /// At 0.5m cell spacing, this controls max climbable slope.
        /// Set high (500cm) because the world map uses CharaWalls (layer 23)
        /// for movement barriers, not terrain slope. The player can walk
        /// up steep world map terrain — only CharaWalls physically block.
        /// The slope penalty (SlopePenaltyStartCm) still steers the A*
        /// toward flat roads without hard-blocking steep paths.
        /// </summary>
        private const int MaxClimbCm = 500;

        /// <summary>
        /// Height difference above which movement gets a cost penalty.
        /// Encourages A* to prefer flat paths (roads) over slopes.
        /// </summary>
        private const int SlopePenaltyStartCm = 30;

        /// <summary>
        /// Radius around a stuck position to mark as blocked (in grid cells).
        /// At 0.5m resolution, 4 cells = 2m radius.
        /// </summary>
        private const int BlockedRadiusCells = 4;

        /// <summary>
        /// Clearance threshold below which cells receive a continuous
        /// penalty. Cells with clearance at or above this value get no
        /// penalty. Based on: comfortable passage = 2x player radius.
        /// </summary>
        private const float ComfortableClearance = 1.5f;

        /// <summary>
        /// Maximum penalty applied to the tightest passable cells
        /// (those just above the hard minimum). The penalty scales
        /// linearly: tighter cells get higher penalties. Kept low
        /// (3.0) so A* prefers direct routes through gaps — the
        /// real-time wall avoidance in NavigationHandler handles
        /// steering through tight passages safely.
        /// </summary>
        private const float MaxClearancePenalty = 3.0f;

        /// <summary>
        /// Preferred minimum clearance (meters) for the first pathfinding
        /// pass. The grid bakes a hard 0.50m floor at generation time, which
        /// equals the player's capsule radius — gaps that tight wedge the
        /// player (they cannot fit even when aimed dead-center). We first
        /// search for a route where every cell has at least this much
        /// clearance (a real safety margin). Only if NO such route exists do
        /// we fall back to the 0.50m-floor route. This steers the player onto
        /// wider roads when one exists, WITHOUT ever making a destination less
        /// reachable than before: the fallback pass is identical to the
        /// original behavior.
        /// </summary>
        private const float PreferredMinClearance = 0.60f;

        /// <summary>
        /// Expansion cap for the FIRST (preferred-clearance) pass only.
        /// If no wide-clearance route exists, that pass would otherwise
        /// sweep the entire landmass before giving up. The preferred pass
        /// is purely a comfort optimization, so it is allowed to give up
        /// early; the authoritative 0.50m-floor pass runs uncapped and
        /// reachability is never affected.
        /// </summary>
        private const int PreferredPassMaxExpansions = 1_500_000;

        /// <summary>Radius (cells) of the start-area clearing, 3m at 0.5m cells.</summary>
        private const int StartClearRadiusCells = 6;

        /// <summary>8-directional movement offsets.</summary>
        private static readonly int[,] Directions = {
            { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 },
            { 1, 1 }, { 1, -1 }, { -1, -1 }, { -1, 1 }
        };

        /// <summary>
        /// True when the most recent FindPath returned null because the start
        /// and target are in different connected regions of the grid — i.e.
        /// there is NO walkable overland route at all (as opposed to a
        /// transient failure). Callers can announce a more helpful message.
        /// </summary>
        public static bool LastNoPathWasDisconnected { get; private set; }

        private static WorldmapGridGenerator.CachedGrid _cachedExpel;
        private static WorldmapGridGenerator.CachedGrid _cachedNede;

        // --- Persistent A* buffers, reused across searches -----------------
        // Sized to gridW*gridH on first use (both world maps share the same
        // fixed bounds, so one set serves both). Validity per search is
        // tracked with a generation counter instead of re-initializing 37M
        // cells per call: a cell's _gCost/_parentDir are only meaningful when
        // _state[i] belongs to the current generation.
        //   _state[i] == 2*gen   → cell visited (open), _gCost valid
        //   _state[i] == 2*gen+1 → cell closed
        private static float[] _gCost;
        private static ushort[] _state;
        private static byte[] _parentDir;
        private static int _bufLen;
        private static ushort _gen;
        private static readonly List<(float f, int x, int z)> _heap
            = new List<(float f, int x, int z)>();

        private static WorldmapGridGenerator.CachedGrid GetCachedGrid(
            WorldmapID wmID)
        {
            if (wmID == WorldmapID.EXPEL)
            {
                if (_cachedExpel == null)
                {
                    _cachedExpel = WorldmapGridGenerator.LoadGrid(
                        WorldmapID.EXPEL);
                    if (_cachedExpel != null) BuildRegions(_cachedExpel);
                }
                return _cachedExpel;
            }
            else
            {
                if (_cachedNede == null)
                {
                    _cachedNede = WorldmapGridGenerator.LoadGrid(
                        WorldmapID.NEDE);
                    if (_cachedNede != null) BuildRegions(_cachedNede);
                }
                return _cachedNede;
            }
        }

        /// <summary>Clears cached grids (call if grid files are regenerated).</summary>
        public static void ClearCache()
        {
            _cachedExpel = null;
            _cachedNede = null;
        }

        /// <summary>
        /// True if the given world XZ lands on a walkable grid cell — real
        /// terrain/road (<c>Height &gt;= 2</c>), not ocean/void (0) or a baked
        /// obstacle/wall (1). Mirrors the A* fallback floor exactly, so a cell
        /// this reports walkable is one the pathfinder can stand on. Returns
        /// false if no grid is cached (caller should treat as "unknown").
        /// Used to pick an entrance-ring point that is NOT buried in a wall.
        /// </summary>
        public static bool IsWalkableWorld(Vector3 world)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return false;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null) return false;
                grid.WorldToGrid(world.x, world.z, out int ax, out int az);
                if (ax < 0 || ax >= grid.GridW || az < 0 || az >= grid.GridH)
                    return false;
                return grid.Height[ax, az] >= 2;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM IsWalkableWorld error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Connected-region id of the grid cell at a world position, or 0 when
        /// unknown (no grid/regions cached, out of bounds, or a non-walkable
        /// cell). Two positions with the same non-zero id are connected for
        /// the on-foot A* — used to pick an entrance point on the PLAYER'S
        /// side of a boundary town (e.g. Salva, which has a Krosse-side and
        /// an Arlia-valley-side entrance).
        /// </summary>
        public static int GetRegionId(Vector3 world)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return 0;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null || grid.Regions == null) return 0;
                grid.WorldToGrid(world.x, world.z, out int ax, out int az);
                if (ax < 0 || ax >= grid.GridW || az < 0 || az >= grid.GridH)
                    return 0;
                return grid.Regions[ax * grid.GridH + az];
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM GetRegionId error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Finds a path on the world map using the pre-computed height grid.
        /// Returns world-space waypoints or null if no path exists.
        /// </summary>
        public static Vector3[] FindPath(Vector3 start, Vector3 end,
            List<Vector3> blockedPositions = null)
        {
            LastNoPathWasDisconnected = false;

            var fm = FieldManager.Instance;
            if (fm == null || !fm.IsExistWorldGridData())
            {
                DebugLogger.LogState(
                    "NAV WM pathfinder: WorldGridData not available.");
                return null;
            }

            var grid = GetCachedGrid(fm.WorldmapID);
            if (grid == null)
            {
                DebugLogger.LogState(
                    "NAV WM pathfinder: no cached grid. " +
                    "Press F9 in debug mode on the world map to generate.");
                ScreenReader.Say(
                    "World map grid not found. Enable debug mode " +
                    "with F12, then press F9 to generate the map grid.");
                return null;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int gridW = grid.GridW;
            int gridH = grid.GridH;
            var height = grid.Height;

            // Undo journal: the grid is mutated in place (blocked stamps +
            // start clearing) and restored in the finally block. This
            // replaces the old full-grid clone (75MB + 37M-cell init per
            // call), which was the main cause of the multi-second freeze
            // at auto-walk start.
            var journal = new List<(int x, int z, ushort old)>();
            void SetCell(int x, int z, ushort v)
            {
                journal.Add((x, z, height[x, z]));
                height[x, z] = v;
            }

            try
            {
                // Convert world positions to grid indices.
                grid.WorldToGrid(start.x, start.z,
                    out int startAx, out int startAz);
                grid.WorldToGrid(end.x, end.z,
                    out int endAx, out int endAz);

                startAx = Mathf.Clamp(startAx, 0, gridW - 1);
                startAz = Mathf.Clamp(startAz, 0, gridH - 1);
                endAx = Mathf.Clamp(endAx, 0, gridW - 1);
                endAz = Mathf.Clamp(endAz, 0, gridH - 1);
                int origStartAx = startAx, origStartAz = startAz;

                // Apply stuck-position blocks (set to ocean/0).
                if (blockedPositions != null)
                {
                    for (int b = 0; b < blockedPositions.Count; b++)
                    {
                        Vector3 bp = blockedPositions[b];
                        grid.WorldToGrid(bp.x, bp.z,
                            out int bax, out int baz);

                        for (int dx = -BlockedRadiusCells;
                            dx <= BlockedRadiusCells; dx++)
                        {
                            for (int dz = -BlockedRadiusCells;
                                dz <= BlockedRadiusCells; dz++)
                            {
                                int nx = bax + dx;
                                int nz = baz + dz;
                                if (nx >= 0 && nx < gridW &&
                                    nz >= 0 && nz < gridH &&
                                    dx * dx + dz * dz <=
                                    BlockedRadiusCells * BlockedRadiusCells &&
                                    height[nx, nz] != 0)
                                {
                                    SetCell(nx, nz, 0);
                                }
                            }
                        }
                    }
                }

                // Clear a small area around the START ONLY so the player's
                // immediate cell is passable (the player may stand on a cell the
                // grid marks tight/obstacle). We deliberately do NOT clear cells
                // around the destination: the old 10m end-clearance punched a hole
                // straight through a town/dungeon model's wall so the A* could reach
                // the centre point — that is exactly the "routes through the wall"
                // behaviour we want gone. Walls stay fully impassable; the caller
                // now targets the navigable enter-trigger ring, and SnapToTerrain
                // pulls the endpoint to the nearest passable cell if needed.
                {
                    int cx = startAx;
                    int cz = startAz;
                    ushort centerH = height[cx, cz] >= 2
                        ? height[cx, cz] : (ushort)12080;
                    for (int dx = -StartClearRadiusCells;
                        dx <= StartClearRadiusCells; dx++)
                    {
                        for (int dz = -StartClearRadiusCells;
                            dz <= StartClearRadiusCells; dz++)
                        {
                            if (dx * dx + dz * dz >
                                StartClearRadiusCells * StartClearRadiusCells)
                                continue;
                            int nx = cx + dx;
                            int nz = cz + dz;
                            if (nx >= 0 && nx < gridW && nz >= 0 && nz < gridH
                                && height[nx, nz] == 1)
                            {
                                SetCell(nx, nz, centerH);
                            }
                        }
                    }
                }

                // Snap start/end to nearest terrain cell.
                if (height[startAx, startAz] < 2)
                    SnapToTerrain(ref startAx, ref startAz,
                        height, gridW, gridH);
                if (height[endAx, endAz] < 2)
                    SnapToTerrain(ref endAx, ref endAz,
                        height, gridW, gridH);

                if (height[startAx, startAz] < 2 ||
                    height[endAx, endAz] < 2)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: start or end not on terrain. " +
                        $"grid={gridW}x{gridH} start=({startAx},{startAz}) " +
                        $"end=({endAx},{endAz})");
                    return null;
                }

                // --- Connected-region fast reject ---
                // If the target's region differs from every region touching
                // the start (including the cleared 3m disc, whose cells can
                // bridge the player out of a baked-obstacle pocket), then NO
                // route exists and a full search would just sweep the whole
                // landmass before saying so. Region 0 = unknown → never
                // reject (fail open); blocked stamps only REMOVE connectivity,
                // so this check can never reject a genuinely reachable pair.
                if (grid.Regions != null)
                {
                    ushort endRegion = grid.Regions[endAx * gridH + endAz];
                    if (endRegion != 0 &&
                        !StartTouchesRegion(grid, origStartAx, origStartAz,
                            startAx, startAz, endRegion))
                    {
                        LastNoPathWasDisconnected = true;
                        DebugLogger.LogState(
                            $"NAV WM pathfinder: start and target are in " +
                            $"different connected regions (target region " +
                            $"{endRegion}) — no overland route exists. " +
                            $"Rejected in {sw.ElapsedMilliseconds}ms.");
                        return null;
                    }
                }

                // Tiered A* search: first try a route where every cell has a
                // real clearance margin (PreferredMinClearance) so the player
                // is never threaded through a body-width gap. Only if no such
                // route exists do we fall back to the grid's baked 0.50m floor
                // (the original behavior). This prefers wide roads when one
                // exists but never removes a reachable destination. The first
                // pass is expansion-capped: it is a comfort preference, not
                // the authority on reachability.
                var path = AStarSearch(startAx, startAz, endAx, endAz,
                    grid, PreferredMinClearance, PreferredPassMaxExpansions,
                    out int expansions1);

                int expansions2 = 0;
                if (path == null)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: no route at " +
                        $"{PreferredMinClearance:F2}m clearance " +
                        $"({expansions1} cells searched) — falling back " +
                        $"to the 0.50m floor.");
                    path = AStarSearch(startAx, startAz, endAx, endAz,
                        grid, 0f, 0, out expansions2);
                }

                if (path == null)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: no path found. " +
                        $"grid={gridW}x{gridH} " +
                        $"start=({startAx},{startAz}) " +
                        $"end=({endAx},{endAz}) " +
                        $"maxClimb={MaxClimbCm}cm " +
                        $"searched={expansions1 + expansions2} cells " +
                        $"in {sw.ElapsedMilliseconds}ms.");
                    return null;
                }

                // Convert path to world-space waypoints. Ground Y comes from
                // the grid's baked heights (the old per-waypoint CalcHeight
                // raycast added hundreds of physics casts per path for a Y
                // value the stick-injection follower never uses). Uses
                // clearance-adjusted positions for cells near CharaWalls so
                // the player walks through the exact center of narrow gaps.
                var waypoints = new List<Vector3>(path.Count);
                foreach (var cell in path)
                {
                    Vector3 wp = grid.GridToWorldWithClearance(cell.x, cell.y);
                    ushort hv = height[cell.x, cell.y];
                    wp.y = hv >= 2 ? (hv / 100f) - 100f : start.y;
                    waypoints.Add(wp);
                }

                // Use raw A* waypoints (0.5m apart) without simplification:
                // path simplification cut long straight segments through
                // obstacle-adjacent airspace, clipping the player's 0.51m
                // capsule on L22 rocks. Raw waypoints keep the player on
                // verified passable cells; native Physics2 handles micro-collisions.
                if (waypoints.Count > 0)
                    waypoints[waypoints.Count - 1] = end;
                else
                    waypoints.Add(end);

                DebugLogger.LogState(
                    $"NAV WM pathfinder: found path with " +
                    $"{waypoints.Count} waypoints in {sw.ElapsedMilliseconds}ms " +
                    $"(searched {expansions1 + expansions2} cells, " +
                    $"maxClimb={MaxClimbCm}cm).");

                return waypoints.ToArray();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM pathfinder error: {ex.Message}");
                return null;
            }
            finally
            {
                // Restore all in-place grid mutations (reverse order).
                for (int i = journal.Count - 1; i >= 0; i--)
                    height[journal[i].x, journal[i].z] = journal[i].old;

                // Don't let a pathological search pin hundreds of MB of
                // heap capacity forever.
                if (_heap.Capacity > 2_000_000)
                {
                    _heap.Clear();
                    _heap.TrimExcess();
                }
            }
        }

        #region Connected Regions

        /// <summary>
        /// Labels every walkable cell with a connected-region number using
        /// the exact same neighbor rule as the authoritative A* pass
        /// (8-directional, both cells walkable, height step ≤ MaxClimbCm).
        /// Two cells share a region if and only if the 0.50m-floor A* could
        /// route between them, so region equality is a sound instant
        /// "any route at all?" test. Runs once per grid load (~1-2s);
        /// region 0 means unlabeled (ocean, obstacle, or label overflow)
        /// and is always treated as "unknown — do not reject".
        /// </summary>
        private static void BuildRegions(WorldmapGridGenerator.CachedGrid grid)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int gridW = grid.GridW;
                int gridH = grid.GridH;
                var height = grid.Height;
                var regions = new ushort[(long)gridW * gridH];
                var queue = new Queue<int>();
                ushort nextLabel = 0;
                bool overflow = false;

                for (int ax = 0; ax < gridW && !overflow; ax++)
                {
                    for (int az = 0; az < gridH; az++)
                    {
                        if (height[ax, az] < 2) continue;
                        int rootIdx = ax * gridH + az;
                        if (regions[rootIdx] != 0) continue;

                        if (nextLabel == ushort.MaxValue)
                        {
                            overflow = true;
                            break;
                        }
                        nextLabel++;

                        regions[rootIdx] = nextLabel;
                        queue.Enqueue(rootIdx);
                        while (queue.Count > 0)
                        {
                            int idx = queue.Dequeue();
                            int cx = idx / gridH;
                            int cz = idx % gridH;
                            ushort ch = height[cx, cz];

                            for (int d = 0; d < 8; d++)
                            {
                                int nx = cx + Directions[d, 0];
                                int nz = cz + Directions[d, 1];
                                if (nx < 0 || nx >= gridW ||
                                    nz < 0 || nz >= gridH) continue;
                                int nIdx = nx * gridH + nz;
                                if (regions[nIdx] != 0) continue;
                                ushort nh = height[nx, nz];
                                if (nh < 2) continue;
                                if (Math.Abs(ch - nh) > MaxClimbCm) continue;
                                regions[nIdx] = nextLabel;
                                queue.Enqueue(nIdx);
                            }
                        }
                    }
                }

                grid.Regions = regions;
                DebugLogger.LogState(
                    $"NAV WM regions: {nextLabel} connected regions labeled " +
                    $"in {sw.ElapsedMilliseconds}ms" +
                    (overflow ? " (label overflow — remainder unlabeled)" : "") +
                    ".");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM regions error: {ex.Message}");
                grid.Regions = null; // fail open — no fast reject
            }
        }

        /// <summary>
        /// True if the snapped start cell, or ANY base-grid cell within the
        /// start-clearing disc (+1 cell rim), belongs to
        /// <paramref name="targetRegion"/>. The start clearing converts
        /// obstacle cells inside the disc to passable, which can bridge the
        /// player onto any region the disc touches — so all of them count
        /// as reachable start regions.
        /// </summary>
        private static bool StartTouchesRegion(
            WorldmapGridGenerator.CachedGrid grid,
            int origAx, int origAz, int snapAx, int snapAz,
            ushort targetRegion)
        {
            int gridH = grid.GridH;
            var regions = grid.Regions;

            ushort sr = regions[snapAx * gridH + snapAz];
            if (sr == 0) return true; // unknown — fail open
            if (sr == targetRegion) return true;

            int rim = StartClearRadiusCells + 1;
            for (int dx = -rim; dx <= rim; dx++)
            {
                for (int dz = -rim; dz <= rim; dz++)
                {
                    if (dx * dx + dz * dz > rim * rim) continue;
                    int nx = origAx + dx;
                    int nz = origAz + dz;
                    if (nx < 0 || nx >= grid.GridW ||
                        nz < 0 || nz >= grid.GridH) continue;
                    if (regions[nx * gridH + nz] == targetRegion)
                        return true;
                }
            }
            return false;
        }

        #endregion

        #region A* with Binary Heap

        /// <summary>
        /// Ensures the persistent search buffers exist and advances the
        /// search generation. On generation wrap the state array is cleared
        /// once (rare) so stale generations can never alias.
        /// </summary>
        private static ushort BeginSearch(int len)
        {
            if (_gCost == null || _bufLen < len)
            {
                _gCost = new float[len];
                _state = new ushort[len];
                _parentDir = new byte[len];
                _bufLen = len;
                _gen = 0;
            }
            if (_gen >= (ushort.MaxValue - 2) / 2)
            {
                Array.Clear(_state, 0, _bufLen);
                _gen = 0;
            }
            _gen++;
            _heap.Clear();
            return _gen;
        }

        /// <summary>
        /// A* search with slope checking and wall proximity penalty.
        /// Reads the (temporarily mutated) grid heights directly and uses
        /// the persistent generation-stamped buffers — no per-call
        /// allocation or full-grid initialization. Cells near walls (with
        /// clearance offsets) get a higher movement cost so the A* prefers
        /// wide corridors.
        /// </summary>
        /// <param name="maxExpansions">Abort after this many cell expansions
        /// (0 = unlimited). Used only by the preferred-clearance pass.</param>
        private static List<Vector2Int> AStarSearch(
            int sx, int sz, int ex, int ez,
            WorldmapGridGenerator.CachedGrid grid,
            float minClearance, int maxExpansions, out int expansions)
        {
            int gridW = grid.GridW;
            int gridH = grid.GridH;
            var height = grid.Height;

            ushort gen = BeginSearch(gridW * gridH);
            int open = gen * 2;
            int closedV = gen * 2 + 1;
            expansions = 0;

            int sIdx = sx * gridH + sz;
            _gCost[sIdx] = 0f;
            _state[sIdx] = (ushort)open;
            _parentDir[sIdx] = 255;
            HeapPush(_heap, (Heuristic(sx, sz, ex, ez), sx, sz));

            while (_heap.Count > 0)
            {
                var (_, cx, cz) = HeapPop(_heap);
                int cIdx = cx * gridH + cz;

                if (_state[cIdx] == closedV) continue;
                _state[cIdx] = (ushort)closedV;

                if (cx == ex && cz == ez)
                    return ReconstructPath(gridH, sx, sz, ex, ez);

                expansions++;
                if (maxExpansions > 0 && expansions > maxExpansions)
                {
                    DebugLogger.LogState(
                        $"NAV WM A*: preferred pass hit expansion cap " +
                        $"({maxExpansions}) — deferring to the floor pass.");
                    return null;
                }

                ushort currentH = height[cx, cz];
                float currentG = _gCost[cIdx];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Directions[d, 0];
                    int nz = cz + Directions[d, 1];

                    if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridH)
                        continue;
                    int nIdx = nx * gridH + nz;
                    if (_state[nIdx] == closedV) continue;

                    ushort neighborH = height[nx, nz];
                    if (neighborH < 2) continue; // Ocean (0) or obstacle (1).

                    // Slope check.
                    int heightDiff = Math.Abs(currentH - neighborH);
                    if (heightDiff > MaxClimbCm) continue; // Too steep.

                    float moveCost = d < 4 ? CardinalCost : DiagonalCost;

                    // Slope penalty: prefer flat paths (roads).
                    if (heightDiff > SlopePenaltyStartCm)
                        moveCost += heightDiff * 0.02f;

                    // Continuous clearance penalty: prefer wide corridors.
                    // Penalty scales linearly from MaxClearancePenalty at
                    // minimum clearance (0.55m) down to 0 at ComfortableClearance.
                    float clr = grid.GetClearance(nx, nz);

                    // Hard clearance floor (first pass only). Cells too
                    // narrow for the player to fit through are skipped
                    // entirely. minClearance == 0 in the fallback pass
                    // disables this, preserving original reachability.
                    if (minClearance > 0f && clr < minClearance) continue;

                    if (clr < ComfortableClearance)
                    {
                        float ratio = (ComfortableClearance - clr) /
                            (ComfortableClearance - 0.55f);
                        if (ratio > 1f) ratio = 1f;
                        moveCost += ratio * MaxClearancePenalty;
                    }

                    float newG = currentG + moveCost;

                    if (_state[nIdx] != open || newG < _gCost[nIdx])
                    {
                        _gCost[nIdx] = newG;
                        _state[nIdx] = (ushort)open;
                        _parentDir[nIdx] = (byte)d;

                        float f = newG + Heuristic(nx, nz, ex, ez);
                        HeapPush(_heap, (f, nx, nz));
                    }
                }
            }

            return null;
        }

        private static void HeapPush(List<(float f, int x, int z)> heap,
            (float f, int x, int z) item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].f <= heap[i].f) break;
                var tmp = heap[parent];
                heap[parent] = heap[i];
                heap[i] = tmp;
                i = parent;
            }
        }

        private static (float f, int x, int z) HeapPop(
            List<(float f, int x, int z)> heap)
        {
            var min = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            last--;

            int i = 0;
            while (true)
            {
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                int smallest = i;

                if (left <= last && heap[left].f < heap[smallest].f)
                    smallest = left;
                if (right <= last && heap[right].f < heap[smallest].f)
                    smallest = right;

                if (smallest == i) break;

                var tmp = heap[i];
                heap[i] = heap[smallest];
                heap[smallest] = tmp;
                i = smallest;
            }

            return min;
        }

        #endregion

        private static float Heuristic(int ax, int az, int bx, int bz)
        {
            float dx = bx - ax;
            float dz = bz - az;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static List<Vector2Int> ReconstructPath(
            int gridH, int sx, int sz, int ex, int ez)
        {
            var path = new List<Vector2Int>();
            int cx = ex, cz = ez;

            while (cx != sx || cz != sz)
            {
                path.Add(new Vector2Int(cx, cz));
                byte d = _parentDir[cx * gridH + cz];
                if (d > 7) break;
                cx -= Directions[d, 0];
                cz -= Directions[d, 1];
                if (path.Count > 500000) break;
            }
            path.Add(new Vector2Int(sx, sz));
            path.Reverse();
            return path;
        }

        /// <summary>Finds nearest terrain cell (height > 0).</summary>
        private static void SnapToTerrain(ref int gx, ref int gz,
            ushort[,] height, int gridW, int gridH)
        {
            for (int r = 1; r <= 100; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r)
                            continue;
                        int nx = gx + dx;
                        int nz = gz + dz;
                        if (nx >= 0 && nx < gridW && nz >= 0 && nz < gridH
                            && height[nx, nz] >= 2)
                        {
                            gx = nx;
                            gz = nz;
                            return;
                        }
                    }
                }
            }
        }

    }
}
