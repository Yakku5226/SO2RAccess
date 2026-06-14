# DialogueChoiceHandler.cs (425 lines)

namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, UnityEngine

## class DialogueChoiceHandler (line 25)
Announces dialogue choice menus (yes/no, story responses, inns) to the screen reader.
Pattern: polling detects presenter visibility (catches ALL menus incl. native-only); ShowSelectChoiceMessage hook captures title text when available; index polling tracks navigation (native-only cursor movement, no Harmony hook fires).

fields/properties (declaration order):
- _patchesApplied : bool (line 28)
- _window : UIConversationWindow (line 31)  — cached conversation window for accessing the choice selector
- _selector : static UISelectChoiceSelector (line 34)  — cached selector reference from the conversation window
- _choiceTexts : static string[] (line 37)  — cached choice texts for the current menu
- _lastIndex : static int (line 40)  — last announced choice index; -1 = none
- _isActive : static bool (line 43)  — whether the choice menu is currently active
- _wasPresenterVisible : bool (line 46)  — presenter visibility last frame (edge detection)
- _pendingTitle : static string (line 49)  — title/prompt text set by hook, consumed on next activation
- _activationPending : bool (line 55)  — true = presenter just became visible, waiting one frame for game to set correct selectChoiceIndex
- _deferredTitle : string (line 58)  — title captured at moment activation was deferred
- _findWindowTimer : float (line 61)  — throttles FindObjectOfType calls
- FindWindowInterval : const float = 2f (line 62)
- _diagCooldown : int (line 65)  — throttles diagnostic logging (~2 seconds at 60fps)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 75)
  - note: Applies two postfix patches on UISelectChoiceSelector.ShowSelectChoiceMessage (both overloads: with-title and no-title). Runs class constructors for IL2CPP type registration. Safe to call multiple times.
- static void ShowSelectChoiceMessage_Postfix(UISelectChoiceSelector __instance, string message) (line 130)
  - note: Postfix for ShowSelectChoiceMessage(string, List<string>). Captures title into _pendingTitle. Polling handles the actual announcement.
- static void ShowSelectChoiceMessageNoTitle_Postfix(UISelectChoiceSelector __instance) (line 148)
  - note: Postfix for ShowSelectChoiceMessage(List<string>). Sets _pendingTitle = null (no-title overload).
- void Update() (line 170)
  - note: Called each frame from Main.UpdateHandlers(). Caches selector via TryCacheSelector(); polls presenter.gameObject.activeInHierarchy for edge detection. On rising edge: defers activation by 1 frame (selectChoiceIndex is stale on frame 0). On second visible frame: calls ActivateChoiceMenu(). On falling edge: resets state. While active: polls selectChoiceIndex and announces changes via Loc.Get("dialogue_choice_item").
- void TryCacheSelector() (line 292)
  - note: Throttled (FindWindowInterval). FindObjectOfType<UIConversationWindow>() then reads .selectChoiceSelector field.
- void ActivateChoiceMenu(string title) (line 322)
  - note: Sets _isActive=true, reads choice texts, builds combined heading+initial-item announcement from Loc keys (dialogue_choice_open_with_title / dialogue_choice_open / dialogue_choice_open_no_items). Strips tags via NotificationHandler.StripTagsPublic().
- void ReadChoiceTexts() (line 367)
  - note: Reads from presenter.choicePresenterList; uses choiceMessageIDList.Count for active-choice count (avoids pre-allocated slots). Strips tags on each text.
- static string GetChoiceText(int index) (line 416)
  - note: Bounds-checked read of _choiceTexts[]; returns "" if null or out of range.
