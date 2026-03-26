using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        /// <summary>Cached camera forward direction for deterministic world map stick conversion.</summary>
        private static Vector3 _wmLockedCamForward;

        /// <summary>True when the world map camera angle is locked for auto-walk.</summary>
        private static bool _wmCameraLocked;

        /// <summary>
        /// Starts auto-walking to the currently highlighted navigation item.
        /// Calculates a NavMesh path to the target and walks along waypoints via Update().
        /// Announces an error and aborts if no path can be found.
        /// Called by NumPad 5.
        /// </summary>
        public void AutoWalkTo()
        {
            if (!_isOpen) return;
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0 || _currentItemIndex >= cat.Count) return;

            var item = cat[_currentItemIndex];

            // Calculate a NavMesh path before committing to the walk.
            Vector3 playerPos;
            try
            {
                var p = FieldManager.Instance?.GetControlPlayer();
                if (p == null)
                {
                    ScreenReader.Say(Loc.Get("nav_not_in_field"));
                    return;
                }
                playerPos = p.transform.position;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV AutoWalkTo: player fetch failed: {ex.Message}");
                ScreenReader.Say(Loc.Get("nav_not_in_field"));
                return;
            }

            // Accept partial NavMesh paths when the target is behind a barrier
            // (counter NPCs), on a different floor, or on a disconnected NavMesh
            // surface that passed IsReachable (e.g. nearby exit with small Y gap).
            // Always allow partial — IsReachable already filtered truly unreachable
            // targets. Refusing partial here would block items that passed the filter.
            bool pathFound;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, item.Position,
                    allowPartial: true);
            }
            catch (Exception ex)
            {
                // NavMesh API completely unavailable — announce and abort.
                DebugLogger.LogState($"NAV AutoWalkTo: NavMesh error: {ex.Message}");
                ScreenReader.Say(Loc.Get("nav_autowalk_no_navmesh"));
                return;
            }

            if (!pathFound)
            {
                ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", item.Label));
                return;
            }

            _autoWalkTarget        = item.Position;
            _autoWalkLabel         = item.Label;
            LastAutoWalkTarget     = item.Position;
            LastAutoWalkLabel      = item.Label;
            _autoWalkTransform     = item.LiveTransform; // may be null for exits
            _autoWalkIsCounter      = item.IsCounterNpc;
            _autoWalkEventRef       = item.EventRef;
            _autoWalkTriggerBounds  = item.TriggerBounds;
            _autoWalkFacePosition   = item.FacePosition;
            _autoWalkDifferentFloor = Mathf.Abs(item.Position.y - playerPos.y) >= FloorChangeThreshold;
            _autoWalkCategoryIndex = _currentCategoryIndex;
            _isAutoWalking       = true;
            _autoWalkArrived     = false;
            _staticIsAutoWalking = true;

            // Initialize stuck detection.
            if (_isWorldmap)
            {
                _wmStuckTimer        = 0f;
                _wmLastStuckCheckPos = playerPos;
            }
            else
            {
                _fieldStuckTimer            = 0f;
                _fieldLastStuckCheckPos     = playerPos;
                _fieldStuckRecalcAttempted  = false;
                _isAvoidingObstacle         = false;
                _avoidanceAttempt           = 0;
            }

            // Close the list — the player is now running, not browsing.
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();

            // Query the player's actual run speed (used for world map movement
            // and as a fallback). Field map movement is handled by the game's own
            // pipeline via GetLeftStick injection — no manual speed needed.
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player != null)
                {
                    _autoWalkSpeed = player.GetMoveSpeed(true);
                    DebugLogger.LogState($"NAV auto-run: speed={_autoWalkSpeed:F1}");
                }
                else
                {
                    _autoWalkSpeed = 10.0f; // fallback if player unavailable
                }
            }
            catch (Exception ex)
            {
                _autoWalkSpeed = 10.0f;
                DebugLogger.LogState($"NAV AutoWalkTo: run setup failed: {ex.Message}");
            }

            ScreenReader.Say(Loc.Get("nav_autowalk_start", item.Label));
            DebugLogger.LogState(
                $"NAV auto-walk started. target={item.Label} " +
                $"pos=({item.Position.x:F1},{item.Position.y:F1},{item.Position.z:F1}) " +
                $"waypoints={_pathCorners.Length}");
        }

        /// <summary>
        /// Cancels an active auto-walk silently.
        /// Called by manual input, scene change, or when the field becomes busy.
        /// No announcement — the "Arrived" message handles successful completion,
        /// and manual cancellation needs no confirmation (player initiated it).
        /// </summary>
        public void CancelAutoWalk()
        {
            if (!_isAutoWalking) return;
            _isAutoWalking         = false;
            _autoWalkArrived       = false;
            _autoWalkIsCounter      = false;
            _autoWalkEventRef       = null;
            _autoWalkTriggerBounds  = null;
            _autoWalkDifferentFloor = false;
            _autoWalkCategoryIndex = 0;
            _autoWalkTransform     = null;
            _staticIsAutoWalking = false;
            _staticAutoWalkStickDir = Vector2.zero;
            _staticCameraStickX     = 0f;
            _wmDirectMoveActive     = false;
            _wmCameraLocked         = false;
            _lidarSmoothedDir       = Vector3.zero;
            _lidarCommittedDir      = Vector3.zero;
            _lidarCommitTimer       = 0f;
            _pathCorners                = null;
            _pathCornerIndex            = 0;
            _pathRecalcTimer            = 0f;
            _fieldStuckTimer            = 0f;
            _fieldStuckRecalcAttempted  = false;
            _isAvoidingObstacle         = false;
            _avoidanceAttempt           = 0;
            _isWorldmap                 = false;
            DebugLogger.LogState("NAV auto-walk cancelled.");
        }

        /// <summary>
        /// Announces an arrival message. If another message was spoken within
        /// the last half second (e.g. a tutorial popup), the arrival is combined
        /// with that message so the user hears both: arrival first, then the
        /// interrupted message replayed after it.
        /// </summary>
        private void AnnounceArrival(string arrivalText)
        {
            string recent = ScreenReader.GetRecentMessage(ArrivalRecentWindow);
            if (recent != null)
            {
                ScreenReader.Say(arrivalText + " " + recent);
                DebugLogger.LogState(
                    $"NAV arrival combined with recent message: '{recent}'");
            }
            else
            {
                ScreenReader.Say(arrivalText);
            }
        }

        /// <summary>
        /// Returns true if the given category index is an exit-type target
        /// (exits, stairs, doors, warps) where a compass direction hint is useful.
        /// </summary>
        private static bool IsExitCategory(int categoryIndex) =>
            categoryIndex == CAT_EXIT     || categoryIndex == CAT_STAIRS ||
            categoryIndex == CAT_DOOR     || categoryIndex == CAT_WARP   ||
            categoryIndex == CAT_LOCATION;

        /// <summary>
        /// Converts a world-space direction vector into a camera-relative stick Vector2.
        /// The game interprets left stick input relative to camera orientation:
        /// stick Y+ = camera forward, stick X+ = camera right.
        /// Projects the world direction onto the camera's XZ-plane forward/right axes,
        /// then returns a normalized Vector2 (magnitude 1.0 = full run speed).
        /// </summary>
        private static Vector2 WorldDirToCameraStick(Vector3 worldDir)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                // No camera — fall back to raw world direction.
                return new Vector2(worldDir.x, worldDir.z).normalized;
            }

            // Camera forward/right projected onto XZ plane.
            Vector3 camFwd = cam.transform.forward;
            camFwd.y = 0f;
            camFwd.Normalize();
            Vector3 camRight = cam.transform.right;
            camRight.y = 0f;
            camRight.Normalize();

            // Dot product gives the component along each camera axis.
            float stickY = worldDir.x * camFwd.x + worldDir.z * camFwd.z;   // forward component
            float stickX = worldDir.x * camRight.x + worldDir.z * camRight.z; // right component

            Vector2 stick = new Vector2(stickX, stickY);
            float mag = stick.magnitude;
            if (mag > 0.001f)
                stick /= mag; // normalize to magnitude 1.0 (full run)
            return stick;
        }

        /// <summary>
        /// Stops auto-walk input injection and clears the auto-walk state.
        /// Used at arrival points to cleanly end the walk.
        /// </summary>
        private void StopAutoWalk()
        {
            _isAutoWalking       = false;
            _staticIsAutoWalking = false;
            _staticAutoWalkStickDir = Vector2.zero;
            _staticCameraStickX  = 0f;
            _wmDirectMoveActive  = false;
            _wmCameraLocked      = false;
            _lidarSmoothedDir    = Vector3.zero;
            _lidarCommittedDir   = Vector3.zero;
            _lidarCommitTimer    = 0f;
            _isAvoidingObstacle  = false;
            _pathCorners         = null;
        }

        /// <summary>
        /// Computes a compass direction string (e.g. "North", "South East")
        /// from the player toward the target.
        /// When <paramref name="worldRelative"/> is false (default, used on field maps),
        /// directions are camera-relative: "North" = camera forward = stick up.
        /// When true (used on the world map), directions are world-relative:
        /// "North" = Z+, "East" = X+, matching the fixed map orientation.
        /// </summary>
        private static string GetCompassDirection(Vector3 playerPos, Vector3 targetPos,
            bool worldRelative = false)
        {
            float dx = targetPos.x - playerPos.x;
            float dz = targetPos.z - playerPos.z;

            if (!worldRelative)
            {
                // Project onto camera-relative axes so "North" = camera forward = stick up.
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 camFwd = cam.transform.forward;
                    camFwd.y = 0f;
                    camFwd.Normalize();
                    Vector3 camRight = cam.transform.right;
                    camRight.y = 0f;
                    camRight.Normalize();

                    // forward component = how far "North" (camera forward) the exit is
                    float fwd   = dx * camFwd.x   + dz * camFwd.z;
                    // right component = how far "East" (camera right) the exit is
                    float right = dx * camRight.x  + dz * camRight.z;

                    dx = right;
                    dz = fwd;
                }
            }
            // else: world-relative — use raw dx/dz where Z+ = North, X+ = East.

            // Angle in degrees: 0 = North (forward), 90 = East (right)
            float angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            // 8 compass directions, each spanning 45 degrees
            if (angle < 22.5f  || angle >= 337.5f) return "North";
            if (angle < 67.5f)  return "North East";
            if (angle < 112.5f) return "East";
            if (angle < 157.5f) return "South East";
            if (angle < 202.5f) return "South";
            if (angle < 247.5f) return "South West";
            if (angle < 292.5f) return "West";
            return "North West";
        }

        /// <summary>
        /// Checks whether a complete NavMesh path exists between two world positions.
        /// Both positions are snapped to the nearest NavMesh surface within
        /// <see cref="NavMeshSampleRadius"/> before path calculation.
        /// Returns true as a fallback if NavMesh is unavailable (scene has none).
        /// </summary>
        /// <summary>
        /// Samples the NavMesh at <paramref name="pos"/> with floor-awareness.
        /// First tries the full <see cref="NavMeshSampleRadius"/>. If the result
        /// snaps to a different floor (Y differs by more than
        /// <see cref="FloorChangeThreshold"/>), retries with a tight radius (1.0)
        /// to stay on the correct floor's NavMesh surface.
        /// Returns false if no NavMesh point is found at all.
        /// </summary>
        private bool SampleNavMeshFloorAware(Vector3 pos, out NavMeshHit hit)
        {
            // First try with tight radius to prefer the correct floor.
            if (NavMesh.SamplePosition(pos, out hit, 1.0f, NavMesh.AllAreas))
            {
                if (Mathf.Abs(hit.position.y - pos.y) <= FloorChangeThreshold)
                    return true;
            }

            // Tight radius missed — try full radius.
            if (!NavMesh.SamplePosition(pos, out hit, NavMeshSampleRadius, NavMesh.AllAreas))
                return false;

            // Log when the full-radius result is on a different elevation.
            // Use the sampled NavMesh position as-is — CalculatePath will
            // determine connectivity. Overriding Y creates a position off
            // the NavMesh surface, causing PathInvalid false negatives.
            if (Mathf.Abs(hit.position.y - pos.y) > FloorChangeThreshold)
            {
                DebugLogger.LogState(
                    $"NAV: SampleNavMesh floor difference " +
                    $"(requested Y={pos.y:F1}, sampled Y={hit.position.y:F1}). " +
                    "Using sampled NavMesh position for pathfinding.");
            }

            return true;
        }

        private bool IsReachable(Vector3 playerPos, Vector3 targetPos)
        {
            // World map: use CalcHeight path sampling to detect ocean barriers.
            if (_isWorldmap)
                return WorldmapIsReachableViaCalcHeight(playerPos, targetPos);

            try
            {
                if (!SampleNavMeshFloorAware(playerPos, out NavMeshHit playerHit))
                    return true; // no NavMesh near player — fallback, don't filter

                if (!SampleNavMeshFloorAware(targetPos, out NavMeshHit targetHit))
                {
                    DebugLogger.LogState(
                        $"NAV: IsReachable=false — target not on NavMesh " +
                        $"(target={targetPos}, sampleRadius={NavMeshSampleRadius})");
                    return false; // target not near any NavMesh surface — unreachable
                }

                NavMesh.CalculatePath(playerHit.position, targetHit.position,
                    NavMesh.AllAreas, _navPath);

                if (_navPath.status == NavMeshPathStatus.PathComplete)
                    return true;

                if (_navPath.status == NavMeshPathStatus.PathPartial)
                {
                    // Partial paths occur when NavMesh surfaces are disconnected
                    // (different floors, stairs, or small elevation changes like
                    // the Krosse Guild entrance). Accept partial paths when:
                    //  1. Target is on a clearly different floor (Y >= threshold), OR
                    //  2. The partial path makes meaningful progress — endpoint is
                    //     significantly closer to the target than the player is.
                    bool differentFloor =
                        Mathf.Abs(targetPos.y - playerPos.y) >= FloorChangeThreshold;
                    if (differentFloor)
                        return true;

                    var corners = _navPath.corners;
                    if (corners.Length > 0)
                    {
                        Vector3 pathEnd = corners[corners.Length - 1];
                        float playerToTarget = Vector3.Distance(playerHit.position,
                            targetHit.position);
                        float pathEndToTarget = Vector3.Distance(pathEnd,
                            targetHit.position);
                        // Accept if the path gets at least 30% closer to the target.
                        if (pathEndToTarget < playerToTarget * 0.7f)
                            return true;
                    }
                }

                if (_navPath.status != NavMeshPathStatus.PathComplete)
                {
                    float directDist = Vector3.Distance(playerPos, targetPos);
                    DebugLogger.LogState(
                        $"NAV: IsReachable=false — path status={_navPath.status}, " +
                        $"directDist={directDist:F1}, " +
                        $"player={playerHit.position}, target={targetHit.position}");
                }
                return false;
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogState($"NAV: IsReachable exception: {ex.Message}");
                // NavMesh API unavailable — preserve current behavior, don't filter.
                return true;
            }
        }

        /// <summary>
        /// Calculates a NavMesh path from the player to the target and populates
        /// <see cref="_pathCorners"/> and <see cref="_pathCornerIndex"/>.
        /// Returns true if a usable path was found.
        /// When <paramref name="allowPartial"/> is true, a partial path is accepted
        /// — the player walks as close as the NavMesh allows (e.g. to a counter).
        /// </summary>
        private bool CalculateAndStorePath(Vector3 playerPos, Vector3 targetPos,
            bool allowPartial = false)
        {
            // World map has no NavMesh — use the game's A* pathfinder instead.
            if (_isWorldmap) return WorldmapCalculateAndStorePath(playerPos, targetPos);

            if (!SampleNavMeshFloorAware(playerPos, out NavMeshHit playerHit))
                return false;

            if (!SampleNavMeshFloorAware(targetPos, out NavMeshHit targetHit))
                return false;

            NavMesh.CalculatePath(playerHit.position, targetHit.position,
                NavMesh.AllAreas, _navPath);

            if (_navPath.status == NavMeshPathStatus.PathInvalid)
                return false;

            if (_navPath.status == NavMeshPathStatus.PathPartial && !allowPartial)
                return false;

            // Copy corners from IL2CPP array into managed array.
            var il2cppCorners = _navPath.corners;
            var corners = new Vector3[il2cppCorners.Length];
            for (int i = 0; i < il2cppCorners.Length; i++)
                corners[i] = il2cppCorners[i];

            _pathCorners = corners;
            _pathCornerIndex = corners.Length > 1 ? 1 : 0;
            _pathRecalcTimer = 0f;

            LogPath(corners);
            return true;
        }

        /// <summary>Logs the accepted path details.</summary>
        private static void LogPath(Vector3[] corners)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"NAV path: {corners.Length} waypoints");
            for (int i = 0; i < corners.Length; i++)
                sb.Append($" [{i}]=({corners[i].x:F1},{corners[i].y:F1},{corners[i].z:F1})");
            DebugLogger.LogState(sb.ToString());
        }

        /// <summary>
        /// Attempts to find a walkable detour point on the NavMesh to route around
        /// an obstacle blocking the current path. Tries perpendicular directions
        /// (alternating left/right) at increasing distances.
        /// Returns true if a valid detour was found and avoidance mode is active.
        /// </summary>
        private bool TryStartObstacleAvoidance(Vector3 playerPos)
        {
            // Determine the direction we were heading toward.
            Vector3 headingDir;
            if (_pathCorners != null && _pathCornerIndex < _pathCorners.Length)
            {
                headingDir = _pathCorners[_pathCornerIndex] - playerPos;
            }
            else
            {
                headingDir = _autoWalkTarget - playerPos;
            }
            headingDir.y = 0f;
            if (headingDir.sqrMagnitude < 0.001f)
                return false;
            headingDir.Normalize();

            // Snap the target onto NavMesh so CalculatePath can find it.
            // Without this, different-floor targets (Y far from NavMesh surface)
            // cause PathInvalid for every candidate, making detours always fail.
            if (!SampleNavMeshFloorAware(_autoWalkTarget, out NavMeshHit targetHit))
            {
                DebugLogger.LogState("NAV obstacle avoidance: target not on NavMesh.");
                return false;
            }
            Vector3 navTarget = targetHit.position;

            // Perpendicular direction (right of heading).
            Vector3 rightDir = new Vector3(headingDir.z, 0f, -headingDir.x);

            _avoidanceAttempt++;

            // Build candidate directions: alternate starting left/right,
            // include diagonal-backward options to handle corners.
            Vector3[] directions;
            if (_avoidanceAttempt % 2 == 1)
            {
                directions = new[]
                {
                    rightDir, -rightDir,
                    (-headingDir + rightDir).normalized,
                    (-headingDir - rightDir).normalized,
                    -headingDir
                };
            }
            else
            {
                directions = new[]
                {
                    -rightDir, rightDir,
                    (-headingDir - rightDir).normalized,
                    (-headingDir + rightDir).normalized,
                    -headingDir
                };
            }

            float[] distances = { 3f, 5f, 8f };

            foreach (var dir in directions)
            {
                foreach (var dist in distances)
                {
                    Vector3 candidate = playerPos + dir * dist;
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                        continue;

                    // Verify a path exists from the detour point to the destination.
                    NavMesh.CalculatePath(hit.position, navTarget, NavMesh.AllAreas, _navPath);
                    if (_navPath.status == NavMeshPathStatus.PathInvalid)
                        continue;
                    if (_navPath.status == NavMeshPathStatus.PathPartial
                        && !_autoWalkIsCounter && !_autoWalkDifferentFloor)
                        continue;

                    // Valid detour found.
                    _avoidanceDetourTarget = hit.position;
                    _isAvoidingObstacle = true;
                    _avoidanceStartTime = Time.time;
                    _fieldLastStuckCheckPos = playerPos;
                    _fieldStuckTimer = 0f;
                    _fieldStuckRecalcAttempted = false;

                    DebugLogger.LogState(
                        $"NAV obstacle avoidance: detour " +
                        $"{Vector3.Distance(playerPos, hit.position):F1}m away, " +
                        $"attempt {_avoidanceAttempt}");
                    return true;
                }
            }

            DebugLogger.LogState(
                $"NAV obstacle avoidance: no valid detour found " +
                $"(tested {directions.Length * distances.Length} candidates).");
            return false;
        }

        /// <summary>
        /// Computes camera follow stick input so the camera gently rotates
        /// to face the walking direction. Uses the cross product between
        /// the camera forward and the movement direction to determine
        /// which way to rotate.
        /// </summary>
        private static void UpdateCameraFollow(Vector3 worldMoveDir)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                _staticCameraStickX = 0f;
                return;
            }

            Vector3 camFwd = cam.transform.forward;
            camFwd.y = 0f;
            camFwd.Normalize();

            // Cross product: positive when moveDir is to the right of camera forward.
            float cross = camFwd.x * worldMoveDir.z - camFwd.z * worldMoveDir.x;

            // Dead zone to prevent jitter when nearly aligned.
            if (Mathf.Abs(cross) < CameraFollowDeadZone)
            {
                _staticCameraStickX = 0f;
                return;
            }

            // Scale and clamp — negative because camera rotates opposite to stick.
            _staticCameraStickX = Mathf.Clamp(-cross * CameraFollowScale, -1f, 1f);
        }
    }
}
