# Code Index: EnemyProximityHandler.cs

## Top-Level Comments

- File-level XML doc (lines 9-13): "Plays a spatial audio cue when field enemies are nearby. Volume scales with distance, stereo pans with direction relative to the player. Scans for enemies periodically and tracks the closest one each frame."

---

## Class: EnemyProximityHandler (line 14)

Namespace: SO2RAccess

### Constants

- private const float MaxDistance = 25f (line 19)
  Note: Distance in Unity units beyond which the audio cue is silent and stopped.
- private const float MinDistance = 3f (line 22)
  Note: Distance in Unity units at or below which the cue plays at full volume (volume = 1.0).
- private const int ScanIntervalFrames = 60 (line 25)
  Note: How many frames pass between full FindObjectsOfType enemy scans to limit CPU cost.

### Fields

- private int _scanTimer (line 31)
- private readonly List<Transform> _cachedEnemies (line 32)
- private bool _isPlaying (line 33)

### Properties

None.

### Methods

#### Public

- public void Update() (line 43)
  Note: Called every frame from Main.UpdateHandlers(). Runs the full proximity loop: checks field state, runs periodic ScanEnemies(), finds the closest cached enemy, computes linear volume falloff and player-relative stereo pan, then calls SpatialAudioPlayer.SetVolumePan() and Start/Stop as needed.

- public void OnSceneChanged() (line 173)
  Note: Clears the enemy cache and resets _scanTimer to 0 (forcing an immediate scan next frame) and stops audio. Call this on every scene/map transition.

- public void ApplyPatches(HarmonyLib.Harmony harmony) (line 189)
  Note: No-op stub. This handler uses polling only and requires no Harmony patches. Present for interface consistency with other handlers.

#### Private

- private void ScanEnemies() (line 202)
  Note: Expensive full scene scan via FindObjectsOfType<FieldEnemy>(). Clears and repopulates _cachedEnemies with the Transform of every live FieldEnemy. Called only when _scanTimer expires, not every frame.

- private bool IsFieldFree() (line 232)
  Note: Returns true only when FieldManager.Instance is non-null, a control player exists, the camp menu is closed, and the shop is closed. Used to suppress audio during menus and battles.

- private bool CanActivate() (line 252)
  Note: Lightweight pre-check — returns true if FieldManager.Instance is non-null. Guards the top of Update() to avoid any work when clearly not on the field.
