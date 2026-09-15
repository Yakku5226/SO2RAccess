using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Spoken directions on the world map (2026-09-06). The field version
    /// re-plans every second; a world map route is far too expensive for that
    /// (grid A* with safe-exit search, pre-validation and body sweeps — seconds
    /// in the worst case), so this half plans ONCE and then follows the stored
    /// route: the player is projected onto it every frame and the route is only
    /// re-planned on evidence that it went wrong — the player pushes the stick
    /// and does not move (stuck: the spot is stamped as blocked, like auto-walk's
    /// recovery), drifts far off the line, or the travel mode changes (bunny,
    /// psynard).
    ///
    /// The raw route is thousands of half-metre grid cells that zig-zag at 45°;
    /// it is first simplified (Douglas–Peucker, then every chord checked against
    /// the grid so no shortcut cuts a rock) before the shared leg builder turns
    /// it into a handful of spoken legs.
    ///
    /// Arrival follows auto-walk's rules: a location is reached when the game's
    /// own "Press X to enter" prompt names it (never by distance — the town
    /// centre is inside the walls), a fishing spot when the fishing bubble shows.
    /// With no grid (Nede, until it is baked) or on the psynard the directions
    /// fall back to a straight bearing and say so.
    /// </summary>
    public partial class NavigationHandler
    {
        #region Constants

        /// <summary>Max lateral deviation (m) of a simplified chord from the raw route.</summary>
        private const float GuideWmSimplifyEpsilon = 1.5f;

        /// <summary>Spacing (m) of the walkability samples along a simplified chord.</summary>
        private const float GuideWmChordSampleStep = 0.5f;

        /// <summary>Legs shorter than this (m) are folded away — a 2 m jog at run speed is 0.3 s.</summary>
        private const float GuideWmMinLegLength = 3f;

        /// <summary>Same, threading a cave mouth or rocky pinch: short legs matter there.</summary>
        private const float GuideWmTightMinLegLength = 1f;

        /// <summary>Distance (m) at which a leg end counts as reached, open ground.</summary>
        private const float GuideWmLegReachedRadius = 2.5f;

        /// <summary>Same, in tight terrain.</summary>
        private const float GuideWmTightLegReachedRadius = 1f;

        /// <summary>How many legs ahead the player is projected onto (corner cutting).</summary>
        private const int GuideWmProjectionWindow = 2;

        /// <summary>Minimum seconds between two re-plans.</summary>
        private const float GuideWmReplanMinGap = 4f;

        /// <summary>Stuck re-plans before giving up on the route (mirrors auto-walk's 5).</summary>
        private const int GuideWmMaxStuckReplans = 5;

        /// <summary>Seconds of pushing without moving that count as stuck.</summary>
        private const float GuideWmStuckSeconds = 0.6f;

        /// <summary>Below this speed (m/s) a pushing player is not moving.</summary>
        private const float GuideWmStuckSpeed = 0.3f;

        /// <summary>How far (m) ahead of a stuck player the second blocked stamp goes.</summary>
        private const float GuideWmStampAhead = 1f;

        /// <summary>Distance (m) off the route that counts as drifting, open ground.</summary>
        private const float GuideWmDriftMeters = 12f;

        /// <summary>Same, in tight terrain.</summary>
        private const float GuideWmTightDriftMeters = 4f;

        /// <summary>Seconds of drifting before a re-plan.</summary>
        private const float GuideWmDriftSeconds = 1.5f;

        /// <summary>Seconds between travel-mode polls.</summary>
        private const float GuideWmModePollInterval = 0.5f;

        /// <summary>
        /// Consecutive non-free frames tolerated before the directions pause. The
        /// world map flickers IsFieldFree at terrain transitions (see Update).
        /// </summary>
        private const int GuideWmNotFreeTolerance = 10;

        /// <summary>Within this (m) of a location's ring point the directions aim at the town itself.</summary>
        private const float GuideWmEntranceRadius = 4f;

        /// <summary>Leaving the fishing stand by more than this (m) ends the "face the water" hold.</summary>
        private const float GuideWmFishLeaveMeters = 6f;

        /// <summary>Seconds at the fishing stand without a bubble before the honest "no prompt" stop.</summary>
        private const float GuideWmFishTimeout = 8f;

        #endregion

        #region State

        /// <summary>True while the running directions are on the world map.</summary>
        private bool _guideOnWorldmap;

        /// <summary>The route as raw grid cells, guidance's own copy.</summary>
        private Vector3[] _guideWmRaw;

        /// <summary>Route goal: the entrance ring point for locations, the stand for fishing, else the target.</summary>
        private Vector3 _guideWmGoal;

        /// <summary>The location's centre symbol — where to walk from the ring to raise the prompt.</summary>
        private Vector3 _guideWmCentre;

        /// <summary>Verified water point of a fishing stand.</summary>
        private Vector3? _guideWmFace;

        /// <summary>True when the directions lead to a fishing spot (arrival = the game's bubble).</summary>
        private bool _guideWmFishing;

        /// <summary>True when the accepted route used the 0.50 m floor tier (re-plans skip the comfort pass).</summary>
        private bool _guideWmFloorTier;

        /// <summary>True in straight-bearing mode: psynard flight, no grid, or the route was given up.</summary>
        private bool _guideWmStraight;

        /// <summary>Where the current route's first leg starts (the player's position at plan time).</summary>
        private Vector3 _guideWmLegOrigin;

        private WorldmapTravelMode _guideWmMode;
        private float _guideWmModePollAt;
        private float _guideWmLastReplanTime;
        private int _guideWmStuckReplans;
        private int _guideWmNotFreeFrames;
        private Vector3 _guideWmStuckLastPos;
        private bool _guideWmStuckHavePos;
        private float _guideWmStuckFor;
        private float _guideWmDriftFor;
        private bool _guideWmEntranceSpoken;
        private float _guideWmFishReachedAt;
        private bool _guideWmFishHolding;

        #endregion

        #region Route acquisition

        /// <summary>
        /// Resolves the real walk target (entrance ring, fishing stand) and plans
        /// the route through the planner auto-walk uses, then keeps a private copy.
        /// Psynard flight and a missing grid degrade to a straight bearing with a
        /// spoken reason instead of refusing. False = no route (caller announces).
        /// </summary>
        private bool TryRouteForWorldmapGuidance(ref NavItem item, Vector3 playerPos, int categoryIndex,
            out Vector3 target, out string startMessage)
        {
            startMessage = null;
            target = item.Position;
            if (item.LiveTransform != null)
            {
                try { target = item.LiveTransform.position; }
                catch { /* destroyed transform — keep the list position */ }
            }

            _guideWmCentre    = item.Position;
            _guideWmFace      = item.FacePosition;
            _guideWmFishing   = item.IsFishing;
            _guideWmRaw       = null;
            _guideWmStraight  = false;
            _guideWmFloorTier = false;
            _guideWmMode      = WorldmapTravel.CurrentMode();

            if (_guideWmMode == WorldmapTravelMode.Psynard)
            {
                _guideWmStraight = true;
                _guideWmGoal = target;
                startMessage = Loc.Get("nav_guide_wm_flying", item.Label);
                DebugLogger.LogState("NAV WM guidance: psynard flight — straight bearing, no route.");
                return true;
            }

            WorldmapID wmId = WorldmapID.INVALID;
            try { wmId = FieldManager.Instance?.WorldmapID ?? WorldmapID.INVALID; }
            catch (Exception ex) { DebugLogger.LogState($"NAV WM guidance: WorldmapID read failed: {ex.Message}"); }
            if (!WorldmapPathfinder.HasGridFor(wmId))
            {
                _guideWmStraight = true;
                _guideWmGoal = target;
                startMessage = Loc.Get("nav_guide_wm_no_grid", item.Label);
                DebugLogger.LogState($"NAV WM guidance: no grid for {wmId} — straight bearing.");
                return true;
            }

            Vector3 walkTarget = target;
            bool routed;
            try
            {
                routed = PlanWorldmapRoute(ref item, playerPos, categoryIndex, ref walkTarget);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM guidance: planner error: {ex.Message}");
                routed = false;
            }
            if (!routed) return false;

            target            = walkTarget;
            _guideWmGoal      = _wmPathGoal;
            _guideWmRaw       = _wmPathWaypoints;
            _guideWmFloorTier = _wmLastRouteFloorTier;
            _guideWmFace      = item.FacePosition;
            return true;
        }

        /// <summary>Resume after a battle: one full plan to the remembered goal, keeping the blocked stamps.</summary>
        private bool ResumeWorldmapGuidance(Vector3 playerPos, Vector3 target, string label,
            Transform liveTransform, int categoryIndex, WorldmapGuideResume resume)
        {
            _isWorldmap       = true; // the planner reads it
            _guideWmCentre    = resume.Centre;
            _guideWmFace      = resume.Face;
            _guideWmFishing   = resume.Fishing;
            _guideWmFloorTier = resume.FloorTier;
            _guideWmRaw       = null;
            _guideWmStraight  = false;
            _guideWmMode      = WorldmapTravel.CurrentMode();
            _guideWmGoal      = resume.Goal;

            if (_guideWmMode == WorldmapTravelMode.Psynard)
            {
                _guideWmStraight = true;
            }
            else
            {
                bool routed;
                try
                {
                    routed = WorldmapCalculateAndStorePath(playerPos, resume.Goal,
                        keepBlockedPositions: true, skipComfortTier: resume.FloorTier);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV WM guidance resume: planner error: {ex.Message}");
                    routed = false;
                }
                if (!routed)
                {
                    ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", label));
                    return false;
                }
                _guideWmRaw       = _wmPathWaypoints;
                _guideWmGoal      = _wmPathGoal;
                _guideWmFloorTier = _wmLastRouteFloorTier;
            }

            StartGuidance(playerPos, target, label, liveTransform, categoryIndex, isCounter: false,
                unverified: false, onWorldmap: true, Loc.Get("nav_guide_resuming", label));
            return true;
        }

        /// <summary>Per-run bookkeeping reset, called by StartGuidance before the legs are built.</summary>
        private void ResetWorldmapGuideState(Vector3 playerPos)
        {
            _guideWmLegOrigin       = playerPos;
            _guideWmModePollAt      = Time.time + GuideWmModePollInterval;
            _guideWmLastReplanTime  = -100f;
            _guideWmStuckReplans    = 0;
            _guideWmNotFreeFrames   = 0;
            _guideWmStuckHavePos    = false;
            _guideWmStuckFor        = 0f;
            _guideWmDriftFor        = 0f;
            _guideWmEntranceSpoken  = false;
            _guideWmFishHolding     = false;
            _guideWmFishReachedAt   = 0f;
        }

        #endregion

        #region Legs

        /// <summary>Turns the raw route (or the straight bearing) into spoken legs.</summary>
        private void BuildWorldmapGuideLegs(Vector3 playerPos)
        {
            if (_guideWmStraight || _guideWmRaw == null || _guideWmRaw.Length == 0)
            {
                _guideLegs.Clear();
                _guideLegs.Add(_guideTarget);
                _guideLegIndex = 0;
                return;
            }

            List<Vector3> simplified = SimplifyWorldmapRoute(_guideWmRaw, _guideWmMode, GuideWmSimplifyEpsilon);
            float minLeg = _wmTightTerrain ? GuideWmTightMinLegLength : GuideWmMinLegLength;
            BuildGuideLegsFrom(simplified, 0, playerPos, _guideTarget, GuideLegMergeDegrees, minLeg);
            DebugLogger.LogState(
                $"NAV WM guidance: {_guideWmRaw.Length} cells -> {simplified.Count} points -> {_guideLegs.Count} legs.");
        }

        /// <summary>
        /// Douglas–Peucker on the horizontal plane, then a walkability pass: any
        /// chord that crosses a cell the grid marks blocked for this travel mode
        /// is split at its farthest raw point and both halves are re-checked, so
        /// the thread through a cave mouth survives while open ground collapses
        /// to long straight legs.
        /// </summary>
        private static List<Vector3> SimplifyWorldmapRoute(Vector3[] raw, WorldmapTravelMode mode, float epsilon)
        {
            int n = raw.Length;
            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;

            if (n > 2)
            {
                var stack = new Stack<(int a, int b)>();
                stack.Push((0, n - 1));
                while (stack.Count > 0)
                {
                    var (a, b) = stack.Pop();
                    if (b - a < 2) continue;
                    int far = FarthestFromChord(raw, a, b, out float dist);
                    if (dist > epsilon)
                    {
                        keep[far] = true;
                        stack.Push((a, far));
                        stack.Push((far, b));
                    }
                }

                int from = 0;
                while (from < n - 1)
                {
                    int to = from + 1;
                    while (!keep[to]) to++;
                    if (to - from >= 2 && ChordCrossesBlockedCell(raw[from], raw[to], mode))
                    {
                        keep[FarthestFromChord(raw, from, to, out _)] = true;
                        continue; // re-check the first half from the same start
                    }
                    from = to;
                }
            }

            var result = new List<Vector3>();
            for (int i = 0; i < n; i++)
                if (keep[i]) result.Add(raw[i]);
            return result;
        }

        /// <summary>Index strictly between a and b farthest (horizontally) from the chord a–b.</summary>
        private static int FarthestFromChord(Vector3[] raw, int a, int b, out float maxDist)
        {
            int far = a + 1;
            maxDist = -1f;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistanceToSegment(raw[i], raw[a], raw[b], out _);
                if (d > maxDist)
                {
                    maxDist = d;
                    far = i;
                }
            }
            return far;
        }

        /// <summary>True when a sample along the chord lands on a cell blocked for the mode.</summary>
        private static bool ChordCrossesBlockedCell(Vector3 p, Vector3 q, WorldmapTravelMode mode)
        {
            float len = FlatDistance(p, q);
            int steps = Mathf.CeilToInt(len / GuideWmChordSampleStep);
            for (int i = 1; i < steps; i++)
            {
                Vector3 at = Vector3.Lerp(p, q, i / (float)steps);
                if (!WorldmapPathfinder.IsWalkableWorld(at, mode)) return true;
            }
            return false;
        }

        /// <summary>Horizontal distance from p to segment a–b; t is the position along it (0..1).</summary>
        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float apx = p.x - a.x, apz = p.z - a.z;
            float lenSq = abx * abx + abz * abz;
            t = lenSq < 1e-6f ? 0f : Mathf.Clamp01((apx * abx + apz * abz) / lenSq);
            float cx = a.x + abx * t - p.x;
            float cz = a.z + abz * t - p.z;
            return Mathf.Sqrt(cx * cx + cz * cz);
        }

        /// <summary>Start point of leg i: the previous leg's end, or where the route was planned.</summary>
        private Vector3 WorldmapLegStart(int i) => i == 0 ? _guideWmLegOrigin : _guideLegs[i - 1];

        #endregion

        #region Tick

        /// <summary>One frame of world map directions (see the class summary for the policy).</summary>
        private void WorldmapGuidanceTick()
        {
            if (_gamepadNavActive) return;

            if (!IsFieldFree())
            {
                // Terrain transitions blip non-free for a frame or two; only a
                // real interruption (battle, menu) re-orients the player afterwards.
                if (++_guideWmNotFreeFrames > GuideWmNotFreeTolerance) ResetGuideSpeech();
                return;
            }
            _guideWmNotFreeFrames = 0;

            if (!TryGetPlayerPosition(out Vector3 playerPos))
            {
                StopGuidance("player unavailable");
                return;
            }

            if (_guideTransform != null)
            {
                try { _guideTarget = _guideTransform.position; }
                catch { _guideTransform = null; }
            }

            CheckWorldmapModeChange(playerPos);
            if (!_guideActive) return;

            if (HandleWorldmapArrival(playerPos)) return;

            UpdateTightTerrain(playerPos);

            if (!_guideWmStraight)
            {
                AdvanceWorldmapLeg(playerPos);
                CheckWorldmapReplan(playerPos);
            }

            SpeakGuideStep(playerPos, interrupt: true);
        }

        /// <summary>
        /// Steps past legs the player has walked. Besides the reached radius, a
        /// player who cut a corner is detected by projecting onto the next legs:
        /// when a later leg is clearly nearer than the current one, the current
        /// one is over. The index never moves backwards except by a re-plan.
        /// </summary>
        private void AdvanceWorldmapLeg(Vector3 playerPos)
        {
            float reached = _wmTightTerrain ? GuideWmTightLegReachedRadius : GuideWmLegReachedRadius;
            while (_guideLegIndex < _guideLegs.Count - 1)
            {
                if (FlatDistance(playerPos, _guideLegs[_guideLegIndex]) <= reached)
                {
                    _guideLegIndex++;
                    continue;
                }

                float current = DistanceToSegment(playerPos, WorldmapLegStart(_guideLegIndex),
                    _guideLegs[_guideLegIndex], out _);
                bool advanced = false;
                int last = Math.Min(_guideLegs.Count - 1, _guideLegIndex + GuideWmProjectionWindow);
                for (int i = _guideLegIndex + 1; i <= last; i++)
                {
                    float d = DistanceToSegment(playerPos, _guideLegs[i - 1], _guideLegs[i], out float t);
                    if (t > 0f && d + 0.5f < current)
                    {
                        _guideLegIndex = i;
                        advanced = true;
                        break;
                    }
                }
                if (!advanced) break;
            }
        }

        /// <summary>The three re-plan triggers: stuck (stamped), drift, and a blocked straight line.</summary>
        private void CheckWorldmapReplan(Vector3 playerPos)
        {
            float dt = Time.deltaTime;

            // Stuck: pushing the stick while the character does not move.
            bool pushing = ManualNavHandler.TryGetMoveIntent(WallProbe.CameraForwardFlat(), out Vector3 pushDir);
            if (pushing && _guideWmStuckHavePos && dt > 0f)
            {
                Vector3 moved = playerPos - _guideWmStuckLastPos;
                moved.y = 0f;
                _guideWmStuckFor = moved.magnitude / dt < GuideWmStuckSpeed ? _guideWmStuckFor + dt : 0f;
            }
            else
            {
                _guideWmStuckFor = 0f;
            }
            _guideWmStuckLastPos = playerPos;
            _guideWmStuckHavePos = true;
            if (_guideWmStuckFor >= GuideWmStuckSeconds)
            {
                _guideWmStuckFor = 0f;
                if (ReplanWorldmapGuide(playerPos, "stuck", pushDir, announce: true)) return;
            }

            // Drift: far from both the current leg and the next.
            float driftLimit = _wmTightTerrain ? GuideWmTightDriftMeters : GuideWmDriftMeters;
            float off = DistanceToSegment(playerPos, WorldmapLegStart(_guideLegIndex), _guideLegs[_guideLegIndex], out _);
            if (_guideLegIndex + 1 < _guideLegs.Count)
                off = Mathf.Min(off, DistanceToSegment(playerPos, _guideLegs[_guideLegIndex], _guideLegs[_guideLegIndex + 1], out _));
            _guideWmDriftFor = off > driftLimit ? _guideWmDriftFor + dt : 0f;
            if (_guideWmDriftFor >= GuideWmDriftSeconds)
            {
                _guideWmDriftFor = 0f;
                if (ReplanWorldmapGuide(playerPos, $"drift {off:F0} m", null, announce: false)) return;
            }

            // A "straight line to the aim crosses a blocked cell" trigger was tried
            // here and removed (2026-09-06 18:49 log): along a shore the line clips
            // water cells constantly, so it re-planned every 4 s and the rebuilt
            // first leg flipped the spoken direction each time. Stuck and drift
            // are the honest signals; a shortcut into rock ends in "stuck".
        }

        /// <summary>
        /// Re-plans from the player's position with auto-walk's stuck-recovery
        /// recipe (one A* to the stored goal, honouring the blocked stamps). A
        /// stuck re-plan stamps the spot first and, after too many, gives the
        /// route up for a straight bearing. Rate limited; returns true when it ran.
        /// </summary>
        private bool ReplanWorldmapGuide(Vector3 playerPos, string reason, Vector3? stampDir, bool announce)
        {
            if (Time.time - _guideWmLastReplanTime < GuideWmReplanMinGap) return false;
            _guideWmLastReplanTime = Time.time;

            if (stampDir.HasValue)
            {
                _wmBlockedPositions.Add(playerPos);
                _wmBlockedPositions.Add(playerPos + stampDir.Value * GuideWmStampAhead);
                _guideWmStuckReplans++;
                if (_guideWmStuckReplans > GuideWmMaxStuckReplans)
                {
                    GiveUpWorldmapRoute($"stuck {_guideWmStuckReplans} times");
                    return true;
                }
            }

            Vector3[] path = null;
            try
            {
                path = WorldmapPathfinder.FindPath(playerPos, _guideWmGoal, _guideWmMode,
                    _wmBlockedPositions, skipComfortTier: _guideWmFloorTier);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM guidance: re-plan error: {ex.Message}");
            }

            if (path == null || path.Length == 0)
            {
                DebugLogger.LogState($"NAV WM guidance: re-plan ({reason}) found no route.");
                if (stampDir.HasValue) GiveUpWorldmapRoute("no route after stuck");
                return true;
            }

            _guideWmFloorTier = _guideWmFloorTier || WorldmapPathfinder.LastPathUsedFloorTier;
            _guideWmRaw       = path;
            _guideWmLegOrigin = playerPos;
            _guideWmDriftFor  = 0f;
            BuildWorldmapGuideLegs(playerPos);
            DebugLogger.LogState($"NAV WM guidance: re-planned ({reason}), {path.Length} cells, {_guideLegs.Count} legs.");

            if (announce)
            {
                ResetGuideSpeech();
                ScreenReader.Say(Loc.Get("nav_guide_wm_rerouting"));
                _guideLastSpeakTime = Time.time;
            }
            return true;
        }

        /// <summary>Drops the route for a straight bearing and says so once.</summary>
        private void GiveUpWorldmapRoute(string reason)
        {
            _guideWmStraight = true;
            _guideLegs.Clear();
            _guideLegs.Add(_guideTarget);
            _guideLegIndex = 0;
            ResetGuideSpeech();
            ScreenReader.Say(Loc.Get("nav_guide_wm_blocked", _guideLabel));
            _guideLastSpeakTime = Time.time;
            DebugLogger.LogState($"NAV WM guidance: giving up the route ({reason}) — straight bearing.");
        }

        /// <summary>
        /// Mount, dismount, take-off or landing: the passable world changes, so
        /// the route is planned again in the new mode. Foot stamps mean nothing
        /// to the bunny, which hops the rocks that stopped a walker.
        /// </summary>
        private void CheckWorldmapModeChange(Vector3 playerPos)
        {
            if (Time.time < _guideWmModePollAt) return;
            _guideWmModePollAt = Time.time + GuideWmModePollInterval;

            var mode = WorldmapTravel.CurrentMode();
            if (mode == _guideWmMode) return;
            DebugLogger.LogState($"NAV WM guidance: travel mode {_guideWmMode} -> {mode}.");
            _guideWmMode = mode;

            if (mode == WorldmapTravelMode.Psynard)
            {
                _guideWmStraight = true;
                _guideLegs.Clear();
                _guideLegs.Add(_guideTarget);
                _guideLegIndex = 0;
                ResetGuideSpeech();
                ScreenReader.Say(Loc.Get("nav_guide_wm_flying", _guideLabel));
                _guideLastSpeakTime = Time.time;
                return;
            }

            if (mode == WorldmapTravelMode.Bunny) _wmBlockedPositions.Clear();
            _guideWmStraight       = false;
            _guideWmStuckReplans   = 0;
            _guideWmLastReplanTime = -100f;
            ScreenReader.Say(Loc.Get("nav_guide_wm_mode_changed", _guideLabel));
            ReplanWorldmapGuide(playerPos, "travel mode", null, announce: true);
        }

        #endregion

        #region Arrival

        /// <summary>
        /// Per-category arrival. Returns true when this frame is done: the
        /// directions stopped (arrived), or the player is being told how to face
        /// the water and leg speech must stay quiet.
        /// </summary>
        private bool HandleWorldmapArrival(Vector3 playerPos)
        {
            if (_guideCategoryIndex == CAT_LOCATION)
            {
                if (EnterPromptMatches(_guideLabel))
                {
                    ArriveWorldmapGuide("enter prompt shown");
                    return true;
                }

                // At the ring point the route is done; the prompt appears while
                // walking on toward the town itself, so aim there and say so.
                if (!_guideWmEntranceSpoken && FlatDistance(playerPos, _guideWmGoal) <= GuideWmEntranceRadius)
                {
                    _guideWmEntranceSpoken = true;
                    _guideLegs.Clear();
                    _guideLegs.Add(_guideWmCentre);
                    _guideLegIndex = 0;
                    _guideWmStraight = true; // no more re-plans: the ring is reached
                    string dir = CompassName(BearingToSector(CompassBearing(playerPos, _guideWmCentre)));
                    ResetGuideSpeech();
                    _guideSpokenSector  = BearingToSector(CompassBearing(playerPos, _guideWmCentre));
                    _guideSpokenBearing = CompassBearing(playerPos, _guideWmCentre);
                    ScreenReader.Say(Loc.Get("nav_guide_wm_at_entrance", _guideLabel, dir));
                    _guideLastSpeakTime = Time.time;
                    DebugLogger.LogState($"NAV WM guidance: at the entrance ring of '{_guideLabel}', aiming at the centre ({dir}).");
                }
                return false;
            }

            bool fishing = _guideCategoryIndex == CAT_INTERACTABLE && _guideWmFishing;
            if (fishing)
            {
                if (FieldPromptHandler.FishPromptShowing)
                {
                    ArriveWorldmapGuide("fishing prompt shown");
                    return true;
                }

                float toStand = FlatDistance(playerPos, _guideWmGoal);
                if (_guideWmFishHolding)
                {
                    if (toStand > GuideWmFishLeaveMeters)
                    {
                        _guideWmFishHolding = false;
                        DebugLogger.LogState("NAV WM guidance: left the fishing stand without a bubble — directions continue.");
                        return false;
                    }
                    if (Time.time - _guideWmFishReachedAt >= GuideWmFishTimeout)
                    {
                        string label = _guideLabel;
                        try { LogFishingArrivalDiag(FieldManager.Instance?.GetControlPlayer(), "directions hold ended"); }
                        catch (Exception ex) { DebugLogger.LogState($"NAV WM guidance: fishing diag failed: {ex.Message}"); }
                        StopGuidance("fishing stand reached, no bubble");
                        ScreenReader.Say(Loc.Get(FishingNoPromptKey(), label));
                    }
                    return true;
                }

                if (toStand <= InteractableArrivalRadius)
                {
                    _guideWmFishHolding   = true;
                    _guideWmFishReachedAt = Time.time;
                    Vector3 water = _guideWmFace ?? _guideWmCentre;
                    string dir = CompassName(BearingToSector(CompassBearing(playerPos, water)));
                    ScreenReader.Say(Loc.Get("nav_guide_wm_face_water", dir));
                    _guideLastSpeakTime = Time.time;
                    DebugLogger.LogState($"NAV WM guidance: fishing stand reached, water is {dir}.");
                    return true;
                }
                return false;
            }

            bool isInteractable = _guideCategoryIndex == CAT_CHEST || _guideCategoryIndex == CAT_INTERACTABLE;
            float radius = isInteractable ? InteractableArrivalRadius : AutoWalkArrivalRadius;
            if (FlatDistance(playerPos, _guideTarget) <= radius
                && Mathf.Abs(_guideTarget.y - playerPos.y) <= ArrivalVerticalTolerance)
            {
                ArriveWorldmapGuide("within radius");
                return true;
            }
            return false;
        }

        private void ArriveWorldmapGuide(string reason)
        {
            string label = _guideLabel;
            StopGuidance("arrived: " + reason);
            ScreenReader.Say(Loc.Get("nav_autowalk_arrived", label));
        }

        #endregion
    }
}
