using Il2CppGame;

namespace SO2RAccess
{
    /// <summary>
    /// Shared quest-list readout helpers used by both the camp Quests sub-screen
    /// (<see cref="CampMenuHandler"/>) and the guild master's accept-mission menu
    /// (<see cref="GuildHandler"/>).
    ///
    /// Both screens drive the same UIQuestSelector / UIQuestListItemData: the guild
    /// mission board renders through the quest selector (gameObject "ui_quest_selector"),
    /// not the mission window — which is why every earlier guild attempt that read the
    /// mission selector found it empty. The announcement logic lives here so the two
    /// handlers share one copy.
    /// </summary>
    public static class QuestReadout
    {
        /// <summary>
        /// Builds the full spoken announcement for the highlighted quest:
        /// "name, [New,] status (index/total)". Returns the empty-slot fallback when the
        /// name is blank, or null when the index is out of range / data is unavailable.
        /// </summary>
        public static string BuildItemAnnouncement(
            UIListSelectorBase listBase, int idx, out string name, out string status)
        {
            name = null;
            status = null;

            var list = listBase?.currentDataList;
            if (list == null) return null;
            int total = list.Count;
            if (total == 0 || idx < 0 || idx >= total) return null;

            var item = list[idx].TryCast<UIQuestListItemData>();
            if (item == null) return null;

            name = item.missionName;
            if (string.IsNullOrEmpty(name))
                return Loc.Get("quest_empty", idx + 1, total);

            status = GetStatusText(item);
            return item.isNew
                ? Loc.Get("quest_item_new", name, status, idx + 1, total)
                : Loc.Get("quest_item", name, status, idx + 1, total);
        }

        /// <summary>
        /// Maps quest flags / MissionState enum to a user-friendly status string.
        /// </summary>
        public static string GetStatusText(UIQuestListItemData item)
        {
            if (item.isEnd)
                return Loc.Get("quest_status_completed");
            if (item.isReportable)
                return Loc.Get("quest_status_reportable");
            if (item.isReceived)
                return Loc.Get("quest_status_received");

            // Fallback to enum
            var state = item.missionState;
            switch (state)
            {
                case UIMissionStatePresenter.MissionState.Completed:
                    return Loc.Get("quest_status_completed");
                case UIMissionStatePresenter.MissionState.Achieved:
                    return Loc.Get("quest_status_completed");
                case UIMissionStatePresenter.MissionState.Reportable:
                    return Loc.Get("quest_status_reportable");
                case UIMissionStatePresenter.MissionState.Received:
                    return Loc.Get("quest_status_received");
                case UIMissionStatePresenter.MissionState.NotAchieved:
                    return Loc.Get("quest_status_not_achieved");
                case UIMissionStatePresenter.MissionState.NotReceived:
                    return Loc.Get("quest_status_available");
                default:
                    return Loc.Get("quest_status_available");
            }
        }
    }
}
