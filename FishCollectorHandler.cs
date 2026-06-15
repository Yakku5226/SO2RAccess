using Il2CppGame;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the Fish Collector ("Reel") trade menus in towns. The flow is a top
    /// menu (UIFishCollectorMenuSelector, options held as UICommonListItemData) →
    /// either the exchange list (UIFishCollectorExchangeSelector) where fish are traded
    /// for items, or the check-reward list (UIFishCollectorCheckRewardSelector) of
    /// milestones; the exchange path can route through a select-fish list
    /// (UIFishCollectorSelectFishSelector). All extend UIListSelectorBase and expose
    /// their item data through managed fields, so they are polled like the camp menus
    /// (navigation is native-only). Each screen announces a heading + current item on
    /// entry and the item on cursor movement.
    /// </summary>
    public class FishCollectorHandler
    {
        private UIFishCollectorMenuSelector _menu;
        private UIFishCollectorSelectFishSelector _selectFish;
        private UIFishCollectorExchangeSelector _exchange;
        private UIFishCollectorCheckRewardSelector _reward;

        private float _menuFind, _selectFishFind, _exchangeFind, _rewardFind;
        private bool _menuActive, _selectFishActive, _exchangeActive, _rewardActive;
        private int _menuLast = -1, _selectFishLast = -1, _exchangeLast = -1, _rewardLast = -1;
        // Exchange also re-announces when the trade quantity changes at the same row.
        private int _exchangeLastCount = -1;

        public void Update()
        {
            PollMenu();
            PollSelectFish();
            PollExchange();
            PollReward();
        }

        /// <summary>Resets all tracking on map/scene change (overlays can linger active).</summary>
        public void OnSceneChanged()
        {
            _menu = null; _selectFish = null; _exchange = null; _reward = null;
            _menuFind = _selectFishFind = _exchangeFind = _rewardFind = 0f;
            _menuActive = _selectFishActive = _exchangeActive = _rewardActive = false;
            _menuLast = _selectFishLast = _exchangeLast = _rewardLast = -1;
            _exchangeLastCount = -1;
        }

        private void PollMenu()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _menu, ref _menuFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { if (_menuActive) { _menuActive = false; _menuLast = -1; } return; }
            try
            {
                int idx = _menu.currentIndex;
                var list = _menu.currentDataList;
                int count = list?.Count ?? 0;

                bool entering = !_menuActive;
                if (entering) { _menuActive = true; _menuLast = idx; }
                else if (idx == _menuLast) return;
                else _menuLast = idx;

                string item = "";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx].TryCast<UICommonListItemData>();
                    if (it != null)
                    {
                        var sb = new StringBuilder();
                        sb.Append(it.text ?? "");
                        TextUtil.AppendPosition(sb, idx, count);
                        item = sb.ToString();
                    }
                }
                Announce(entering, "collector_menu_heading", item);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollector menu: {ex.Message}"); }
        }

        private void PollSelectFish()
        {
            // Rows live in itemDataList (a typed list), NOT the base currentDataList,
            // which stays empty on this screen.
            bool active = UiFinder.TryGetActiveOverlay(ref _selectFish, ref _selectFishFind,
                s => s.gameObject?.activeInHierarchy == true && s.itemDataList?.Count > 0);
            if (!active) { if (_selectFishActive) { _selectFishActive = false; _selectFishLast = -1; } return; }
            try
            {
                int idx = _selectFish.currentIndex;
                var list = _selectFish.itemDataList;
                int count = list?.Count ?? 0;

                bool entering = !_selectFishActive;
                if (entering) { _selectFishActive = true; _selectFishLast = idx; }
                else if (idx == _selectFishLast) return;
                else _selectFishLast = idx;

                string item = "";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx];
                    if (it != null)
                    {
                        var sb = new StringBuilder();
                        sb.Append(it.itemName ?? "");
                        if (!string.IsNullOrEmpty(it.fishSize)) sb.Append(", ").Append(it.fishSize);
                        sb.Append(". ").Append(Loc.Get("collector_have", it.haveCount));
                        TextUtil.AppendPosition(sb, idx, count);
                        item = sb.ToString();
                    }
                }
                Announce(entering, "collector_selectfish_heading", item);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollector selectFish: {ex.Message}"); }
        }

        private void PollExchange()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _exchange, ref _exchangeFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active)
            {
                if (_exchangeActive) { _exchangeActive = false; _exchangeLast = -1; _exchangeLastCount = -1; }
                return;
            }
            try
            {
                int idx = _exchange.currentIndex;
                var list = _exchange.currentDataList;
                int count = list?.Count ?? 0;

                var it = (count > 0 && idx >= 0 && idx < count)
                    ? list[idx].TryCast<UIFishCollectorExchangeListItemData>() : null;
                int exchangeCount = it?.exchangeCount ?? 0;

                // Re-announce on row change OR when the trade quantity at the same row changes.
                bool entering = !_exchangeActive;
                if (entering) { _exchangeActive = true; }
                else if (idx == _exchangeLast && exchangeCount == _exchangeLastCount) return;
                _exchangeLast = idx;
                _exchangeLastCount = exchangeCount;

                string item = "";
                if (it != null)
                {
                    var sb = new StringBuilder();
                    // Reward name (and its display size, if any).
                    sb.Append(it.itemName ?? "");
                    if (!string.IsNullOrEmpty(it.fishSize)) sb.Append(", ").Append(it.fishSize);
                    sb.Append(". ");

                    // How many of the REWARD item you already own (haveCount), max-stock flag.
                    sb.Append(Loc.Get("collector_owned", it.haveCount));
                    bool max = false;
                    try { max = it.IsMaxStock(); }
                    catch (Exception ex) { DebugLogger.LogState($"FishCollector IsMaxStock: {ex.Message}"); }
                    if (max) sb.Append(" ").Append(Loc.Get("collector_owned_max"));

                    // Trade requirement: the qualifying fish with the game's own resolved
                    // names and per-fish have/need counts, read from the condition panel.
                    string req = BuildRequirementFromConditions(it);
                    if (!string.IsNullOrEmpty(req)) sb.Append(" ").Append(req);

                    if (exchangeCount > 0)
                        sb.Append(" ").Append(Loc.Get("collector_exchanging", exchangeCount));
                    TextUtil.AppendPosition(sb, idx, count);
                    item = sb.ToString();
                }
                Announce(entering, "collector_exchange_heading", item);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollector exchange: {ex.Message}"); }
        }

        private void PollReward()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _reward, ref _rewardFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { if (_rewardActive) { _rewardActive = false; _rewardLast = -1; } return; }
            try
            {
                int idx = _reward.currentIndex;
                var list = _reward.currentDataList;
                int count = list?.Count ?? 0;

                bool entering = !_rewardActive;
                if (entering) { _rewardActive = true; _rewardLast = idx; }
                else if (idx == _rewardLast) return;
                else _rewardLast = idx;

                string item = "";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx].TryCast<UIFishCollectorCheckRewardListItemData>();
                    if (it != null)
                    {
                        var sb = new StringBuilder();
                        sb.Append(it.rewardName ?? "");
                        sb.Append(". ").Append(Loc.Get("collector_reward_fish", it.fishCount));
                        sb.Append(" ").Append(Loc.Get(it.isCleared
                            ? "collector_reward_claimed" : "collector_reward_locked"));
                        TextUtil.AppendPosition(sb, idx, count);
                        item = sb.ToString();
                    }
                }
                Announce(entering, "collector_reward_heading", item);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollector reward: {ex.Message}"); }
        }

        /// <summary>
        /// Builds the trade-requirement text for an exchange reward by reading the
        /// on-screen condition panel (<see cref="UIFishCollectorExchangeSelector.conditionPresenterList"/>).
        /// Each active row carries the game's own resolved qualifying-fish name (or a
        /// catch-all like "All fish" / a size such as "Large") and how many the player
        /// owns. Fish names are not stored in any readable data table (the item parameter
        /// only yields a placeholder key and TextManager can't resolve it), so the
        /// rendered panel is the authoritative source. The "any kind" fallback covers the
        /// unlikely case where the panel has no rows yet.
        /// </summary>
        private string BuildRequirementFromConditions(UIFishCollectorExchangeListItemData it)
        {
            try
            {
                var rows = new List<string>();
                var conds = _exchange?.conditionPresenterList;
                if (conds != null)
                {
                    for (int i = 0; i < conds.Count; i++)
                    {
                        var p = conds[i];
                        if (p == null || p.gameObject?.activeInHierarchy != true) continue;

                        string name = TextUtil.StripTags(p.type?.text);
                        if (string.IsNullOrEmpty(name)) continue;

                        // Each row's name is the game's own resolved fish type (or "All
                        // fish" / a size like "Large"); haveCount is how many qualify that
                        // the player owns. useCount stays 0 while browsing (it's the
                        // in-trade allocation), so it isn't announced.
                        string have = TextUtil.StripTags(p.haveCount?.text);
                        rows.Add(Loc.Get("collector_fish_req", name, have));
                    }
                }

                if (rows.Count > 0)
                    return Loc.Get("collector_costs_named",
                        it?.needCount ?? 0, string.Join(", ", rows));

                // No specific fish in the panel — the trade takes any fish.
                return Loc.Get("collector_costs_any", it?.needCount ?? 0);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FishCollector conditions: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// On entry, announces the screen heading followed by the current item; on a
        /// cursor move, announces just the item. Empty item strings are skipped.
        /// </summary>
        private static void Announce(bool entering, string headingKey, string item)
        {
            if (entering)
            {
                string heading = Loc.Get(headingKey);
                ScreenReader.Say(string.IsNullOrEmpty(item) ? heading : $"{heading} {item}");
            }
            else if (!string.IsNullOrEmpty(item))
            {
                ScreenReader.Say(item);
            }
        }
    }
}
