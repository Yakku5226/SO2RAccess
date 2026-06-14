using Il2CppGame;
using MelonLoader;
using System;
using System.Text;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        // Item sub-screen
        // UICampItemSelector wraps UICampItemListSelector (field itemListSelector).
        // currentIndex/currentDataList live on UIListSelectorBase — need a cast.
        private static UICampItemSelector _itemSelector = null;
        private static UIListSelectorBase _itemListSelectorBase = null;
        private static readonly SubScreenState _itemState = new SubScreenState();

        // Cached from UIItemInformationPresenter.Set hook — contains the effect text
        // ("Restores 30% HP") and factor info that UIItemListItemData doesn't carry.
        private static string _itemCachedEffect = "";
        private static string _itemCachedFactorName = "";
        private static string _itemCachedFactorInfo = "";

        /// <summary>
        /// Polls the UICampItemSelector and announces item name, quantity, effect,
        /// description, factor info, and position.
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
            if (_lastRootMenuItemName != "Item") return;

            try
            {
                bool isActive = _itemSelector.gameObject.activeInHierarchy;

                bool shouldPoll = _itemState.CheckEntry(
                    isActive,
                    () => ScreenReader.Say(Loc.Get("camp_item_screen")),
                    "CampItem",
                    onHidden: () => { _itemListSelectorBase = null; });

                if (!shouldPoll)
                {
                    // On genuine first entry, cache the inner list selector.
                    if (_itemState.WasActive && _itemListSelectorBase == null)
                    {
                        var inner = _itemSelector.itemListSelector;
                        _itemListSelectorBase = inner?.TryCast<UIListSelectorBase>();

                        if (_itemListSelectorBase != null)
                            DebugLogger.LogState("CampItem: inner list selector cached.");
                        else
                            MelonLogger.Warning("[CAMP] itemListSelector cast to UIListSelectorBase failed.");
                    }
                    return;
                }

                if (_itemListSelectorBase == null) return;

                int idx = _itemListSelectorBase.currentIndex;
                if (idx == _itemState.LastIndex) return;
                _itemState.LastIndex = idx;

                var list = _itemListSelectorBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIItemListItemData>();
                if (item == null) return;

                string name = item.itemName ?? "";
                int count = item.itemCount;
                string description = item.itemDescription ?? "";

                // Effect, factor name, and factor info come from the
                // UIItemInformationPresenter.Set hook (cached per navigation).
                string effect = _itemCachedEffect;
                string factorName = _itemCachedFactorName;
                string factorInfo = _itemCachedFactorInfo;

                DebugLogger.LogGameValue("CampItem.item",
                    $"{name} x{count} ({idx + 1}/{total}): effect='{effect}' desc='{description}' factor='{factorName}'");

                // Build announcement: Name x[count]. Effect. Description. Factor. Position.
                var sb = new StringBuilder();
                sb.Append(name).Append(" x").Append(count).Append(". ");

                if (!string.IsNullOrEmpty(effect))
                    AppendSentence(sb, effect);
                if (!string.IsNullOrEmpty(description))
                    AppendSentence(sb, description);
                if (!string.IsNullOrEmpty(factorName))
                    sb.Append(Loc.Get("camp_item_factor", factorName)).Append(". ");
                if (!string.IsNullOrEmpty(factorInfo))
                    AppendSentence(sb, factorInfo);

                sb.Append(Loc.Get("camp_item_position", idx + 1, total));

                ScreenReader.Say(sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateItemSelector: {ex.Message}");
                _itemSelector = null;
                _itemListSelectorBase = null;
                _itemState.Reset();
            }
        }
    }
}
