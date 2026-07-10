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

        /// <summary>Max distance to show chests on the world map.</summary>
        private const float WorldmapChestMaxDistance = 200f;

        /// <summary>Max distance to show enemies on the world map.</summary>
        private const float WorldmapEnemyMaxDistance = 150f;

        // --- Tight-terrain following (cave-mouth / rocky pinch threading) ---
        // The grid pathfinder already routes correctly through body-width gaps; the
        // problem was EXECUTION — full-speed waypoint following overshoots the
        // gap-centered waypoints and clips the rocks, and stuck-recovery skip-ahead
        // jumps several metres through a rock. When the player is near obstacle walls
        // we slow down and stop skipping so the player tracks the grid's thread.

        /// <summary>
        /// Layers carrying world-map obstacle walls (Col_Obstacle): Obstacle (22) +
        /// CharaWall (23) — the layers the stuck dumps and SpatialSensor both use.
        /// (The pre-validate's WmObstacleLayerMask is layer 22 only; the rocks at the
        /// Krosse Cave mouth sit on BOTH, so the tight probe checks both.)
        /// </summary>
        private const int WmTightProbeMask = (1 << 22) | (1 << 23);

        /// <summary>
        /// Radius (m) to probe for nearby obstacle walls. Within this the player is
        /// threading tight terrain and switches to precise + slow following.
        /// </summary>
        private const float WmTightProbeRadius = 2.5f;

        /// <summary>
        /// Stick magnitude (fraction of full run) applied while tight, so the player
        /// tracks the 0.5 m gap-centered waypoints instead of overshooting into rocks.
        /// </summary>
        private const float WmTightSpeedScale = 0.5f;

        /// <summary>
        /// Skip-ahead max jump while tight — small, so stuck-recovery can only hop to
        /// an immediately-adjacent waypoint, never jump metres ahead through a rock.
        /// </summary>
        private const float WmTightSkipAheadMaxDist = 1.0f;

        /// <summary>How often (frames) to re-probe for nearby walls. Cheap throttle.</summary>
        private const int WmTightProbeIntervalFrames = 6;

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

        /// <summary>Timer for world map stuck detection during auto-walk.</summary>
        private float _wmStuckTimer;

        /// <summary>Player position at the last stuck check, for distance comparison.</summary>
        private Vector3 _wmLastStuckCheckPos;

        /// <summary>Timer for diagnostic logging — logs once per second.</summary>
        private float _wmDiagTimer;

        /// <summary>
        /// True when the player is currently near world-map obstacle walls (threading a
        /// cave mouth / rocky pinch). Drives precise + slow waypoint following. Refreshed
        /// by <see cref="UpdateTightTerrain"/> every <see cref="WmTightProbeIntervalFrames"/>.
        /// </summary>
        private bool _wmTightTerrain;

        /// <summary>Frame counter throttling the tight-terrain wall probe.</summary>
        private int _wmTightProbeCounter;

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
                // Locations: arrive ONLY when the game shows THIS location's "Press X to enter"
                // prompt — i.e. the player has entered the location's enter-trigger ring (a
                // navigable EventCollision distinct from the physical wall) and can actually
                // enter. Each location's ring has its own size, so a fixed distance would stop
                // too early/late; the prompt is the per-location truth. The prompt must match
                // the target: a town merely passed en route must NOT count as arrival.
                if (EnterPromptMatchesTarget())
                {
                    ArriveAtWorldmapLocation("enter prompt shown");
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

            // Refresh tight-terrain state (near obstacle walls?) before stuck-recovery
            // and movement use it. Throttled internally.
            UpdateTightTerrain(playerPos);

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
                        // In tight terrain, only hop to an immediately-adjacent waypoint —
                        // a longer jump would aim the stick metres ahead, straight through
                        // the rock the skipped waypoints were routing around.
                        float skipMax = _wmTightTerrain
                            ? WmTightSkipAheadMaxDist : WmSkipAheadMaxDist;
                        for (int skip = 1; skip <= maxSkip; skip++)
                        {
                            Vector3 futureWp = _wmPathWaypoints[_wmPathIndex + skip];
                            float skipDx = futureWp.x - playerPos.x;
                            float skipDz = futureWp.z - playerPos.z;
                            float skipDist = Mathf.Sqrt(skipDx * skipDx + skipDz * skipDz);
                            if (skipDist < skipMax)
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

                        // If we got stuck but THIS location's enter prompt is already up, the
                        // player is in the enter-trigger ring — announce arrival rather than
                        // recalculating. Distance alone never counts as arrival (the ring, not a
                        // fixed radius, defines "close enough to enter"); a genuine stuck short of
                        // the ring falls through to recalc/unreachable below.
                        if (_autoWalkCategoryIndex == CAT_LOCATION &&
                            EnterPromptMatchesTarget())
                        {
                            ArriveAtWorldmapLocation("enter prompt shown (stuck)");
                            return;
                        }

                        // Mode re-queried per recalc: dismounting mid-walk
                        // makes the next recalc re-plan on the foot lane.
                        // Recalc goal: locations MUST re-plan to the stored
                        // ring point (_wmPathGoal), never the town-centre
                        // symbol — the centre sits INSIDE the walls, so a
                        // centre-aimed re-plan always collapses to a
                        // wall-hugging floor route (proven by goal-cell
                        // clearance logs, 2026-07-10).
                        Vector3 recalcGoal =
                            _autoWalkCategoryIndex == CAT_LOCATION
                                ? _wmPathGoal : _autoWalkTarget;
                        var newPath = WorldmapPathfinder.FindPath(
                            playerPos, recalcGoal,
                            WorldmapTravel.CurrentMode(),
                            _wmBlockedPositions);
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
                    $"tight={_wmTightTerrain} " +
                    $"speed={(_wmTightTerrain ? WmTightSpeedScale : 1f):F2} " +
                    $"pos=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");
            }
        }

        /// <summary>
        /// Applies movement on the world map via stick injection, following the grid
        /// path's heading. Runs at full speed in open terrain; in tight terrain (near
        /// obstacle walls) the stick magnitude is scaled down (<see cref="WmTightSpeedScale"/>)
        /// so the player tracks the 0.5 m gap-centered waypoints precisely instead of
        /// overshooting and clipping the rocks at a cave mouth / pinch.
        /// </summary>
        private void ApplyWorldmapMovement(FieldPlayer player, Vector3 moveDir,
            Vector3 playerPos)
        {
            _wmDirectMoveActive = false;
            Vector2 stick = WorldDirToCameraStick(moveDir);
            if (_wmTightTerrain)
                stick *= WmTightSpeedScale;
            _staticAutoWalkStickDir = stick;
        }

        /// <summary>
        /// Refreshes <see cref="_wmTightTerrain"/>: true when a non-trigger obstacle wall
        /// (Col_Obstacle on layers 22/23) is within <see cref="WmTightProbeRadius"/> of the
        /// player. In tight terrain the follower slows down and stops skipping waypoints so
        /// it tracks the grid's gap-centered thread precisely (e.g. threading a cave mouth)
        /// instead of overshooting into rocks. Throttled to one probe every
        /// <see cref="WmTightProbeIntervalFrames"/> frames; keeps the last value on a probe
        /// failure. Logs each enter/leave transition for diagnostics.
        /// </summary>
        private void UpdateTightTerrain(Vector3 playerPos)
        {
            if (++_wmTightProbeCounter < WmTightProbeIntervalFrames)
                return;
            _wmTightProbeCounter = 0;

            try
            {
                var hits = UnityEngine.Physics.OverlapSphere(
                    playerPos, WmTightProbeRadius, WmTightProbeMask);
                bool tight = false;
                int wallCount = 0;
                if (hits != null)
                {
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var c = hits[i];
                        if (c == null || c.isTrigger) continue;
                        tight = true;
                        wallCount++;
                    }
                }

                if (tight != _wmTightTerrain)
                    DebugLogger.LogState(
                        $"NAV WM tight-terrain {(tight ? "ENTER" : "LEAVE")} " +
                        $"(walls within {WmTightProbeRadius:F1}m={wallCount}) — " +
                        $"{(tight ? "slowing + no skip-ahead" : "full speed")}.");

                _wmTightTerrain = tight;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM tight-probe error: {ex.Message}");
            }
        }

        #endregion

        #region World Map Location Entry

        /// <summary>
        /// Ends world-map auto-walk at a location: stops movement and announces arrival.
        /// Does NOT enter the location — the player decides whether to enter and presses the
        /// game's own "X to enter" button themselves (the prompt is read on appearance). Shared
        /// by the distance/prompt arrival check and the stuck-recovery fallback.
        /// </summary>
        /// <param name="reason">Short diagnostic note on what triggered arrival.</param>
        private void ArriveAtWorldmapLocation(string reason)
        {
            StopAutoWalk();
            AnnounceArrival(Loc.Get("nav_autowalk_arrived", _autoWalkLabel));
            DebugLogger.LogState(
                $"NAV auto-walk arrived at '{_autoWalkLabel}' ({reason}).");
        }

        /// <summary>
        /// True only when the world-map "Press X to enter" prompt currently on screen belongs
        /// to the location we are auto-walking to. The prompt signal is global, so this guards
        /// against a town we merely pass (e.g. Krosse City) tripping arrival for a distant target
        /// (e.g. Mountain Palace). Nav labels may add a suffix like " (Dungeon)", so the match is
        /// containment in either direction, case-insensitive.
        /// </summary>
        private bool EnterPromptMatchesTarget()
        {
            if (!FieldPromptHandler.EnterPromptShowing) return false;

            string promptLabel = FieldPromptHandler.EnterPromptLabel;
            if (string.IsNullOrEmpty(promptLabel) || string.IsNullOrEmpty(_autoWalkLabel))
                return false;

            string a = _autoWalkLabel.Trim();
            string b = promptLabel.Trim();
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0
                || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
