using HarmonyLib;
using Il2CppGame;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;
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

        /// <summary>Extracts the name from sprite tags (e.g. "&lt;sprite name=PS4_Cross&gt;" → "Cross").</summary>
        private static readonly Regex _spriteNameExtractor = new Regex(
            @"<sprite\s+name\s*=\s*([^>]+?)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>Strips any remaining rich text tags from game strings.</summary>
        private static readonly Regex _tagStripper = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Set to true by DialogPresenter_Setup_Postfix so that the immediate
        /// SelectChoices call made during Setup initialization is suppressed.
        /// The initial choice is already included in the Setup announcement.
        /// </summary>
        private static bool _skipNextSelectChoices = false;

        /// <summary>
        /// Queue for stacked field notifications (EXP, Fol, items, level-ups, etc.)
        /// that fire in rapid succession. Messages are collected and announced
        /// together after a short delay so the screen reader doesn't interrupt itself.
        /// </summary>
        private static readonly System.Collections.Generic.List<string> _notificationQueue =
            new System.Collections.Generic.List<string>();

        /// <summary>Time remaining before the notification queue is flushed and announced.</summary>
        private static float _notificationFlushTimer = 0f;

        /// <summary>Delay in seconds to wait for more notifications before announcing.</summary>
        private const float NotificationFlushDelay = 0.5f;

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
                RuntimeHelpers.RunClassConstructor(typeof(UIOverflowItemPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(OverflowResourceData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldLocationPointPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(GameManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstRewardParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstItemParameter).TypeHandle);

                RuntimeHelpers.RunClassConstructor(typeof(UIFieldInformationStackSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldInformationStackDataBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldItemInformationStackData).TypeHandle);

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

                // Fires when an item acquisition popup is populated (treasure chests,
                // quest rewards, etc.). CallerCount(3) — hookable.
                harmony.Patch(
                    AccessTools.Method(typeof(UIOverflowItemPresenter), "SetItem",
                        new Type[] { typeof(List<OverflowResourceData>) }),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(OverflowItemPresenter_SetItem_Postfix))
                );

                // Fires when a location discovery notification popup appears.
                // CallerCount(1) — hookable.
                harmony.Patch(
                    AccessTools.Method(typeof(UIFieldLocationPointPresenter), "Set",
                        new Type[] { typeof(string), typeof(string) }),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(LocationPointPresenter_Set_Postfix))
                );

                // Fires when the game gives rewards with a UI window (location points,
                // missions, etc.). CallerCount(6) — hookable. Announces reward contents.
                harmony.Patch(
                    AccessTools.Method(typeof(GameManager), "GiveRewardWithWindow",
                        new Type[]
                        {
                            typeof(Il2CppSystem.Collections.Generic.List<ConstRewardParameter>),
                            typeof(Il2CppSystem.Action<GameManager.IncreaseItemResult>),
                            typeof(bool),
                            typeof(string),
                            typeof(string),
                            typeof(GameDefine.Jingle)
                        }),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(GiveRewardWithWindow_Postfix))
                );

                // Fires every time a stacked field notification popup is shown
                // (EXP gained, Fol gained, items received, level-ups, talents,
                // skill learning, etc.). CallerCount(15) — hookable.
                harmony.Patch(
                    AccessTools.Method(typeof(UIFieldInformationStackSelector),
                        nameof(UIFieldInformationStackSelector.ShowInformation),
                        new Type[] { typeof(UIFieldInformationStackDataBase) }),
                    postfix: new HarmonyMethod(typeof(NotificationHandler),
                        nameof(FieldInformationStack_ShowInformation_Postfix))
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
                string operation = StripTags(data.operation ?? "");

                if (string.IsNullOrEmpty(description) && string.IsNullOrEmpty(title)) return;

                string announcement;
                if (string.IsNullOrEmpty(title))
                    announcement = Loc.Get("tutorial_page_no_title", description);
                else
                    announcement = Loc.Get("tutorial_page", title, description);

                // Append operation/controls text if present.
                if (!string.IsNullOrEmpty(operation))
                    announcement += " " + Loc.Get("tutorial_operation", operation);

                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("Tutorial",
                    $"title='{title}' desc='{description}' operation='{operation}'");
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

        /// <summary>
        /// Postfix for UIOverflowItemPresenter.SetItem(List&lt;OverflowResourceData&gt;).
        /// Fires when an item acquisition popup is populated (treasure chests, quest
        /// rewards, etc.). Announces the popup message and all acquired items.
        /// </summary>
        private static void OverflowItemPresenter_SetItem_Postfix(
            UIOverflowItemPresenter __instance, List<OverflowResourceData> itemList)
        {
            try
            {
                if (itemList == null || itemList.Count == 0) return;

                var sb = new StringBuilder();

                // Read the popup message text (e.g. "Obtained the following items").
                string msg = StripTags(__instance.message?.text ?? "");
                if (!string.IsNullOrEmpty(msg))
                {
                    sb.Append(msg);
                    sb.Append(" ");
                }

                // List each item with its count.
                for (int i = 0; i < itemList.Count; i++)
                {
                    var item = itemList[i];
                    if (item == null) continue;

                    string itemName = StripTags(item.name ?? "");
                    if (string.IsNullOrEmpty(itemName)) continue;

                    int count = item.count;
                    if (count > 1)
                        sb.Append(Loc.Get("overflow_item_multi", itemName, count));
                    else
                        sb.Append(Loc.Get("overflow_item", itemName));

                    if (i < itemList.Count - 1)
                        sb.Append(", ");
                }

                string announcement = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(announcement))
                {
                    ScreenReader.Say(announcement);
                    DebugLogger.LogGameValue("OverflowItem",
                        $"msg='{msg}' itemCount={itemList.Count}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"OverflowItemPresenter_SetItem_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIFieldLocationPointPresenter.Set(string name, string description).
        /// Fires when a location discovery notification popup appears on the field.
        /// Announces "Discovered [name]. [description]" to the screen reader.
        /// </summary>
        private static void LocationPointPresenter_Set_Postfix(string name, string description)
        {
            try
            {
                string cleanName = StripTags(name ?? "");
                string cleanDesc = StripTags(description ?? "");

                if (string.IsNullOrEmpty(cleanName) && string.IsNullOrEmpty(cleanDesc))
                    return;

                string announcement;
                if (!string.IsNullOrEmpty(cleanDesc))
                    announcement = Loc.Get("location_discovered_desc", cleanName, cleanDesc);
                else
                    announcement = Loc.Get("location_discovered", cleanName);

                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("LocationDiscovered",
                    $"name='{cleanName}' desc='{cleanDesc}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"LocationPointPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GameManager.GiveRewardWithWindow(List, Action, bool, string, string, Jingle).
        /// Fires when the game awards rewards with a popup window via managed code
        /// (missions, etc.). Location point rewards bypass this (native-only flow).
        /// </summary>
        private static void GiveRewardWithWindow_Postfix(
            Il2CppSystem.Collections.Generic.List<ConstRewardParameter> rewardParameterList,
            string message, string description)
        {
            try
            {
                if (rewardParameterList == null || rewardParameterList.Count == 0) return;

                var sb = new StringBuilder();

                // Include the popup message if present.
                string cleanMsg = StripTags(message ?? "");
                if (!string.IsNullOrEmpty(cleanMsg))
                {
                    sb.Append(cleanMsg);
                    sb.Append(" ");
                }

                string rewardText = FormatRewardList(rewardParameterList);
                if (!string.IsNullOrEmpty(rewardText))
                    sb.Append(rewardText);

                string announcement = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(announcement))
                {
                    ScreenReader.Say(announcement);
                    DebugLogger.LogGameValue("Reward",
                        $"count={rewardParameterList.Count} msg='{cleanMsg}'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"GiveRewardWithWindow_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIFieldInformationStackSelector.ShowInformation(UIFieldInformationStackDataBase).
        /// Fires every time a stacked field notification popup appears — EXP, Fol, items,
        /// level-ups, talents, battle skills, etc. Queues notifications for combined
        /// announcement after a short delay (see Update method).
        /// </summary>
        private static void FieldInformationStack_ShowInformation_Postfix(
            UIFieldInformationStackDataBase data)
        {
            try
            {
                if (data == null) return;

                string info = StripTags(data.information ?? "");

                // Check if this is an item-style notification with extra fields.
                var itemData = data.TryCast<UIFieldItemInformationStackData>();
                if (itemData != null)
                {
                    string getText = StripTags(itemData.getText ?? "");
                    int count = itemData.count;
                    string unit = StripTags(itemData.unit ?? "");

                    // Build a richer announcement for item notifications.
                    // getText is typically "Got" or similar prefix text.
                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(getText))
                    {
                        sb.Append(getText);
                        sb.Append(" ");
                    }
                    if (!string.IsNullOrEmpty(info))
                        sb.Append(info);
                    if (count > 0)
                    {
                        sb.Append(" x");
                        sb.Append(count);
                    }
                    if (!string.IsNullOrEmpty(unit))
                    {
                        sb.Append(" ");
                        sb.Append(unit);
                    }

                    string itemAnnouncement = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(itemAnnouncement))
                    {
                        QueueNotification(itemAnnouncement);
                        DebugLogger.LogGameValue("FieldInfoStack(item)",
                            $"getText='{getText}' info='{info}' count={count} unit='{unit}'");
                    }
                    return;
                }

                // Generic text notification (EXP, Fol, level-up, talent, etc.).
                if (!string.IsNullOrEmpty(info))
                {
                    QueueNotification(info);
                    DebugLogger.LogGameValue("FieldInfoStack", $"info='{info}'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"FieldInformationStack_ShowInformation_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Notification Queue

        /// <summary>
        /// Adds a message to the notification queue and resets the flush timer.
        /// Messages are held until no new notifications arrive for NotificationFlushDelay
        /// seconds, then announced together as a single combined message.
        /// </summary>
        private static void QueueNotification(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notificationQueue.Add(text);
            _notificationFlushTimer = NotificationFlushDelay;
        }

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). Counts down the flush timer
        /// and announces all queued notifications as one combined message when it expires.
        /// </summary>
        public void Update()
        {
            if (_notificationQueue.Count == 0) return;

            _notificationFlushTimer -= UnityEngine.Time.deltaTime;
            if (_notificationFlushTimer > 0f) return;

            // Flush: combine all queued messages into one announcement.
            string combined = string.Join(". ", _notificationQueue);
            _notificationQueue.Clear();
            _notificationFlushTimer = 0f;

            ScreenReader.Say(combined);
            DebugLogger.LogGameValue("FieldInfoStack(flush)",
                $"count={combined.Length} text='{combined}'");
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
        /// Public wrapper for StripTags, used by other handlers that need tag stripping.
        /// </summary>
        public static string StripTagsPublic(string text) => StripTags(text);

        /// <summary>
        /// Public wrapper for StripControllerPrefix, used by other handlers that parse
        /// button sprite names (e.g. "PS4_Cross" → "Cross").
        /// </summary>
        public static string StripControllerPrefixPublic(string spriteName) =>
            TextUtil.StripControllerPrefix(spriteName);

        /// <summary>
        /// Cleans rich text from a game string. Sprite tags have their name
        /// extracted and controller prefixes stripped (e.g. "&lt;sprite name=PS4_Cross&gt;"
        /// → "Cross"), then any remaining tags are removed.
        /// </summary>
        private static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = _spriteNameExtractor.Replace(text, m => TextUtil.StripControllerPrefix(m.Groups[1].Value));
            text = _tagStripper.Replace(text, "");
            return text.Trim();
        }

        /// <summary>
        /// Formats a list of ConstRewardParameter into a readable string.
        /// Used by GiveRewardWithWindow_Postfix.
        /// </summary>
        private static string FormatRewardList(
            Il2CppSystem.Collections.Generic.List<ConstRewardParameter> rewards)
        {
            if (rewards == null || rewards.Count == 0) return null;

            var sb = new StringBuilder();
            for (int i = 0; i < rewards.Count; i++)
            {
                var param = rewards[i];
                if (param == null) continue;

                RewardType type = param.rewardType;
                int val = param.value;
                int count = param.count;

                string rewardText = type switch
                {
                    RewardType.EXP  => Loc.Get("reward_exp", val),
                    RewardType.FOL  => Loc.Get("reward_fol", val),
                    RewardType.SP   => Loc.Get("reward_sp", val),
                    RewardType.BP   => Loc.Get("reward_bp", val),
                    RewardType.ITEM => FormatItemReward(val, count),
                    _               => null
                };

                if (string.IsNullOrEmpty(rewardText)) continue;

                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(rewardText);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Resolves an item reward to a readable string. Looks up the item name
        /// from ParameterManager and TextManager. Falls back to parsing the key
        /// if TextManager cannot resolve it.
        /// </summary>
        private static string FormatItemReward(int itemID, int count)
        {
            string name = null;
            string diagNameID = "(no param)", diagResolved = "(not tried)";
            try
            {
                var pm = ParameterManager.Instance;
                if (pm != null)
                {
                    var itemParam = pm.GetItemParameter(itemID);
                    if (itemParam != null)
                    {
                        string nameID = itemParam.itemNameID;
                        diagNameID = nameID ?? "(null)";
                        if (!string.IsNullOrEmpty(nameID))
                        {
                            // Try TextManager to resolve the name ID to a display name.
                            var tm = TextManager.Instance;
                            if (tm != null)
                            {
                                string resolved = tm.GetMessage(nameID, TextManager.MessageType.Item);
                                diagResolved = $"'{resolved}'";
                                if (!string.IsNullOrEmpty(resolved) && resolved != nameID)
                                    name = resolved;
                            }

                            // Fallback: parse the key (strip "ITEM_", title case),
                            // but ONLY when it yields real words. Some items have a
                            // purely-numeric name key (e.g. ITEM_0024) that TextManager
                            // can't resolve here — parsing those just produces "0024",
                            // so leave the name unresolved and let the caller skip it.
                            if (string.IsNullOrEmpty(name))
                            {
                                string key = nameID;
                                if (key.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase))
                                    key = key.Substring(5);
                                bool hasLetters = false;
                                foreach (char c in key)
                                    if (char.IsLetter(c)) { hasLetters = true; break; }
                                if (hasLetters)
                                    name = System.Globalization.CultureInfo.InvariantCulture
                                        .TextInfo.ToTitleCase(key.Replace("_", " ").ToLower());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FormatItemReward: failed for itemID={itemID}: {ex.Message}");
            }

            DebugLogger.LogState(
                $"FormatItemReward DIAG: itemID={itemID} itemNameID={diagNameID} " +
                $"textManager={diagResolved} -> name='{name ?? "(unresolved)"}'");

            // No readable name (e.g. a numeric-key item the reward window can't resolve).
            // Skip it rather than announce a raw code — the overflow-item toast already
            // announces the game's rendered name for the same reward.
            if (string.IsNullOrEmpty(name))
                return null;

            return count > 1
                ? Loc.Get("reward_item_multi", name, count)
                : Loc.Get("reward_item", name);
        }

        #endregion
    }
}
