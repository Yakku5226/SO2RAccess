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
        // Battle skill LEVELING sub-screen (battleSkillSelector on UICampWindow)
        // UICampBattleSkillSelector wraps two inner selectors:
        //   - UISelectBattleSkillSelector (battleSkillSelector) for battle skills (SP)
        //   - UICampCombatSkillSelector (combatSkillSelector) for combat skills (BP)
        // State.SelectBattleSkill → battle skills, State.SelectCombatSkill → combat skills.
        // Both share UIBattleSkillInformationPresenter.Set hook for navigation announcements.
        //
        // Accessed via:
        //   Camp → BattleSkill (main menu)
        //   Camp → Enhance → BattleSkillPoint (battle skill leveling with SP)
        //   Camp → Enhance → CombatPoint (combat skill leveling with BP)
        private static UICampBattleSkillSelector _battleSkillOuterSelector = null;
        private static UISelectBattleSkillSelector _battleSkillInnerSelector = null;
        private static UIListSelectorBase _battleSkillListBase = null;
        private static UICampCombatSkillSelector _combatSkillInnerSelector = null;
        private static UIListSelectorBase _combatSkillListBase = null;
        private static bool _battleSkillWasActive = false;
        private static bool _battleSkillSuppressHeading = false;

        // Battle skill EQUIP SETTING sub-screen (battleSkillSettingSelector on UICampWindow)
        // Shows button slots (L2/R2 etc.) with their assigned skills, and a skill picker.
        // UICampBattleSkillSettingSelector fields used:
        //   equipListSelector (UICampBattleSkillEquipListSelector → UIHelpListSelectorBase →
        //     UIListSelectorBase) — the list of button slots; cast for currentIndex.
        //   battleSkillListSelector (UICampBattleSkillListSelector → UIListSelectorBase)
        //     — the skill picker (visible when assigning a skill to a slot).
        //   currentState (State: Equip = browsing slots, SelectBattleSkill = picking a skill)
        // Item data: UICampBattleSkillSettingEquipListItemData — categoryName (button label),
        //   battleSkillName (skill in that slot, empty if none), equipPosition (enum).
        private static UICampBattleSkillSettingSelector _battleSkillSettingSelector = null;
        private static UICampBattleSkillEquipListSelector _battleSkillEquipListSel = null;
        private static UIListSelectorBase _battleSkillEquipListBase = null;
        private static UIListSelectorBase _battleSkillPickerListBase = null;
        private static int _battleSkillEquipLastIndex = -1;
        private static bool _battleSkillSettingWasActive = false;
        private static bool _battleSkillSettingSuppressHeading = false;

        /// <summary>
        /// Checks if the current root menu item is one that opens a battle/combat skill screen.
        /// "BattleSkill" = main camp menu item.
        /// "BattleSkillPoint" = Enhance sub-menu (battle skill leveling with SP).
        /// "CombatPoint" = Enhance sub-menu (combat skill leveling with BP).
        /// </summary>
        private static bool IsBattleSkillRelatedMenu()
        {
            return _lastRootMenuItemName == "BattleSkill"
                || _lastRootMenuItemName == "BattleSkillPoint"
                || _lastRootMenuItemName == "CombatPoint";
        }

        /// <summary>
        /// Polls the UICampBattleSkillSelector (outer container) for active state changes.
        /// Announces "Battle skills." or "Combat skills." when the screen opens.
        /// Skill-level navigation announcements are handled entirely by the
        /// UIBattleSkillInformationPresenter.Set hook — this method only tracks
        /// open/close state and caches the inner selector references.
        ///
        /// The outer selector has two states:
        ///   SelectBattleSkill → caches UISelectBattleSkillSelector (battle skill SP leveling)
        ///   SelectCombatSkill → caches UICampCombatSkillSelector (combat skill BP leveling)
        /// </summary>
        private void UpdateBattleSkillSelector()
        {
            if (_battleSkillOuterSelector == null) return;

            // Only poll when the root menu highlights a battle/combat skill item.
            if (!IsBattleSkillRelatedMenu())
            {
                // Don't reset _battleSkillWasActive here.
                // Resetting causes stale announcements when root menu cursor
                // returns to the item during normal navigation.
                return;
            }

            try
            {
                bool isActive = _battleSkillOuterSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_battleSkillWasActive)
                    {
                        _battleSkillWasActive = false;
                        _battleSkillListBase = null;
                        _battleSkillInnerSelector = null;
                        _combatSkillListBase = null;
                        _combatSkillInnerSelector = null;
                        DebugLogger.LogState("CampBattleSkill: selector hidden.");
                    }
                    return;
                }

                if (!_battleSkillWasActive)
                {
                    _battleSkillWasActive = true;

                    // Check which inner selector to cache based on the outer's state.
                    var outerState = _battleSkillOuterSelector.currentState;

                    if (outerState == UICampBattleSkillSelector.State.SelectCombatSkill)
                    {
                        // Combat skill leveling (via Enhance → CombatPoint)
                        _combatSkillInnerSelector = _battleSkillOuterSelector.combatSkillSelector;
                        _combatSkillListBase = _combatSkillInnerSelector?.TryCast<UIListSelectorBase>();
                        _battleSkillInnerSelector = null;
                        _battleSkillListBase = null;

                        if (_combatSkillListBase == null)
                            MelonLogger.Warning("[CAMP] combatSkill inner selector cast to UIListSelectorBase failed.");

                        if (!_battleSkillSuppressHeading)
                        {
                            ScreenReader.Say(Loc.Get("camp_combatskill_screen"));
                            DebugLogger.LogState("CampCombatSkill: selector visible (combat skills).");
                        }
                        else
                        {
                            _battleSkillSuppressHeading = false;
                            DebugLogger.LogState("CampCombatSkill: stale open — heading suppressed.");
                        }
                    }
                    else
                    {
                        // Battle skill leveling (via BattleSkill or Enhance → BattleSkillPoint)
                        _battleSkillInnerSelector = _battleSkillOuterSelector.battleSkillSelector;
                        _battleSkillListBase = _battleSkillInnerSelector?.TryCast<UIListSelectorBase>();
                        _combatSkillInnerSelector = null;
                        _combatSkillListBase = null;

                        if (_battleSkillListBase == null)
                            MelonLogger.Warning("[CAMP] battleSkill inner selector cast to UIListSelectorBase failed.");

                        if (!_battleSkillSuppressHeading)
                        {
                            ScreenReader.Say(Loc.Get("camp_battleskill_screen"));
                            DebugLogger.LogState("CampBattleSkill: selector visible (battle skills).");
                        }
                        else
                        {
                            _battleSkillSuppressHeading = false;
                            DebugLogger.LogState("CampBattleSkill: stale open — heading suppressed.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateBattleSkillSelector: {ex.Message}");
                _battleSkillOuterSelector = null;
                _battleSkillInnerSelector = null;
                _battleSkillListBase = null;
                _combatSkillInnerSelector = null;
                _combatSkillListBase = null;
                _battleSkillWasActive = false;
                _battleSkillSuppressHeading = false;
            }
        }

        /// <summary>
        /// Polls the UICampBattleSkillSettingSelector (button assignment screen).
        /// Announces "Battle skill assignment." when the screen opens.
        /// In Equip state (browsing button slots): polls the equip slot list and announces
        ///   "[Button]: [Skill name]" or "[Button]: no skill assigned".
        /// In SelectBattleSkill state (picking a skill): the UIBattleSkillInformationPresenter.Set
        ///   hook handles announcements with "Assigning to [button]:" prefix.
        /// </summary>
        private void UpdateBattleSkillSettingSelector()
        {
            if (_battleSkillSettingSelector == null) return;

            // Only poll when the root menu highlights a battle skill item.
            if (!IsBattleSkillRelatedMenu())
            {
                // Don't reset _battleSkillSettingWasActive or equip state here.
                // Resetting causes stale announcements when root menu cursor
                // returns to the item during normal navigation.
                return;
            }

            try
            {
                bool isActive = _battleSkillSettingSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_battleSkillSettingWasActive)
                    {
                        _battleSkillSettingWasActive = false;
                        _battleSkillEquipListSel = null;
                        _battleSkillEquipListBase = null;
                        _battleSkillPickerListBase = null;
                        _battleSkillEquipLastIndex = -1;
                        DebugLogger.LogState("CampBattleSkillSetting: selector hidden.");
                    }
                    return;
                }

                if (!_battleSkillSettingWasActive)
                {
                    _battleSkillSettingWasActive = true;

                    _battleSkillEquipListSel  = _battleSkillSettingSelector.equipListSelector;
                    _battleSkillEquipListBase  = _battleSkillEquipListSel?.TryCast<UIListSelectorBase>();
                    var pickerSel              = _battleSkillSettingSelector.battleSkillListSelector;
                    _battleSkillPickerListBase  = pickerSel?.TryCast<UIListSelectorBase>();

                    if (!_battleSkillSettingSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_battleskill_setting_screen"));
                        _battleSkillEquipLastIndex = -1;
                        DebugLogger.LogState("CampBattleSkillSetting: selector visible.");
                    }
                    else
                    {
                        _battleSkillSettingSuppressHeading = false;
                        DebugLogger.LogState("CampBattleSkillSetting: stale open — heading suppressed.");
                    }
                }

                // In Equip state: poll slot list. In SelectBattleSkill: hook handles it.
                var state = _battleSkillSettingSelector.currentState;
                if (state == UICampBattleSkillSettingSelector.State.Equip)
                    UpdateBattleSkillEquipSlotList();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateBattleSkillSettingSelector: {ex.Message}");
                _battleSkillSettingSelector = null;
                _battleSkillEquipListSel = null;
                _battleSkillEquipListBase = null;
                _battleSkillPickerListBase = null;
                _battleSkillEquipLastIndex = -1;
                _battleSkillSettingWasActive = false;
                _battleSkillSettingSuppressHeading = false;
            }
        }

        /// <summary>
        /// Polls the equip slot list (UICampBattleSkillEquipListSelector) for index changes
        /// and announces the highlighted button slot and the skill assigned to it.
        /// Called by UpdateBattleSkillSettingSelector only while in Equip state.
        /// </summary>
        private void UpdateBattleSkillEquipSlotList()
        {
            if (_battleSkillEquipListBase == null) return;

            try
            {
                int idx = _battleSkillEquipListBase.currentIndex;
                if (idx == _battleSkillEquipLastIndex) return;
                _battleSkillEquipLastIndex = idx;

                var list = _battleSkillEquipListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                // GetCurrentData() reads the currently focused slot (same index).
                var item = _battleSkillEquipListSel?.GetCurrentData();
                if (item == null) return;

                string button    = StripTags(item.categoryName    ?? "");
                string skillName = item.battleSkillName ?? "";

                DebugLogger.LogGameValue("CampBattleSkillSetting.slot",
                    $"button='{button}' skill='{skillName}' ({idx + 1}/{total})");

                if (string.IsNullOrEmpty(skillName))
                    ScreenReader.Say(Loc.Get("camp_battleskill_setting_slot_empty", button, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("camp_battleskill_setting_slot", button, skillName, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateBattleSkillEquipSlotList: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIBattleSkillInformationPresenter.Set(UIBattleSkillInformationData).
        /// Fires whenever any skill information panel updates — the leveling screen (both
        /// battle skills and combat skills via Enhance) and the setting screen's skill picker
        /// share this hook via the same presenter type.
        ///
        /// Leveling screen (UICampBattleSkillSelector active):
        ///   Announces skill name, level, MP cost, description, effect, position.
        ///   Works for both battle skills (SelectBattleSkill) and combat skills (SelectCombatSkill).
        ///
        /// Setting screen (UICampBattleSkillSettingSelector active, SelectBattleSkill state):
        ///   Announces "Assigning to [button]: [name]. [level]. [MP]. [desc]. [position]."
        ///   When in Equip state (browsing slots), hook is silent — polling handles it.
        /// </summary>
        private static void BattleSkillInfoPresenter_Set_Postfix(UIBattleSkillInformationData data)
        {
            if (data == null) return;

            // Only process when the root menu is on a battle/combat skill item.
            if (!IsBattleSkillRelatedMenu()) return;

            bool levelingActive =
                _battleSkillWasActive &&
                _battleSkillOuterSelector != null &&
                _battleSkillOuterSelector.gameObject.activeInHierarchy;

            bool settingActive =
                _battleSkillSettingWasActive &&
                _battleSkillSettingSelector != null &&
                _battleSkillSettingSelector.gameObject.activeInHierarchy;

            if (!levelingActive && !settingActive) return;

            try
            {
                string name        = data.battleSkillName        ?? "";
                string description = data.battleSkillDescription ?? "";
                string effect      = data.effectDescription      ?? "";
                int level          = data.skillLevel;
                int levelMax       = data.skillLevelMax;
                int consumeMP      = data.consumeMP;

                // --- Leveling screen ---
                if (levelingActive)
                {
                    DebugLogger.LogGameValue("CampBattleSkill.info",
                        $"name='{name}' lv={level}/{levelMax} mp={consumeMP} " +
                        $"desc='{description}' effect='{effect}'");

                    var sb = new StringBuilder();
                    AppendSkillInfo(sb, data);

                    // Read position from the appropriate inner selector.
                    // Combat skills use _combatSkillInnerSelector; battle skills use _battleSkillInnerSelector.
                    if (_combatSkillInnerSelector != null && _combatSkillListBase != null)
                    {
                        var items = _combatSkillInnerSelector.itemDataList;
                        int total = items?.Count ?? 0;
                        int idx   = _combatSkillListBase.currentIndex;
                        if (total > 0 && idx >= 0)
                            sb.Append(Loc.Get("camp_battleskill_position", idx + 1, total));
                    }
                    else if (_battleSkillInnerSelector != null && _battleSkillListBase != null)
                    {
                        var items = _battleSkillInnerSelector.itemDataList;
                        int total = items?.Count ?? 0;
                        int idx   = _battleSkillListBase.currentIndex;
                        if (total > 0 && idx >= 0)
                            sb.Append(Loc.Get("camp_battleskill_position", idx + 1, total));
                    }

                    string result = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(result))
                        ScreenReader.Say(result);
                    return;
                }

                // --- Setting screen skill picker ---
                // Only announce in SelectBattleSkill state; in Equip state polling handles it.
                var settingState = _battleSkillSettingSelector.currentState;
                if (settingState != UICampBattleSkillSettingSelector.State.SelectBattleSkill)
                    return;

                // Read the button name from the currently selected equip slot.
                string buttonName = "";
                var slotData = _battleSkillEquipListSel?.GetCurrentData();
                if (slotData != null)
                    buttonName = StripTags(slotData.categoryName ?? "");

                DebugLogger.LogGameValue("CampBattleSkillSetting.picker",
                    $"button='{buttonName}' name='{name}' lv={level}/{levelMax} mp={consumeMP} " +
                    $"desc='{description}' effect='{effect}'");

                var sb2 = new StringBuilder();
                if (!string.IsNullOrEmpty(buttonName))
                    sb2.Append(Loc.Get("camp_battleskill_setting_assigning", buttonName)).Append(". ");
                AppendSkillInfo(sb2, data);

                if (_battleSkillPickerListBase != null)
                {
                    int pickerIdx   = _battleSkillPickerListBase.currentIndex;
                    var pickerList  = _battleSkillPickerListBase.currentDataList;
                    int pickerTotal = pickerList?.Count ?? 0;
                    if (pickerTotal > 0 && pickerIdx >= 0)
                        sb2.Append(Loc.Get("camp_battleskill_position", pickerIdx + 1, pickerTotal));
                }

                string result2 = sb2.ToString().Trim();
                if (!string.IsNullOrEmpty(result2))
                    ScreenReader.Say(result2);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.BattleSkillInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends battle skill details (name, level, MP cost, description, effect)
        /// to a StringBuilder. Shared between leveling and assignment screen hooks.
        /// </summary>
        private static void AppendSkillInfo(StringBuilder sb, UIBattleSkillInformationData data)
        {
            string name        = data.battleSkillName        ?? "";
            string description = data.battleSkillDescription ?? "";
            string effect      = data.effectDescription      ?? "";
            int levelMax       = data.skillLevelMax;
            int consumeMP      = data.consumeMP;

            if (!string.IsNullOrEmpty(name))        sb.Append(name).Append(". ");
            if (levelMax > 0)
                sb.Append(Loc.Get("camp_battleskill_level", data.skillLevel, levelMax)).Append(". ");
            if (consumeMP > 0)
                sb.Append(Loc.Get("camp_battleskill_mp", consumeMP)).Append(". ");
            if (!string.IsNullOrEmpty(description)) sb.Append(description).Append(". ");
            if (!string.IsNullOrEmpty(effect))      sb.Append(effect).Append(". ");
        }
    }
}
