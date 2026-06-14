using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler: UICampWindow.Open postfix — caches selectors and seeds stale-active suppression.
    public partial class CampMenuHandler
    {
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

            // Detect field shortcut IC (D-pad Down opens directly to SelectSpecialSkill).
            UIDefine.CampState openState = UIDefine.CampState.Menu;
            try { openState = __instance.OpenCampState; }
            catch { /* fallback to Menu */ }

            _isFieldShortcutIC = (openState == UIDefine.CampState.SelectSpecialSkill);

            if (_isFieldShortcutIC)
            {
                ScreenReader.Say(Loc.Get("ic_shortcut_screen"));
                DebugLogger.LogState("CampMenu: field shortcut IC opened.");
            }
            else
            {
                ScreenReader.Say(Loc.Get("camp_menu_screen"));
                DebugLogger.LogState("CampMenu: window opened.");
            }

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
            _cachedStatusElementalAnnouncement = "";
            _cachedFriendshipAnnouncement = "";
            _statusVirtualLines.Clear();
            _statusVirtualIndex = -1;
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

            // Quest and Mission sub-screens (separate windows, not direct selectors)
            _questState.Reset();
            _questListBase = null;
            _missionState.Reset();
            _missionListBase = null;
            _missionLastCategory = -1;

            // --- Item Creation ---
            CacheItemCreationSelectors(__instance);

            _playerDataSelector = __instance.playerDataSelector;
            _playerDataPresenter = _playerDataSelector?.playerDataPresenter;
            _playerDataState.Reset();
            _playerDataIndex = 0;
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
    }
}
