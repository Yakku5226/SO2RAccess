using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler: world-map PATHFINDING —
    // CalcHeight-based safe-approach/exit point computation, ocean-barrier
    // reachability checks, the AI pathfinder accessor, and the A* path builder
    // (WorldmapCalculateAndStorePath). The per-frame walk loop that consumes
    // these paths lives in NavigationHandler.Worldmap.cs.
    public partial class NavigationHandler
    {
        #region World Map Pathfinding

        /// <summary>
        /// Checks whether a target is reachable on the world map by sampling
        /// CalcHeight at evenly spaced points along the line from player to target.
        /// If any sample has no ground (success=false), there is ocean between
        /// the player and target — the target is unreachable.
        /// Returns true as a fallback if CalcHeight throws.
        /// </summary>
        private bool WorldmapIsReachableViaCalcHeight(Vector3 playerPos, Vector3 targetPos)
        {
            try
            {
                for (int s = 1; s <= WorldmapCalcHeightSamples; s++)
                {
                    float t = s / (float)WorldmapCalcHeightSamples;
                    Vector3 samplePos = new Vector3(
                        playerPos.x + (targetPos.x - playerPos.x) * t,
                        playerPos.y + (targetPos.y - playerPos.y) * t,
                        playerPos.z + (targetPos.z - playerPos.z) * t);

                    GameUtility.CalcHeight(samplePos, out bool success);
                    if (!success)
                    {
                        DebugLogger.LogState(
                            $"NAV worldmap: ocean barrier at sample {s}/{WorldmapCalcHeightSamples} " +
                            $"toward ({targetPos.x:F1},{targetPos.z:F1})");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WorldmapCalcHeight: {ex.Message}");
                return true; // fallback — don't filter
            }
        }

        /// <summary>
        /// Number of waypoints to pre-validate against real L22 obstacles
        /// before the player starts walking. Covers ~15m of path at 0.5m spacing.
        /// </summary>
        private const int WmPreValidateCount = 30;

        /// <summary>
        /// Maximum pre-validation rounds before accepting the best path found.
        /// Each round detects blocked waypoints and re-pathfinds around them.
        /// </summary>
        private const int WmPreValidateMaxRounds = 10;

        /// <summary>
        /// Radius for OverlapSphere when pre-validating waypoints.
        /// Slightly larger than player collision (0.50m) to account for
        /// physics contact offset and ensure clearance.
        /// </summary>
        private const float WmPreValidateRadius = 0.55f;

        /// <summary>
        /// Layer mask for L22 (Col_Obstacle) — the physical rocks/obstacles
        /// that block world map movement. Used for pre-validation only.
        /// </summary>
        private static readonly int WmObstacleLayerMask = 1 << 22;

        /// <summary>
        /// Maximum distance (meters) at which a straight-line fallback is
        /// still used when the grid pathfinder finds no path. Close-range
        /// failures are usually grid-snap artifacts; beyond this, "no path"
        /// means the route genuinely doesn't exist and auto-walk must report
        /// unreachable instead of grinding the player into obstacles.
        /// </summary>
        private const float WmStraightLineFallbackMaxDist = 15f;

        /// <summary>
        /// Resolves the world-map auto-walk destination for a location: the nearest
        /// navigable point on that location's enter-trigger RING — its
        /// FieldMapjumpCollision trigger collider, the same volume that raises the
        /// "Press X to enter" prompt. The ring is navigable; the location's model
        /// wall is not. Targeting the ring (not the centre point, and not a guessed
        /// hole through the model) lets the pathfinder keep the whole model impassable
        /// and still deliver the player to exactly where they can enter.
        /// Returns <paramref name="locationPos"/> unchanged if no trigger is found.
        /// </summary>
        private Vector3 ComputeEnterTriggerTarget(Vector3 locationPos, Vector3 playerPos)
        {
            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                {
                    DebugLogger.LogState(
                        "NAV WM enter-trigger: no FieldMapjumpCollision found.");
                    return locationPos;
                }

                // Nearest mapjump to the location, and its destination fieldmap.
                FieldMapjumpCollision nearest = null;
                float nearestDist = float.MaxValue;
                FieldmapID nearestFieldmapID = default;
                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    float d = Vector3.Distance(c.transform.position, locationPos);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearest = c;
                        nearestFieldmapID = c.fieldmapID;
                    }
                }
                if (nearest == null) return locationPos;

                // Collect ALL ground-level entrance trigger colliders for that
                // destination (small Y extent = road entrances, not town-wide
                // detection volumes). Boundary towns like Salva have SEVERAL
                // entrances facing different regions (Krosse side vs Arlia
                // valley side) — sampling only one can target the wrong side
                // of the town entirely. Fall back to any trigger on the nearest.
                var rings = new List<Collider>();
                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null || c.fieldmapID != nearestFieldmapID) continue;
                    var cols = c.GetComponents<Collider>();
                    if (cols == null) continue;
                    for (int k = 0; k < cols.Length; k++)
                    {
                        var col = cols[k];
                        if (col == null || !col.isTrigger) continue;
                        if (col.bounds.size.y > 20f) continue; // skip town-wide zones
                        rings.Add(col);
                    }
                }
                if (rings.Count == 0)
                {
                    var cols = nearest.GetComponents<Collider>();
                    if (cols != null)
                        for (int k = 0; k < cols.Length; k++)
                            if (cols[k] != null && cols[k].isTrigger)
                            { rings.Add(cols[k]); break; }
                }

                Vector3 dest;
                if (rings.Count > 0)
                {
                    // The triggers hug the model wall, so raw points often land
                    // on baked-obstacle cells (→ "no path") or on a walkable
                    // cell CONNECTED TO THE WRONG REGION (Salva's valley-side
                    // sliver — the "unreachable Salva from Krosse" bug).
                    // Sample all entrances for a walkable cell, preferring one
                    // in the SAME connected region as the player.
                    dest = PickReachableRingPoint(rings, playerPos, locationPos,
                        out bool usedCenter);
                    if (usedCenter)
                        DebugLogger.LogState(
                            "NAV WM enter-trigger: no walkable cell on any " +
                            "entrance trigger; routing to the location centre " +
                            "instead (arrival still waits for this location's " +
                            "enter prompt).");
                }
                else
                {
                    dest = nearest.transform.position;
                }

                float h = GameUtility.CalcHeight(
                    new Vector3(dest.x, 150f, dest.z), out bool ok, 300f);
                if (ok) dest.y = h;

                DebugLogger.LogState(
                    $"NAV WM enter-trigger: routing to ring point " +
                    $"({dest.x:F1},{dest.z:F1}) for location " +
                    $"({locationPos.x:F1},{locationPos.z:F1}) fieldmap={nearestFieldmapID}");
                return dest;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM enter-trigger error: {ex.Message}");
                return locationPos;
            }
        }

        /// <summary>
        /// Returns a point on one of the location's entrance triggers that sits
        /// on a WALKABLE grid cell (the road into the location). Candidates in
        /// the SAME connected region as the player win over merely-walkable
        /// ones: boundary towns (Salva) have walkable cells at BOTH entrances,
        /// and a cell on the far side's region is a guaranteed "no route" —
        /// that was the "unreachable Salva from Krosse" bug. Within a tier the
        /// nearest candidate to the player wins. Samples each trigger's center,
        /// its closest point to the player, and its perimeter from 16
        /// directions (projecting external reference points onto the collider
        /// covers every approach side). If nothing tests walkable (triggers
        /// fully buried in the model wall, or no grid cached), sets
        /// <paramref name="usedCenter"/> and returns the location centre so the
        /// caller can route there instead — the enter prompt still gates arrival.
        /// </summary>
        private Vector3 PickReachableRingPoint(List<Collider> rings,
            Vector3 playerPos, Vector3 locationPos, out bool usedCenter)
        {
            usedCenter = false;

            int playerRegion = WorldmapPathfinder.GetRegionId(playerPos);

            // Tier 1: walkable AND in the player's connected region.
            Vector3 bestConn = Vector3.zero;
            float bestConnSq = float.MaxValue;
            // Tier 2: walkable (region unknown or different) — old behavior.
            Vector3 bestWalk = Vector3.zero;
            float bestWalkSq = float.MaxValue;

            void Consider(Vector3 cand)
            {
                if (!WorldmapPathfinder.IsWalkableWorld(cand)) return;
                float d = (cand - playerPos).sqrMagnitude;
                if (d < bestWalkSq) { bestWalkSq = d; bestWalk = cand; }
                if (playerRegion != 0 &&
                    WorldmapPathfinder.GetRegionId(cand) == playerRegion &&
                    d < bestConnSq)
                {
                    bestConnSq = d;
                    bestConn = cand;
                }
            }

            foreach (var ring in rings)
            {
                if (ring == null) continue;
                Bounds b = ring.bounds;

                // Trigger center — the middle of the entrance road, and of the
                // passable cells the grid generator punched for this trigger.
                Consider(new Vector3(b.center.x, b.center.y, b.center.z));

                // Plain closest point to the player (skip the degenerate
                // "player already inside" case).
                try
                {
                    Vector3 cp = ring.ClosestPoint(playerPos);
                    if ((cp - playerPos).sqrMagnitude > 0.0001f) Consider(cp);
                }
                catch { /* unsupported collider type — sampling below */ }

                // Perimeter samples from all around this trigger.
                float reach = b.extents.x + b.extents.z + 4f;
                const int Steps = 16;
                for (int i = 0; i < Steps; i++)
                {
                    float ang = (i / (float)Steps) * Mathf.PI * 2f;
                    Vector3 refPt = new Vector3(
                        b.center.x + Mathf.Cos(ang) * reach,
                        b.center.y,
                        b.center.z + Mathf.Sin(ang) * reach);
                    try { Consider(ring.ClosestPoint(refPt)); }
                    catch { /* skip this sample */ }
                }
            }

            if (bestConnSq < float.MaxValue)
            {
                DebugLogger.LogState(
                    $"NAV WM enter-trigger: connected ring point at " +
                    $"({bestConn.x:F1},{bestConn.z:F1}), " +
                    $"{Mathf.Sqrt(bestConnSq):F1}m from player " +
                    $"(player region {playerRegion}, {rings.Count} triggers).");
                return bestConn;
            }

            if (bestWalkSq < float.MaxValue)
            {
                DebugLogger.LogState(
                    $"NAV WM enter-trigger: no candidate in player region " +
                    $"{playerRegion}; using nearest walkable ring point at " +
                    $"({bestWalk.x:F1},{bestWalk.z:F1}), " +
                    $"{Mathf.Sqrt(bestWalkSq):F1}m from player " +
                    $"({rings.Count} triggers). If no route exists the " +
                    $"pathfinder will reject it honestly.");
                return bestWalk;
            }

            usedCenter = true;
            return locationPos;
        }

        /// <summary>
        /// Computes a safe exit point when the player is STARTING near a town:
        /// a point ~25m away in the direction AWAY from the nearest trigger
        /// (toward open terrain), so the A* leaves the town's wall ring cleanly
        /// before routing to the target. Checks both L22 obstacles and L23 CharaWalls.
        /// </summary>
        private Vector3 ComputeSafeExitPoint(Vector3 playerPos)
        {
            const float SafeDistance = 25f;
            int bothLayerMask = (1 << 22) | (1 << 23);

            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                    return playerPos;

                // Find the nearest ground-level trigger to the player.
                FieldMapjumpCollision nearestGround = null;
                float nearestGroundDist = float.MaxValue;

                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    var col = c.GetComponent<Collider>();
                    if (col == null || col.bounds.size.y > 20f) continue;
                    float dist = Vector3.Distance(
                        c.transform.position, playerPos);
                    if (dist < nearestGroundDist)
                    {
                        nearestGroundDist = dist;
                        nearestGround = c;
                    }
                }

                if (nearestGround == null || nearestGroundDist > 30f)
                    return playerPos; // Not near a town.

                Vector3 triggerCenter = nearestGround.transform.position;

                // Direction outward: from trigger center through player
                // position and beyond (away from the town).
                Vector3 outward = playerPos - triggerCenter;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f)
                    outward = Vector3.forward;
                outward.Normalize();

                // Try outward direction first, then 8 directions.
                Vector3[] directions = new Vector3[9];
                directions[0] = outward;
                for (int d = 0; d < 8; d++)
                {
                    float angle = d * 45f * Mathf.Deg2Rad;
                    directions[d + 1] = new Vector3(
                        Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                }

                foreach (var dir in directions)
                {
                    Vector3 candidate = playerPos + dir * SafeDistance;

                    float h = GameUtility.CalcHeight(
                        candidate, out bool hasGround, 50f);
                    if (!hasGround) continue;
                    candidate.y = h;

                    // Check no L22 OR L23 obstacles — we want wide open terrain.
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            candidate, 2.0f, bothLayerMask);
                        bool blocked = false;
                        if (hits != null)
                        {
                            foreach (var col in hits)
                            {
                                if (col != null && !col.isTrigger)
                                {
                                    blocked = true;
                                    break;
                                }
                            }
                        }
                        if (blocked) continue;
                    }
                    catch { continue; }

                    DebugLogger.LogState(
                        $"NAV WM safe exit: found at ({candidate.x:F1}," +
                        $"{candidate.z:F1}) {SafeDistance:F0}m from player");
                    return candidate;
                }

                DebugLogger.LogState(
                    "NAV WM safe exit: all directions blocked.");
                return playerPos;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM safe exit error: {ex.Message}");
                return playerPos;
            }
        }

        /// <summary>
        /// Calculates a world map path using the CalcHeight-based A* pathfinder.
        /// When starting near a town, routes to a safe exit point first.
        /// When targeting a location, routes to a safe approach point.
        /// Falls back to a single-waypoint straight line if pathfinding fails.
        /// </summary>
        private bool WorldmapCalculateAndStorePath(Vector3 playerPos, Vector3 targetPos,
            bool keepBlockedPositions = false)
        {
            _wmRecalcCount = 0;
            if (!keepBlockedPositions)
                _wmBlockedPositions.Clear();

            // For locations, targetPos is already the enter-trigger ring point
            // (resolved by ComputeEnterTriggerTarget when the walk started), so we
            // route straight to it — the model stays fully impassable in the grid.

            // If starting near a town, compute a safe exit point first.
            // Route: player → safe exit → target. This prevents the A* from
            // cutting through the town's obstacle ring or CharaWall boundary.
            Vector3 safeExit = ComputeSafeExitPoint(playerPos);
            bool usingSafeExit = Vector3.Distance(safeExit, playerPos) > 5f;

            // If using a safe exit, compute two-part path:
            // player → safe exit → target.
            Vector3 aStarStart = playerPos;
            Vector3[] exitPath = null;
            if (usingSafeExit)
            {
                DebugLogger.LogState(
                    $"NAV WM safe exit: routing via ({safeExit.x:F1}," +
                    $"{safeExit.z:F1}) before heading to target");
                exitPath = WorldmapPathfinder.FindPath(
                    playerPos, safeExit, _wmBlockedPositions);
                if (exitPath != null && exitPath.Length > 0)
                {
                    aStarStart = safeExit;
                }
                else
                {
                    DebugLogger.LogState(
                        "NAV WM safe exit: no path to exit point, going direct.");
                    exitPath = null;
                    usingSafeExit = false;
                }
            }

            Vector3[] bestPath = null;
            int bestFirstBlockedIdx = -1;

            for (int round = 0; round < WmPreValidateMaxRounds; round++)
            {
                var path = WorldmapPathfinder.FindPath(aStarStart, targetPos,
                    _wmBlockedPositions.Count > 0 ? _wmBlockedPositions : null);

                if (path == null || path.Length == 0)
                {
                    DebugLogger.LogState(
                        $"NAV WM pre-validate round {round}: no path found.");
                    break;
                }

                // Check the first N waypoints for L22 obstacles.
                int checkCount = Math.Min(WmPreValidateCount, path.Length);
                int firstBlockedIdx = -1;
                int totalBlocked = 0;

                for (int i = 0; i < checkCount; i++)
                {
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            path[i], WmPreValidateRadius, WmObstacleLayerMask);
                        if (hits != null && hits.Length > 0)
                        {
                            // Check if any are real non-trigger L22 obstacles.
                            bool hasRealObstacle = false;
                            foreach (var col in hits)
                            {
                                if (col != null && !col.isTrigger)
                                {
                                    hasRealObstacle = true;
                                    break;
                                }
                            }
                            if (hasRealObstacle)
                            {
                                totalBlocked++;
                                if (firstBlockedIdx < 0) firstBlockedIdx = i;
                            }
                        }
                    }
                    catch { }
                }

                // Track best path (one with obstacles furthest from start).
                if (firstBlockedIdx < 0)
                {
                    // Clean path — no L22 obstacles in first N waypoints.
                    bestPath = path;
                    bestFirstBlockedIdx = -1;
                    DebugLogger.LogState(
                        $"NAV WM pre-validate round {round}: CLEAR. " +
                        $"{path.Length} waypoints, checked {checkCount}.");
                    break;
                }

                // This path has obstacles. Keep it if it's better than previous.
                if (bestPath == null || firstBlockedIdx > bestFirstBlockedIdx)
                {
                    bestPath = path;
                    bestFirstBlockedIdx = firstBlockedIdx;
                }

                DebugLogger.LogState(
                    $"NAV WM pre-validate round {round}: {totalBlocked} blocked " +
                    $"waypoints in first {checkCount}. First blocked at wp[{firstBlockedIdx}] " +
                    $"({path[firstBlockedIdx].x:F1},{path[firstBlockedIdx].y:F1}," +
                    $"{path[firstBlockedIdx].z:F1}). Marking and re-pathfinding.");

                // Mark blocked waypoints so A* avoids them next round.
                for (int i = 0; i < checkCount; i++)
                {
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            path[i], WmPreValidateRadius, WmObstacleLayerMask);
                        if (hits != null)
                        {
                            foreach (var col in hits)
                            {
                                if (col != null && !col.isTrigger)
                                {
                                    _wmBlockedPositions.Add(path[i]);
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (bestPath != null && bestPath.Length > 0)
            {
                // Concatenate exit path + main path if using safe exit.
                if (usingSafeExit && exitPath != null && exitPath.Length > 0)
                {
                    var combined = new Vector3[exitPath.Length + bestPath.Length];
                    exitPath.CopyTo(combined, 0);
                    bestPath.CopyTo(combined, exitPath.Length);
                    _wmPathWaypoints = combined;
                    DebugLogger.LogState(
                        $"NAV WM: combined path: {exitPath.Length} exit + " +
                        $"{bestPath.Length} main = {combined.Length} total waypoints");
                }
                else
                {
                    _wmPathWaypoints = bestPath;
                }
                _wmPathIndex = 0;

                // Diagnostic: log first 10 waypoints to verify path direction.
                int logCount = Math.Min(bestPath.Length, 10);
                DebugLogger.LogState(
                    $"NAV WM PATH: {bestPath.Length} waypoints. " +
                    $"Start=({playerPos.x:F1},{playerPos.z:F1}) " +
                    $"Target=({targetPos.x:F1},{targetPos.z:F1})");
                for (int i = 0; i < logCount; i++)
                {
                    DebugLogger.LogState(
                        $"NAV WM PATH wp[{i}]=({bestPath[i].x:F1},{bestPath[i].y:F1},{bestPath[i].z:F1})");
                }
                if (bestPath.Length > 10)
                {
                    DebugLogger.LogState(
                        $"NAV WM PATH wp[last]=({bestPath[bestPath.Length - 1].x:F1}," +
                        $"{bestPath[bestPath.Length - 1].y:F1},{bestPath[bestPath.Length - 1].z:F1})");
                }
            }
            else
            {
                // No grid path. For VERY CLOSE targets this can be a grid-snap
                // artifact (start/end in the same spot, or a cell-alignment
                // quirk), so a short straight-line hop is safe. For anything
                // farther, walking a blind player straight at an unknown
                // obstacle course was the old "grind into a wall for 10+
                // seconds, then give up" behavior — fail fast and honestly
                // instead. The caller announces unreachable.
                float fbDx = targetPos.x - playerPos.x;
                float fbDz = targetPos.z - playerPos.z;
                float fbDist = Mathf.Sqrt(fbDx * fbDx + fbDz * fbDz);
                if (fbDist > WmStraightLineFallbackMaxDist)
                {
                    DebugLogger.LogState(
                        $"NAV worldmap: no grid path and target is " +
                        $"{fbDist:F0}m away — refusing straight-line " +
                        $"fallback, reporting unreachable.");
                    return false;
                }

                DebugLogger.LogState(
                    "NAV worldmap: no grid path but target is close " +
                    $"({fbDist:F1}m). Using straight-line fallback.");
                _wmPathWaypoints = new Vector3[] { targetPos };
                _wmPathIndex = 0;
            }

            // Still need these for shared code compatibility.
            _pathCorners = new Vector3[] { targetPos };
            _pathCornerIndex = 0;
            _pathRecalcTimer = 0f;

            return true;
        }

        #endregion
    }
}
