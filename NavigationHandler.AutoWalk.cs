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
        #region Field Battle-Resume State

        // Mirrors the world map battle-resume (see NavigationHandler.Worldmap.cs),
        // but field maps get interrupted by many things that are NOT battles
        // (dialogue, cutscenes, menus). So a saved resume is only "pending" until
        // its cause is classified: it resumes after a battle, and is discarded for
        // every other interruption (see UpdateFieldResume / ResumeFieldAutoWalk).

        /// <summary>True when a field auto-walk was interrupted and may resume.</summary>
        private bool _fieldResumePending;

        /// <summary>True once a battle was detected during the pending window.</summary>
        private bool _fieldResumeBattleSeen;

        /// <summary>
        /// Consecutive frames that IsFieldFree returned false during a field
        /// auto-walk. Tolerates brief flicker (event transitions, and the
        /// post-battle return settling) so a 1-frame blip doesn't cancel the walk
        /// — in particular, it must not re-cancel a freshly resumed walk.
        /// </summary>
        private int _fieldFreeFailCount;

        /// <summary>Seconds the field has been continuously free while pending.</summary>
        private float _fieldResumeFreeTimer;

        /// <summary>Saved auto-walk target snapshot for the resume.</summary>
        private Vector3 _fieldResumeTarget;
        private string _fieldResumeLabel;
        private int _fieldResumeCategoryIndex;
        private Transform _fieldResumeTransform;
        private Vector3? _fieldResumeFacePosition;
        private bool _fieldResumeIsCounter;
        private FieldEventCollision _fieldResumeEventRef;
        private Bounds? _fieldResumeTriggerBounds;
        private FieldmapID _fieldResumeMapId;

        /// <summary>
        /// How long (seconds) the field may stay free without a battle appearing
        /// before a pending resume is discarded as a non-battle interruption.
        /// Long enough to cover the brief encounter-transition gap before the
        /// battle scene loads.
        /// </summary>
        private const float FieldResumeDiscardDelay = 0.6f;

        #endregion

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

            // A new explicit walk supersedes any pending battle-resume.
            ClearFieldResume();

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
            // (complete NavMesh path, else recorded traversal).
            // Accept partial NavMesh paths when the target is behind a barrier
            // (counter NPCs), on a different floor, or on a disconnected NavMesh
            // surface that passed IsReachable (e.g. nearby exit with small Y gap).
            // Always allow partial — IsReachable already filtered truly unreachable
            // targets. Refusing partial here would block items that passed the filter.
            // For world-map locations, walk to the enter-trigger RING (where the
            // "Press X to enter" prompt fires), not the location centre — so the
            // pathfinder keeps the model wall impassable and still reaches the entrance.
            Vector3 walkTarget = item.Position;
            if (_isWorldmap && _currentCategoryIndex == CAT_LOCATION)
                walkTarget = ComputeEnterTriggerTarget(item.Position, playerPos);

            bool pathFound;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, walkTarget,
                    allowPartial: true, isCounter: item.IsCounterNpc);
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
                string failKey = _lastPathBlockedByExit
                    ? "nav_autowalk_route_exits"
                    : (_isWorldmap && WorldmapPathfinder.LastNoPathWasDisconnected)
                        ? "nav_autowalk_no_land_route"
                        : "nav_autowalk_unreachable";
                ScreenReader.Say(Loc.Get(failKey, item.Label));
                return;
            }

            _autoWalkTarget        = walkTarget;
            _autoWalkLabel         = item.Label;
            LastAutoWalkTarget     = walkTarget;
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
                _wmTightTerrain      = false;
                _wmTightProbeCounter = 0;
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

            _spatialSensor.Reset(); // fresh wedge/obstacle state for this walk

            // Arm NPC-aware carving: carvers are placed on the first field-update frame
            // (UpdateFieldCarvers), then the path is recomputed once they've cut the mesh
            // (the path above was computed on the un-carved mesh). World map excluded.
            _carveRefreshTimer  = 0f;
            _carvePeriodicTimer = 0f;
            _carveWedgeRecalcs  = 0;
            _carveForceRecalcAt = (!_isWorldmap && ModSettings.NpcAwarePathfindingEnabled)
                ? Time.time + CarveSettleDelay
                : 0f;

            // Fresh carve-oscillation (livelock) state for this walk.
            ResetLivelockState();

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
            _pathCorners                = null;
            _pathCornerIndex            = 0;
            _pathRecalcTimer            = 0f;
            _fieldStuckTimer            = 0f;
            _fieldStuckRecalcAttempted  = false;
            _isAvoidingObstacle         = false;
            _avoidanceAttempt           = 0;
            _isWorldmap                 = false;
            // Stop carving (also covers scene change — Main.OnSceneWasLoaded cancels).
            _carverPool.DeactivateAll();
            _carveForceRecalcAt         = 0f;
            _carveRefreshTimer          = 0f;
            _carvePeriodicTimer         = 0f;
            _carveWedgeRecalcs          = 0;
            // Clear livelock state + lift carve suppression so the next walk carves again.
            ResetLivelockState();
            DebugLogger.LogState("NAV auto-walk cancelled.");
        }

        #region Field Battle-Resume

        /// <summary>
        /// Returns true if a battle is currently active. Uses the same signal as
        /// the bonus gauge handler: BattleManager exists with a populated player list.
        /// </summary>
        private static bool IsBattleActive()
        {
            try
            {
                var bm = BattleManager.Instance;
                if (bm == null) return false;
                var players = bm.battlePlayerList;
                return players != null && players.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Snapshots the current field auto-walk target so it can be resumed after
        /// a battle. Must be called BEFORE <see cref="CancelAutoWalk"/> (which clears
        /// the live auto-walk fields). The resume only actually fires if a battle is
        /// detected during the pending window — see <see cref="UpdateFieldResume"/>.
        /// </summary>
        private void SaveFieldResume()
        {
            _fieldResumePending       = true;
            _fieldResumeBattleSeen    = false;
            _fieldResumeFreeTimer     = 0f;
            _fieldResumeTarget        = _autoWalkTarget;
            _fieldResumeLabel         = _autoWalkLabel;
            _fieldResumeCategoryIndex = _autoWalkCategoryIndex;
            _fieldResumeTransform     = _autoWalkTransform;
            _fieldResumeFacePosition  = _autoWalkFacePosition;
            _fieldResumeIsCounter     = _autoWalkIsCounter;
            _fieldResumeEventRef      = _autoWalkEventRef;
            _fieldResumeTriggerBounds = _autoWalkTriggerBounds;
            try { _fieldResumeMapId = FieldManager.Instance?.currentFieldmapID ?? FieldmapID.INVALID; }
            catch { _fieldResumeMapId = FieldmapID.INVALID; }
            DebugLogger.LogState(
                $"NAV field: interruption, saving potential resume for '{_autoWalkLabel}'.");
        }

        /// <summary>Clears a pending field resume without resuming.</summary>
        private void ClearFieldResume()
        {
            _fieldResumePending    = false;
            _fieldResumeBattleSeen = false;
            _fieldResumeFreeTimer  = 0f;
            _fieldResumeTransform  = null;
            _fieldResumeEventRef   = null;
        }

        /// <summary>
        /// Per-frame handler for a pending field auto-walk resume. Called from
        /// Update() while a resume is pending and auto-walk is not running.
        /// Classifies the interruption: if a battle becomes active it resumes once
        /// the battle ends and the field is free again; any other interruption
        /// (dialogue, cutscene, menu) is discarded after a short grace period.
        /// </summary>
        private void UpdateFieldResume()
        {
            try
            {
                if (IsBattleActive())
                {
                    _fieldResumeBattleSeen = true;
                    _fieldResumeFreeTimer = 0f;
                }

                if (!IsFieldFree())
                {
                    _fieldResumeFreeTimer = 0f;
                    return;
                }

                // Field is free again. Discard if the player left the original map
                // (e.g. a story warp after the fight) — resuming elsewhere is wrong.
                var fm = FieldManager.Instance;
                if (fm != null && _fieldResumeMapId != FieldmapID.INVALID
                    && fm.currentFieldmapID != _fieldResumeMapId)
                {
                    DebugLogger.LogState("NAV field resume: map changed, discarding.");
                    ClearFieldResume();
                    return;
                }

                if (_fieldResumeBattleSeen)
                {
                    // Battle finished and the field is usable again — resume.
                    ResumeFieldAutoWalk();
                    return;
                }

                // No battle seen yet. Wait out a short grace period (covers the
                // encounter-transition gap before the battle scene loads) before
                // concluding this was a non-battle interruption and dropping it.
                _fieldResumeFreeTimer += Time.deltaTime;
                if (_fieldResumeFreeTimer >= FieldResumeDiscardDelay)
                {
                    DebugLogger.LogState("NAV field resume: non-battle interruption, discarding.");
                    ClearFieldResume();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV field resume error: {ex.Message}");
                ClearFieldResume();
            }
        }

        /// <summary>
        /// Restores the saved field auto-walk target, re-routes from the player's
        /// current position, and resumes walking with a spoken confirmation.
        /// </summary>
        private void ResumeFieldAutoWalk()
        {
            var fm = FieldManager.Instance;
            var player = fm?.GetControlPlayer();
            if (player == null) { ClearFieldResume(); return; }

            Vector3 playerPos = player.transform.position;

            // The live transform may have been destroyed during the battle —
            // fall back to the saved position.
            Vector3 target = _fieldResumeTarget;
            if (_fieldResumeTransform != null)
            {
                try { target = _fieldResumeTransform.position; }
                catch { _fieldResumeTransform = null; }
            }

            _autoWalkAllowExit = IsExitCategory(_fieldResumeCategoryIndex);

            bool pathFound;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, target,
                    allowPartial: true, isCounter: _fieldResumeIsCounter);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV field resume: path error: {ex.Message}");
                ClearFieldResume();
                return;
            }

            if (!pathFound)
            {
                DebugLogger.LogState("NAV field resume: no path after battle, discarding.");
                ClearFieldResume();
                return;
            }

            _autoWalkTarget         = target;
            _autoWalkLabel          = _fieldResumeLabel;
            LastAutoWalkTarget      = target;
            LastAutoWalkLabel       = _fieldResumeLabel;
            _autoWalkTransform      = _fieldResumeTransform;
            _autoWalkIsCounter      = _fieldResumeIsCounter;
            _autoWalkEventRef       = _fieldResumeEventRef;
            _autoWalkTriggerBounds  = _fieldResumeTriggerBounds;
            _autoWalkFacePosition   = _fieldResumeFacePosition;
            _autoWalkDifferentFloor = Mathf.Abs(target.y - playerPos.y) >= FloorChangeThreshold;
            _autoWalkCategoryIndex  = _fieldResumeCategoryIndex;
            _isAutoWalking          = true;
            _autoWalkArrived        = false;
            _staticIsAutoWalking    = true;
            _isWorldmap             = false;

            _fieldStuckTimer            = 0f;
            _fieldLastStuckCheckPos     = playerPos;
            _fieldStuckRecalcAttempted  = false;
            _isAvoidingObstacle         = false;
            _avoidanceAttempt           = 0;
            ResetLivelockState();

            try { _autoWalkSpeed = player.GetMoveSpeed(true); }
            catch { _autoWalkSpeed = 10.0f; }

            string label = _autoWalkLabel;
            ClearFieldResume();

            ScreenReader.Say(Loc.Get("nav_autowalk_resuming", label));
            DebugLogger.LogState($"NAV field auto-walk resumed after battle. target={label}");
        }

        #endregion

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

                // Exempt any exit zone the player is already standing in.
                // corners[0] is the player's current position; walking OUT of a
                // gate you are parked on (e.g. the town entrance you spawn on) is
                // the goal, not a "crossing". Only routing THROUGH a different gate
                // mid-journey should be blocked. Without this, opening nav while
                // standing on a gate makes every NavMesh route fail with
                // "Cannot reach ... without leaving the area" — see the Kurik
                // lighthouse case where the marker was rejected at the player's own
                // start position while an NPC 3 m away (routed via breadcrumbs,
                // which skip this check) was reachable.
                Vector3 start = corners[0];
                int exempted = zones.RemoveAll(z => z.Contains(start));
                if (exempted > 0)
                    DebugLogger.LogState(
                        $"NAV: exit-barrier exempted {exempted} zone(s) at player " +
                        $"start ({start.x:F1},{start.y:F1},{start.z:F1}) — leaving a " +
                        "gate you are standing on is allowed.");
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
            _isAvoidingObstacle  = false;
            _pathCorners         = null;
            _spatialSensor.Reset();
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
        /// complete path exists a normal single-path walk is correct. Returns
        /// false on partial/invalid paths or any error.
        /// </summary>
        private bool HasCompleteNavMeshPath(Vector3 playerPos, Vector3 targetPos)
        {
            try
            {
                bool complete = TryFindCompletePath(playerPos, targetPos, _navPath);
                if (complete)
                    DebugLogger.LogState(
                        "NAV: NavMesh path is COMPLETE — using direct walk.");
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
            // World map: connected-region compare for the current travel
            // mode (replaces the old straight-line CalcHeight ocean check,
            // which false-hid chests behind lake fingers).
            if (_isWorldmap)
                return WorldmapModeRegionReachable(playerPos, targetPos);

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
        /// When <paramref name="isCounter"/> is true the recorded-traversal route is
        /// skipped: a counter NPC must use the partial NavMesh path that stops right
        /// in front of the counter, not a breadcrumb route that won't line up there.
        /// </summary>
        private bool CalculateAndStorePath(Vector3 playerPos, Vector3 targetPos,
            bool allowPartial = false, bool isCounter = false)
        {
            bool ok = CalculateAndStorePathCore(playerPos, targetPos, allowPartial, isCounter);
            if (ok) return true;

            // Disconnection safety net: if carving markers are active and no path was
            // found, the carved holes may have sealed the only lane. Retry once on the
            // un-carved mesh so NPC-aware pathing can never make a reachable target
            // unreachable — worst case equals the pre-carving behavior.
            if (!_isWorldmap && _carverPool.ActiveCount > 0 && !_carverPool.Suppressed)
            {
                _carverPool.Suppress(true);
                try
                {
                    if (CalculateAndStorePathCore(playerPos, targetPos, allowPartial, isCounter))
                    {
                        DebugLogger.LogState(
                            "NAV carve: route was blocked by carving — recomputed on the un-carved mesh.");
                        return true;
                    }
                }
                finally { _carverPool.Suppress(false); }
            }
            return ok;
        }

        /// <summary>
        /// Core NavMesh/traversal path computation (unchanged behavior). Wrapped by
        /// <see cref="CalculateAndStorePath"/> which adds the carving disconnection fallback.
        /// </summary>
        private bool CalculateAndStorePathCore(Vector3 playerPos, Vector3 targetPos,
            bool allowPartial = false, bool isCounter = false)
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
            //    Skipped for counter NPCs: in a town with breadcrumbs a counter would
            //    otherwise route over recorded paths that don't stop at the counter
            //    front — it must use the partial NavMesh path below instead.
            else if (!isCounter && UseTraversal() &&
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

            TrackPathStability(corners);
            LogPath(corners);
            return true;
        }

        /// <summary>
        /// Clears all carve-oscillation (livelock) tracking state and lifts any carve
        /// suppression so the next walk starts clean. Called on walk start, resume, and cancel.
        /// </summary>
        private void ResetLivelockState()
        {
            _lastPathFirstLegDir     = Vector3.zero;
            _pathFirstLegValid       = false;
            _pathReversalCount       = 0;
            _livelockAnchored        = false;
            _walkBestApproach        = float.MaxValue;
            _carveSuppressedForBlock = false;
            _blockCommitDeadline     = 0f;
            _carverPool.Suppress(false);
        }

        /// <summary>
        /// Records the first-leg heading of each accepted path and counts how often it
        /// REVERSES between consecutive recalcs. Carve-oscillation (a goal walled in by
        /// near-only-carved NPCs) is the only case that keeps alternating the first-leg
        /// direction toward ↔ away; a legitimate long detour keeps one stable heading.
        /// Every accepted path funnels through CalculateAndStorePathCore, so this single
        /// instrument covers every recalc site. See <see cref="HandleCarveLivelock"/>.
        /// </summary>
        private void TrackPathStability(Vector3[] corners)
        {
            if (_isWorldmap) return;

            Vector3 dir = Vector3.zero;
            if (corners != null && corners.Length >= 2)
            {
                dir = corners[1] - corners[0];
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
            }

            if (_pathFirstLegValid && dir != Vector3.zero && _lastPathFirstLegDir != Vector3.zero
                && Vector3.Dot(dir, _lastPathFirstLegDir) < PathReversalDot)
            {
                _pathReversalCount++;
                _livelockAnchored = true;
                DebugLogger.LogState($"NAV livelock: first-leg reversed, count={_pathReversalCount}.");
            }

            _lastPathFirstLegDir = dir;
            _pathFirstLegValid = dir != Vector3.zero;
        }

        /// <summary>
        /// Announces an auto-walk give-up. When a physically-blocking NPC sits in the
        /// forward cone toward the target, the player is told the destination is blocked
        /// by people; otherwise the generic "path blocked" message is used.
        /// </summary>
        private void AnnounceBlockedGiveUp(Vector3 playerPos)
        {
            bool people = IsBlockedByPersonAhead(playerPos);
            ScreenReader.Say(Loc.Get(
                people ? "nav_autowalk_blocked_people" : "nav_autowalk_stuck",
                _autoWalkLabel));
            DebugLogger.LogState($"NAV give-up: blockedByPeople={people} label='{_autoWalkLabel}'.");
        }

        /// <summary>
        /// True if a physically-blocking NPC is in the forward cone toward the target,
        /// within <see cref="BlockProbeRange"/>. Uses the game's own ground truth:
        /// <c>FieldNpcCharacter.isPlayerObstacle</c> (or a non-empty
        /// <c>obstacleEventFunction</c>, i.e. an NPC that fires an event when bumped).
        /// Same enumeration/filter pattern as <see cref="GatherCarveTargets"/>.
        /// </summary>
        private bool IsBlockedByPersonAhead(Vector3 playerPos)
        {
            try
            {
                Vector3 dir = _autoWalkTarget - playerPos;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) return false;
                dir.Normalize();

                var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (found == null) return false;

                foreach (var npc in found)
                {
                    if (npc == null) continue;
                    if (npc.TryCast<FieldPlayer>() != null) continue;
                    if (npc.TryCast<FieldFollowCharacter>() != null) continue;
                    if (npc.TryCast<FieldEnemy>() != null) continue;

                    Vector3 to = npc.transform.position - playerPos;
                    to.y = 0f;
                    float d = to.magnitude;
                    if (d < 0.01f || d > BlockProbeRange) continue;
                    if (Vector3.Dot(dir, to / d) < BlockAheadCos) continue; // not ahead

                    bool blk = false;
                    string ev = null;
                    // PascalCase property does the IL2CPP runtime-invoke; the lowercase
                    // backing field can read stale (same rule as IsAcquired for chests).
                    try { blk = npc.IsPlayerObstacle; } catch { }
                    try { ev = npc.obstacleEventFunction; } catch { }
                    if (blk || !string.IsNullOrEmpty(ev))
                    {
                        DebugLogger.LogState(
                            $"NAV give-up: blocking NPC '{npc.name}' d={d:F1}m " +
                            $"isPlayerObstacle={blk} evt='{ev}'.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV blocked-ahead error: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Carve-oscillation (livelock) handler. Confirms the flip-flop (alternating
        /// first-leg reversals with no genuine approach to the target), then suppresses
        /// carving for the rest of the walk and commits to the un-carved (pre-carving)
        /// route so the player heads straight at the blockers and is told the destination
        /// is blocked. Returns true if the walk was ended this frame.
        /// Called every frame from the field auto-walk update loop.
        /// </summary>
        private bool HandleCarveLivelock(Vector3 playerPos)
        {
            try
            {
                // Already committed: walk the direct route until we wedge or time out.
                if (_carveSuppressedForBlock)
                {
                    bool wedged = ModSettings.WalkAssistEnabled && _spatialSensor.IsHardWedged;
                    if (wedged || Time.time >= _blockCommitDeadline)
                    {
                        AnnounceBlockedGiveUp(playerPos);
                        CancelAutoWalk();
                        return true;
                    }
                    return false; // keep walking; normal arrival still fires upstream if the way is clear
                }

                // Own horizontal distance to the target — the loop's targetDist is
                // float.MaxValue for different-floor targets, which would neuter the
                // best-approach guard, so compute it here (XZ works on any floor).
                float ddx = _autoWalkTarget.x - playerPos.x;
                float ddz = _autoWalkTarget.z - playerPos.z;
                float targetDist = Mathf.Sqrt(ddx * ddx + ddz * ddz);

                // Detours legitimately change direction — never count those as reversals.
                if (_isAvoidingObstacle)
                {
                    _livelockAnchored = false;
                    _pathReversalCount = 0;
                    return false;
                }

                // Genuine progress = beating the best approach ever achieved this walk.
                // (The player keeps re-touching the blocker line at the same distance during
                // the oscillation, so only a NEW closest distance counts — a real detour keeps
                // beating it; a walled-in goal plateaus.)
                if (targetDist < _walkBestApproach - LivelockApproachEps)
                {
                    _walkBestApproach = targetDist;
                    _livelockAnchored = false;
                    _pathReversalCount = 0;
                    return false;
                }
                if (targetDist < _walkBestApproach) _walkBestApproach = targetDist;

                if (_livelockAnchored && _pathReversalCount >= LivelockMinReversals)
                {
                    DebugLogger.LogState(
                        $"NAV livelock CONFIRMED: reversals={_pathReversalCount}, " +
                        $"bestApproach={_walkBestApproach:F1}m (no improvement). " +
                        "Suppressing carvers, committing to the direct route.");

                    _carverPool.Suppress(true);
                    _carveSuppressedForBlock = true;
                    _blockCommitDeadline = Time.time + BlockCommitTimeout;
                    _pathFirstLegValid = false; // committed; stop reversal tracking

                    // Walk a STRAIGHT LINE at the target rather than recomputing a NavMesh
                    // path. Suppressing the carvers does NOT restore the carved mesh until
                    // the next frame, so an immediate recompute still returns the loop (the
                    // player would wander off around it). A direct 2-point path presses the
                    // player into the blockers; the game's own collision stops them there and
                    // the wedge/timeout give-up fires with the blocker right ahead.
                    _pathCorners = new[] { playerPos, _autoWalkTarget };
                    _pathCornerIndex = 1;
                    _pathRecalcTimer = 0f;
                    // fall through and walk the committed straight-line route this frame.
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV livelock error: {ex.Message}");
            }
            return false;
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

        #region NPC-aware carving

        private readonly List<Vector3> _carveScratch = new List<Vector3>(CarverCap);

        /// <summary>
        /// Parks carving markers on the nearest standing NPCs around the player so the
        /// NavMesh routes around them. Throttled to <see cref="CarverRefreshInterval"/>.
        /// No-op (and clears carvers) when the NPC-aware setting is off or on the world map.
        /// </summary>
        private void UpdateFieldCarvers(Vector3 playerPos)
        {
            if (_isWorldmap || !ModSettings.NpcAwarePathfindingEnabled)
            {
                if (_carverPool.ActiveCount > 0) _carverPool.DeactivateAll();
                return;
            }

            // Place immediately on first use (ActiveCount==0); then throttle.
            _carveRefreshTimer += Time.deltaTime;
            if (_carverPool.ActiveCount > 0 && _carveRefreshTimer < CarverRefreshInterval)
                return;
            _carveRefreshTimer = 0f;

            GatherCarveTargets(playerPos, _carveScratch);
            _carverPool.SetPositions(_carveScratch);
        }

        /// <summary>
        /// Fills <paramref name="outPositions"/> with the nearest NPC body positions
        /// within <see cref="CarveBand"/> of the player (closest first, capped to the
        /// pool size). Excludes the player, party followers, enemies, and the auto-walk
        /// goal NPC (so we never carve the destination).
        /// </summary>
        private void GatherCarveTargets(Vector3 playerPos, List<Vector3> outPositions)
        {
            outPositions.Clear();
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (found == null) return;

                var cand = new List<(Vector3 pos, float d)>();
                float bandSq = CarveBand * CarveBand;
                foreach (var npc in found)
                {
                    if (npc == null) continue;
                    if (npc.TryCast<FieldPlayer>() != null) continue;
                    if (npc.TryCast<FieldFollowCharacter>() != null) continue;
                    if (npc.TryCast<FieldEnemy>() != null) continue;

                    Vector3 p = npc.transform.position;
                    float dSq = (p - playerPos).sqrMagnitude;
                    if (dSq > bandSq) continue;
                    // Don't carve the goal NPC (or anything sitting right on it).
                    if ((p - _autoWalkTarget).sqrMagnitude < 1.0f) continue;

                    cand.Add((p, dSq));
                }

                cand.Sort((a, b) => a.d.CompareTo(b.d));
                int n = Math.Min(cand.Count, CarverCap);
                for (int i = 0; i < n; i++) outPositions.Add(cand[i].pos);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV carve gather error: {ex.Message}");
            }
        }

        #endregion

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
                    // A detour legitimately changes heading — don't let the next stored
                    // path's reversal vs the pre-detour heading count as oscillation.
                    _pathFirstLegValid = false;
                    _pathReversalCount = 0;
                    _livelockAnchored = false;

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
