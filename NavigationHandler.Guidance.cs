using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Spoken turn-by-turn directions to a navigation-list target — the
    /// hands-on alternative to auto-walk. The player keeps full control of
    /// the character and is told, leg by leg, which way to push the stick
    /// and how far to go: "North East, 14 meters." A new instruction is
    /// spoken whenever the direction to push changes, either because the
    /// route turns a corner or because the camera has rotated far enough
    /// that the same corner now needs a different stick direction.
    ///
    /// Directions are CAMERA-RELATIVE, matching the rest of the mod:
    /// "North" means straight up on the left stick. The route comes from
    /// the same NavMesh/traversal pathfinder auto-walk uses, so anything
    /// auto-walk can reach can also be described.
    ///
    /// Field and dungeon maps only — the world map is not covered yet.
    ///
    /// TRIGGER (see Main.ProcessGamepad):
    /// Hold L2 to open the navigation list, highlight a target, then push
    /// the left stick DOWN. Pushing it down again on the same target stops
    /// the directions; on a different target it switches destination.
    /// Auto-walk (L2 + stick up, or the walk key) also takes over.
    /// </summary>
    public partial class NavigationHandler
    {
        #region Constants

        /// <summary>Seconds between route recomputations while guiding.</summary>
        private const float GuideRepathInterval = 1f;

        /// <summary>
        /// Corners whose headings differ by less than this are merged into one
        /// spoken leg. NavMesh corner lists contain many near-straight kinks
        /// around geometry; without merging, the player would be told to
        /// "turn" for course corrections they cannot even feel.
        /// </summary>
        private const float GuideLegMergeDegrees = 22f;

        /// <summary>
        /// How far the announced heading must swing before a NEW instruction is
        /// spoken. Slightly above the merge angle so a re-route that reproduces
        /// essentially the same leg stays silent.
        /// </summary>
        private const float GuideTurnDegrees = 30f;

        /// <summary>
        /// How close (meters, horizontal) counts as having reached a leg end,
        /// so the next leg is announced just before the corner rather than on
        /// top of it.
        /// </summary>
        private const float GuideLegReachedRadius = 1.5f;

        /// <summary>Legs shorter than this are folded into the following leg.</summary>
        private const float GuideMinLegLength = 1.2f;

        /// <summary>
        /// How close a freshly scanned item must be to the current destination
        /// to count as the same thing (the stop-versus-switch decision).
        /// </summary>
        private const float GuideSameTargetMeters = 2f;

        /// <summary>
        /// The player must have covered this much ground since the last
        /// announcement for the distance reminder to fire, so standing still
        /// to think is never interrupted every few seconds.
        /// </summary>
        private const float GuideRemindMinMove = 1f;

        /// <summary>Minimum seconds between two spoken directions (anti-chatter).</summary>
        private const float GuideMinSpeakGap = 1.2f;

        /// <summary>
        /// Seconds of silence after which the current leg (direction and remaining
        /// distance) is repeated: <see cref="ModSettings.GuideReminderSeconds"/>,
        /// set in the mod menu; 0 = never. Long straight legs would otherwise give
        /// the player no feedback at all about their progress.
        /// </summary>
        private static float GuideRemindInterval => ModSettings.GuideReminderSeconds;

        /// <summary>Grace period (s) after an interruption before a pending directions resume is dropped.</summary>
        private const float GuideResumeDiscardDelay = 3f;

        #endregion

        #region State

        /// <summary>True while spoken directions are running.</summary>
        private bool _guideActive;

        /// <summary>Final destination (refreshed from the live transform each frame).</summary>
        private Vector3 _guideTarget;

        /// <summary>Spoken name of the destination.</summary>
        private string _guideLabel;

        /// <summary>Live transform of a moving destination (NPC); null for fixed targets.</summary>
        private Transform _guideTransform;

        /// <summary>Navigation category of the destination (picks the arrival radius).</summary>
        private int _guideCategoryIndex;

        /// <summary>True when the destination is a counter NPC (partial route accepted).</summary>
        private bool _guideIsCounter;

        /// <summary>
        /// The route reduced to spoken legs: each entry is the world position
        /// where the current straight stretch ends. Rebuilt on every repath.
        /// </summary>
        private readonly List<Vector3> _guideLegs = new List<Vector3>();

        /// <summary>Index into <see cref="_guideLegs"/> of the leg being walked.</summary>
        private int _guideLegIndex;

        /// <summary>Time.time of the next route recomputation.</summary>
        private float _guideRepathAt;

        /// <summary>Camera-relative compass sector of the last instruction, -1 = none.</summary>
        private int _guideSpokenSector;

        /// <summary>
        /// Camera-relative bearing (degrees, 0 = North) at the moment the last
        /// instruction was spoken. Gives the sector change its hysteresis: a
        /// bearing resting exactly on a sector boundary would otherwise flip
        /// words back and forth for as long as the player walked.
        /// </summary>
        private float _guideSpokenBearing;

        /// <summary>
        /// The aim point the last instruction referred to. When the aim jumps
        /// (a corner was rounded, or a followed NPC walked off), the player is
        /// on a new leg and is told about it even if the compass word is
        /// unchanged — the distance has changed underneath them.
        /// </summary>
        private Vector3 _guideSpokenAim;

        /// <summary>Time.time of the last spoken instruction or reminder.</summary>
        private float _guideLastSpeakTime;

        /// <summary>Where the player stood when the last instruction was spoken.</summary>
        private Vector3 _guideLastSpeakPos;

        /// <summary>True once the "no route" warning has been spoken for the current outage.</summary>
        private bool _guideRouteLost;

        /// <summary>
        /// True while the legs come from the floor grid (map geometry) rather than
        /// a walked or NavMesh route. Announced, because the player is about to
        /// verify that ground with their own feet.
        /// </summary>
        private bool _guideUnverified;

        /// <summary>Whether the player is being guided by spoken directions.</summary>
        public bool IsGuiding => _guideActive;

        #endregion

        #region Start / Stop

        /// <summary>
        /// Starts (or stops, or re-targets) spoken directions to the currently
        /// highlighted navigation item. Repeating the gesture on the SAME target
        /// stops the directions; on a different target it switches destination.
        /// Announces the reason and does nothing when no route exists.
        /// </summary>
        public void GuideTo()
        {
            if (!_isOpen) return;
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0 || _currentItemIndex >= cat.Count) return;

            var item = cat[_currentItemIndex];

            // Second press on the destination already being described = stop.
            if (_guideActive && IsSameGuideTarget(item))
            {
                string stopping = _guideLabel;
                StopGuidance("repeated gesture");
                ScreenReader.Say(Loc.Get("nav_guide_stopped", stopping));
                return;
            }

            if (_isWorldmap)
            {
                ScreenReader.Say(Loc.Get("nav_guide_not_worldmap"));
                return;
            }

            Vector3 playerPos;
            if (!TryGetPlayerPosition(out playerPos))
            {
                ScreenReader.Say(Loc.Get("nav_not_in_field"));
                return;
            }

            // Where the target IS now, not where it stood when the list was built.
            Vector3 target = item.Position;
            if (item.LiveTransform != null)
            {
                try { target = item.LiveTransform.position; }
                catch { /* destroyed transform — keep the list position */ }
            }

            // Directions supersede an active walk — one navigation aid at a
            // time. Cancelled first, because CancelAutoWalk drops the stored
            // route and would throw away a path computed before it.
            if (_isAutoWalking) CancelAutoWalk();

            ClearGuideResume();
            if (!TryRouteForGuidance(playerPos, target, _currentCategoryIndex,
                    item.IsCounterNpc, out bool unverified))
            {
                string failKey = _lastPathBlockedByExit
                    ? "nav_autowalk_route_exits" : "nav_autowalk_unreachable";
                ScreenReader.Say(Loc.Get(failKey, item.Label));
                return;
            }

            // Close the list — the player is now following directions, not
            // browsing. The gamepad overlay rebuilds on the next L2 hold.
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();

            StartGuidance(playerPos, target, item.Label, item.LiveTransform,
                _currentCategoryIndex, item.IsCounterNpc, unverified,
                Loc.Get(unverified ? "nav_guide_unverified_start" : "nav_guide_start", item.Label));
        }

        /// <summary>
        /// Computes the route for spoken directions: the walked/NavMesh route
        /// first (same barrier rule as auto-walk — a route may only pass through
        /// a map exit when the exit is the destination), else the floor grid.
        /// Directions move nothing, so an unverified grid route only costs the
        /// player a walk — and that walk records breadcrumbs that verify it.
        /// Leaves the route in <see cref="_pathCorners"/>.
        /// </summary>
        private bool TryRouteForGuidance(Vector3 playerPos, Vector3 target, int categoryIndex,
            bool isCounter, out bool unverified)
        {
            unverified = false;
            _autoWalkAllowExit = IsExitCategory(categoryIndex);

            bool routed;
            try
            {
                routed = CalculateAndStorePath(playerPos, target,
                    allowPartial: true, isCounter: isCounter);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV guidance: NavMesh error: {ex.Message}");
                routed = false;
            }

            if ((!routed || GuideRouteFallsShort(target)) && !_lastPathBlockedByExit
                && TryFloorGridRoute(playerPos, target, out var gridCorners, out _))
            {
                _pathCorners     = gridCorners;
                _pathCornerIndex = gridCorners.Length > 1 ? 1 : 0;
                routed     = true;
                unverified = true;
            }
            return routed;
        }

        /// <summary>
        /// Activates directions toward a destination whose route is already in
        /// <see cref="_pathCorners"/>, speaks <paramref name="startMessage"/>, then
        /// the first leg. Shared by a fresh start and a resume after battle.
        /// </summary>
        private void StartGuidance(Vector3 playerPos, Vector3 target, string label,
            Transform liveTransform, int categoryIndex, bool isCounter, bool unverified,
            string startMessage)
        {
            _guideActive        = true;
            _guideTarget        = target;
            _guideLabel         = label;
            _guideTransform     = liveTransform;
            _guideCategoryIndex = categoryIndex;
            _guideIsCounter     = isCounter;
            _guideRepathAt      = Time.time + GuideRepathInterval;
            _guideRouteLost     = false;
            _guideUnverified    = unverified;
            ResetGuideSpeech();
            BuildGuideLegs(playerPos, target);

            ScreenReader.Say(startMessage);
            DebugLogger.LogState(
                $"NAV guidance started. target={label} " +
                $"pos=({target.x:F1},{target.y:F1},{target.z:F1}) " +
                $"legs={_guideLegs.Count} unverified={unverified}");

            // Speak the first leg immediately, queued behind the start message.
            SpeakGuideStep(playerPos, interrupt: false);
        }

        #endregion

        #region Battle Resume

        /// <summary>True when directions were interrupted by a scene change and may resume.</summary>
        private bool _guideResumePending;
        /// <summary>True once a battle was detected during the pending window.</summary>
        private bool _guideResumeBattleSeen;
        /// <summary>Seconds the field has been free without a battle having been seen.</summary>
        private float _guideResumeFreeTimer;
        private Vector3   _guideResumeTarget;
        private string    _guideResumeLabel;
        private Transform _guideResumeTransform;
        private int       _guideResumeCategoryIndex;
        private bool      _guideResumeIsCounter;
        private FieldmapID _guideResumeMapId;

        /// <summary>
        /// Scene change while directions run (a battle scene loading, most of
        /// the time): remembers the destination so the directions come back on
        /// their own once the battle is over, then stops. Mirrors the auto-walk
        /// battle resume — only a battle resumes; anything else is dropped.
        /// </summary>
        public void OnSceneChangeGuidance()
        {
            if (_guideActive)
            {
                _guideResumePending       = true;
                _guideResumeBattleSeen    = false;
                _guideResumeFreeTimer     = 0f;
                _guideResumeTarget        = _guideTarget;
                _guideResumeLabel         = _guideLabel;
                _guideResumeTransform     = _guideTransform;
                _guideResumeCategoryIndex = _guideCategoryIndex;
                _guideResumeIsCounter     = _guideIsCounter;
                try { _guideResumeMapId = FieldManager.Instance?.currentFieldmapID ?? FieldmapID.INVALID; }
                catch { _guideResumeMapId = FieldmapID.INVALID; }
                DebugLogger.LogState($"NAV guidance: scene change, saving potential resume for '{_guideLabel}'.");
            }
            StopGuidance("scene change");
        }

        /// <summary>Drops a pending directions resume.</summary>
        private void ClearGuideResume()
        {
            _guideResumePending    = false;
            _guideResumeBattleSeen = false;
            _guideResumeFreeTimer  = 0f;
            _guideResumeTransform  = null;
        }

        /// <summary>
        /// Per-frame handler for a pending directions resume (called from Update
        /// while nothing else is guiding or walking). Resumes once a battle was
        /// seen and the field is free again on the same map; a non-battle
        /// interruption is dropped after a short grace period.
        /// </summary>
        private void UpdateGuideResume()
        {
            try
            {
                if (IsBattleActive())
                {
                    _guideResumeBattleSeen = true;
                    _guideResumeFreeTimer = 0f;
                }
                if (!IsFieldFree())
                {
                    _guideResumeFreeTimer = 0f;
                    return;
                }

                var fm = FieldManager.Instance;
                if (fm != null && _guideResumeMapId != FieldmapID.INVALID
                    && fm.currentFieldmapID != _guideResumeMapId)
                {
                    DebugLogger.LogState("NAV guidance resume: map changed, discarding.");
                    ClearGuideResume();
                    return;
                }

                if (_guideResumeBattleSeen)
                {
                    ResumeGuidance();
                    return;
                }

                _guideResumeFreeTimer += Time.deltaTime;
                if (_guideResumeFreeTimer >= GuideResumeDiscardDelay)
                {
                    DebugLogger.LogState("NAV guidance resume: non-battle interruption, discarding.");
                    ClearGuideResume();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV guidance resume error: {ex.Message}");
                ClearGuideResume();
            }
        }

        /// <summary>Re-routes from the player's current position and restarts the directions.</summary>
        private void ResumeGuidance()
        {
            Vector3 target = _guideResumeTarget;
            if (_guideResumeTransform != null)
            {
                try { target = _guideResumeTransform.position; }
                catch { _guideResumeTransform = null; }
            }
            string label = _guideResumeLabel;
            var transform = _guideResumeTransform;
            int category = _guideResumeCategoryIndex;
            bool isCounter = _guideResumeIsCounter;
            ClearGuideResume();

            if (!TryGetPlayerPosition(out Vector3 playerPos)) return;
            if (!TryRouteForGuidance(playerPos, target, category, isCounter, out bool unverified))
            {
                DebugLogger.LogState($"NAV guidance resume: no route to '{label}' after battle, discarding.");
                return;
            }
            StartGuidance(playerPos, target, label, transform, category, isCounter, unverified,
                Loc.Get("nav_guide_resuming", label));
        }

        #endregion

        #region Start / Stop (continued)

        /// <summary>
        /// Stops spoken directions without announcing. Callers that stop on the
        /// player's behalf (arrival, a deliberate cancel) speak their own
        /// message; silent stops (map change, scene change) speak nothing.
        /// </summary>
        public void StopGuidance(string reason)
        {
            if (!_guideActive) return;
            _guideActive    = false;
            _guideTransform = null;
            _guideLabel     = null;
            _guideLegs.Clear();
            _guideLegIndex  = 0;
            ResetGuideSpeech();
            DebugLogger.LogState($"NAV guidance stopped ({reason}).");
        }

        /// <summary>
        /// Stops spoken directions and says so. Used by the deliberate cancel
        /// paths, where the player must hear that the aid is gone.
        /// </summary>
        public void CancelGuidanceSpoken()
        {
            if (!_guideActive) return;
            string label = _guideLabel;
            StopGuidance("cancelled");
            ScreenReader.Say(Loc.Get("nav_guide_stopped", label));
        }

        /// <summary>
        /// True when the stored route is a partial NavMesh path that stops well
        /// short of the destination — typically at the foot of a cliff below a
        /// target on another level. Its legs would end with a straight bearing
        /// into the cliff, so a floor-grid route is preferred when one exists.
        /// </summary>
        private bool GuideRouteFallsShort(Vector3 target)
        {
            if (!_lastPathWasPartial || _pathCorners == null || _pathCorners.Length == 0)
                return false;
            Vector3 end = _pathCorners[_pathCorners.Length - 1];
            return Mathf.Abs(end.y - target.y) >= FloorChangeThreshold
                || FlatDistance(end, target) > GuidePartialShortMeters;
        }

        /// <summary>A partial route ending farther than this from the target (m) "falls short".</summary>
        private const float GuidePartialShortMeters = 5f;

        /// <summary>Clears the "what was last spoken" memory so the next tick announces.</summary>
        private void ResetGuideSpeech()
        {
            _guideSpokenSector  = -1;
            _guideSpokenBearing = 0f;
            _guideSpokenAim     = Vector3.zero;
            _guideLastSpeakTime = Time.time;
        }

        /// <summary>
        /// True when the given list item is the destination currently being
        /// described. Compares label and position rather than object identity:
        /// the list is rebuilt from scratch on every scan, and an IL2CPP
        /// component fetched twice can arrive as two managed wrappers around
        /// the same native object, so reference equality would silently fail.
        /// A live destination is re-read every frame, so a followed NPC's
        /// stored position still matches the scan that just found it.
        /// </summary>
        private bool IsSameGuideTarget(NavItem item)
        {
            return item.Label == _guideLabel
                && FlatDistance(item.Position, _guideTarget) < GuideSameTargetMeters;
        }

        #endregion

        #region Per-frame Tick

        /// <summary>
        /// One frame of spoken directions. Called from <see cref="Update"/>
        /// while guidance is active and auto-walk is not.
        ///
        /// Guidance never moves the player, so an interruption (dialogue,
        /// battle, menu) only PAUSES it: the tick returns, and the leg is
        /// re-announced once the field is free again so the player is
        /// re-oriented after whatever took over.
        /// </summary>
        private void GuidanceTick()
        {
            // The held-L2 overlay is reading the item list out loud; directions
            // would interrupt it mid-word. The gamepad nav list is also how the
            // player re-targets or stops guidance, so this pause is on the
            // normal route through the feature, not an edge case. The last
            // instruction is remembered: it is still valid when the list closes,
            // and repeating it there would only echo what was just said.
            if (_gamepadNavActive) return;

            // Dialogue, a battle or a menu took the screen. Guidance never moves
            // the player, so it only pauses — and forgets what it said, so the
            // player is re-oriented rather than dropped back mid-leg.
            if (!IsFieldFree())
            {
                ResetGuideSpeech();
                return;
            }

            Vector3 playerPos;
            if (!TryGetPlayerPosition(out playerPos))
            {
                StopGuidance("player unavailable");
                return;
            }

            // Follow a moving destination (a wandering NPC).
            if (_guideTransform != null)
            {
                try { _guideTarget = _guideTransform.position; }
                catch { _guideTransform = null; }
            }

            if (IsAtGuideTarget(playerPos))
            {
                string label = _guideLabel;
                StopGuidance("arrived");
                ScreenReader.Say(Loc.Get("nav_autowalk_arrived", label));
                return;
            }

            if (Time.time >= _guideRepathAt)
            {
                _guideRepathAt = Time.time + GuideRepathInterval;
                RefreshGuideRoute(playerPos);
            }

            AdvanceGuideLeg(playerPos);
            SpeakGuideStep(playerPos, interrupt: true);
        }

        /// <summary>
        /// Recomputes the route and rebuilds the spoken legs. A failure is
        /// announced once and guidance continues on the straight bearing to the
        /// target — a crow-flies heading is still more use than silence, and the
        /// route usually returns on the next attempt.
        /// </summary>
        private void RefreshGuideRoute(Vector3 playerPos)
        {
            _autoWalkAllowExit = IsExitCategory(_guideCategoryIndex);

            bool routed;
            try
            {
                routed = CalculateAndStorePath(playerPos, _guideTarget,
                    allowPartial: true, isCounter: _guideIsCounter);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV guidance repath error: {ex.Message}");
                routed = false;
            }

            if (routed && !GuideRouteFallsShort(_guideTarget))
            {
                if (_guideRouteLost)
                {
                    _guideRouteLost = false;
                    DebugLogger.LogState("NAV guidance: route recovered.");
                }
                if (_guideUnverified)
                {
                    _guideUnverified = false;
                    DebugLogger.LogState("NAV guidance: a walked/NavMesh route now exists — leaving the floor-grid route.");
                }
                BuildGuideLegs(playerPos, _guideTarget);
                return;
            }

            // Floor-grid fallback (see GuideTo). Announced once on the switch
            // from a verified route; silent while it simply continues.
            if (!_lastPathBlockedByExit
                && TryFloorGridRoute(playerPos, _guideTarget, out var gridCorners, out _))
            {
                _pathCorners     = gridCorners;
                _pathCornerIndex = gridCorners.Length > 1 ? 1 : 0;
                if (!_guideUnverified)
                {
                    _guideUnverified = true;
                    ScreenReader.Say(Loc.Get("nav_guide_unverified_switch"));
                    _guideLastSpeakTime = Time.time;
                }
                _guideRouteLost = false;
                BuildGuideLegs(playerPos, _guideTarget);
                return;
            }

            // A partial route with no grid alternative: still better than a
            // straight bearing — it at least follows the floor as far as it goes.
            if (routed)
            {
                BuildGuideLegs(playerPos, _guideTarget);
                return;
            }

            // No route: fall back to a single straight leg at the destination.
            _guideLegs.Clear();
            _guideLegs.Add(_guideTarget);
            _guideLegIndex = 0;
            if (!_guideRouteLost)
            {
                _guideRouteLost = true;
                DebugLogger.LogState(
                    $"NAV guidance: no route to '{_guideLabel}' — using the straight bearing.");
                ScreenReader.Say(Loc.Get("nav_guide_no_route", _guideLabel));
                _guideLastSpeakTime = Time.time;
            }
        }

        /// <summary>
        /// Reduces the stored path corners to the legs a person can actually
        /// follow: corners that only bend the route slightly are merged into the
        /// straight stretch they belong to, very short stretches are folded into
        /// the next one, and the real destination always terminates the list
        /// (a partial route stops short of it).
        /// </summary>
        private void BuildGuideLegs(Vector3 playerPos, Vector3 target)
        {
            _guideLegs.Clear();
            _guideLegIndex = 0;

            var corners = _pathCorners;
            if (corners != null && corners.Length > 1)
            {
                // corners[0] is the player's own snapped position — the first
                // real waypoint is index 1 (matching _pathCornerIndex).
                Vector3 from = playerPos;
                Vector3 legHeading = Vector3.zero;

                for (int i = 1; i < corners.Length; i++)
                {
                    Vector3 heading = FlatDirection(from, corners[i]);
                    if (heading == Vector3.zero) continue;

                    if (legHeading == Vector3.zero)
                    {
                        // First stretch — nothing to compare against yet.
                        legHeading = heading;
                        _guideLegs.Add(corners[i]);
                        from = corners[i];
                        continue;
                    }

                    if (Vector3.Angle(legHeading, heading) < GuideLegMergeDegrees)
                    {
                        // Same straight stretch — extend it instead of turning.
                        _guideLegs[_guideLegs.Count - 1] = corners[i];
                    }
                    else
                    {
                        legHeading = heading;
                        _guideLegs.Add(corners[i]);
                    }
                    from = corners[i];
                }
            }

            // Always finish at the destination itself. A partial route (counter
            // NPCs, a target just off the mesh) ends short of it, and the last
            // few meters still have to be described.
            if (_guideLegs.Count == 0
                || FlatDistance(_guideLegs[_guideLegs.Count - 1], target) > GuideMinLegLength)
            {
                _guideLegs.Add(target);
            }
            else
            {
                _guideLegs[_guideLegs.Count - 1] = target;
            }

            DropShortGuideLegs(playerPos);
        }

        /// <summary>
        /// Folds stretches shorter than <see cref="GuideMinLegLength"/> into the
        /// one that follows. A one-meter jog is not a turn worth announcing, and
        /// speaking it would bury the instruction that matters. The final leg is
        /// always kept — it ends at the destination.
        /// </summary>
        private void DropShortGuideLegs(Vector3 playerPos)
        {
            Vector3 from = playerPos;
            for (int i = 0; i < _guideLegs.Count - 1; )
            {
                if (FlatDistance(from, _guideLegs[i]) < GuideMinLegLength)
                {
                    _guideLegs.RemoveAt(i);
                    continue;
                }
                from = _guideLegs[i];
                i++;
            }
        }

        /// <summary>
        /// Steps past legs the player has already walked. The next leg is picked
        /// up slightly BEFORE its corner (<see cref="GuideLegReachedRadius"/>) so
        /// the turn is announced while there is still time to make it.
        /// </summary>
        private void AdvanceGuideLeg(Vector3 playerPos)
        {
            while (_guideLegIndex < _guideLegs.Count - 1
                   && FlatDistance(playerPos, _guideLegs[_guideLegIndex]) <= GuideLegReachedRadius)
            {
                _guideLegIndex++;
            }
        }

        /// <summary>
        /// Speaks an instruction when one is due. Three things trigger it:
        /// the player moved onto a new leg (the aim point jumped), the way to
        /// push the stick changed by a real margin — whether because the route
        /// turned or because the camera did — or nothing has been said for
        /// <see cref="GuideRemindInterval"/> and the remaining distance on the
        /// current leg is repeated.
        /// </summary>
        private void SpeakGuideStep(Vector3 playerPos, bool interrupt)
        {
            if (_guideLegs.Count == 0) return;
            if (_guideLegIndex >= _guideLegs.Count)
                _guideLegIndex = _guideLegs.Count - 1;

            Vector3 aim = _guideLegs[_guideLegIndex];
            if (FlatDirection(playerPos, aim) == Vector3.zero) return;

            float bearing = CompassBearing(playerPos, aim);
            int sector = BearingToSector(bearing);
            float sinceSpoke = Time.time - _guideLastSpeakTime;

            bool first = _guideSpokenSector < 0;
            // A new instruction is spoken ONLY when the way to push the stick
            // changes: a different compass word, once the bearing has swung clear
            // of where it was — otherwise a bearing resting on a sector boundary
            // alternates words forever while the player walks straight. A new leg
            // in the SAME direction is not a turn, so it stays silent (the user's
            // rule: repeat directions only when a direction change is necessary).
            bool reworded = !first && sector != _guideSpokenSector
                && Mathf.Abs(Mathf.DeltaAngle(_guideSpokenBearing, bearing)) >= GuideTurnDegrees;

            if (first || (reworded && sinceSpoke >= GuideMinSpeakGap))
            {
                _guideSpokenSector  = sector;
                _guideSpokenBearing = bearing;
                _guideSpokenAim     = aim;
                _guideLastSpeakTime = Time.time;
                _guideLastSpeakPos  = playerPos;
                int meters = GuideMeters(playerPos, aim);
                ScreenReader.Say(
                    Loc.Get("nav_guide_leg", CompassName(sector), meters), interrupt);
                DebugLogger.LogState(
                    $"NAV guidance: leg {_guideLegIndex + 1}/{_guideLegs.Count} " +
                    $"{CompassName(sector)} {meters}m " +
                    $"({(first ? "start" : "direction changed")})");
                return;
            }

            // Progress reminder — only while the player is actually walking, and
            // only if the player has not turned it off. Standing still to think
            // must never be interrupted on a timer. Repeats the whole leg
            // (direction and distance) so a player who lost the thread while
            // fighting or listening to a pickup is fully re-oriented.
            if (GuideRemindInterval > 0f && sinceSpoke >= GuideRemindInterval
                && FlatDistance(playerPos, _guideLastSpeakPos) >= GuideRemindMinMove)
            {
                _guideLastSpeakTime = Time.time;
                _guideLastSpeakPos  = playerPos;
                ScreenReader.Say(
                    Loc.Get("nav_guide_leg", CompassName(sector), GuideMeters(playerPos, aim)), interrupt);
            }
        }

        /// <summary>Whole meters to the aim point, never rounded down to zero.</summary>
        private static int GuideMeters(Vector3 playerPos, Vector3 aim) =>
            Mathf.Max(1, Mathf.RoundToInt(FlatDistance(playerPos, aim)));

        #endregion

        #region Helpers

        /// <summary>
        /// True when the player is at the destination, using the same radii and
        /// vertical tolerance as auto-walk's arrival test so both aids agree on
        /// what "arrived" means.
        /// </summary>
        private bool IsAtGuideTarget(Vector3 playerPos)
        {
            bool isInteractable = _guideCategoryIndex == CAT_CHEST
                || _guideCategoryIndex == CAT_SAVE
                || _guideCategoryIndex == CAT_INTERACTABLE;
            float radius = isInteractable
                ? InteractableArrivalRadius : AutoWalkArrivalRadius;

            return FlatDistance(playerPos, _guideTarget) <= radius
                && Mathf.Abs(_guideTarget.y - playerPos.y) <= ArrivalVerticalTolerance;
        }

        /// <summary>Reads the controlled player's position; false when not on a field.</summary>
        private static bool TryGetPlayerPosition(out Vector3 position)
        {
            position = Vector3.zero;
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player == null) return false;
                position = player.transform.position;
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV guidance: player fetch failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Horizontal (XZ) distance between two world positions.</summary>
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Normalized horizontal direction from one position to another, or
        /// Vector3.zero when they are effectively the same spot.
        /// </summary>
        private static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 d = new Vector3(to.x - from.x, 0f, to.z - from.z);
            return d.sqrMagnitude < 0.0001f ? Vector3.zero : d.normalized;
        }

        #endregion
    }
}
