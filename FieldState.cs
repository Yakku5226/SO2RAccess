using System;
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
        /// Returns true if the player is on the field with no menus blocking.
        /// Checks: FieldManager exists, player exists, camp and shop are closed.
        /// </summary>
        public static bool IsFieldFree()
        {
            try
            {
                if (FieldManager.Instance == null) return false;
                if (FieldManager.Instance.GetControlPlayer() == null) return false;
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
