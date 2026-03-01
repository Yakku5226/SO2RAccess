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

        // Heading + inner selector caching is deferred to the hook, because
        // activeInHierarchy is always true for these selectors. The hook only
        // fires when the screen is truly showing skill data.
        private static bool _battleSkillHeadingPending = false;

        // Combat skill toggle mode (ChangeOnOff) — tracks Square button sub-mode.
        private static UICampCombatSkillSelector.State _combatSkillLastState =
            UICampCombatSkillSelector.State.Normal;
        private static int _combatSkillToggleLastIndex = -1;
        private static bool _combatSkillToggleLastIsUse = false;

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
        /// Tracks entry/exit of battle/combat skill leveling screens.
        /// These selectors have activeInHierarchy=true permanently, so we cannot
        /// use that for detection. Instead, we set a "heading pending" flag when
        /// the root menu item changes to a skill-related item, and the
        /// UIBattleSkillInformationPresenter.Set hook handles caching the inner
        /// selectors and announcing the heading when it fires for the first time.
        /// This method also polls the combat skill toggle mode (Square button).
        /// </summary>
        private void UpdateBattleSkillSelector()
        {
            if (_battleSkillOuterSelector == null) return;

            if (!IsBattleSkillRelatedMenu())
            {
                // Root menu moved away from skill items — deactivate.
                if (_battleSkillWasActive)
                {
                    _battleSkillWasActive = false;
                    _battleSkillHeadingPending = false;
                    _battleSkillListBase = null;
                    _battleSkillInnerSelector = null;
                    _combatSkillListBase = null;
                    _combatSkillInnerSelector = null;
                    _combatSkillLastState = UICampCombatSkillSelector.State.Normal;
                    _combatSkillToggleLastIndex = -1;
                    _combatSkillToggleLastIsUse = false;
                    DebugLogger.LogState("CampBattleSkill: deactivated (root menu moved away).");
                }
                return;
            }

            try
            {
                if (!_battleSkillWasActive)
                {
                    // Root menu just landed on a battle/combat skill item.
                    // Don't cache or announce yet — the hook will do that.
                    _battleSkillWasActive = true;
                    _battleSkillHeadingPending = true;
                    _battleSkillInnerSelector = null;
                    _battleSkillListBase = null;
                    _combatSkillInnerSelector = null;
                    _combatSkillListBase = null;
                    _combatSkillLastState = UICampCombatSkillSelector.State.Normal;
                    _combatSkillToggleLastIndex = -1;
                    _combatSkillToggleLastIsUse = false;
                    DebugLogger.LogState($"CampBattleSkill: heading pending for '{_lastRootMenuItemName}'.");
                }

                // Poll combat skill toggle mode (Square button: Normal ↔ ChangeOnOff).
                if (_combatSkillInnerSelector != null)
                    UpdateCombatSkillToggleMode();
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
                _battleSkillHeadingPending = false;
                _combatSkillLastState = UICampCombatSkillSelector.State.Normal;
                _combatSkillToggleLastIndex = -1;
                _combatSkillToggleLastIsUse = false;
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

            bool levelingActive = _battleSkillWasActive && _battleSkillOuterSelector != null;

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
                    // On first hook fire: cache inner selectors based on root menu
                    // item name and announce heading + balance.
                    if (_battleSkillHeadingPending)
                    {
                        _battleSkillHeadingPending = false;
                        CacheBattleSkillInnerSelectors();
                    }

                    // Read list item data for BP cost, max level, and current balance.
                    int pointCost = 0;
                    bool isMax = false;
                    string balance = "";
                    int idx = -1;
                    int total = 0;

                    if (_combatSkillInnerSelector != null && _combatSkillListBase != null)
                    {
                        var items = _combatSkillInnerSelector.itemDataList;
                        total = items?.Count ?? 0;
                        idx   = _combatSkillListBase.currentIndex;
                        if (total > 0 && idx >= 0 && idx < total)
                        {
                            var itemData = items[idx];
                            pointCost = itemData.consumeBP;
                            isMax     = itemData.isLevelMax;
                        }
                        balance = ReadSkillPointBalance(_combatSkillInnerSelector);
                    }
                    else if (_battleSkillInnerSelector != null && _battleSkillListBase != null)
                    {
                        var items = _battleSkillInnerSelector.itemDataList;
                        total = items?.Count ?? 0;
                        idx   = _battleSkillListBase.currentIndex;
                        if (total > 0 && idx >= 0 && idx < total)
                        {
                            var itemData = items[idx];
                            pointCost = itemData.consumeBP;
                            isMax     = itemData.isLevelMax;
                        }
                        balance = ReadSkillPointBalance(_battleSkillInnerSelector);
                    }

                    DebugLogger.LogGameValue("CampBattleSkill.info",
                        $"name='{name}' lv={level}/{levelMax} mp={consumeMP} " +
                        $"cost={pointCost} isMax={isMax} balance='{balance}' " +
                        $"root='{_lastRootMenuItemName}' " +
                        $"desc='{description}' effect='{effect}'");

                    var sb = new StringBuilder();
                    AppendSkillInfo(sb, data, pointCost, isMax, balance);

                    if (total > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_battleskill_position", idx + 1, total));

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
        /// Caches the appropriate inner selector and announces the heading + balance.
        /// Called from the hook on the first fire after root menu item changes.
        /// Uses _lastRootMenuItemName to determine which inner selector to cache,
        /// avoiding the broken activeInHierarchy / stale outer-state detection.
        /// </summary>
        private static void CacheBattleSkillInnerSelectors()
        {
            if (_lastRootMenuItemName == "CombatPoint")
            {
                _combatSkillInnerSelector = _battleSkillOuterSelector.combatSkillSelector;
                _combatSkillListBase = _combatSkillInnerSelector?.TryCast<UIListSelectorBase>();
                _battleSkillInnerSelector = null;
                _battleSkillListBase = null;
                _combatSkillLastState = _combatSkillInnerSelector?.currentState
                    ?? UICampCombatSkillSelector.State.Normal;
                _combatSkillToggleLastIndex = -1;

                ScreenReader.Say(Loc.Get("camp_combatskill_screen"));
                DebugLogger.LogState("CampCombatSkill: cached (combat skills).");
            }
            else
            {
                _battleSkillInnerSelector = _battleSkillOuterSelector.battleSkillSelector;
                _battleSkillListBase = _battleSkillInnerSelector?.TryCast<UIListSelectorBase>();
                _combatSkillInnerSelector = null;
                _combatSkillListBase = null;

                ScreenReader.Say(Loc.Get("camp_battleskill_screen"));
                DebugLogger.LogState($"CampBattleSkill: cached (battle skills) for '{_lastRootMenuItemName}'.");
            }
        }

        /// <summary>
        /// Polls the combat skill selector's state for toggle mode changes (Square button).
        /// In ChangeOnOff mode, tracks index changes and announces each skill's active/inactive
        /// status via isUse from UICampCombatSkillListItemData.
        /// Called from UpdateBattleSkillSelector when combat skill selector is active.
        /// </summary>
        private void UpdateCombatSkillToggleMode()
        {
            var state = _combatSkillInnerSelector.currentState;

            if (state != _combatSkillLastState)
            {
                _combatSkillLastState = state;

                if (state == UICampCombatSkillSelector.State.ChangeOnOff)
                {
                    ScreenReader.Say(Loc.Get("camp_combatskill_toggle"));
                    _combatSkillToggleLastIndex = -1; // force first item announcement
                    _combatSkillToggleLastIsUse = false;
                    DebugLogger.LogState("CampCombatSkill: entered toggle mode.");
                }
                else
                {
                    // Back to normal — re-announce heading.
                    ScreenReader.Say(Loc.Get("camp_combatskill_screen"));
                    _combatSkillToggleLastIndex = -1;
                    _combatSkillToggleLastIsUse = false;
                    DebugLogger.LogState("CampCombatSkill: exited toggle mode.");
                }
            }

            // In toggle mode, poll index and isUse — announce on either change.
            // Index changes when navigating; isUse changes when confirming a toggle.
            if (state == UICampCombatSkillSelector.State.ChangeOnOff && _combatSkillListBase != null)
            {
                int idx = _combatSkillListBase.currentIndex;
                var items = _combatSkillInnerSelector.itemDataList;
                int total = items?.Count ?? 0;
                bool isActive = false;

                if (total > 0 && idx >= 0 && idx < total)
                    isActive = items[idx].isUse;

                if (idx != _combatSkillToggleLastIndex || isActive != _combatSkillToggleLastIsUse)
                {
                    _combatSkillToggleLastIndex = idx;
                    _combatSkillToggleLastIsUse = isActive;

                    if (total > 0 && idx >= 0 && idx < total)
                    {
                        string skillName = items[idx].skillName ?? "";

                        string key = isActive ? "camp_combatskill_active" : "camp_combatskill_inactive";
                        ScreenReader.Say(Loc.Get(key, skillName, idx + 1, total));

                        DebugLogger.LogGameValue("CampCombatSkill.toggle",
                            $"name='{skillName}' isUse={isActive} ({idx + 1}/{total})");
                    }
                }
            }
        }

        /// <summary>
        /// Appends battle skill details (name, level, BP balance/cost, MP cost, description, effect)
        /// to a StringBuilder. Shared between leveling and assignment screen hooks.
        /// </summary>
        /// <param name="pointCost">BP cost to level up (from list item data). 0 = omit.</param>
        /// <param name="isMaxLevel">Whether the skill is at max level (from list item data).</param>
        /// <param name="balance">Current BP balance string (from skillPointValue). Empty = omit.</param>
        private static void AppendSkillInfo(StringBuilder sb, UIBattleSkillInformationData data,
            int pointCost = 0, bool isMaxLevel = false, string balance = "")
        {
            string name        = data.battleSkillName        ?? "";
            string description = data.battleSkillDescription ?? "";
            string effect      = data.effectDescription      ?? "";
            int levelMax       = data.skillLevelMax;
            int consumeMP      = data.consumeMP;

            if (!string.IsNullOrEmpty(name))        sb.Append(name).Append(". ");
            if (levelMax > 0)
            {
                sb.Append(Loc.Get("camp_battleskill_level", data.skillLevel, levelMax));
                if (isMaxLevel) sb.Append(Loc.Get("camp_skill_max_level"));
                sb.Append(". ");
            }
            // Both battle skills and combat skills use BP (Battle Points).
            // Show "BP: balance / cost" so the user always knows what they can afford.
            if (pointCost > 0 && !isMaxLevel && !string.IsNullOrEmpty(balance))
                sb.Append(Loc.Get("camp_skill_bp_cost", balance, pointCost)).Append(". ");
            if (consumeMP > 0)
                sb.Append(Loc.Get("camp_battleskill_mp", consumeMP)).Append(". ");
            if (!string.IsNullOrEmpty(description)) AppendSentence(sb, description);
            if (!string.IsNullOrEmpty(effect))      AppendSentence(sb, effect);
        }

        /// <summary>
        /// Appends text to a StringBuilder, ensuring it ends with exactly ". ".
        /// Prevents double punctuation when game data already ends with a period.
        /// </summary>
        private static void AppendSentence(StringBuilder sb, string text)
        {
            sb.Append(text.TrimEnd('.', ' ')).Append(". ");
        }

        /// <summary>
        /// Reads the displayed skill point balance from a selector's skillPointValue GameText.
        /// Works for UISelectBattleSkillSelector (SP) and UICampCombatSkillSelector (BP).
        /// Returns the text as-is (e.g. "100" or "SP: 100"), or empty string on failure.
        /// </summary>
        private static string ReadSkillPointBalance(object selector)
        {
            try
            {
                GameText gt = null;
                if (selector is UISelectBattleSkillSelector bs)
                    gt = bs.skillPointValue;
                else if (selector is UICampCombatSkillSelector cs)
                    gt = cs.skillPointValue;
                else if (selector is UICampSkillSelector ss)
                    gt = ss.skillPointValue;

                if (gt == null) return "";
                string text = gt.text;
                return string.IsNullOrEmpty(text) ? "" : text.Trim();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ReadSkillPointBalance failed: {ex.Message}");
                return "";
            }
        }
    }
}
