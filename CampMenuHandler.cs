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
    public partial class CampMenuHandler
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

        /// <summary>
        /// Timestamp when IsCampOpen was set to true. Used to prevent the
        /// IsOpened closure check from falsely clearing the flag during the
        /// window's opening animation (IsOpened returns false briefly after Open).
        /// </summary>
        private static float _campOpenTime;

        /// <summary>Cached UICampWindow instance for detecting camp closure.</summary>
        private static UICampWindow _campWindow = null;

        // Tracks which root menu item is highlighted (for sub-screen detection).
        private static string _lastRootMenuItemName = "";

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
                RuntimeHelpers.RunClassConstructor(typeof(UICampSelectCharacterSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPartyMemberPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPartyMemberSelectItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CampCharacterStatusParameterData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampCharacterStatusPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingEquipListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingCharacterListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingCharacterListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationSelectListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationCharacterListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIOperationListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UITalentPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UITalentData).TypeHandle);

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

                // UITalentPresenter.Set fires when the status screen initializes
                // (CallerCount 1 — hookable). Caches talent data; announced on page switch.
                harmony.Patch(
                    AccessTools.Method(typeof(UITalentPresenter), "Set",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<UITalentData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(TalentPresenter_Set_Postfix))
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

                // UICampCharacterStatusPresenter.SetStatus fires when the party formation
                // screen populates/updates the character data list (CallerCount 1).
                harmony.Patch(
                    AccessTools.Method(typeof(UICampCharacterStatusPresenter), "SetStatus",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(CharacterStatusPresenter_SetStatus_Postfix))
                );

                // UICampOperationInformationPresenter.Set fires when the tactics operation
                // info panel updates (CallerCount 1). Announces operation name + description.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampOperationInformationPresenter), "Set",
                        new Type[] { typeof(string), typeof(string), typeof(string) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(OperationInfoPresenter_Set_Postfix))
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
                    // Grace period: IsOpened returns false during the opening animation,
                    // so ignore it for the first second after the Open postfix fires.
                    if (!_campWindow.IsOpened && (UnityEngine.Time.time - _campOpenTime) > 1.0f)
                    {
                        IsCampOpen = false;
                        _campWindow = null;
                        DebugLogger.LogState("CampMenu: window closed (IsCampOpen=false via IsOpened).");
                    }
                }
                catch (Exception ex)
                {
                    IsCampOpen = false;
                    _campWindow = null;
                    DebugLogger.LogState($"CampMenu: closure check error: {ex.Message}");
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
            UpdatePartyFormationSelector();
            UpdateAssistSettingSelector();
            UpdateTacticsSelector();
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
                    _statusLastPageIndex = -1;
                    _statusParamData = null;
                    _statusLevelData = null;
                    _statusPlayerName = "";
                    _cachedTalentAnnouncement = "";
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

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UICampWindow.Open().
        /// Announces the screen heading and caches selectors for polling.
        /// </summary>
        private static void CampWindow_Open_Postfix(UICampWindow __instance)
        {
            IsCampOpen = true;
            _campOpenTime = UnityEngine.Time.time;
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
            _statusLastPageIndex = -1;
            _statusParamData = null;
            _statusLevelData = null;
            _statusPlayerName = "";
            _cachedTalentAnnouncement = "";
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

            // --- Party Formation ---
            _selectCharSelector = __instance.selectCharacterSelector;
            _selectCharLastIndex = -1;
            _selectCharWasActive = false;
            _selectCharSuppressHeading = false;
            _selectCharDataList = null;

            if (_selectCharSelector != null)
            {
                DebugLogger.LogState("CampMenu: select character selector cached.");

                try
                {
                    if (_selectCharSelector.gameObject.activeInHierarchy)
                    {
                        _selectCharSuppressHeading = true;
                        DebugLogger.LogState("CampPartyFormation: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampPartyFormation stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.selectCharacterSelector is null.");
            }

            // --- Assist Formation ---
            _assistSelector = __instance.assistSettingSelector;
            _assistWasActive = false;
            _assistSuppressHeading = false;
            _assistEquipListBase = null;
            _assistEquipLastIndex = -1;
            _assistCharListBase = null;
            _assistCharLastIndex = -1;
            _assistLastState = -1;

            if (_assistSelector != null)
            {
                DebugLogger.LogState("CampMenu: assist setting selector cached.");

                try
                {
                    if (_assistSelector.gameObject.activeInHierarchy)
                    {
                        _assistSuppressHeading = true;
                        DebugLogger.LogState("CampAssist: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampAssist stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.assistSettingSelector is null.");
            }

            // --- Tactics ---
            _operationSelector = __instance.operationSelector;
            _operationWasActive = false;
            _operationSuppressHeading = false;
            _operationCharLastIndex = -1;
            _operationSelectListBase = null;
            _operationSelectLastIndex = -1;
            _operationLastState = -1;

            if (_operationSelector != null)
            {
                DebugLogger.LogState("CampMenu: operation selector cached.");

                try
                {
                    if (_operationSelector.gameObject.activeInHierarchy)
                    {
                        _operationSuppressHeading = true;
                        DebugLogger.LogState("CampTactics: stale on open — heading will be suppressed.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampTactics stale-check failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.operationSelector is null.");
            }
        }

        #endregion

        #region Helpers

        private static string StripTags(string text) => TextUtil.StripTags(text);

        /// <summary>
        /// Resets camp menu state on scene change, preventing IsCampOpen from
        /// remaining stale if the scene unloads while camp is open.
        /// </summary>
        public void OnSceneChanged()
        {
            if (IsCampOpen)
            {
                IsCampOpen = false;
                _campWindow = null;
                DebugLogger.LogState("CampMenu: scene changed — IsCampOpen reset.");
            }
        }

        #endregion
    }
}
