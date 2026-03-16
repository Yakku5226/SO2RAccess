using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces guild mission menu events to the screen reader.
    ///
    /// The guild mission UI operates entirely in native C++ — all managed data
    /// accessors return empty/stale values, all text components contain only
    /// template placeholders, and all presenter fields are unpopulated. The game
    /// renders mission names through a native pipeline that bypasses Unity's
    /// managed TextMeshPro entirely.
    ///
    /// What works: window open/close detection via gameObject.activeInHierarchy.
    /// The dialogue system separately catches "Mission accepted.", provisions,
    /// and "There are no more missions to accept." — providing core guild flow.
    ///
    /// Known limitation: individual mission names and cursor position cannot
    /// be read from managed code. Extensively tested: currentDataList, presenters
    /// (FindObjectsOfTypeAll), all 59 TMPro components (.text, GetParsedText,
    /// textInfo.characterCount), currentIndex, windowState — all frozen/empty.
    /// </summary>
    public class GuildHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        private static UIMissionWindow _missionWindow = null;
        private static bool _guildOpen = false;
        private static int _findCooldown = 0;

        /// <summary>True while the guild mission screen is open.</summary>
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
            _missionWindow = __result;
            DebugLogger.LogState("Guild: captured UIMissionWindow from hook.");
        }

        /// <summary>Resets state on scene change.</summary>
        public void OnSceneChanged()
        {
            _guildOpen = false;
            _findCooldown = 0;
        }

        #endregion

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            // Camp has its own quest and mission handlers — guild detection
            // must not fire during camp or it hijacks the mission window.
            if (CampMenuHandler.IsCampOpen) return;
            DetectMissionWindow();
        }

        /// <summary>
        /// Detects mission window open/close via gameObject.activeInHierarchy.
        /// Announces "Guild." on open. Mission names and cursor tracking are
        /// not possible due to native code wall — dialogue system handles the rest.
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
                bool goActive = _missionWindow.gameObject != null
                    && _missionWindow.gameObject.activeInHierarchy;

                if (goActive && !_guildOpen)
                {
                    _guildOpen = true;
                    ScreenReader.Say(Loc.Get("guild_screen"));
                    DebugLogger.LogState("Guild: opened.");
                }
                else if (!goActive && _guildOpen)
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

        #endregion
    }
}
