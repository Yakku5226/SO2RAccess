using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Plays an audio cue when an enemy is about to hit the player, giving
    /// time to press X and dodge. Hooks BattleCharacter.DoAttackNotify —
    /// the game's own visual "incoming attack" flash trigger.
    /// </summary>
    public class BattleCounterHandler
    {
        private bool _patchesApplied;

        // The game raises DoAttackNotify twice within ~50 ms for some attacks (log
        // 2026-09-06 11:43:49.895 and .945). PlaySound restarts the cue on every
        // call, so the second call cut the first one off after 50 ms — heard as a
        // stutter. One warning per this window; the cue itself is 150 ms long.
        private const float RepeatWindowSeconds = 0.15f;
        private static float _lastWarningTime = -1f;

        /// <summary>
        /// Applies Harmony patch on DoAttackNotify for incoming-attack detection.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleCharacter).TypeHandle);

                var doAttackNotify = AccessTools.Method(typeof(BattleCharacter),
                    nameof(BattleCharacter.DoAttackNotify),
                    new[] { typeof(BattleCharacter) });
                var postfix = AccessTools.Method(typeof(BattleCounterHandler),
                    nameof(DoAttackNotify_Postfix));
                harmony.Patch(doAttackNotify, postfix: new HarmonyMethod(postfix));

                _patchesApplied = true;
                MelonLogger.Msg("BattleCounterHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BattleCounterHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix on BattleCharacter.DoAttackNotify(BattleCharacter target).
        /// Fires when the game triggers its visual "incoming attack" flash on a character.
        /// If the target is the player, plays the dodge warning audio cue.
        /// </summary>
        private static void DoAttackNotify_Postfix(BattleCharacter target)
        {
            try
            {
                if (target == null) return;
                if (!target.IsControlPlayer()) return;
                if (!ModSettings.DodgeSoundEnabled) return;

                float now = UnityEngine.Time.unscaledTime;
                if (now - _lastWarningTime < RepeatWindowSeconds)
                {
                    DebugLogger.LogState("BattleCounter: incoming attack — repeat within window, cue left playing.");
                    return;
                }
                _lastWarningTime = now;

                AudioCuePlayer.PlayDodgeWarningCue();
                DebugLogger.LogState("BattleCounter: incoming attack — dodge warning played.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleCounter.DoAttackNotify_Postfix error: {ex.Message}");
            }
        }
    }
}
