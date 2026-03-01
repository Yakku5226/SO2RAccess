using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        // Battle skill LEVELING sub-screen (battleSkillSelector on UICampWindow)
        // UICampBattleSkillSelector wraps two inner selectors:
        //   - UISelectBattleSkillSelector (battleSkillSelector) for battle skills (BP)
        //   - UICampCombatSkillSelector (combatSkillSelector) for combat skills (BP)
        // State.SelectBattleSkill → battle skills, State.SelectCombatSkill → combat skills.
        // Both share UIBattleSkillInformationPresenter.Set hook for navigation announcements.
        //
        // Accessed via:
        //   Camp → BattleSkill (root menu) — tactical readout
        //   Camp → Enhance → BattleSkillPoint (battle skill leveling) — upgrade-focused
        //   Camp → Enhance → CombatPoint (combat skill leveling) — upgrade-focused
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

        // Tracks which specific skill sub-item we're on (e.g. "CombatPoint" vs
        // "BattleSkillPoint" vs "BattleSkill") so we re-cache inner selectors
        // when switching between them within the same gate.
        private static string _lastBattleSkillMenuItem = "";

        // Combat skill toggle mode (ChangeOnOff) — tracks Square button sub-mode.
        // Only used in Enhance menu (CombatPoint), not in root BattleSkill.
        private static UICampCombatSkillSelector.State _combatSkillLastState =
            UICampCombatSkillSelector.State.Normal;
        private static int _combatSkillToggleLastIndex = -1;
        private static bool _combatSkillToggleLastIsUse = false;

        // Battle skill EQUIP SETTING sub-screen (battleSkillSettingSelector on UICampWindow)
        // Shows button slots (L2/R2 etc.) with their assigned skills, and a skill picker.
        // Only accessible from the root BattleSkill menu item.
        private static UICampBattleSkillSettingSelector _battleSkillSettingSelector = null;
        private static UICampBattleSkillEquipListSelector _battleSkillEquipListSel = null;
        private static UIListSelectorBase _battleSkillEquipListBase = null;
        private static UIListSelectorBase _battleSkillPickerListBase = null;
        private static int _battleSkillEquipLastIndex = -1;
        private static bool _battleSkillSettingWasActive = false;
        private static bool _battleSkillSettingSuppressHeading = false;

        #region Gate Functions

        /// <summary>
        /// Returns true when the root menu highlights "BattleSkill" (Camp root).
        /// Opens a tactical view of battle skills and the assignment screen.
        /// </summary>
        private static bool IsRootBattleSkillMenu()
        {
            return _lastRootMenuItemName == "BattleSkill";
        }

        /// <summary>
        /// Returns true when the root menu highlights a battle/combat skill Enhance item.
        /// "BattleSkillPoint" = battle skill leveling (BP).
        /// "CombatPoint" = combat skill leveling (BP).
        /// </summary>
        private static bool IsEnhanceBattleSkillMenu()
        {
            return _lastRootMenuItemName == "BattleSkillPoint"
                || _lastRootMenuItemName == "CombatPoint";
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Tracks entry/exit of battle/combat skill leveling screens.
        /// These selectors have activeInHierarchy=true permanently, so detection
        /// relies on a "heading pending" flag set when the root menu item changes.
        /// Also polls the combat skill toggle mode (Enhance menu only).
        /// </summary>
        private void UpdateBattleSkillSelector()
        {
            if (_battleSkillOuterSelector == null) return;

            bool isSkillMenu = IsRootBattleSkillMenu() || IsEnhanceBattleSkillMenu();

            if (!isSkillMenu)
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
                    _lastBattleSkillMenuItem = "";
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
                    _lastBattleSkillMenuItem = _lastRootMenuItemName;
                    DebugLogger.LogState($"CampBattleSkill: heading pending for '{_lastRootMenuItemName}'.");
                }
                else if (_lastRootMenuItemName != _lastBattleSkillMenuItem)
                {
                    // Switched between skill sub-items (e.g. CombatPoint → BattleSkillPoint).
                    // Must re-cache inner selectors since each sub-item uses a different one.
                    _battleSkillHeadingPending = true;
                    _battleSkillInnerSelector = null;
                    _battleSkillListBase = null;
                    _combatSkillInnerSelector = null;
                    _combatSkillListBase = null;
                    _combatSkillLastState = UICampCombatSkillSelector.State.Normal;
                    _combatSkillToggleLastIndex = -1;
                    _combatSkillToggleLastIsUse = false;
                    _lastBattleSkillMenuItem = _lastRootMenuItemName;
                    DebugLogger.LogState($"CampBattleSkill: sub-item changed to '{_lastRootMenuItemName}', re-caching.");
                }

                // Poll combat skill toggle mode (Square button) — Enhance menu only.
                if (IsEnhanceBattleSkillMenu() && _combatSkillInnerSelector != null)
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
                _lastBattleSkillMenuItem = "";
            }
        }

        /// <summary>
        /// Polls the UICampBattleSkillSettingSelector (button assignment screen).
        /// Only active when the root menu highlights "BattleSkill" (not Enhance).
        /// </summary>
        private void UpdateBattleSkillSettingSelector()
        {
            if (_battleSkillSettingSelector == null) return;

            // Only poll when the root menu highlights the BattleSkill item.
            if (!IsRootBattleSkillMenu())
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

        #endregion

        #region Hook

        /// <summary>
        /// Postfix for UIBattleSkillInformationPresenter.Set(UIBattleSkillInformationData).
        /// Routes to different announcement builders based on menu context:
        ///   Root (Camp → BattleSkill): detailed tactical readout via BuildRootBattleSkillAnnouncement.
        ///   Enhance (Camp → Enhance → BattleSkillPoint/CombatPoint): upgrade-focused via BuildEnhanceBattleSkillAnnouncement.
        ///   Assignment picker (root only): prefixed with "Assigning to [button]".
        /// </summary>
        private static void BattleSkillInfoPresenter_Set_Postfix(UIBattleSkillInformationData data)
        {
            if (data == null) return;

            bool isRoot = IsRootBattleSkillMenu();
            bool isEnhance = IsEnhanceBattleSkillMenu();
            if (!isRoot && !isEnhance) return;

            bool levelingActive = _battleSkillWasActive && _battleSkillOuterSelector != null;

            bool settingActive = isRoot &&
                _battleSkillSettingWasActive &&
                _battleSkillSettingSelector != null &&
                _battleSkillSettingSelector.gameObject.activeInHierarchy;

            if (!levelingActive && !settingActive) return;

            try
            {
                // --- Leveling screen ---
                if (levelingActive)
                {
                    // On first hook fire: cache inner selectors and announce heading.
                    if (_battleSkillHeadingPending)
                    {
                        _battleSkillHeadingPending = false;
                        CacheBattleSkillInnerSelectors();
                    }

                    if (isRoot)
                    {
                        // Root: detailed tactical readout.
                        string targetStr = ResolveCurrentSkillTargetType();

                        DebugLogger.LogGameValue("CampBattleSkill.root",
                            $"name='{data.battleSkillName}' lv={data.skillLevel}/{data.skillLevelMax} " +
                            $"mp={data.consumeMP} dmgType={data.damageSpeciallyType} target='{targetStr}' " +
                            $"range='{data.rangeDescription}' effect='{data.effectDescription}' " +
                            $"desc='{data.battleSkillDescription}'");

                        string result = BuildRootBattleSkillAnnouncement(data, targetStr);
                        if (!string.IsNullOrEmpty(result))
                            ScreenReader.Say(result);
                    }
                    else
                    {
                        // Enhance: upgrade-focused readout.
                        int pointCost = 0;
                        bool isMax = false;
                        string balance = "";
                        int listSkillLevel = 0;
                        int listSkillLevelMax = 0;

                        if (_combatSkillInnerSelector != null && _combatSkillListBase != null)
                        {
                            var items = _combatSkillInnerSelector.itemDataList;
                            int total = items?.Count ?? 0;
                            int idx   = _combatSkillListBase.currentIndex;
                            if (total > 0 && idx >= 0 && idx < total)
                            {
                                pointCost = items[idx].consumeBP;
                                isMax     = items[idx].isLevelMax;
                                listSkillLevel = items[idx].skillLevel;
                                // Look up max level from game parameter data
                                try
                                {
                                    var csParam = ParameterManager.Instance?.GetCombatSkillParameter(
                                        items[idx].combatSkillID);
                                    if (csParam?.levelupBp != null)
                                        listSkillLevelMax = csParam.levelupBp.Count;
                                }
                                catch (Exception ex)
                                {
                                    DebugLogger.LogState(
                                        $"CombatSkill max level lookup failed: {ex.Message}");
                                }
                            }
                            balance = ReadSkillPointBalance(_combatSkillInnerSelector);
                        }
                        else if (_battleSkillInnerSelector != null && _battleSkillListBase != null)
                        {
                            var items = _battleSkillInnerSelector.itemDataList;
                            int total = items?.Count ?? 0;
                            int idx   = _battleSkillListBase.currentIndex;
                            if (total > 0 && idx >= 0 && idx < total)
                            {
                                pointCost = items[idx].consumeBP;
                                isMax     = items[idx].isLevelMax;
                            }
                            balance = ReadSkillPointBalance(_battleSkillInnerSelector);
                        }

                        bool isCombat = _lastRootMenuItemName == "CombatPoint";

                        DebugLogger.LogGameValue("CampBattleSkill.enhance",
                            $"name='{data.battleSkillName}' lv={data.skillLevel}/{data.skillLevelMax} " +
                            $"listLv={listSkillLevel}/{listSkillLevelMax} " +
                            $"mp={data.consumeMP} cost={pointCost} isMax={isMax} balance='{balance}' " +
                            $"isCombat={isCombat} bonuses={data.bonusDataList?.Count ?? 0}");

                        string result = BuildEnhanceBattleSkillAnnouncement(
                            data, pointCost, isMax, balance, isCombat,
                            listSkillLevel, listSkillLevelMax);
                        if (!string.IsNullOrEmpty(result))
                            ScreenReader.Say(result);
                    }
                    return;
                }

                // --- Setting screen skill picker (root only) ---
                var settingState = _battleSkillSettingSelector.currentState;
                if (settingState != UICampBattleSkillSettingSelector.State.SelectBattleSkill)
                    return;

                // Read the button name from the currently selected equip slot.
                string buttonName = "";
                var slotData = _battleSkillEquipListSel?.GetCurrentData();
                if (slotData != null)
                    buttonName = StripTags(slotData.categoryName ?? "");

                DebugLogger.LogGameValue("CampBattleSkillSetting.picker",
                    $"button='{buttonName}' name='{data.battleSkillName}' " +
                    $"lv={data.skillLevel}/{data.skillLevelMax} mp={data.consumeMP}");

                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(buttonName))
                    sb.Append(Loc.Get("camp_battleskill_setting_assigning", buttonName)).Append(". ");
                AppendSkillInfo(sb, data);

                if (_battleSkillPickerListBase != null)
                {
                    int pickerIdx   = _battleSkillPickerListBase.currentIndex;
                    var pickerList  = _battleSkillPickerListBase.currentDataList;
                    int pickerTotal = pickerList?.Count ?? 0;
                    if (pickerTotal > 0 && pickerIdx >= 0)
                        sb.Append(Loc.Get("camp_battleskill_position", pickerIdx + 1, pickerTotal));
                }

                string result3 = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result3))
                    ScreenReader.Say(result3);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.BattleSkillInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Announcement Builders

        /// <summary>
        /// Builds a detailed tactical readout for the root battle skill menu.
        /// Format: Name. MP: X. Type: Y. Target: Z. Element: A. Range: B. Effect. Description. Level: X of Y.
        /// Fields with empty/zero values are omitted.
        /// </summary>
        private static string BuildRootBattleSkillAnnouncement(
            UIBattleSkillInformationData data, string targetTypeStr)
        {
            var parts = new List<string>();

            string name = data.battleSkillName ?? "";
            if (!string.IsNullOrEmpty(name))
                parts.Add(name);

            // MP cost
            if (data.consumeMP > 0)
                parts.Add(Loc.Get("camp_root_battleskill_mp", data.consumeMP));

            // Damage type
            string typeStr = MapDamageSpeciallyType(data.damageSpeciallyType);
            if (!string.IsNullOrEmpty(typeStr))
                parts.Add(typeStr);

            // Target type (from ParameterManager lookup)
            if (!string.IsNullOrEmpty(targetTypeStr))
                parts.Add(targetTypeStr);

            // Elements
            var elements = data.elementIDList;
            if (elements != null && elements.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < elements.Count; i++)
                {
                    string eName = MapElementName(elements[i]);
                    if (!string.IsNullOrEmpty(eName))
                        names.Add(eName);
                }
                if (names.Count > 0)
                    parts.Add(Loc.Get("camp_root_battleskill_element", string.Join(", ", names)));
            }

            // Range
            string range = data.rangeDescription ?? "";
            if (!string.IsNullOrEmpty(range))
                parts.Add(Loc.Get("camp_root_battleskill_range", range));

            // Effect description
            string effect = (data.effectDescription ?? "").TrimEnd('.', ' ');
            if (!string.IsNullOrEmpty(effect))
                parts.Add(effect);

            // Description
            string desc = (data.battleSkillDescription ?? "").TrimEnd('.', ' ');
            if (!string.IsNullOrEmpty(desc))
                parts.Add(desc);

            // Level
            if (data.skillLevelMax > 0)
                parts.Add(Loc.Get("camp_root_battleskill_level", data.skillLevel, data.skillLevelMax));

            return parts.Count > 0 ? string.Join(". ", parts) + "." : "";
        }

        /// <summary>
        /// Builds an upgrade-focused readout for the Enhance battle/combat skill menu.
        /// Battle skills: Name. MP. Level. BP. Effect. Description. Upgrade: bonuses.
        /// Combat skills: Name. Level. BP. Description. Upgrade: effect.
        /// Level and upgrade info use list item data for combat skills (info panel is 0/0).
        /// At max level, upgrade section is omitted and level shows ", max".
        /// </summary>
        private static string BuildEnhanceBattleSkillAnnouncement(
            UIBattleSkillInformationData data, int pointCost, bool isMaxLevel,
            string balance, bool isCombatSkill,
            int listSkillLevel, int listSkillLevelMax)
        {
            var parts = new List<string>();

            string name = data.battleSkillName ?? "";
            if (!string.IsNullOrEmpty(name))
                parts.Add(name);

            // MP cost (right after name — zero for combat skills, so naturally skipped)
            if (data.consumeMP > 0)
                parts.Add(Loc.Get("camp_enhance_mp", data.consumeMP));

            // Level — use list item data when available (combat skills have 0/0 on info panel)
            int level = listSkillLevelMax > 0 ? listSkillLevel : data.skillLevel;
            int levelMax = listSkillLevelMax > 0 ? listSkillLevelMax : data.skillLevelMax;
            if (levelMax > 0)
            {
                if (isMaxLevel)
                    parts.Add(Loc.Get("camp_enhance_level_max", level, levelMax));
                else
                    parts.Add(Loc.Get("camp_enhance_level", level, levelMax));
            }

            // Point balance and cost (BP for both battle and combat skills)
            if (pointCost > 0 && !string.IsNullOrEmpty(balance))
            {
                parts.Add(Loc.Get("camp_enhance_bp", balance, pointCost));
            }

            string effect = (data.effectDescription ?? "").TrimEnd('.', ' ');
            string desc = (data.battleSkillDescription ?? "").TrimEnd('.', ' ');

            // Avoid reading identical text twice (e.g. Body Control: both are "Reduces daze time")
            bool effectSameAsDesc = !string.IsNullOrEmpty(effect) && !string.IsNullOrEmpty(desc)
                && string.Equals(effect, desc, StringComparison.OrdinalIgnoreCase);

            if (isCombatSkill)
            {
                // Combat skills: Description first, then effect as "Upgrade:" label
                if (!string.IsNullOrEmpty(desc))
                    parts.Add(desc);

                if (!isMaxLevel && !string.IsNullOrEmpty(effect) && !effectSameAsDesc)
                    parts.Add(Loc.Get("camp_enhance_next", effect));
            }
            else
            {
                // Battle skills: Effect, Description, then bonus list as "Upgrade:"
                if (!string.IsNullOrEmpty(effect))
                    parts.Add(effect);

                if (!string.IsNullOrEmpty(desc) && !effectSameAsDesc)
                    parts.Add(desc);

                if (!isMaxLevel)
                {
                    string bonusStr = BuildBonusList(data.bonusDataList);
                    if (!string.IsNullOrEmpty(bonusStr))
                        parts.Add(Loc.Get("camp_enhance_next", bonusStr));
                }
            }

            return parts.Count > 0 ? string.Join(". ", parts) + "." : "";
        }

        #endregion

        #region Cache and Toggle

        /// <summary>
        /// Caches the appropriate inner selector and announces the heading.
        /// Called from the hook on the first fire after root menu item changes.
        /// Uses _lastRootMenuItemName to determine which inner selector to cache
        /// and which heading to announce (root vs enhance, battle vs combat).
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

                ScreenReader.Say(Loc.Get("camp_enhance_combatskill_screen"));
                DebugLogger.LogState("CampCombatSkill: cached (combat skills via Enhance).");
            }
            else
            {
                _battleSkillInnerSelector = _battleSkillOuterSelector.battleSkillSelector;
                _battleSkillListBase = _battleSkillInnerSelector?.TryCast<UIListSelectorBase>();
                _combatSkillInnerSelector = null;
                _combatSkillListBase = null;

                string headingKey = IsRootBattleSkillMenu()
                    ? "camp_root_battleskill_screen"
                    : "camp_enhance_battleskill_screen";

                ScreenReader.Say(Loc.Get(headingKey));
                DebugLogger.LogState($"CampBattleSkill: cached (battle skills) for '{_lastRootMenuItemName}'.");
            }
        }

        /// <summary>
        /// Polls the combat skill selector's state for toggle mode changes (Square button).
        /// In ChangeOnOff mode, tracks index changes and announces each skill's active/inactive
        /// status via isUse from UICampCombatSkillListItemData.
        /// Called from UpdateBattleSkillSelector when combat skill selector is active (Enhance only).
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
                    ScreenReader.Say(Loc.Get("camp_enhance_combatskill_screen"));
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

        #endregion

        #region Helper Methods

        /// <summary>
        /// Resolves the current battle skill's target type from ParameterManager.
        /// Looks up the battleSkillID from the inner selector's current item and
        /// returns a localized target type string, or empty if unavailable.
        /// </summary>
        private static string ResolveCurrentSkillTargetType()
        {
            try
            {
                if (_battleSkillInnerSelector == null || _battleSkillListBase == null)
                    return "";

                var items = _battleSkillInnerSelector.itemDataList;
                int idx = _battleSkillListBase.currentIndex;
                if (items == null || idx < 0 || idx >= items.Count)
                    return "";

                var skillId = items[idx].battleSkillID;
                var pm = ParameterManager.Instance;
                if (pm == null) return "";

                var bsParam = pm.GetBattleSkillParameter(skillId);
                if (bsParam == null) return "";

                return MapTargetType(bsParam.targetType);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ResolveCurrentSkillTargetType failed: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Maps DamageSpeciallyType enum to a localized type string.
        /// </summary>
        private static string MapDamageSpeciallyType(DamageSpeciallyType type)
        {
            return type switch
            {
                DamageSpeciallyType.DAMAGE => Loc.Get("camp_root_battleskill_type_damage"),
                DamageSpeciallyType.DURABILITY => Loc.Get("camp_root_battleskill_type_durability"),
                DamageSpeciallyType.DAMAGE_DURABILITY => Loc.Get("camp_root_battleskill_type_damage_durability"),
                DamageSpeciallyType.SUPPORT => Loc.Get("camp_root_battleskill_type_support"),
                _ => ""
            };
        }

        /// <summary>
        /// Maps TargetType enum to a localized target string.
        /// </summary>
        private static string MapTargetType(TargetType type)
        {
            return type switch
            {
                TargetType.ENEMY => Loc.Get("camp_root_battleskill_target_enemy"),
                TargetType.ENEMY_ALL => Loc.Get("camp_root_battleskill_target_enemy_all"),
                TargetType.PLAYER => Loc.Get("camp_root_battleskill_target_player"),
                TargetType.PLAYER_ALL => Loc.Get("camp_root_battleskill_target_player_all"),
                TargetType.SELF => Loc.Get("camp_root_battleskill_target_self"),
                _ => ""
            };
        }

        /// <summary>
        /// Maps ElementID enum to a localized element name.
        /// Returns empty for INVALID, NOTHING, and MAX.
        /// </summary>
        private static string MapElementName(ElementID id)
        {
            return id switch
            {
                ElementID.EARTH => Loc.Get("element_earth"),
                ElementID.WATER => Loc.Get("element_water"),
                ElementID.FIRE => Loc.Get("element_fire"),
                ElementID.WIND => Loc.Get("element_wind"),
                ElementID.LIGHTNING => Loc.Get("element_lightning"),
                ElementID.STAR => Loc.Get("element_star"),
                ElementID.NEGATIVE => Loc.Get("element_negative"),
                ElementID.LIGHT => Loc.Get("element_light"),
                ElementID.DARK => Loc.Get("element_dark"),
                _ => ""
            };
        }

        /// <summary>
        /// Maps BattleSkillEnhanceBonusType to a localized bonus name.
        /// </summary>
        private static string MapBonusType(BattleSkillEnhanceBonusType type)
        {
            return type switch
            {
                BattleSkillEnhanceBonusType.DAMAGE_UP => Loc.Get("camp_enhance_bonus_damage_up"),
                BattleSkillEnhanceBonusType.HIT_UP => Loc.Get("camp_enhance_bonus_hit_up"),
                BattleSkillEnhanceBonusType.CRITICAL_UP => Loc.Get("camp_enhance_bonus_critical_up"),
                BattleSkillEnhanceBonusType.RANGE_EXPANSION => Loc.Get("camp_enhance_bonus_range_expansion"),
                BattleSkillEnhanceBonusType.HEAL_UP => Loc.Get("camp_enhance_bonus_heal_up"),
                BattleSkillEnhanceBonusType.GRANT_UP => Loc.Get("camp_enhance_bonus_grant_up"),
                BattleSkillEnhanceBonusType.ADD_ATTRACTING => Loc.Get("camp_enhance_bonus_add_attracting"),
                BattleSkillEnhanceBonusType.STOP_TIME_UP => Loc.Get("camp_enhance_bonus_stop_time_up"),
                BattleSkillEnhanceBonusType.CHANGE_EFFECT => Loc.Get("camp_enhance_bonus_change_effect"),
                BattleSkillEnhanceBonusType.ADD_PENETRATION => Loc.Get("camp_enhance_bonus_add_penetration"),
                BattleSkillEnhanceBonusType.IGNORE_DEFENCE => Loc.Get("camp_enhance_bonus_ignore_defence"),
                BattleSkillEnhanceBonusType.ADD_WEAKNESS_ATTACK => Loc.Get("camp_enhance_bonus_weakness_attack"),
                BattleSkillEnhanceBonusType.RECOVER_ABNORMAL => Loc.Get("camp_enhance_bonus_recover_abnormal"),
                _ => ""
            };
        }

        /// <summary>
        /// Builds a comma-separated list of upgrade bonuses from bonusDataList.
        /// Only includes bonuses where isUp is true (improvements at next level).
        /// Uses bonusName if available, otherwise maps bonusType to a Loc string.
        /// </summary>
        private static string BuildBonusList(
            Il2CppSystem.Collections.Generic.List<UIEnhanceBonusData> bonusDataList)
        {
            if (bonusDataList == null || bonusDataList.Count == 0)
                return "";

            var bonusNames = new List<string>();
            for (int i = 0; i < bonusDataList.Count; i++)
            {
                try
                {
                    var bonus = bonusDataList[i];
                    if (bonus == null || !bonus.isUp) continue;

                    string bName = bonus.bonusName ?? "";
                    if (string.IsNullOrEmpty(bName))
                        bName = MapBonusType(bonus.bonusType);

                    if (!string.IsNullOrEmpty(bName))
                        bonusNames.Add(bName);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"BuildBonusList entry {i} failed: {ex.Message}");
                }
            }

            return bonusNames.Count > 0 ? string.Join(", ", bonusNames) : "";
        }

        /// <summary>
        /// Appends battle skill details (name, level, MP cost, description, effect)
        /// to a StringBuilder. Used by the assignment screen skill picker only.
        /// </summary>
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
        /// Works for UISelectBattleSkillSelector (BP) and UICampCombatSkillSelector (BP).
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

        #endregion
    }
}
