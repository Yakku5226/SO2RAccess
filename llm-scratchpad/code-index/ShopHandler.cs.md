# ShopHandler.cs (636 lines)

Announces shop menu navigation to the screen reader. Features: root menu (Buy/Sell/Cancel)
polled via UIShopMenuSelector.currentIndex; item list polled via UIShopItemListSelector;
quantity selection polled via selectCount on current item. All navigation is native C++ —
no Harmony hooks fire; polling is the correct approach. UIShopWindow found via
FindObjectOfType (lazy, throttled). Item descriptions come from UIItemInformationPresenter.Set
hook via CacheItemInfo() because the game does not populate itemDescription on shop list data.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class ShopHandler (line 33)
Announces shop menu navigation to the screen reader.

fields/properties (declaration order):
- _patchesApplied : bool (line 37)
- _shopWindow : static UIShopWindow (line 40)  — cached permanently per scene via FindObjectOfType
- _shopOpen : static bool (line 41)
- _findCooldown : static int (line 42)  — throttles FindObjectOfType to ~1 search per 60 frames
- _menuSelector : static UIShopMenuSelector (line 45)  — root Buy/Sell/Cancel/etc. selector
- _menuLastIndex : static int (line 46)
- _menuWasActive : static bool (line 47)
- _itemListSelector : static UIShopItemListSelector (line 50)
- _itemListBase : static UIListSelectorBase (line 51)  — TryCast of _itemListSelector
- _itemLastIndex : static int (line 52)
- _lastSelectCount : static int (line 55)  — last seen quantity during count selection
- _wasSelectingCount : static bool (line 56)
- _cachedItemInfo : static string (line 60)  — cached from UIItemInformationPresenter.Set hook
- _cachedItemEffect : static string (line 61)  — cached from UIItemInformationPresenter.Set hook
- _lastItemListState : static UIShopItemListSelector.State (line 66)  — tracks Buy/Sell for heading announcements on mode change
- IsShopOpen : static bool { get } => _shopOpen (line 69)
- CacheItemInfo(string info, string effect) : static void (line 76)
  - note: called from UIItemInformationPresenter.Set hook; stores description and effect text for the currently highlighted shop item; used by BuildItemDetails

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 91)
  - note: no Harmony hooks patched; only calls RuntimeHelpers.RunClassConstructor on relevant IL2CPP types to ensure reflection works at runtime
- void OnSceneChanged() (line 121)
  - note: resets all cached state — window ref, selectors, indices, counts, item info cache; call on scene transition
- void Update() (line 147)
  - note: called every frame from Main.UpdateHandlers(); calls DetectShopWindow, then UpdateRootMenu, UpdateItemList, UpdateQuantitySelection if shop is open
- void DetectShopWindow() (line 161)
  - note: lazy-finds UIShopWindow with 60-frame cooldown; polls IsOpened to detect open/close transitions; on open, caches selectors, pre-seeds _itemLastIndex to suppress stale announcement, announces "shop_screen"; on close, resets state
- void UpdateRootMenu() (line 253)
  - note: polls _menuSelector.currentIndex; detects activation to force re-announcement; announces menu item name and position via "shop_menu_item" loc key
- void UpdateItemList() (line 316)
  - note: detects Buy/Sell state change and announces heading; skips while isSelectItemCount; polls currentIndex, reads UIShopItemListItemData and UIItemListItemData, calls BuildItemDetails; announces buy or sell info with name, price, details, position; item list is permanently activeInHierarchy so stale-seed on open prevents spurious first announcement
- void UpdateQuantitySelection() (line 391)
  - note: polls isSelectItemCount; on entry resets _lastSelectCount; polls shopItem.selectCount and announces quantity and total cost (unit price * count) on change via "shop_item_quantity" loc key
- string BuildItemDetails(UIItemListItemData, UIShopItemListItemData) (line 457)
  - note: assembles detail string: equipment category name, equipment stats (BuildEquipmentStats), cached info/effect from hook, factor description (GetFactorDescription); joins non-empty parts with ". "; returns "" if nothing available
- string BuildEquipmentStats(int itemID) (line 513)
  - note: reads ConstItemParameter from ParameterManager; formats only non-zero stats (ATK, DEF, INT, STM, LCK, POW, GUTS, HIT, EVD, CRT) as e.g. "ATK +10, DEF +5"
- string GetFactorDescription(FactorID factorID) (line 555)
  - note: looks up ConstFactorParameter via ParameterManager, resolves messageID via GetFactorMessage; returns null for INVALID factorID or empty description
- static bool IsEquipmentCategory(ItemCategoryType cat) (line 583)
  - note: returns true for weapon and armor categories (SWORD, TWIN_SWORD, WAND, KNUCKLE, PUNCH, BOOK, WHIP, GUN_AND_DISK, STUNGUN, ROD, HELMET, SHIELD, ARMOR, GREEVE, ACCESSORY)
- static string GetCategoryName(ItemCategoryType cat) (line 611)
  - note: switch on ItemCategoryType returning Loc.Get("shop_cat_*") localization key for each equipment category; returns null for non-equipment
