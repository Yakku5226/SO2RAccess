using Il2CppGame;
using System;
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
            bool active = UiFinder.TryGetActiveOverlay(ref _selectFish, ref _selectFishFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { if (_selectFishActive) { _selectFishActive = false; _selectFishLast = -1; } return; }
            try
            {
                int idx = _selectFish.currentIndex;
                var list = _selectFish.currentDataList;
                int count = list?.Count ?? 0;

                bool entering = !_selectFishActive;
                if (entering) { _selectFishActive = true; _selectFishLast = idx; }
                else if (idx == _selectFishLast) return;
                else _selectFishLast = idx;

                string item = "";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx].TryCast<UIFishCollectorSelectFishListItemData>();
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
                    sb.Append(it.itemName ?? "");
                    if (!string.IsNullOrEmpty(it.fishSize)) sb.Append(", ").Append(it.fishSize);
                    sb.Append(". ").Append(Loc.Get("collector_need_have", it.needCount, it.haveCount));
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
