using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces gamepad (controller) binding menu navigation to the screen reader.
    ///
    /// Patches applied:
    ///   UIKeyConfigSelectItemPresenter.OnSelected  — announces focused item on navigation.
    ///     Reads label (action name) and pressKeyText / icon (button name) from the presenter.
    ///     Button names may be sprite tags (e.g. "&lt;sprite name=PS4_Cross&gt;") — the prefix
    ///     is stripped so the user hears "Cross" rather than "PS4_Cross".
    ///   UIKeyConfigSelector.OnDecision             — announces button-capture prompt.
    ///   UIKeyConfigSelector.UpdateAliasAction      — re-announces item after button is assigned.
    /// </summary>
    public class GamepadMenuHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Cached reference to the currently focused item presenter.
        // Used by UpdateAliasAction to re-read the button name after a new binding is set.
        private static UIKeyConfigSelectItemPresenter _selectedPresenter;

        // Prefixes the game uses in sprite names to indicate controller type.
        // Stripped so the user hears "Cross" instead of "PS4_Cross".
        private static readonly string[] _spritePrefixes = new[]
        {
            "PS5_", "PS4_", "Xbox_", "Switch_", "PC_", "Gamepad_"
        };

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the gamepad binding menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIKeyConfigSelectItemPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIKeyConfigSelector).TypeHandle);

                // Fires when the user navigates to a new item in the list.
                harmony.Patch(
                    AccessTools.Method(typeof(UIKeyConfigSelectItemPresenter),
                        nameof(UIKeyConfigSelectItemPresenter.OnSelected)),
                    postfix: new HarmonyMethod(typeof(GamepadMenuHandler),
                        nameof(GamepadItem_OnSelected_Postfix))
                );

                // Fires when the user presses confirm on an action to start button capture.
                harmony.Patch(
                    AccessTools.Method(typeof(UIKeyConfigSelector),
                        nameof(UIKeyConfigSelector.OnDecision)),
                    postfix: new HarmonyMethod(typeof(GamepadMenuHandler),
                        nameof(GamepadSelector_OnDecision_Postfix))
                );

                // Fires after a new button has been assigned and the alias binding updated.
                // Re-reads the presenter to announce the new assignment.
                harmony.Patch(
                    AccessTools.Method(typeof(UIKeyConfigSelector),
                        nameof(UIKeyConfigSelector.UpdateAliasAction)),
                    postfix: new HarmonyMethod(typeof(GamepadMenuHandler),
                        nameof(GamepadSelector_UpdateAliasAction_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("DIAG: GamepadMenuHandler patches applied OK.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"GamepadMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UIKeyConfigSelectItemPresenter.OnSelected(SelectItemDataBase).
        /// Fires whenever the cursor moves to this item. Reads the action name and
        /// assigned button from the presenter's own GameText fields.
        /// </summary>
        private static void GamepadItem_OnSelected_Postfix(
            UIKeyConfigSelectItemPresenter __instance, SelectItemDataBase itemData)
        {
            try
            {
                _selectedPresenter = __instance;
                var selector = __instance.GetComponentInParent<UIKeyConfigSelector>();
                AnnouncePresenter(__instance, selector);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"GamepadItem_OnSelected_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIKeyConfigSelector.OnDecision.
        /// If the state has changed to CommonChanging or BattleChanging, the user confirmed
        /// an action and the game is now waiting for a button press.
        /// </summary>
        private static void GamepadSelector_OnDecision_Postfix(UIKeyConfigSelector __instance)
        {
            try
            {
                if (__instance == null) return;
                var state = __instance.currentState;
                if (state == UIKeyConfigSelector.State.CommonChanging ||
                    state == UIKeyConfigSelector.State.BattleChanging)
                    ScreenReader.Say(Loc.Get("gamepad_press_button"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"GamepadSelector_OnDecision_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIKeyConfigSelector.UpdateAliasAction(InputAction).
        /// Called after a new button binding is saved. Re-reads the cached presenter,
        /// which now reflects the new assignment, and announces it.
        /// </summary>
        private static void GamepadSelector_UpdateAliasAction_Postfix(
            UIKeyConfigSelector __instance, GameInputManager.InputAction action)
        {
            try
            {
                if (_selectedPresenter == null || __instance == null) return;
                AnnouncePresenter(_selectedPresenter, __instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"GamepadSelector_UpdateAliasAction_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Reads the action name and assigned button from the presenter and announces the result.
        /// Button names may arrive as sprite tags (e.g. "&lt;sprite name=PS4_Cross&gt;").
        /// The tag is stripped and the controller-type prefix removed so the user hears
        /// a plain name like "Cross" regardless of which controller is connected.
        /// </summary>
        private static void AnnouncePresenter(
            UIKeyConfigSelectItemPresenter presenter,
            UIKeyConfigSelector selector)
        {
            if (presenter == null) return;

            string actionName = presenter.label?.text ?? "";
            if (string.IsNullOrEmpty(actionName)) return;

            // icon is the per-item assigned button sprite (e.g. "<sprite name=PS4_Cross>").
            // pressKeyText is the "Press a button to assign." prompt — only use it as a
            // fallback and only if it looks like a sprite tag, not plain prompt text.
            string buttonName = ExtractButtonName(presenter.icon?.text ?? "");
            if (string.IsNullOrEmpty(buttonName))
            {
                string pressText = presenter.pressKeyText?.text ?? "";
                if (pressText.StartsWith("<sprite"))
                    buttonName = ExtractButtonName(pressText);
            }

            int idx = -1;
            int count = 0;
            if (selector != null)
            {
                idx = selector.currentIndex;
                count = selector.presenterList?.Count ?? 0;
            }

            DebugLogger.LogGameValue("GamepadMenu.item",
                $"{actionName}: '{buttonName}' ({idx + 1}/{count})");

            if (string.IsNullOrEmpty(buttonName))
                ScreenReader.Say(Loc.Get("gamepad_item_no_button", actionName, idx + 1, count));
            else
                ScreenReader.Say(Loc.Get("gamepad_item", actionName, buttonName, idx + 1, count));
        }

        /// <summary>
        /// Extracts a plain button name from a raw GameText string.
        /// If the string is a sprite tag (&lt;sprite name=X&gt;), the name is extracted and
        /// any controller-type prefix (PS4_, Xbox_, etc.) is stripped.
        /// Plain text is returned trimmed.
        /// </summary>
        private static string ExtractButtonName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            if (raw.StartsWith("<sprite name=") && raw.EndsWith(">"))
            {
                string spriteName = raw.Substring(13, raw.Length - 14);
                return StripControllerPrefix(spriteName);
            }

            return raw.Trim();
        }

        /// <summary>
        /// Removes a controller-type prefix from a sprite name.
        /// For example "PS4_Cross" becomes "Cross", "Xbox_A" becomes "A".
        /// </summary>
        private static string StripControllerPrefix(string spriteName)
        {
            foreach (var prefix in _spritePrefixes)
            {
                if (spriteName.StartsWith(prefix))
                    return spriteName.Substring(prefix.Length);
            }
            return spriteName;
        }

        #endregion
    }
}
