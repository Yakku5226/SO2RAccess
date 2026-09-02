using Il2CppGame;
using MelonLoader;
using System;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler: shared L1/R1 tab-switch announcing.
    //
    // Several camp sub-screens put a tab strip above the list — the party member whose
    // equipment/skills are shown, or the item category being browsed — and L1/R1 cycles
    // it. Sighted players see the strip change; a screen reader user hears nothing
    // unless the mod says so. Before this, only the Status, Skills and Item Creation
    // screens announced a switch, each with its own copy of the logic.
    //
    // TabSwitchAnnouncer is that logic in one place. Screens differ in HOW the row under
    // the cursor is announced — equipment slots are polled, battle skills come from an
    // information-presenter hook — so the announcer never speaks the row itself. It
    // parks the tab's label and lets whichever code path announces the new row prepend
    // it, so the user hears one sentence ("Rena. Weapon: Sword, 1 of 7.") instead of two
    // that cut each other off.
    public partial class CampMenuHandler
    {
        /// <summary>
        /// Detects an L1/R1 tab switch on a camp sub-screen and hands its label to the
        /// screen's own row announcement.
        ///
        /// Usage per screen:
        ///   1. <see cref="HasChanged"/> with the tab's identity (a PlayerID or category
        ///      enum cast to int) every frame, or from the hook that fires on a switch.
        ///      The first call after a <see cref="Reset"/> only seeds, so merely opening
        ///      a screen never blurts the label.
        ///   2. When it returns true, <see cref="Park"/> the spoken label and force the
        ///      row under the cursor to re-announce (its content belongs to the new tab).
        ///   3. Pass the row announcement through <see cref="Decorate"/> before speaking.
        ///   4. Call <see cref="FlushIfStale"/> every frame and <see cref="Reset"/> when
        ///      the screen closes.
        ///
        /// Both a poll and a hook may call <see cref="HasChanged"/> for the same screen:
        /// whichever runs first consumes the switch, the other sees no change. That is
        /// deliberate — the hook merges the label into the row it is about to speak,
        /// and the poll is the safety net for when the hook does not fire.
        /// </summary>
        private sealed class TabSwitchAnnouncer
        {
            /// <summary>
            /// How long a parked label waits for a row announcement to claim it before
            /// it is spoken on its own. Long enough for the game to rebuild the list and
            /// its information panel (a few frames), short enough not to trail the row.
            /// </summary>
            private const float PrefixTimeout = 0.3f;

            private readonly string _logLabel;
            private readonly string _prefixKey;

            private int _currentTab;
            private bool _seeded;
            private string _pendingLabel;
            private float _pendingTime;

            /// <param name="logLabel">Screen name used in debug log lines.</param>
            /// <param name="prefixKey">
            /// Loc key joining the tab label to the row text, e.g. "camp_character_prefix".
            /// </param>
            public TabSwitchAnnouncer(string logLabel, string prefixKey)
            {
                _logLabel = logLabel;
                _prefixKey = prefixKey;
            }

            /// <summary>Forgets the tracked tab and any parked label. Call on screen close.</summary>
            public void Reset()
            {
                _currentTab = 0;
                _seeded = false;
                _pendingLabel = null;
            }

            /// <summary>
            /// Feeds the tab the screen is showing now. Returns true only on a genuine
            /// switch — the first call after a reset seeds the tracker and returns false.
            /// </summary>
            public bool HasChanged(int tab)
            {
                if (!_seeded)
                {
                    _currentTab = tab;
                    _seeded = true;
                    // One line per screen entry — proves the tab value is readable at all,
                    // which is the first thing to check if a switch is never announced.
                    DebugLogger.LogState($"{_logLabel}: tab tracking seeded at {tab}.");
                    return false;
                }

                if (tab == _currentTab) return false;

                _currentTab = tab;
                return true;
            }

            /// <summary>
            /// Parks the label for the switch <see cref="HasChanged"/> just reported.
            /// An empty label is logged rather than silently dropped — a switch the mod
            /// cannot name is a bug worth seeing in the log.
            /// </summary>
            public void Park(string label)
            {
                if (string.IsNullOrEmpty(label))
                {
                    DebugLogger.LogState($"{_logLabel}: tab switched but no label resolved.");
                    _pendingLabel = null;
                    return;
                }

                _pendingLabel = label;
                _pendingTime = UnityEngine.Time.time;
                DebugLogger.LogState($"{_logLabel}: tab switched to '{label}'.");
            }

            /// <summary>
            /// Prefixes the parked label onto a row announcement and clears it.
            /// Returns <paramref name="text"/> unchanged when no switch is pending.
            /// </summary>
            public string Decorate(string text)
            {
                if (_pendingLabel == null) return text;

                string label = _pendingLabel;
                _pendingLabel = null;

                if (string.IsNullOrEmpty(text)) return label;
                return Loc.Get(_prefixKey, label, text);
            }

            /// <summary>
            /// Takes the parked label without a row to attach it to, or null when none
            /// is pending. For screens that have to speak the label alone (an empty
            /// item category has no row to read).
            /// </summary>
            public string Take()
            {
                string label = _pendingLabel;
                _pendingLabel = null;
                return label;
            }

            /// <summary>
            /// Speaks the parked label on its own once it is clear no row announcement is
            /// going to claim it. Called every frame while the camp menu is open, so a
            /// tab switch is never silent even if the screen's row announcement fails.
            /// </summary>
            public void FlushIfStale()
            {
                if (_pendingLabel == null) return;
                if (UnityEngine.Time.time - _pendingTime < PrefixTimeout) return;

                string label = _pendingLabel;
                _pendingLabel = null;
                DebugLogger.LogState(
                    $"{_logLabel}: no row announcement claimed '{label}' — speaking it alone.");
                ScreenReader.Say(label);
            }
        }

        // Character tabs (L1/R1) — one announcer per screen that has them.
        // The two battle/combat skill leveling screens share one: only one of their
        // inner selectors is cached at a time, and switching between them re-seeds.
        private static readonly TabSwitchAnnouncer _equipCharTab =
            new TabSwitchAnnouncer("CampEquip", "camp_character_prefix");
        private static readonly TabSwitchAnnouncer _battleSkillCharTab =
            new TabSwitchAnnouncer("CampBattleSkill", "camp_character_prefix");
        private static readonly TabSwitchAnnouncer _battleSkillSettingCharTab =
            new TabSwitchAnnouncer("CampBattleSkillSetting", "camp_character_prefix");

        // Item category tabs (L1/R1) on the Items screen.
        private static readonly TabSwitchAnnouncer _itemCategoryTab =
            new TabSwitchAnnouncer("CampItem", "camp_item_category_prefix");

        /// <summary>
        /// Gives every announcer a chance to speak a label no row announcement claimed.
        /// Called once per frame from <see cref="Update"/>.
        /// </summary>
        private static void TickTabSwitchAnnouncers()
        {
            _equipCharTab.FlushIfStale();
            _battleSkillCharTab.FlushIfStale();
            _battleSkillSettingCharTab.FlushIfStale();
            _itemCategoryTab.FlushIfStale();
        }

        /// <summary>
        /// Feeds a screen's current character to its announcer and parks the name when it
        /// genuinely changed. Returns true then, so the caller can force its row to re-read.
        ///
        /// PlayerID.INVALID is ignored rather than tracked. A sub-screen reports INVALID
        /// (enum value 0) until the game populates it, which can be a second after the
        /// mod starts polling — seeding on that turns the first real character into a
        /// phantom switch and the screen greets you with a name you did not ask for.
        /// </summary>
        private static bool TrackCharacterTab(TabSwitchAnnouncer announcer, PlayerID playerID)
        {
            if (playerID == PlayerID.INVALID) return false;
            if (!announcer.HasChanged((int)playerID)) return false;

            announcer.Park(ResolveCharacterName(playerID));
            return true;
        }

        /// <summary>
        /// The party member's localized first name, or "" when it cannot be resolved.
        /// Shared by every character-tab screen so the name comes from one place.
        /// </summary>
        private static string ResolveCharacterName(PlayerID playerID)
        {
            if (playerID == PlayerID.INVALID) return "";

            try
            {
                string name = ParameterManager.Instance?.GetCharacterFirstName(playerID);
                if (!string.IsNullOrEmpty(name)) return name;

                DebugLogger.LogState($"CampCharacterTab: no first name for {playerID}.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"CampMenuHandler.ResolveCharacterName({playerID}): {ex.Message}");
            }

            return "";
        }
    }
}
