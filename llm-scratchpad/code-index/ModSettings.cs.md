# ModSettings.cs (214 lines)

Persistent mod settings. Loads from and saves to JSON at
UserData/SO2RAccess/settings.json. All properties are static for easy access
from any handler. Uses a private SettingsData DTO for (de)serialization.
namespace: SO2RAccess (line 7)
usings (non-System / notable only): MelonLoader

## enum DialogueVoiceMode (line 12)
Controls how dialogue is announced when voice acting is present.

members:
- Full = 0 (line 14)  — always read speaker name and full text
- NameOnlyWhenVoiced = 1 (line 16)  — read only the speaker name when the line is voiced

## static class ModSettings (line 25)
Persistent mod settings backed by UserData/SO2RAccess/settings.json.

fields/properties (declaration order):
- SaveSoundEnabled : static bool { get; set; } = true (line 30)
- SaveSoundVolume : static float { get; set; } = 0.5f (line 33)
- DodgeSoundEnabled : static bool { get; set; } = true (line 36)
- DodgeSoundVolume : static float { get; set; } = 0.8f (line 39)
- EnemyProximitySoundEnabled : static bool { get; set; } = true (line 42)
- EnemyProximitySoundVolume : static float { get; set; } = 1.0f (line 45)
- DialogueVoiceMode : static DialogueVoiceMode { get; set; } = Full (line 48)
- AllyHealthWarningEnabled : static bool { get; set; } = true (line 51)
- AllyStatusAilmentEnabled : static bool { get; set; } = true (line 54)
- PlayerDamageDealtEnabled : static bool { get; set; } = true (line 57)
- PrivateActionSoundVolume : static float { get; set; } = 0.7f (line 60)  — 0 = off
- BonusGaugeSoundVolume : static float { get; set; } = 0.7f (line 63)  — 0 = off
- BonusGaugeBreakAnnouncementEnabled : static bool { get; set; } = true (line 66)
- BonusGaugePercentAnnounceEnabled : static bool { get; set; } = false (line 72)  — speech every 5% as gauge rises; default off
- JumpPromptSoundEnabled : static bool { get; set; } = true (line 78)
- JumpPromptSoundVolume : static float { get; set; } = 0.8f (line 81)
- JumpPromptSpeechEnabled : static bool { get; set; } = true (line 87)  — independent of audio cue toggle
- _settingsPath : static string (line 93)  — resolved at Load() time; empty until then

methods (declaration order):

- static void Load() (line 99)
  - note: Creates UserData/SO2RAccess/ directory if needed. If no file exists, calls Save() to write defaults. On load, clamps all float values to [0,1] and validates DialogueVoiceMode enum. Safe: falls back to defaults on any deserialization error.

- static void Save() (line 150)
  - note: Guards against empty _settingsPath (Load not yet called). Builds SettingsData DTO, serializes with WriteIndented=true. Logs warning on failure but does not throw.

## private class SettingsData (line 191)
JSON DTO — mirrors all ModSettings properties with the same defaults.
All members are auto-properties with default initializers.

fields/properties (declaration order):
- SaveSoundEnabled : bool { get; set; } = true (line 193)
- SaveSoundVolume : float { get; set; } = 0.5f (line 194)
- DodgeSoundEnabled : bool { get; set; } = true (line 195)
- DodgeSoundVolume : float { get; set; } = 0.8f (line 196)
- EnemyProximitySoundEnabled : bool { get; set; } = true (line 197)
- EnemyProximitySoundVolume : float { get; set; } = 1.0f (line 198)
- DialogueVoiceMode : int { get; set; } = 0 (line 199)  — stored as int; cast to enum on load
- AllyHealthWarningEnabled : bool { get; set; } = true (line 200)
- AllyStatusAilmentEnabled : bool { get; set; } = true (line 201)
- PlayerDamageDealtEnabled : bool { get; set; } = true (line 202)
- PrivateActionSoundVolume : float { get; set; } = 0.7f (line 203)
- BonusGaugeSoundVolume : float { get; set; } = 0.7f (line 204)
- BonusGaugeBreakAnnouncementEnabled : bool { get; set; } = true (line 205)
- BonusGaugePercentAnnounceEnabled : bool { get; set; } = false (line 206)
- JumpPromptSoundEnabled : bool { get; set; } = true (line 207)
- JumpPromptSoundVolume : float { get; set; } = 0.8f (line 208)
- JumpPromptSpeechEnabled : bool { get; set; } = true (line 209)
