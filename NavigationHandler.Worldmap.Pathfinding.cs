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
        /// Computes a safe approach point outside a location's obstacle ring.
        /// Finds the nearest FieldMapjumpCollision trigger, then places a point
        /// 20m outward (away from the target) where there is ground and no L22
        /// obstacles. Falls back to 8 directions, then trigger position itself.
        /// </summary>
        private Vector3 ComputeSafeApproachPoint(Vector3 targetPos)
        {
            const float SafeDistance = 20f;

            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                {
                    DebugLogger.LogState("NAV WM safe approach: no triggers found.");
                    return targetPos;
                }

                // Find the nearest trigger to the target.
                FieldMapjumpCollision nearest = null;
                float nearestDist = float.MaxValue;

                // Also track a ground-level preferred trigger (small Y bounds).
                FieldMapjumpCollision groundTrigger = null;
                float groundTriggerDist = float.MaxValue;
                FieldmapID nearestFieldmapID = default;

                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    float dist = Vector3.Distance(c.transform.position, targetPos);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = c;
                        nearestFieldmapID = c.fieldmapID;
                    }
                }

                if (nearest == null) return targetPos;

                // Among triggers for the same destination, prefer ground-level.
                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null || c.fieldmapID != nearestFieldmapID) continue;

                    var col = c.GetComponent<Collider>();
                    if (col != null)
                    {
                        float yExtent = col.bounds.size.y;
                        if (yExtent < 20f)
                        {
                            float dist = Vector3.Distance(c.transform.position, targetPos);
                            if (dist < groundTriggerDist)
                            {
                                groundTriggerDist = dist;
                                groundTrigger = c;
                            }
                        }
                    }
                }

                var chosenTrigger = groundTrigger ?? nearest;
                Vector3 triggerCenter = chosenTrigger.transform.position;

                // Direction outward: from trigger center pointing away from target.
                Vector3 outward = triggerCenter - targetPos;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f)
                    outward = Vector3.forward; // fallback if target == trigger
                outward.Normalize();

                // Try outward direction first, then 8 directions.
                Vector3[] directions = new Vector3[9];
                directions[0] = outward;
                for (int d = 0; d < 8; d++)
                {
                    float angle = d * 45f * Mathf.Deg2Rad;
                    directions[d + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                }

                foreach (var dir in directions)
                {
                    Vector3 candidate = triggerCenter + dir * SafeDistance;

                    // Check ground exists.
                    float h = GameUtility.CalcHeight(candidate, out bool hasGround, 50f);
                    if (!hasGround) continue;
                    candidate.y = h;

                    // Check no L22 obstacles.
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            candidate, 1.0f, WmObstacleLayerMask);
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
                        $"NAV WM safe approach: found at ({candidate.x:F1},{candidate.z:F1}) " +
                        $"{SafeDistance:F0}m from trigger ({triggerCenter.x:F1},{triggerCenter.z:F1})");
                    return candidate;
                }

                // All directions blocked — use trigger position as fallback.
                DebugLogger.LogState(
                    "NAV WM safe approach: all directions blocked, using trigger position.");
                return triggerCenter;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM safe approach error: {ex.Message}");
                return targetPos;
            }
        }

        /// <summary>
        /// Computes a safe exit point when the player is near a town.
        /// Uses the same approach as ComputeSafeApproachPoint but picks the
        /// direction AWAY from the nearest trigger (toward open terrain).
        /// Also checks L23 CharaWalls in addition to L22 obstacles.
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

            // For location targets, route to a safe approach point outside the
            // obstacle ring instead of directly to the town entrance.
            _wmOriginalTarget = targetPos;
            if (_autoWalkCategoryIndex == CAT_LOCATION)
            {
                Vector3 safePoint = ComputeSafeApproachPoint(targetPos);
                if (Vector3.Distance(safePoint, targetPos) > 1f)
                {
                    DebugLogger.LogState(
                        $"NAV WM: routing to safe approach at " +
                        $"({safePoint.x:F1},{safePoint.z:F1}) instead of location at " +
                        $"({targetPos.x:F1},{targetPos.z:F1})");
                    targetPos = safePoint;
                }
            }

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
                // Fallback: straight line (pathfinder may fail for very close targets
                // or if terrain data is unavailable).
                DebugLogger.LogState(
                    "NAV worldmap: CalcHeight pathfinder returned no path. " +
                    "Using straight-line fallback.");
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
