# Code Index: NotificationHandler.cs

## Top-Level Comments

Lines 12-25: Class-level XML doc summary listing all four Harmony patches applied by this handler:
- UITutorialInformationPresenter.SetInformation — tutorial page display
- UIDialogPresenter.Setup — yes/no and OK dialog boxes
- UIDialogPresenter.SelectChoices — cursor movement between dialog buttons
- UIDialogWindow.SetupDescription — description-style popups (e.g. acquired battle arts)

---

## Class: NotificationHandler (line 26)

Namespace: SO2RAccess

### Fields

- `private bool _patchesApplied` (line 30)
- `private static readonly Regex _spriteNameExtractor` (line 33)
  Note: Extracts the name from Unity rich-text sprite tags, e.g. `<sprite name=PS4_Cross>` -> `"Cross"`
- `private static readonly Regex _tagStripper` (line 36)
  Note: Strips any remaining HTML/rich-text tags after sprite extraction
- `private static readonly string[] _spritePrefixes` (line 38)
  Note: Controller-type prefixes to remove from sprite names ("PS5_", "PS4_", "Xbox_", "Switch_", "PC_", "Gamepad_")
- `private static bool _skipNextSelectChoices` (line 46)
  Note: Set true by DialogPresenter_Setup_Postfix to suppress the automatic SelectChoices call that fires during dialog initialization; cleared on the first SelectChoices call after Setup
- `private static readonly System.Collections.Generic.List<string> _notificationQueue` (line 53)
  Note: Collects stacked field notifications (EXP, Fol, items, level-ups, etc.) that fire in rapid succession so they can be announced as one combined message
- `private static float _notificationFlushTimer` (line 57)
  Note: Countdown timer; queue is flushed when this reaches zero
- `private const float NotificationFlushDelay` (line 60)
  Note: Value = 0.5f — seconds to wait for more notifications before flushing the queue

---

### Methods

#### Patch Application

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 71)
  Note: Registers all seven Harmony postfixes. Calls RuntimeHelpers.RunClassConstructor on each target type before patching (IL2CPP initialization requirement). Safe to call multiple times — guarded by _patchesApplied. Patches applied:
  1. UITutorialInformationPresenter.SetInformation
  2. UIDialogPresenter.Setup (3-arg overload)
  3. UIDialogPresenter.SelectChoices (2-arg overload)
  4. UIDialogWindow.SetupDescription
  5. UIOverflowItemPresenter.SetItem
  6. UIFieldLocationPointPresenter.Set
  7. GameManager.GiveRewardWithWindow (6-arg overload)
  8. UIFieldInformationStackSelector.ShowInformation

#### Harmony Patch Methods

- `private static void TutorialInformation_SetInformation_Postfix(UITutorialInformationData data)` (line 195)
  Note: Announces tutorial page title and body; appends operation/controls text if present. Uses Loc keys: tutorial_page_no_title, tutorial_page, tutorial_operation.

- `private static void DialogPresenter_Setup_Postfix(UIDialogPresenter __instance, string message, UIDefine.DialogChoices choice)` (line 235)
  Note: Announces the dialog message combined with the initially-focused button label. Sets _skipNextSelectChoices = true to suppress the redundant SelectChoices call that immediately follows Setup. Uses Loc keys: dialog_message, dialog_message_with_choice.

- `private static void DialogPresenter_SelectChoices_Postfix(UIDialogPresenter __instance, UIDefine.DialogChoices choice)` (line 266)
  Note: Announces the newly-focused dialog button label (Yes, No, OK) on each cursor movement. Skips the very first call after Setup using the _skipNextSelectChoices flag. Uses Loc key: dialog_choice.

- `private static void DialogWindow_SetupDescription_Postfix(string message, string description)` (line 297)
  Note: Announces the name and description of description-style popups (e.g. acquired battle arts). Uses Loc keys: dialog_description_no_desc, dialog_description.

- `private static void OverflowItemPresenter_SetItem_Postfix(UIOverflowItemPresenter __instance, List<OverflowResourceData> itemList)` (line 327)
  Note: Announces the popup header text (e.g. "Obtained the following items") followed by all acquired items with counts. Uses Loc keys: overflow_item_multi, overflow_item.

- `private static void LocationPointPresenter_Set_Postfix(string name, string description)` (line 383)
  Note: Announces location discovery ("Discovered [name]. [description]"). Uses Loc keys: location_discovered_desc, location_discovered.

- `private static void GiveRewardWithWindow_Postfix(Il2CppSystem.Collections.Generic.List<ConstRewardParameter> rewardParameterList, string message, string description)` (line 415)
  Note: Announces rewards given via managed-code popup (missions, etc.). Does NOT handle location point rewards — those are native-only and are handled separately. Delegates reward formatting to FormatRewardList.

- `private static void FieldInformationStack_ShowInformation_Postfix(UIFieldInformationStackDataBase data)` (line 457)
  Note: Fires for every stacked field notification (EXP, Fol, items, level-ups, talents, battle skills). TryCasts to UIFieldItemInformationStackData for richer item announcements. Adds to _notificationQueue rather than announcing immediately, so rapid bursts are combined into one message.

#### Notification Queue

- `private static void QueueNotification(string text)` (line 528)
  Note: Adds a message to _notificationQueue and resets _notificationFlushTimer to NotificationFlushDelay (0.5s).

- `public void Update()` (line 539)
  Note: Called each frame from Main.UpdateHandlers(). Counts down _notificationFlushTimer; when it expires, joins all queued messages with ". " and announces the combined string via ScreenReader.Say.

#### Helpers

- `private static string GetChoiceLabel(UIDialogPresenter presenter, UIDefine.DialogChoices choice)` (line 564)
  Note: Returns the display text of the button corresponding to the given DialogChoices enum value. Cancel maps to the same button as No.

- `private static string StripTags(string text)` (line 582)
  Note: Two-pass tag cleaner. First pass replaces sprite tags with just the button name (stripping controller prefix). Second pass removes any remaining rich-text tags. Returns trimmed result.

- `private static string StripControllerPrefix(string spriteName)` (line 594)
  Note: Removes one matching controller prefix from _spritePrefixes (e.g. "PS4_Cross" -> "Cross"). Case-insensitive. Returns original string unchanged if no prefix matches.

- `private static string FormatRewardList(Il2CppSystem.Collections.Generic.List<ConstRewardParameter> rewards)` (line 608)
  Note: Formats a list of ConstRewardParameter entries into a comma-separated readable string. Handles RewardType.EXP, FOL, SP, BP, and ITEM. Delegates item name lookup to FormatItemReward. Returns null if list is empty or all entries produce no text.

- `private static string FormatItemReward(int itemID, int count)` (line 647)
  Note: Resolves an item ID to a display name via ParameterManager + TextManager. Falls back to parsing the itemNameID key (strips "ITEM_" prefix, converts underscores to spaces, applies title case) if TextManager returns nothing. Falls back further to "item {id}" if lookup throws. Uses Loc keys: reward_item_multi, reward_item.
