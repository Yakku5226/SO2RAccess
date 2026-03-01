# Code Index: DialogueHandler.cs

## Top-level comments

Lines 13-34: XML doc comment on the class. Describes:
- The single Harmony patch applied: `UIConversationPresenter.SetMessage(string, string, string, bool, ref Rect)` (postfix)
- Why one patch covers all dialogue types: all public `SetMessage` overloads delegate to this internal overload after resolving messageID lookups.
- The NPC name-learning system and the two maps it maintains (`NpcDisplayNames` and `PersistentNpcNames`), including their purposes, lifetimes, and who reads them.

---

## Class: DialogueHandler (line 35)

Namespace: `SO2RAccess`

### Fields

- `private bool _patchesApplied` (line 39)
  Note: Guard flag — prevents `ApplyPatches` from registering the same Harmony patch more than once.

- `private static readonly Regex _tagStripper` (line 40)
  Note: Pre-compiled regex `<[^>]+>` used by `StripTags` to remove TextMeshPro rich-text markup.

- `internal static readonly Dictionary<int, string> NpcDisplayNames` (lines 48-49)
  Note: Runtime-only map. Key = `FieldNpcCharacter` instance ID (reassigned each session). Value = dialogue display name. Populated as the player speaks to NPCs; read by `NavigationHandler.ResolveNpcName` as a fast first-pass lookup.

- `internal static readonly Dictionary<string, string> PersistentNpcNames` (lines 59-60)
  Note: Cross-session map. Key = `ConstNpcParameter` code name (e.g. `"NPC_0003_01a_17_GRANDFATHER2"`). Value = dialogue display name. Loaded from disk at startup; appended whenever a new name is learned. Read by `NavigationHandler` to show real names before the player has spoken to an NPC this session.

- `private static string _persistPath` (line 62)
  Note: Absolute path to `UserData\SO2RAccess_npc_names.txt`, set in `LoadPersistentNames`. Null until that method runs.

### Methods

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 74)
  Note: Entry point called from `Main`. Loads persistent NPC names from disk, then registers the `SetMessage` postfix patch. Uses `RuntimeHelpers.RunClassConstructor` to force IL2CPP class initialization before patching. Safe to call multiple times (guarded by `_patchesApplied`).

- `private static void ConversationPresenter_SetMessage_Postfix(string message, string talkerName)` (line 118)
  Note: Harmony postfix — fires after every `UIConversationPresenter.SetMessage` call. Strips rich-text tags from both parameters, calls `TryRecordNpcName` if a speaker name is present, then announces the result via `ScreenReader.Say`. Uses `Loc.Get` with keys `"dialogue_no_name"` or `"dialogue_with_name"` depending on whether a name is present.

- `private static string StripTags(string text)` (line 158)
  Note: Runs `_tagStripper` (the compiled regex) on the input and trims whitespace. Returns the input unchanged if it is null or empty.

- `private static void TryRecordNpcName(string displayName)` (lines 170)
  Note: Finds the nearest `FieldNpcCharacter` within 15 units of the player (excluding the player object itself), then stores `displayName` in `NpcDisplayNames` (by instance ID) and, if the code name is new, also in `PersistentNpcNames` (by `ConstNpcParameter.Name`) and appends it to disk via `AppendPersistentName`. Silently returns if `FieldManager` is unavailable (handles cutscene narration gracefully). Uses a 15-unit ceiling to avoid associating cutscene narration with a random distant NPC.

- `private static void LoadPersistentNames()` (line 260)
  Note: Reads `UserData\SO2RAccess_npc_names.txt` at startup and populates `PersistentNpcNames`. File format: one `codeName|displayName` entry per line. Sets `_persistPath` as a side effect. Called once from `ApplyPatches`.

- `private static void AppendPersistentName(string codeName, string displayName)` (line 300)
  Note: Appends a single `codeName|displayName\n` line to the persistent file. Only called for genuinely new entries (deduplication is done in `TryRecordNpcName` before this is called). No-ops if `_persistPath` is null.
