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
                RuntimeHelpers.RunClassConstructor(typeof(UICampCombatSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampCombatSkillListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEnhanceBonusData).TypeHandle);
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
                RuntimeHelpers.RunClassConstructor(typeof(UIElementalGroupPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIElementalData).TypeHandle);

                // Database sub-screens
                RuntimeHelpers.RunClassConstructor(typeof(UICampTutorialListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampTutorialListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommonBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEnemyPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEnemyPictureBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEnemyPictureBookInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampItemPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFishPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFishPictureBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFishPictureBookInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampLocationPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampLocationPictureBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampLocationPictureBookInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UITutorialInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPlayerDataSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPlayerDataPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPlayerDataItemPresenter).TypeHandle);

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

                // UIElementalGroupPresenter.Set fires when the elemental resistance panel
                // updates (CallerCount 8 — hookable). Announces resistances on Triangle press.
                harmony.Patch(
                    AccessTools.Method(typeof(UIElementalGroupPresenter), "Set",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<UIElementalData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(ElementalGroupPresenter_Set_Postfix))
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

                // UICampPartyMemberPresenter.SetData fires per-slot when a character is
                // assigned to a party member slot (CallerCount 2). Caches per-slot data.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampPartyMemberPresenter), "SetData",
                        new Type[] { typeof(int), typeof(UICampPartyMemberSelectItemData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(PartyMemberPresenter_SetData_Postfix))
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
                        _menuSelector = null;
                        DebugLogger.LogState("CampMenu: window closed (IsCampOpen=false via IsOpened).");
                    }
                }
                catch (Exception ex)
                {
                    IsCampOpen = false;
                    _campWindow = null;
                    _menuSelector = null;
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
            UpdateTutorialSelector();
            UpdateEnemyPictureBook();
            UpdateItemPictureBook();
            UpdateFishPictureBook();
            UpdateLocationPictureBook();
            UpdatePlayerData();
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

            // --- STALE-SEED GUIDE FOR FUTURE SUB-SCREENS ---
            // ALL sub-screen selectors have activeInHierarchy=True permanently.
            // The root menu selector ALSO stays active when inside a sub-screen.
            // Sub-screens are gated by _lastRootMenuItemName, which passes as soon as
            // the root menu cursor highlights the item — BEFORE the user confirms.
            //
            // To prevent spurious announcements when merely highlighting a root item:
            //   1. Call _xxxState.Reset() to clear the SubScreenState.
            //   2. If the selector is already active (stale), call SuppressNextHeading()
            //      or SeedOnOpen() to suppress the heading on first CheckEntry.
            //   3. If the sub-screen has NESTED CHILD selectors with their own manual
            //      _xxxWasActive / _xxxLastIndex tracking, ALSO seed those:
            //        - Set _xxxLastIndex = childSelector.currentIndex
            //        - Set _xxxWasActive = true  ← CRITICAL, prevents first-activation reset
            //   4. Preferred pattern for NEW child selectors: skip _xxxWasActive entirely,
            //      just compare idx == _xxxLastIndex. See UpdateBattleSkillEquipSlotList.
            //      This avoids the stale-seed pitfall altogether.
            // ---

            _itemSelector = __instance.itemSelector;
            _itemListSelectorBase = null;
            _itemState.Reset();

            _equipSelector = __instance.equipSelector;
            _equipState.Reset();
            _equipSlotListBase = null;
            _equipSlotLastIndex = -1;
            _equipSlotWasActive = false;
            _equipItemListBase = null;
            _equipItemListActive = false;
            _cachedElementalAnnouncement = null;

            if (_menuSelector != null)
                DebugLogger.LogState("CampMenu: menu selector cached.");
            else
                MelonLogger.Warning("[CAMP] campWindow.menuSelector is null.");

            if (_itemSelector != null)
            {
                DebugLogger.LogState("CampMenu: item selector cached.");

                // If the item selector is already active on open it is stale from a
                // previous session (the game does not reset its active state on close).
                // Pre-seed index so neither "Items." nor the stale item is re-announced.
                try
                {
                    if (_itemSelector.gameObject.activeInHierarchy)
                    {
                        var inner = _itemSelector.itemListSelector;
                        var baseSel = inner?.TryCast<UIListSelectorBase>();
                        if (baseSel != null)
                        {
                            _itemListSelectorBase = baseSel;
                            _itemState.SeedOnOpen(baseSel.currentIndex);
                            DebugLogger.LogState($"CampItem: stale on open, seeded index={_itemState.LastIndex}.");
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

                // STALE-SEED for nested child selector.
                // The equip sub-screen has a child slot list with its own _equipSlotWasActive
                // and _equipSlotLastIndex tracking. We must seed BOTH the SubScreenState (outer)
                // AND the child's tracking variables. If _equipSlotWasActive is left false,
                // UpdateEquipSlotList's first-activation logic resets _equipSlotLastIndex to -1,
                // causing a spurious slot announcement when the root menu cursor merely
                // highlights "Equip" (before the user presses confirm to enter).
                // See SubScreenState.cs class docs for the full pattern.
                try
                {
                    if (_equipSelector.gameObject.activeInHierarchy)
                    {
                        _equipState.SuppressNextHeading();
                        var slotSel = _equipSelector.equipListSelector;
                        var slotBase = slotSel?.TryCast<UIListSelectorBase>();
                        if (slotBase != null)
                        {
                            _equipSlotListBase = slotBase;
                            _equipSlotLastIndex = slotBase.currentIndex;
                            // Mark child slot list as already active so its first-activation
                            // logic in UpdateEquipSlotList doesn't reset the seeded index.
                            _equipSlotWasActive = true;
                        }
                        DebugLogger.LogState($"CampEquip: stale on open, seeded slotIdx={_equipSlotLastIndex}.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampEquip stale-seed failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.equipSelector is null.");
            }

            _battleSkillOuterSelector = __instance.battleSkillSelector;
            _battleSkillInnerSelector = null;
            _battleSkillListBase = null;
            _combatSkillInnerSelector = null;
            _combatSkillListBase = null;
            _battleSkillWasActive = false;
            _battleSkillHeadingPending = false;

            if (_battleSkillOuterSelector != null)
                DebugLogger.LogState("CampMenu: battle skill selector cached.");
            else
                MelonLogger.Warning("[CAMP] campWindow.battleSkillSelector is null.");

            _battleSkillSettingSelector = __instance.battleSkillSettingSelector;
            _battleSkillEquipListSel = null;
            _battleSkillEquipListBase = null;
            _battleSkillPickerListBase = null;
            _battleSkillEquipLastIndex = -1;
            _battleSkillSettingState.Reset();

            if (_battleSkillSettingSelector != null)
            {
                DebugLogger.LogState("CampMenu: battle skill setting selector cached.");

                // Seed child equip slot index so highlighting "BattleSkill" on root menu
                // doesn't trigger a spurious slot announcement.
                try
                {
                    if (_battleSkillSettingSelector.gameObject.activeInHierarchy)
                    {
                        _battleSkillSettingState.SuppressNextHeading();
                        var equipSel = _battleSkillSettingSelector.equipListSelector;
                        var equipBase = equipSel?.TryCast<UIListSelectorBase>();
                        if (equipBase != null)
                        {
                            _battleSkillEquipListSel = equipSel;
                            _battleSkillEquipListBase = equipBase;
                            _battleSkillEquipLastIndex = equipBase.currentIndex;
                        }
                        DebugLogger.LogState($"CampBattleSkillSetting: stale on open, seeded equipIdx={_battleSkillEquipLastIndex}.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"CampBattleSkillSetting stale-seed failed: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.battleSkillSettingSelector is null.");
            }

            _formationSelector = __instance.formationSelector;
            _formationState.Reset();

            if (_formationSelector != null)
            {
                DebugLogger.LogState("CampMenu: formation selector cached.");
                StaleSuppressIfActive(_formationSelector.gameObject, _formationState, "CampFormation");
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.formationSelector is null.");
            }

            _skillSelector = __instance.skillSelector;
            _skillState.Reset();

            if (_skillSelector != null)
            {
                DebugLogger.LogState("CampMenu: skill selector cached.");
                StaleSuppressIfActive(_skillSelector.gameObject, _skillState, "CampSkill");
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.skillSelector is null.");
            }

            // --- Party Formation ---
            _selectCharSelector = __instance.selectCharacterSelector;
            _selectCharState.Reset();
            _selectCharSlotData.Clear();

            if (_selectCharSelector != null)
            {
                DebugLogger.LogState("CampMenu: select character selector cached.");
                StaleSuppressIfActive(_selectCharSelector.gameObject, _selectCharState, "CampPartyFormation");
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.selectCharacterSelector is null.");
            }

            // --- Assist Formation ---
            _assistSelector = __instance.assistSettingSelector;
            _assistState.Reset();
            _assistEquipListBase = null;
            _assistEquipLastIndex = -1;
            _assistCharListBase = null;
            _assistCharLastIndex = -1;
            _assistLastState = -1;

            if (_assistSelector != null)
            {
                DebugLogger.LogState("CampMenu: assist setting selector cached.");
                StaleSuppressIfActive(_assistSelector.gameObject, _assistState, "CampAssist");
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.assistSettingSelector is null.");
            }

            // --- Tactics ---
            _operationSelector = __instance.operationSelector;
            _operationState.Reset();
            _operationCharLastIndex = -1;
            _operationSelectListBase = null;
            _operationSelectLastIndex = -1;
            _operationLastState = -1;

            if (_operationSelector != null)
            {
                DebugLogger.LogState("CampMenu: operation selector cached.");
                StaleSuppressIfActive(_operationSelector.gameObject, _operationState, "CampTactics");
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.operationSelector is null.");
            }

            // --- Database sub-screens ---
            // Picture book selectors have activeInHierarchy=true permanently (same stale bug).
            // We eagerly get the UIListSelectorBase and seed the current index so that
            // merely highlighting the Database sub-item on root menu doesn't trigger an announcement.
            // The Update methods also gate on !_wasActive (root menu hidden) for extra safety.
            _tutorialSelector = __instance.tutorialSelector;
            _tutorialListBase = null;
            _tutorialState.Reset();
            if (_tutorialSelector != null)
            {
                DebugLogger.LogState("CampMenu: tutorial selector cached.");
                StaleSeedPictureBook(_tutorialSelector, _tutorialState, ref _tutorialListBase, "CampTutorial");
            }

            _enemyPBSelector = __instance.enemyPictureBookSelector;
            _enemyPBListBase = null;
            _enemyPBState.Reset();
            if (_enemyPBSelector != null)
            {
                DebugLogger.LogState("CampMenu: enemy picture book selector cached.");
                StaleSeedPictureBook(_enemyPBSelector, _enemyPBState, ref _enemyPBListBase, "CampEnemyPB");
            }

            _itemPBSelector = __instance.itemPictureBookSelector;
            _itemPBListBase = null;
            _itemPBState.Reset();
            if (_itemPBSelector != null)
            {
                DebugLogger.LogState("CampMenu: item picture book selector cached.");
                StaleSeedPictureBook(_itemPBSelector, _itemPBState, ref _itemPBListBase, "CampItemPB");
            }

            _fishPBSelector = __instance.fishPictureBookSelector;
            _fishPBListBase = null;
            _fishPBState.Reset();
            if (_fishPBSelector != null)
            {
                DebugLogger.LogState("CampMenu: fish picture book selector cached.");
                StaleSeedPictureBook(_fishPBSelector, _fishPBState, ref _fishPBListBase, "CampFishPB");
            }

            _locationPBSelector = __instance.locationPictureBookSelector;
            _locationPBListBase = null;
            _locationPBState.Reset();
            if (_locationPBSelector != null)
            {
                DebugLogger.LogState("CampMenu: location picture book selector cached.");
                StaleSeedPictureBook(_locationPBSelector, _locationPBState, ref _locationPBListBase, "CampLocationPB");
            }

            _playerDataSelector = __instance.playerDataSelector;
            _playerDataPresenter = _playerDataSelector?.playerDataPresenter;
            _playerDataState.Reset();
            _playerDataIndex = 0;
            _playerDataLastIndex = 0;
            if (_playerDataSelector != null)
            {
                DebugLogger.LogState("CampMenu: player data selector cached.");
                try
                {
                    var go = (_playerDataSelector as UnityEngine.Component)?.gameObject;
                    if (go != null && go.activeInHierarchy)
                    {
                        _playerDataState.SeedOnOpen(0);
                        DebugLogger.LogState("CampPlayerData: stale on open, seeded index=0.");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampPlayerData: stale seed error: {ex.Message}");
                    _playerDataState.SuppressNextHeading();
                }
            }
        }

        #endregion

        #region Helpers

        private static string StripTags(string text) => TextUtil.StripTags(text);

        /// <summary>
        /// If the game object is already active (stale from previous session),
        /// marks the SubScreenState to suppress its heading on next activation.
        /// Called in the Open postfix for each sub-screen selector.
        /// </summary>
        private static void StaleSuppressIfActive(
            UnityEngine.GameObject go, SubScreenState state, string logLabel)
        {
            try
            {
                if (go.activeInHierarchy)
                {
                    state.SuppressNextHeading();
                    DebugLogger.LogState($"{logLabel}: stale on open — heading will be suppressed.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{logLabel} stale-check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Eagerly casts a picture book selector to UIListSelectorBase and seeds
        /// the SubScreenState with the current index. This prevents spurious
        /// announcements when the selector is already active on camp open.
        /// </summary>
        private static void StaleSeedPictureBook(
            Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase selector,
            SubScreenState state,
            ref UIListSelectorBase listBase,
            string logLabel)
        {
            try
            {
                var go = (selector as UnityEngine.Component)?.gameObject;
                if (go == null || !go.activeInHierarchy) return;

                var baseSel = selector.TryCast<UIListSelectorBase>();
                if (baseSel != null)
                {
                    listBase = baseSel;
                    state.SeedOnOpen(baseSel.currentIndex);
                    DebugLogger.LogState($"{logLabel}: stale on open, seeded index={state.LastIndex}.");
                }
                else
                {
                    state.SuppressNextHeading();
                    DebugLogger.LogState($"{logLabel}: stale on open — heading suppressed (no base cast).");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{logLabel} stale-seed failed: {ex.Message}");
            }
        }

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
