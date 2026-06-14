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
    /// Announces enemy information when the player cycles targets during
    /// L2 target change mode in battle: name, HP percentage (or exact if
    /// Spectacles used), shield/break gauge percentage, leader type, and
    /// active buffs/debuffs.
    ///
    /// Detection uses two methods:
    ///   1. Harmony postfix on BattleManager.SetControlPlayerTarget (CallerCount 7).
    ///   2. Polling each frame — detects target changes AND TargetChangeMode entry
    ///      (so single-enemy battles still get announced when L2 is pressed).
    /// Both are debounced to avoid double announcements.
    /// </summary>
    public class BattleTargetHandler
    {
        private bool _patchesApplied;

        // Polling state — tracks the last announced target by IL2CPP pointer.
        private IntPtr _lastTargetPtr = IntPtr.Zero;

        // Tracks whether we were in target change mode last frame.
        private bool _wasInTargetChangeMode;

        // Ally control player switching (R2).
        private int _lastControlPlayerIndex = -1;
        private bool _controlPlayerSeeded;

        // Debounce: the hook and polling share this to avoid double announcements.
        private static IntPtr _lastAnnouncedPtr = IntPtr.Zero;

        private const int BATTLE_STATE_TARGET_CHANGE = 5;
        private const int BATTLE_STATE_CONTROL_PLAYER_CHANGE = 6;

        // Tracks whether we were in control player change mode last frame.
        private bool _wasInControlPlayerChangeMode;

        /// <summary>
        /// Applies Harmony postfix on SetControlPlayerTarget for target change detection.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleController).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleCharacter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleParameterBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleEnemyParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CharacterParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleEnemy).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattlePlayer).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattlePlayerParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattlePauseSelector).TypeHandle);

                // Hook SetControlPlayerTarget — CallerCount(7), fires when target changes.
                var setTarget = AccessTools.Method(typeof(BattleManager),
                    nameof(BattleManager.SetControlPlayerTarget),
                    new[] { typeof(BattleCharacter), typeof(bool) });
                if (setTarget != null)
                {
                    harmony.Patch(setTarget,
                        postfix: new HarmonyMethod(typeof(BattleTargetHandler),
                            nameof(SetControlPlayerTarget_Postfix)));
                    DebugLogger.LogState("BattleTargetHandler: SetControlPlayerTarget hook applied.");
                }
                else
                {
                    MelonLogger.Warning("BattleTargetHandler: SetControlPlayerTarget method not found.");
                }

                _patchesApplied = true;
                MelonLogger.Msg("BattleTargetHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BattleTargetHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix on BattleManager.SetControlPlayerTarget(BattleCharacter, bool).
        /// CallerCount(7) — fires when the player's target is set, including during
        /// L2 target change mode. Only announces enemy targets (not player characters).
        /// </summary>
        private static void SetControlPlayerTarget_Postfix(BattleCharacter target)
        {
            try
            {
                if (target == null) return;
                if (target.IsPlayer()) return;

                IntPtr ptr = target.Pointer;
                if (ptr == _lastAnnouncedPtr) return;
                _lastAnnouncedPtr = ptr;

                AnnounceTarget(target);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleTarget.SetControlPlayerTarget_Postfix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Called each frame from Main.UpdateHandlers().
        /// Detects target changes via polling AND detects TargetChangeMode entry
        /// to handle single-enemy battles where the target pointer doesn't change.
        /// </summary>
        public void Update()
        {
            try
            {
                var bm = BattleManager.Instance;
                if (bm == null)
                {
                    if (_lastTargetPtr != IntPtr.Zero || _controlPlayerSeeded)
                    {
                        _lastTargetPtr = IntPtr.Zero;
                        _lastAnnouncedPtr = IntPtr.Zero;
                        _wasInTargetChangeMode = false;
                        _lastControlPlayerIndex = -1;
                        _controlPlayerSeeded = false;
                        _wasInControlPlayerChangeMode = false;
                    }
                    return;
                }

                // Check if we just entered target change mode (L2 pressed).
                bool inTargetChangeMode = false;
                var stateMachine = bm.stateMachine;
                if (stateMachine != null)
                    inTargetChangeMode = stateMachine.currentState == BATTLE_STATE_TARGET_CHANGE;

                var uiCtrl = UnityEngine.Object.FindObjectOfType<UIBattleController>();
                if (uiCtrl == null) return;

                var currentTarget = uiCtrl.currentTargetEnemy;
                IntPtr currentPtr = currentTarget != null ? currentTarget.Pointer : IntPtr.Zero;

                // Case 1: Target pointer changed — new enemy selected.
                if (currentPtr != _lastTargetPtr && currentPtr != IntPtr.Zero)
                {
                    _lastTargetPtr = currentPtr;

                    if (currentPtr != _lastAnnouncedPtr)
                    {
                        _lastAnnouncedPtr = currentPtr;
                        AnnounceTarget(currentTarget);
                    }
                }
                // Case 2: Just entered TargetChangeMode but target is the same
                // (single-enemy battle or re-pressing L2). Force re-announce.
                else if (inTargetChangeMode && !_wasInTargetChangeMode
                         && currentPtr != IntPtr.Zero)
                {
                    _lastAnnouncedPtr = currentPtr;
                    AnnounceTarget(currentTarget);
                }
                // Case 3: Target cleared.
                else if (currentPtr == IntPtr.Zero && _lastTargetPtr != IntPtr.Zero)
                {
                    _lastTargetPtr = IntPtr.Zero;
                }

                _wasInTargetChangeMode = inTargetChangeMode;

                // --- Ally control player switching (R2) ---
                bool inControlChangeMode = stateMachine != null
                    && stateMachine.currentState == BATTLE_STATE_CONTROL_PLAYER_CHANGE;

                int ctrlIdx = bm.controlPlayerIndex;

                // Seed the index on first battle frame without announcing.
                if (!_controlPlayerSeeded)
                {
                    _controlPlayerSeeded = true;
                    _lastControlPlayerIndex = ctrlIdx;
                }
                else if (ctrlIdx != _lastControlPlayerIndex)
                {
                    // Index changed — new ally selected.
                    _lastControlPlayerIndex = ctrlIdx;
                    var playerList = bm.BattlePlayerList;
                    if (playerList != null && ctrlIdx >= 0 && ctrlIdx < playerList.Count)
                    {
                        var ally = playerList[ctrlIdx];
                        if (ally != null)
                            AnnounceControlPlayer(ally);
                    }
                }
                else if (inControlChangeMode && !_wasInControlPlayerChangeMode
                         && ctrlIdx >= 0)
                {
                    // Just entered ControlPlayerChangeMode but index is the same
                    // (first R2 press highlights current character). Force announce.
                    var playerList = bm.BattlePlayerList;
                    if (playerList != null && ctrlIdx < playerList.Count)
                    {
                        var ally = playerList[ctrlIdx];
                        if (ally != null)
                            AnnounceControlPlayer(ally);
                    }
                }

                _wasInControlPlayerChangeMode = inControlChangeMode;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleTarget.Update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets tracking state on scene change.
        /// </summary>
        public void OnSceneChanged()
        {
            _lastTargetPtr = IntPtr.Zero;
            _lastAnnouncedPtr = IntPtr.Zero;
            _wasInTargetChangeMode = false;
            _lastControlPlayerIndex = -1;
            _controlPlayerSeeded = false;
            _wasInControlPlayerChangeMode = false;
        }

        /// <summary>
        /// Announces full enemy info: name, HP, shield, leader type, buffs/debuffs.
        /// Called by both the hook and the polling fallback.
        /// </summary>
        private static void AnnounceTarget(BattleCharacter target)
        {
            var battleParam = target.BattleCharacterParameter;
            if (battleParam == null) return;
            var charParam = battleParam.CharacterParameter;
            if (charParam == null) return;

            string baseName = ResolveEnemyName(battleParam, charParam);

            string displayName = ResolveDuplicateName(target, baseName);

            int hp = charParam.HitPoint;
            int hpMax = charParam.HitPointMax;

            // Defeated check
            if (hp <= 0)
            {
                ScreenReader.Say($"{displayName} {Loc.Get("battle_target_defeated")}");
                DebugLogger.LogState($"BattleTarget: {displayName} defeated.");
                return;
            }

            // HP string — exact if spectacled, percentage otherwise
            string hpStr;
            if (IsEnemySpectacled(battleParam) && hpMax > 0)
            {
                hpStr = Loc.Get("battle_target_hp_exact", hp, hpMax);
            }
            else
            {
                int hpPct = hpMax > 0 ? (int)Math.Round(100.0 * hp / hpMax) : 0;
                hpStr = Loc.Get("battle_target_hp_pct", hpPct);
            }

            // Shield/durability string
            float dur = battleParam.DurabilityPoint;
            float durMax = battleParam.DurabilityPointMax;
            string shieldStr = "";
            if (durMax > 0)
            {
                if (dur <= 0)
                {
                    shieldStr = Loc.Get("battle_target_shield_broken");
                }
                else
                {
                    int shieldPct = (int)Math.Round(100.0 * dur / durMax);
                    shieldStr = Loc.Get("battle_target_shield_pct", shieldPct);
                }
            }

            // Leader type string
            string leaderStr = ResolveLeaderType(battleParam);

            // Active buffs/debuffs string
            string statusStr = ResolveBuffDebuffs(charParam);

            // Build final message
            var parts = new List<string> { displayName, hpStr };
            if (!string.IsNullOrEmpty(shieldStr))
                parts.Add(shieldStr);
            if (!string.IsNullOrEmpty(leaderStr))
                parts.Add(leaderStr);
            if (!string.IsNullOrEmpty(statusStr))
                parts.Add(statusStr);

            string message = string.Join(" ", parts);
            ScreenReader.Say(message);
            DebugLogger.LogState($"BattleTarget: {displayName} HP={hp}/{hpMax} Dur={dur}/{durMax}" +
                $" leader={leaderStr ?? "none"} status={statusStr ?? "none"}");
        }

        /// <summary>
        /// Announces ally info when the player switches the controlled character
        /// via R2: name, HP, MP, and any active buffs/debuffs.
        /// </summary>
        private static void AnnounceControlPlayer(BattleCharacter ally)
        {
            var charParam = ally.BattleCharacterParameter?.CharacterParameter;
            if (charParam == null) return;

            string name = BattleStatusHandler.ResolveAllyName(ally);
            int hp = charParam.HitPoint;
            int hpMax = charParam.HitPointMax;
            int mp = charParam.MentalPoint;
            int mpMax = charParam.MentalPointMax;

            string statusStr = ResolveBuffDebuffs(charParam);

            string message;
            if (!string.IsNullOrEmpty(statusStr))
                message = Loc.Get("battle_ally_switch_status", name, hp, hpMax, mp, mpMax, statusStr);
            else
                message = Loc.Get("battle_ally_switch", name, hp, hpMax, mp, mpMax);

            ScreenReader.Say(message);
            DebugLogger.LogState($"BattleTarget: Control player → {name} HP={hp}/{hpMax} MP={mp}/{mpMax}" +
                $" status={statusStr ?? "none"}");
        }

        #region Helpers

        /// <summary>
        /// Resolves the enemy's display name. Tries CharacterParameter.CharacterName
        /// first (set by native init), falls back to ConstEnemyParameter.charaNameID
        /// parsed into a readable name (e.g. "CHARA_LIZARDAXE" → "Lizardaxe").
        /// </summary>
        internal static string ResolveEnemyName(BattleParameterBase battleParam,
            CharacterParameter charParam)
        {
            // Try the runtime name first (set by native BattleCharacter.Initialize).
            string name = charParam.CharacterName;
            if (!string.IsNullOrEmpty(name))
                return TextUtil.StripTags(name);

            // Fall back to ConstEnemyParameter.charaNameID.
            try
            {
                var enemyParam = battleParam.TryCast<BattleEnemyParameter>();
                if (enemyParam != null)
                {
                    var constEnemy = enemyParam.EnemyParameter;
                    if (constEnemy != null)
                    {
                        string nameKey = constEnemy.charaNameID;
                        if (!string.IsNullOrEmpty(nameKey))
                            return TextUtil.ResolveCharaNameKey(nameKey);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleTarget.ResolveEnemyName fallback error: {ex.Message}");
            }

            return "Enemy";
        }

        /// <summary>
        /// Checks whether this enemy has been scanned with Spectacles by querying
        /// the game's see-through list on UIBattlePauseSelector.
        /// </summary>
        internal static bool IsEnemySpectacled(BattleParameterBase battleParam)
        {
            try
            {
                var enemyParam = battleParam.TryCast<BattleEnemyParameter>();
                if (enemyParam == null) return false;

                var constEnemy = enemyParam.EnemyParameter;
                if (constEnemy == null) return false;

                var pauseSelector = UnityEngine.Object.FindObjectOfType<UIBattlePauseSelector>();
                if (pauseSelector == null) return false;

                return pauseSelector.IsSeeThroughEnemy(constEnemy.EnemyID);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves the leader type of an enemy to a display string.
        /// Returns null if the enemy is not a leader.
        /// Chain: BattleEnemyParameter.LeaderType → ToSystemMessageID → TextManager.
        /// </summary>
        internal static string ResolveLeaderType(BattleParameterBase battleParam)
        {
            try
            {
                var enemyParam = battleParam.TryCast<BattleEnemyParameter>();
                if (enemyParam == null) return null;

                var leaderType = enemyParam.LeaderType;
                if (leaderType == EnemyLeaderType.INVALID) return null;

                string msgId = leaderType.ToSystemMessageID();
                if (string.IsNullOrEmpty(msgId)) return null;

                var tm = TextManager.Instance;
                string resolved = tm?.GetMessage(msgId, TextManager.MessageType.System);
                if (string.IsNullOrEmpty(resolved))
                    resolved = leaderType.ToString();

                return Loc.Get("battle_target_leader", TextUtil.StripTags(resolved));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleTarget.ResolveLeaderType error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves active buffs and debuffs on an enemy to a comma-separated string.
        /// Returns null if none are active. Skips BREAK (announced via shield gauge).
        /// Chain: CharacterParameter.GetBuffDebuffList → ToMessageID → TextManager.
        /// </summary>
        internal static string ResolveBuffDebuffs(CharacterParameter charParam)
        {
            try
            {
                var buffList = charParam.GetBuffDebuffList(BuffDebuffType.INVALID);
                if (buffList == null || buffList.Count == 0) return null;

                var tm = TextManager.Instance;
                var names = new List<string>();

                for (int i = 0; i < buffList.Count; i++)
                {
                    var id = buffList[i];
                    // BREAK is already covered by the shield gauge reading.
                    if (id == BuffDebuffID.BREAK || id == BuffDebuffID.INVALID) continue;

                    string msgId = id.ToMessageID();
                    if (string.IsNullOrEmpty(msgId)) continue;

                    string resolved = tm?.GetMessage(msgId, TextManager.MessageType.System);
                    if (!string.IsNullOrEmpty(resolved))
                        names.Add(TextUtil.StripTags(resolved));
                    else
                        names.Add(id.ToString());
                }

                if (names.Count == 0) return null;
                return Loc.Get("battle_target_status", string.Join(", ", names));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleTarget.ResolveBuffDebuffs error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves duplicate enemy names by appending a number suffix when
        /// multiple alive enemies share the same base name.
        /// Uses ResolveEnemyName for consistent name comparison.
        /// </summary>
        internal static string ResolveDuplicateName(BattleCharacter target, string baseName)
        {
            try
            {
                var bm = BattleManager.Instance;
                if (bm == null) return baseName;

                var enemyList = bm.battleEnemyList;
                if (enemyList == null) return baseName;

                // Collect all enemies with this base name
                var sameNameEnemies = new List<BattleEnemy>();
                for (int i = 0; i < enemyList.Count; i++)
                {
                    var enemy = enemyList[i];
                    if (enemy == null) continue;
                    var bp = enemy.BattleCharacterParameter;
                    if (bp == null) continue;
                    var cp = bp.CharacterParameter;
                    if (cp == null) continue;

                    string name = ResolveEnemyName(bp, cp);
                    if (name == baseName)
                        sameNameEnemies.Add(enemy);
                }

                if (sameNameEnemies.Count <= 1)
                    return baseName;

                // Find which number this target is
                for (int i = 0; i < sameNameEnemies.Count; i++)
                {
                    if (sameNameEnemies[i].Pointer == target.Pointer)
                        return $"{baseName} {i + 1}";
                }

                return baseName;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleTarget.ResolveDuplicateName error: {ex.Message}");
                return baseName;
            }
        }

        #endregion
    }
}
