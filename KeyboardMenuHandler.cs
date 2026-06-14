using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces keyboard binding menu navigation to the screen reader.
    ///
    /// Patches applied:
    ///   UIConfigKeyboardListItemPresenter.OnSelected — announces focused item on navigation.
    ///     Reads actionName and pressKeyText directly from the presenter GameText fields,
    ///     which always show exactly what is on screen (unlike the data's label field,
    ///     which does not contain the assigned key).
    ///   UIConfigKeyboardListSelector.OnDecision      — announces key-capture prompt.
    ///   UIConfigKeyboardListSelector.UpdateAfterKeyboard — re-announces item after key assignment.
    /// </summary>
    public class KeyboardMenuHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Cached reference to the currently focused item presenter.
        // Used by UpdateAfterKeyboard to re-read pressKeyText after the new key is set.
        private static UIConfigKeyboardListItemPresenter _selectedPresenter;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the keyboard binding menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIConfigKeyboardListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIConfigKeyboardListItemPresenter).TypeHandle);

                // Fires when the user navigates to a new item in the list.
                // This is the primary source for "action: key, N of total".
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigKeyboardListItemPresenter),
                        nameof(UIConfigKeyboardListItemPresenter.OnSelected)),
                    postfix: new HarmonyMethod(typeof(KeyboardMenuHandler),
                        nameof(KeyboardListItem_OnSelected_Postfix))
                );

                // Fires when the user presses confirm on an action to start key capture.
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigKeyboardListSelector),
                        nameof(UIConfigKeyboardListSelector.OnDecision)),
                    postfix: new HarmonyMethod(typeof(KeyboardMenuHandler),
                        nameof(KeyboardList_OnDecision_Postfix))
                );

                // Fires after a new key has been assigned and the list display refreshed.
                // Re-reads pressKeyText from the cached presenter to announce the new binding.
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigKeyboardListSelector),
                        nameof(UIConfigKeyboardListSelector.UpdateAfterKeyboard)),
                    postfix: new HarmonyMethod(typeof(KeyboardMenuHandler),
                        nameof(KeyboardList_UpdateAfterKeyboard_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("KeyboardMenuHandler patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"KeyboardMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UIConfigKeyboardListItemPresenter.OnSelected(ListItemDataBase).
        /// Fires whenever the cursor moves to this item. Reads actionName and pressKeyText
        /// from the presenter's own GameText fields for the most accurate on-screen values.
        /// </summary>
        private static void KeyboardListItem_OnSelected_Postfix(
            UIConfigKeyboardListItemPresenter __instance, ListItemDataBase itemData)
        {
            try
            {
                _selectedPresenter = __instance;

                var selector = __instance.GetComponentInParent<UIConfigKeyboardListSelector>();
                AnnouncePresenter(__instance, selector);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"KeyboardListItem_OnSelected_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIConfigKeyboardListSelector.OnDecision.
        /// If the state has changed to KeyboardChanging, the user confirmed an action
        /// and the game is now waiting for a key press.
        /// </summary>
        private static void KeyboardList_OnDecision_Postfix(UIConfigKeyboardListSelector __instance)
        {
            try
            {
                if (__instance == null) return;
                if (__instance.currentState == UIConfigKeyboardListSelector.State.KeyboardChanging)
                    ScreenReader.Say(Loc.Get("keyboard_press_key"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"KeyboardList_OnDecision_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIConfigKeyboardListSelector.UpdateAfterKeyboard.
        /// Called after the new key binding is saved and the list display is refreshed.
        /// Re-reads pressKeyText from the cached presenter, which now shows the new key.
        /// </summary>
        private static void KeyboardList_UpdateAfterKeyboard_Postfix(
            UIConfigKeyboardListSelector __instance)
        {
            try
            {
                if (_selectedPresenter == null) return;
                AnnouncePresenter(_selectedPresenter, __instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"KeyboardList_UpdateAfterKeyboard_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Reads actionName and the assigned key from the presenter and announces the result.
        /// Optionally uses the selector for position (N of total).
        /// The key name is stored in the Icon child GameText as a TextMeshPro sprite tag
        /// (e.g. "&lt;sprite name=Space&gt;"). We strip the tag to get the plain key name.
        /// </summary>
        private static void AnnouncePresenter(
            UIConfigKeyboardListItemPresenter presenter,
            UIConfigKeyboardListSelector selector)
        {
            if (presenter == null) return;

            string actionName = presenter.actionName?.text ?? "";
            if (string.IsNullOrEmpty(actionName)) return;

            // Read the bound key from the Icon child GameText.
            // The game stores it as "<sprite name=KEYNAME>" — strip the wrapper.
            string assignedKey = "";
            var allTexts = presenter.GetComponentsInChildren<GameText>(true);
            if (allTexts != null)
            {
                foreach (var gt in allTexts)
                {
                    if (gt?.gameObject?.name == "Icon")
                    {
                        string raw = gt.text ?? "";
                        if (raw.StartsWith("<sprite name=") && raw.EndsWith(">"))
                            assignedKey = raw.Substring(13, raw.Length - 14);
                        break;
                    }
                }
            }

            int idx = -1;
            int count = 0;
            if (selector != null)
            {
                idx = selector.currentIndex;
                count = selector.currentDataList?.Count ?? 0;
            }

            DebugLogger.LogGameValue("KeyboardMenu.item",
                $"{actionName}: '{assignedKey}' ({idx + 1}/{count})");

            if (string.IsNullOrEmpty(assignedKey))
                ScreenReader.Say(Loc.Get("keyboard_item_no_key", actionName, idx + 1, count));
            else
                ScreenReader.Say(Loc.Get("keyboard_item", actionName, assignedKey, idx + 1, count));
        }

        #endregion
    }
}
