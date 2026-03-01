using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
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
    }
}
