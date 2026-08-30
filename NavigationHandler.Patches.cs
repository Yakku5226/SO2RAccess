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
            if (!_staticIsAutoWalking || _wmDirectMoveActive) return;
            __result = _staticAutoWalkStickDir;
        }

        /// <summary>
        /// Harmony postfix for GameInputManager.GetPlayerControlStick().
        /// CallerCount(0) — called only from native IL2CPP code, but Harmony patches
        /// still intercept the call. The world map's native movement pipeline reads
        /// this method instead of GetLeftStick(), so both must be hooked for auto-walk
        /// to work on both field maps and the world map.
        /// </summary>
        private static void GetPlayerControlStick_Postfix(ref Vector2 __result)
        {
            if (!_staticIsAutoWalking || _wmDirectMoveActive) return;
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
        /// Game actions bound to the mod's modifier button (L2), suppressed while
        /// the nav overlay is held so holding the modifier doesn't also trigger
        /// its game function. Rebuilt from the LIVE bindings on each overlay open
        /// (RebuildModifierSuppressSet), so it stays correct if the player
        /// rebinds pad buttons in the game config.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<GameInputManager.InputAction>
            _modifierSuppressSet = new System.Collections.Generic.HashSet<GameInputManager.InputAction>();

        /// <summary>
        /// Fills <see cref="_modifierSuppressSet"/> with every game action whose
        /// live pad binding is the mod modifier (InputKey.L2). Falls back to the
        /// statically likely L2 actions when the binding API is unavailable —
        /// never leaves the set empty while the overlay is in use.
        /// </summary>
        private static void RebuildModifierSuppressSet()
        {
            _modifierSuppressSet.Clear();
            try
            {
                var gim = GameInputManager.Instance;
                if (gim != null)
                {
                    foreach (GameInputManager.InputAction action in
                        Enum.GetValues(typeof(GameInputManager.InputAction)))
                    {
                        if (action == GameInputManager.InputAction.Invalid ||
                            action == GameInputManager.InputAction.Max)
                            continue;
                        if (gim.GetBindInputKey(action) == Il2CppCommon.InputManager.InputKey.L2)
                            _modifierSuppressSet.Add(action);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV modifier suppress rebuild failed: {ex.Message}");
            }

            if (_modifierSuppressSet.Count == 0)
            {
                // Binding API not ready — fall back to the actions L2 plausibly
                // drives so the overlay never leaks the button to the game.
                _modifierSuppressSet.Add(GameInputManager.InputAction.TriggerLeft2);
                _modifierSuppressSet.Add(GameInputManager.InputAction.FieldWalk);
                _modifierSuppressSet.Add(GameInputManager.InputAction.FieldCameraUp);
                _modifierSuppressSet.Add(GameInputManager.InputAction.FieldCameraDown);
            }

            DebugLogger.LogState(
                "NAV modifier suppress set: " + string.Join(", ", _modifierSuppressSet));
        }

        /// <summary>
        /// Shared input suppression logic for IsDown and IsRepeat prefixes.
        /// When the mod menu is open, blocks ALL game input actions.
        /// When only the gamepad nav overlay is active, blocks D-pad directions,
        /// shortcut actions, and whatever game actions live on the mod's L2
        /// modifier (dynamic set, see RebuildModifierSuppressSet).
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
            if (inputAction == GameInputManager.InputAction.Up ||
                inputAction == GameInputManager.InputAction.Down ||
                inputAction == GameInputManager.InputAction.Left ||
                inputAction == GameInputManager.InputAction.Right ||
                inputAction == GameInputManager.InputAction.ShortCutUp ||
                inputAction == GameInputManager.InputAction.ShortCutDown ||
                inputAction == GameInputManager.InputAction.ShortCutLeft ||
                inputAction == GameInputManager.InputAction.ShortCutRight ||
                _modifierSuppressSet.Contains(inputAction))
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

        /// <summary>Last fishing result announcement text, for dedup.</summary>
        private static string _lastFishingAnnouncement;
        /// <summary>Timestamp of last fishing result announcement.</summary>
        private static float _lastFishingAnnouncementTime;

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

                    // Dedup: the game calls Set() many times per catch. Only announce once.
                    float now = Time.time;
                    if (announcement == _lastFishingAnnouncement &&
                        now - _lastFishingAnnouncementTime < 2f)
                        return;
                    _lastFishingAnnouncement = announcement;
                    _lastFishingAnnouncementTime = now;

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
