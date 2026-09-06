using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
        // Holds the CampMenuItem ENUM identifier ("Equip", "BattleSkill", ...), which is
        // stable across game languages — never compare against on-screen text here.
        private static string _lastRootMenuItemName = "";

        // Localized on-screen labels for root menu items, keyed by CampMenuItem enum
        // value. UICampMenuItemData carries no display text (only the enum), and the
        // enum identifiers are English-only — the row's rendered GameText is the only
        // language-correct source. Captured from the universal OnSelected hook
        // (ListSelectionHandler), which re-fires on every focus, so a live language
        // switch refreshes each entry before its row is announced.
        private static readonly Dictionary<int, string> _rootMenuLabels =
            new Dictionary<int, string>();

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
                        _isFieldShortcutIC = false;
                        _campWindow = null;
                        _menuSelector = null;
                        DebugLogger.LogState("CampMenu: window closed (IsCampOpen=false via IsOpened).");
                    }
                }
                catch (Exception ex)
                {
                    IsCampOpen = false;
                    _isFieldShortcutIC = false;
                    _campWindow = null;
                    _menuSelector = null;
                    DebugLogger.LogState($"CampMenu: closure check error: {ex.Message}");
                }
            }

            UpdateRootMenu();
            UpdateItemSelector();
            UpdateItemCharacterSelect();
            UpdateStatusSelector();
            UpdateEquipSelector();
            UpdateBattleSkillSelector();
            UpdateBattleSkillSettingSelector();
            UpdateFormationSelector();
            UpdateSkillSelector();
            SyncFormationSiblingScreen();
            UpdatePartyFormationSelector();
            UpdateAssistSettingSelector();
            UpdateTacticsSelector();
            UpdateItemCreation();
            UpdateSkillLearning();
            UpdateQuestList();
            UpdateMissionList();
            UpdateTutorialSelector();
            UpdateEnemyPictureBook();
            UpdateItemPictureBook();
            UpdateFishPictureBook();
            UpdateLocationPictureBook();
            UpdatePlayerData();
            UpdateStoryHint();

            // Last, after every screen has had its chance to claim them: speak the
            // battle skill readout and any L1/R1 tab label that was parked for a row
            // announcement which never came, so nothing is dropped in silence.
            FlushPendingBattleSkillInfo();
            TickTabSwitchAnnouncers();
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
                    _statusPlayerID = PlayerID.INVALID;
                    _cachedTalentAnnouncement = "";
                    _cachedStatusElementalAnnouncement = "";
                    _cachedStatusElementalLines.Clear();
                    _cachedFriendshipAnnouncement = "";
                    _cachedFriendshipLines.Clear();
                    _statusVirtualLines.Clear();
                    _statusVirtualIndex = -1;
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

                // Speak the localized on-screen label; the enum identifier is the
                // English-only fallback if the row's OnSelected has not fired yet.
                string spokenName = _rootMenuLabels.TryGetValue((int)item.menuItem, out string label)
                    ? label
                    : name;

                DebugLogger.LogGameValue("CampMenu.item",
                    $"{name} spoken='{spokenName}' available={available} ({idx + 1}/{total})");

                if (available)
                    ScreenReader.Say(Loc.Get("camp_menu_item", spokenName, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("camp_menu_item_unavailable", spokenName, idx + 1, total));
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

        #region Helpers

        private static string StripTags(string text) => TextUtil.StripTags(text);

        /// <summary>
        /// Postfix for UICampMenuItemPresenter.UpdateShow(ListItemDataBase).
        /// Fires from managed code whenever the game populates a root menu row (or a
        /// System sub-menu row — same presenter type) with its data. Caches the row's
        /// rendered (localized) label, keyed by its CampMenuItem enum value, so the
        /// root menu poll announces the on-screen text instead of the English enum
        /// identifier. Population happens on menu build — before any announcement —
        /// and a language change rebuilds the menu, refreshing the cache.
        /// </summary>
        private static void CampMenuItemPresenter_UpdateShow_Postfix(
            UICampMenuItemPresenter __instance, ListItemDataBase itemData)
        {
            try
            {
                if (__instance == null) return;
                var data = itemData?.TryCast<UICampMenuItemData>();
                if (data == null)
                {
                    DebugLogger.LogState("CampMenu.UpdateShow: data cast failed, label not cached.");
                    return;
                }

                var tmp = __instance.gameText?.TryCast<Il2CppTMPro.TMP_Text>();
                string label = StripTags(tmp?.text);
                if (string.IsNullOrWhiteSpace(label))
                {
                    DebugLogger.LogState($"CampMenu.UpdateShow: no text on row '{data.menuItem}'.");
                    return;
                }

                _rootMenuLabels[(int)data.menuItem] = label;
                DebugLogger.LogState($"CampMenu.UpdateShow: cached '{data.menuItem}' = '{label}'.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampMenu.UpdateShow_Postfix: {ex.Message}");
            }
        }

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
