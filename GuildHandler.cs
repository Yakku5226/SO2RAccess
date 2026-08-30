using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the guild master's mission menus to the screen reader.
    ///
    /// Key finding (2026-06-16): the guild's "accept missions" list is rendered through
    /// the QUEST selector (UIQuestSelector, gameObject "ui_quest_selector"), NOT the
    /// mission window. Every earlier attempt read UIMissionWindow.missionListSelector —
    /// which stays empty/frozen at the guild — and concluded "native wall". A scene-wide
    /// selector scan showed ui_quest_selector with a populated currentDataList and a
    /// cursor index that tracks navigation, so it reads exactly like the camp Quest
    /// screen (shared logic in <see cref="QuestReadout"/>).
    ///
    /// The quest list is found by polling (navigation is native-only), gated on the
    /// selector being active + populated and the camp being closed (the camp Quest screen
    /// drives the same selector and is handled by <see cref="CampMenuHandler"/>).
    ///
    /// The mission window is still detected for the flows that do use it (announces
    /// "Guild." on open). The dialogue system separately catches "Mission accepted.",
    /// provisions, and "There are no more missions to accept."
    /// </summary>
    public class GuildHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Mission window — detected only to announce "Guild." for flows that open it.
        private static UIMissionWindow _missionWindow = null;
        private static bool _guildOpen = false;
        private static int _findCooldown = 0;

        // Guild quest list — the actual readable accept-missions menu.
        private static UIQuestSelector _questSelector = null;
        private static UIListSelectorBase _questListBase = null;
        private static float _questFind = 0f;
        private static bool _questActive = false;
        private static int _questLast = -1;

        /// <summary>True while the guild mission window is open.</summary>
        public static bool IsGuildOpen => _guildOpen;

        #endregion

        #region Patches

        /// <summary>
        /// Hooks GameUIManager.OpenMissionWindow to capture the window reference.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIMissionWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(GameUIManager).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(GameUIManager), "OpenMissionWindow"),
                    postfix: new HarmonyMethod(typeof(GuildHandler),
                        nameof(OpenMissionWindow_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("[GUILD] Patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GUILD] Patch error: {ex.Message}");
            }
        }

        private static void OpenMissionWindow_Postfix(UIMissionWindow __result)
        {
            if (__result == null) return;
            // Camp captures the same window for its own handler; don't steal it.
            if (CampMenuHandler.IsCampOpen) return;
            _missionWindow = __result;
            DebugLogger.LogState("Guild: captured UIMissionWindow from hook.");
        }

        /// <summary>Resets state on scene change.</summary>
        public void OnSceneChanged()
        {
            _guildOpen = false;
            _findCooldown = 0;
            _questSelector = null;
            _questListBase = null;
            _questFind = 0f;
            _questActive = false;
            _questLast = -1;
        }

        #endregion

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            // Camp has its own quest and mission handlers — guild detection must not
            // fire during camp or it hijacks the shared quest/mission selectors.
            if (CampMenuHandler.IsCampOpen) return;

            DetectMissionWindow();
            PollGuildQuests();
        }

        /// <summary>
        /// Detects mission window open/close via IsOpened and announces "Guild." for the
        /// flows that route through it (report results, etc.). Mission names there are not
        /// readable from managed code; the accept-missions list is read via
        /// <see cref="PollGuildQuests"/> instead.
        /// </summary>
        private void DetectMissionWindow()
        {
            if (_missionWindow == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    var guiMgr = GameUIManager.Instance;
                    if (guiMgr != null)
                    {
                        var wc = guiMgr.GetWindow(UIDefine.WindowType.Mission);
                        if (wc != null)
                        {
                            _missionWindow = wc.TryCast<UIMissionWindow>();
                            if (_missionWindow != null)
                            {
                                DebugLogger.LogState("Guild: got window via GetWindow.");
                                return;
                            }
                        }
                    }

                    var found = UnityEngine.Object.FindObjectOfType<UIMissionWindow>();
                    if (found != null)
                    {
                        _missionWindow = found;
                        DebugLogger.LogState("Guild: got window via FindObjectOfType.");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"Guild find error: {ex.Message}");
                }
                return;
            }

            try
            {
                // IsOpened (not gameObject.activeInHierarchy): the guild building shares
                // its UI hierarchy with the shop window, so activeInHierarchy reads true
                // whenever the shop is open. IsOpened reflects this window's own state.
                bool isOpen = _missionWindow.IsOpened;

                if (isOpen && !_guildOpen)
                {
                    _guildOpen = true;
                    ScreenReader.Say(Loc.Get("guild_screen"));
                    DebugLogger.LogState("Guild: opened.");
                }
                else if (!isOpen && _guildOpen)
                {
                    _guildOpen = false;
                    DebugLogger.LogState("Guild: closed.");
                }
            }
            catch
            {
                _missionWindow = null;
                _guildOpen = false;
                _findCooldown = 0;
            }
        }

        /// <summary>
        /// Polls the guild's accept-missions list (the quest selector) and announces the
        /// highlighted entry's name, status, and position — mirroring the camp Quest
        /// readout via <see cref="QuestReadout"/>. Found and verified-active via
        /// <see cref="UiFinder"/>: active in hierarchy AND a populated data list. The
        /// camp guard in <see cref="Update"/> keeps this from firing on the camp Quest
        /// screen, which drives the same selector.
        /// </summary>
        private void PollGuildQuests()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _questSelector, ref _questFind,
                s => s.gameObject != null && s.gameObject.activeInHierarchy
                     && (s.TryCast<UIListSelectorBase>()?.currentDataList?.Count ?? 0) > 0);

            if (!active)
            {
                if (_questActive) { _questActive = false; _questLast = -1; }
                _questListBase = null;
                return;
            }

            // Opening the guild's command menu (Accept/Report) wakes the quest selector
            // with STALE data from the previous visit — active in hierarchy and populated,
            // but without input focus. Real focus (entering the list, moving the cursor)
            // always fires UIQuestListItemPresenter.OnSelected (universal hook), so
            // require a recent selection event before announcing anything; index shifts
            // from background list rebuilds stay silent too.
            if (!ListSelectionHandler.WasRecentlySelected(
                    nameof(UIQuestListItemPresenter), 1.0f))
            {
                return;
            }

            try
            {
                if (_questListBase == null)
                {
                    _questListBase = _questSelector.TryCast<UIListSelectorBase>();
                    if (_questListBase == null) return;
                }

                int idx = _questListBase.currentIndex;

                bool entering = !_questActive;
                if (entering)
                {
                    _questActive = true;
                    ScreenReader.Say(Loc.Get("guild_missions_screen"));
                }
                else if (idx == _questLast)
                {
                    return;
                }
                _questLast = idx;

                string announcement = QuestReadout.BuildItemAnnouncement(
                    _questListBase, idx, out string name, out string status);
                if (announcement == null) return;

                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("GuildQuest.item", $"{name} [{status}] ({idx + 1})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"GuildHandler.PollGuildQuests: {ex.Message}");
                _questListBase = null;
            }
        }

        #endregion
    }
}
