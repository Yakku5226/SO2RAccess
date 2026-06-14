using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    // Partial class fragment of BattleMenuHandler: target-selection (ally/enemy) and tactics pollers.
    public partial class BattleMenuHandler
    {
        #region Phases D & E: Target + Tactics

        private void PollTargetSelector()
        {
            if (_targetSelector == null) return;

            try
            {
                bool isAll = _targetSelector.isSelectedAll;
                bool isPlayer = _targetSelector.isSelectPlayer;

                // AoE: announce once
                if (isAll)
                {
                    if (!_lastTargetAllAnnounced)
                    {
                        _lastTargetAllAnnounced = true;
                        string skillName = ResolveUseDescTitle();

                        if (isPlayer)
                            ScreenReader.Say(Loc.Get("battle_menu_target_all_allies", skillName));
                        else
                            ScreenReader.Say(Loc.Get("battle_menu_target_all_enemies", skillName));
                    }
                    return;
                }

                if (isPlayer)
                    PollAllyTarget();
                else
                    PollEnemyTarget();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTargetSelector error: {ex.Message}");
            }
        }

        private void PollEnemyTarget()
        {
            var enemySel = _targetSelector.selectEnemySelector;
            if (enemySel == null) return;

            int idx = enemySel.currentIndex;
            if (idx == _lastTargetIndex && _lastTargetIsEnemy) return;
            _lastTargetIsEnemy = true;
            _lastTargetIndex = idx;

            try
            {
                var charList = enemySel.battleCharacterList;
                if (charList == null || idx < 0 || idx >= charList.Count) return;

                var bc = charList[idx];
                if (bc == null) return;

                string skillName = ResolveUseDescTitle();
                int total = charList.Count;

                // Resolve enemy name via BattleTargetHandler helpers
                var battleParam = bc.BattleCharacterParameter;
                var charParam = battleParam?.CharacterParameter;

                string enemyName = BattleTargetHandler.ResolveEnemyName(battleParam, charParam);
                enemyName = BattleTargetHandler.ResolveDuplicateName(bc, enemyName);

                // HP — exact if spectacled, percent otherwise
                if (charParam != null && charParam.HitPoint <= 0)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy",
                        skillName, enemyName, 0, idx + 1, total));
                }
                else if (BattleTargetHandler.IsEnemySpectacled(battleParam))
                {
                    int hp = charParam?.HitPoint ?? 0;
                    int hpMax = charParam?.HitPointMax ?? 1;
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy_exact",
                        skillName, enemyName, hp, hpMax, idx + 1, total));
                }
                else if (charParam != null)
                {
                    int hpMax = charParam.HitPointMax;
                    int pct = hpMax > 0 ? (charParam.HitPoint * 100 / hpMax) : 0;
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy",
                        skillName, enemyName, pct, idx + 1, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy_unknown",
                        skillName, enemyName, idx + 1, total));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollEnemyTarget error: {ex.Message}");
            }
        }

        private void PollAllyTarget()
        {
            int idx = _targetSelector.currentIndex;
            if (idx == _lastTargetIndex && !_lastTargetIsEnemy) return;
            _lastTargetIsEnemy = false;
            _lastTargetIndex = idx;

            try
            {
                var charList = _targetSelector.battleCharacterList;
                if (charList == null || idx < 0 || idx >= charList.Count) return;

                var bc = charList[idx];
                if (bc == null) return;

                string skillName = ResolveUseDescTitle();
                int total = charList.Count;

                var charParam = bc.BattleCharacterParameter?.CharacterParameter;
                string name = charParam?.CharacterName ?? "???";
                int hp = charParam?.HitPoint ?? 0;
                int hpMax = charParam?.HitPointMax ?? 0;
                int mp = charParam?.MentalPoint ?? 0;
                int mpMax = charParam?.MentalPointMax ?? 0;

                // Self-targeting (list has 1 entry)
                if (total == 1)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_target_self", skillName));
                    return;
                }

                ScreenReader.Say(Loc.Get("battle_menu_target_ally",
                    skillName, name, hp, hpMax, mp, mpMax, idx + 1, total));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollAllyTarget error: {ex.Message}");
            }
        }

        /// <summary>Gets the skill/item name for target announcements.</summary>
        private string ResolveUseDescTitle()
        {
            // First try hook cache
            if (!string.IsNullOrEmpty(_cachedUseDescTitle))
                return _cachedUseDescTitle;

            // Fallback: read directly from presenter
            try
            {
                var presenter = _targetSelector?.useDescriptionPresenter;
                if (presenter != null)
                {
                    var titleText = presenter.title;
                    if (titleText != null)
                    {
                        string text = ((Il2CppTMPro.TMP_Text)titleText)?.text;
                        if (!string.IsNullOrEmpty(text))
                            return TextUtil.StripTags(text);
                    }
                }
            }
            catch { }

            // Last fallback: check the info cache label
            return _cachedInfoLabel ?? "";
        }



        private void PollTacticsSelector()
        {
            if (_tacticsSelector == null) return;

            try
            {
                int state = (int)_tacticsSelector.currentState;

                // State transition
                if (state != _lastTacticsState)
                {
                    _lastTacticsState = state;
                    _lastTacticsCharIndex = -1;
                    _lastTacticsOpIndex = -1;
                    _tacticsOpListBase = null;
        
                }

                if (state == 0) // SelectCharacter
                    PollTacticsCharacter();
                else if (state == 1) // SelectOperation
                    PollTacticsOperation();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTacticsSelector error: {ex.Message}");
            }
        }

        private void PollTacticsCharacter()
        {
            int idx = _tacticsSelector.currentIndex;
            if (idx == _lastTacticsCharIndex) return;
            _lastTacticsCharIndex = idx;

            try
            {
                var charList = _tacticsSelector.characterDataList;
                if (charList == null || idx < 0 || idx >= charList.Count) return;

                var charData = charList[idx];
                if (charData == null) return;

                string name = charData.characterName ?? "???";
                string operation = charData.operation ?? "";
                int total = charList.Count;

                ScreenReader.Say(Loc.Get("battle_menu_tactics_char",
                    name, operation, idx + 1, total));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTacticsCharacter error: {ex.Message}");
            }
        }

        private void PollTacticsOperation()
        {
            // Lazy-find the operation list
            if (_tacticsOpListBase == null)
            {
                _tacticsOpListBase = _tacticsSelector.operationListSelector?.TryCast<UIListSelectorBase>();
                if (_tacticsOpListBase == null) return;
            }

            int idx = _tacticsOpListBase.currentIndex;
            if (idx == _lastTacticsOpIndex) return;
            _lastTacticsOpIndex = idx;

            try
            {
                // Read operation name from the list item presenter's displayed text.
                // Each UIBattleOperationListItemPresenter has an operationName GameText
                // that's already populated by the game for visible items.
                string opName = "";

                // Try hook cache first
                if (!string.IsNullOrEmpty(_cachedOpName))
                {
                    opName = _cachedOpName;
                }
                else
                {
                    // Read from the operation list item presenters on the UI.
                    // The battle tactics uses UICommonListItemPresenter (via UIOperationListItemPresenter)
                    // which has a textMesh GameText field containing the operation name.
                    try
                    {
                        var opListPresenter = _tacticsSelector.operationListPresenter;
                        if (opListPresenter != null)
                        {
                            var presenters = opListPresenter.gameObject
                                .GetComponentsInChildren<UICommonListItemPresenter>();
                            if (presenters != null && idx >= 0 && idx < presenters.Count)
                            {
                                var nameText = presenters[idx].textMesh;
                                if (nameText != null)
                                    opName = ((Il2CppTMPro.TMP_Text)nameText)?.text ?? "";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState($"BattleMenuHandler: op presenter read error: {ex.Message}");
                    }
                }

                opName = TextUtil.StripTags(opName).Trim();

                var dataList = _tacticsOpListBase.currentDataList;
                int total = dataList?.Count ?? 0;

                // Check if this is the currently set operation
                bool isCurrent = false;
                if (dataList != null && idx >= 0 && idx < dataList.Count)
                {
                    var opData = dataList[idx]?.TryCast<UIOperationListItemData>();
                    if (opData != null)
                        isCurrent = opData.isSetting;
                }

                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(opName))
                    sb.Append(opName).Append(". ");
                if (isCurrent)
                    sb.Append(Loc.Get("battle_menu_tactics_current")).Append(" ");
                if (total > 0 && idx >= 0)
                    sb.Append(Loc.Get("battle_menu_position", idx + 1, total));

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTacticsOperation error: {ex.Message}");
            }
        }

        #endregion
    }
}
