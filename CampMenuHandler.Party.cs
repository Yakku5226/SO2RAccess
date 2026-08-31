using Il2CppGame;
using MelonLoader;
using System;
using System.Text;
using System.Collections.Generic;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        #region Fields — Party Formation

        // Party formation sub-screen (selectCharacterSelector on UICampWindow)
        // UICampSelectCharacterSelector extends UISelectorBase (NOT UIListSelectorBase).
        // CANNOT poll GetCurrentIndex() — it requires currentSelectedPresenter which is
        // always null from managed IL2CPP code. Navigation is fully native.
        // Detection: compare cursor transform position to slot positions each frame.
        private static UICampSelectCharacterSelector _selectCharSelector = null;
        private static readonly SubScreenState _selectCharState = new SubScreenState();
        private static readonly Dictionary<int, CampCharacterStatusParameterData> _selectCharSlotData = new Dictionary<int, CampCharacterStatusParameterData>();

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
        /// Navigation is 100% native — no Harmony hooks fire during cursor movement.
        /// Detection: compare cursor transform position to each character slot position
        /// each frame to determine which slot is highlighted.
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
                    () =>
                    {
                        ScreenReader.Say(Loc.Get("camp_party_formation_screen"));
                    },
                    "CampPartyFormation");

                if (!shouldPoll) return;

                // Detect cursor slot via cursor target matching.
                var cursorPresenter = _selectCharSelector.cursorPresenter;
                var partyPresenter = _selectCharSelector.partyMemberPresenter;
                if (cursorPresenter == null || partyPresenter == null) return;

                var slotList = partyPresenter.partyMemberPresenterList;
                if (slotList == null || slotList.Count == 0) return;

                // Try to get the current target via task objects.
                UICursorTarget currentTarget = null;
                var followTask = cursorPresenter.followTask;
                if (followTask != null)
                    currentTarget = followTask.target;
                if (currentTarget == null)
                {
                    var moveTask = cursorPresenter.moveTask;
                    if (moveTask != null)
                        currentTarget = moveTask.cursorTarget;
                }

                int nearestIdx = -1;

                if (currentTarget != null)
                {
                    // Pointer comparison: match target to slot's cursorTarget.
                    for (int i = 0; i < slotList.Count; i++)
                    {
                        var slot = slotList[i];
                        if (slot == null) continue;
                        if (!slot.gameObject.activeInHierarchy) continue;
                        var slotTarget = slot.cursorTarget;
                        if (slotTarget != null && slotTarget.Pointer == currentTarget.Pointer)
                        {
                            nearestIdx = i;
                            break;
                        }
                    }
                }
                else
                {
                    // Fallback: compare cursor position to each slot's cursorTarget
                    // position (static anchors, not animated slot positions).
                    var cursorGo = cursorPresenter.gameObject;
                    if (cursorGo == null) return;
                    var cursorPos = cursorGo.transform.position;

                    float nearestDist = float.MaxValue;
                    for (int i = 0; i < slotList.Count; i++)
                    {
                        var slot = slotList[i];
                        if (slot == null) continue;
                        if (!slot.gameObject.activeInHierarchy) continue;
                        var slotTarget = slot.cursorTarget;
                        if (slotTarget == null) continue;
                        var targetRt = slotTarget.myRectTransform;
                        if (targetRt == null) continue;
                        var targetPos = targetRt.position;
                        float dist = UnityEngine.Vector3.Distance(cursorPos, targetPos);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearestIdx = i;
                        }
                    }
                }

                if (nearestIdx < 0) return;
                if (nearestIdx == _selectCharState.LastIndex) return;
                _selectCharState.LastIndex = nearestIdx;

                // Announce the character at the detected slot.
                if (!_selectCharSlotData.TryGetValue(nearestIdx, out var charData) || charData == null)
                    return;

                string name = charData.characterName ?? "";
                int level = charData.level;
                int hp = charData.hp;
                int maxHp = charData.maxHp;
                int mp = charData.mp;
                int maxMp = charData.maxMp;

                // Count active slots for position display.
                int total = 0;
                for (int i = 0; i < slotList.Count; i++)
                {
                    var s = slotList[i];
                    if (s != null && s.gameObject != null && s.gameObject.activeInHierarchy)
                        total++;
                }

                string role = charData.characterPosition switch
                {
                    UIDefine.CharacterPosition.Leader => Loc.Get("camp_party_role_leader"),
                    UIDefine.CharacterPosition.Battle => Loc.Get("camp_party_role_battle"),
                    UIDefine.CharacterPosition.Sub    => Loc.Get("camp_party_role_reserve"),
                    UIDefine.CharacterPosition.Assist => Loc.Get("camp_party_role_assist"),
                    _                                 => charData.positionText ?? ""
                };

                DebugLogger.LogGameValue("CampPartyFormation.char",
                    $"name='{name}' lv={level} hp={hp}/{maxHp} mp={mp}/{maxMp} role={role} ({nearestIdx + 1}/{total})");

                var sb = new StringBuilder();
                sb.Append(Loc.Get("camp_party_formation_char",
                    name, level, hp, maxHp, mp, maxMp, role, nearestIdx + 1, total));

                if (charData.isGuest)
                    sb.Append(" ").Append(Loc.Get("camp_party_formation_guest"));
                if (!charData.canDecisioned)
                    sb.Append(" ").Append(Loc.Get("camp_party_formation_unavailable"));

                ScreenReader.Say(sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdatePartyFormationSelector: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-announces the currently selected character after data changes
        /// (e.g. user toggled battle/reserve or changed leader).
        /// Forces the cursor poll to re-announce by resetting LastIndex.
        /// </summary>
        private static void ForceReannounceCurrentSlot()
        {
            _selectCharState.LastIndex = -1;
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
                        $"btn='{button}' char='{charName}' assist='{assistName}' type='{item.battleSkillType}' cool={item.coolTime} ({idx + 1}/{total})");

                    var sb = new StringBuilder();
                    if (string.IsNullOrEmpty(charName))
                    {
                        sb.Append(Loc.Get("camp_assist_slot_empty", button));
                    }
                    else
                    {
                        sb.Append(Loc.Get("camp_assist_slot", button, charName));
                        string summary = BuildAssistSkillSummary(item.assistID, assistName, item.battleSkillType, item.coolTime)
                            ?? assistName;
                        if (!string.IsNullOrEmpty(summary))
                            sb.Append(' ').Append(summary);
                    }
                    TextUtil.AppendPosition(sb, idx, total);
                    ScreenReader.Say(sb.ToString());
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
                        $"name='{charName}' assistID={item.assistID} settingNow='{settingNow}' ({idx + 1}/{total})");

                    var sb = new StringBuilder();
                    sb.Append(!string.IsNullOrEmpty(settingNow)
                        ? Loc.Get("camp_assist_char_current", charName)
                        : Loc.Get("camp_assist_char", charName));

                    // Skill name and type come from the on-screen info panel, which the
                    // game refreshes for the hovered character (UpdateAssistDescription).
                    var panel = _assistSelector.assistDescriptionPresenter;
                    string panelName = TextUtil.StripTags(panel?.assistName?.text);
                    string typeText = TextUtil.StripTags(panel?.battleSkillType?.text);
                    string summary = BuildAssistSkillSummary(item.assistID, panelName, typeText);
                    if (summary != null)
                        sb.Append(' ').Append(summary);

                    TextUtil.AppendPosition(sb, idx, total);
                    ScreenReader.Say(sb.ToString());
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
        /// Builds a spoken summary of the assault skill an assist character performs:
        /// "[skill name], [type], cooldown [N] seconds." — matching exactly what the
        /// screen shows (no description; user decision 2026-07-11, and the game data
        /// has none for assist-only skills anyway).
        /// The skill ID comes from the character's LIVE CharacterParameter.AssistBattleSkillID
        /// (the player-assigned assault skill), falling back to the const assist table for
        /// fixed assist-only characters. displayedSkillName (info panel / list data) is
        /// preferred; the const message table name is the fallback. Do NOT use
        /// UICommon.CreateBattleSkillInformationData here — it returns stale cached data
        /// for invalid IDs and non-party assists (confirmed via 2026-07-11 logs).
        /// Returns null when nothing readable was found (reason logged).
        /// </summary>
        private static string BuildAssistSkillSummary(
            AssistID assistID, string displayedSkillName, string skillTypeText, int coolTime = -1)
        {
            if (assistID == AssistID.INVALID || assistID == AssistID.MAX) return null;

            var pm = ParameterManager.Instance;
            if (pm == null)
            {
                DebugLogger.LogState("CampAssist: ParameterManager null — no skill summary.");
                return null;
            }

            ConstAssistParameter assistParam = null;
            try
            {
                assistParam = pm.GetAssistParameter(assistID);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampAssist: GetAssistParameter({assistID}) failed: {ex.Message}");
            }
            if (assistParam == null)
            {
                DebugLogger.LogState($"CampAssist: no assist parameter for {assistID} — no skill summary.");
                return null;
            }

            // Resolve the actual assault skill: live character assignment first,
            // const table fallback (fixed assist-only characters).
            var playerID = assistParam.PlayerID;
            var skillID = BattleSkillID.INVALID;
            try
            {
                var charaParam = pm.UserParameter?.GetCharacterParameter(playerID);
                if (charaParam != null) skillID = charaParam.AssistBattleSkillID;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampAssist: GetCharacterParameter({playerID}) failed: {ex.Message}");
            }
            if (skillID == BattleSkillID.INVALID) skillID = assistParam.AssistBattleSkillID;

            // The on-screen name is authoritative; const message table fills the gap
            // if the display was empty (first-hover edge case).
            string skillName = TextUtil.StripTags(displayedSkillName)?.Trim() ?? "";
            if (string.IsNullOrEmpty(skillName) && skillID != BattleSkillID.INVALID)
            {
                try
                {
                    var msg = pm.GetConstBattleSkillMessage(skillID);
                    skillName = TextUtil.StripTags(msg?.name) ?? "";
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampAssist: const message failed for {skillID}: {ex.Message}");
                }
            }

            if (coolTime < 0 && skillID != BattleSkillID.INVALID)
            {
                try
                {
                    coolTime = UICommon.CalcAssistCoolTime(skillID, playerID);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampAssist: CalcAssistCoolTime failed for {assistID}: {ex.Message}");
                }
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(skillName)) parts.Add(skillName);
            if (!string.IsNullOrEmpty(skillTypeText)) parts.Add(skillTypeText);
            if (coolTime > 0) parts.Add(Loc.Get("camp_assist_cooldown", coolTime));

            if (parts.Count == 0)
            {
                DebugLogger.LogState($"CampAssist: no readable skill data for {assistID}.");
                return null;
            }

            DebugLogger.LogGameValue("CampAssist.skill",
                $"id={assistID} skillID={skillID} name='{skillName}' " +
                $"type='{skillTypeText}' cool={coolTime}");
            return string.Join(", ", parts) + ".";
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
        /// Fires when the party formation screen updates character status.
        /// Triggers re-announcement so the user hears updated data after changes.
        /// </summary>
        private static void CharacterStatusPresenter_SetStatus_Postfix(
            Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData> dataList)
        {
            if (dataList == null) return;
            if (_lastRootMenuItemName != "PartyFormation") return;

            try
            {
                DebugLogger.LogState($"CampPartyFormation: SetStatus fired with {dataList.Count} character(s).");

                // Force re-announcement of current slot so the user hears
                // updated data (e.g. after toggling battle/reserve or changing leader).
                ForceReannounceCurrentSlot();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.CharacterStatusPresenter_SetStatus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampPartyMemberPresenter.SetData(int index, UICampPartyMemberSelectItemData data).
        /// Fires per-slot when a character is assigned to a party member slot.
        /// Caches the character data keyed by slot index for reliable slot→data mapping.
        /// </summary>
        private static void PartyMemberPresenter_SetData_Postfix(
            int index, UICampPartyMemberSelectItemData data)
        {
            if (_lastRootMenuItemName != "PartyFormation") return;
            if (data == null) return;

            try
            {
                var charData = data.statusParameterData;
                if (charData == null) return;

                _selectCharSlotData[index] = charData;

                DebugLogger.LogState($"CampPartyFormation: SetData slot {index} = '{charData.characterName}'.");

                ForceReannounceCurrentSlot();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.PartyMemberPresenter_SetData_Postfix: {ex.Message}");
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
                    AppendSentence(sb, name);
                if (!string.IsNullOrEmpty(description))
                    AppendSentence(sb, description);

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
