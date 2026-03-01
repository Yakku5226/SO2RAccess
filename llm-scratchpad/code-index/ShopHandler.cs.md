# Code Index: ShopHandler.cs

## Top-Level Comments

The file's class-level XML doc (lines 10-26) describes the full feature set:
- Root menu (Buy/Sell/Cancel) polling via UIShopMenuSelector.currentIndex
- Item list browsing polling via UIShopItemListSelector.currentIndex
- Quantity selection polling via selectCount on the current item
- Heading announced when entering Buy or Sell mode
- UIShopWindow found via FindObjectOfType with lazy init and a 60-frame throttle
- All navigation is native C++ — no Harmony hooks fire; polling is the correct approach

---

## Class: ShopHandler (line 27)

Namespace: SO2RAccess

### Fields

#### Region: Fields (lines 29-61)

- `private bool _patchesApplied` (line 31)
- `private static UIShopWindow _shopWindow` (line 34)
  Note: Cached reference to the shop window found via FindObjectOfType. Null between scenes.
- `private static bool _shopOpen` (line 35)
- `private static int _findCooldown` (line 36)
  Note: Frame counter throttling FindObjectOfType calls to roughly once per second (60 frames).
- `private static UIShopMenuSelector _menuSelector` (line 39)
- `private static int _menuLastIndex` (line 40)
- `private static bool _menuWasActive` (line 41)
- `private static UIShopItemListSelector _itemListSelector` (line 44)
- `private static UIListSelectorBase _itemListBase` (line 45)
  Note: A UIListSelectorBase cast of _itemListSelector, cached on shop open for currentIndex/currentDataList access.
- `private static int _itemLastIndex` (line 46)
- `private static int _lastSelectCount` (line 49)
- `private static bool _wasSelectingCount` (line 50)
- `private static UIShopItemListSelector.State _lastItemListState` (lines 55-56)
  Note: Initialized to -1 (cast to enum) so the first real state always triggers a heading announcement.

### Properties

- `public static bool IsShopOpen` (line 59) — get-only; returns `_shopOpen`

---

### Methods

#### Region: Patches (lines 63-113)

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 70)
  Note: Despite the name, applies NO actual Harmony patches. Its only work is calling RuntimeHelpers.RunClassConstructor on six IL2CPP types to ensure they are initialized before polling begins. Sets `_patchesApplied` guard.

- `public void OnSceneChanged()` (line 97)
  Note: Resets every cached field/selector reference to null/-1/false. Must be called on scene change because UIShopWindow may not exist in every scene.

#### Region: Update Loop (lines 115-415)

- `public void Update()` (line 121)
  Note: Entry point called every frame from Main.UpdateHandlers(). Calls DetectShopWindow(), then conditionally calls the three sub-update methods.

- `private void DetectShopWindow()` (line 135)
  Note: Two-phase method. Phase 1 — if _shopWindow is null, throttles FindObjectOfType calls via _findCooldown (60 frames between tries). Phase 2 — polls IsOpened on the cached window; on open transition caches selectors, pre-seeds stale indexes, and announces "shop_screen"; on close transition resets tracking state. Swallows all exceptions and resets to null on failure.

- `private void UpdateRootMenu()` (line 223)
  Note: Polls _menuSelector.currentIndex each frame. Announces menu item name and position (x of y) via Loc.Get("shop_menu_item") on index change. Uses _menuWasActive to force re-announcement when the root menu becomes visible again after being hidden by a sub-screen.

- `private void UpdateItemList()` (line 286)
  Note: Polls _itemListBase.currentIndex each frame. Detects Buy/Sell mode changes via currentState and announces a heading ("shop_buy_heading" / "shop_sell_heading") on transition, resetting _itemLastIndex so the first item in the new mode re-announces. Silences item announcements while isSelectItemCount is true (defers to UpdateQuantitySelection). Announces item name, price (buy or sell price depending on mode), and position.

- `private void UpdateQuantitySelection()` (line 359)
  Note: Polls isSelectItemCount each frame. When true, reads selectCount from the current UIShopItemListItemData and announces quantity plus total cost (unitPrice * selectCount) via Loc.Get("shop_item_quantity") on count change. Cleans up _wasSelectingCount and _lastSelectCount when quantity mode ends.
