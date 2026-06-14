# EnemyProximityHandler.cs (263 lines)

Plays a spatial audio cue when field enemies are nearby. Volume scales with distance (linear falloff), stereo pan scales with direction relative to player forward. Scans for enemies periodically via FindObjectsOfType; tracks closest enemy each frame.
namespace: SO2RAccess (line 8)
usings: Il2CppGame, MelonLoader, UnityEngine

## class EnemyProximityHandler (line 14)

fields/properties (declaration order):
- MaxDistance : float (line 19)  [— const; beyond this distance cue is silent (25 units)]
- MinDistance : float (line 22)  [— const; at or below this distance cue plays at full volume (3 units)]
- ScanIntervalFrames : int (line 25)  [— const; frames between full FindObjectsOfType enemy scans (60)]
- _scanTimer : int (line 31)
- _cachedEnemies : List<Transform> (line 32)  [— readonly; live transforms from last scan; stale entries pruned each frame]
- _isPlaying : bool (line 33)

methods (declaration order):
- void Update() (line 43)
  - note: Called every frame from Main.UpdateHandlers(). Respects ModSettings.EnemyProximitySoundEnabled and UserVolume. Runs periodic ScanEnemies(), then finds closest cached enemy, computes linear volume and signed-angle pan, drives SpatialAudioPlayer.SetVolumePan/Start/Stop.
- void OnSceneChanged() (line 187)
  - note: Clears enemy cache, resets scan timer to 0 (forces immediate scan next frame), stops audio.
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 202)
  - note: No-op; included for handler interface consistency. All data read via polling.
- void ScanEnemies() (line 216)
  - note: Clears _cachedEnemies then calls FindObjectsOfType<FieldEnemy>() and caches each enemy's Transform.
- bool IsFieldFree() (line 243)
  - note: Delegates to FieldState.IsFieldFree().
- bool CanActivate() (line 249)
  - note: Quick pre-check; returns true if FieldManager.Instance is non-null.
