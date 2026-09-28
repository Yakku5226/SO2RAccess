using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Psynard (flying mount) navigation, kept apart from walking on purpose:
    /// auto-fly while mounted, and a list row for the parked mount on foot.
    ///
    /// Auto-fly drives the game's own controls the way a sighted player does:
    /// the left stick sets the heading (camera-relative, like walking) and the
    /// boost button (L1 / Left Shift, held) provides the forward motion, since
    /// the psynard hovers in place without it. The mod never moves the mount
    /// itself, so barriers, the world edge and the altitude limits behave as
    /// they do for everyone. Altitude and landing stay manual.
    ///
    /// What the native flight code reads is unmeasured (2026-09-27): both stick
    /// queries are injected and counted, and the commanded bearing is logged
    /// against the mount's real heading once a second, so the first test flight
    /// shows exactly which assumption holds.
    /// </summary>
    public partial class NavigationHandler
    {
        /// <summary>Horizontal arrival distance for a flight (the mount is large and fast).</summary>
        private const float FlyArrivalRadius = 12f;
        /// <summary>Closest approach not improving by FlyProgressMeters for this long ends the flight as blocked.</summary>
        private const float FlyStuckSeconds = 12f;
        /// <summary>Improvement of the closest approach that counts as progress.</summary>
        private const float FlyProgressMeters = 3f;
        /// <summary>The mount's turning circle under boost (measured ~55 m, 2026-09-27 15:05 orbit).</summary>
        private const float FlyTurnRadius = 55f;
        /// <summary>Boosted speed (measured ~85 m/s) — with FlyMaxTurnRate it gives FlyTurnRadius.</summary>
        private const float FlyBoostSpeed = 85f;
        /// <summary>Turn rate at full sideways stick (measured ~90°/s under boost).</summary>
        private const float FlyMaxTurnRate = 90f;
        /// <summary>Sideways stick below which the mount does not turn (X 0.31 held 20 s = no turn; 0.42 = ~30°/s).</summary>
        private const float FlyStickDeadzone = 0.3f;
        /// <summary>Extra sideways stick on the arc so it turns a little tighter than the geometry asks.</summary>
        private const float FlyStickMargin = 0.05f;
        /// <summary>Heading error treated as "on the bearing": straight run, no sideways stick.</summary>
        private const float FlyAlignedDegrees = 3f;
        /// <summary>Turn phase hysteresis: full sideways turn above enter, back to the arc below exit.</summary>
        private const float FlyTurnEnterDegrees = 90f;
        private const float FlyTurnExitDegrees = 80f;
        /// <summary>How far ahead the psynard wall probe looks.</summary>
        private const float FlyWallLookahead = 80f;
        /// <summary>Final straight run: within this distance and heading error the boost is released.</summary>
        private const float FlyFinalRadius = 45f;
        private const float FlyFinalDegrees = 20f;
        /// <summary>A town's designated landing spot is used when this close to its symbol.</summary>
        private const float FlyLandingSpotMaxOffset = 120f;
        /// <summary>On foot, the walk target sits this far in front of the parked mount.</summary>
        private const float PsynardApproachOffset = 3f;

        private bool    _flyActive;
        private Vector3 _flyTarget;
        private string  _flyLabel;
        private float   _flyDiagTimer;
        private int     _flyFieldFreeFailCount;
        /// <summary>Latched turn direction (+1 right, −1 left, 0 none) while the target is more than 90° off.</summary>
        private int     _flyTurnSign;
        /// <summary>Closest horizontal approach so far and when it last improved (blocked detection).</summary>
        private float   _flyBestDist;
        private float   _flyBestDistTime;
        /// <summary>Current steering phase ("turn", "arc", "standoff"), logged on change.</summary>
        private string  _flyPhase;

        /// <summary>True while a mounted flight is being driven; set for the input patches.</summary>
        private static bool _staticIsAutoFlying;
        /// <summary>True while the flight wants the boost button held (input prefix answers it).</summary>
        private static bool _staticFlyBoost;
        /// <summary>GetLeftStick calls seen during the current diagnostic second (which query the flight reads).</summary>
        private static int _flyLeftStickReads;
        /// <summary>GetPlayerControlStick calls seen during the current diagnostic second.</summary>
        private static int _flyControlStickReads;

        /// <summary>True while auto-fly is running.</summary>
        public bool IsAutoFlying => _flyActive;

        // --------------------------------------------------------------------
        // Auto-fly
        // --------------------------------------------------------------------

        /// <summary>
        /// Starts a flight to the selected item. Called from <see cref="AutoWalkTo"/>
        /// when the party is mounted on the world map. Towns aim at their
        /// designated psynard landing spot when the map data has one.
        /// </summary>
        private void StartAutoFly(NavItem item, Vector3 mountPos)
        {
            Vector3 target = item.Position;
            if (item.LiveTransform != null)
            {
                try { target = item.LiveTransform.position; }
                catch { /* destroyed transform — keep the list position */ }
            }

            string targetNote = "item position";
            if (_currentCategoryIndex == CAT_LOCATION
                && TryFindLandingSpot(target, out Vector3 landing, out string spotNote))
            {
                target = landing;
                targetNote = spotNote;
            }

            // One navigation aid at a time.
            StopGuidance("auto-fly started");
            ClearGuideResume();
            CloseListForFlight();

            _flyActive      = true;
            _flyTarget      = target;
            _flyLabel       = item.Label;
            _flyDiagTimer   = 0f;
            _flyFieldFreeFailCount = 0;
            _flyTurnSign    = 0;
            _flyBestDist    = float.MaxValue;
            _flyBestDistTime = Time.time;
            _flyPhase       = null;
            _flyLeftStickReads = 0;
            _flyControlStickReads = 0;
            _staticIsAutoFlying = true;
            _staticFlyBoost = false;
            _staticIsAutoWalking = true;   // the stick postfixes inject only while this is set
            _wmDirectMoveActive  = false;
            _staticCameraStickX  = 0f;     // the mount's camera task follows on its own

            float dist = FlatDistance(mountPos, target);
            // Flight milestones are always-on log lines (like [WMReach]); the
            // per-second heading/stick diagnostics stay debug-only.
            MelonLoader.MelonLogger.Msg(
                $"[Psynard] FLY start to '{item.Label}' target=({target.x:F1},{target.y:F1},{target.z:F1}) " +
                $"[{targetNote}] from ({mountPos.x:F1},{mountPos.y:F1},{mountPos.z:F1}) dist={dist:F0} m.");
            ScreenReader.Say(Loc.Get("nav_fly_start", item.Label));
        }

        /// <summary>Closes the overlay the way a walk start does, without touching walk state.</summary>
        private void CloseListForFlight()
        {
            if (_isOpen) GamepadCloseNav();
        }

        /// <summary>
        /// Per-frame flight driver. Runs before the walking update and returns
        /// early when no flight is active, so walking code never sees a flight.
        /// </summary>
        private void UpdateAutoFly()
        {
            if (!_flyActive) return;

            if (!FieldState.IsRidingPsynard())
            {
                MelonLoader.MelonLogger.Msg("[Psynard] FLY ended — no longer mounted.");
                CancelAutoFly();
                return;
            }

            // Menus and events end the flight; brief flickers are tolerated
            // exactly like the world map walk (terrain zone transitions).
            if (!IsFieldFree())
            {
                if (++_flyFieldFreeFailCount > 10)
                {
                    MelonLoader.MelonLogger.Msg("[Psynard] FLY ended — field not free.");
                    CancelAutoFly();
                    return;
                }
                _staticAutoWalkStickDir = Vector2.zero;
                return;
            }
            _flyFieldFreeFailCount = 0;

            Vector3 mountPos;
            Vector3 heading = Vector3.forward;
            int wallMask = 0;
            try
            {
                var psynard = FieldManager.Instance?.FieldPsynard;
                if (psynard == null) { CancelAutoFly(); return; }
                mountPos = psynard.transform.position;
                heading  = psynard.transform.forward;
                wallMask = psynard.GetLayerMaskWall(); // L17 PsynardWall for this form
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV FLY: mount read failed: {ex.Message}");
                CancelAutoFly();
                return;
            }

            float dist = FlatDistance(mountPos, _flyTarget);
            if (dist <= FlyArrivalRadius)
            {
                string label = _flyLabel;
                MelonLoader.MelonLogger.Msg($"[Psynard] FLY arrived above '{label}' ({dist:F1} m).");
                CancelAutoFly();
                ScreenReader.Say(Loc.Get("nav_fly_arrived", label));
                return;
            }

            // Measured 2026-09-27 (logs 14:52 and 15:04): the flight reads
            // GetLeftStick (~3300 calls/s); the injected boost gives ~85 m/s and
            // the stick alone (no boost) ~24 m/s; the mount turns ONLY with a
            // sideways stick and ONLY while moving (hovering = no turn), on a
            // fixed circle of about FlyTurnRadius under boost — a target closer
            // than that to the side is orbited forever (15:05:55, 13 s at 55 m,
            // then "blocked"); a backward stick does nothing (the heading is
            // held). Camera-relative frame, camera trailing 25–50° in turns.
            //
            // Steering therefore follows the turning circle: a target more than
            // 90° off is a full turn with a latched direction; otherwise the arc
            // through the target has radius d / (2 sin e) — feasible when it is
            // at least the minimum circle (stick X = ratio, Y forward), else
            // the mount holds its heading until the geometry allows the arc.
            // The boost is dropped only on the final straight run (slow, exact
            // 12 m arrival); the stick never points backward.
            Vector3 worldDir = new Vector3(_flyTarget.x - mountPos.x, 0f, _flyTarget.z - mountPos.z).normalized;
            float wantBearing = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
            float haveBearing = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            float error = Mathf.DeltaAngle(haveBearing, wantBearing); // + = target to the right
            float absError = Mathf.Abs(error);

            Vector2 stick;
            bool boost = true;
            string phase;
            Vector3 fwd = new Vector3(heading.x, 0f, heading.z).normalized;

            // A psynard wall ahead (story barrier, world edge) ends a standoff or
            // a gentle arc early: the mount turns toward the target instead of
            // pressing into the wall for the whole blocked window (16:28, 16:30).
            bool wallAhead = false;
            float wallDist = 0f;
            if (wallMask != 0)
            {
                try
                {
                    if (Physics.Raycast(mountPos, fwd, out RaycastHit hit, FlyWallLookahead, wallMask))
                    {
                        wallAhead = true;
                        wallDist = hit.distance;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV FLY: wall probe failed: {ex.Message}");
                }
            }

            // Turn phase with hysteresis (enter above 90°, leave below 80°) so a
            // target exactly abeam does not flip between phases every frame.
            if (_flyTurnSign != 0 && absError < FlyTurnExitDegrees) _flyTurnSign = 0;
            if (_flyTurnSign == 0 && (absError > FlyTurnEnterDegrees || (wallAhead && absError > FlyAlignedDegrees)))
                _flyTurnSign = error >= 0f ? 1 : -1;

            if (_flyTurnSign != 0)
            {
                stick = new Vector2(_flyTurnSign, 0f);
                phase = wallAhead ? "turn (wall ahead)" : "turn";
            }
            else if (absError <= FlyAlignedDegrees)
            {
                stick = WorldDirToCameraStick(worldDir);
                if (stick.y < 0.5f) stick = new Vector2(0f, 1f); // camera lag guard
                phase = "straight";
                if (dist <= FlyFinalRadius) boost = false; // final run at stick speed
            }
            else
            {
                float sinE = Mathf.Sin(absError * Mathf.Deg2Rad);
                float arcRadius = dist / (2f * sinE);
                if (arcRadius >= FlyTurnRadius || wallAhead)
                {
                    // Intercept arc. The stick is calibrated from the 15:04–16:30
                    // logs: below FlyStickDeadzone sideways the mount does not
                    // turn at all (X 0.31 held for 20 s = 0°/s), full sideways is
                    // FlyMaxTurnRate, roughly linear between. Turn rate needed for
                    // the arc = speed / radius; re-solved every frame.
                    float rateDeg = Mathf.Min(FlyMaxTurnRate, FlyBoostSpeed / arcRadius * Mathf.Rad2Deg);
                    float x = FlyStickDeadzone + rateDeg / FlyMaxTurnRate * (1f - FlyStickDeadzone) + FlyStickMargin;
                    x = Mathf.Clamp01(x) * Mathf.Sign(error);
                    stick = new Vector2(x, Mathf.Sqrt(Mathf.Max(0f, 1f - x * x)));
                    phase = "arc";
                    if (dist <= FlyFinalRadius && absError <= FlyFinalDegrees) boost = false;
                }
                else
                {
                    // Inside the minimum circle: hold the heading and make room.
                    stick = WorldDirToCameraStick(fwd);
                    if (stick.y < 0.5f) stick = new Vector2(0f, 1f); // camera lag guard
                    phase = "standoff";
                }
            }
            if (wallAhead && phase != _flyPhase)
                DebugLogger.LogState($"NAV FLY: psynard wall {wallDist:F0} m ahead.");
            if (phase != _flyPhase)
            {
                DebugLogger.LogState(
                    $"NAV FLY: phase {phase} (dist={dist:F0} m, error {error:F0}°" +
                    (phase == "standoff" ? ", target inside the turning circle" : "") + ").");
                _flyPhase = phase;
            }
            _staticAutoWalkStickDir = stick;
            _staticFlyBoost = boost;

            // Blocked: the closest approach so far stops improving (robust to the
            // standoff detour and to orbits, which a position window is not).
            if (dist < _flyBestDist - FlyProgressMeters)
            {
                _flyBestDist = dist;
                _flyBestDistTime = Time.time;
            }
            else if (Time.time - _flyBestDistTime >= FlyStuckSeconds)
            {
                string label = _flyLabel;
                MelonLoader.MelonLogger.Msg(
                    $"[Psynard] FLY blocked — closest approach {_flyBestDist:F0} m not improved for " +
                    $"{FlyStuckSeconds:F0} s at ({mountPos.x:F1},{mountPos.y:F1},{mountPos.z:F1}), " +
                    $"{dist:F0} m from '{label}', heading error {error:F0}°, phase {phase}.");
                CancelAutoFly();
                ScreenReader.Say(Loc.Get("nav_fly_blocked", label));
                return;
            }

            // Once-a-second diagnostics: commanded vs real heading, the camera yaw
            // (the stick's frame), phase, boost and which stick query is read.
            _flyDiagTimer += Time.deltaTime;
            if (_flyDiagTimer >= 1f)
            {
                _flyDiagTimer = 0f;
                float camYaw = float.NaN;
                try
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 f = cam.transform.forward;
                        camYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                    }
                }
                catch { /* diagnostics only */ }
                DebugLogger.LogState(
                    $"NAV FLY: dist={dist:F0} m alt={mountPos.y:F1} bearing want={wantBearing:F0} " +
                    $"have={haveBearing:F0} err={error:F0} cam={camYaw:F0} stick=({stick.x:F2},{stick.y:F2}) " +
                    $"phase={phase} boost={boost} best={_flyBestDist:F0} " +
                    $"reads left={_flyLeftStickReads} control={_flyControlStickReads}");
                _flyLeftStickReads = 0;
                _flyControlStickReads = 0;
            }
        }

        /// <summary>
        /// Ends the flight and releases every injected input. Silent: the
        /// callers speak (arrival, blocked, cancel) when the player should hear why.
        /// </summary>
        public void CancelAutoFly()
        {
            if (!_flyActive) return;
            _flyActive = false;
            _flyLabel  = null;
            _staticIsAutoFlying = false;
            _staticFlyBoost = false;
            _staticIsAutoWalking = false;
            _staticAutoWalkStickDir = Vector2.zero;
            _staticCameraStickX = 0f;
        }

        /// <summary>Spoken cancel for the deliberate paths (walk key, menu open).</summary>
        private void CancelAutoFlySpoken()
        {
            string label = _flyLabel;
            MelonLoader.MelonLogger.Msg($"[Psynard] FLY cancelled by the player ('{label}').");
            CancelAutoFly();
            ScreenReader.Say(Loc.Get("nav_fly_cancelled", label));
        }

        /// <summary>
        /// The designated psynard landing spot of the town nearest
        /// <paramref name="symbolPos"/>: the world map's mapjump layout records
        /// (one per entrance) each carry a PsynardPosition. Every candidate is
        /// logged so a wrong spot is diagnosable.
        /// </summary>
        private static bool TryFindLandingSpot(Vector3 symbolPos, out Vector3 landing, out string note)
        {
            landing = Vector3.zero;
            note = null;
            try
            {
                var fm = FieldManager.Instance;
                var pm = ParameterManager.Instance;
                if (fm == null || pm == null) return false;

                var layouts = pm.GetMapjumpLayoutParameter(fm.currentFieldmapID);
                if (layouts == null) return false;

                float bestSq = float.MaxValue;
                for (int i = 0; i < layouts.Count; i++)
                {
                    var data = layouts[i];
                    if (data == null) continue;
                    Vector3 spot = data.PsynardPosition;
                    if (spot == Vector3.zero) continue;

                    float entranceSq = FlatDistanceSq(data.Position, symbolPos);
                    DebugLogger.LogState(
                        $"NAV FLY: landing candidate {data.MapjumpID} → {data.ToFieldmapID}: " +
                        $"entrance ({data.Position.x:F1},{data.Position.z:F1}) {Mathf.Sqrt(entranceSq):F0} m " +
                        $"from the symbol, psynard ({spot.x:F1},{spot.y:F1},{spot.z:F1}) " +
                        $"dir={data.PsynardDirection:F0}.");
                    if (entranceSq < bestSq)
                    {
                        bestSq = entranceSq;
                        landing = spot;
                        note = $"landing spot of {data.MapjumpID} → {data.ToFieldmapID}";
                    }
                }

                if (bestSq == float.MaxValue) return false;
                if (bestSq > FlyLandingSpotMaxOffset * FlyLandingSpotMaxOffset)
                {
                    DebugLogger.LogState(
                        $"NAV FLY: nearest landing spot is {Mathf.Sqrt(bestSq):F0} m from the symbol — " +
                        "using the symbol position instead.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV FLY: landing spot lookup failed: {ex.Message}");
                return false;
            }
        }

        private static float FlatDistanceSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        // --------------------------------------------------------------------
        // Parked mount on foot
        // --------------------------------------------------------------------

        /// <summary>
        /// Lists the parked psynard as an Interactable on the world map so it can
        /// be found again on foot or by bunny. The walk target sits a few metres
        /// in front of the mount (its body would block a walk onto its centre)
        /// and the player faces it on arrival, where the game's own get-on prompt
        /// takes over.
        /// </summary>
        private void BuildParkedPsynard(Vector3 playerPos)
        {
            // Always-on log (like [WMReach]): the 2026-09-27 14:4x run had debug
            // off and the row's absence could not be explained.
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return;
                if (fm.IsFieldFlag(FieldBitFlag.Psynard))
                {
                    MelonLoader.MelonLogger.Msg("[Psynard] row skipped: mounted.");
                    return;
                }

                if (!TryFindParkedPsynard(fm, out Transform mount, out string source))
                {
                    MelonLoader.MelonLogger.Msg(
                        "[Psynard] row skipped: no psynard object on this map " +
                        "(FieldManager.FieldPsynard null, no FieldPsynard component, no PSYNARD npc).");
                    return;
                }

                // An inactive object (the game may cull the parked mount at a
                // distance) still has its parked position — list it anyway.
                bool active = mount.gameObject.activeInHierarchy;
                Vector3 mountPos = mount.position;
                Vector3 forward  = mount.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 approach = ChoosePsynardApproach(mountPos, forward, out string side);
                float dist = FlatDistance(playerPos, mountPos);

                _categories[CAT_INTERACTABLE].Add(new NavItem
                {
                    Label         = Loc.Get("nav_psynard_row"),
                    Distance      = dist,
                    Position      = approach,
                    FacePosition  = mountPos,
                    Kind          = InteractableKind.Psynard,
                    Identity      = "psynard",
                });
                MelonLoader.MelonLogger.Msg(
                    $"[Psynard] listed: parked at ({mountPos.x:F1},{mountPos.y:F1},{mountPos.z:F1}) " +
                    $"approach ({approach.x:F1},{approach.z:F1}) [{side}] dist={dist:F0} m " +
                    $"source={source} active={active} object='{mount.gameObject.name}'.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[Psynard] row failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The walk target beside the parked mount: the first of front / right /
        /// left / back (PsynardApproachOffset out) that the current travel
        /// mode's grid can stand on. A mount parked at a plateau edge had its
        /// front point off the grid (2026-09-27 15:10, ten re-path rounds). Front
        /// when no grid answers.
        /// </summary>
        private static Vector3 ChoosePsynardApproach(Vector3 mountPos, Vector3 forward, out string side)
        {
            var mode = WorldmapTravel.CurrentMode();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            var candidates = new (string name, Vector3 dir)[]
            {
                ("front", forward), ("right", right), ("left", -right), ("back", -forward),
            };
            foreach (var c in candidates)
            {
                Vector3 p = mountPos + c.dir * PsynardApproachOffset;
                if (WorldmapPathfinder.IsWalkableWorld(p, mode))
                {
                    side = c.name;
                    return p;
                }
            }
            side = "front, no walkable side";
            return mountPos + forward * PsynardApproachOffset;
        }

        /// <summary>
        /// The parked mount's transform: FieldManager.FieldPsynard first, then
        /// any FieldPsynard component in the scene (inactive included), then a
        /// field npc of type PSYNARD. Which one answered is returned for the log,
        /// because which of these the game keeps after a dismount is unmeasured.
        /// </summary>
        private static bool TryFindParkedPsynard(FieldManager fm, out Transform mount, out string source)
        {
            mount = null;
            source = null;

            try
            {
                var psynard = fm.FieldPsynard;
                if (psynard != null)
                {
                    mount = psynard.transform;
                    source = $"FieldManager.FieldPsynard (state {psynard.PsynardState})";
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[Psynard] FieldManager.FieldPsynard read failed: {ex.Message}");
            }

            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<FieldPsynard>(true);
                if (found != null && found.Length > 0 && found[0] != null)
                {
                    mount = found[0].transform;
                    source = $"FieldPsynard component ({found.Length} in scene, state {found[0].PsynardState})";
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[Psynard] FieldPsynard scene search failed: {ex.Message}");
            }

            try
            {
                var npcs = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (npcs != null)
                {
                    for (int i = 0; i < npcs.Length; i++)
                    {
                        var npc = npcs[i];
                        if (npc == null || npc.NpcType != NpcType.PSYNARD) continue;
                        mount = npc.transform;
                        source = "PSYNARD npc";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[Psynard] npc search failed: {ex.Message}");
            }
            return false;
        }
    }
}
