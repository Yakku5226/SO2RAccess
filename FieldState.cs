using System;
using Il2CppCommon;
using Il2CppGame;

namespace SO2RAccess
{
    /// <summary>
    /// Shared field-state queries used by multiple handlers.
    /// Centralizes checks so all handlers agree on when the field is usable.
    /// </summary>
    public static class FieldState
    {
        /// <summary>
        /// Returns true if the player is on the field with no menus, dialogues,
        /// events, or notifications blocking.
        /// Checks: FieldManager exists, player exists, game not paused,
        /// no event running, camp and shop are closed.
        /// </summary>
        public static bool IsFieldFree()
        {
            try
            {
                if (FieldManager.Instance == null) return false;
                if (FieldManager.Instance.GetControlPlayer() == null) return false;

                // Game-level pause covers dialogues, notifications, tutorials,
                // and any UI that freezes field gameplay.
                if (PauseManager.Instance != null && PauseManager.Instance.IsPause)
                    return false;

                // Event system covers cutscenes, scripted scenes, and NPC events.
                if (EventManager.Instance != null && EventManager.Instance.IsRunning)
                    return false;

                if (CampMenuHandler.IsCampOpen) return false;
                if (ShopHandler.IsShopOpen) return false;
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FieldState.IsFieldFree: exception: {ex.Message}");
                return false;
            }
        }
    }
}
