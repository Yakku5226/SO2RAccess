using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces combat status events during active battle:
    ///   - Ally health below 50% / below 25% / knocked out
    ///   - Ally gains a negative status ailment
    ///   - Player-controlled character damage dealt
    ///
    /// All announcements are queued (non-interrupting) via ScreenReader.SayQueued().
    /// Each feature can be toggled independently in ModSettings.
    ///
    /// Detection via Harmony hooks:
    ///   - BattleCharacter.DoCollisionReceiveAction (CallerCount 2) prefix+postfix for HP/damage
    ///   - CharacterParameter.SetBuffDebuffState (CallerCount 19) postfix for ailments
    /// </summary>
    public class BattleStatusHandler
    {
        #region Fields

        private bool _patchesApplied;

        // HP threshold tracking per ally (keyed by BattleCharacter pointer).
        // States: 0=above 50%, 1=below 50%, 2=below 25%, 3=KO.
        private static readonly Dictionary<IntPtr, int> _allyHpState = new();

        // Status ailment tracking per ally (keyed by CharacterParameter pointer).
        // Tracks which ailments have been announced so re-application triggers again.
        private static readonly Dictionary<IntPtr, HashSet<BuffDebuffID>> _allyAilments = new();

        // Pre-damage HP for DoCollisionReceiveAction prefix/postfix pair.
        private static int _preDamageHp;
        private static IntPtr _preDamageVictimPtr;

        // Negative status ailment IDs to announce.
        private static readonly HashSet<BuffDebuffID> _negativeAilments = new()
        {
            BuffDebuffID.POISON,
            BuffDebuffID.PARALYZE,
            BuffDebuffID.PETRIFACTION,
            BuffDebuffID.CONFUSION,
            BuffDebuffID.SILENCE,
            BuffDebuffID.FAINT,
            BuffDebuffID.DEATH,
            BuffDebuffID.STOP,
            BuffDebuffID.SWALLOWED,
            BuffDebuffID.CONTROLLED
        };

        #endregion

        #region Patches

        /// <summary>
        /// Registers Harmony hooks on DoCollisionReceiveAction and SetBuffDebuffState
        /// for battle status announcement detection.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(BattleCharacter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CharacterParameter).TypeHandle);
                // DamageResult intentionally NOT loaded — ref IL2CPP value type
                // crashes Harmony trampolines.
                RuntimeHelpers.RunClassConstructor(typeof(BattleAttackCollision).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BuffDebuffID).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BuffDebuffType).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(TextManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattlePlayerParameter).TypeHandle);

                // Hook DoCollisionReceiveAction(BattleAttackCollision, Vector3)
                // CallerCount(2) — fires when a character receives a collision hit.
                // We use this instead of DoDamage because DoDamage has a ref DamageResult
                // parameter (IL2CPP value type) that corrupts Harmony's trampoline.
                try
                {
                    var doCollision = AccessTools.Method(typeof(BattleCharacter),
                        "DoCollisionReceiveAction",
                        new[] { typeof(BattleAttackCollision),
                                typeof(Vector3) });
                    if (doCollision != null)
                    {
                        harmony.Patch(doCollision,
                            prefix: new HarmonyMethod(typeof(BattleStatusHandler),
                                nameof(DoCollisionReceiveAction_Prefix)),
                            postfix: new HarmonyMethod(typeof(BattleStatusHandler),
                                nameof(DoCollisionReceiveAction_Postfix)));
                        DebugLogger.LogState("BattleStatusHandler: DoCollisionReceiveAction hook applied.");
                    }
                    else
                    {
                        MelonLogger.Warning("BattleStatusHandler: DoCollisionReceiveAction method not found.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BattleStatusHandler: DoCollisionReceiveAction hook failed: {ex.Message}");
                }

                // Hook SetBuffDebuffState — CallerCount(19)
                // Fires when any buff/debuff is applied or removed on a character.
                try
                {
                    var setBuffDebuff = AccessTools.Method(typeof(CharacterParameter),
                        "SetBuffDebuffState",
                        new[] { typeof(BuffDebuffID), typeof(bool),
                                typeof(float), typeof(float), typeof(float) });
                    if (setBuffDebuff != null)
                    {
                        harmony.Patch(setBuffDebuff,
                            postfix: new HarmonyMethod(typeof(BattleStatusHandler),
                                nameof(SetBuffDebuffState_Postfix)));
                        DebugLogger.LogState("BattleStatusHandler: SetBuffDebuffState hook applied.");
                    }
                    else
                    {
                        MelonLogger.Warning("BattleStatusHandler: SetBuffDebuffState method not found.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BattleStatusHandler: SetBuffDebuffState hook failed: {ex.Message}");
                }

                _patchesApplied = true;
                MelonLogger.Msg("BattleStatusHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BattleStatusHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Hook Callbacks

        /// <summary>
        /// Prefix: capture victim's HP before the collision action processes damage.
        /// DoCollisionReceiveAction fires when a character is hit — before DoDamage
        /// applies the actual damage internally.
        /// </summary>
        private static void DoCollisionReceiveAction_Prefix(
            BattleCharacter __instance, BattleAttackCollision attackCollision)
        {
            try
            {
                if (__instance == null) return;
                var charParam = __instance.BattleCharacterParameter?.CharacterParameter;
                if (charParam == null) return;

                _preDamageVictimPtr = __instance.Pointer;
                _preDamageHp = charParam.HitPoint;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"BattleStatus.DoCollisionReceiveAction_Prefix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix: check HP changes after the collision action completes.
        /// The attacker is obtained from attackCollision.OwnerCharacter.
        /// </summary>
        private static void DoCollisionReceiveAction_Postfix(
            BattleCharacter __instance, BattleAttackCollision attackCollision)
        {
            try
            {
                if (__instance == null) return;

                var charParam = __instance.BattleCharacterParameter?.CharacterParameter;
                if (charParam == null) return;

                int postHp = charParam.HitPoint;
                int hpMax = charParam.HitPointMax;

                // Calculate actual damage dealt from HP change.
                int actualDamage = 0;
                if (__instance.Pointer == _preDamageVictimPtr && _preDamageHp > postHp)
                    actualDamage = _preDamageHp - postHp;

                // Feature 5: Player damage dealt.
                // Attacker is control player, target is enemy, damage > 0.
                if (ModSettings.PlayerDamageDealtEnabled && actualDamage > 0
                    && attackCollision != null && !__instance.IsPlayer())
                {
                    try
                    {
                        var attacker = attackCollision.OwnerCharacter;
                        if (attacker != null && attacker.IsControlPlayer())
                        {
                            ScreenReader.SayQueued(
                                Loc.Get("battle_status_damage_dealt", actualDamage));
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState(
                            $"BattleStatus: control player check error: {ex.Message}");
                    }
                }

                // Features 1-3: Ally health tracking.
                if (ModSettings.AllyHealthWarningEnabled
                    && __instance.IsPlayer() && hpMax > 0)
                {
                    CheckAllyHealthThreshold(__instance, postHp, hpMax);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"BattleStatus.DoCollisionReceiveAction_Postfix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix: detect negative status ailments applied to allies.
        /// Also clears tracking when ailments are removed, so re-application
        /// triggers a fresh announcement.
        /// </summary>
        private static void SetBuffDebuffState_Postfix(
            CharacterParameter __instance, BuffDebuffID buffDebuffID, bool flag)
        {
            try
            {
                if (!_negativeAilments.Contains(buffDebuffID)) return;

                IntPtr cpPtr = __instance.Pointer;

                // Ailment removed — clear from tracking so re-application announces.
                if (!flag)
                {
                    if (_allyAilments.TryGetValue(cpPtr, out var tracked))
                        tracked.Remove(buffDebuffID);
                    return;
                }

                // Ailment being applied — only announce for allies.
                if (!ModSettings.AllyStatusAilmentEnabled) return;

                var bm = BattleManager.Instance;
                if (bm == null) return;

                var playerList = bm.battlePlayerList;
                if (playerList == null) return;

                // Find which ally owns this CharacterParameter.
                string allyName = null;
                for (int i = 0; i < playerList.Count; i++)
                {
                    var player = playerList[i];
                    if (player == null) continue;
                    var bp = player.BattleCharacterParameter;
                    if (bp == null) continue;
                    var cp = bp.CharacterParameter;
                    if (cp == null || cp.Pointer != cpPtr) continue;

                    allyName = ResolveAllyName(player);
                    break;
                }

                if (allyName == null) return; // Not a player character

                // Deduplicate: skip if already announced for this ally.
                if (!_allyAilments.TryGetValue(cpPtr, out var ailments))
                {
                    ailments = new HashSet<BuffDebuffID>();
                    _allyAilments[cpPtr] = ailments;
                }
                if (!ailments.Add(buffDebuffID)) return;

                string ailmentName = ResolveBuffDebuffName(buffDebuffID);
                ScreenReader.SayQueued(
                    Loc.Get("battle_status_ailment", allyName, ailmentName));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"BattleStatus.SetBuffDebuffState_Postfix error: {ex.Message}");
            }
        }

        #endregion

        #region HP Threshold Logic

        /// <summary>
        /// Checks if an ally crossed a health threshold and queues an announcement.
        /// Only announces downward transitions (getting worse, not healing).
        /// </summary>
        private static void CheckAllyHealthThreshold(
            BattleCharacter ally, int hp, int hpMax)
        {
            IntPtr ptr = ally.Pointer;
            int currentState = GetHpThresholdState(hp, hpMax);

            if (!_allyHpState.TryGetValue(ptr, out int prevState))
                prevState = 0; // Assume healthy at battle start

            _allyHpState[ptr] = currentState;

            // Only announce downward transitions (health getting worse).
            if (currentState <= prevState) return;

            string name = ResolveAllyName(ally);

            switch (currentState)
            {
                case 1:
                    ScreenReader.SayQueued(
                        Loc.Get("battle_status_hp_below_50", name));
                    break;
                case 2:
                    ScreenReader.SayQueued(
                        Loc.Get("battle_status_hp_below_25", name));
                    break;
                case 3:
                    ScreenReader.SayQueued(
                        Loc.Get("battle_status_ko", name));
                    break;
            }
        }

        /// <summary>
        /// Returns the HP threshold state for a given HP/max pair.
        /// 0=above 50%, 1=below 50%, 2=below 25%, 3=KO.
        /// </summary>
        private static int GetHpThresholdState(int hp, int hpMax)
        {
            if (hp <= 0) return 3;
            int pct = hp * 100 / hpMax;
            if (pct < 25) return 2;
            if (pct < 50) return 1;
            return 0;
        }

        #endregion

        #region Name Resolution

        /// <summary>
        /// Resolves an ally's display name using multiple fallbacks.
        /// </summary>
        private static string ResolveAllyName(BattleCharacter ally)
        {
            try
            {
                var charParam = ally.BattleCharacterParameter?.CharacterParameter;
                string name = charParam?.CharacterName;
                if (!string.IsNullOrEmpty(name))
                    return TextUtil.StripTags(name);

                var bp = ally.BattleCharacterParameter;
                var playerParam = bp?.TryCast<BattlePlayerParameter>();
                if (playerParam != null)
                {
                    var constPlayer = playerParam.PlayerParameter;
                    if (constPlayer != null)
                    {
                        string nameKey = constPlayer.charaNameID;
                        if (!string.IsNullOrEmpty(nameKey))
                        {
                            var tm = TextManager.Instance;
                            if (tm != null)
                            {
                                string resolved = tm.GetMessage(
                                    nameKey, TextManager.MessageType.System);
                                if (!string.IsNullOrEmpty(resolved))
                                    return TextUtil.StripTags(resolved);
                            }
                            return TextUtil.ParseCharaNameID(nameKey);
                        }
                    }
                }
            }
            catch { }

            return "Ally";
        }

        /// <summary>
        /// Resolves a BuffDebuffID to a human-readable name via TextManager.
        /// </summary>
        private static string ResolveBuffDebuffName(BuffDebuffID id)
        {
            try
            {
                string msgId = id.ToMessageID();
                if (!string.IsNullOrEmpty(msgId))
                {
                    var tm = TextManager.Instance;
                    if (tm != null)
                    {
                        string resolved = tm.GetMessage(
                            msgId, TextManager.MessageType.System);
                        if (!string.IsNullOrEmpty(resolved))
                            return TextUtil.StripTags(resolved);
                    }
                }
            }
            catch { }

            return id.ToString();
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Resets all tracking state on scene change.
        /// </summary>
        public void OnSceneChanged()
        {
            _allyHpState.Clear();
            _allyAilments.Clear();
            _preDamageHp = 0;
            _preDamageVictimPtr = IntPtr.Zero;
        }

        #endregion
    }
}
