using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region World Map Constants

        /// <summary>Seconds between stuck checks during world map auto-walk.</summary>
        private const float WorldmapStuckCheckInterval = 2f;

        /// <summary>
        /// Minimum distance the player must move during a stuck check interval
        /// to be considered making progress. At 2s interval, 1.5m means
        /// minimum 0.75 m/s — well below full speed (6.5 m/s) but detects
        /// stuck states faster when following raw waypoints in tight areas.
        /// </summary>
        private const float WorldmapStuckMinMove = 1.5f;

        /// <summary>
        /// Number of CalcHeight samples along the line from player to target
        /// for ocean barrier detection on the world map.
        /// </summary>
        private const int WorldmapCalcHeightSamples = 10;

        /// <summary>
        /// Arrival radius for world map locations (cities, dungeons).
        /// Used as fallback when stuck near a location — triggers
        /// TryEnterWorldmapLocation() to force entry via ChangeFieldmap().
        /// Set to 10 to cover the collision barrier near location entrances.
        /// </summary>
        private const float WorldmapLocationArrivalRadius = 10f;

        /// <summary>Max distance to show chests on the world map.</summary>
        private const float WorldmapChestMaxDistance = 200f;

        /// <summary>Max distance to show enemies on the world map.</summary>
        private const float WorldmapEnemyMaxDistance = 150f;

        #endregion

        #region World Map State

        /// <summary>
        /// True when the current field is a world map (Expel/Nede overworld).
        /// Set at the start of ScanAndOpenList, persists during auto-walk,
        /// cleared when auto-walk ends or the nav list closes.
        /// </summary>
        private bool _isWorldmap;

        /// <summary>
        /// True when world map auto-walk uses OnMove() for direct movement
        /// instead of stick injection. Static so the Harmony postfixes can
        /// check it and not inject stick input (which would fight OnMove).
        /// </summary>
        private static bool _wmDirectMoveActive;

        /// <summary>
        /// Cached AIPathFinder for world map pathfinding (retained for ocean
        /// reachability checks; movement uses CalcHeight-based WorldmapPathfinder).
        /// </summary>
        private AIPathFinder<FieldCharacter> _wmPathFinder;

        /// <summary>Timer for world map stuck detection during auto-walk.</summary>
        private float _wmStuckTimer;

        /// <summary>Player position at the last stuck check, for distance comparison.</summary>
        private Vector3 _wmLastStuckCheckPos;

        /// <summary>Timer for diagnostic logging — logs once per second.</summary>
        private float _wmDiagTimer;

        /// <summary>Waypoints from CalcHeight-based A* pathfinder.</summary>
        private Vector3[] _wmPathWaypoints;

        /// <summary>Current index into _wmPathWaypoints.</summary>
        private int _wmPathIndex;

        /// <summary>Number of path recalculations attempted for the current auto-walk.</summary>
        private int _wmRecalcCount;

        /// <summary>
        /// Positions where the character got stuck due to physical obstacles
        /// invisible to CalcHeight. Passed to WorldmapPathfinder on recalculation
        /// so the A* routes around these areas.
        /// </summary>
        private List<Vector3> _wmBlockedPositions = new List<Vector3>();

        /// <summary>Maximum recalculation attempts before giving up.</summary>
        private const int WmMaxRecalcAttempts = 5;

        /// <summary>
        /// Original auto-walk target before safe approach substitution.
        /// Used by TryEnterWorldmapLocation to find the correct mapjump trigger.
        /// </summary>
        private Vector3 _wmOriginalTarget;

        #region Battle Resume State

        /// <summary>True when auto-walk was interrupted by battle and should resume.</summary>
        private bool _wmResumeActive;

        /// <summary>Saved target position for battle resume.</summary>
        private Vector3 _wmResumeTarget;

        /// <summary>Saved target label for battle resume announcement.</summary>
        private string _wmResumeLabel;

        /// <summary>Saved category index for battle resume.</summary>
        private int _wmResumeCategoryIndex;

        /// <summary>Saved live transform for battle resume (may be null).</summary>
        private Transform _wmResumeTransform;

        #endregion

        /// <summary>Distance at which the next waypoint is considered reached.</summary>
        private const float WmWaypointArrivalThreshold = 1.5f;

        /// <summary>
        /// Tighter arrival threshold used when consecutive waypoints are
        /// close together, indicating a narrow area where the player must
        /// follow raw A* waypoints precisely (0.5m cell spacing).
        /// </summary>
        private const float WmGapWaypointArrivalThreshold = 0.5f;

        /// <summary>
        /// If the distance to the NEXT waypoint from the current one is
        /// below this, we're in a narrow area and use the tight threshold.
        /// </summary>
        private const float WmGapDetectionDistance = 5.0f;

        /// <summary>
        /// How many waypoints ahead to check when attempting skip-ahead
        /// stuck recovery (player may have slid past current waypoint
        /// via native wall sliding).
        /// </summary>
        private const int WmSkipAheadLookahead = 5;

        /// <summary>
        /// Maximum distance to a future waypoint for skip-ahead to trigger.
        /// </summary>
        private const float WmSkipAheadMaxDist = 3.0f;

        /// <summary>
        /// Consecutive frames that IsFieldFree returned false on the world map.
        /// Used to tolerate brief terrain transition events without cancelling auto-walk.
        /// </summary>
        internal int _wmFieldFreeFailCount;

        #endregion

        #region World Map Auto-Walk Update

        /// <summary>
        /// Per-frame update for world map auto-walk. Follows pre-computed waypoints
        /// from the CalcHeight-based A* pathfinder via left stick injection.
        /// The world map's native movement pipeline reads GetPlayerControlStick()
        /// (not GetLeftStick()), so both are hooked to inject synthetic stick input.
        /// Called from Update() when _isWorldmap is true.
        /// </summary>
        private void UpdateWorldmapAutoWalk(FieldPlayer player, Vector3 playerPos)
        {
            // If the target has a live transform (chest, enemy), update position.
            if (_autoWalkTransform != null)
                _autoWalkTarget = _autoWalkTransform.position;

            // XZ distance to final target.
            float targetDx = _autoWalkTarget.x - playerPos.x;
            float targetDz = _autoWalkTarget.z - playerPos.z;
            float targetDist = Mathf.Sqrt(targetDx * targetDx + targetDz * targetDz);

            // --- Arrival check at final target ---
            if (_autoWalkCategoryIndex == CAT_LOCATION)
            {
                // Locations: stop at the location arrival radius and try
                // to enter via map transition. The location's obstacle ring
                // physically blocks getting closer, so don't try.
                if (targetDist <= WorldmapLocationArrivalRadius)
                {
                    StopAutoWalk();
                    if (TryEnterWorldmapLocation())
                    {
                        AnnounceArrival(Loc.Get("nav_autowalk_entering",
                            _autoWalkLabel));
                        DebugLogger.LogState(
                            $"NAV auto-walk entering '{_autoWalkLabel}' via mapjump.");
                    }
                    else
                    {
                        AnnounceArrival(Loc.Get("nav_autowalk_arrived",
                            _autoWalkLabel));
                        DebugLogger.LogState(
                            $"NAV auto-walk arrived at '{_autoWalkLabel}'.");
                    }
                    return;
                }
            }
            else
            {
                bool isInteractable = _autoWalkCategoryIndex == CAT_CHEST
                    || _autoWalkCategoryIndex == CAT_SAVE;
                float arrivalRadius = isInteractable
                    ? InteractableArrivalRadius : AutoWalkArrivalRadius;

                if (targetDist <= arrivalRadius)
                {
                    StopAutoWalk();
                    LogNpcArrivalDiagnostics(player, playerPos, targetDist);

                    string arrivalMsg = Loc.Get("nav_autowalk_arrived", _autoWalkLabel);
                    AnnounceArrival(arrivalMsg);
                    DebugLogger.LogState($"NAV auto-walk arrived at '{_autoWalkLabel}'.");
                    return;
                }
            }

            // --- Movement phase: follow CalcHeight A* waypoints ---
            _staticIsAutoWalking = true;

            // Stuck detection: if no progress, try recalculating path.
            _wmStuckTimer += Time.deltaTime;
            if (_wmStuckTimer >= WorldmapStuckCheckInterval)
            {
                float movedDx = playerPos.x - _wmLastStuckCheckPos.x;
                float movedDz = playerPos.z - _wmLastStuckCheckPos.z;
                float movedSq = movedDx * movedDx + movedDz * movedDz;
                if (movedSq < WorldmapStuckMinMove * WorldmapStuckMinMove)
                {
                    // Skip-ahead recovery: the player may have slid past the
                    // current waypoint via native wall sliding. Check if any
                    // future waypoint is nearby and skip to it.
                    bool skippedAhead = false;
                    if (_wmPathWaypoints != null &&
                        _wmPathIndex < _wmPathWaypoints.Length - 1)
                    {
                        int maxSkip = Math.Min(WmSkipAheadLookahead,
                            _wmPathWaypoints.Length - _wmPathIndex - 1);
                        for (int skip = 1; skip <= maxSkip; skip++)
                        {
                            Vector3 futureWp = _wmPathWaypoints[_wmPathIndex + skip];
                            float skipDx = futureWp.x - playerPos.x;
                            float skipDz = futureWp.z - playerPos.z;
                            float skipDist = Mathf.Sqrt(skipDx * skipDx + skipDz * skipDz);
                            if (skipDist < WmSkipAheadMaxDist)
                            {
                                DebugLogger.LogState(
                                    $"NAV worldmap: skip-ahead from wp {_wmPathIndex} to " +
                                    $"{_wmPathIndex + skip} (dist={skipDist:F1}).");
                                _wmPathIndex += skip;
                                skippedAhead = true;
                                break;
                            }
                        }
                    }

                    if (skippedAhead)
                    {
                        _wmLastStuckCheckPos = playerPos;
                        _wmStuckTimer = 0f;
                    }
                    // Full recalculation if skip-ahead didn't help.
                    else if (_wmRecalcCount < WmMaxRecalcAttempts)
                    {
                        _wmRecalcCount++;

                        // Record the blocked area so the pathfinder avoids it.
                        // Mark both the player position AND the current waypoint —
                        // the obstacle is somewhere between them.
                        _wmBlockedPositions.Add(playerPos);
                        if (_wmPathWaypoints != null && _wmPathIndex < _wmPathWaypoints.Length)
                            _wmBlockedPositions.Add(_wmPathWaypoints[_wmPathIndex]);
                        DebugLogger.LogState(
                            $"NAV worldmap: stuck (moved {Mathf.Sqrt(movedSq):F1}). " +
                            $"Marking ({playerPos.x:F1},{playerPos.z:F1}) as blocked. " +
                            $"Recalculating path (attempt {_wmRecalcCount}, " +
                            $"{_wmBlockedPositions.Count} blocked zones).");

                        // Diagnostic: log what's around the stuck position.
                        try
                        {
                            var stuckColliders = UnityEngine.Physics.OverlapSphere(
                                playerPos, 5f);
                            if (stuckColliders != null)
                            {
                                foreach (var col in stuckColliders)
                                {
                                    if (col == null || col.isTrigger) continue;
                                    string cName = col.gameObject?.name ?? "?";
                                    int cLayer = col.gameObject?.layer ?? -1;
                                    string cTag = col.gameObject?.tag ?? "?";
                                    // Get closest point on collider to player
                                    Vector3 closest = col.ClosestPoint(playerPos);
                                    float closestDist = Vector3.Distance(playerPos, closest);
                                    DebugLogger.LogState(
                                        $"NAV stuck collider: name=\"{cName}\" " +
                                        $"layer={cLayer} tag=\"{cTag}\" " +
                                        $"closestDist={closestDist:F2} " +
                                        $"closest=({closest.x:F1},{closest.y:F1},{closest.z:F1}) " +
                                        $"bounds={col.bounds.size}");
                                }
                            }

                            // Also log CalcHeight at player pos and toward target.
                            float hHere = GameUtility.CalcHeight(playerPos, out bool sHere, 50f);
                            Vector3 ahead = playerPos + (_autoWalkTarget - playerPos).normalized * 3f;
                            float hAhead = GameUtility.CalcHeight(ahead, out bool sAhead, 50f);
                            DebugLogger.LogState(
                                $"NAV stuck terrain: here={hHere:F2}({(sHere ? "Y" : "N")}) " +
                                $"3m ahead={hAhead:F2}({(sAhead ? "Y" : "N")}) " +
                                $"diff={Mathf.Abs(hAhead - hHere):F2}");
                        }
                        catch { }

                        // Close-range location entry fallback.
                        if (_autoWalkCategoryIndex == CAT_LOCATION &&
                            targetDist <= WorldmapLocationArrivalRadius)
                        {
                            StopAutoWalk();
                            if (TryEnterWorldmapLocation())
                            {
                                AnnounceArrival(Loc.Get("nav_autowalk_entering",
                                    _autoWalkLabel));
                                DebugLogger.LogState(
                                    $"NAV auto-walk entering '{_autoWalkLabel}' via mapjump.");
                            }
                            else
                            {
                                AnnounceArrival(Loc.Get("nav_autowalk_arrived",
                                    _autoWalkLabel));
                                DebugLogger.LogState(
                                    $"NAV auto-walk arrived at '{_autoWalkLabel}' " +
                                    "but no mapjump found.");
                            }
                            return;
                        }

                        var newPath = WorldmapPathfinder.FindPath(
                            playerPos, _autoWalkTarget, _wmBlockedPositions);
                        if (newPath != null && newPath.Length > 0)
                        {
                            _wmPathWaypoints = newPath;
                            _wmPathIndex = 0;
                            _wmLastStuckCheckPos = playerPos;
                            _wmStuckTimer = 0f;
                            DebugLogger.LogState(
                                $"NAV worldmap: recalculated with {newPath.Length} waypoints.");

                            // Log first 10 waypoints of recalculated path.
                            int logCount = Math.Min(newPath.Length, 10);
                            for (int i = 0; i < logCount; i++)
                            {
                                DebugLogger.LogState(
                                    $"NAV WM RECALC wp[{i}]=({newPath[i].x:F1},{newPath[i].y:F1},{newPath[i].z:F1})");
                            }
                        }
                        else
                        {
                            DebugLogger.LogState("NAV worldmap: recalc found no path.");
                            ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", _autoWalkLabel));
                            CancelAutoWalk();
                            return;
                        }
                    }
                    else
                    {
                        DebugLogger.LogState(
                            $"NAV worldmap: stuck after {WmMaxRecalcAttempts} recalcs. Cancelling.");
                        ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", _autoWalkLabel));
                        CancelAutoWalk();
                        return;
                    }
                }
                _wmLastStuckCheckPos = playerPos;
                _wmStuckTimer = 0f;
            }

            // --- Waypoint following ---
            if (_wmPathWaypoints == null || _wmPathWaypoints.Length == 0)
            {
                // No path — fall back to straight line toward target.
                Vector3 fallbackDir = new Vector3(
                    targetDx / targetDist, 0f, targetDz / targetDist);
                ApplyWorldmapMovement(player, fallbackDir, playerPos);
                return;
            }

            // Advance past reached waypoints. Use tighter threshold when
            // consecutive waypoints are close (narrow CharaWall gaps) so the
            // player follows clearance-adjusted waypoints precisely.
            while (_wmPathIndex < _wmPathWaypoints.Length)
            {
                Vector3 wp = _wmPathWaypoints[_wmPathIndex];
                float wpDx = wp.x - playerPos.x;
                float wpDz = wp.z - playerPos.z;
                float wpDist = Mathf.Sqrt(wpDx * wpDx + wpDz * wpDz);

                // Check if next waypoint is close — indicates a narrow gap.
                float threshold = WmWaypointArrivalThreshold;
                if (_wmPathIndex + 1 < _wmPathWaypoints.Length)
                {
                    Vector3 next = _wmPathWaypoints[_wmPathIndex + 1];
                    float nextDx = next.x - wp.x;
                    float nextDz = next.z - wp.z;
                    float nextDist = Mathf.Sqrt(nextDx * nextDx + nextDz * nextDz);
                    if (nextDist < WmGapDetectionDistance)
                        threshold = WmGapWaypointArrivalThreshold;
                }

                if (wpDist <= threshold)
                {
                    _wmPathIndex++;
                    continue;
                }
                break;
            }

            // All waypoints consumed — head straight for target.
            if (_wmPathIndex >= _wmPathWaypoints.Length)
            {
                Vector3 dirToTarget = targetDist > 0.01f
                    ? new Vector3(targetDx / targetDist, 0f, targetDz / targetDist)
                    : Vector3.forward;
                ApplyWorldmapMovement(player, dirToTarget, playerPos);
                return;
            }

            // Walk toward current waypoint.
            Vector3 currentWp = _wmPathWaypoints[_wmPathIndex];
            float cwDx = currentWp.x - playerPos.x;
            float cwDz = currentWp.z - playerPos.z;
            float cwDist = Mathf.Sqrt(cwDx * cwDx + cwDz * cwDz);

            Vector3 moveDir = cwDist > 0.01f
                ? new Vector3(cwDx / cwDist, 0f, cwDz / cwDist)
                : Vector3.forward;

            ApplyWorldmapMovement(player, moveDir, playerPos);
            UpdateCameraFollow(moveDir);

            // Diagnostic logging — once per second.
            _wmDiagTimer += Time.deltaTime;
            if (_wmDiagTimer >= 1f)
            {
                _wmDiagTimer = 0f;
                DebugLogger.LogState(
                    $"NAV WM: wp={_wmPathIndex}/{_wmPathWaypoints.Length} " +
                    $"wpDist={cwDist:F1} targetDist={targetDist:F1} " +
                    $"pos=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");
            }
        }

        /// <summary>Number of LIDAR rays to cast around the player.</summary>
        private const int LidarRayCount = 36;

        /// <summary>Maximum LIDAR sensing range in meters.</summary>
        private const float LidarRange = 3.0f;

        /// <summary>
        /// Distance threshold below which LIDAR gap-finding activates.
        /// When no L22 obstacle is within this range, use normal waypoint direction.
        /// </summary>
        private const float LidarActivationRange = 2.0f;

        /// <summary>
        /// How much to favor the original waypoint direction vs the widest gap.
        /// 0 = pure gap direction, 1 = pure waypoint direction.
        /// </summary>
        private const float LidarWaypointBias = 0.4f;

        /// <summary>
        /// Layer mask combining L22 (obstacles) and L23 (CharaWalls) for LIDAR.
        /// </summary>
        private static readonly int WmLidarLayerMask = (1 << 22) | (1 << 23);

        /// <summary>
        /// Minimum time in seconds to commit to a LIDAR direction before
        /// re-evaluating. Prevents frame-by-frame oscillation between
        /// two equally-scored directions.
        /// </summary>
        private const float LidarCommitTime = 0.5f;

        /// <summary>Current committed LIDAR direction (world-space).</summary>
        private Vector3 _lidarCommittedDir = Vector3.zero;

        /// <summary>Smoothed LIDAR direction using exponential moving average.</summary>
        private Vector3 _lidarSmoothedDir = Vector3.zero;

        /// <summary>
        /// Applies movement on the world map via stick injection.
        /// Directly follows waypoint direction at full speed. Safe approach
        /// waypoints (Point 3) keep the player away from obstacle rings,
        /// so complex local avoidance is not needed.
        /// </summary>
        private void ApplyWorldmapMovement(FieldPlayer player, Vector3 moveDir,
            Vector3 playerPos)
        {
            _wmDirectMoveActive = false;
            _staticAutoWalkStickDir = WorldDirToCameraStick(moveDir);
        }

        // LIDAR code preserved but disabled — kept for potential future use.
        private void ApplyWorldmapMovement_Lidar(FieldPlayer player, Vector3 moveDir,
            Vector3 playerPos)
        {
            _wmDirectMoveActive = false;

            // Check if we need LIDAR — any L22 obstacle within activation range?
            bool needsLidar = false;
            try
            {
                var nearby = UnityEngine.Physics.OverlapSphere(
                    playerPos, LidarActivationRange, WmObstacleLayerMask);
                if (nearby != null)
                {
                    foreach (var col in nearby)
                    {
                        if (col != null && !col.isTrigger)
                        {
                            float dist = Vector3.Distance(
                                playerPos, col.ClosestPoint(playerPos));
                            if (dist < LidarActivationRange)
                            {
                                needsLidar = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            if (!needsLidar)
            {
                // Open area — full speed, direct waypoint direction.
                _staticAutoWalkStickDir = WorldDirToCameraStick(moveDir);
                return;
            }

            // --- LIDAR gap-finding ---
            // Cast rays in all directions to map nearby obstacles.
            // Find the best direction: widest gap closest to waypoint direction.
            float[] clearance = new float[LidarRayCount];
            float angleStep = 360f / LidarRayCount;
            Vector3 rayOrigin = playerPos + Vector3.up * 0.5f;

            for (int i = 0; i < LidarRayCount; i++)
            {
                float angle = i * angleStep;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 rayDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

                RaycastHit hit;
                if (UnityEngine.Physics.Raycast(rayOrigin, rayDir, out hit,
                    LidarRange, WmLidarLayerMask))
                {
                    clearance[i] = hit.distance;
                }
                else
                {
                    clearance[i] = LidarRange;
                }
            }

            // --- Cone-constrained gap finding ---
            // Only consider directions within a cone around the waypoint heading.
            // Start with ±45°. If everything is blocked, expand to ±90°.
            // Never go more than 90° off the waypoint — let the A* recalculate
            // rather than sending the player in the wrong direction.
            float desiredAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            if (desiredAngle < 0f) desiredAngle += 360f;

            float bestScore = -1f;
            int bestIdx = -1;
            float usedCone = 45f;

            // Try narrow cone first, then wider if needed.
            float[] coneWidths = { 45f, 90f };
            foreach (float coneHalf in coneWidths)
            {
                bestScore = -1f;
                bestIdx = -1;

                for (int i = 0; i < LidarRayCount; i++)
                {
                    float rayAngle = i * angleStep;
                    float angleDiff = Mathf.Abs(
                        Mathf.DeltaAngle(rayAngle, desiredAngle));

                    // Hard reject directions outside the cone.
                    if (angleDiff > coneHalf) continue;

                    // Hard reject directions with very low clearance.
                    if (clearance[i] < 0.6f) continue;

                    // Clearance score with neighbor check for wide gaps.
                    float clrScore = clearance[i] / LidarRange;
                    float neighborSum = 0f;
                    for (int n = -2; n <= 2; n++)
                    {
                        if (n == 0) continue;
                        int ni = (i + n + LidarRayCount) % LidarRayCount;
                        neighborSum += clearance[ni] / LidarRange;
                    }
                    float gapScore = (clrScore * 2f + neighborSum) / 6f;

                    // Within the cone, prefer directions closest to waypoint.
                    float alignScore = 1f - (angleDiff / coneHalf);

                    // Combine: mostly clearance, some alignment.
                    float score = gapScore * 0.6f + alignScore * 0.4f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0)
                {
                    usedCone = coneHalf;
                    break; // Found a passable direction in this cone.
                }
            }

            // If even ±90° has nothing, fall back to waypoint direction.
            Vector3 finalDir;
            float finalAngleDeg;
            if (bestIdx >= 0)
            {
                finalAngleDeg = bestIdx * angleStep;
                float rad = finalAngleDeg * Mathf.Deg2Rad;
                finalDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            }
            else
            {
                finalDir = moveDir;
                finalAngleDeg = desiredAngle;
            }

            // Slow down when navigating near obstacles for precision.
            Vector2 stick = WorldDirToCameraStick(finalDir);
            stick *= 0.5f;
            _staticAutoWalkStickDir = stick;

            // Log LIDAR decision periodically (reuse diag timer).
            if (_wmDiagTimer < 0.05f)
            {
                DebugLogger.LogState(
                    $"NAV WM LIDAR: desired={desiredAngle:F0}° " +
                    $"best={finalAngleDeg:F0}° " +
                    $"cone=±{usedCone:F0}° " +
                    $"clr={(bestIdx >= 0 ? clearance[bestIdx] : 0):F2}m " +
                    $"score={bestScore:F2}");
            }
        }

        #endregion

        #region World Map Location Entry

        /// <summary>
        /// Finds the nearest FieldMapjumpCollision to the auto-walk target and
        /// triggers it to enter the location manually via ChangeFieldmap().
        /// Retained as a potential fallback — not called from normal auto-walk
        /// flow since stick injection lets the player walk into trigger colliders
        /// naturally (OnTriggerEnter fires and handles the map change).
        /// Returns true if a mapjump was found and triggered.
        /// </summary>
        private bool TryEnterWorldmapLocation()
        {
            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                {
                    DebugLogger.LogState(
                        "NAV worldmap enter: no FieldMapjumpCollision objects found.");
                    return false;
                }

                FieldMapjumpCollision nearest = null;
                float nearestDist = float.MaxValue;

                // Use _wmOriginalTarget (pre-safe-approach) to find the correct
                // trigger, since _autoWalkTarget may have been substituted with a
                // safe approach point further away from the trigger.
                Vector3 triggerSearchPos = _wmOriginalTarget != Vector3.zero
                    ? _wmOriginalTarget : _autoWalkTarget;

                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    float dist = Vector3.Distance(
                        c.transform.position, triggerSearchPos);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = c;
                    }
                }

                if (nearest == null)
                {
                    DebugLogger.LogState(
                        "NAV worldmap enter: no valid FieldMapjumpCollision.");
                    return false;
                }

                DebugLogger.LogState(
                    $"NAV worldmap enter: triggering mapjump " +
                    $"dist={nearestDist:F1} fieldmap={nearest.fieldmapID} " +
                    $"pos=({nearest.transform.position.x:F1}," +
                    $"{nearest.transform.position.z:F1})");

                return nearest.ChangeFieldmap();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV worldmap enter error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
