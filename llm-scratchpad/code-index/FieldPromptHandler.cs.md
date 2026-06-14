# FieldPromptHandler.cs (346 lines)

Handles field "operation" prompts (world-space button guide above player): detects the jump-down prompt
via UIFieldOperationPresenter.Set hook [CallerCount(7), confirmed hookable], plays audio cue and/or
speaks once. Jump detection uses action word "Jump" (not isPlayer flag — presenter is shared with other prompts).
Hide is native-only (no managed hook), so hiding is detected by polling presenter inactive or text cleared.
Debug mode logs every prompt under [GAME] FieldPrompt for cataloguing.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, System.Text.RegularExpressions

## class FieldPromptHandler (line 33)
Detects and announces the one-way jump-down ledge prompt via hook + per-frame polling for hide detection.

fields/properties (declaration order):
- _patchesApplied : bool = false (line 36)
- JumpAction : const string = "Jump" (line 39)  — action word discriminator for jump-down prompt (English build)
- _operationParser : static readonly Regex (line 42)  — parses "<sprite name=BUTTON>ACTION" entries; compiled, IgnoreCase
- _jumpShowing : static bool = false (line 47)  — true while jump prompt is currently showing
- _jumpPresenter : static UIFieldOperationPresenter (line 50)  — cached presenter for hide polling
- _lastSignature : static string = "" (line 53)  — dedup key for debug logging
- _lastLogTime : static float = -100f (line 54)
- DedupWindow : const float = 2f (line 55)  — seconds before repeating a log entry

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 65)
  - note: Patches UIFieldOperationPresenter.Set (name-only lookup — only one overload). Safe to call repeatedly.
- static void FieldOperationPresenter_Set_Postfix(UIFieldOperationPresenter __instance, Il2CppSystem.Collections.Generic.List<string> operationList, UnityEngine.Transform followTransform, bool isPlayer) (line 100)
  - note: Postfix for UIFieldOperationPresenter.Set(...). Calls TryFindAction for JumpAction; announces once on new appearance (_jumpShowing false → true). If a non-jump prompt replaces the jump prompt on the same presenter, clears _jumpShowing. Always calls LogPromptDebug.
- void Update() (line 147)
  - note: Called each frame from Main.UpdateHandlers(). Short-circuits if !_jumpShowing. Calls IsJumpStillShowing(); clears _jumpShowing and _jumpPresenter when prompt disappears.
- private static bool IsJumpStillShowing() (line 162)
  - note: Checks presenter.gameObject.activeInHierarchy then scans operationTextList for JumpAction substring (raw text, no tag strip, avoids per-frame regex). Any IL2CPP access failure returns false.
- private static void AnnounceJump(string button) (line 201)
  - note: Plays AudioCuePlayer.PlayJumpCue() if ModSettings.JumpPromptSoundEnabled. Speaks jump_prompt / jump_prompt_no_button Loc key if ModSettings.JumpPromptSpeechEnabled.
- private static bool TryFindAction(Il2CppSystem.Collections.Generic.List<string> operationList, string action, out string button) (line 225)
  - note: Iterates operationList, calls ParseOperation on each entry, returns true + button glyph on first action match.
- private static void ParseOperation(string raw, out string button, out string action) (line 252)
  - note: Applies _operationParser regex; on match, strips controller prefix from group 1 (button) and tags from group 2 (action). No-match fallback strips tags from entire string as action.
- private static void LogPromptDebug(UIFieldOperationPresenter presenter, Il2CppSystem.Collections.Generic.List<string> operationList, UnityEngine.Transform followTransform, bool isPlayer) (line 271)
  - note: No-op when !Main.DebugMode. Deduped by signature + DedupWindow seconds. Logs isPlayer, anchor gameObject name, raw tags-preserved operationList, cleaned display text.
- private static string JoinIl2CppStrings(Il2CppSystem.Collections.Generic.List<string> list) (line 301)
  - note: Formats list as "[0]=a | [1]=b" preserving raw sprite tags for log readability.
- private static string ReadDisplayText(UIFieldOperationPresenter presenter) (line 319)
  - note: Reads presenter.operationTextList GameText entries, strips tags, joins with spaces.
