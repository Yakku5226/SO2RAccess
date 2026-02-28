using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces game over (battle loss) menu navigation to the screen reader.
    ///
    /// Features:
    ///   - "Game over." heading when the screen appears.
    ///   - "Retry, 1 of 2." / "Title, 2 of 2." as the player navigates.
    ///
    /// Detection: UIGameOverWindow found via FindObjectOfType (lazy init with throttle),
    /// IsOpened polled each frame to detect open/close transitions.
    ///
    /// All navigation in this menu is native C++ — no Harmony hooks fire for
    /// up/down. Polling is the correct approach (same pattern as camp/shop menus).
    /// </summary>
    public class GameOverHandler
    {
        #region Fields

        private bool _patchesApplied;

        private static UIGameOverWindow _window;
        private static bool _isOpen;
        private static int _findCooldown;

        private static UIGameOverSelector _selector;
        private static UIListSelectorBase _selectorBase;
        private static int _lastIndex = -1;

        // Menu option names by index — matches UIGameOverSelector.MenuType enum order.
        private static readonly string[] MenuNames = { "Retry", "Title" };

        #endregion

        #region Patches

        /// <summary>
        /// Initializes IL2CPP types for runtime access. No Harmony hooks needed —
        /// all announcements are polling-based.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIGameOverWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIGameOverSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIGameOverSelectorData).TypeHandle);

                _patchesApplied = true;
                MelonLogger.Msg("[GAMEOVER] Patches applied (polling-only).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GAMEOVER] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Called on scene change to reset cached references.
        /// </summary>
        public void OnSceneChanged()
        {
            _window = null;
            _isOpen = false;
            _findCooldown = 0;
            _selector = null;
            _selectorBase = null;
            _lastIndex = -1;
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Detects game over screen open/close and polls navigation.
        /// </summary>
        public void Update()
        {
            DetectWindow();
            if (!_isOpen) return;

            UpdateMenu();
        }

        /// <summary>
        /// Finds UIGameOverWindow lazily (throttled to ~1 search per second) and polls
        /// IsOpened to detect open/close transitions.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _window = UnityEngine.Object.FindObjectOfType<UIGameOverWindow>();
                    if (_window != null)
                    {
                        DebugLogger.LogState("GameOver: cached UIGameOverWindow reference.");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"GameOver detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                bool isOpen = _window.IsOpened;

                if (isOpen && !_isOpen)
                {
                    _isOpen = true;
                    _selector = _window.gameOverSelector;
                    _selectorBase = _selector?.TryCast<UIListSelectorBase>();
                    _lastIndex = -1;

                    ScreenReader.Say(Loc.Get("gameover_screen"));
                    DebugLogger.LogState("GameOver: opened.");
                }
                else if (!isOpen && _isOpen)
                {
                    _isOpen = false;
                    _lastIndex = -1;
                    DebugLogger.LogState("GameOver: closed.");
                }
            }
            catch
            {
                _window = null;
                _isOpen = false;
                _findCooldown = 0;
            }
        }

        /// <summary>
        /// Polls the UIGameOverSelector and announces the focused menu item
        /// (Retry or Title) with position.
        /// </summary>
        private void UpdateMenu()
        {
            if (_selectorBase == null) return;

            try
            {
                int idx = _selectorBase.currentIndex;
                if (idx == _lastIndex) return;
                _lastIndex = idx;

                int total = MenuNames.Length;
                if (idx < 0 || idx >= total) return;

                string name = MenuNames[idx];

                DebugLogger.LogGameValue("GameOver.menu",
                    $"{name} ({idx + 1}/{total})");

                ScreenReader.Say(Loc.Get("gameover_menu_item", name, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"GameOverHandler.UpdateMenu: {ex.Message}");
            }
        }

        #endregion
    }
}
