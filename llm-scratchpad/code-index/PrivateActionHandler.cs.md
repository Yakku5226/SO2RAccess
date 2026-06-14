# PrivateActionHandler.cs (86 lines)

No file-level comment block.
namespace: SO2RAccess (line 3)
usings: Il2CppGame, UnityEngine

## class PrivateActionHandler (line 13)
Detects when a private action is available in the current town and plays an audio cue.
PAs are a town-wide mode triggered by pressing Square; availability is determined by locality parameter's IsPrivateAction flag.

fields/properties (declaration order):
- _announced : bool (line 18)
- _pollTimer : float (line 19)
- PollInterval : const float = 1.0f (line 20)

methods (declaration order):
- void Update() (line 29)
  - note: Called each frame from Main.UpdateHandlers(). Throttled by PollInterval. Plays PrivateAction audio cue once per location visit (when not yet _announced); gates on FieldState.IsFieldFree() and IsPALocation().
- void OnSceneChanged() (line 48)
  - note: Resets _announced and sets _pollTimer = 2.0f (initial delay) on scene change so a new town announces again.
- bool IsPALocation() (line 64)
  - note: Checks FieldManager.Instance.FieldmapID → ParameterManager.GetLocalityParameter() → localityParam.IsPrivateAction. Wrapped in try/catch.
