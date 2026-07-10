using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// A* pathfinder for the world map using a pre-computed grid at 0.5m
    /// resolution, aware of the player's travel mode: FOOT is blocked by all
    /// the game's wall layers, the BUNNY ignores region walls (CharaWall)
    /// and object walls but still needs ground, and the PSYNARD flies (no
    /// grid search — callers short-circuit it as "everything reachable").
    /// Rejects moves between cells where the height difference is too steep.
    /// Trees (Col_Obstacle triggers) are passthrough — only solid geometry
    /// blocks.
    ///
    /// Performance design (2026-07-05): search buffers are allocated ONCE
    /// and reused across searches via a generation counter, the grid's
    /// FLAGS lane is mutated in place with an undo journal (the height lane
    /// is read-only after load), and precomputed per-mode connected-region
    /// maps answer "is there any route at all?" instantly — so unreachable
    /// targets fail in microseconds instead of after a full-map search.
    /// The region/connectivity code lives in WorldmapPathfinder.Regions.cs.
    /// </summary>
    public static partial class WorldmapPathfinder
    {
        /// <summary>Cost for cardinal movement (1 cell = 0.5m).</summary>
        private const float CardinalCost = 1.0f;

        /// <summary>Cost for diagonal movement.</summary>
        private const float DiagonalCost = 1.414f;

        /// <summary>
        /// Max height difference in centimeters between adjacent cells.
        /// At 0.5m cell spacing, this controls max climbable slope.
        /// Set high (500cm) because the world map uses wall colliders for
        /// movement barriers, not terrain slope — the player can walk up
        /// steep world map terrain. The Phase A ride trace confirmed the
        /// bunny's steepest observed climbs also fit under this rule, so
        /// the SAME climb rule serves both modes; colliders, not slope,
        /// are what differs between them. The slope penalty
        /// (SlopePenaltyStartCm) still steers the A* toward flat roads.
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
        /// penalty (foot only). Cells with clearance at or above this value
        /// get no penalty. Based on: comfortable passage = 2x player radius.
        /// </summary>
        private const float ComfortableClearance = 1.5f;

        /// <summary>
        /// Maximum penalty applied to the tightest passable cells
        /// (those just above the hard minimum). The penalty scales
        /// linearly: tighter cells get higher penalties. Kept low
        /// (3.0) so A* prefers direct routes through gaps — the
        /// real-time wall avoidance in NavigationHandler handles
        /// steering through tight passages safely. Foot only: the
        /// clearance tables describe gaps in walls the bunny ignores.
        /// </summary>
        private const float MaxClearancePenalty = 3.0f;

        /// <summary>
        /// Preferred minimum clearance (meters) for the first FOOT
        /// pathfinding pass. The grid bakes a hard 0.50m floor at generation
        /// time, which equals the player's capsule radius — gaps that tight
        /// wedge the player (they cannot fit even when aimed dead-center).
        /// We first search for a route where every cell has at least this
        /// much clearance (a real safety margin). Only if NO such route
        /// exists do we fall back to the 0.50m-floor route. This steers the
        /// player onto wider roads when one exists, WITHOUT ever making a
        /// destination less reachable than before: the fallback pass is
        /// identical to the original behavior.
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
        /// there is NO overland route at all for the searched travel mode
        /// (as opposed to a transient failure). Callers can announce a more
        /// helpful message.
        /// </summary>
        public static bool LastNoPathWasDisconnected { get; internal set; }

        /// <summary>
        /// True when the last successful FOOT FindPath had to fall back to
        /// the 0.50m clearance floor because no comfort-tier
        /// (<see cref="PreferredMinClearance"/>) route existed. Floor routes
        /// thread body-exact gaps the game's physics may refuse to walk —
        /// diagnostics use this to flag physically risky routes.
        /// </summary>
        public static bool LastPathUsedFloorTier { get; private set; }

        private static WorldmapGridFormat.CachedGrid _cachedExpel;
        private static WorldmapGridFormat.CachedGrid _cachedNede;

        /// <summary>One-time log flag for the legacy-grid bunny fallback.</summary>
        private static bool _loggedLegacyBunnyFallback;

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

        private static WorldmapGridFormat.CachedGrid GetCachedGrid(
            WorldmapID wmID)
        {
            if (wmID == WorldmapID.EXPEL)
            {
                if (_cachedExpel == null)
                {
                    _cachedExpel = WorldmapGridFormat.LoadGrid(
                        WorldmapID.EXPEL);
                    if (_cachedExpel != null) BuildFootRegions(_cachedExpel);
                }
                return _cachedExpel;
            }
            else
            {
                if (_cachedNede == null)
                {
                    _cachedNede = WorldmapGridFormat.LoadGrid(
                        WorldmapID.NEDE);
                    if (_cachedNede != null) BuildFootRegions(_cachedNede);
                }
                return _cachedNede;
            }
        }

        /// <summary>Clears cached grids (call if grid files are regenerated).</summary>
        public static void ClearCache()
        {
            _cachedExpel = null;
            _cachedNede = null;
            _loggedLegacyBunnyFallback = false;
        }

        /// <summary>
        /// The grid's blocked-flag bit for a travel mode's search. Bunny on
        /// a legacy grid falls back to the FOOT bit — any foot route also
        /// works mounted, so this is safe (never a false unreachable), just
        /// conservative. Psynard has no grid lane; if a psynard search is
        /// requested anyway it also uses the foot predicate (callers are
        /// expected to short-circuit psynard as "everything reachable").
        /// </summary>
        private static byte ModeSearchBit(WorldmapGridFormat.CachedGrid grid,
            WorldmapTravelMode mode)
        {
            if (mode == WorldmapTravelMode.Bunny)
            {
                if (grid.BunnyDataAvailable)
                    return WorldmapGridFormat.CachedGrid.FlagBunnyBlocked;
                if (!_loggedLegacyBunnyFallback)
                {
                    _loggedLegacyBunnyFallback = true;
                    DebugLogger.LogState(
                        "NAV WM pathfinder: legacy grid has no bunny lane — " +
                        "bunny searches use the FOOT predicate (safe: any " +
                        "foot route works mounted). Regenerate with F9 for " +
                        "true bunny routing.");
                }
                return WorldmapGridFormat.CachedGrid.FlagFootBlocked;
            }
            if (mode == WorldmapTravelMode.Psynard)
            {
                DebugLogger.LogState(
                    "NAV WM pathfinder: psynard search requested — using " +
                    "the foot predicate (flying auto-walk is out of scope; " +
                    "psynard reachability is always true at the callers).");
                return WorldmapGridFormat.CachedGrid.FlagFootBlocked;
            }
            return WorldmapGridFormat.CachedGrid.FlagFootBlocked;
        }

        /// <summary>
        /// True if the given world XZ lands on a grid cell that is passable
        /// for the given travel mode — real ground, not blocked by that
        /// mode's baked walls. Mirrors the A* floor pass exactly, so a cell
        /// this reports walkable is one the pathfinder can stand on. Returns
        /// false if no grid is cached (caller should treat as "unknown").
        /// Used to pick an entrance-ring point that is NOT buried in a wall.
        /// </summary>
        public static bool IsWalkableWorld(Vector3 world,
            WorldmapTravelMode mode = WorldmapTravelMode.Foot)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return false;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null) return false;
                grid.WorldToGrid(world.x, world.z, out int ax, out int az);
                return grid.IsPassable(ax, az, ModeSearchBit(grid, mode));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM IsWalkableWorld error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Diagnostic: raw grid cell value + flags at a world position.
        /// Raw height: 0 = no ground, 1 = legacy baked obstacle,
        /// 2+ = ground with encoded height. Flags: per-mode blocked bits
        /// (always 0 on a legacy grid). Returns false when no grid is
        /// cached or the position is out of bounds.
        /// </summary>
        public static bool TryGetCellInfo(Vector3 world, out ushort raw,
            out byte cellFlags, out bool isV2)
        {
            raw = 0;
            cellFlags = 0;
            isV2 = false;
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return false;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null) return false;
                grid.WorldToGrid(world.x, world.z, out int ax, out int az);
                if (ax < 0 || ax >= grid.GridW || az < 0 || az >= grid.GridH)
                    return false;
                raw = grid.Height[ax, az];
                cellFlags = grid.Flags[(long)ax * grid.GridH + az];
                isV2 = grid.IsV2;
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM TryGetCellInfo error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds a path on the world map using the pre-computed grid, with
        /// the passability rule of the given travel mode. Returns
        /// world-space waypoints or null if no path exists.
        /// </summary>
        public static Vector3[] FindPath(Vector3 start, Vector3 end,
            WorldmapTravelMode mode = WorldmapTravelMode.Foot,
            List<Vector3> blockedPositions = null)
        {
            LastNoPathWasDisconnected = false;
            LastPathUsedFloorTier = false;

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
            var flags = grid.Flags;
            byte modeBit = ModeSearchBit(grid, mode);
            bool foot = modeBit ==
                WorldmapGridFormat.CachedGrid.FlagFootBlocked;

            // Undo journals: the grid is mutated in place (blocked stamps +
            // start clearing) and restored in the finally block. Mutations
            // go to the FLAGS lane; the height lane is only touched for the
            // legacy format, whose baked obstacles live in the height lane.
            // This replaces the old full-grid clone (75MB + 37M-cell init
            // per call), which was the main cause of the multi-second
            // freeze at auto-walk start.
            var flagJournal = new List<(long idx, byte old)>();
            var heightJournal = new List<(int x, int z, ushort old)>();
            // Cells covered by a stuck-position stamp. The start clearing
            // must NEVER un-block these: in the old code stamps set cells
            // to ocean, which the clearing (height==1 only) left alone —
            // stamps always won. With the flags lane both operations touch
            // the same bits, so the clearing skips stamped cells explicitly.
            HashSet<long> stampedIdx = null;

            void StampBlocked(int x, int z)
            {
                long idx = (long)x * gridH + z;
                flagJournal.Add((idx, flags[idx]));
                flags[idx] |=
                    WorldmapGridFormat.CachedGrid.FlagAnyModeBlocked;
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

                // Apply stuck-position blocks (both mode bits — a physical
                // obstruction the player wedged on blocks the current walk
                // regardless of mode; the journal restores it after).
                if (blockedPositions != null && blockedPositions.Count > 0)
                {
                    stampedIdx = new HashSet<long>();
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
                                if (nx < 0 || nx >= gridW ||
                                    nz < 0 || nz >= gridH ||
                                    dx * dx + dz * dz >
                                    BlockedRadiusCells * BlockedRadiusCells)
                                    continue;
                                // Every covered cell is exempt from start
                                // clearing, including legacy obstacle cells.
                                stampedIdx.Add((long)nx * gridH + nz);
                                if (height[nx, nz] >= 2)
                                    StampBlocked(nx, nz);
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
                // targets the navigable enter-trigger ring, and SnapToPassable
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
                            if (nx < 0 || nx >= gridW ||
                                nz < 0 || nz >= gridH) continue;
                            // Stuck-position stamps win over the clearing —
                            // otherwise a recalc starting near the wedge
                            // spot would route straight back into it.
                            if (stampedIdx != null &&
                                stampedIdx.Contains((long)nx * gridH + nz))
                                continue;

                            if (height[nx, nz] == 1)
                            {
                                // Legacy baked obstacle: passability lives in
                                // the height lane, so the clear must too.
                                heightJournal.Add((nx, nz, height[nx, nz]));
                                height[nx, nz] = centerH;
                            }
                            else if (height[nx, nz] >= 2)
                            {
                                long idx = (long)nx * gridH + nz;
                                if ((flags[idx] & WorldmapGridFormat
                                    .CachedGrid.FlagAnyModeBlocked) != 0)
                                {
                                    flagJournal.Add((idx, flags[idx]));
                                    flags[idx] &= unchecked((byte)
                                        ~WorldmapGridFormat.CachedGrid
                                            .FlagAnyModeBlocked);
                                }
                            }
                        }
                    }
                }

                // Snap start/end to the nearest cell passable for this mode.
                if (!grid.IsPassable(startAx, startAz, modeBit))
                    SnapToPassable(ref startAx, ref startAz, grid, modeBit);
                if (!grid.IsPassable(endAx, endAz, modeBit))
                    SnapToPassable(ref endAx, ref endAz, grid, modeBit);

                if (!grid.IsPassable(startAx, startAz, modeBit) ||
                    !grid.IsPassable(endAx, endAz, modeBit))
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: start or end not on passable " +
                        $"terrain ({mode}). grid={gridW}x{gridH} " +
                        $"start=({startAx},{startAz}) end=({endAx},{endAz})");
                    return null;
                }

                // --- Connected-region fast reject (per travel mode) ---
                // If the target's region differs from every region touching
                // the start (including the cleared 3m disc, whose cells can
                // bridge the player out of a baked-obstacle pocket), then NO
                // route exists and a full search would just sweep the whole
                // landmass before saying so. Region 0 = unknown → never
                // reject (fail open); a missing region map for the mode
                // skips the reject entirely; blocked stamps only REMOVE
                // connectivity, so this check can never reject a genuinely
                // reachable pair.
                var regions = GetRegionsForSearch(grid, mode);
                if (regions != null)
                {
                    ushort endRegion = regions[(long)endAx * gridH + endAz];
                    if (endRegion != 0 &&
                        !StartTouchesRegion(grid, regions,
                            origStartAx, origStartAz,
                            startAx, startAz, endRegion))
                    {
                        LastNoPathWasDisconnected = true;
                        DebugLogger.LogState(
                            $"NAV WM pathfinder: start and target are in " +
                            $"different connected regions for {mode} " +
                            $"(target region {endRegion}) — no overland " +
                            $"route exists. Rejected in " +
                            $"{sw.ElapsedMilliseconds}ms.");
                        return null;
                    }
                }

                // Tiered A* search (FOOT only): first try a route where every
                // cell has a real clearance margin (PreferredMinClearance) so
                // the player is never threaded through a body-width gap. Only
                // if no such route exists do we fall back to the grid's baked
                // 0.50m floor (the original behavior). This prefers wide
                // roads when one exists but never removes a reachable
                // destination. The first pass is expansion-capped: it is a
                // comfort preference, not the authority on reachability.
                // The BUNNY skips the tier: the clearance tables describe
                // gaps in CharaWalls, which the bunny ignores entirely.
                List<Vector2Int> path = null;
                int expansions1 = 0, expansions2 = 0;

                if (foot)
                {
                    path = AStarSearch(startAx, startAz, endAx, endAz,
                        grid, modeBit, PreferredMinClearance, true,
                        PreferredPassMaxExpansions, out expansions1);
                    if (path == null)
                    {
                        // Log WHY: a comfort failure is either a pinched
                        // route or an endpoint whose own cell is too narrow
                        // to ever close — these need different fixes, so the
                        // endpoint data goes in the log (D1 investigation,
                        // 2026-07-10).
                        float startClr = grid.GetClearance(startAx, startAz);
                        float endClr = grid.GetClearance(endAx, endAz);
                        DebugLogger.LogState(
                            $"NAV WM pathfinder: no route at " +
                            $"{PreferredMinClearance:F2}m clearance " +
                            $"({expansions1} cells searched) — falling back " +
                            $"to the 0.50m floor. start cell " +
                            $"({startAx},{startAz}) clearance=" +
                            $"{FormatClearance(startClr)}, goal cell " +
                            $"({endAx},{endAz}) clearance=" +
                            $"{FormatClearance(endClr)}.");
                    }
                }

                if (path == null)
                {
                    path = AStarSearch(startAx, startAz, endAx, endAz,
                        grid, modeBit, 0f, foot, 0, out expansions2);
                    if (foot && path != null)
                        LastPathUsedFloorTier = true;
                }

                if (path == null)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: no path found ({mode}). " +
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
                // value the stick-injection follower never uses). Foot uses
                // clearance-adjusted positions for cells near CharaWalls so
                // the player walks through the exact center of narrow gaps;
                // the bunny ignores those walls, so it takes plain centers.
                var waypoints = new List<Vector3>(path.Count);
                foreach (var cell in path)
                {
                    Vector3 wp = foot
                        ? grid.GridToWorldWithClearance(cell.x, cell.y)
                        : grid.GridToWorld(cell.x, cell.y);
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
                    $"NAV WM pathfinder: found {mode} path with " +
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
                for (int i = flagJournal.Count - 1; i >= 0; i--)
                    flags[flagJournal[i].idx] = flagJournal[i].old;
                for (int i = heightJournal.Count - 1; i >= 0; i--)
                    height[heightJournal[i].x, heightJournal[i].z]
                        = heightJournal[i].old;

                // Don't let a pathological search pin hundreds of MB of
                // heap capacity forever.
                if (_heap.Capacity > 2_000_000)
                {
                    _heap.Clear();
                    _heap.TrimExcess();
                }
            }
        }

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
        /// A* search with slope checking and (foot only) wall proximity
        /// penalty. Reads the (temporarily mutated) grid lanes directly and
        /// uses the persistent generation-stamped buffers — no per-call
        /// allocation or full-grid initialization.
        /// </summary>
        /// <param name="modeBit">Flags-lane blocked bit that makes a cell
        /// impassable for this search.</param>
        /// <param name="minClearance">Hard clearance floor (0 = disabled).
        /// Used only by the foot preferred pass.</param>
        /// <param name="clearancePenalty">Apply the continuous clearance
        /// penalty (foot only — clearance data describes CharaWall gaps).</param>
        /// <param name="maxExpansions">Abort after this many cell expansions
        /// (0 = unlimited). Used only by the preferred-clearance pass.</param>
        private static List<Vector2Int> AStarSearch(
            int sx, int sz, int ex, int ez,
            WorldmapGridFormat.CachedGrid grid, byte modeBit,
            float minClearance, bool clearancePenalty,
            int maxExpansions, out int expansions)
        {
            int gridW = grid.GridW;
            int gridH = grid.GridH;
            var height = grid.Height;
            var flags = grid.Flags;

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
                    // No ground / legacy obstacle, or blocked for this mode.
                    if (neighborH < 2) continue;
                    if ((flags[nIdx] & modeBit) != 0) continue;

                    // Slope check.
                    int heightDiff = Math.Abs(currentH - neighborH);
                    if (heightDiff > MaxClimbCm) continue; // Too steep.

                    float moveCost = d < 4 ? CardinalCost : DiagonalCost;

                    // Slope penalty: prefer flat paths (roads).
                    if (heightDiff > SlopePenaltyStartCm)
                        moveCost += heightDiff * 0.02f;

                    if (clearancePenalty)
                    {
                        float clr = grid.GetClearance(nx, nz);

                        // Hard clearance floor (preferred pass only). Cells
                        // too narrow for the player to fit through are
                        // skipped entirely. minClearance == 0 in the
                        // fallback pass disables this, preserving original
                        // reachability.
                        if (minClearance > 0f && clr < minClearance)
                            continue;

                        // Continuous clearance penalty: prefer wide
                        // corridors. Scales linearly from
                        // MaxClearancePenalty at minimum clearance down to
                        // 0 at ComfortableClearance.
                        if (clr < ComfortableClearance)
                        {
                            float ratio = (ComfortableClearance - clr) /
                                (ComfortableClearance - 0.55f);
                            if (ratio > 1f) ratio = 1f;
                            moveCost += ratio * MaxClearancePenalty;
                        }
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

        /// <summary>Formats a clearance value for logs ("wide" for cells
        /// without an explicit table entry).</summary>
        private static string FormatClearance(float clearance)
            => clearance == float.MaxValue ? "wide" : $"{clearance:F2}m";

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

        /// <summary>Finds the nearest cell passable for the mode, searching
        /// outward in growing rings (up to 50m).</summary>
        private static void SnapToPassable(ref int gx, ref int gz,
            WorldmapGridFormat.CachedGrid grid, byte modeBit)
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
                        if (grid.IsPassable(nx, nz, modeBit))
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
