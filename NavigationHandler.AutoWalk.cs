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

            // Accept a partial NavMesh path when the target is behind a barrier
            // (counter NPCs) or on a different floor (significant Y difference means
            // separate NavMesh surfaces connected only by stairs/ramps — the player
            // walks as far as the current floor allows, typically toward the stairs).
            bool differentFloor = Mathf.Abs(item.Position.y - playerPos.y) > FloorChangeThreshold;
            bool pathFound;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, item.Position,
                    allowPartial: item.IsCounterNpc || differentFloor);
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
            _autoWalkTransform     = item.LiveTransform; // may be null for exits
            _autoWalkIsCounter     = item.IsCounterNpc;
            _autoWalkCategoryIndex = _currentCategoryIndex;
            _isAutoWalking       = true;
            _autoWalkArrived     = false;
            _staticIsApproaching = true;

            // Initialize world map stuck detection.
            if (_isWorldmap)
            {
                _wmStuckTimer        = 0f;
                _wmLastStuckCheckPos = playerPos;
            }

            // Close the list — the player is now running, not browsing.
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();

            // Query the player's actual run speed and start the run animation.
            // _staticIsApproaching=true means the Harmony prefix will block the game
            // from resetting the Run animation to Idle each frame.
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player != null)
                {
                    _autoWalkSpeed = player.GetMoveSpeed(true);
                    DebugLogger.LogState($"NAV auto-run: speed={_autoWalkSpeed:F1}");
                    player.PlayMoveAnimation(FieldAnimationKind.Run);
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
            _autoWalkIsCounter     = false;
            _autoWalkCategoryIndex = 0;
            _autoWalkTransform     = null;
            _staticIsApproaching = false; // re-enable normal animation resets
            _pathCorners         = null;
            _pathCornerIndex     = 0;
            _pathRecalcTimer     = 0f;
            _isWorldmap          = false;
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
        private bool IsReachable(Vector3 playerPos, Vector3 targetPos)
        {
            // World map: use CalcHeight path sampling to detect ocean barriers.
            if (_isWorldmap)
                return WorldmapIsReachableViaCalcHeight(playerPos, targetPos);

            try
            {
                if (!NavMesh.SamplePosition(playerPos, out NavMeshHit playerHit,
                        NavMeshSampleRadius, NavMesh.AllAreas))
                    return true; // no NavMesh near player — fallback, don't filter

                if (!NavMesh.SamplePosition(targetPos, out NavMeshHit targetHit,
                        NavMeshSampleRadius, NavMesh.AllAreas))
                {
                    DebugLogger.LogState(
                        $"NAV: IsReachable=false — target not on NavMesh " +
                        $"(target={targetPos}, sampleRadius={NavMeshSampleRadius})");
                    return false; // target not near any NavMesh surface — unreachable
                }

                NavMesh.CalculatePath(playerHit.position, targetHit.position,
                    NavMesh.AllAreas, _navPath);
                if (_navPath.status != NavMeshPathStatus.PathComplete)
                {
                    float directDist = Vector3.Distance(playerPos, targetPos);
                    DebugLogger.LogState(
                        $"NAV: IsReachable=false — path status={_navPath.status}, " +
                        $"directDist={directDist:F1}, " +
                        $"player={playerHit.position}, target={targetHit.position}");
                }
                return _navPath.status == NavMeshPathStatus.PathComplete;
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

            if (!NavMesh.SamplePosition(playerPos, out NavMeshHit playerHit,
                    NavMeshSampleRadius, NavMesh.AllAreas))
                return false;

            if (!NavMesh.SamplePosition(targetPos, out NavMeshHit targetHit,
                    NavMeshSampleRadius, NavMesh.AllAreas))
                return false;

            NavMesh.CalculatePath(playerHit.position, targetHit.position,
                NavMesh.AllAreas, _navPath);

            if (_navPath.status == NavMeshPathStatus.PathInvalid)
                return false;

            if (_navPath.status == NavMeshPathStatus.PathPartial && !allowPartial)
                return false;

            // Copy corners from IL2CPP array into managed array.
            var il2cppCorners = _navPath.corners;
            _pathCorners = new Vector3[il2cppCorners.Length];
            for (int i = 0; i < il2cppCorners.Length; i++)
                _pathCorners[i] = il2cppCorners[i];

            // Start walking toward index 1 (index 0 is the start position).
            _pathCornerIndex = _pathCorners.Length > 1 ? 1 : 0;
            _pathRecalcTimer = 0f;

            DebugLogger.LogState(
                $"NAV path: {_pathCorners.Length} waypoints, status={_navPath.status}");
            return true;
        }
    }
}
