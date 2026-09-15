using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// World map fishing arrival: the last metres to a baked stand, the wait for
    /// the game's bubble, and the honest verdict when it never shows — including
    /// whether the party leader lacks the Fishing skill (the game shows no bubble
    /// without it, which cost a whole afternoon of cliff-hunting on 2026-09-06).
    /// Stands come from <see cref="WorldmapFishingStands"/>; nothing here scans.
    /// </summary>
    public partial class NavigationHandler
    {
        #region Fishing arrival state

        /// <summary>
        /// True from reaching the stand's arrival radius until the bubble shows or
        /// the wait gives up. The walk stops up to the arrival radius short of the
        /// verified cell, and the game's ~5 m forward probe can miss from there, so
        /// the player creeps onto the exact stand first.
        /// </summary>
        private bool _wmFishCreepActive;

        /// <summary>Wall-clock deadline ending the creep toward the stand.</summary>
        private float _wmFishCreepDeadline;

        /// <summary>Player position where the creep started (distance cap).</summary>
        private Vector3 _wmFishCreepStart;

        /// <summary>When the bubble wait at the stand ends; 0 while still creeping.</summary>
        private float _wmFishHoldUntil;

        /// <summary>
        /// True during the second creep: from the stand on toward the water. The
        /// bake's stand test (<c>CheckWorldmapFishingPoint</c>) is necessary, not
        /// sufficient — at the Salva stand the game showed the bubble only ~2.7 m
        /// closer to the water, and at Krosse the player found it by stepping
        /// toward the water (2026-09-09). So the walk does what the player did.
        /// </summary>
        private bool _wmFishWaterCreep;

        /// <summary>Wall-clock deadline ending the water creep.</summary>
        private float _wmFishWaterCreepDeadline;

        /// <summary>Position and time of the last stall check during the water creep.</summary>
        private Vector3 _wmFishWaterCreepLastPos;
        private float _wmFishWaterCreepLastCheck;

        /// <summary>Maximum seconds to creep before holding at the stand regardless.</summary>
        private const float WmFishCreepMaxSeconds = 5f;

        /// <summary>Maximum metres to creep past the arrival point.</summary>
        private const float WmFishCreepMaxDist = 2f;

        /// <summary>Stick scale during the creep — slow so the water's edge stops the player gently.</summary>
        private const float WmFishCreepSpeedScale = 0.4f;

        /// <summary>Flat distance (m) to the stand cell that counts as standing on it.</summary>
        private const float WmFishStandTolerance = 0.3f;

        /// <summary>
        /// When the creep onto the stand ends farther than this from the cell,
        /// something physical stopped the player short of it — the verdict then
        /// says so instead of "try stepping toward the water" (2026-09-13: the
        /// Arlia stand behind the town wall, 1.2 m short every time).
        /// </summary>
        private const float WmFishStandBlockedMeters = 0.6f;

        /// <summary>Metres the stand creep ended short of the stand; 0 when the stand was reached.</summary>
        private float _wmFishStandShortMeters;

        /// <summary>Seconds to face the water at the stand waiting for the bubble before the honest stop.</summary>
        private const float WmFishBubbleWaitSeconds = 2f;

        /// <summary>Maximum metres of the water creep, measured from the stand.</summary>
        private const float WmFishWaterCreepMaxDist = 8f;

        /// <summary>Maximum seconds of the water creep.</summary>
        private const float WmFishWaterCreepMaxSeconds = 8f;

        /// <summary>Stall detection: less than this many metres within the window means the water's edge stopped the player.</summary>
        private const float WmFishWaterCreepStallDist = 0.15f;
        private const float WmFishWaterCreepStallSeconds = 0.7f;

        /// <summary>Flat distance (m) to the stand within which a showing bubble counts as arrival.</summary>
        private const float WmFishBubbleArrivalMeters = 12f;

        /// <summary>
        /// The facing sweep at the water's edge: stick pushes relative to the
        /// water bearing, each held <see cref="WmFishFanStepSeconds"/>. Pressing
        /// against the edge turns the character without moving it, which is
        /// what produced the bubble by hand every time (2026-09-09: three
        /// arrivals with the game check false while standing still, the bubble
        /// appearing seconds later at the SAME spot once the player turned or
        /// pushed the stick). Writing the transform rotation does not count —
        /// the game re-derives its facing from the input.
        /// </summary>
        private static readonly float[] WmFishFanDegrees = { 0f, 35f, -35f, 70f, -70f, 0f };
        private const float WmFishFanStepSeconds = 0.9f;

        /// <summary>Drift (m) from the edge position that ends the sweep (sliding away along the shore).</summary>
        private const float WmFishFanMaxDrift = 3f;

        /// <summary>Fan state: base bearing toward the water, step index, next step time, anchor position.</summary>
        private Vector3 _wmFishFanBase;
        private int _wmFishFanStep;
        private float _wmFishFanNextAt;
        private Vector3 _wmFishFanAnchor;

        #endregion

        #region Creep + hold

        /// <summary>Starts the creep onto the stand once the walk is within the arrival radius.</summary>
        private void BeginFishCreep(FieldPlayer player, Vector3 playerPos, float targetDist)
        {
            _wmFishCreepActive   = true;
            _wmFishCreepDeadline = Time.time + WmFishCreepMaxSeconds;
            _wmFishCreepStart    = playerPos;
            _wmFishHoldUntil     = 0f;
            _wmFishWaterCreep    = false;
            _wmFishStandShortMeters = 0f;
            DebugLogger.LogState(
                $"NAV WM fishing: stand reached (targetDist={targetDist:F2}) without bubble — " +
                "creeping onto the stand.");
            UpdateFishCreep(player, playerPos);
        }

        /// <summary>
        /// One frame of the fishing arrival, in three steps: creep onto the exact
        /// stand cell; creep on toward the water until the water's edge stops the
        /// player (or a distance/time cap); then hold facing the water for
        /// <see cref="WmFishBubbleWaitSeconds"/>. The bubble itself ends the walk
        /// in the caller's FishPromptShowing branch at any step; this only ever
        /// ends it with the honest "no prompt" verdict. Stuck detection is
        /// bypassed while creeping — pressing gently against the water's edge is
        /// expected here.
        /// </summary>
        private void UpdateFishCreep(FieldPlayer player, Vector3 playerPos)
        {
            if (_wmFishHoldUntil > 0f)
            {
                UpdateFacingSweep(player, playerPos);
                return;
            }

            if (_wmFishWaterCreep)
            {
                UpdateWaterCreep(player, playerPos);
                return;
            }

            float toStand = FlatDistance(playerPos, _autoWalkTarget);
            float crept = FlatDistance(playerPos, _wmFishCreepStart);
            if (toStand <= WmFishStandTolerance || crept >= WmFishCreepMaxDist
                || Time.time >= _wmFishCreepDeadline)
            {
                BeginWaterCreep(player, playerPos, toStand, crept);
                return;
            }

            CreepToward(_autoWalkTarget, playerPos);
        }

        /// <summary>Step two: from the stand on toward the baked water point.</summary>
        private void BeginWaterCreep(FieldPlayer player, Vector3 playerPos, float toStand, float crept)
        {
            if (!_autoWalkFacePosition.HasValue)
            {
                // No water point to aim at (should not happen for a baked stand): hold as before.
                BeginBubbleHold(player, playerPos, "no water point to creep toward");
                return;
            }
            _wmFishWaterCreep          = true;
            _wmFishWaterCreepDeadline  = Time.time + WmFishWaterCreepMaxSeconds;
            _wmFishWaterCreepLastPos   = playerPos;
            _wmFishWaterCreepLastCheck = Time.time;
            bool reached = toStand <= WmFishStandBlockedMeters;
            _wmFishStandShortMeters = reached ? 0f : toStand;
            DebugLogger.LogState(
                (reached
                    ? $"NAV WM fishing: on the stand (toStand={toStand:F2} crept={crept:F2}) without bubble — "
                    : $"NAV WM fishing: STOPPED SHORT of the stand (toStand={toStand:F2} crept={crept:F2}) — " +
                      "something blocks the way onto it; still ") +
                $"creeping toward the water, up to {WmFishWaterCreepMaxDist:F0} m / {WmFishWaterCreepMaxSeconds:F0} s.");
            CreepToward(_autoWalkFacePosition.Value, playerPos);
        }

        /// <summary>One frame of the water creep: stop at a stall (the edge), the distance cap or the deadline.</summary>
        private void UpdateWaterCreep(FieldPlayer player, Vector3 playerPos)
        {
            float fromStand = FlatDistance(playerPos, _autoWalkTarget);
            string stop = null;
            if (fromStand >= WmFishWaterCreepMaxDist) stop = $"distance cap ({fromStand:F1} m from the stand)";
            else if (Time.time >= _wmFishWaterCreepDeadline) stop = "time cap";
            else if (Time.time - _wmFishWaterCreepLastCheck >= WmFishWaterCreepStallSeconds)
            {
                float moved = FlatDistance(playerPos, _wmFishWaterCreepLastPos);
                if (moved < WmFishWaterCreepStallDist)
                    stop = $"stalled at the water's edge ({moved:F2} m in {WmFishWaterCreepStallSeconds:F1} s, {fromStand:F1} m from the stand)";
                _wmFishWaterCreepLastPos   = playerPos;
                _wmFishWaterCreepLastCheck = Time.time;
            }
            if (stop != null)
            {
                _wmFishWaterCreep = false;
                BeginBubbleHold(player, playerPos, stop);
                return;
            }
            CreepToward(_autoWalkFacePosition.Value, playerPos);
        }

        /// <summary>
        /// Step three: at the water's edge, keep pressing the stick gently toward
        /// the water and sweep the push direction through <see cref="WmFishFanDegrees"/>
        /// so the game re-evaluates the prompt for several facings. Ends with the
        /// honest verdict after the last step (the bubble ends it earlier in the
        /// caller). Replaces the old motionless 2 s hold.
        /// </summary>
        private void BeginBubbleHold(FieldPlayer player, Vector3 playerPos, string why)
        {
            Vector3 water = _autoWalkFacePosition ?? (playerPos + player.transform.forward);
            Vector3 toWater = water - playerPos;
            toWater.y = 0f;
            if (toWater.sqrMagnitude < 0.0001f) toWater = player.transform.forward;
            _wmFishFanBase   = toWater.normalized;
            _wmFishFanStep   = 0;
            _wmFishFanNextAt = Time.time + WmFishFanStepSeconds;
            _wmFishFanAnchor = playerPos;
            _wmFishHoldUntil = Time.time + WmFishFanDegrees.Length * WmFishFanStepSeconds;
            DebugLogger.LogState(
                $"NAV WM fishing: water creep ended — {why}; at ({playerPos.x:F1},{playerPos.z:F1}), " +
                $"pressing toward the water and sweeping the facing " +
                $"({WmFishFanDegrees.Length} steps × {WmFishFanStepSeconds:F1} s).");
            CreepDirection(_wmFishFanBase);
        }

        /// <summary>One frame of the facing sweep.</summary>
        private void UpdateFacingSweep(FieldPlayer player, Vector3 playerPos)
        {
            float drift = FlatDistance(playerPos, _wmFishFanAnchor);
            bool done = Time.time >= _wmFishHoldUntil || _wmFishFanStep >= WmFishFanDegrees.Length;
            if (done || drift > WmFishFanMaxDrift)
            {
                _wmFishCreepActive = false;
                string label = _autoWalkLabel;
                float shortBy = _wmFishStandShortMeters;
                DebugLogger.LogState(
                    $"NAV WM fishing: facing sweep ended without a bubble " +
                    (done ? "(all steps tried)" : $"(drifted {drift:F1} m along the shore)") +
                    $" at ({playerPos.x:F1},{playerPos.z:F1})" +
                    (shortBy > 0f ? $"; the stand itself was never reached ({shortBy:F1} m short)." : "."));
                LogFishingArrivalDiag(player, "auto-walk sweep ended");
                StopAutoWalk();
                AnnounceArrival(Loc.Get(FishingNoPromptKey(shortBy), label, shortBy.ToString("F1")));
                return;
            }
            if (Time.time >= _wmFishFanNextAt)
            {
                _wmFishFanStep++;
                _wmFishFanNextAt = Time.time + WmFishFanStepSeconds;
                if (_wmFishFanStep < WmFishFanDegrees.Length)
                {
                    LogFishingArrivalDiag(player, $"sweep step {_wmFishFanStep} ({WmFishFanDegrees[_wmFishFanStep]:F0}°)");
                }
            }
            if (_wmFishFanStep >= WmFishFanDegrees.Length) return; // next frame ends it
            Vector3 dir = Quaternion.AngleAxis(WmFishFanDegrees[_wmFishFanStep], Vector3.up) * _wmFishFanBase;
            CreepDirection(dir);
        }

        /// <summary>Slow stick push toward a world point (flat).</summary>
        private void CreepToward(Vector3 goal, Vector3 playerPos)
        {
            Vector3 dir = goal - playerPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            CreepDirection(dir.normalized);
        }

        /// <summary>Slow stick push along a flat unit direction.</summary>
        private void CreepDirection(Vector3 dir)
        {
            _staticIsAutoWalking = true;
            _wmDirectMoveActive = false;
            _staticAutoWalkStickDir = WorldDirToCameraStick(dir) * WmFishCreepSpeedScale;
        }

        #endregion

        #region Fishing skill + diagnostics

        /// <summary>
        /// True when the party leader has learned the Fishing skill — the game
        /// never shows the bubble otherwise. Unreadable = true (never accuse the
        /// skill on a guess); the reason is always filled for the log.
        /// </summary>
        internal static bool LeaderHasFishingSkill(out string reason)
        {
            try
            {
                var user = ParameterManager.Instance?.UserParameter;
                var party = user?.PartyParameter;
                if (party == null)
                {
                    reason = "party parameter unavailable";
                    return true;
                }
                PlayerID leader = party.LeaderID;
                var character = user.GetCharacterParameter(leader);
                if (character == null)
                {
                    reason = $"no character parameter for leader {leader}";
                    return true;
                }
                bool learned = character.IsLearnedSpecialSkill(SpecialSkillID.FISHING);
                reason = $"leader={leader} fishingSkill={learned}";
                return learned;
            }
            catch (Exception ex)
            {
                reason = "skill check failed: " + ex.Message;
                return true;
            }
        }

        /// <summary>
        /// The right "no bubble" message key, in order of what the player can act
        /// on: the missing skill when that is provable (no bubble can ever show);
        /// the way onto the stand being blocked (<paramref name="standShortMeters"/>
        /// above 0, format argument {1}); else the generic "step toward the water".
        /// </summary>
        private static string FishingNoPromptKey(float standShortMeters = 0f)
        {
            bool hasSkill = LeaderHasFishingSkill(out string reason);
            DebugLogger.LogState($"NAV fishing: no bubble verdict — {reason}" +
                (standShortMeters > 0f ? $", stand blocked {standShortMeters:F1} m short." : "."));
            if (!hasSkill) return "nav_autowalk_arrived_no_fish_skill";
            return standShortMeters > 0f
                ? "nav_autowalk_arrived_fish_stand_blocked"
                : "nav_autowalk_arrived_no_fish_prompt";
        }

        /// <summary>
        /// Queued after the start message of a walk or directions to a fishing
        /// spot when the leader cannot fish: the trip still makes sense (the
        /// player may swap leaders there), but the outcome should not surprise.
        /// </summary>
        private static void WarnIfNoFishingSkill()
        {
            if (LeaderHasFishingSkill(out string reason)) return;
            DebugLogger.LogState($"NAV fishing: warning at start — {reason}.");
            ScreenReader.SayQueued(Loc.Get("nav_fish_skill_missing"));
        }

        /// <summary>
        /// One log line with everything the bubble depends on, from the live
        /// player: painted contact ID, the game's FishingPoint flag, its stand test
        /// from the feet and facing, and the skill verdict.
        /// </summary>
        private static void LogFishingArrivalDiag(FieldPlayer player, string context)
        {
            if (!Main.DebugMode) return;
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || player == null) return;
                Vector3 feet = player.transform.position;
                Vector3 forward = player.transform.forward;
                bool gameCheck = fm.CheckWorldmapFishingPoint(ref feet, ref forward);
                // The game's own entry point with the player object — does it
                // agree with the feet+forward call? (2026-09-09: the latter
                // flickered true/false at a fixed position.)
                string playerCheck;
                try { playerCheck = fm.CheckFishingPoint(player).ToString(); }
                catch (Exception ex) { playerCheck = "error:" + ex.Message; }
                bool flag = fm.IsFieldFlag(FieldBitFlag.FishingPoint);
                int contact = fm.GetContactFishingWaterPlaceID();
                LeaderHasFishingSkill(out string skill);
                Vector3 p = player.transform.position;
                DebugLogger.LogState(
                    $"NAV WM fishing diag ({context}): pos=({p.x:F1},{p.y:F1},{p.z:F1}) " +
                    $"forward=({forward.x:F2},{forward.z:F2}) gameCheck={gameCheck} playerCheck={playerCheck} " +
                    $"fishingPointFlag={flag} contactID={contact} bubble={FieldPromptHandler.FishPromptShowing} {skill}.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM fishing diag failed: {ex.Message}");
            }
        }

        #endregion
    }
}
