# NotificationHandler.cs (710 lines)

Announces tutorial text boxes, dialog popups, description popups (e.g. acquired battle arts), item acquisition popups, location discoveries, reward windows, and stacked field notifications (EXP/Fol/level-up/items) to the screen reader. Patches: UITutorialInformationPresenter.SetInformation, UIDialogPresenter.Setup, UIDialogPresenter.SelectChoices, UIDialogWindow.SetupDescription, UIOverflowItemPresenter.SetItem, UIFieldLocationPointPresenter.Set, GameManager.GiveRewardWithWindow, UIFieldInformationStackSelector.ShowInformation.
namespace: SO2RAccess (line 11)
usings (non-System / notable only): HarmonyLib, Il2CppGame, Il2CppSystem.Collections.Generic, MelonLoader, System.Runtime.CompilerServices, System.Text, System.Text.RegularExpressions

## class NotificationHandler (line 26)
Announces tutorial text boxes, dialog popups, and description popups to the screen reader.

fields/properties (declaration order):
- _patchesApplied : bool (line 30)
- _spriteNameExtractor : static readonly Regex (line 33)  — extracts name from sprite tags e.g. "<sprite name=PS4_Cross>" → "Cross"
- _tagStripper : static readonly Regex (line 36)  — strips any remaining rich text tags
- _spritePrefixes : static readonly string[] (line 38)  — controller-type prefixes stripped from sprite names ("PS5_", "PS4_", "Xbox_", "Switch_", "PC_", "Gamepad_")
- _skipNextSelectChoices : static bool (line 46)  — suppresses the redundant SelectChoices call fired automatically during dialog Setup
- _notificationQueue : static readonly List<string> (line 53)  — queue for stacked field notifications (EXP, Fol, items, level-ups) that fire in rapid succession
- _notificationFlushTimer : static float (line 57)  — time remaining before queue is flushed and announced
- NotificationFlushDelay : const float = 0.5f (line 60)  — seconds to wait for more notifications before announcing

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 71)
  - note: runs RuntimeHelpers.RunClassConstructor for 9 IL2CPP types before patching; applies 7 Harmony postfixes; safe to call multiple times (_patchesApplied guard).
- void TutorialInformation_SetInformation_Postfix(UITutorialInformationData data) (line 195)  [private static]
  - note: announces page title + description + optional operation/controls text via Loc.Get keys "tutorial_page", "tutorial_page_no_title", "tutorial_operation".
- void DialogPresenter_Setup_Postfix(UIDialogPresenter __instance, string message, UIDefine.DialogChoices choice) (line 235)  [private static]
  - note: announces dialog message together with initially focused button label; sets _skipNextSelectChoices=true to suppress the immediate SelectChoices call that fires during Setup.
- void DialogPresenter_SelectChoices_Postfix(UIDialogPresenter __instance, UIDefine.DialogChoices choice) (line 266)  [private static]
  - note: skips first call after Setup (consumed by _skipNextSelectChoices flag); then announces the focused button label on real navigation.
- void DialogWindow_SetupDescription_Postfix(string message, string description) (line 297)  [private static]
  - note: announces item/skill name + description for popup windows (acquired battle arts, etc.).
- void OverflowItemPresenter_SetItem_Postfix(UIOverflowItemPresenter __instance, List<OverflowResourceData> itemList) (line 327)  [private static]
  - note: reads __instance.message.text for popup heading; iterates itemList for name+count; announces combined string.
- void LocationPointPresenter_Set_Postfix(string name, string description) (line 383)  [private static]
- void GiveRewardWithWindow_Postfix(Il2CppSystem.Collections.Generic.List<ConstRewardParameter> rewardParameterList, string message, string description) (line 415)  [private static]
  - note: announces reward window message plus formatted reward list. Location point rewards bypass this (native-only flow).
- void FieldInformationStack_ShowInformation_Postfix(UIFieldInformationStackDataBase data) (line 457)  [private static]
  - note: TryCast to UIFieldItemInformationStackData for richer item announcements (getText + info + count + unit); falls back to plain info string. All queued via QueueNotification.
- void QueueNotification(string text) (line 528)  [private static]
  - note: adds to _notificationQueue and resets _notificationFlushTimer to NotificationFlushDelay.
- void Update() (line 539)  [public]
  - note: called each frame from Main.UpdateHandlers(); counts down flush timer; when expired, joins all queued messages with ". " and announces as one combined string.
- string GetChoiceLabel(UIDialogPresenter presenter, UIDefine.DialogChoices choice) (line 564)  [private static]
  - note: maps Yes→yes button, No/Cancel→no button, default→Ok button; strips tags from button text.
- string StripTagsPublic(string text) (line 580)  [public static]  — public wrapper for StripTags
- string StripControllerPrefixPublic(string spriteName) (line 586)  [public static]  — public wrapper for StripControllerPrefix
- string StripTags(string text) (line 594)  [private static]
  - note: first extracts sprite names via _spriteNameExtractor and strips controller prefix, then removes all remaining rich text tags.
- string StripControllerPrefix(string spriteName) (line 606)  [private static]
  - note: iterates _spritePrefixes; returns substring after matching prefix, or original if none match.
- string FormatRewardList(Il2CppSystem.Collections.Generic.List<ConstRewardParameter> rewards) (line 620)  [private static]
  - note: formats EXP/FOL/SP/BP/ITEM reward parameters using Loc.Get keys; calls FormatItemReward for ITEM type; returns null if empty.
- string FormatItemReward(int itemID, int count) (line 659)  [private static]
  - note: resolves item name via ParameterManager.GetItemParameter → itemNameID → TextManager.GetMessage(MessageType.Item); falls back to stripping "ITEM_" prefix and title-casing the key; final fallback is "item {itemID}".
