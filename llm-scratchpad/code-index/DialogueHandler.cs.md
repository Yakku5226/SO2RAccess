# DialogueHandler.cs (390 lines)

Announces NPC dialogue text boxes to the screen reader.
Patches UIConversationPresenter.SetMessage (the single implementation called by all
SetMessage overloads after resolving messageID lookups). Supports two voice modes:
  Full (AlwaysReadFull): announces immediately in SetMessage postfix.
  NameOnlyWhenVoiced: defers 1 frame, polls voice state in ProcessPendingDialogue();
    if voice is playing, announces name only; otherwise announces name + full text.
NPC Name Learning: records talkerName → nearest FieldNpcCharacter in two maps:
  NpcDisplayNames (runtime, instance ID → display name, session-only).
  PersistentNpcNames (persistent, code name → display name, saved to disk as codeName|displayName).
NavigationHandler reads both maps in ResolveNpcName.

namespace: SO2RAccess (line 11)
usings (non-System / notable only): HarmonyLib, Il2CppCommon, Il2CppGame, MelonLoader, UnityEngine

## class DialogueHandler (line 35)
Announces NPC dialogue text to the screen reader; learns and persists NPC display names.

fields/properties (declaration order):
- _patchesApplied : bool (line 39)
- _pendingMessage : static string (line 43)  — deferred dialogue text awaiting voice state check
- _pendingName : static string (line 44)
- _pendingVoiceID : static string (line 45)
- _pendingFrame : static int (line 46)  — frame on which SetMessage postfix fired; ProcessPendingDialogue waits until frameCount > this
- _cachedSelector : static UIConversationSelector (line 49)  — cached for polling currentVoiceController.IsPlaying()
- NpcDisplayNames : static readonly Dictionary<int, string> (line 56)  — internal; runtime map: instance ID → display name (session-only). Read by NavigationHandler.
- PersistentNpcNames : static readonly Dictionary<string, string> (line 67)  — internal; persistent map: code name → display name (survives restarts). Read by NavigationHandler.
- _persistPath : static string (line 70)  — path to UserData\SO2RAccess_npc_names.txt

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 82)
  - note: Calls LoadPersistentNames() then patches UIConversationPresenter.SetMessage(string, string, string, bool, ref Rect) with postfix. Safe to call multiple times.

- static void ConversationPresenter_SetMessage_Postfix(string message, string talkerName, string voiceID) (line 126)
  - note: Postfix for UIConversationPresenter.SetMessage. Strips tags from message/name. If Full mode: announces immediately. If NameOnlyWhenVoiced: saves to pending fields and records _pendingFrame for 1-frame deferral. Also calls TryRecordNpcName if talkerName non-empty.

- static void ProcessPendingDialogue() (line 182)
  - note: Called from Main.OnLateUpdate(). Waits until frameCount > _pendingFrame, then polls _cachedSelector.currentVoiceController.IsPlaying(). Announces name-only if voiced, full text otherwise.

- static string StripTags(string text) (line 229)
  - note: Delegates to TextUtil.StripTags(text).

- static void TryRecordNpcName(string displayName) (line 237)
  - note: Finds nearest FieldNpcCharacter within 15 units (skipping player), records instance ID → displayName in NpcDisplayNames. Also looks up ConstNpcParameter by spawn position to get code name; if new and not ev_* prefix, appends to PersistentNpcNames and calls AppendPersistentName.

- static void LoadPersistentNames() (line 332)
  - note: Reads UserData\SO2RAccess_npc_names.txt at startup. Each line: codeName|displayName. Skips ev_* prefixed entries (legacy bad saves). Called once from ApplyPatches.

- static void AppendPersistentName(string codeName, string displayName) (line 375)
  - note: Appends a single new "codeName|displayName\n" line to disk. Only called for genuinely new entries.
