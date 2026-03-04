using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        #region Fields — Party Formation

        // Party formation sub-screen (selectCharacterSelector on UICampWindow)
        // UICampSelectCharacterSelector extends UISelectorBase (NOT UIListSelectorBase).
        // Uses GetCurrentIndex() method instead of currentIndex property.
        // Character data cached from UICampCharacterStatusPresenter.SetStatus hook.
        private static UICampSelectCharacterSelector _selectCharSelector = null;
        private static readonly SubScreenState _selectCharState = new SubScreenState();
        private static Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData> _selectCharDataList = null;

        #endregion

        #region Fields — Assist Formation

        // Assist formation sub-screen (assistSettingSelector on UICampWindow)
        // UICampAssistSettingSelector with two states: Equip (slot browsing),
        // SelectAssistCharacter (character picker).
        private static UICampAssistSettingSelector _assistSelector = null;
        private static readonly SubScreenState _assistState = new SubScreenState();
        private static UIListSelectorBase _assistEquipListBase = null;
        private static int _assistEquipLastIndex = -1;
        private static UIListSelectorBase _assistCharListBase = null;
        private static int _assistCharLastIndex = -1;
        private static int _assistLastState = -1; // tracks Equip(0) vs SelectAssistCharacter(1)

        #endregion

        #region Fields — Tactics

        // Tactics sub-screen (operationSelector on UICampWindow)
        // UICampOperationSelector extends UIListSelectorBase.
        // Two states: SelectCharacter (pick party member), SelectOperation (pick tactic).
        // Character data: UICampOperationCharacterListItemData (characterName, operation).
        // Operation info: UICampOperationInformationPresenter.Set hook.
        private static UICampOperationSelector _operationSelector = null;
        private static readonly SubScreenState _operationState = new SubScreenState();
        private static int _operationCharLastIndex = -1;
        private static UIListSelectorBase _operationSelectListBase = null;
        private static int _operationSelectLastIndex = -1;
        private static int _operationLastState = -1; // tracks SelectCharacter(0) vs SelectOperation(1)

        #endregion

        #region Update Methods — Party Formation / Assist / Tactics

        /// <summary>
        /// Polls the UICampSelectCharacterSelector for the party formation screen.
        /// Announces "Party formation." when the screen opens.
        /// Announces character name, level, position on navigation.
        /// Character data is cached from UICampCharacterStatusPresenter.SetStatus hook.
        /// </summary>
        private void UpdatePartyFormationSelector()
        {
            if (_selectCharSelector == null) return;

            if (_lastRootMenuItemName != "PartyFormation") return;

            try
            {
                bool isActive = _selectCharSelector.gameObject.activeInHierarchy;

                bool shouldPoll = _selectCharState.CheckEntry(
                    isActive,
                    () => ScreenReader.Say(Loc.Get("camp_party_formation_screen")),
                    "CampPartyFormation");

                if (!shouldPoll) return;

                // Poll GetCurrentIndex() — not a UIListSelectorBase, so method call needed.
                int idx = _selectCharSelector.GetCurrentIndex();
                if (idx == _selectCharState.LastIndex) return;
                _selectCharState.LastIndex = idx;

                if (_selectCharDataList == null || idx < 0 || idx >= _selectCharDataList.Count)
                    return;

                var charData = _selectCharDataList[idx];
                if (charData == null) return;

                string name = charData.characterName ?? "";
                int level = charData.level;
                string position = charData.positionText ?? "";
                int total = _selectCharDataList.Count;

                DebugLogger.LogGameValue("CampPartyFormation.char",
                    $"name='{name}' lv={level} pos='{position}' ({idx + 1}/{total})");

                ScreenReader.Say(Loc.Get("camp_party_formation_char",
                    name, level, position, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdatePartyFormationSelector: {ex.Message}");
                _selectCharSelector = null;
                _selectCharState.Reset();
                _selectCharDataList = null;
            }
        }

        /// <summary>
        /// Polls the UICampAssistSettingSelector for the assist formation screen.
        /// Announces "Assist formation." when the screen opens.
        /// Has two states: Equip (browsing button slots) and SelectAssistCharacter (picking a character).
        /// </summary>
        private void UpdateAssistSettingSelector()
        {
            if (_assistSelector == null) return;

            if (_lastRootMenuItemName != "AssistFormation") return;

            try
            {
                bool isActive = _assistSelector.gameObject.activeInHierarchy;

                bool shouldPoll = _assistState.CheckEntry(
                    isActive,
                    () =>
                    {
                        ScreenReader.Say(Loc.Get("camp_assist_screen"));
                        _assistEquipLastIndex = -1;
                        _assistCharLastIndex = -1;
                        _assistLastState = -1;
                    },
                    "CampAssist",
                    onHidden: () =>
                    {
                        _assistEquipListBase = null;
                        _assistCharListBase = null;
                        _assistLastState = -1;
                    });

                if (!shouldPoll) return;

                int state = (int)_assistSelector.currentState;

                // State changed — reset sub-selector tracking.
                if (state != _assistLastState)
                {
                    _assistLastState = state;
                    if (state == 0) // Equip
                    {
                        _assistEquipLastIndex = -1;
                        DebugLogger.LogState("CampAssist: state → Equip.");
                    }
                    else // SelectAssistCharacter
                    {
                        _assistCharLastIndex = -1;
                        DebugLogger.LogState("CampAssist: state → SelectAssistCharacter.");
                    }
                }

                if (state == 0) // Equip — browsing button slots
                {
                    if (_assistEquipListBase == null)
                    {
                        _assistEquipListBase = _assistSelector.equipListSelector?.TryCast<UIListSelectorBase>();
                        if (_assistEquipListBase == null) return;
                    }

                    int idx = _assistEquipListBase.currentIndex;
                    if (idx == _assistEquipLastIndex) return;
                    _assistEquipLastIndex = idx;

                    var list = _assistEquipListBase.currentDataList;
                    if (list == null) return;
                    int total = list.Count;
                    if (total == 0 || idx < 0 || idx >= total) return;

                    var item = list[idx]?.TryCast<UICampAssistEquipListItemData>();
                    if (item == null) return;

                    string button = item.buttonText ?? "";
                    string charName = item.characterName ?? "";
                    string assistName = item.assistName ?? "";

                    DebugLogger.LogGameValue("CampAssist.slot",
                        $"btn='{button}' char='{charName}' assist='{assistName}' ({idx + 1}/{total})");

                    if (string.IsNullOrEmpty(charName))
                    {
                        ScreenReader.Say(Loc.Get("camp_assist_slot_empty",
                            button, idx + 1, total));
                    }
                    else
                    {
                        ScreenReader.Say(Loc.Get("camp_assist_slot",
                            button, charName, assistName, idx + 1, total));
                    }
                }
                else // SelectAssistCharacter — picking a character
                {
                    if (_assistCharListBase == null)
                    {
                        _assistCharListBase = _assistSelector.characterListSelector?.TryCast<UIListSelectorBase>();
                        if (_assistCharListBase == null) return;
                    }

                    int idx = _assistCharListBase.currentIndex;
                    if (idx == _assistCharLastIndex) return;
                    _assistCharLastIndex = idx;

                    var list = _assistCharListBase.currentDataList;
                    if (list == null) return;
                    int total = list.Count;
                    if (total == 0 || idx < 0 || idx >= total) return;

                    var item = list[idx]?.TryCast<UICampAssistSettingCharacterListItemData>();
                    if (item == null) return;

                    string charName = item.characterName ?? "";
                    string settingNow = item.settingNow ?? "";

                    DebugLogger.LogGameValue("CampAssist.char",
                        $"name='{charName}' settingNow='{settingNow}' ({idx + 1}/{total})");

                    if (!string.IsNullOrEmpty(settingNow))
                    {
                        ScreenReader.Say(Loc.Get("camp_assist_char_current",
                            charName, idx + 1, total));
                    }
                    else
                    {
                        ScreenReader.Say(Loc.Get("camp_assist_char",
                            charName, idx + 1, total));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateAssistSettingSelector: {ex.Message}");
                _assistSelector = null;
                _assistState.Reset();
                _assistEquipListBase = null;
                _assistCharListBase = null;
                _assistLastState = -1;
            }
        }

        /// <summary>
        /// Polls the UICampOperationSelector for the tactics screen.
        /// Announces "Tactics." when the screen opens.
        /// Has two states: SelectCharacter (pick party member) and SelectOperation (pick tactic).
        /// Character state: polled — "[Name]: [Current tactic]. [X] of [Y]."
        /// Operation state: polled position + hook-driven details from
        /// UICampOperationInformationPresenter.Set.
        /// </summary>
        private void UpdateTacticsSelector()
        {
            if (_operationSelector == null) return;

            if (_lastRootMenuItemName != "Tactics") return;

            try
            {
                bool isActive = _operationSelector.gameObject.activeInHierarchy;

                bool shouldPoll = _operationState.CheckEntry(
                    isActive,
                    () =>
                    {
                        ScreenReader.Say(Loc.Get("camp_tactics_screen"));
                        _operationCharLastIndex = -1;
                        _operationSelectLastIndex = -1;
                        _operationLastState = -1;
                    },
                    "CampTactics",
                    onHidden: () =>
                    {
                        _operationSelectListBase = null;
                        _operationLastState = -1;
                    });

                if (!shouldPoll) return;

                int state = (int)_operationSelector.currentState;

                // State changed — reset sub-selector tracking.
                if (state != _operationLastState)
                {
                    _operationLastState = state;
                    if (state == 0) // SelectCharacter
                    {
                        _operationCharLastIndex = -1;
                        DebugLogger.LogState("CampTactics: state → SelectCharacter.");
                    }
                    else // SelectOperation
                    {
                        _operationSelectLastIndex = -1;
                        DebugLogger.LogState("CampTactics: state → SelectOperation.");
                    }
                }

                if (state == 0) // SelectCharacter — browsing party members
                {
                    // UICampOperationSelector extends UIListSelectorBase,
                    // so we can use currentIndex directly.
                    var baseSel = _operationSelector.TryCast<UIListSelectorBase>();
                    if (baseSel == null) return;

                    int idx = baseSel.currentIndex;
                    if (idx == _operationCharLastIndex) return;
                    _operationCharLastIndex = idx;

                    var list = baseSel.currentDataList;
                    if (list == null) return;
                    int total = list.Count;
                    if (total == 0 || idx < 0 || idx >= total) return;

                    var item = list[idx]?.TryCast<UICampOperationCharacterListItemData>();
                    if (item == null) return;

                    string charName = item.characterName ?? "";
                    string operation = item.operation ?? "";

                    DebugLogger.LogGameValue("CampTactics.char",
                        $"name='{charName}' op='{operation}' ({idx + 1}/{total})");

                    ScreenReader.Say(Loc.Get("camp_tactics_char",
                        charName, operation, idx + 1, total));
                }
                else // SelectOperation — picking a tactic
                {
                    if (_operationSelectListBase == null)
                    {
                        _operationSelectListBase = _operationSelector.selectListSelector?.TryCast<UIListSelectorBase>();
                        if (_operationSelectListBase == null) return;
                    }

                    int idx = _operationSelectListBase.currentIndex;
                    if (idx == _operationSelectLastIndex) return;
                    _operationSelectLastIndex = idx;

                    // Position tracking only — the actual operation name + description
                    // are announced by the OperationInfoPresenter_Set_Postfix hook.
                    DebugLogger.LogGameValue("CampTactics.selectOp",
                        $"index={idx + 1}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateTacticsSelector: {ex.Message}");
                _operationSelector = null;
                _operationState.Reset();
                _operationSelectListBase = null;
                _operationLastState = -1;
            }
        }

        #endregion

        #region Harmony Patch Methods — Party Formation / Tactics

        /// <summary>
        /// Postfix for UICampCharacterStatusPresenter.SetStatus(List&lt;CampCharacterStatusParameterData&gt;).
        /// Fires when the party formation screen populates the character status list.
        /// Caches the data list so UpdatePartyFormationSelector can read character info by index.
        /// </summary>
        private static void CharacterStatusPresenter_SetStatus_Postfix(
            Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData> dataList)
        {
            if (dataList == null) return;
            if (_lastRootMenuItemName != "PartyFormation") return;

            try
            {
                _selectCharDataList = dataList;
                DebugLogger.LogState($"CampPartyFormation: cached {dataList.Count} character(s) from SetStatus.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.CharacterStatusPresenter_SetStatus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampOperationInformationPresenter.Set(string, string, string).
        /// Fires when the tactics operation info panel updates — on each navigation
        /// in the operation list. Announces operation name and description.
        /// Gated: only announces when the tactics screen is in SelectOperation state.
        /// </summary>
        private static void OperationInfoPresenter_Set_Postfix(
            string name, string description, string prefabPath)
        {
            if (!_operationState.WasActive) return;
            if (_operationSelector == null) return;
            if (_lastRootMenuItemName != "Tactics") return;

            try
            {
                // Only announce in SelectOperation state (state == 1).
                int state = (int)_operationSelector.currentState;
                if (state != 1) return;

                DebugLogger.LogGameValue("CampTactics.opInfo",
                    $"name='{name}' desc='{description}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(name))
                    sb.Append(name).Append(". ");
                if (!string.IsNullOrEmpty(description))
                    sb.Append(description).Append(". ");

                // Read position and "currently set" flag from the selectListSelector.
                if (_operationSelectListBase != null)
                {
                    int idx = _operationSelectListBase.currentIndex;
                    var list = _operationSelectListBase.currentDataList;
                    int total = list?.Count ?? 0;
                    if (total > 0 && idx >= 0)
                    {
                        var item = list[idx]?.TryCast<UIOperationListItemData>();
                        bool isCurrent = item?.isSetting ?? false;

                        if (isCurrent)
                            sb.Append(Loc.Get("camp_tactics_currently_set")).Append(" ");
                        sb.Append(Loc.Get("camp_tactics_operation_position", idx + 1, total));
                    }
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.OperationInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
