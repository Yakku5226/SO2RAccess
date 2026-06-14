# GuildHandler.cs (169 lines)

Announces guild mission menu events to the screen reader. The guild mission UI
operates entirely in native C++; all managed data accessors return empty/stale
values. Only window open/close detection via gameObject.activeInHierarchy works.
The dialogue system catches "Mission accepted.", provisions, and "no more missions".
Individual mission names and cursor position cannot be read from managed code
(extensively tested — confirmed native code wall).
namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class GuildHandler (line 28)
Announces guild mission menu open/close; mission names unreadable due to native wall.

fields/properties (declaration order):
- _patchesApplied : bool (line 32)
- _missionWindow : static UIMissionWindow (line 34)
- _guildOpen : static bool (line 35)
- _findCooldown : static int (line 36)
- IsGuildOpen : static bool (line 39)  — public property; true while guild mission screen is open

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 48)
  - note: Patches GameUIManager.OpenMissionWindow (postfix) to capture the UIMissionWindow reference.
- void OpenMissionWindow_Postfix(UIMissionWindow __result) (line 72)
  - note: Postfix for GameUIManager.OpenMissionWindow. Caches __result as _missionWindow.
- void OnSceneChanged() (line 79)
- void Update() (line 91)
  - note: Called every frame from Main.UpdateHandlers(). Guards against camp menu being open before calling DetectMissionWindow.
- void DetectMissionWindow() (line 104)
  - note: Polls _missionWindow.gameObject.activeInHierarchy with a 60-frame find cooldown. On open, announces Loc.Get("guild_screen"). Falls back to GetWindow(UIDefine.WindowType.Mission) then FindObjectOfType if reference is null.
