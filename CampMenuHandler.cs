using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces camp menu navigation to the screen reader.
    ///
    /// Patches applied:
    ///   UICampWindow.Open — announces "Camp menu." and caches selectors for polling.
    ///   UIItemInformationPresenter.Set — announces equip item details (name, description,
    ///   stats, factor) when the equip item list is active.
    ///   UIBattleSkillInformationPresenter.Set — announces battle skill details (name,
    ///   level, MP cost, description) when the battle skill leveling or assignment screen
    ///   is active.
    ///   UICampStatusParameterPresenter.Setup — captures character stat data (attack, defence,
    ///   magic, hit, dodge, critical, str, con, dex, agl, int, luc, stamina, guts) for
    ///   the status screen announcement.
    ///
    /// Root menu type: UICampMenuSelector (field menuSelector on UICampWindow).
    /// Item sub-screen: UICampItemSelector (field itemSelector on UICampWindow).
    ///   Item data type: UIItemListItemData — itemName, itemCount, itemDescription.
    ///
    /// Status sub-screen: UICampStatusSelector (field statusSelector on UICampWindow).
    ///   Detection: activeInHierarchy is always true, so we gate on root menu hidden +
    ///   last highlighted root menu item == "Status".
    ///   currentIndex — which party member tab is selected (0-based).
    ///   statusLevelCacheData (UICampStatusLevelData) — level, hp, maxHp, mp, maxMp, exp.
    ///   Stats: UICampStatusParameterData captured by Setup hook on parameter presenter.
    ///   Character name: statusPresenter.characterTabPresenter.itemTabDataList[index]
    ///     cast to UICharacterTabItemData → playerID → ParameterManager.GetCharacterFirstName.
    ///   Approach: polling currentIndex — navigation is native-only, same pattern as root menu.
    ///
    /// Equip sub-screen: UICampEquipSelector (field equipSelector on UICampWindow).
    ///   Slot list: UIEquipListSelector (equipListSelector) — polled.
    ///   Item list: UICampEquipItemListSelector (itemListSelector) — hook-driven.
    ///   Item detail hook: UIItemInformationPresenter.Set(UIItemInformationData).
    ///
    /// Battle skill sub-screen: UICampBattleSkillSelector (battleSkillSelector on UICampWindow).
    ///   Inner list: UISelectBattleSkillSelector (battleSkillSelector field on outer).
    ///   Extends UICharacterTabListSelectorBase → UIHelpListSelectorBase → UIListSelectorBase.
    ///   Navigation hook: UIBattleSkillInformationPresenter.Set(UIBattleSkillInformationData).
    ///   Data: battleSkillName, battleSkillDescription, skillLevel, skillLevelMax, consumeMP,
    ///   effectDescription. Position: currentIndex on UIListSelectorBase, count from itemDataList.
    ///
    /// Battle skill assignment sub-screen: UICampBattleSkillSettingSelector (battleSkillSettingSelector
    ///   on UICampWindow). Two states: Equip (browsing button slots, polled), SelectBattleSkill
    ///   (picking a skill, hook announces with "Assigning to [button]:" prefix).
    ///   Slot list: UICampBattleSkillEquipListSelector (equipListSelector) — polled.
    ///   Skill picker: UICampBattleSkillListSelector (battleSkillListSelector) — hook-driven.
    ///
    /// Navigation approach — polling:
    ///   Navigation is driven from native C++ code; no managed Harmony hook fires.
    ///   Update() polls currentIndex each frame. When it changes, the focused item
    ///   is announced. Re-announces when the selector becomes active again.
    /// </summary>
    public class CampMenuHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Static so the Harmony postfix (static method) can write and Update() can read.

        // Root menu
        private static UICampMenuSelector _menuSelector = null;
        private static int _lastIndex = -1;
        private static bool _wasActive = false;

        /// <summary>
        /// True while the camp menu window is open. Used by NavigationHandler
        /// to prevent gamepad nav overlay from activating during camp.
        /// </summary>
        public static bool IsCampOpen { get; private set; }

        /// <summary>Cached UICampWindow instance for detecting camp closure.</summary>
        private static UICampWindow _campWindow = null;

        // Item sub-screen
        // UICampItemSelector wraps UICampItemListSelector (field itemListSelector).
        // currentIndex/currentDataList live on UIListSelectorBase — need a cast.
        private static UICampItemSelector _itemSelector = null;
        private static UIListSelectorBase _itemListSelectorBase = null;
        private static int _itemLastIndex = -1;
        private static bool _itemWasActive = false;
        // When the item selector is already active on camp open (stale from a previous
        // session), suppress the "Items." heading and don't reset _itemLastIndex to -1.
        private static bool _itemSuppressHeading = false;

        // Equip sub-screen
        // UICampEquipSelector wraps:
        //   equipListSelector (UIEquipListSelector → UIHelpListSelectorBase → UIListSelectorBase)
        //     — the list of equipment slots showing what is currently equipped
        //   itemListSelector (UICampEquipItemListSelector → UIListSelectorBase)
        //     — the list of items that can be equipped in the selected slot
        // Slot list: polled. Item list: driven by UIItemInformationPresenter.Set hook.
        private static UICampEquipSelector _equipSelector = null;
        private static bool _equipWasActive = false;
        private static bool _equipSuppressHeading = false;

        // Equip slot list
        private static UIListSelectorBase _equipSlotListBase = null;
        private static int _equipSlotLastIndex = -1;
        private static bool _equipSlotWasActive = false;

        // Equip item list — used by the hook to read currentIndex and total count.
        private static UIListSelectorBase _equipItemListBase = null;
        private static bool _equipItemListActive = false;

        // Battle skill LEVELING sub-screen (battleSkillSelector on UICampWindow)
        // UICampBattleSkillSelector wraps UISelectBattleSkillSelector (battleSkillSelector field).
        // UISelectBattleSkillSelector → UICharacterTabListSelectorBase → UIHelpListSelectorBase
        //   → UIListSelectorBase (cast needed for currentIndex).
        // itemDataList (List<UICampBattleSkillListItemData>) gives the typed skill list and count.
        // Navigation announcements driven by UIBattleSkillInformationPresenter.Set hook.
        private static UICampBattleSkillSelector _battleSkillOuterSelector = null;
        private static UISelectBattleSkillSelector _battleSkillInnerSelector = null;
        private static UIListSelectorBase _battleSkillListBase = null;
        private static bool _battleSkillWasActive = false;
        private static bool _battleSkillSuppressHeading = false;

        // Status sub-screen (statusSelector on UICampWindow)
        // Detection: hook-driven. Both activeInHierarchy and root-menu-hidden approaches
        // failed (both stay true). Instead, UICampStatusSelector.UpdatePresenter hook fires
        // when the status screen opens or character tab changes — use it as the trigger.
        // Data captured from hooks that fire just before UpdatePresenter:
        //   UpdateName → _statusPlayerName
        //   LevelPresenter.Setup → _statusLevelData
        //   StatusParamPresenter.Setup → _statusParamData
        private static UICampStatusSelector _statusSelector = null;
        private static bool _statusScreenOpen = false;
        private static int _statusLastIndex = -1;
        private static UICampStatusParameterData _statusParamData = null;
        private static UICampStatusLevelData _statusLevelData = null;
        private static string _statusPlayerName = "";
        // Tracks which root menu item is highlighted (for sub-screen detection).
        private static string _lastRootMenuItemName = "";

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

        // Formation sub-screen (formationSelector on UICampWindow)
        // UICampFormationSelector extends UIHelpListSelectorBase → UIListSelectorBase.
        // Info: UICampFormationInformationPresenter.Set hook fires on every formation change.
        // Announces formation name, effect description, and position.
        private static UICampFormationSelector _formationSelector = null;
        private static bool _formationWasActive = false;
        private static bool _formationSuppressHeading = false;

        // Skills sub-screen — field/IC skills (skillSelector on UICampWindow)
        // UICampSkillSelector extends UICharacterTabListSelectorBase → UIHelpListSelectorBase
        //   → UIListSelectorBase. Has states: Skill, SpecialSkill, Learning.
        // Info: UISkillInformationPresenter.Set hook fires on every skill navigation.
        // Announces skill name, description, level, and position.
        private static UICampSkillSelector _skillSelector = null;
        private static bool _skillWasActive = false;
        private static bool _skillSuppressHeading = false;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the camp menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UICampMenuItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampMenuSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampItemSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampItemListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEquipSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEquipItemListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusParameterData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICharacterStatusItemInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISelectBattleSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillSettingSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillEquipListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillSettingEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusLevelData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusParameterPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusLevelPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CharacterParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstPlayerParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemTabPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICharacterTabItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFormationSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFormationListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFormationInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSkillListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillInformationData).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UICampWindow),
                        nameof(UICampWindow.Open)),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(CampWindow_Open_Postfix))
                );

                // UIItemInformationPresenter.Set has two overloads — patch the one that
                // takes UIItemInformationData (fires on every equip item navigation).
                harmony.Patch(
                    AccessTools.Method(typeof(UIItemInformationPresenter), "Set",
                        new Type[] {
                            typeof(UIItemInformationData),
                            typeof(UICharacterStatusItemInformationData),
                            typeof(bool)
                        }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(ItemInfoPresenter_Set_Postfix))
                );

                // UIBattleSkillInformationPresenter.Set fires on every skill navigation
                // in the battle skill screen (CallerCount 4 — hookable from managed code).
                harmony.Patch(
                    AccessTools.Method(typeof(UIBattleSkillInformationPresenter), "Set",
                        new Type[] { typeof(UIBattleSkillInformationData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(BattleSkillInfoPresenter_Set_Postfix))
                );

                // UICampStatusParameterPresenter.Setup fires when the status screen
                // parameter panel updates (CallerCount 12 — hookable). Captures all stats.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusParameterPresenter), "Setup",
                        new Type[] { typeof(UICampStatusParameterData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(StatusParamPresenter_Setup_Postfix))
                );

                // --- STATUS SCREEN HOOKS (hook-driven detection) ---
                // Both activeInHierarchy and root-menu-hidden detection failed for the status
                // screen. These hooks fire when the status screen opens or character changes.
                // UpdatePresenter fires LAST and triggers the announcement.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusSelector), "UpdatePresenter",
                        new Type[] { typeof(int), typeof(int), typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusSelector_UpdatePresenter))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusSelector), "UpdateStatusLevel",
                        new Type[] { typeof(CharacterParameter) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusSelector_UpdateStatusLevel))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusSelector), "UpdateName",
                        new Type[] { typeof(PlayerID), typeof(ConstPlayerParameter) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusSelector_UpdateName))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusLevelPresenter), "Setup",
                        new Type[] { typeof(UICampStatusLevelData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusLevelPresenter_Setup))
                );

                // UICampFormationInformationPresenter.Set fires when the formation info
                // panel updates (CallerCount 1 — hookable from managed code).
                harmony.Patch(
                    AccessTools.Method(typeof(UICampFormationInformationPresenter), "Set"),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(FormationInfoPresenter_Set_Postfix))
                );

                // UISkillInformationPresenter.Set fires when the skill info panel updates
                // in the skills sub-screen (CallerCount 1 — hookable from managed code).
                harmony.Patch(
                    AccessTools.Method(typeof(UISkillInformationPresenter), "Set",
                        new Type[] { typeof(UISkillInformationData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(SkillInfoPresenter_Set_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("CampMenuHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"CampMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Update (Polling)

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Polls cached selectors for index changes and announces the focused item.
        /// </summary>
        public void Update()
        {
            // Detect camp window closure — clear IsCampOpen when the window is closed.
            // NOTE: gameObject.activeInHierarchy stays true even after camp closes,
            // so we use WindowComponent.IsOpened which properly tracks open/close state.
            if (IsCampOpen && _campWindow != null)
            {
                try
                {
                    if (!_campWindow.IsOpened)
                    {
                        IsCampOpen = false;
                        _campWindow = null;
                        DebugLogger.LogState("CampMenu: window closed (IsCampOpen=false via IsOpened).");
                    }
                }
                catch
                {
                    IsCampOpen = false;
                    _campWindow = null;
                }
            }

            UpdateRootMenu();
            UpdateItemSelector();
            UpdateStatusSelector();
            UpdateEquipSelector();
            UpdateBattleSkillSelector();
            UpdateBattleSkillSettingSelector();
            UpdateFormationSelector();
            UpdateSkillSelector();
        }

        /// <summary>
        /// Polls the root UICampMenuSelector and announces the focused command.
        /// Re-announces when returning from a sub-screen.
        /// </summary>
        private void UpdateRootMenu()
        {
            if (_menuSelector == null) return;

            try
            {
                bool isActive = _menuSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_wasActive)
                    {
                        _wasActive = false;
                        DebugLogger.LogState("CampMenu: selector hidden.");
                    }
                    return;
                }

                // Selector just became visible: camp opened or returned from sub-screen.
                if (!_wasActive)
                {
                    _wasActive = true;
                    _lastIndex = -1; // Force announcement of current item.
                    DebugLogger.LogState("CampMenu: selector visible.");
                }

                int idx = _menuSelector.currentIndex;
                if (idx == _lastIndex) return;
                _lastIndex = idx;

                // Root menu index changed — user returned from a sub-screen or navigated.
                // Reset status screen state so next open announces the heading again.
                if (_statusScreenOpen)
                {
                    _statusScreenOpen = false;
                    _statusLastIndex = -1;
                    _statusParamData = null;
                    _statusLevelData = null;
                    _statusPlayerName = "";
                    DebugLogger.LogState("CampStatus: closed (root menu index changed).");
                }

                var list = _menuSelector.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UICampMenuItemData>();
                if (item == null) return;

                string name = item.menuItem.ToString();
                _lastRootMenuItemName = name;
                bool available = item.canDecisioned;

                DebugLogger.LogGameValue("CampMenu.item",
                    $"{name} available={available} ({idx + 1}/{total})");

                if (available)
                    ScreenReader.Say(Loc.Get("camp_menu_item", name, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("camp_menu_item_unavailable", name, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateRootMenu: {ex.Message}");
                _menuSelector = null;
                _wasActive = false;
                _lastIndex = -1;
                IsCampOpen = false;
            }
        }

        /// <summary>
        /// Polls the UICampItemSelector and announces item name, quantity, and description.
        /// UICampItemSelector wraps a UICampItemListSelector (itemListSelector field).
        /// currentIndex and currentDataList are on UIListSelectorBase — cast required.
        /// Announces "Items." when genuinely entering the sub-screen.
        /// If the selector was already active on camp open (stale), the heading and first
        /// item are suppressed; subsequent navigation announces normally.
        /// </summary>
        private void UpdateItemSelector()
        {
            if (_itemSelector == null) return;

            // Only poll when the root menu highlights "Item".
            // All sub-screens have activeInHierarchy=True permanently, so we use the
            // root menu item name as the only reliable signal for which screen is current.
            if (_lastRootMenuItemName != "Item")
            {
                // Don't reset _itemWasActive or _itemLastIndex here.
                // The item list is permanently active and retains its index,
                // so resetting would cause stale announcements when the root
                // menu cursor returns to "Item" during normal navigation.
                return;
            }

            try
            {
                bool isActive = _itemSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_itemWasActive)
                    {
                        _itemWasActive = false;
                        _itemListSelectorBase = null;
                        DebugLogger.LogState("CampItem: selector hidden.");
                    }
                    return;
                }

                if (!_itemWasActive)
                {
                    _itemWasActive = true;

                    // Cache the inner list selector if not already pre-cached by the postfix.
                    if (_itemListSelectorBase == null)
                    {
                        var inner = _itemSelector.itemListSelector;
                        _itemListSelectorBase = inner?.TryCast<UIListSelectorBase>();

                        if (_itemListSelectorBase != null)
                            DebugLogger.LogState("CampItem: inner list selector cached.");
                        else
                            MelonLogger.Warning("[CAMP] itemListSelector cast to UIListSelectorBase failed.");
                    }

                    if (!_itemSuppressHeading)
                    {
                        // Genuine entry — announce heading and reset index to force
                        // first-item announcement next frame.
                        _itemLastIndex = -1;
                        ScreenReader.Say(Loc.Get("camp_item_screen"));
                        DebugLogger.LogState("CampItem: selector visible.");
                    }
                    else
                    {
                        // Stale on camp re-open — suppress heading and keep pre-seeded
                        // _itemLastIndex so the stale item is not re-announced.
                        _itemSuppressHeading = false;
                        DebugLogger.LogState("CampItem: stale open — heading suppressed.");
                    }

                    return;
                }

                if (_itemListSelectorBase == null) return;

                int idx = _itemListSelectorBase.currentIndex;
                if (idx == _itemLastIndex) return;
                _itemLastIndex = idx;

                var list = _itemListSelectorBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIItemListItemData>();
                if (item == null) return;

                string name = item.itemName ?? "";
                int count = item.itemCount;
                string description = item.itemDescription ?? "";

                DebugLogger.LogGameValue("CampItem.item",
                    $"{name} x{count} ({idx + 1}/{total}): {description}");

                if (string.IsNullOrEmpty(description))
                    ScreenReader.Say(Loc.Get("camp_item_entry_nodesc", name, count, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("camp_item_entry", name, count, description, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateItemSelector: {ex.Message}");
                _itemSelector = null;
                _itemListSelectorBase = null;
                _itemWasActive = false;
                _itemLastIndex = -1;
                _itemSuppressHeading = false;
            }
        }

        /// <summary>
        /// Polls the UICampStatusSelector and announces the focused party member.
        /// Detection: the status selector's activeInHierarchy is always true (stale),
        /// so we detect the status screen by: root menu is hidden AND the last highlighted
        /// root menu item was "Status". This avoids false announcements on camp open.
        ///
        /// Announces "Status." when the screen opens, then full character data (name, level,
        /// HP, MP, EXP, all combat stats, all base attributes) on open and character tab change.
        /// Stats come from UICampStatusParameterPresenter.Setup hook (fires before our poll).
        /// </summary>
        /// <summary>
        /// No longer polls for status detection — hook-driven via UpdatePresenter.
        /// This method is kept only to reset state when camp closes (selector goes null).
        /// </summary>
        private void UpdateStatusSelector()
        {
            // Nothing to poll — status announcements are fully hook-driven.
            // Reset is handled by CampWindow_Open_Postfix and root menu index changes.
        }

        /// <summary>
        /// Builds and announces character status from hook-captured data.
        /// Called from the UpdatePresenter hook (fires last in the hook chain).
        /// Data sources: _statusPlayerName (UpdateName hook), _statusLevelData
        /// (LevelPresenter.Setup hook), _statusParamData (StatusParamPresenter.Setup hook).
        /// </summary>
        private static void AnnounceStatusCharacter(int index, int total)
        {
            try
            {
                var sb = new StringBuilder();

                // Character name (captured by UpdateName hook).
                if (!string.IsNullOrEmpty(_statusPlayerName))
                    sb.Append(_statusPlayerName + ". ");

                // Level, HP, MP, EXP (captured by LevelPresenter.Setup hook).
                if (_statusLevelData != null)
                {
                    sb.Append(Loc.Get("camp_status_level_hp_mp",
                        _statusLevelData.level,
                        _statusLevelData.hp, _statusLevelData.maxHp,
                        _statusLevelData.mp, _statusLevelData.maxMp));
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_exp",
                        _statusLevelData.exp, _statusLevelData.nextExp));
                    sb.Append(" ");
                }

                // Combat stats and base attributes (captured by StatusParamPresenter.Setup hook).
                if (_statusParamData != null)
                {
                    sb.Append(Loc.Get("camp_status_combat",
                        _statusParamData.attack, _statusParamData.defence,
                        _statusParamData.magic, _statusParamData.hit,
                        _statusParamData.dodge, _statusParamData.critical));
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_attributes",
                        _statusParamData.str, _statusParamData.con,
                        _statusParamData.dex, _statusParamData.agl,
                        _statusParamData.intelligence, _statusParamData.luc));
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_stamina_guts",
                        _statusParamData.stm, _statusParamData.guts));
                    sb.Append(" ");
                }

                // Position in party.
                sb.Append(Loc.Get("camp_status_position", index + 1, total));

                DebugLogger.LogGameValue("CampStatus.char",
                    $"name='{_statusPlayerName}' idx={index} ({index + 1}/{total}) " +
                    $"levelData={(_statusLevelData != null ? "yes" : "null")} " +
                    $"paramData={(_statusParamData != null ? "yes" : "null")}");

                ScreenReader.Say(sb.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.AnnounceStatusCharacter: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the UICampEquipSelector and its sub-selectors.
        /// Announces "Equipment." when the screen opens.
        /// The slot list (what is currently equipped) is polled here.
        /// The item list (items to choose from) is announced via the
        /// UIItemInformationPresenter.Set hook — this method only tracks its active state.
        /// </summary>
        private void UpdateEquipSelector()
        {
            if (_equipSelector == null) return;

            // Only poll when the root menu highlights "Equip".
            if (_lastRootMenuItemName != "Equip")
            {
                // Don't reset _equipWasActive or slot/item state here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "Equip" during normal navigation.
                return;
            }

            try
            {
                bool isActive = _equipSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_equipWasActive)
                    {
                        _equipWasActive = false;
                        _equipSlotWasActive = false;
                        _equipItemListActive = false;
                        _equipSlotListBase = null;
                        _equipItemListBase = null;
                        DebugLogger.LogState("CampEquip: selector hidden.");
                    }
                    return;
                }

                if (!_equipWasActive)
                {
                    _equipWasActive = true;

                    // Cache sub-selectors.
                    var slotSel = _equipSelector.equipListSelector;
                    _equipSlotListBase = slotSel?.TryCast<UIListSelectorBase>();

                    var itemSel = _equipSelector.itemListSelector;
                    _equipItemListBase = itemSel?.TryCast<UIListSelectorBase>();

                    if (!_equipSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_equip_screen"));
                        _equipSlotLastIndex = -1;
                        DebugLogger.LogState("CampEquip: selector visible.");
                    }
                    else
                    {
                        _equipSuppressHeading = false;
                        DebugLogger.LogState("CampEquip: stale open — heading suppressed.");
                    }
                }

                // Keep item-list active flag updated every frame for hook gating.
                var itemListSel = _equipSelector.itemListSelector;
                _equipItemListActive = itemListSel != null && itemListSel.gameObject.activeInHierarchy;

                // When the item list is open, item announcements are handled by the hook.
                // Only poll the slot list while the item list is not shown.
                if (!_equipItemListActive)
                    UpdateEquipSlotList();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateEquipSelector: {ex.Message}");
                _equipSelector = null;
                _equipWasActive = false;
                _equipSuppressHeading = false;
                _equipSlotListBase = null;
                _equipSlotLastIndex = -1;
                _equipSlotWasActive = false;
                _equipItemListBase = null;
                _equipItemListActive = false;
            }
        }

        /// <summary>
        /// Polls the UIEquipListSelector (the equipment slot list showing what is
        /// currently equipped) and announces the highlighted slot on change.
        /// Each slot's data holds the name of the item currently equipped there.
        /// Called by UpdateEquipSelector only while the item sub-list is not open.
        /// </summary>
        private void UpdateEquipSlotList()
        {
            if (_equipSlotListBase == null) return;

            try
            {
                var slotSel = _equipSelector?.equipListSelector;
                if (slotSel == null) return;

                bool isActive = slotSel.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_equipSlotWasActive)
                    {
                        _equipSlotWasActive = false;
                        _equipSlotLastIndex = -1;
                        DebugLogger.LogState("CampEquip: slot list hidden.");
                    }
                    return;
                }

                if (!_equipSlotWasActive)
                {
                    _equipSlotWasActive = true;
                    _equipSlotLastIndex = -1; // Force re-announce on entry.
                    DebugLogger.LogState("CampEquip: slot list visible.");
                }

                int idx = _equipSlotListBase.currentIndex;
                if (idx == _equipSlotLastIndex) return;
                _equipSlotLastIndex = idx;

                var list = _equipSlotListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIEquipListItemData>();
                if (item == null) return;

                string name = item.itemName ?? "";
                bool available = item.canDecision;

                DebugLogger.LogGameValue("CampEquip.slot",
                    $"name='{name}' available={available} ({idx + 1}/{total})");

                if (string.IsNullOrEmpty(name))
                    ScreenReader.Say(Loc.Get("camp_equip_slot_empty", idx + 1, total));
                else if (available)
                    ScreenReader.Say(Loc.Get("camp_equip_slot", name, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("camp_equip_slot_unavailable", name, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateEquipSlotList: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the UICampBattleSkillSelector (outer container) for active state changes.
        /// Announces "Battle skills." when the screen opens.
        /// Skill-level navigation announcements are handled entirely by the
        /// UIBattleSkillInformationPresenter.Set hook — this method only tracks
        /// open/close state and caches the inner selector references.
        ///
        /// Inner selector: UISelectBattleSkillSelector (battleSkillSelector field on outer).
        /// Cast to UIListSelectorBase gives currentIndex for position-in-list.
        /// itemDataList.Count gives total skills for the current character tab.
        /// </summary>
        private void UpdateBattleSkillSelector()
        {
            if (_battleSkillOuterSelector == null) return;

            // Only poll when the root menu highlights "BattleSkill".
            if (_lastRootMenuItemName != "BattleSkill")
            {
                // Don't reset _battleSkillWasActive here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "BattleSkill" during normal navigation.
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
                        DebugLogger.LogState("CampBattleSkill: selector hidden.");
                    }
                    return;
                }

                if (!_battleSkillWasActive)
                {
                    _battleSkillWasActive = true;

                    // Cache the inner selector and its UIListSelectorBase cast.
                    _battleSkillInnerSelector = _battleSkillOuterSelector.battleSkillSelector;
                    _battleSkillListBase = _battleSkillInnerSelector?.TryCast<UIListSelectorBase>();

                    if (_battleSkillListBase == null)
                        MelonLogger.Warning("[CAMP] battleSkill inner selector cast to UIListSelectorBase failed.");

                    if (!_battleSkillSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_battleskill_screen"));
                        DebugLogger.LogState("CampBattleSkill: selector visible.");
                    }
                    else
                    {
                        _battleSkillSuppressHeading = false;
                        DebugLogger.LogState("CampBattleSkill: stale open — heading suppressed.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateBattleSkillSelector: {ex.Message}");
                _battleSkillOuterSelector = null;
                _battleSkillInnerSelector = null;
                _battleSkillListBase = null;
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

            // Only poll when the root menu highlights "BattleSkill" (same parent screen).
            if (_lastRootMenuItemName != "BattleSkill")
            {
                // Don't reset _battleSkillSettingWasActive or equip state here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "BattleSkill" during normal navigation.
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

                string button    = item.categoryName    ?? "";
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
        /// Polls the UICampFormationSelector for active state changes.
        /// Announces "Formation." when the screen opens.
        /// Formation detail announcements (name, effect, position) are handled by the
        /// UICampFormationInformationPresenter.Set hook.
        /// </summary>
        private void UpdateFormationSelector()
        {
            if (_formationSelector == null) return;

            // Only poll when the root menu highlights "Formation".
            if (_lastRootMenuItemName != "Formation")
            {
                // Don't reset _formationWasActive here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "Formation" during normal navigation.
                return;
            }

            try
            {
                bool isActive = _formationSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_formationWasActive)
                    {
                        _formationWasActive = false;
                        DebugLogger.LogState("CampFormation: selector hidden.");
                    }
                    return;
                }

                if (!_formationWasActive)
                {
                    _formationWasActive = true;

                    if (!_formationSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_formation_screen"));
                        DebugLogger.LogState("CampFormation: selector visible.");
                    }
                    else
                    {
                        _formationSuppressHeading = false;
                        DebugLogger.LogState("CampFormation: stale open — heading suppressed.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateFormationSelector: {ex.Message}");
                _formationSelector = null;
                _formationWasActive = false;
                _formationSuppressHeading = false;
            }
        }

        /// <summary>
        /// Polls the UICampSkillSelector for active state changes.
        /// Announces "Skills." when the screen opens.
        /// Skill detail announcements (name, description, level, position) are handled by the
        /// UISkillInformationPresenter.Set hook.
        /// </summary>
        private void UpdateSkillSelector()
        {
            if (_skillSelector == null) return;

            // Only poll when the root menu highlights "Skill".
            if (_lastRootMenuItemName != "Skill")
            {
                // Don't reset _skillWasActive here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "Skill" during normal navigation.
                return;
            }

            try
            {
                bool isActive = _skillSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_skillWasActive)
                    {
                        _skillWasActive = false;
                        DebugLogger.LogState("CampSkill: selector hidden.");
                    }
                    return;
                }

                if (!_skillWasActive)
                {
                    _skillWasActive = true;

                    if (!_skillSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_skill_screen"));
                        DebugLogger.LogState("CampSkill: selector visible.");
                    }
                    else
                    {
                        _skillSuppressHeading = false;
                        DebugLogger.LogState("CampSkill: stale open — heading suppressed.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateSkillSelector: {ex.Message}");
                _skillSelector = null;
                _skillWasActive = false;
                _skillSuppressHeading = false;
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UICampWindow.Open().
        /// Announces the screen heading and caches selectors for polling.
        /// </summary>
        private static void CampWindow_Open_Postfix(UICampWindow __instance)
        {
            IsCampOpen = true;
            _campWindow = __instance;

            ScreenReader.Say(Loc.Get("camp_menu_screen"));
            DebugLogger.LogState("CampMenu: window opened.");

            if (__instance == null) return;

            _menuSelector = __instance.menuSelector;
            _lastIndex = -1;
            _wasActive = false;

            _itemSelector = __instance.itemSelector;
            _itemListSelectorBase = null;
            _itemLastIndex = -1;
            _itemWasActive = false;
            _itemSuppressHeading = false;

            _equipSelector = __instance.equipSelector;
            _equipWasActive = false;
            _equipSuppressHeading = false;
            _equipSlotListBase = null;
            _equipSlotLastIndex = -1;
            _equipSlotWasActive = false;
            _equipItemListBase = null;
            _equipItemListActive = false;

            if (_menuSelector != null)
                DebugLogger.LogState("CampMenu: menu selector cached.");
            else
                MelonLogger.Warning("[CAMP] campWindow.menuSelector is null.");

            if (_itemSelector != null)
            {
                DebugLogger.LogState("CampMenu: item selector cached.");

                // If the item selector is already active on open it is stale from a
                // previous session (the game does not reset its active state on close).
                // Pre-seed _itemLastIndex with the current index and mark heading as
                // suppressed so neither "Items." nor the stale item is re-announced.
                try
                {
                    if (_itemSelector.gameObject.activeInHierarchy)
                    {
                        var inner = _itemSelector.itemListSelector;
                        var baseSel = inner?.TryCast<UIListSelectorBase>();
                        if (baseSel != null)
                        {
                            _itemListSelectorBase = baseSel;
                            _itemLastIndex = baseSel.currentIndex;
                            _itemSuppressHeading = true;
                            DebugLogger.LogState($"CampItem: stale on open, seeded index={_itemLastIndex}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampItem stale-seed failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.itemSelector is null.");
            }

            _statusSelector = __instance.statusSelector;
            _statusScreenOpen = false;
            _statusLastIndex = -1;
            _statusParamData = null;
            _statusLevelData = null;
            _statusPlayerName = "";
            _lastRootMenuItemName = "";

            if (_statusSelector != null)
                DebugLogger.LogState("CampMenu: status selector cached.");
            else
                MelonLogger.Warning("[CAMP] campWindow.statusSelector is null.");

            if (_equipSelector != null)
            {
                DebugLogger.LogState("CampMenu: equip selector cached.");

                // Check for stale active state — suppress heading if already open.
                try
                {
                    if (_equipSelector.gameObject.activeInHierarchy)
                    {
                        _equipSuppressHeading = true;
                        DebugLogger.LogState("CampEquip: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampEquip stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.equipSelector is null.");
            }

            _battleSkillOuterSelector = __instance.battleSkillSelector;
            _battleSkillInnerSelector = null;
            _battleSkillListBase = null;
            _battleSkillWasActive = false;
            _battleSkillSuppressHeading = false;

            if (_battleSkillOuterSelector != null)
            {
                DebugLogger.LogState("CampMenu: battle skill selector cached.");

                // Check for stale active state — suppress heading if already open.
                try
                {
                    if (_battleSkillOuterSelector.gameObject.activeInHierarchy)
                    {
                        _battleSkillSuppressHeading = true;
                        DebugLogger.LogState("CampBattleSkill: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampBattleSkill stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.battleSkillSelector is null.");
            }

            _battleSkillSettingSelector = __instance.battleSkillSettingSelector;
            _battleSkillEquipListSel = null;
            _battleSkillEquipListBase = null;
            _battleSkillPickerListBase = null;
            _battleSkillEquipLastIndex = -1;
            _battleSkillSettingWasActive = false;
            _battleSkillSettingSuppressHeading = false;

            if (_battleSkillSettingSelector != null)
            {
                DebugLogger.LogState("CampMenu: battle skill setting selector cached.");

                // Check for stale active state — suppress heading if already open.
                try
                {
                    if (_battleSkillSettingSelector.gameObject.activeInHierarchy)
                    {
                        _battleSkillSettingSuppressHeading = true;
                        DebugLogger.LogState("CampBattleSkillSetting: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampBattleSkillSetting stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.battleSkillSettingSelector is null.");
            }

            _formationSelector = __instance.formationSelector;
            _formationWasActive = false;
            _formationSuppressHeading = false;

            if (_formationSelector != null)
            {
                DebugLogger.LogState("CampMenu: formation selector cached.");

                try
                {
                    if (_formationSelector.gameObject.activeInHierarchy)
                    {
                        _formationSuppressHeading = true;
                        DebugLogger.LogState("CampFormation: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampFormation stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.formationSelector is null.");
            }

            _skillSelector = __instance.skillSelector;
            _skillWasActive = false;
            _skillSuppressHeading = false;

            if (_skillSelector != null)
            {
                DebugLogger.LogState("CampMenu: skill selector cached.");

                try
                {
                    if (_skillSelector.gameObject.activeInHierarchy)
                    {
                        _skillSuppressHeading = true;
                        DebugLogger.LogState("CampSkill: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampSkill stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.skillSelector is null.");
            }
        }

        /// <summary>
        /// Postfix for UIItemInformationPresenter.Set(UIItemInformationData, ...).
        /// Fires whenever the item information panel is updated — which happens each time
        /// the cursor moves in the equip item list.
        ///
        /// Gated: only announces when the equip screen is open and its item list is active.
        /// Announces item name, description, battle effect, stat values with item equipped,
        /// factor name and description, and position in the list.
        ///
        /// Note on stats: statusParameterData contains absolute stats WITH the highlighted
        /// item equipped (not deltas). Only the five combat-relevant stats are announced
        /// (Attack, Defence, Magic/INT, Hit, Avoidance) and only those with non-zero values.
        /// </summary>
        private static void ItemInfoPresenter_Set_Postfix(UIItemInformationData data)
        {
            // Gate: only process when the equip screen is open and item list is active.
            if (_equipSelector == null) return;
            if (_lastRootMenuItemName != "Equip") return;

            try
            {
                if (!_equipSelector.gameObject.activeInHierarchy) return;
                if (!_equipItemListActive) return;
                if (data == null) return;

                string name        = data.itemName             ?? "";
                string description = data.itemInformation      ?? "";
                string effectInfo  = data.itemEffectInformation ?? "";
                string factorName  = data.itemFactorName        ?? "";
                string factorInfo  = data.itemFactorInformation ?? "";

                DebugLogger.LogGameValue("CampEquip.itemInfo",
                    $"name='{name}' desc='{description}' effect='{effectInfo}' " +
                    $"factor='{factorName}' factorInfo='{factorInfo}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(name))        sb.Append(name).Append(". ");
                if (!string.IsNullOrEmpty(description)) sb.Append(description).Append(". ");
                if (!string.IsNullOrEmpty(effectInfo))  sb.Append(effectInfo).Append(". ");

                // Announce the five combat stats shown in the stat comparison panel.
                // statusParameterData holds character stats with this item equipped.
                var stats = data.statusParameterData;
                if (stats != null)
                {
                    if (stats.attack  != 0) sb.Append(Loc.Get("camp_equip_stat_attack",    stats.attack)).Append(". ");
                    if (stats.defence != 0) sb.Append(Loc.Get("camp_equip_stat_defence",   stats.defence)).Append(". ");
                    if (stats.magic   != 0) sb.Append(Loc.Get("camp_equip_stat_magic",     stats.magic)).Append(". ");
                    if (stats.hit     != 0) sb.Append(Loc.Get("camp_equip_stat_hit",       stats.hit)).Append(". ");
                    if (stats.dodge   != 0) sb.Append(Loc.Get("camp_equip_stat_avoidance", stats.dodge)).Append(". ");
                }

                if (!string.IsNullOrEmpty(factorName))
                    sb.Append(Loc.Get("camp_equip_factor", factorName)).Append(". ");
                if (!string.IsNullOrEmpty(factorInfo))
                    sb.Append(factorInfo).Append(". ");

                // Append position in the list if the count is available.
                if (_equipItemListBase != null)
                {
                    int idx   = _equipItemListBase.currentIndex;
                    var lData = _equipItemListBase.currentDataList;
                    if (lData != null && lData.Count > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_equip_position", idx + 1, lData.Count));
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.ItemInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIBattleSkillInformationPresenter.Set(UIBattleSkillInformationData).
        /// Fires whenever any skill information panel updates — both the leveling screen and
        /// the setting screen's skill picker share this hook via the same presenter type.
        ///
        /// Leveling screen (UICampBattleSkillSelector active):
        ///   Announces skill name, level, MP cost, description, effect, position.
        ///
        /// Setting screen (UICampBattleSkillSettingSelector active, SelectBattleSkill state):
        ///   Announces "Assigning to [button]: [name]. [level]. [MP]. [desc]. [position]."
        ///   When in Equip state (browsing slots), hook is silent — polling handles it.
        /// </summary>
        private static void BattleSkillInfoPresenter_Set_Postfix(UIBattleSkillInformationData data)
        {
            if (data == null) return;

            // Only process when the root menu is on "BattleSkill".
            if (_lastRootMenuItemName != "BattleSkill") return;

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
                    if (!string.IsNullOrEmpty(name))        sb.Append(name).Append(". ");
                    if (levelMax > 0)
                        sb.Append(Loc.Get("camp_battleskill_level", level, levelMax)).Append(". ");
                    if (consumeMP > 0)
                        sb.Append(Loc.Get("camp_battleskill_mp", consumeMP)).Append(". ");
                    if (!string.IsNullOrEmpty(description)) sb.Append(description).Append(". ");
                    if (!string.IsNullOrEmpty(effect))      sb.Append(effect).Append(". ");

                    if (_battleSkillInnerSelector != null && _battleSkillListBase != null)
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
                    buttonName = slotData.categoryName ?? "";

                DebugLogger.LogGameValue("CampBattleSkillSetting.picker",
                    $"button='{buttonName}' name='{name}' lv={level}/{levelMax} mp={consumeMP} " +
                    $"desc='{description}' effect='{effect}'");

                var sb2 = new StringBuilder();
                if (!string.IsNullOrEmpty(buttonName))
                    sb2.Append(Loc.Get("camp_battleskill_setting_assigning", buttonName)).Append(". ");
                if (!string.IsNullOrEmpty(name))        sb2.Append(name).Append(". ");
                if (levelMax > 0)
                    sb2.Append(Loc.Get("camp_battleskill_level", level, levelMax)).Append(". ");
                if (consumeMP > 0)
                    sb2.Append(Loc.Get("camp_battleskill_mp", consumeMP)).Append(". ");
                if (!string.IsNullOrEmpty(description)) sb2.Append(description).Append(". ");
                if (!string.IsNullOrEmpty(effect))      sb2.Append(effect).Append(". ");

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
        /// Postfix for UICampStatusParameterPresenter.Setup(UICampStatusParameterData).
        /// Fires whenever the status parameter panel updates — on screen open and character
        /// tab changes. Captures the stat data so UpdateStatusSelector can include all stats
        /// (attack, defence, magic, hit, dodge, critical, str, con, dex, agl, int, luc,
        /// stamina, guts) in the announcement.
        /// </summary>
        private static void StatusParamPresenter_Setup_Postfix(UICampStatusParameterData data)
        {
            _statusParamData = data;
            DebugLogger.LogGameValue("CampStatus.paramHook",
                $"attack={data?.attack} defence={data?.defence} str={data?.str}");
        }

        /// <summary>
        /// Postfix for UICampFormationInformationPresenter.Set(...).
        /// Fires whenever the formation information panel updates — on each navigation
        /// in the formation list. Announces formation name, effect description, and position.
        /// </summary>
        private static void FormationInfoPresenter_Set_Postfix(
            string formationName, string effectDescription)
        {
            if (!_formationWasActive) return;
            if (_formationSelector == null) return;
            if (_lastRootMenuItemName != "Formation") return;

            try
            {
                DebugLogger.LogGameValue("CampFormation.info",
                    $"name='{formationName}' effect='{effectDescription}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(formationName))
                    sb.Append(formationName).Append(". ");
                if (!string.IsNullOrEmpty(effectDescription))
                    sb.Append(effectDescription).Append(". ");

                // Read position from the selector (cast to UIListSelectorBase).
                var baseSel = _formationSelector.TryCast<UIListSelectorBase>();
                if (baseSel != null)
                {
                    int idx = baseSel.currentIndex;
                    var list = baseSel.currentDataList;
                    int total = list?.Count ?? 0;
                    if (total > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_formation_position", idx + 1, total));
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.FormationInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UISkillInformationPresenter.Set(UISkillInformationData).
        /// Fires whenever the skill information panel updates — on each navigation
        /// in the skills list. Announces skill name, level, description, and position.
        /// </summary>
        private static void SkillInfoPresenter_Set_Postfix(UISkillInformationData data)
        {
            if (!_skillWasActive) return;
            if (_skillSelector == null) return;
            if (_lastRootMenuItemName != "Skill") return;
            if (data == null) return;

            try
            {
                string name = data.skillName ?? "";
                string description = data.skillDescription ?? "";
                int level = data.skillLevel;

                DebugLogger.LogGameValue("CampSkill.info",
                    $"name='{name}' lv={level} desc='{description}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(name))
                    sb.Append(name).Append(". ");
                if (level > 0)
                    sb.Append(Loc.Get("camp_skill_level", level)).Append(". ");
                if (!string.IsNullOrEmpty(description))
                    sb.Append(description).Append(". ");

                // Read position from the selector (cast to UIListSelectorBase).
                var baseSel = _skillSelector.TryCast<UIListSelectorBase>();
                if (baseSel != null)
                {
                    int idx = baseSel.currentIndex;
                    var list = baseSel.currentDataList;
                    int total = list?.Count ?? 0;
                    if (total > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_skill_position", idx + 1, total));
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.SkillInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Status Screen Hooks (hook-driven detection)

        /// <summary>
        /// Postfix for UICampStatusSelector.UpdatePresenter(int, int, bool).
        /// Fires LAST in the hook chain when the status screen opens or character tab changes.
        /// By this point, UpdateName, LevelPresenter.Setup, and StatusParamPresenter.Setup
        /// have already captured all the data we need. This is the announcement trigger.
        /// </summary>
        private static void Diag_StatusSelector_UpdatePresenter(int index, int difference, bool isDelay)
        {
            try
            {
                // Announce heading on first open.
                if (!_statusScreenOpen)
                {
                    _statusScreenOpen = true;
                    _statusLastIndex = -1;
                    ScreenReader.Say(Loc.Get("camp_status_screen"));
                    DebugLogger.LogState("CampStatus: screen opened (hook-driven).");
                }

                // Get total from tab presenter's data list.
                int total = 1;
                if (_statusSelector != null)
                {
                    var tabPresenter = _statusSelector.statusPresenter?.characterTabPresenter;
                    var tabList = tabPresenter?.itemTabDataList;
                    if (tabList != null)
                        total = tabList.Count;
                }

                // Skip if same index (shouldn't happen often since game fires on change).
                if (index == _statusLastIndex) return;
                _statusLastIndex = index;

                AnnounceStatusCharacter(index, total);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.StatusUpdatePresenter: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampStatusSelector.UpdateName(PlayerID, ConstPlayerParameter).
        /// Fires when the character name updates — captures the name for announcement.
        /// </summary>
        private static void Diag_StatusSelector_UpdateName(PlayerID playerID, ConstPlayerParameter playerParam)
        {
            try
            {
                var pm = ParameterManager.Instance;
                _statusPlayerName = pm != null ? (pm.GetCharacterFirstName(playerID) ?? "") : playerID.ToString();
                DebugLogger.LogGameValue("CampStatus.nameHook", $"playerID={playerID} name='{_statusPlayerName}'");
            }
            catch (Exception ex)
            {
                _statusPlayerName = playerID.ToString();
                MelonLogger.Warning($"CampMenuHandler.StatusUpdateName: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampStatusSelector.UpdateStatusLevel(CharacterParameter).
        /// Fires when level data updates — no data to capture here (LevelPresenter.Setup has it).
        /// </summary>
        private static void Diag_StatusSelector_UpdateStatusLevel(CharacterParameter charaParam)
        {
            DebugLogger.LogState("CampStatus: UpdateStatusLevel fired.");
        }

        /// <summary>
        /// Postfix for UICampStatusLevelPresenter.Setup(UICampStatusLevelData).
        /// Fires when the level/HP/MP presenter updates — captures the data for announcement.
        /// </summary>
        private static void Diag_StatusLevelPresenter_Setup(UICampStatusLevelData data)
        {
            _statusLevelData = data;
            DebugLogger.LogGameValue("CampStatus.levelHook",
                data != null ? $"lv={data.level} hp={data.hp}/{data.maxHp} mp={data.mp}/{data.maxMp}" : "null");
        }

        #endregion
    }
}
