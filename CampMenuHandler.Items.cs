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

        // Logs the category tab table once per visit to the Items screen, so the mapping
        // from Category enum to on-screen name stays checkable without a code change.
        private static bool _itemTabTableLogged = false;

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
                    onHidden: () =>
                    {
                        _itemListSelectorBase = null;
                        _itemCategoryTab.Reset();
                        _itemTabTableLogged = false;
                    });

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

                PollItemCategory();

                var list = _itemListSelectorBase.currentDataList;
                // A null list means the game is between lists, not that the category is
                // empty — wait for the rebuilt one rather than calling it empty. Any
                // parked category label survives; TickTabSwitchAnnouncers is the backstop.
                if (list == null) return;
                int total = list.Count;

                // A category the party owns nothing from has no row to read, so the
                // category announces itself. Handled before the index check because an
                // empty list leaves currentIndex parked and nothing else would speak.
                if (total == 0)
                {
                    string emptyCategory = _itemCategoryTab.Take();
                    if (emptyCategory != null)
                    {
                        ScreenReader.Say(Loc.Get("camp_item_category_empty", emptyCategory));
                        _itemState.LastIndex = -1;
                    }
                    return;
                }

                int idx = _itemListSelectorBase.currentIndex;
                if (idx == _itemState.LastIndex) return;
                _itemState.LastIndex = idx;

                if (idx < 0 || idx >= total) return;

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

                // Prefixes the category name when this row follows an L1/R1 tab switch.
                ScreenReader.Say(_itemCategoryTab.Decorate(sb.ToString().Trim()));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateItemSelector: {ex.Message}");
                _itemSelector = null;
                _itemListSelectorBase = null;
                _itemState.Reset();
                _itemCategoryTab.Reset();
            }
        }

        /// <summary>
        /// Detects an L1/R1 category switch on the item list. The category is the whole
        /// content of the list, so a switch forces the row under the cursor to be read
        /// again — it is a different item now, even when the cursor has not moved.
        /// The category enum is the trigger; the spoken name comes from the game's own
        /// tab label (see <see cref="ResolveItemCategoryName"/>).
        /// </summary>
        private void PollItemCategory()
        {
            try
            {
                var listSel = _itemSelector?.itemListSelector;
                if (listSel == null) return;

                LogItemTabTableOnce(listSel);

                var category = listSel.currentCategory;
                if (!_itemCategoryTab.HasChanged((int)category)) return;

                _itemCategoryTab.Park(ResolveItemCategoryName(listSel, category));
                _itemState.LastIndex = -1; // the list changed under the cursor
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampItem: category poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// The localized name of the category now showing, read out of the tab strip's
        /// own data table — <c>itemTabPresenter.itemTabDataList</c>, one entry per
        /// Category enum value with an isDisplay flag for the ones this save hides.
        /// The table is static, so the name always belongs to the category the poll just
        /// detected.
        ///
        /// DO NOT read <c>itemTabLabel.currentText</c> here. It is a
        /// UICommonSelectTextPresenter mid-animation and holds the PREVIOUS category —
        /// log 26-9-2_20-17-0 announced ten switches, every one of them the name of the
        /// tab the user had just left (proven by pairing each announced name against the
        /// item list that came with it: "Weapons" arrived with Tuna Sashimi, "Armor" with
        /// a Longsword, and the very first switch had no previous name to give at all,
        /// which is where the stray "Category changed." came from).
        ///
        /// Falls back to a generic line, logging why, rather than letting a switch pass
        /// in silence or naming the wrong tab.
        /// </summary>
        private static string ResolveItemCategoryName(
            UICampItemListSelector listSel, UIItemListSelectorBase.Category category)
        {
            try
            {
                var tabs = listSel?.itemTabPresenter?.itemTabDataList;
                int idx = (int)category;

                if (tabs != null && idx >= 0 && idx < tabs.Count)
                {
                    string name = StripTags(tabs[idx]?.tabName ?? "").Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        DebugLogger.LogGameValue("CampItem.category",
                            $"{category} (index {idx}) = '{name}'");
                        return name;
                    }

                    DebugLogger.LogState($"CampItem: tab table entry {idx} ({category}) has no name.");
                }
                else
                {
                    DebugLogger.LogState(
                        $"CampItem: tab table holds {tabs?.Count ?? -1} entries, no index {idx} " +
                        $"for {category} — the table is not indexed by the Category enum.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampItem: tab table read failed: {ex.Message}");
            }

            return Loc.Get("camp_item_category_unknown");
        }

        /// <summary>
        /// Dumps the category tab table on the first poll of each visit to the Items
        /// screen: index, name, and whether the tab is shown. One block per visit, and
        /// only in debug mode — it is what proves the table lines up with the Category
        /// enum, and what to look at first if a category is ever named wrongly.
        /// </summary>
        private static void LogItemTabTableOnce(UICampItemListSelector listSel)
        {
            if (_itemTabTableLogged) return;

            try
            {
                var tabs = listSel?.itemTabPresenter?.itemTabDataList;

                // The table is still empty on the first poll after the screen opens, so
                // do not spend the one dump on it — wait for the game to fill it in.
                // (Log 26-9-2_20-33-27 printed no table at all for exactly this reason.)
                if (tabs == null || tabs.Count == 0) return;

                _itemTabTableLogged = true;

                for (int i = 0; i < tabs.Count; i++)
                {
                    var tab = tabs[i];
                    DebugLogger.LogGameValue("CampItem.tabTable",
                        $"[{i}] {(UIItemListSelectorBase.Category)i} = '{tab?.tabName ?? ""}' " +
                        $"display={tab?.isDisplay} selectable={tab?.canSelected}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampItem: tab table dump failed: {ex.Message}");
            }
        }
    }
}
