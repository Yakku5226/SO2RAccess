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
    /// Announces bonus gauge progress and break events during battle.
    /// Sound cues play at 25% (1 beep), 50% (2 beeps), 75% (3 beeps).
    /// On break (100%): 4 beeps + screen reader announcement of level and buff.
    ///
    /// Detection: polling for gauge fill ratio each frame,
    /// Harmony hook on BreakBonusGauge (CallerCount 3) for break events.
    /// </summary>
    public class BonusGaugeHandler
    {
        #region Fields

        private bool _patchesApplied;

        // Gauge progress tracking (static for hook access).
        private static int _lastLevel = -1;
        private static readonly HashSet<int> _announcedThresholds = new();
        private static bool _wasInBattle;

        // Pre-break buff snapshot for detecting newly granted buff.
        private static readonly Dictionary<BonusBuffType, float> _preBreakValues = new();

        /// <summary>Gap between repeated beeps in seconds.</summary>
        private const float GaugeFillRepeatGap = 0.15f;

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
        /// Registers Harmony hook on BreakBonusGauge for break detection.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BonusBuffType).TypeHandle);

                try
                {
                    var breakMethod = AccessTools.Method(typeof(BattleManager),
                        "BreakBonusGauge", new[] { typeof(bool) });
                    if (breakMethod != null)
                    {
                        harmony.Patch(breakMethod,
                            prefix: new HarmonyMethod(typeof(BonusGaugeHandler),
                                nameof(BreakBonusGauge_Prefix)),
                            postfix: new HarmonyMethod(typeof(BonusGaugeHandler),
                                nameof(BreakBonusGauge_Postfix)));
                        DebugLogger.LogState("BonusGaugeHandler: BreakBonusGauge hook applied.");
                    }
                    else
                    {
                        MelonLogger.Warning("BonusGaugeHandler: BreakBonusGauge method not found.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BonusGaugeHandler: BreakBonusGauge hook failed: {ex.Message}");
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
        /// Polls the bonus gauge ratio each frame during battle.
        /// Plays sound cues at 25%, 50%, 75% thresholds.
        /// </summary>
        public void Update()
        {
            var bm = BattleManager.Instance;
            if (bm == null)
            {
                if (_wasInBattle) Reset();
                return;
            }

            // BattleManager.Instance is a persistent singleton — it exists
            // outside of battle too. Guard by checking battlePlayerList.
            var playerList = bm.battlePlayerList;
            if (playerList == null || playerList.Count == 0)
            {
                if (_wasInBattle) Reset();
                return;
            }

            if (!_wasInBattle)
            {
                _wasInBattle = true;
                _lastLevel = bm.sphereBonusBuffLevel;
                _announcedThresholds.Clear();
            }

            // Skip if gauge sound disabled (volume at 0).
            if (ModSettings.BonusGaugeSoundVolume < 0.01f) return;
            if (!AudioCuePlayer.IsGaugeFillSoundLoaded) return;

            int level = bm.sphereBonusBuffLevel;
            if (level != _lastLevel)
            {
                _announcedThresholds.Clear();
                _lastLevel = level;
            }

            float ratio;
            try
            {
                ratio = bm.GetBattleSphereBonusCurrentLevelRatio();
            }
            catch
            {
                return;
            }

            int pct = (int)(ratio * 100f);

            if (pct >= 25 && _announcedThresholds.Add(25))
                MelonCoroutines.Start(PlayGaugeFillCoroutine(1));
            if (pct >= 50 && _announcedThresholds.Add(50))
                MelonCoroutines.Start(PlayGaugeFillCoroutine(2));
            if (pct >= 75 && _announcedThresholds.Add(75))
                MelonCoroutines.Start(PlayGaugeFillCoroutine(3));
            // 100% handled by BreakBonusGauge hook.
        }

        #endregion

        #region Hook Callbacks

        /// <summary>
        /// Prefix: snapshot active buffs before break, suppress pending threshold sounds.
        /// </summary>
        private static void BreakBonusGauge_Prefix(BattleManager __instance)
        {
            try
            {
                // Mark all thresholds as announced to prevent duplicate sounds
                // if polling runs in the same frame.
                _announcedThresholds.Add(25);
                _announcedThresholds.Add(50);
                _announcedThresholds.Add(75);

                // Snapshot current buffs to detect the new one in postfix.
                _preBreakValues.Clear();
                foreach (var bt in _allBuffTypes)
                {
                    try
                    {
                        float val = __instance.GetSphereBonusBuffValueCache(bt);
                        if (val > 0f)
                            _preBreakValues[bt] = val;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BonusGauge.BreakPrefix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix: play 4 beeps for the break and announce new level and buff type.
        /// </summary>
        private static void BreakBonusGauge_Postfix(BattleManager __instance)
        {
            try
            {
                // Reset thresholds for the new level.
                _announcedThresholds.Clear();
                _lastLevel = __instance.sphereBonusBuffLevel;

                // Play 4 beeps for break.
                if (ModSettings.BonusGaugeSoundVolume >= 0.01f
                    && AudioCuePlayer.IsGaugeFillSoundLoaded)
                {
                    MelonCoroutines.Start(PlayGaugeFillCoroutine(4));
                }

                // Screen reader announcement.
                if (!ModSettings.BonusGaugeBreakAnnouncementEnabled) return;

                int level = __instance.sphereBonusBuffLevel;

                // Find newly added buff by comparing with pre-break snapshot.
                BonusBuffType newBuff = BonusBuffType.INVALID;
                foreach (var bt in _allBuffTypes)
                {
                    try
                    {
                        float val = __instance.GetSphereBonusBuffValueCache(bt);
                        if (val > 0f && !_preBreakValues.ContainsKey(bt))
                        {
                            newBuff = bt;
                            break;
                        }
                    }
                    catch { }
                }

                string buffName = newBuff != BonusBuffType.INVALID
                    ? Loc.Get($"bonus_buff_{newBuff.ToString().ToLower()}")
                    : Loc.Get("bonus_buff_unknown");

                ScreenReader.SayQueued(Loc.Get("bonus_gauge_break", level, buffName));
                DebugLogger.LogState($"BonusGauge break: level={level}, buff={newBuff}");
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
        /// with a short gap between each play.
        /// </summary>
        private static IEnumerator PlayGaugeFillCoroutine(int count)
        {
            for (int i = 0; i < count; i++)
            {
                AudioCuePlayer.PlayGaugeFillCue();
                if (i < count - 1)
                    yield return new WaitForSeconds(GaugeFillRepeatGap);
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
            _announcedThresholds.Clear();
            _preBreakValues.Clear();
        }

        #endregion
    }
}
