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

            // Allow the hard map-exit barrier to be bypassed ONLY when the target
            // itself is an exit (the player explicitly wants to walk through it).
            _autoWalkAllowExit = IsExitCategory(_currentCategoryIndex);

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

            // Cross-area routing is handled inside CalculateAndStorePath
            // (complete NavMesh path, else recorded traversal). The old NavMesh
            // island/bridge multi-segment system is retired.

            // Same island (or no island graph) — use standard single-path auto-walk.
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
                ScreenReader.Say(Loc.Get(
                    _lastPathBlockedByExit
                        ? "nav_autowalk_route_exits"
                        : "nav_autowalk_unreachable",
                    item.Label));
                return;
            }

            _crossingExitZones     = null; // single-target walk: no exit steering
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
            _lidarSmoothedDir       = Vector3.zero;
            _lidarCommittedDir      = Vector3.zero;
            _pathCorners                = null;
            _pathCornerIndex            = 0;
            _pathRecalcTimer            = 0f;
            _fieldStuckTimer            = 0f;
            _fieldStuckRecalcAttempted  = false;
            _isAvoidingObstacle         = false;
            _avoidanceAttempt           = 0;
            _isWorldmap                 = false;
            _routeSegments              = null;
            _routeSegmentIndex          = 0;
            _routeIsSpeculative         = false;
            _isCrossingPhase            = false;
            _crossingTimer              = 0f;
            _crossingExitZones          = null;
            DebugLogger.LogState("NAV auto-walk cancelled.");
        }

        /// <summary>
        /// Starts a multi-segment auto-walk across multiple islands.
        /// Calculates the first segment's NavMesh path and begins walking.
        /// </summary>
        private void StartMultiSegmentWalk(NavItem item, Vector3 playerPos,
            RouteResult route)
        {
            _routeSegments = route.Segments;
            _routeSegmentIndex = 0;
            _routeIsSpeculative = route.HasSpeculativeSegments;
            _isCrossingPhase = false;
            _routeFinalTarget = item.Position;
            _routeFinalLabel = item.Label;

            // Cache map-exit trigger zones so the crossing phase can steer
            // around them (a straight-line crossing must not trip a transition).
            CacheCrossingExitZones(item.Position);

            // Set up core auto-walk state.
            _autoWalkTarget = _routeSegments[0].WalkTarget;
            _autoWalkLabel = item.Label;
            LastAutoWalkTarget = item.Position;
            LastAutoWalkLabel = item.Label;
            _autoWalkTransform = null; // Multi-segment doesn't track live transforms per segment.
            _autoWalkIsCounter = false;
            _autoWalkEventRef = null;
            _autoWalkTriggerBounds = null;
            _autoWalkFacePosition = null;
            _autoWalkDifferentFloor = true; // Cross-island = different elevation.
            _autoWalkCategoryIndex = _currentCategoryIndex;

            // Calculate NavMesh path for the first segment.
            bool pathFound;
            try
            {
                pathFound = CalculateAndStorePath(playerPos,
                    _routeSegments[0].WalkTarget, allowPartial: true);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV multi-segment: first segment path failed: {ex.Message}");
                ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", item.Label));
                return;
            }

            if (!pathFound)
            {
                ScreenReader.Say(Loc.Get(
                    _lastPathBlockedByExit
                        ? "nav_autowalk_route_exits"
                        : "nav_autowalk_unreachable",
                    item.Label));
                DebugLogger.LogState(
                    $"NAV multi-segment refused for '{item.Label}' " +
                    $"(blockedByExit={_lastPathBlockedByExit}).");
                return;
            }

            _isAutoWalking = true;
            _autoWalkArrived = false;
            _staticIsAutoWalking = true;
            _fieldStuckTimer = 0f;
            _fieldLastStuckCheckPos = playerPos;
            _fieldStuckRecalcAttempted = false;
            _isAvoidingObstacle = false;
            _avoidanceAttempt = 0;

            // Close the navigation list.
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();

            // Get run speed.
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                _autoWalkSpeed = player != null ? player.GetMoveSpeed(true) : 10f;
            }
            catch { _autoWalkSpeed = 10f; }

            int crossings = _routeSegments.Count;
            if (_routeIsSpeculative)
            {
                ScreenReader.Say(Loc.Get("nav_island_exploring", item.Label,
                    route.SpeculativeCount.ToString()));
            }
            else
            {
                ScreenReader.Say(Loc.Get("nav_island_route", item.Label,
                    crossings.ToString()));
            }

            DebugLogger.LogState(
                $"NAV multi-segment started: '{item.Label}', " +
                $"{crossings} segments, speculative={_routeIsSpeculative}");
        }

        /// <summary>
        /// True if the given NavMesh path would carry the player THROUGH a
        /// map-exit trigger (FieldMapjumpCollision). Samples densely along each
        /// path segment and tests against every exit collider's bounds (expanded
        /// by <see cref="MapExitBarrierMargin"/>). Used as a hard barrier so a
        /// route to a non-exit target can never walk the player out of the area.
        /// </summary>
        private bool PathCrossesMapExit(Vector3[] corners)
        {
            if (corners == null || corners.Length < 2) return false;
            try
            {
                var exits = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
                if (exits == null || exits.Length == 0) return false;

                var zones = new List<Bounds>();
                foreach (var exit in exits)
                {
                    if (exit == null) continue;
                    var col = exit.GetComponent<Collider>();
                    if (col == null) continue;
                    var b = col.bounds;
                    b.Expand(MapExitBarrierMargin * 2f); // Expand() takes full size
                    zones.Add(b);
                }
                if (zones.Count == 0) return false;

                for (int i = 0; i < corners.Length - 1; i++)
                {
                    Vector3 a = corners[i];
                    Vector3 b = corners[i + 1];
                    float segLen = Vector3.Distance(a, b);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / 0.75f));
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, (float)s / steps);
                        for (int z = 0; z < zones.Count; z++)
                        {
                            if (zones[z].Contains(p))
                            {
                                DebugLogger.LogState(
                                    $"NAV: path crosses map-exit zone at " +
                                    $"({p.x:F1},{p.y:F1},{p.z:F1}).");
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: PathCrossesMapExit error: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Returns the set of island IDs that contain a map-exit trigger
        /// (FieldMapjumpCollision). Route planning avoids passing through these
        /// islands so the player isn't walked into a map transition.
        /// </summary>
        private HashSet<int> GetExitIslandSet()
        {
            var set = new HashSet<int>();
            if (!_islandNav.HasGraph) return set;
            try
            {
                var exits = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
                if (exits == null) return set;
                foreach (var exit in exits)
                {
                    if (exit == null) continue;
                    int isl = _islandNav.GetIsland(exit.transform.position);
                    if (isl >= 0) set.Add(isl);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: GetExitIslandSet failed: {ex.Message}");
            }

            if (set.Count > 0)
                DebugLogger.LogState(
                    $"NAV: exit-bearing islands to avoid as transit: " +
                    string.Join(",", set));
            return set;
        }

        /// <summary>
        /// Caches the world-space bounds of every map-exit trigger
        /// (FieldMapjumpCollision) on the current map, so the crossing phase
        /// can steer around them. Zones that sit near a route waypoint (the
        /// destination may legitimately be next to an exit) are excluded.
        /// </summary>
        private void CacheCrossingExitZones(Vector3 finalTarget)
        {
            _crossingExitZones = null;
            try
            {
                var exits = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
                if (exits == null || exits.Length == 0) return;

                var zones = new List<Bounds>();
                foreach (var exit in exits)
                {
                    if (exit == null) continue;
                    var col = exit.GetComponent<Collider>();
                    if (col == null) continue;

                    Bounds b = col.bounds;

                    // Skip exits adjacent to a route waypoint or the final
                    // target — those are part of where we're going, not hazards.
                    if (IsNearRouteWaypoint(b.center, finalTarget))
                        continue;

                    zones.Add(b);
                }

                if (zones.Count > 0)
                {
                    _crossingExitZones = zones;
                    DebugLogger.LogState(
                        $"NAV crossing: cached {zones.Count} exit zone(s) to avoid.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV crossing: exit zone cache failed: {ex.Message}");
                _crossingExitZones = null;
            }
        }

        /// <summary>
        /// True if <paramref name="pos"/> is within
        /// <see cref="ExitZoneWaypointExclusion"/> of any route walk target,
        /// arrival point, or the final target (XZ distance only).
        /// </summary>
        private bool IsNearRouteWaypoint(Vector3 pos, Vector3 finalTarget)
        {
            float r2 = ExitZoneWaypointExclusion * ExitZoneWaypointExclusion;
            if (FlatSqrDistance(pos, finalTarget) <= r2) return true;

            if (_routeSegments != null)
            {
                foreach (var seg in _routeSegments)
                {
                    if (FlatSqrDistance(pos, seg.WalkTarget) <= r2) return true;
                    if (FlatSqrDistance(pos, seg.ArrivalPoint) <= r2) return true;
                }
            }
            return false;
        }

        /// <summary>Squared XZ-plane distance between two points (ignores Y).</summary>
        private static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Adjusts a desired walk direction to steer around cached map-exit
        /// trigger zones. Uses a TANGENTIAL deflection (arc around the zone)
        /// plus a mild direct push when very close, so a head-on approach slips
        /// past the gate instead of stalling against it. Returns the input
        /// direction unchanged when no zones are near.
        /// </summary>
        private Vector3 AvoidExitZones(Vector3 playerPos, Vector3 desiredDir)
        {
            if (_crossingExitZones == null || _crossingExitZones.Count == 0)
                return desiredDir;

            Vector3 steer = desiredDir;
            foreach (var zone in _crossingExitZones)
            {
                // Closest point on the zone to the player, in the XZ plane.
                Vector3 closest = zone.ClosestPoint(playerPos);
                Vector3 away = new Vector3(
                    playerPos.x - closest.x, 0f, playerPos.z - closest.z);
                float d = away.magnitude;
                if (d >= ExitZoneAvoidRadius) continue;

                float strength = (ExitZoneAvoidRadius - d) / ExitZoneAvoidRadius; // 0..1

                // Which side of the zone is the player on? Used to pick the
                // tangent that arcs AWAY from the zone center.
                Vector3 fromCenter = new Vector3(
                    playerPos.x - zone.center.x, 0f, playerPos.z - zone.center.z);
                if (fromCenter.sqrMagnitude < 0.0001f)
                    fromCenter = new Vector3(-desiredDir.z, 0f, desiredDir.x);

                // Tangent perpendicular to the desired direction, chosen on the
                // side that points away from the zone center → player arcs around.
                Vector3 perp = new Vector3(-desiredDir.z, 0f, desiredDir.x);
                if (Vector3.Dot(perp, fromCenter) < 0f) perp = -perp;

                steer += perp * (strength * ExitZoneAvoidWeight);

                // Direct push-away grows quadratically only at very close range,
                // to prevent actually touching the trigger.
                Vector3 awayDir = d > 0.01f ? away / d : fromCenter.normalized;
                steer += awayDir * (strength * strength * ExitZoneAvoidWeight);
            }

            steer.y = 0f;
            if (steer.sqrMagnitude < 0.0001f)
                return desiredDir;
            return steer.normalized;
        }

        /// <summary>
        /// Called from Update() when a multi-segment route is active.
        /// Handles transitions between segments: detects when the current
        /// segment's path is exhausted, starts the crossing phase, detects
        /// island arrival, and starts the next segment.
        /// Returns true if this method handled the frame (caller should skip
        /// normal auto-walk processing).
        /// </summary>
        private bool CheckSegmentTransition(Vector3 playerPos)
        {
            if (_routeSegments == null) return false;

            // --- Crossing phase: walking toward bridge/gap edge ---
            if (_isCrossingPhase)
            {
                _crossingTimer += Time.deltaTime;

                var seg = _routeSegments[_routeSegmentIndex];
                int currentIsland = _islandNav.GetIsland(playerPos);

                // Check if we've arrived on the target island.
                if (currentIsland == seg.ToIsland)
                {
                    // Crossing successful!
                    _isCrossingPhase = false;

                    if (!seg.IsConfirmed)
                    {
                        ScreenReader.Say(Loc.Get("nav_island_crossing_confirmed"));
                        DebugLogger.LogState(
                            $"NAV crossing confirmed: island {seg.FromIsland} -> {seg.ToIsland}");
                    }

                    // Advance to next segment.
                    _routeSegmentIndex++;

                    if (_routeSegmentIndex >= _routeSegments.Count)
                    {
                        // All crossings done. Walk to the final target.
                        StartFinalSegment(playerPos);
                        return true;
                    }

                    // Start next segment's NavMesh path.
                    StartNextSegment(playerPos);
                    return true;
                }

                // Check timeout.
                float timeout = seg.IsConfirmed
                    ? ConfirmedCrossingTimeout
                    : SpeculativeCrossingTimeout;

                if (_crossingTimer >= timeout)
                {
                    if (!seg.IsConfirmed)
                    {
                        // Speculative crossing failed — mark gap as blocked.
                        _islandNav.MarkGapBlocked(seg.FromIsland, seg.ToIsland);
                        ScreenReader.Say(Loc.Get("nav_island_crossing_blocked"));
                        DebugLogger.LogState(
                            $"NAV crossing blocked: island {seg.FromIsland} -> " +
                            $"{seg.ToIsland} (gap marked blocked)");
                    }
                    else
                    {
                        ScreenReader.Say(Loc.Get("nav_island_crossing_stuck"));
                        DebugLogger.LogState(
                            $"NAV crossing stuck: island {seg.FromIsland} -> " +
                            $"{seg.ToIsland} (confirmed bridge, timeout)");
                    }
                    CancelAutoWalk();
                    return true;
                }

                // Still crossing — inject stick input toward the arrival point,
                // steering around any map-exit trigger zones in the way.
                Vector3 crossTarget = seg.ArrivalPoint;
                Vector3 toTarget = crossTarget - playerPos;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Vector3 dir = AvoidExitZones(playerPos, toTarget.normalized);
                    _staticAutoWalkStickDir = WorldDirToCameraStick(dir);
                }
                return true;
            }

            // --- Not in crossing phase: check if current segment path is exhausted ---
            if (_pathCorners != null && _pathCornerIndex >= _pathCorners.Length)
            {
                // Current segment path exhausted. Enter crossing phase.
                var seg = _routeSegments[_routeSegmentIndex];
                _isCrossingPhase = true;
                _crossingTimer = 0f;

                if (!seg.IsConfirmed)
                {
                    ScreenReader.Say(Loc.Get("nav_island_crossing_attempt"));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("nav_island_crossing"));
                }

                DebugLogger.LogState(
                    $"NAV entering crossing phase: island {seg.FromIsland} -> " +
                    $"{seg.ToIsland}, confirmed={seg.IsConfirmed}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Calculates and starts walking the next segment's NavMesh path.
        /// </summary>
        private void StartNextSegment(Vector3 playerPos)
        {
            var seg = _routeSegments[_routeSegmentIndex];
            int remaining = _routeSegments.Count - _routeSegmentIndex;

            ScreenReader.Say(Loc.Get("nav_island_continuing", _routeFinalLabel,
                remaining.ToString()));

            // Calculate NavMesh path to this segment's walk target.
            _autoWalkTarget = seg.WalkTarget;
            bool pathFound = false;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, seg.WalkTarget,
                    allowPartial: true);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV multi-segment: segment path failed: {ex.Message}");
            }

            if (!pathFound)
            {
                // Can't pathfind on this island — try walking directly.
                DebugLogger.LogState(
                    "NAV multi-segment: no path for segment, entering crossing directly.");
                _isCrossingPhase = true;
                _crossingTimer = 0f;
                return;
            }

            // Reset stuck detection for the new segment.
            _fieldStuckTimer = 0f;
            _fieldLastStuckCheckPos = playerPos;
            _fieldStuckRecalcAttempted = false;
            _isAvoidingObstacle = false;
            _avoidanceAttempt = 0;
        }

        /// <summary>
        /// All crossings complete. Calculate final NavMesh path to the
        /// actual target (chest, NPC, etc.) on the destination island.
        /// </summary>
        private void StartFinalSegment(Vector3 playerPos)
        {
            ScreenReader.Say(Loc.Get("nav_island_final", _routeFinalLabel));
            DebugLogger.LogState(
                $"NAV multi-segment: all crossings done, final walk to '{_routeFinalLabel}'");

            // Switch to normal single-path auto-walk for the final target.
            _routeSegments = null;
            _routeSegmentIndex = 0;
            _isCrossingPhase = false;
            _autoWalkTarget = _routeFinalTarget;
            _autoWalkDifferentFloor = false; // Now on the same island.

            bool pathFound = false;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, _routeFinalTarget,
                    allowPartial: true);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV multi-segment: final path failed: {ex.Message}");
            }

            if (!pathFound)
            {
                ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", _routeFinalLabel));
                CancelAutoWalk();
                return;
            }

            // Reset stuck detection.
            _fieldStuckTimer = 0f;
            _fieldLastStuckCheckPos = playerPos;
            _fieldStuckRecalcAttempted = false;
            _isAvoidingObstacle = false;
            _avoidanceAttempt = 0;
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
            _lidarSmoothedDir    = Vector3.zero;
            _lidarCommittedDir   = Vector3.zero;
            _isAvoidingObstacle  = false;
            _pathCorners         = null;
            _crossingExitZones   = null;
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

        /// <summary>
        /// Probe offsets (XZ) around the player used to overcome NavMesh
        /// fragmentation: the player's exact spot can snap to a tiny disconnected
        /// sliver even when the main walkable mesh — and a full path — is a meter
        /// or two away. We try the exact spot first, then nearby points.
        /// </summary>
        private static readonly Vector3[] _completePathProbes =
        {
            Vector3.zero,
            new Vector3( 1.5f, 0f,  0f), new Vector3(-1.5f, 0f,  0f),
            new Vector3( 0f,   0f,  1.5f), new Vector3( 0f,   0f, -1.5f),
            new Vector3( 1.5f, 0f,  1.5f), new Vector3(-1.5f, 0f,  1.5f),
            new Vector3( 1.5f, 0f, -1.5f), new Vector3(-1.5f, 0f, -1.5f),
            new Vector3( 3f,   0f,  0f), new Vector3(-3f,   0f,  0f),
            new Vector3( 0f,   0f,  3f), new Vector3( 0f,   0f, -3f),
        };

        /// <summary>
        /// Tries hard to find a COMPLETE NavMesh path from the player to the
        /// target, leaving the result in <paramref name="result"/>. Because the
        /// dungeon NavMesh is fragmented, the player's exact position can snap to
        /// a disconnected sliver; so we probe several nearby points (same floor)
        /// and accept the first snap that yields a complete path. This turns many
        /// false "no route" cases into real, reliable walks. Returns false if no
        /// probe produces a complete path.
        /// </summary>
        private bool TryFindCompletePath(Vector3 playerPos, Vector3 targetPos,
            NavMeshPath result)
        {
            if (!SampleNavMeshFloorAware(targetPos, out NavMeshHit targetHit))
                return false;

            for (int i = 0; i < _completePathProbes.Length; i++)
            {
                Vector3 probe = playerPos + _completePathProbes[i];
                if (!NavMesh.SamplePosition(probe, out NavMeshHit pHit,
                    1.5f, NavMesh.AllAreas))
                    continue;
                // Keep the snap on the player's own level.
                if (Mathf.Abs(pHit.position.y - playerPos.y) > FloorChangeThreshold)
                    continue;

                NavMesh.CalculatePath(pHit.position, targetHit.position,
                    NavMesh.AllAreas, result);
                if (result.status == NavMeshPathStatus.PathComplete)
                {
                    if (i != 0)
                        DebugLogger.LogState(
                            $"NAV: complete path found via probe offset " +
                            $"({_completePathProbes[i].x:F1},{_completePathProbes[i].z:F1}) " +
                            "— player was snapped to a disconnected sliver.");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Copies an IL2CPP NavMeshPath corner array into a managed array.</summary>
        private static Vector3[] CopyCorners(NavMeshPath path)
        {
            var il = path.corners;
            var arr = new Vector3[il.Length];
            for (int i = 0; i < il.Length; i++) arr[i] = il[i];
            return arr;
        }

        /// <summary>
        /// Returns true if a COMPLETE NavMesh path connects the two positions,
        /// probing around the player to overcome NavMesh fragmentation. When a
        /// complete path exists a normal single-path walk is correct (and avoids
        /// bogus multi-segment routes through map exits). Returns false on
        /// partial/invalid paths or any error.
        /// </summary>
        private bool HasCompleteNavMeshPath(Vector3 playerPos, Vector3 targetPos)
        {
            try
            {
                bool complete = TryFindCompletePath(playerPos, targetPos, _navPath);
                if (complete)
                    DebugLogger.LogState(
                        "NAV: NavMesh path is COMPLETE — using direct walk, " +
                        "skipping island routing.");
                return complete;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV: HasCompleteNavMeshPath error: {ex.Message}");
                return false;
            }
        }

        private bool IsReachable(Vector3 playerPos, Vector3 targetPos)
        {
            // World map: use CalcHeight path sampling to detect ocean barriers.
            if (_isWorldmap)
                return WorldmapIsReachableViaCalcHeight(playerPos, targetPos);

            // Reliable, in priority order: a complete NavMesh path (towns and
            // connected areas), then a recorded traversal route (dungeons the
            // player has actually walked).
            if (HasCompleteNavMeshPath(playerPos, targetPos))
                return true;
            if (UseTraversal() && _traversal.IsReachable(playerPos, targetPos))
                return true;
            return false;
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

            Vector3[] corners;
            bool fromTraversal = false;
            _lastPathBlockedByExit = false;

            // 1. A COMPLETE NavMesh path (towns / connected areas) — smooth and
            //    reliable. Probe around the player to beat NavMesh fragmentation.
            if (TryFindCompletePath(playerPos, targetPos, _navPath))
            {
                corners = CopyCorners(_navPath);
            }
            // 2. A recorded traversal route (dungeons the player has walked). The
            //    waypoints are real walked positions, so the route is walkable.
            else if (UseTraversal() &&
                     _traversal.FindPath(playerPos, targetPos, out var tc) && tc != null)
            {
                corners = tc;
                fromTraversal = true;
            }
            // 3. Partial NavMesh path, only when allowed (e.g. counter NPCs).
            else
            {
                if (!allowPartial) return false;
                if (!SampleNavMeshFloorAware(playerPos, out NavMeshHit playerHit))
                    return false;
                if (!SampleNavMeshFloorAware(targetPos, out NavMeshHit targetHit))
                    return false;
                NavMesh.CalculatePath(playerHit.position, targetHit.position,
                    NavMesh.AllAreas, _navPath);
                if (_navPath.status == NavMeshPathStatus.PathInvalid)
                    return false;
                corners = CopyCorners(_navPath);
            }

            // Hard map-exit barrier for NavMesh paths. Traversal paths are skipped
            // — they are real walked routes, so they never leave the area.
            if (!fromTraversal && !_autoWalkAllowExit && PathCrossesMapExit(corners))
            {
                _lastPathBlockedByExit = true;
                DebugLogger.LogState(
                    "NAV: path rejected — it would cross a map exit.");
                return false;
            }

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
