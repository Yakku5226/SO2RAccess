using Il2CppGame;
using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// Shared mission-list readout helpers used by both the camp Quests and Missions
    /// sub-screen (<see cref="CampMenuHandler"/>) and the guild mission window
    /// (<see cref="GuildHandler"/>).
    ///
    /// Both screens drive the same UIMissionWindow / UIMissionListSelector types, so the
    /// announcement logic — status text, category name, reward clause, and the full
    /// highlighted-item announcement — lives here in one place rather than being
    /// duplicated per handler. Each handler keeps only its own open/close detection and
    /// cursor polling; the strings come from here.
    /// </summary>
    public static class MissionReadout
    {
        /// <summary>
        /// Builds the full spoken announcement for the highlighted mission:
        /// "name, status (index/total) reward". Returns the empty-slot fallback when the
        /// name is blank, or null when the index is out of range / data is unavailable.
        /// </summary>
        public static string BuildItemAnnouncement(
            UIMissionListSelector selector, UIListSelectorBase listBase, int idx,
            out string name, out string status)
        {
            name = null;
            status = null;

            var list = listBase?.currentDataList;
            if (list == null) return null;
            int total = list.Count;
            if (total == 0 || idx < 0 || idx >= total) return null;

            var item = list[idx].TryCast<UIMissionListItemData>();
            if (item == null) return null;

            name = item.missionName;
            if (string.IsNullOrEmpty(name))
                return Loc.Get("mission_empty", idx + 1, total);

            status = GetStatusText(item);
            string announcement = Loc.Get("mission_item", name, status, idx + 1, total);

            string reward = BuildReward(selector);
            if (!string.IsNullOrEmpty(reward))
                announcement += " " + reward;

            return announcement;
        }

        /// <summary>
        /// Builds the reward clause for the highlighted mission from the selector's
        /// rewardItemDataList — the same data the game shows in its reward panel, with
        /// names already resolved (so it works for items whose IDs we can't resolve
        /// ourselves, e.g. gems). Returns null when no reward data is available.
        /// </summary>
        public static string BuildReward(UIMissionListSelector selector)
        {
            try
            {
                var rewards = selector?.rewardItemDataList;
                if (rewards == null || rewards.Count == 0) return null;

                var parts = new List<string>();
                for (int i = 0; i < rewards.Count; i++)
                {
                    var r = rewards[i];
                    if (r == null) continue;

                    string rName = TextUtil.StripTags(r.rewardName ?? "");
                    if (string.IsNullOrEmpty(rName)) continue;

                    int count = r.itemCount;
                    parts.Add(count > 1
                        ? Loc.Get("overflow_item_multi", rName, count)
                        : Loc.Get("overflow_item", rName));
                }

                if (parts.Count == 0) return null;
                return Loc.Get("mission_reward", string.Join(", ", parts));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"MissionReadout.BuildReward error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maps mission data flags to a user-friendly status string.
        /// </summary>
        public static string GetStatusText(UIMissionListItemData item)
        {
            if (item.isClear)
                return Loc.Get("mission_status_complete");

            // stateMessage is the game's own localized status text
            string stateMsg = item.stateMessage;
            if (!string.IsNullOrEmpty(stateMsg))
                return stateMsg;

            // Fallback to flags/enum
            if (item.isAchieved)
                return Loc.Get("mission_status_achieved");

            var state = item.missionState;
            switch (state)
            {
                case UIMissionStatePresenter.MissionState.Completed:
                    return Loc.Get("mission_status_complete");
                case UIMissionStatePresenter.MissionState.Achieved:
                    return Loc.Get("mission_status_achieved");
                case UIMissionStatePresenter.MissionState.Reportable:
                    return Loc.Get("mission_status_reportable");
                case UIMissionStatePresenter.MissionState.Received:
                    return Loc.Get("mission_status_in_progress");
                case UIMissionStatePresenter.MissionState.NotAchieved:
                    return Loc.Get("mission_status_incomplete");
                case UIMissionStatePresenter.MissionState.NotReceived:
                    return Loc.Get("mission_status_incomplete");
                default:
                    return Loc.Get("mission_status_incomplete");
            }
        }

        /// <summary>
        /// Returns a friendly name for the mission category
        /// (Beginner, Expert, Specialist, Legend).
        /// </summary>
        public static string GetCategoryName(int category)
        {
            switch (category)
            {
                case 0: return Loc.Get("mission_cat_beginner");
                case 1: return Loc.Get("mission_cat_expert");
                case 2: return Loc.Get("mission_cat_specialist");
                case 3: return Loc.Get("mission_cat_legend");
                default: return category.ToString();
            }
        }
    }
}
