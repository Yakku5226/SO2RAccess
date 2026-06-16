using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces shop menu navigation to the screen reader.
    ///
    /// Features:
    ///   - Root menu (Buy/Sell/Cancel): polled via UIShopMenuSelector.currentIndex.
    ///   - Item list browsing: polled via UIShopItemListSelector currentIndex.
    ///     Announces item name, buy/sell price, description, and position.
    ///   - Quantity selection: polls selectCount on current item when isSelectItemCount
    ///     is true. Announces quantity and total cost on change.
    ///   - Heading announced when entering item list ("Buy." or "Sell.").
    ///
    /// Item descriptions come from the UIItemInformationPresenter.Set hook (shared
    /// with CampMenuHandler) — the game does NOT populate itemDescription on the
    /// shop list data objects. CacheItemInfo() receives description/effect text
    /// from the hook each time the info panel updates.
    ///
    /// Detection: UIShopWindow found via FindObjectOfType (lazy init with throttle),
    /// IsOpened polled each frame to detect open/close transitions.
    ///
    /// All navigation in this menu is native C++ — no Harmony hooks fire for
    /// up/down/left/right. Polling is the correct approach (same as camp menu).
    /// </summary>
    public class ShopHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Shop window — found via FindObjectOfType, cached permanently per scene.
        private static UIShopWindow _shopWindow = null;
        private static bool _shopOpen = false;
        private static int _findCooldown = 0;

        // Root menu (Buy/Sell/Cancel/SuperAppraisal/OpenStore)
        private static UIShopMenuSelector _menuSelector = null;
        private static int _menuLastIndex = -1;
        private static bool _menuWasActive = false;

        // Item list
        private static UIShopItemListSelector _itemListSelector = null;
        private static UIListSelectorBase _itemListBase = null;
        private static int _itemLastIndex = -1;

        // Quantity tracking
        private static int _lastSelectCount = -1;
        private static bool _wasSelectingCount = false;

        // Cached from UIItemInformationPresenter.Set hook — the game populates
        // descriptions here rather than on UIItemListItemData for shop items.
        private static string _cachedItemInfo = "";
        private static string _cachedItemEffect = "";

        // Tracks the item list state (Buy/Sell) for heading announcements on mode change.
        // The item list is permanently active, so we announce headings on state transitions
        // instead of on first activation.
        private static UIShopItemListSelector.State _lastItemListState =
            (UIShopItemListSelector.State)(-1);

        /// <summary>True while a shop window is open.</summary>
        public static bool IsShopOpen => _shopOpen;

        /// <summary>
        /// Called from UIItemInformationPresenter.Set hook to cache item
        /// description and effect text for the current shop item.
        /// </summary>
        public static void CacheItemInfo(string info, string effect)
        {
            _cachedItemInfo = info ?? "";
            _cachedItemEffect = effect ?? "";
        }

        #endregion

        #region Patches

        /// <summary>
        /// Applies Harmony patches for the shop system.
        /// Currently no hooks are needed — all announcements are polling-based.
        /// IL2CPP type constructors are initialized to ensure reflection works.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                // Ensure IL2CPP types are initialized for runtime access.
                RuntimeHelpers.RunClassConstructor(typeof(UIShopWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIShopMenuSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIShopMenuListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIShopItemListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIShopItemListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstItemParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstFactorParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstFactorMessage).TypeHandle);

                _patchesApplied = true;
                MelonLogger.Msg("[SHOP] Patches applied (polling-only).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SHOP] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Called on scene change to reset cached references.
        /// The UIShopWindow may not exist in every scene.
        /// </summary>
        public void OnSceneChanged()
        {
            _shopWindow = null;
            _shopOpen = false;
            _findCooldown = 0;
            _menuSelector = null;
            _menuLastIndex = -1;
            _menuWasActive = false;
            _itemListSelector = null;
            _itemListBase = null;
            _itemLastIndex = -1;
            _lastSelectCount = -1;
            _wasSelectingCount = false;
            _lastItemListState = (UIShopItemListSelector.State)(-1);
            _cachedItemInfo = "";
            _cachedItemEffect = "";
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Detects shop open/close and polls all sub-selectors.
        /// </summary>
        public void Update()
        {
            DetectShopWindow();
            if (!_shopOpen) return;

            UpdateRootMenu();
            UpdateItemList();
            UpdateQuantitySelection();
        }

        /// <summary>
        /// Finds UIShopWindow lazily (throttled to ~1 search per second) and polls
        /// IsOpened to detect shop open/close transitions.
        /// </summary>
        private void DetectShopWindow()
        {
            // Try to find UIShopWindow if we don't have a reference.
            if (_shopWindow == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _shopWindow = UnityEngine.Object.FindObjectOfType<UIShopWindow>();
                    if (_shopWindow != null)
                    {
                        DebugLogger.LogState("Shop: cached UIShopWindow reference.");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"Shop detection error: {ex.Message}");
                }
                return;
            }

            // Poll IsOpened on the cached window.
            try
            {
                bool isOpen = _shopWindow.IsOpened;

                if (isOpen && !_shopOpen)
                {
                    // Shop just opened — cache selectors.
                    _shopOpen = true;
                    _menuSelector = _shopWindow.menuSelector;
                    _itemListSelector = _shopWindow.itemListSelector;
                    _menuLastIndex = -1;
                    _menuWasActive = false;
                    _itemLastIndex = -1;
                    _lastSelectCount = -1;
                    _wasSelectingCount = false;

                    // Cache the UIListSelectorBase cast for the item list.
                    _itemListBase = _itemListSelector?.TryCast<UIListSelectorBase>();

                    // The item list is permanently activeInHierarchy — it never goes
                    // inactive. Pre-seed state so no stale announcement fires on open.
                    // Items will announce when the user genuinely navigates (index change).
                    if (_itemListBase != null)
                    {
                        _itemLastIndex = _itemListBase.currentIndex;
                        DebugLogger.LogState($"Shop: stale-seed itemIdx={_itemLastIndex}.");
                    }

                    // Reset info cache so stale descriptions don't carry over.
                    _cachedItemInfo = "";
                    _cachedItemEffect = "";

                    // Track item list state for heading announcements on Buy/Sell switch.
                    try
                    {
                        _lastItemListState = _itemListSelector.currentState;
                        DebugLogger.LogState($"Shop: initial item state={_lastItemListState}.");
                    }
                    catch { }

                    ScreenReader.Say(Loc.Get("shop_screen"));
                    DebugLogger.LogState("Shop: opened.");
                }
                else if (!isOpen && _shopOpen)
                {
                    // Shop just closed.
                    _shopOpen = false;
                    _menuLastIndex = -1;
                    _menuWasActive = false;
                    _itemLastIndex = -1;
                    _lastSelectCount = -1;
                    _wasSelectingCount = false;
                    DebugLogger.LogState("Shop: closed.");
                }
            }
            catch
            {
                // Window reference broke — reset and re-find.
                _shopWindow = null;
                _shopOpen = false;
                _findCooldown = 0;
            }
        }

        /// <summary>
        /// Polls the UIShopMenuSelector and announces the focused menu item
        /// (Buy, Sell, Cancel, etc.) with position.
        /// </summary>
        private void UpdateRootMenu()
        {
            if (_menuSelector == null) return;

            try
            {
                bool isActive = _menuSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_menuWasActive)
                    {
                        _menuWasActive = false;
                        DebugLogger.LogState("Shop: root menu hidden.");
                    }
                    return;
                }

                // Menu just became visible — force re-announcement.
                if (!_menuWasActive)
                {
                    _menuWasActive = true;
                    _menuLastIndex = -1;
        
                    DebugLogger.LogState("Shop: root menu visible.");
                }

                int idx = _menuSelector.currentIndex;
                if (idx == _menuLastIndex) return;
                _menuLastIndex = idx;

                var list = _menuSelector.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIShopMenuListItemData>();
                if (item == null) return;

                string name = item.menuName ?? "";

                DebugLogger.LogGameValue("Shop.menu",
                    $"{name} ({idx + 1}/{total})");

                ScreenReader.Say(Loc.Get("shop_menu_item", name, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ShopHandler.UpdateRootMenu: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the UIShopItemListSelector and announces the focused item
        /// with name, price, and position.
        ///
        /// The item list is permanently activeInHierarchy (never goes inactive), so we
        /// cannot use activation transitions to detect sub-screen entry. Instead:
        ///   - On shop open, index and state are pre-seeded (stale-seed) to prevent
        ///     announcing the stale first item.
        ///   - Headings ("Buy."/"Sell.") are announced on state CHANGE, not on first activation.
        ///   - Items announce on genuine index change (user navigation).
        /// </summary>
        private void UpdateItemList()
        {
            if (_itemListSelector == null || _itemListBase == null) return;

            try
            {
                // Announce heading when the item list mode changes (Buy↔Sell).
                // This replaces the old activation-based heading which fired on stale state.
                var currentState = _itemListSelector.currentState;
                if (currentState != _lastItemListState)
                {
                    _lastItemListState = currentState;
                    bool isBuying = currentState == UIShopItemListSelector.State.Buy
                                 || currentState == UIShopItemListSelector.State.PublishingBuy;
                    string heading = isBuying
                        ? Loc.Get("shop_buy_heading")
                        : Loc.Get("shop_sell_heading");
                    ScreenReader.Say(heading);

                    // Reset index so the first item in the new mode is announced.
                    _itemLastIndex = -1;

                }

                // Don't announce items while in quantity selection mode — that's
                // handled by UpdateQuantitySelection().
                if (_itemListSelector.isSelectItemCount) return;

                int idx = _itemListBase.currentIndex;
                if (idx == _itemLastIndex) return;
                _itemLastIndex = idx;

                var list = _itemListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var rawItem = list[idx];
                var shopItem = rawItem?.TryCast<UIShopItemListItemData>();
                if (shopItem == null) return;

                var itemData = shopItem.TryCast<UIItemListItemData>();
                string name = itemData?.itemName ?? "";
                int haveCount = shopItem.haveCount;

                var state2 = _itemListSelector.currentState;
                bool buying = state2 == UIShopItemListSelector.State.Buy
                           || state2 == UIShopItemListSelector.State.PublishingBuy;

                int price = buying ? shopItem.buyPrice : shopItem.sellPrice;
                string details = BuildItemDetails(itemData, shopItem);

                DebugLogger.LogGameValue("Shop.item",
                    $"'{name}' price={price} have={haveCount} buy={buying} ({idx + 1}/{total}) details='{details}'");

                if (buying)
                {
                    ScreenReader.Say(Loc.Get("shop_item_buy", name, price, details, idx + 1, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("shop_item_sell", name, price, haveCount, details, idx + 1, total));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ShopHandler.UpdateItemList: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the selectCount on the current item when the shop is in quantity
        /// selection mode (isSelectItemCount == true). Announces quantity and total
        /// cost when the count changes.
        /// </summary>
        private void UpdateQuantitySelection()
        {
            if (_itemListSelector == null || _itemListBase == null) return;

            try
            {
                bool selecting = _itemListSelector.isSelectItemCount;

                if (!selecting)
                {
                    if (_wasSelectingCount)
                    {
                        _wasSelectingCount = false;
                        _lastSelectCount = -1;
                        DebugLogger.LogState("Shop: quantity selection ended.");
                    }
                    return;
                }

                // Just entered quantity mode — announce current item with count context.
                if (!_wasSelectingCount)
                {
                    _wasSelectingCount = true;
                    _lastSelectCount = -1;
                    DebugLogger.LogState("Shop: entered quantity selection.");
                }

                // Read current item data.
                int idx = _itemListBase.currentIndex;
                var list = _itemListBase.currentDataList;
                if (list == null || idx < 0 || idx >= list.Count) return;

                var shopItem = list[idx]?.TryCast<UIShopItemListItemData>();
                if (shopItem == null) return;

                int selectCount = shopItem.selectCount;
                if (selectCount == _lastSelectCount) return;
                _lastSelectCount = selectCount;

                var state = _itemListSelector.currentState;
                bool buying = state == UIShopItemListSelector.State.Buy
                           || state == UIShopItemListSelector.State.PublishingBuy;
                int unitPrice = buying ? shopItem.buyPrice : shopItem.sellPrice;
                int totalCost = unitPrice * selectCount;

                DebugLogger.LogGameValue("Shop.quantity",
                    $"count={selectCount} unitPrice={unitPrice} total={totalCost}");

                ScreenReader.Say(Loc.Get("shop_item_quantity", selectCount, totalCost));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ShopHandler.UpdateQuantitySelection: {ex.Message}");
            }
        }

        #endregion

        #region Item Details

        /// <summary>
        /// Builds a details string for a shop item containing description,
        /// equipment category, stats, and factor effects. Only non-zero stats
        /// are included. Returns empty string if no details available.
        /// The returned string ends with ". " if non-empty, ready for inline use.
        /// </summary>
        private string BuildItemDetails(UIItemListItemData itemData, UIShopItemListItemData shopItem)
        {
            if (itemData == null) return "";

            var parts = new List<string>();

            try
            {
                var category = shopItem.itemCategoryType;
                bool isEquipment = IsEquipmentCategory(category);

                // Equipment category name (e.g. "Sword", "Greave")
                if (isEquipment)
                {
                    string catName = GetCategoryName(category);
                    if (!string.IsNullOrEmpty(catName))
                        parts.Add(catName);
                }

                // Equipment stats from ConstItemParameter
                if (isEquipment)
                {
                    string stats = BuildEquipmentStats(itemData.itemID);
                    if (!string.IsNullOrEmpty(stats))
                        parts.Add(stats);
                }

                // Item description — from the UIItemInformationPresenter.Set hook
                // cache, since the game doesn't populate itemDescription on shop
                // list data for non-equipment items.
                if (!string.IsNullOrEmpty(_cachedItemInfo))
                    parts.Add(_cachedItemInfo);
                if (!string.IsNullOrEmpty(_cachedItemEffect))
                    parts.Add(_cachedItemEffect);

                // Factor effect description for equipment
                if (isEquipment)
                {
                    string factorDesc = GetFactorDescription(itemData.factorID);
                    if (!string.IsNullOrEmpty(factorDesc))
                        parts.Add(factorDesc);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Shop: BuildItemDetails error: {ex.Message}");
            }

            // Join with clean sentence separators so a description that already ends
            // in "." (e.g. "...straight blade.") doesn't produce a double period.
            string joined = TextUtil.JoinSentences(parts);
            return joined.Length == 0 ? "" : joined + ". ";
        }

        /// <summary>
        /// Reads stats from ConstItemParameter and returns a string of only
        /// non-zero stats, e.g. "ATK +10, DEF +5, CRT +3%".
        /// </summary>
        private string BuildEquipmentStats(int itemID)
        {
            try
            {
                var pm = ParameterManager.Instance;
                if (pm == null) return "";

                var param = pm.GetItemParameter(itemID);
                if (param == null) return "";

                var stats = new List<string>();

                void AddStat(string label, int value)
                {
                    if (value != 0)
                        stats.Add($"{label} {value:+#;-#}");
                }

                AddStat("ATK", param.Attack);
                AddStat("DEF", param.Defense);
                AddStat("INT", param.Intelligence);
                AddStat("STM", param.Stamina);
                AddStat("LCK", param.Luck);
                AddStat("POW", param.Power);
                AddStat("GUTS", param.Guts);
                AddStat("HIT", param.HitRate);
                AddStat("EVD", param.EvasionRate);
                AddStat("CRT", param.CriticalRate);

                return string.Join(", ", stats);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Shop: BuildEquipmentStats error: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Gets the factor effect description for equipment with a factor.
        /// Returns null if the item has no factor or the description is empty.
        /// </summary>
        private string GetFactorDescription(FactorID factorID)
        {
            if (factorID == FactorID.INVALID) return null;

            try
            {
                var pm = ParameterManager.Instance;
                if (pm == null) return null;

                var factorParam = pm.GetFactorParameter(factorID);
                if (factorParam == null) return null;

                string messageID = factorParam.messageID;
                if (string.IsNullOrEmpty(messageID)) return null;

                string desc = pm.GetFactorMessage(messageID);
                return string.IsNullOrEmpty(desc) ? null : desc;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Shop: GetFactorDescription error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns true if the category represents equipment (weapons or armor).
        /// </summary>
        private static bool IsEquipmentCategory(ItemCategoryType cat)
        {
            switch (cat)
            {
                case ItemCategoryType.SWORD:
                case ItemCategoryType.TWIN_SWORD:
                case ItemCategoryType.WAND:
                case ItemCategoryType.KNUCKLE:
                case ItemCategoryType.PUNCH:
                case ItemCategoryType.BOOK:
                case ItemCategoryType.WHIP:
                case ItemCategoryType.GUN_AND_DISK:
                case ItemCategoryType.STUNGUN:
                case ItemCategoryType.ROD:
                case ItemCategoryType.HELMET:
                case ItemCategoryType.SHIELD:
                case ItemCategoryType.ARMOR:
                case ItemCategoryType.GREEVE:
                case ItemCategoryType.ACCESSORY:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the localized display name for an equipment category.
        /// </summary>
        private static string GetCategoryName(ItemCategoryType cat)
        {
            switch (cat)
            {
                case ItemCategoryType.SWORD:        return Loc.Get("shop_cat_sword");
                case ItemCategoryType.TWIN_SWORD:   return Loc.Get("shop_cat_twin_sword");
                case ItemCategoryType.WAND:         return Loc.Get("shop_cat_wand");
                case ItemCategoryType.KNUCKLE:      return Loc.Get("shop_cat_knuckle");
                case ItemCategoryType.PUNCH:        return Loc.Get("shop_cat_punch");
                case ItemCategoryType.BOOK:         return Loc.Get("shop_cat_book");
                case ItemCategoryType.WHIP:         return Loc.Get("shop_cat_whip");
                case ItemCategoryType.GUN_AND_DISK: return Loc.Get("shop_cat_gun_disk");
                case ItemCategoryType.STUNGUN:      return Loc.Get("shop_cat_stungun");
                case ItemCategoryType.ROD:          return Loc.Get("shop_cat_rod");
                case ItemCategoryType.HELMET:       return Loc.Get("shop_cat_helmet");
                case ItemCategoryType.SHIELD:       return Loc.Get("shop_cat_shield");
                case ItemCategoryType.ARMOR:        return Loc.Get("shop_cat_armor");
                case ItemCategoryType.GREEVE:       return Loc.Get("shop_cat_greave");
                case ItemCategoryType.ACCESSORY:    return Loc.Get("shop_cat_accessory");
                default: return null;
            }
        }

        #endregion
    }
}
