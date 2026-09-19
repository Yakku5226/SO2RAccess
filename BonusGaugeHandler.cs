using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Bonus gauge feedback during battle. Spheres fill the gauge; a full gauge raises
    /// the bonus level (each level switches on more party bonuses); the gauge can BREAK
    /// (be lost) — that is what the game's BreakBonusGauge means, not a level-up.
    ///
    /// Four independent outputs, each with its own mod-menu switch:
    /// level speech (once at battle start, then on every level change, with the
    /// bonuses the new level switched on), level beeps (one beep per level on a level
    /// change), break speech + break cue (gauge lost), and the spoken fill percentage.
    ///
    /// Detection: level and fill ratio are polled (the level-up happens inside the
    /// native IncreaseSphereBonusPoint — log 2026-09-18 showed a level-up with no
    /// BreakBonusGauge call); the break comes from a Harmony hook on BreakBonusGauge.
    /// </summary>
    public class BonusGaugeHandler
    {
        #region Fields

        private bool _patchesApplied;

        // Tracking is static so the Harmony callbacks can reach it.
        private static int _lastLevel = -1;
        private static bool _wasInBattle;

        // Highest 5% bucket already spoken for the current level (-1 = none yet).
        private static int _lastAnnouncedGaugeBucket = -1;

        /// <summary>Step (in percent) between spoken bonus-gauge percentages.</summary>
        private const int GaugePercentStep = 5;

        // Bonuses active at the last settled level — the new level's bonuses are
        // whatever is active afterwards and is not in here.
        private static readonly HashSet<BonusBuffType> _activeBuffs = new();

        // A level change waits this long before it is spoken, so the game's bonus
        // value cache has caught up with the new level.
        private const float LevelSpeechDelay = 0.3f;
        private static int _pendingLevel = -1;
        private static float _pendingLevelTime;

        // State captured by the BreakBonusGauge prefix, compared in the postfix.
        private static int _preBreakLevel = -1;
        private static float _preBreakRatio;

        /// <summary>
        /// Gap between repeated beeps in seconds. Each play restarts the cue, and its
        /// audible part lasts ~0.2 s: at 0.15 s the beeps ran into each other and two
        /// sounded like one (user report 2026-09-19).
        /// </summary>
        private const float GaugeFillRepeatGap = 0.25f;

        // All valid BonusBuffType values for iteration.
        private static readonly BonusBuffType[] _allBuffTypes = new[]
        {
            BonusBuffType.SPHERE_UP, BonusBuffType.GUTS_UP, BonusBuffType.EXP_UP,
            BonusBuffType.MP_COST_CUT, BonusBuffType.SUPER_ARMER,
            BonusBuffType.ATK_UP, BonusBuffType.INT_UP, BonusBuffType.DEF_UP,
            BonusBuffType.HIT_UP, BonusBuffType.AVD_UP, BonusBuffType.FOL_UP,
            BonusBuffType.SPHERE_MP_RECOVER, BonusBuffType.REGIST_ABNORMAL,
            BonusBuffType.ITEM_ALL_RANGE, BonusBuffType.ITEM_RECAST_ZERO,
            BonusBuffType.ENEMY_ELEMENT_DISABLE, BonusBuffType.SPHERE_ATK_UP,
            BonusBuffType.ITEM_NOT_CONSUME, BonusBuffType.HP_RECOVER_ONTIME,
            BonusBuffType.MP_RECOVER_ONTIME, BonusBuffType.ATK_INT_UP_ONTIME,
            BonusBuffType.CRT_UP
        };

        #endregion

        #region Patches

        /// <summary>
        /// Registers the Harmony hook on BreakBonusGauge (the gauge being lost).
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BonusBuffType).TypeHandle);

                var breakMethod = AccessTools.Method(typeof(BattleManager),
                    "BreakBonusGauge", new[] { typeof(bool) });
                if (breakMethod != null)
                {
                    harmony.Patch(breakMethod,
                        prefix: new HarmonyMethod(typeof(BonusGaugeHandler),
                            nameof(BreakBonusGauge_Prefix)),
                        postfix: new HarmonyMethod(typeof(BonusGaugeHandler),
                            nameof(BreakBonusGauge_Postfix)));
                    // Not debug-only: F12 is pressed long after start-up, and the
                    // 2026-09-19 review could not confirm the hook from the log.
                    MelonLogger.Msg("BonusGaugeHandler: BreakBonusGauge hook applied.");
                }
                else
                {
                    MelonLogger.Warning("BonusGaugeHandler: BreakBonusGauge method not found.");
                }

                _patchesApplied = true;
                MelonLogger.Msg("BonusGaugeHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BonusGaugeHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Polling

        /// <summary>
        /// Polls the bonus gauge each frame during battle: speaks the level once on
        /// entry, reports level changes (speech + beeps) and speaks the fill percentage.
        /// </summary>
        public void Update()
        {
            var bm = BattleManager.Instance;

            // BattleManager.Instance is a persistent singleton — it exists outside of
            // battle too, so a battle is recognised by its player list.
            var playerList = bm?.battlePlayerList;
            if (playerList == null || playerList.Count == 0)
            {
                if (_wasInBattle) Reset();
                return;
            }

            float ratio;
            int level;
            try
            {
                level = bm.sphereBonusBuffLevel;
                ratio = bm.GetBattleSphereBonusCurrentLevelRatio();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BonusGauge: read error: {ex.Message}");
                return;
            }

            int pct = (int)(ratio * 100f);

            if (!_wasInBattle)
            {
                EnterBattle(bm, level, ratio, pct);
                return;
            }

            if (level != _lastLevel)
                OnLevelChanged(_lastLevel, level);

            if (_pendingLevel >= 0 && Time.time >= _pendingLevelTime)
                SpeakPendingLevel(bm);

            // Spoken exact percentage, every GaugePercentStep percent as it climbs.
            // Capped below 100% — the level announcement covers the level-up.
            if (ModSettings.BonusGaugePercentAnnounceEnabled)
            {
                int bucket = pct - (pct % GaugePercentStep);
                if (bucket >= GaugePercentStep && bucket < 100
                    && bucket > _lastAnnouncedGaugeBucket)
                {
                    _lastAnnouncedGaugeBucket = bucket;
                    ScreenReader.SayQueued(Loc.Get("gauge_percent", bucket));
                }
            }
        }

        /// <summary>
        /// First frame of a battle. Level and fill carry over from the previous battle,
        /// so the tracking is seeded from the live values (nothing is replayed) and the
        /// level is spoken once when it is above zero.
        /// </summary>
        private static void EnterBattle(BattleManager bm, int level, float ratio, int pct)
        {
            _wasInBattle = true;
            _lastLevel = level;
            _pendingLevel = -1;
            _lastAnnouncedGaugeBucket = pct - (pct % GaugePercentStep);
            SnapshotActiveBuffs(bm, _activeBuffs);

            DebugLogger.LogState($"BonusGauge: battle entered, level={level}, ratio={ratio:F2}, "
                + $"active=[{string.Join(", ", _activeBuffs)}].");
            ReviewProbes.LogBonusRows(bm, level, _allBuffTypes);

            if (level > 0 && ModSettings.BonusGaugeLevelAnnounceEnabled)
                ScreenReader.SayQueued(Loc.Get("bonus_gauge_level", level));
        }

        /// <summary>
        /// The polled level moved (a level-up, or a drop the break hook did not report).
        /// Beeps at once — one per level — and schedules the speech.
        /// </summary>
        private static void OnLevelChanged(int oldLevel, int newLevel)
        {
            DebugLogger.LogState($"BonusGauge: level {oldLevel} -> {newLevel}.");
            _lastLevel = newLevel;

            // New level — the gauge starts again near zero, so the spoken percentage
            // may announce from the start of this level.
            _lastAnnouncedGaugeBucket = -1;

            PlayLevelBeeps(newLevel);

            _pendingLevel = newLevel;
            _pendingLevelTime = Time.time + LevelSpeechDelay;
        }

        /// <summary>
        /// Speaks the scheduled level change together with the bonuses that became
        /// active since the last settled level, then settles on the new set.
        /// </summary>
        private static void SpeakPendingLevel(BattleManager bm)
        {
            int level = _pendingLevel;
            _pendingLevel = -1;

            var now = new HashSet<BonusBuffType>();
            SnapshotActiveBuffs(bm, now);

            var gained = new List<string>();
            foreach (var bt in _allBuffTypes)
            {
                if (now.Contains(bt) && !_activeBuffs.Contains(bt))
                    gained.Add(Loc.Get($"bonus_buff_{bt.ToString().ToLower()}"));
            }

            _activeBuffs.Clear();
            _activeBuffs.UnionWith(now);

            DebugLogger.LogState($"BonusGauge: level {level} settled, gained=[{string.Join(", ", gained)}], "
                + $"active=[{string.Join(", ", now)}].");
            ReviewProbes.LogBonusRows(bm, level, _allBuffTypes);

            if (!ModSettings.BonusGaugeLevelAnnounceEnabled) return;

            ScreenReader.SayQueued(gained.Count > 0
                ? Loc.Get("bonus_gauge_break", level, string.Join(", ", gained))
                : Loc.Get("bonus_gauge_level", level));
        }

        /// <summary>Fills <paramref name="target"/> with every bonus that currently has a value.</summary>
        private static void SnapshotActiveBuffs(BattleManager bm, HashSet<BonusBuffType> target)
        {
            target.Clear();
            foreach (var bt in _allBuffTypes)
            {
                // Native call into a changing game API — one bad type must not
                // lose the rest.
                try
                {
                    if (bm.GetSphereBonusBuffValueCache(bt) > 0f) target.Add(bt);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"BonusGauge: buff read error ({bt}): {ex.Message}");
                }
            }
        }

        /// <summary>Plays one gauge beep per level (nothing for level 0), if enabled.</summary>
        private static void PlayLevelBeeps(int level)
        {
            if (level <= 0 || !ModSettings.BonusGaugeLevelBeepEnabled) return;
            if (ModSettings.BonusGaugeSoundVolume < 0.01f || !AudioCuePlayer.IsGaugeFillSoundLoaded) return;
            MelonCoroutines.Start(PlayGaugeFillCoroutine(level));
        }

        #endregion

        #region Hook Callbacks

        /// <summary>Prefix: remember level and fill so the postfix can tell what was lost.</summary>
        private static void BreakBonusGauge_Prefix(BattleManager __instance)
        {
            try
            {
                _preBreakLevel = __instance.sphereBonusBuffLevel;
                _preBreakRatio = __instance.GetBattleSphereBonusCurrentLevelRatio();
            }
            catch (Exception ex)
            {
                _preBreakLevel = -1;
                DebugLogger.LogState($"BonusGauge.BreakPrefix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix: the gauge broke. Reported only when something was really lost — the
        /// game also calls this on an empty gauge, which would be a false alarm.
        /// </summary>
        private static void BreakBonusGauge_Postfix(BattleManager __instance)
        {
            try
            {
                int postLevel = __instance.sphereBonusBuffLevel;
                float postRatio = __instance.GetBattleSphereBonusCurrentLevelRatio();

                bool lost = _preBreakLevel >= 0
                    && (postLevel < _preBreakLevel
                        || (postLevel == _preBreakLevel && postRatio < _preBreakRatio - 0.001f));

                DebugLogger.LogState($"BonusGauge.Break: level {_preBreakLevel} -> {postLevel}, "
                    + $"ratio {_preBreakRatio:F2} -> {postRatio:F2}, lost={lost}.");

                if (!lost) return;

                // Settle the tracking here so the poll does not report the same drop
                // again as a level change.
                _lastLevel = postLevel;
                _pendingLevel = -1;
                _lastAnnouncedGaugeBucket = -1;
                SnapshotActiveBuffs(__instance, _activeBuffs);

                if (ModSettings.BonusGaugeBreakSoundEnabled)
                    AudioCuePlayer.PlayGaugeBreakCue();

                if (ModSettings.BonusGaugeBreakAnnouncementEnabled)
                    ScreenReader.SayQueued(Loc.Get("bonus_gauge_lost", postLevel));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BonusGauge.BreakPostfix error: {ex.Message}");
            }
        }

        #endregion

        #region Sound Playback

        /// <summary>
        /// Coroutine that plays the gauge fill sound the specified number of times
        /// with a short gap between each play. Real-time wait: the battle's time scale
        /// (hit stop, slow motion) must not stretch or swallow the gaps.
        /// </summary>
        private static IEnumerator PlayGaugeFillCoroutine(int count)
        {
            DebugLogger.LogState($"BonusGauge: playing {count} level beep(s), {GaugeFillRepeatGap:F2} s apart.");
            for (int i = 0; i < count; i++)
            {
                AudioCuePlayer.PlayGaugeFillCue();
                if (i < count - 1)
                    yield return new WaitForSecondsRealtime(GaugeFillRepeatGap);
            }
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Resets all tracking state on scene change.
        /// </summary>
        public void OnSceneChanged()
        {
            Reset();
        }

        private static void Reset()
        {
            _wasInBattle = false;
            _lastLevel = -1;
            _pendingLevel = -1;
            _preBreakLevel = -1;
            _lastAnnouncedGaugeBucket = -1;
            _activeBuffs.Clear();
        }

        #endregion
    }
}
