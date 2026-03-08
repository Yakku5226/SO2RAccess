using Il2CppGame;
using MelonLoader;
using System;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Database sub-menu accessibility: Tutorial, Enemy Picture Book, Item Picture Book,
    /// Fish Picture Book, and Location Picture Book.
    ///
    /// All use polling-based navigation (native-only, no Harmony hooks for cursor movement).
    /// Browse: name + New flag + locked status + position.
    /// Confirm (Decision key): full detail readout for unlocked entries.
    /// Locked entries always announced (never silent).
    /// </summary>
    public partial class CampMenuHandler
    {
        #region Database Fields

        // Tutorial
        private static UICampTutorialListSelector _tutorialSelector;
        private static UIListSelectorBase _tutorialListBase;
        private static readonly SubScreenState _tutorialState = new SubScreenState();

        // Enemy Picture Book
        private static UICampEnemyPictureBookSelector _enemyPBSelector;
        private static UIListSelectorBase _enemyPBListBase;
        private static readonly SubScreenState _enemyPBState = new SubScreenState();

        // Item Picture Book
        private static UICampItemPictureBookSelector _itemPBSelector;
        private static UIListSelectorBase _itemPBListBase;
        private static readonly SubScreenState _itemPBState = new SubScreenState();

        // Fish Picture Book
        private static UICampFishPictureBookSelector _fishPBSelector;
        private static UIListSelectorBase _fishPBListBase;
        private static readonly SubScreenState _fishPBState = new SubScreenState();

        // Location Picture Book
        private static UICampLocationPictureBookSelector _locationPBSelector;
        private static UIListSelectorBase _locationPBListBase;
        private static readonly SubScreenState _locationPBState = new SubScreenState();

        // Player Data (virtual cursor — no native list selector)
        private static UICampPlayerDataSelector _playerDataSelector;
        private static UICampPlayerDataPresenter _playerDataPresenter;
        private static readonly SubScreenState _playerDataState = new SubScreenState();
        private static int _playerDataIndex;
        private static int _playerDataLastIndex;
        private static int _playerDataTotal;

        #endregion

        #region Tutorial

        /// <summary>
        /// Polls the tutorial list selector and announces entries.
        /// Locked tutorials (empty name or no information data) say "Locked".
        /// </summary>
        private void UpdateTutorialSelector()
        {
            if (_lastRootMenuItemName != "Tutorial") return;
            if (_tutorialSelector == null) return;

            try
            {
                if (_tutorialListBase == null)
                {
                    _tutorialListBase = _tutorialSelector.TryCast<UIListSelectorBase>();
                    if (_tutorialListBase == null) return;
                }

                bool isActive = _tutorialSelector.gameObject.activeInHierarchy;
                if (!_tutorialState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("db_tutorial_screen")),
                    "CampTutorial"))
                    return;

                int idx = _tutorialListBase.currentIndex;
                if (idx == _tutorialState.LastIndex)
                {
                    CheckTutorialDetailPress(idx);
                    return;
                }
                _tutorialState.LastIndex = idx;

                var list = _tutorialListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UICampTutorialListItemData>();
                if (item == null)
                {
                    var baseItem = list[idx].TryCast<UICommonBookListItemData>();
                    AnnounceTutorialBase(baseItem, idx, total);
                    return;
                }

                string name = item.name;
                bool isLocked = string.IsNullOrEmpty(name) ||
                                item.informationDataList == null ||
                                item.informationDataList.Count == 0;

                if (isLocked)
                {
                    ScreenReader.Say(Loc.Get("db_tutorial_locked", idx + 1, total));
                }
                else if (item.isNew)
                {
                    ScreenReader.Say(Loc.Get("db_tutorial_item_new", name, idx + 1, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("db_tutorial_item", name, idx + 1, total));
                }

                DebugLogger.LogGameValue("CampTutorial.item",
                    $"{(isLocked ? "LOCKED" : name)} ({idx + 1}/{total})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateTutorialSelector: {ex.Message}");
                _tutorialListBase = null;
            }
        }

        /// <summary>Fallback if cast to UICampTutorialListItemData fails.</summary>
        private static void AnnounceTutorialBase(UICommonBookListItemData item, int idx, int total)
        {
            if (item == null) return;
            string name = item.name;
            if (string.IsNullOrEmpty(name))
                ScreenReader.Say(Loc.Get("db_tutorial_locked", idx + 1, total));
            else if (item.isNew)
                ScreenReader.Say(Loc.Get("db_tutorial_item_new", name, idx + 1, total));
            else
                ScreenReader.Say(Loc.Get("db_tutorial_item", name, idx + 1, total));
        }

        /// <summary>Reads tutorial detail on confirm press.</summary>
        private void CheckTutorialDetailPress(int idx)
        {
            var gim = GameInputManager.Instance;
            if (gim == null || !gim.IsDown(GameInputManager.InputAction.Decision)) return;
            if (_tutorialListBase == null) return;

            var list = _tutorialListBase.currentDataList;
            if (list == null) return;
            int total = list.Count;
            if (idx < 0 || idx >= total) return;

            var item = list[idx].TryCast<UICampTutorialListItemData>();
            if (item == null) return;

            if (string.IsNullOrEmpty(item.name) ||
                item.informationDataList == null ||
                item.informationDataList.Count == 0)
                return; // Locked, nothing extra to say

            try
            {
                var info = item.informationDataList[0];
                if (info == null) return;

                string title = info.title;
                string desc = info.description;
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(desc))
                    ScreenReader.Say(Loc.Get("db_tutorial_detail", title, desc));
                else if (!string.IsNullOrEmpty(title))
                    ScreenReader.Say(title);
                else if (!string.IsNullOrEmpty(desc))
                    ScreenReader.Say(desc);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampTutorial detail error: {ex.Message}");
            }
        }

        #endregion

        #region Enemy Picture Book

        /// <summary>
        /// Polls the enemy picture book selector. Browse: name + position.
        /// Confirm: full stats. Locked: "Unknown enemy".
        /// </summary>
        private void UpdateEnemyPictureBook()
        {
            if (_lastRootMenuItemName != "EnemyList") return;
            if (_enemyPBSelector == null) return;

            try
            {
                if (_enemyPBListBase == null)
                {
                    _enemyPBListBase = _enemyPBSelector.TryCast<UIListSelectorBase>();
                    if (_enemyPBListBase == null) return;
                }

                bool isActive = _enemyPBSelector.gameObject.activeInHierarchy;
                if (!_enemyPBState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("db_enemy_screen")),
                    "CampEnemyPB"))
                    return;

                int idx = _enemyPBListBase.currentIndex;
                if (idx == _enemyPBState.LastIndex)
                {
                    CheckEnemyDetailPress(idx);
                    return;
                }
                _enemyPBState.LastIndex = idx;

                var list = _enemyPBListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UICampEnemyPictureBookListItemData>();
                if (item == null) return;

                var info = item.informationData;
                bool isLocked = info == null || !info.isRelease;

                if (isLocked)
                {
                    ScreenReader.Say(Loc.Get("db_enemy_locked", idx + 1, total));
                }
                else
                {
                    string name = item.enemyName;
                    if (string.IsNullOrEmpty(name) && info != null) name = info.enemyName;
                    if (item.isNew)
                        ScreenReader.Say(Loc.Get("db_enemy_item_new", name, idx + 1, total));
                    else
                        ScreenReader.Say(Loc.Get("db_enemy_item", name, idx + 1, total));
                }

                DebugLogger.LogGameValue("CampEnemyPB.item",
                    $"{(isLocked ? "LOCKED" : item.enemyName)} ({idx + 1}/{total})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateEnemyPictureBook: {ex.Message}");
                _enemyPBListBase = null;
            }
        }

        /// <summary>Reads full enemy details on confirm press.</summary>
        private void CheckEnemyDetailPress(int idx)
        {
            var gim = GameInputManager.Instance;
            if (gim == null || !gim.IsDown(GameInputManager.InputAction.Decision)) return;
            if (_enemyPBListBase == null) return;

            var list = _enemyPBListBase.currentDataList;
            if (list == null) return;
            int total = list.Count;
            if (idx < 0 || idx >= total) return;

            var item = list[idx].TryCast<UICampEnemyPictureBookListItemData>();
            if (item == null) return;

            var info = item.informationData;
            if (info == null || !info.isRelease) return;

            try
            {
                var sb = new StringBuilder();
                string name = info.enemyName;
                if (!string.IsNullOrEmpty(name)) AppendSentence(sb, name);
                if (info.isBoss) AppendSentence(sb, Loc.Get("db_enemy_boss"));
                AppendSentence(sb, Loc.Get("db_enemy_hp", info.hp));
                AppendSentence(sb, Loc.Get("db_enemy_exp", info.exp));
                AppendSentence(sb, Loc.Get("db_enemy_fol", info.money));

                var dropList = info.dropItemList;
                if (dropList != null && dropList.Count > 0)
                {
                    var drops = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < dropList.Count; i++)
                    {
                        string drop = dropList[i];
                        if (!string.IsNullOrEmpty(drop)) drops.Add(drop);
                    }
                    if (drops.Count > 0)
                        AppendSentence(sb, Loc.Get("db_enemy_drops", string.Join(", ", drops)));
                }

                if (!string.IsNullOrEmpty(info.livingPlace))
                    AppendSentence(sb, Loc.Get("db_enemy_habitat", info.livingPlace));

                ScreenReader.Say(sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampEnemyPB detail error: {ex.Message}");
            }
        }

        #endregion

        #region Item Picture Book

        /// <summary>
        /// Polls the item picture book selector. Browse: name + position.
        /// Confirm: name + description. Locked: "Unknown item".
        /// </summary>
        private void UpdateItemPictureBook()
        {
            if (_lastRootMenuItemName != "ItemPictureBook") return;
            if (_itemPBSelector == null) return;

            try
            {
                if (_itemPBListBase == null)
                {
                    _itemPBListBase = _itemPBSelector.TryCast<UIListSelectorBase>();
                    if (_itemPBListBase == null) return;
                }

                bool isActive = _itemPBSelector.gameObject.activeInHierarchy;
                if (!_itemPBState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("db_item_screen")),
                    "CampItemPB"))
                    return;

                int idx = _itemPBListBase.currentIndex;
                if (idx == _itemPBState.LastIndex)
                {
                    CheckItemPBDetailPress(idx);
                    return;
                }
                _itemPBState.LastIndex = idx;

                var list = _itemPBListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIItemListItemData>();
                if (item == null) return;

                string name = item.itemName;
                bool isLocked = string.IsNullOrEmpty(name);

                if (isLocked)
                {
                    ScreenReader.Say(Loc.Get("db_item_locked", idx + 1, total));
                }
                else if (item.isNew)
                {
                    ScreenReader.Say(Loc.Get("db_item_item_new", name, idx + 1, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("db_item_item", name, idx + 1, total));
                }

                DebugLogger.LogGameValue("CampItemPB.item",
                    $"{(isLocked ? "LOCKED" : name)} ({idx + 1}/{total})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateItemPictureBook: {ex.Message}");
                _itemPBListBase = null;
            }
        }

        /// <summary>Reads item description on confirm press.</summary>
        private void CheckItemPBDetailPress(int idx)
        {
            var gim = GameInputManager.Instance;
            if (gim == null || !gim.IsDown(GameInputManager.InputAction.Decision)) return;
            if (_itemPBListBase == null) return;

            var list = _itemPBListBase.currentDataList;
            if (list == null) return;
            int total = list.Count;
            if (idx < 0 || idx >= total) return;

            var item = list[idx].TryCast<UIItemListItemData>();
            if (item == null) return;
            if (string.IsNullOrEmpty(item.itemName)) return;

            try
            {
                var sb = new StringBuilder();
                AppendSentence(sb, item.itemName);
                if (!string.IsNullOrEmpty(item.itemDescription))
                    AppendSentence(sb, item.itemDescription);
                ScreenReader.Say(sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampItemPB detail error: {ex.Message}");
            }
        }

        #endregion

        #region Fish Picture Book

        /// <summary>
        /// Polls the fish picture book selector. Browse: name + position.
        /// Confirm: full details. Locked: "Unknown fish".
        /// </summary>
        private void UpdateFishPictureBook()
        {
            if (_lastRootMenuItemName != "FishPictureBook") return;
            if (_fishPBSelector == null) return;

            try
            {
                if (_fishPBListBase == null)
                {
                    _fishPBListBase = _fishPBSelector.TryCast<UIListSelectorBase>();
                    if (_fishPBListBase == null) return;
                }

                bool isActive = _fishPBSelector.gameObject.activeInHierarchy;
                if (!_fishPBState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("db_fish_screen")),
                    "CampFishPB"))
                    return;

                int idx = _fishPBListBase.currentIndex;
                if (idx == _fishPBState.LastIndex)
                {
                    CheckFishDetailPress(idx);
                    return;
                }
                _fishPBState.LastIndex = idx;

                var list = _fishPBListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UICampFishPictureBookListItemData>();
                if (item == null) return;

                var info = item.informationData;
                bool isLocked = info == null || !info.isRelease;

                if (isLocked)
                {
                    ScreenReader.Say(Loc.Get("db_fish_locked", idx + 1, total));
                }
                else
                {
                    string name = item.fishName;
                    if (string.IsNullOrEmpty(name) && info != null) name = info.fishName;
                    if (item.isNew)
                        ScreenReader.Say(Loc.Get("db_fish_item_new", name, idx + 1, total));
                    else
                        ScreenReader.Say(Loc.Get("db_fish_item", name, idx + 1, total));
                }

                DebugLogger.LogGameValue("CampFishPB.item",
                    $"{(isLocked ? "LOCKED" : item.fishName)} ({idx + 1}/{total})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateFishPictureBook: {ex.Message}");
                _fishPBListBase = null;
            }
        }

        /// <summary>Reads full fish details on confirm press.</summary>
        private void CheckFishDetailPress(int idx)
        {
            var gim = GameInputManager.Instance;
            if (gim == null || !gim.IsDown(GameInputManager.InputAction.Decision)) return;
            if (_fishPBListBase == null) return;

            var list = _fishPBListBase.currentDataList;
            if (list == null) return;
            int total = list.Count;
            if (idx < 0 || idx >= total) return;

            var item = list[idx].TryCast<UICampFishPictureBookListItemData>();
            if (item == null) return;

            var info = item.informationData;
            if (info == null || !info.isRelease) return;

            try
            {
                var sb = new StringBuilder();
                string name = info.fishName;
                if (!string.IsNullOrEmpty(name)) AppendSentence(sb, name);
                if (info.isRare) AppendSentence(sb, Loc.Get("db_fish_rare"));
                if (info.isCrown) AppendSentence(sb, Loc.Get("db_fish_crown"));
                if (!string.IsNullOrEmpty(info.description))
                    AppendSentence(sb, info.description);
                if (!string.IsNullOrEmpty(info.fishShadow))
                    AppendSentence(sb, Loc.Get("db_fish_shadow", info.fishShadow));
                if (!string.IsNullOrEmpty(info.livingPlace))
                    AppendSentence(sb, Loc.Get("db_fish_habitat", info.livingPlace));
                if (info.fishingCount > 0)
                    AppendSentence(sb, Loc.Get("db_fish_caught", info.fishingCount));
                if (info.maxLength > 0)
                    AppendSentence(sb, Loc.Get("db_fish_max_length", info.maxLength));

                ScreenReader.Say(sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampFishPB detail error: {ex.Message}");
            }
        }

        #endregion

        #region Location Picture Book

        /// <summary>
        /// Polls the location picture book selector. Browse: name + position.
        /// Confirm: full details. Locked: "Undiscovered".
        /// </summary>
        private void UpdateLocationPictureBook()
        {
            if (_lastRootMenuItemName != "Location") return;
            if (_locationPBSelector == null) return;

            try
            {
                if (_locationPBListBase == null)
                {
                    _locationPBListBase = _locationPBSelector.TryCast<UIListSelectorBase>();
                    if (_locationPBListBase == null) return;
                }

                bool isActive = _locationPBSelector.gameObject.activeInHierarchy;
                if (!_locationPBState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("db_location_screen")),
                    "CampLocationPB"))
                    return;

                int idx = _locationPBListBase.currentIndex;
                if (idx == _locationPBState.LastIndex)
                {
                    CheckLocationDetailPress(idx);
                    return;
                }
                _locationPBState.LastIndex = idx;

                var list = _locationPBListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UICampLocationPictureBookListItemData>();
                if (item == null) return;

                var info = item.informationData;
                bool isLocked = info == null || !info.isRelease;

                if (isLocked)
                {
                    ScreenReader.Say(Loc.Get("db_location_locked", idx + 1, total));
                }
                else
                {
                    string name = item.locationName;
                    if (string.IsNullOrEmpty(name) && info != null) name = info.locationName;
                    if (item.isNew)
                        ScreenReader.Say(Loc.Get("db_location_item_new", name, idx + 1, total));
                    else
                        ScreenReader.Say(Loc.Get("db_location_item", name, idx + 1, total));
                }

                DebugLogger.LogGameValue("CampLocationPB.item",
                    $"{(isLocked ? "LOCKED" : item.locationName)} ({idx + 1}/{total})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateLocationPictureBook: {ex.Message}");
                _locationPBListBase = null;
            }
        }

        /// <summary>Reads full location details on confirm press.</summary>
        private void CheckLocationDetailPress(int idx)
        {
            var gim = GameInputManager.Instance;
            if (gim == null || !gim.IsDown(GameInputManager.InputAction.Decision)) return;
            if (_locationPBListBase == null) return;

            var list = _locationPBListBase.currentDataList;
            if (list == null) return;
            int total = list.Count;
            if (idx < 0 || idx >= total) return;

            var item = list[idx].TryCast<UICampLocationPictureBookListItemData>();
            if (item == null) return;

            var info = item.informationData;
            if (info == null || !info.isRelease) return;

            try
            {
                var sb = new StringBuilder();
                string name = info.locationName;
                if (!string.IsNullOrEmpty(name)) AppendSentence(sb, name);
                if (!string.IsNullOrEmpty(info.discoveryName))
                    AppendSentence(sb, Loc.Get("db_location_discovered_by", info.discoveryName));
                if (!string.IsNullOrEmpty(info.description))
                    AppendSentence(sb, info.description);

                ScreenReader.Say(sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampLocationPB detail error: {ex.Message}");
            }
        }

        #endregion

        #region Player Data

        /// <summary>
        /// Virtual cursor navigation for the Player Data screen.
        /// Up/Down moves through 24 stats across 3 categories.
        /// Category names announced when crossing boundaries.
        /// </summary>
        private void UpdatePlayerData()
        {
            if (_lastRootMenuItemName != "PlayerData") return;
            if (_playerDataSelector == null || _playerDataPresenter == null) return;

            try
            {
                bool isActive = _playerDataSelector.gameObject.activeInHierarchy;
                if (!_playerDataState.CheckEntry(isActive,
                    () =>
                    {
                        // Build total from presenter lists
                        _playerDataTotal = GetPlayerDataTotal();
                        _playerDataIndex = 0;
                        _playerDataLastIndex = 0;
                        if (_playerDataTotal > 0)
                        {
                            string category = GetPlayerDataCategoryName(0);
                            string stat = GetPlayerDataStat(0);
                            ScreenReader.Say(Loc.Get("db_playerdata_category_stat",
                                Loc.Get("db_playerdata_screen"), category, stat));
                        }
                        else
                        {
                            ScreenReader.Say(Loc.Get("db_playerdata_screen"));
                        }
                    },
                    "CampPlayerData"))
                    return;

                // Refresh total each frame in case it changed
                _playerDataTotal = GetPlayerDataTotal();
                if (_playerDataTotal == 0) return;

                var gim = GameInputManager.Instance;
                if (gim == null) return;

                int newIndex = _playerDataIndex;
                if (gim.IsDown(GameInputManager.InputAction.Down))
                {
                    newIndex = Math.Min(_playerDataIndex + 1, _playerDataTotal - 1);
                }
                else if (gim.IsDown(GameInputManager.InputAction.Up))
                {
                    newIndex = Math.Max(_playerDataIndex - 1, 0);
                }

                if (newIndex == _playerDataIndex) return;

                int oldCategory = GetPlayerDataCategory(_playerDataIndex);
                int newCategory = GetPlayerDataCategory(newIndex);
                _playerDataIndex = newIndex;
                _playerDataLastIndex = newIndex;

                string statText = GetPlayerDataStat(newIndex);

                if (oldCategory != newCategory)
                {
                    string catName = GetPlayerDataCategoryName(newIndex);
                    ScreenReader.Say(Loc.Get("db_playerdata_category_stat",
                        catName, statText));
                }
                else
                {
                    ScreenReader.Say(statText);
                }

                DebugLogger.LogGameValue("CampPlayerData.item",
                    $"{statText} ({newIndex + 1}/{_playerDataTotal})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdatePlayerData: {ex.Message}");
            }
        }

        /// <summary>Returns total number of player data stats across all 3 categories.</summary>
        private static int GetPlayerDataTotal()
        {
            int total = 0;
            var battle = _playerDataPresenter.battleDataPresenterList;
            var collection = _playerDataPresenter.collectionDataPresenterList;
            var others = _playerDataPresenter.othersDataPresenterList;
            if (battle != null) total += battle.Count;
            if (collection != null) total += collection.Count;
            if (others != null) total += others.Count;
            return total;
        }

        /// <summary>Returns category index (0=Battle, 1=Collection, 2=Other) for a flat index.</summary>
        private static int GetPlayerDataCategory(int flatIndex)
        {
            int battleCount = _playerDataPresenter.battleDataPresenterList?.Count ?? 0;
            int collectionCount = _playerDataPresenter.collectionDataPresenterList?.Count ?? 0;
            if (flatIndex < battleCount) return 0;
            if (flatIndex < battleCount + collectionCount) return 1;
            return 2;
        }

        /// <summary>Returns the category name for a given flat index.</summary>
        private static string GetPlayerDataCategoryName(int flatIndex)
        {
            int cat = GetPlayerDataCategory(flatIndex);
            switch (cat)
            {
                case 0: return Loc.Get("db_playerdata_battle");
                case 1: return Loc.Get("db_playerdata_collection");
                default: return Loc.Get("db_playerdata_other");
            }
        }

        /// <summary>Returns "label: value" string for the stat at flatIndex.</summary>
        private static string GetPlayerDataStat(int flatIndex)
        {
            var item = GetPlayerDataItem(flatIndex);
            if (item == null) return "???";

            string label = item.label?.text ?? "";
            string value = item.value?.text ?? "";
            return $"{label}: {value}";
        }

        /// <summary>Returns the UICampPlayerDataItemPresenter at the given flat index.</summary>
        private static UICampPlayerDataItemPresenter GetPlayerDataItem(int flatIndex)
        {
            var battle = _playerDataPresenter.battleDataPresenterList;
            int battleCount = battle?.Count ?? 0;
            if (flatIndex < battleCount)
                return battle[flatIndex];

            flatIndex -= battleCount;
            var collection = _playerDataPresenter.collectionDataPresenterList;
            int collectionCount = collection?.Count ?? 0;
            if (flatIndex < collectionCount)
                return collection[flatIndex];

            flatIndex -= collectionCount;
            var others = _playerDataPresenter.othersDataPresenterList;
            int othersCount = others?.Count ?? 0;
            if (flatIndex < othersCount)
                return others[flatIndex];

            return null;
        }

        #endregion
    }
}
