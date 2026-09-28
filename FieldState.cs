using System;
using Il2CppCommon;
using Il2CppGame;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Shared field-state queries used by multiple handlers.
    /// Centralizes checks so all handlers agree on when the field is usable.
    /// </summary>
    public static class FieldState
    {
        /// <summary>Last reason IsFieldFree said no; logged only when it changes (called every frame).</summary>
        private static string _lastBlockReason;

        /// <summary>
        /// Returns true if the player is on the field with no menus, dialogues,
        /// events, or notifications blocking.
        /// Checks: FieldManager exists, something is under player control (the
        /// walking party leader, the bunny, or a mounted psynard), game not
        /// paused, no event running, camp and shop are closed.
        /// The blocking reason is logged once per change so a silent refusal
        /// (e.g. "nav menu does nothing") explains itself in the log.
        /// </summary>
        public static bool IsFieldFree()
        {
            string reason = BlockReason();
            if (reason != _lastBlockReason)
            {
                _lastBlockReason = reason;
                DebugLogger.LogState(reason == null
                    ? "FieldState: field free again."
                    : $"FieldState: not free — {reason}.");
            }
            return reason == null;
        }

        /// <summary>Null when the field is free, otherwise the first failing check.</summary>
        private static string BlockReason()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return "no FieldManager";
                if (!HasControlObject(fm)) return "no control player (and not riding the psynard)";

                // Game-level pause covers dialogues, notifications, tutorials,
                // and any UI that freezes field gameplay.
                if (PauseManager.Instance != null && PauseManager.Instance.IsPause)
                    return "game paused";

                // Event system covers cutscenes, scripted scenes, and NPC events.
                if (EventManager.Instance != null && EventManager.Instance.IsRunning)
                    return "event running";

                if (CampMenuHandler.IsCampOpen) return "camp open";
                if (ShopHandler.IsShopOpen) return "shop open";
                // The camp quick heal dialog closes the camp window while it shows.
                if (QuickRecoveryHandler.IsCampRecoveryOpen) return "camp recovery open";
                return null;
            }
            catch (Exception ex)
            {
                return $"exception: {ex.Message}";
            }
        }

        /// <summary>
        /// True while the party rides the psynard. The bunny is a kind of
        /// FieldPlayer and stays the control player, but the psynard is a
        /// separate flying object: while mounted, GetControlPlayer() is null
        /// and the psynard's own transform is the party's position.
        /// </summary>
        public static bool IsRidingPsynard()
        {
            try
            {
                var fm = FieldManager.Instance;
                return fm != null && IsRidingPsynard(fm);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FieldState.IsRidingPsynard: exception: {ex.Message}");
                return false;
            }
        }

        private static bool IsRidingPsynard(FieldManager fm)
            => fm.IsFieldFlag(FieldBitFlag.Psynard) && fm.FieldPsynard != null;

        /// <summary>Something the player steers exists: a control player or a mounted psynard.</summary>
        private static bool HasControlObject(FieldManager fm)
            => fm.GetControlPlayer() != null || IsRidingPsynard(fm);

        /// <summary>
        /// Position of whatever the player is steering: the control player on
        /// foot or on the bunny, the psynard while flying. False when neither
        /// exists (not on a field, or mid-transition).
        /// </summary>
        public static bool TryGetControlPosition(out Vector3 position)
        {
            position = Vector3.zero;
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return false;

                var player = fm.GetControlPlayer();
                if (player != null)
                {
                    position = player.transform.position;
                    return true;
                }

                if (IsRidingPsynard(fm))
                {
                    position = fm.FieldPsynard.transform.position;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FieldState.TryGetControlPosition: exception: {ex.Message}");
                return false;
            }
        }
    }
}
