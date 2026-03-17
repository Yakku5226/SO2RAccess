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
        /// True while auto-walk input injection is active. Static for Harmony postfix access.
        /// Set true at auto-walk start, false at cancel/arrival.
        /// </summary>
        private static bool _staticIsAutoWalking;

        /// <summary>
        /// Synthetic left stick direction injected into GetLeftStick() during auto-walk.
        /// Camera-relative Vector2: X = right/left, Y = forward/back.
        /// Magnitude 1.0 = full run speed.
        /// </summary>
        private static Vector2 _staticAutoWalkStickDir;

        /// <summary>
        /// Synthetic camera stick X injected into GetFieldCameraRightStick() during auto-walk.
        /// Positive = rotate camera right, negative = rotate camera left.
        /// Keeps the camera facing the walking direction so the player stays oriented.
        /// </summary>
        private static float _staticCameraStickX;

        /// <summary>
        /// Harmony postfix for GameInputManager.GetLeftStick().
        /// When auto-walk is active, replaces the returned stick value with a synthetic
        /// direction pointing toward the current waypoint. This makes the game's own
        /// movement pipeline handle physics, colliders, animations, triggers, and party AI.
        /// </summary>
        private static void GetLeftStick_Postfix(ref Vector2 __result)
        {
            if (!_staticIsAutoWalking) return;
            __result = _staticAutoWalkStickDir;
        }

        /// <summary>
        /// Harmony postfix for GameInputManager.GetFieldCameraRightStick().
        /// When auto-walk is active and the camera isn't aligned with the walking direction,
        /// injects gentle rotation to keep the camera facing forward along the path.
        /// </summary>
        private static void GetFieldCameraRightStick_Postfix(ref Vector2 __result)
        {
            if (!_staticIsAutoWalking) return;
            if (Mathf.Abs(_staticCameraStickX) < 0.01f) return;
            __result = new Vector2(_staticCameraStickX, 0f);
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

        /// <summary>
        /// Postfix on UIFieldFishingResultPresenter.Set — fires when the fishing
        /// result screen is populated with caught fish/items. Announces each catch
        /// via screen reader with name, size, and record status.
        /// </summary>
        private static void FishingResultSet_Postfix(
            Il2CppSystem.Collections.Generic.List<UIFieldFishingResultListItemData> fishingDataList)
        {
            try
            {
                if (fishingDataList == null || fishingDataList.Count == 0) return;

                var parts = new System.Collections.Generic.List<string>();

                for (int i = 0; i < fishingDataList.Count; i++)
                {
                    var data = fishingDataList[i];
                    if (data == null) continue;

                    string name = data.fishName;
                    if (string.IsNullOrEmpty(name)) continue;

                    string entry = name;

                    // Append size for fish (not items).
                    if (data.isFish && !string.IsNullOrEmpty(data.fishSize))
                        entry += $", {data.fishSize}";

                    // Append record/new flags.
                    if (data.isMaxSize)
                        entry += $", {Loc.Get("fish_max_size")}";
                    else if (data.isNewRecord)
                        entry += $", {Loc.Get("fish_new_record")}";

                    if (data.isNew)
                        entry += $", {Loc.Get("fish_new")}";

                    parts.Add(entry);

                    DebugLogger.LogState(
                        $"FishingResult: [{i}] {name} size={data.fishSize} " +
                        $"isFish={data.isFish} new={data.isNew} record={data.isNewRecord} max={data.isMaxSize}");
                }

                if (parts.Count > 0)
                {
                    string announcement = Loc.Get("fish_caught") + " " + string.Join(". ", parts) + ".";
                    ScreenReader.Say(announcement);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FishingResultSet_Postfix error: {ex.Message}");
            }
        }
    }
}
