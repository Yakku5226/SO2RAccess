using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        /// <summary>
        /// Harmony prefix for FieldBillboardObject.PlayMoveAnimation(FieldAnimationKind).
        /// During the auto-run approach phase, blocks any non-Run animation from being
        /// applied to the player. This prevents the game's internal state machine from
        /// resetting the player's Run animation to Idle every frame when no movement
        /// keys are held. Returns false (skip original) to block; true to allow.
        /// </summary>
        private static bool PlayMoveAnimation_Prefix(
            FieldBillboardObject __instance, FieldAnimationKind animationKind)
        {
            // Only intercept during the approach phase (not proximity-lock, not stopped).
            if (!_staticIsApproaching) return true;

            // Run is always allowed — let it set or re-set Run normally.
            if (animationKind == FieldAnimationKind.Run) return true;

            // Check if this FieldBillboardObject is the player character.
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return true;
                var player = fm.GetControlPlayer();
                if (player == null) return true;
                if (__instance.GetInstanceID() != player.GetInstanceID()) return true;

                // It is the player and we're in the approach phase — block the reset.
                return false;
            }
            catch
            {
                return true; // on any error, allow the call through
            }
        }

        /// <summary>
        /// Shared input suppression logic for IsDown and IsRepeat prefixes.
        /// When the mod menu is open, blocks ALL game input actions.
        /// When only the gamepad nav overlay is active, blocks D-pad directions,
        /// shortcut actions, and FieldCameraLeft (L1 camera).
        /// </summary>
        private static bool SuppressNavInput(
            GameInputManager.InputAction inputAction, ref bool __result)
        {
            // Mod menu open — block everything so no game action leaks through.
            if (ModMenuHandler.SuppressAllGameInput)
            {
                __result = false;
                return false;
            }

            if (!_gamepadNavActive) return true;

            // Up=11, Down=12, Right=13, Left=14 — basic D-pad movement
            // ShortCutUp=39, ShortCutDown=40, ShortCutLeft=41, ShortCutRight=42 — field shortcuts
            // FieldCameraLeft=56 — L1 camera panning
            if (inputAction == GameInputManager.InputAction.Up ||
                inputAction == GameInputManager.InputAction.Down ||
                inputAction == GameInputManager.InputAction.Left ||
                inputAction == GameInputManager.InputAction.Right ||
                inputAction == GameInputManager.InputAction.ShortCutUp ||
                inputAction == GameInputManager.InputAction.ShortCutDown ||
                inputAction == GameInputManager.InputAction.ShortCutLeft ||
                inputAction == GameInputManager.InputAction.ShortCutRight ||
                inputAction == GameInputManager.InputAction.FieldCameraLeft)
            {
                __result = false;
                return false; // skip original
            }

            return true;
        }

        /// <summary>
        /// Harmony prefix for GameInputManager.IsDown(InputAction).
        /// Blocks suppressed inputs while gamepad nav overlay is active.
        /// </summary>
        private static bool IsDown_Prefix(
            GameInputManager.InputAction inputAction, ref bool __result)
        {
            return SuppressNavInput(inputAction, ref __result);
        }

        /// <summary>
        /// Harmony prefix for GameInputManager.IsRepeat(InputAction).
        /// Mirrors IsDown suppression so held D-pad doesn't auto-repeat in the game.
        /// </summary>
        private static bool IsRepeat_Prefix(
            GameInputManager.InputAction inputAction, ref bool __result)
        {
            return SuppressNavInput(inputAction, ref __result);
        }

        /// <summary>
        /// Harmony prefix for GameInputManager.GetDPad().
        /// Returns zero vector while gamepad nav is active so D-pad analog
        /// input doesn't move the player character.
        /// </summary>
        private static bool GetDPad_Prefix(ref Vector2 __result)
        {
            if (ModMenuHandler.SuppressAllGameInput)
            {
                __result = Vector2.zero;
                return false;
            }

            if (!_gamepadNavActive) return true;

            __result = Vector2.zero;
            return false;
        }
    }
}
