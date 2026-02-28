using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SO2RAccess
{
    /// <summary>
    /// Announces tutorial text boxes, dialog popups, and description popups
    /// (e.g. acquired battle art notifications) to the screen reader.
    ///
    /// Patches applied:
    ///   UITutorialInformationPresenter.SetInformation — fires on each tutorial page
    ///       display; announces the page title and body text.
    ///   UIDialogPresenter.Setup — fires for simple yes/no and OK dialog boxes;
    ///       announces the dialog message.
    ///   UIDialogPresenter.SelectChoices — fires when the cursor moves between Yes/No/OK
    ///       buttons; announces the focused button label.
    ///   UIDialogWindow.SetupDescription — fires for description-style popups such
    ///       as acquired battle arts; announces the name and description text.
    /// </summary>
    public class NotificationHandler
    {
        #region Fields

        private bool _patchesApplied = false;
        private static readonly Regex _tagStripper = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Set to true by DialogPresenter_Setup_Postfix so that the immediate
        /// SelectChoices call made during Setup initialization is suppressed.
        /// The initial choice is already included in the Setup announcement.
        /// </summary>
        private static bool _skipNextSelectChoices = false;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for tutorial and notification announcements.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UITutorialInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIDialogPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIDialogWindow).TypeHandle);

                // Fires when a tutorial page is displayed or navigated to.
                harmony.Patch(
                    AccessTools.Method(typeof(UITutorialInformationPresenter),
                        nameof(UITutorialInformationPresenter.SetInformation)),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(TutorialInformation_SetInformation_Postfix))
                );

                // Fires for yes/no and OK dialog boxes.
                harmony.Patch(
                    AccessTools.Method(typeof(UIDialogPresenter), "Setup",
                        new Type[]
                        {
                            typeof(string),                 // message
                            typeof(UIDefine.DialogType),    // type
                            typeof(UIDefine.DialogChoices)  // choice
                        }),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(DialogPresenter_Setup_Postfix))
                );

                // Fires when the cursor moves between Yes/No/OK buttons.
                harmony.Patch(
                    AccessTools.Method(typeof(UIDialogPresenter), "SelectChoices",
                        new Type[]
                        {
                            typeof(UIDefine.DialogChoices), // choice
                            typeof(float)                   // cursorMoveTime
                        }),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(DialogPresenter_SelectChoices_Postfix))
                );

                // Fires for description-style popups (acquired arts, items, etc.).
                // Uses method name only to avoid matching complex optional-parameter types.
                harmony.Patch(
                    AccessTools.Method(typeof(UIDialogWindow), "SetupDescription"),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(DialogWindow_SetupDescription_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("NotificationHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"NotificationHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UITutorialInformationPresenter.SetInformation(UITutorialInformationData).
        /// Fires each time a tutorial page is shown. Announces the page title and body.
        /// </summary>
        private static void TutorialInformation_SetInformation_Postfix(
            UITutorialInformationData data)
        {
            try
            {
                if (data == null) return;

                string title = StripTags(data.title ?? "");
                string description = StripTags(data.description ?? "");

                if (string.IsNullOrEmpty(description) && string.IsNullOrEmpty(title)) return;

                string announcement = string.IsNullOrEmpty(title)
                    ? Loc.Get("tutorial_page_no_title", description)
                    : Loc.Get("tutorial_page", title, description);

                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("Tutorial",
                    $"title='{title}' desc='{description}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"TutorialInformation_SetInformation_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIDialogPresenter.Setup(string message, DialogType, DialogChoices).
        /// Fires for simple yes/no and OK dialogs. Announces the dialog question together
        /// with the initially focused button label so the user hears the full context.
        /// Sets a flag to suppress the redundant SelectChoices call that fires right after.
        /// </summary>
        private static void DialogPresenter_Setup_Postfix(
            UIDialogPresenter __instance, string message, UIDefine.DialogChoices choice)
        {
            try
            {
                string cleanMsg = StripTags(message ?? "");
                if (string.IsNullOrEmpty(cleanMsg)) return;

                string choiceLabel = GetChoiceLabel(__instance, choice);

                string announcement = string.IsNullOrEmpty(choiceLabel)
                    ? Loc.Get("dialog_message", cleanMsg)
                    : Loc.Get("dialog_message_with_choice", cleanMsg, choiceLabel);

                _skipNextSelectChoices = true;
                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("Dialog",
                    $"msg='{cleanMsg}' initialChoice={choice} label='{choiceLabel}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"DialogPresenter_Setup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIDialogPresenter.SelectChoices(DialogChoices, float).
        /// Fires each time the cursor moves between dialog buttons (Yes, No, OK).
        /// Skips the first call after Setup (that call is the initial state, already
        /// announced by DialogPresenter_Setup_Postfix). Announces on real navigation.
        /// </summary>
        private static void DialogPresenter_SelectChoices_Postfix(
            UIDialogPresenter __instance, UIDefine.DialogChoices choice)
        {
            try
            {
                // Suppress the automatic call made during dialog initialization.
                if (_skipNextSelectChoices)
                {
                    _skipNextSelectChoices = false;
                    return;
                }

                string text = GetChoiceLabel(__instance, choice);
                if (string.IsNullOrEmpty(text)) return;

                ScreenReader.Say(Loc.Get("dialog_choice", text));
                DebugLogger.LogGameValue("DialogChoice",
                    $"choice={choice} label='{text}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"DialogPresenter_SelectChoices_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIDialogWindow.SetupDescription(string message, string description, ...).
        /// Fires for description-style popups such as acquired battle arts.
        /// Announces the item or skill name followed by its description.
        /// </summary>
        private static void DialogWindow_SetupDescription_Postfix(
            string message, string description)
        {
            try
            {
                string cleanName = StripTags(message ?? "");
                string cleanDesc = StripTags(description ?? "");

                if (string.IsNullOrEmpty(cleanName) && string.IsNullOrEmpty(cleanDesc)) return;

                string announcement = string.IsNullOrEmpty(cleanDesc)
                    ? Loc.Get("dialog_description_no_desc", cleanName)
                    : Loc.Get("dialog_description", cleanName, cleanDesc);

                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("DialogDescription",
                    $"name='{cleanName}' desc='{cleanDesc}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"DialogWindow_SetupDescription_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Returns the displayed label text for the given dialog choice button.
        /// Cancel is treated the same as No (both dismiss the dialog).
        /// </summary>
        private static string GetChoiceLabel(
            UIDialogPresenter presenter, UIDefine.DialogChoices choice)
        {
            UIGameTextPresenter btn = choice switch
            {
                UIDefine.DialogChoices.Yes    => presenter.yes,
                UIDefine.DialogChoices.No     => presenter.no,
                UIDefine.DialogChoices.Cancel => presenter.no,
                _                             => presenter.Ok
            };
            return StripTags(btn?.gameText?.text ?? "");
        }

        /// <summary>
        /// Removes TextMeshPro rich-text markup tags from a string
        /// so only the plain text is announced.
        /// </summary>
        private static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return _tagStripper.Replace(text, "").Trim();
        }

        #endregion
    }
}
