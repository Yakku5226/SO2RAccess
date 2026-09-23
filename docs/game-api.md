# Star Ocean: The Second Story R — Game API Documentation

## Overview

- **Game:** Star Ocean: The Second Story R (Demo)
- **Engine:** Unity 2021.3.22f1 (IL2CPP)
- **Runtime:** net6
- **Architecture:** 64-bit
- **Developer:** SquareEnix
- **MelonLoader:** v0.7.2-ci.2394
- **Scripting note:** Game uses MoonSharp (Lua) for some logic — some behaviour may be in Lua scripts, not C#

---

## 1. Singleton Access Points

All major systems use `SingletonMonoBehaviour<T>` or `SingletonBasicMonoBehaviour<T>`.
Access pattern: `ClassName.Instance`

- `GameManager.Instance` — core game state (GameManager.cs, ~5,352 lines)
- `GameInputManager.Instance` — all input handling (GameInputManager.cs)
- `GameUIManager.Instance` — central UI management (GameUIManager.cs, ~2,945 lines)
- `TextManager.Instance` — text/message retrieval (TextManager.cs)
- `FieldManager.Instance` — field/overworld state (FieldManager.cs, ~7,529 lines)
- `BattleManager.Instance` — battle state (BattleManager.cs, ~5,044 lines)
- `ParameterManager.Instance` — player/game data (ParameterManager.cs, ~32,079 lines)
- `EventManager.Instance` — event/dialogue system (EventManager.cs, ~18,494 lines)
- `PartyManager.Instance` — party management
- `GameSaveManager.Instance` — save/load (GameSaveManager.cs, ~2,996 lines)
- `GameSoundManager.Instance` — audio
- `GameResourceManager.Instance` — asset loading
- `ItemManager.Instance` — item system

---

## 2. Game Key Bindings (DO NOT override in mod!)

**CRITICAL: All input goes through GameInputManager using the InputAction enum.**
The game does NOT use raw KeyCode — it maps controller buttons to InputAction values.
F-keys and NumPad keys are NOT in the InputAction enum and are safe for the mod.

### InputAction Enum (complete list)

- Invalid (0)
- Decision (1), Cancel (2)
- LeftStickUp (3), LeftStickDown (4), LeftStickRight (5), LeftStickLeft (6)
- RightStickUp (7), RightStickDown (8), RightStickRight (9), RightStickLeft (10)
- Up (11), Down (12), Right (13), Left (14)
- Auto (15), BackLog (16), PageLeft (17), PageRight (18)
- Start (19), BattleMember (20), Square (21), Triangle (22)
- SkillPageLeft (23), SkillPageRight (24)
- Sort (27), MissionReceiveAll (28), BattleResultBonus (29)
- TriggerLeft2 (30), TriggerRight2 (31)
- FieldWalk (32), FieldConversation (33), FieldFishing (34), FieldBaitFishing (35)
- FieldBaitSelect (36), FieldChangeMode (37)
- CampMenu (38)
- ShortCutUp (39), ShortCutDown (40), ShortCutLeft (41), ShortCutRight (42)
- FieldBunnyGetoff (43), FieldPsynardLanding (44), FieldPsynardUp (45), FieldPsynardDown (46)
- FieldPsynardHighSpeedAdvance (47), FieldPsynardFallBack (48)
- ShowShortcut (49), PickPocket (50), ToggleMinimap (51)
- FieldCurrentLocation (52)
- FieldCameraUp (53), FieldCameraDown (54), FieldCameraRight (55), FieldCameraLeft (56)
- PhotoMode_CameraZoomin (58), PhotoMode_CameraZoomout (59), CameraEffect (60)
- BattleSkill1 (64), BattleSkill2 (65), BattleNormalAttack (66), BattleMenu (67)
- BattleTargetLock (68)
- BattleChangeTargetStickRight (69), BattleChangeTargetStickLeft (70)
- BattleChangeTargetStickUp (71), BattleChangeTargetStickDown (72)
- BattleChangeTargetDPadRight (73), BattleChangeTargetDPadLeft (74)
- BattleChangeTargetDPadUp (75), BattleChangeTargetDPadDown (76)
- BattleStepAvoid (77), BattleChangeCommand (78), BattleTargetChangeMode (79)
- BattleControlPlayerChangeMode (80)
- BattleAssistMember1 (81), BattleAssistMember2 (82), BattleAssistMember3 (83), BattleAssistMember4 (84)
- BattleEffectSkip (85), BattlePause (86), R3 (87), L3 (88), Select (89)
- EventSkip (96), EventFastForward (97)
- Leader (98), AddBattleMember (99), RemoveBattleMember (100)
- AssistFormation (101), GameExit (102)
- CampQuickRecovery (25), CampSelectCharacter (26)

### UIInputController.Key Enum (UI menus only)

Up, Down, Left, Right, RepeatUp, RepeatDown, RepeatLeft, RepeatRight,
RightStickUp, RightStickDown, RightStickRepeatUp, RightStickRepeatDown,
Decision, RepeatDecision, ReleaseDecision, Cancel,
Square, Triangle,
TriggerR1, TriggerL1, TriggerR2, TriggerL2,
RepeatTriggerR2, RepeatTriggerL2, ReleaseTriggerR2, ReleaseTriggerL2,
RepeatTriggerR1, RepeatTriggerL1,
Start, Select, Sort, R3, L3

---

## 3. Safe Mod Keys

The mod's defaults live in `ModKeys.cs` (single source of truth); the full
user-facing table is in `docs/mod-bindings.md`.

How safety is verified (2026-08-30 rework): the old rule ("keys absent from the
InputAction enum are safe") was too weak — keyboard bindings are player-
rebindable and live in native data, not the enum. The authority is now the
**live binding dump**: turning debug mode on (F12) logs every game action's
current keyboard key (via `SystemConfigParameter.GetKeyboardKey`) and pad
button (via `GameInputManager.GetBindInputKey`), plus a per-mod-key
FREE/CLASHES verdict (`InputBindingDump.cs`). Check the dump before claiming a
new key.

Current defaults:

- **F-keys** — F1 help, F2 dialogue voice, F3 Fol, F4 mod menu, F12 debug;
  F5–F11 debug-only. Not used by the game's keyboard defaults.
- **Minus / Equals / LeftBracket / RightBracket / Backslash** — modeless
  navigation family (battle pause reuses minus/equals/brackets in its own
  context). Chosen because no game default uses them (verify via dump).
- **Quote/apostrophe** (camp menu: story hint), **P** (Quick Recovery: party
  status) — context-gated. Dump verdict 2026-08-30: H clashed (game maps
  keyboard H to its R3 action: backlog / battle target lock) → moved to Quote;
  P verified FREE. Full dump recorded: game keyboard defaults use F (confirm),
  C (cancel/dodge), WASD + arrows, Tab, Space, Q/E, R, T, Z/X, J, H,
  Digit1–4, LeftShift/LeftCtrl/LeftAlt — most letters near WASD are taken.
- **NumPad** — no longer used by the mod (numpadless keyboards must work).
- **Gamepad** — mod modifier is **L2** (held: nav overlay; +L3 mod menu;
  +R3 Fol). L1 belongs to the game (pickpocket on the field). L1/R1 are used
  by the mod ONLY inside the battle pause menu, where they are free.

---

## 4. Input System API

**File:** `decompiled/Assembly-CSharp/Il2CppGame/GameInputManager.cs`
**Class:** `GameInputManager : SingletonMonoBehaviour<GameInputManager>`

### Key Methods

```csharp
bool IsDown(InputAction inputAction)          // held down this frame
bool IsRelease(InputAction inputAction)       // released this frame
bool IsRepeat(InputAction inputAction)        // repeated press
InputKey GetBindInputKey(InputAction action)  // current bound PAD button (InputKey is pad-shaped)
InputKey GetDefaultBindInputKey(InputAction action)
InputAction GetAliasInputAction(InputAction action)
Vector2 GetRightStick()
Vector2 GetLeftStick()
Vector2 GetDPad()
bool IsMouseLeftClickDown()
bool IsMouseRightClickDown()
void SetInputTask(InputTask inputTask)        // set active input handler
```

### Keyboard binding API (live, per action) — used by InputBindingDump

**File:** `decompiled/Assembly-CSharp/Il2CppGame/SystemConfigParameter.cs`
Access: `ParameterManager.Instance.SystemConfigParameter`

```csharp
Key  GetKeyboardKey(GameInputManager.InputAction a)        // LIVE keyboard binding (UnityEngine.InputSystem.Key)
void SetKeyboardKey(GameInputManager.InputAction a, Key k) // rebind (future rebinder hook)
void SwapKeyboardKey(GameInputManager.InputAction a, GameInputManager.InputAction b)
```

Notes: iterate only DEFINED InputAction enum members (the enum has gaps:
61–63, 90–95, 103–127); wrap each native call in try/catch; both singletons
can be null before a save is loaded.

---

## 5. UI System

### Text Rendering
The game uses **TextMeshPro** (`TextMeshProUGUI`) for all text display.
`GameText : TextMeshProUGUI` — game's custom text component with localization support.

### Base UI Classes (hierarchy)

```
TaskBase
  └─ TaskComponent (MonoBehaviour)
       ├─ UIComponent
       │    └─ UIControllerBase
       │         ├─ UIBattleController   (UIBattleController.cs, ~2,486 lines)
       │         └─ UIFieldController    (UIFieldController.cs, ~2,513 lines)
       └─ WindowComponent
```

### UIPresenterBase — all UI panels inherit from this

Virtual methods:
- `void Show()`
- `void Hide(Il2CppSystem.Action onHided = null)`
- `void ForceHide()`
- `void SetActive(bool active)`

### Window Types (UIDefine.WindowType enum)

None, Dialog, Battle, Camp, Title, Conversation, GameOver, Shop, SaveLoad,
Config, WorldMap, Field, System, Mission, Quest, EndingCollection,
FishCollector, Coliseum, Loading, Endroll, BunnyRace, Tutorial,
CookingMaster, Logo, Achievement, Fin

### Window Registration
**File:** `decompiled/Assembly-CSharp/Il2CppGame/UIWindowRegister.cs`
**Class:** `UIWindowRegister : BaseMonoBehaviour`
Properties: `window : WindowComponent`, `windowType : UIDefine.WindowType`

### UI Input (menu-level)
**File:** `decompiled/Assembly-CSharp/Il2CppGame/UIInputController.cs`
**Class:** `UIInputController : Il2CppSystem.Object`
- Map keys to actions: `SetAction(UIInputController.Key, Il2CppSystem.Action)`
- Actions execute each frame via `Update()`

---

## 6. Text / Localization System

**File:** `decompiled/Assembly-CSharp/Il2CppGame/TextManager.cs`
**Class:** `TextManager : SingletonBasicMonoBehaviour<TextManager>`

### Message Types
```csharp
enum MessageType { System = 0, Skill = 100, Item = 200 }
```

### Retrieving Text
```csharp
string text = TextManager.Instance.GetMessage(messageId, TextManager.MessageType.System);
```

### GameText Component
**File:** `decompiled/Assembly-CSharp/Il2CppGame/GameText.cs`
**Class:** `GameText : TextMeshProUGUI`
Properties:
- `messageId : string` — localization key
- `messageType : MessageType` — message category

To read what's displayed: cast to `TMP_Text` and read `.text` property.

---

## 7. Scene / State Management

### Scene Classes
- `TitleScene` — title screen
- `BootScene` — startup
- `FieldmapScene` — overworld
- `BattleScene` — combat
- `WorldmapScene` — world map
- `GameMapScene` — in-game map
- `LogoScene`, `CreditScene`, `SkipMovieCreditScene`, `TitleOpeningScene`

### Game State Enums (UIDefine)

**BattleState:** None, Menu, Result, SelectCharacter, Spell, Item, Tactics, Pause, Operation

**FieldState:** None, PickPocket, FishingBait, FishingResult, Fishing, LocationPoint, QuickRecovery

**TitleState:** None, Start, Menu, NewGame, Load, VoiceGallery, OriginalStaff, Copyright

**WorldMapState:** None, FastTravel, CurrentLocation

**SystemState:** None, ItemDiscard, OverflowItem, EquipWizard, AssistDialog

**UIControllerType:** None, Field, Battle, Event, Footer, Caption

### Task-Based Input Architecture
```
InputTask (base)
  ├─ FieldInputTask
  ├─ BattlePlayableInputTask
  ├─ FieldBunnyInputTask
  ├─ FieldPsynardInputTask
  └─ CookingMasterInputTask
```
Active input task set via: `GameInputManager.Instance.SetInputTask(inputTask)`

---

## 8. Key Files for Mod Development

- `GameInputManager.cs` — input system (read first for any feature)
- `GameManager.cs` — core game state
- `GameUIManager.cs` — UI management
- `TextManager.cs` — text retrieval
- `UIDefine.cs` — all UI enums and constants
- `GameDefine.cs` — game constants
- `ParameterManager.cs` — character and game data
- `EventManager.cs` — dialogue/event system
- `InputTask.cs`, `FieldInputTask.cs`, `BattlePlayableInputTask.cs` — input handlers
- `WindowComponent.cs`, `InputComponent.cs` — component base classes
- `UIInputController.cs` — menu-level input
- `GameText.cs` — text display component

---

## 9. Code Examples

### Reading current displayed text from a GameText component
```csharp
var tmp = gameTextObj.GetComponent<TMP_Text>();
string displayed = tmp?.text ?? "";
```

### Checking if a mod key is pressed (raw keyboard, safe for mod)
```csharp
// Use Unity's Input system or MelonLoader's InputSystem binding
// Do NOT use GameInputManager — that is for game actions only
if (UnityEngine.Input.GetKeyDown(KeyCode.F1)) { /* help */ }
```

### Accessing a singleton safely
```csharp
var gm = GameManager.Instance;
if (gm == null) { Log.Warning("GameManager not ready"); return; }
```

---

## 10. Known Issues and Workarounds

- MelonLoader RemoteAPI did not find the game (demo) — normal, stubs still generated via Cpp2IL
- `UICommonSelectTextPresenter` animation: when a value changes, `currentText` holds the OLD value (fading out) and `nextText` holds the NEW value (animating in). Always read `nextText` in a postfix on value-change methods; read `currentText` only during navigation when no animation is running.

---

## 11. Assembly / Namespace Notes

- TextMeshPro IL2CPP namespace is `Il2CppTMPro` (NOT `TMPro`) — add `using Il2CppTMPro;`
- `UnityEngine.UI.dll` must be added to csproj when accessing `GameText` or any `TextMeshProUGUI`-based type
- `UITitleSelectMenuSelectItemData` value field is `text` (string), not `itemName`
- `UIConfigMenuSelector.GetMessageID(Menu)` is an **instance** method (not static)

---

## 12. Save/Load Menu

**Files:** `UISaveLoadWindow.cs`, `UISaveLoadSelector.cs`, `UISaveLoadPresenter.cs`, `UISaveLoadListItemPresenter.cs`, `UISaveLoadListItemData.cs`

### Class Hierarchy
- `UISaveLoadWindow : WindowComponent` — top-level window, holds `UISaveLoadSelector`
- `UISaveLoadSelector : UIListSelectorBase` — the navigable list; inherits `currentIndex` and `currentDataList` from base
- `UISaveLoadListItemPresenter : UICanSelectedListItemPresenterBase` — one item per slot
- `UISaveLoadListItemData : ListItemDataBase` — data for one slot

### UISaveLoadListItemData Fields (all pre-formatted strings)
- `isExistData : bool` — true if save data exists in this slot
- `isAutoSave : bool` — true if this is an auto-save slot
- `slotText : string` — slot label ("1", "2", etc.; empty for auto-save)
- `heroName : string` — main character name
- `heroLevel : string` — level as display string
- `difficultyLevel : string` — difficulty name
- `fieldName : string` — current location name
- `playTimeValue : string` — formatted playtime
- `saveDataIndex : int` — zero-based slot index
- `isCleared : bool` — game completion flag

### Key Hook Points
- `UISaveLoadSelector.Show()` — fires when the screen opens
- `UISaveLoadListItemPresenter.OnSelected(ListItemDataBase)` — fires on cursor move; cast itemData to `UISaveLoadListItemData`

---

---

## 13. Dialogue System

**File:** `UIConversationPresenter.cs`
**Class:** `UIConversationPresenter : UIAnimationPresenterBase`

### Key Fields/Properties
- `message` (GameText) — the displayed dialogue text
- `talkerName` (GameText) — the NPC speaker name
- `textFeedController` (TextFeedController) — controls character-by-character animation
- `MessageGameText` (property) — getter for `message`
- `TalkerNameGameText` (property) — getter for `talkerName`

### Key SetMessage Overloads
All public overloads eventually delegate to the private implementation:
```csharp
// Private impl — all overloads resolve to this (5 callers). Receives actual text.
void SetMessage(string message, string talkerName, string voiceID, bool isWait, ref Rect rect)

// Field NPC version (canvas + FieldObject) — looks up messageID then calls private impl
void SetMessage(string messageID, Canvas canvas, FieldObject fieldObject, ref Vector3 worldOffset, bool isClampSafeArea = true)

// Full field version with pre-resolved text (3 callers)
void SetMessage(string messageID, string message, string talkerName, string voiceID, Canvas canvas, FieldObject fieldObject, ref Vector3 worldOffset, bool isClampSafeArea = true)
```

### Hook Point (used in mod)
Postfix on the private implementation — catches all dialogue types:
```csharp
[HarmonyPatch] SetMessage(string message, string talkerName, string voiceID, bool isWait, ref Rect rect)
```
Read `message` and `talkerName` parameters directly. Strip TMP tags before announcing.

---

## 14. Tutorial System

**File:** `UITutorialInformationPresenter.cs`
**Class:** `UITutorialInformationPresenter : UIAnimationPresenterBase`

### Key Fields
- `title` (GameText), `description` (GameText), `operationText` (GameText)
- `operationLeft` / `operationRight` (GameText) — nav button labels
- `currentPage` / `maxPage` (GameText) — page counter display

### Key Methods
```csharp
void SetInformation(UITutorialInformationData data)  // [CallerCount(2)] — fires per page
void SetPageCount(int current, int max)              // [CallerCount(7)]
```

**Data class:** `UITutorialInformationData`
- `title` (string), `description` (string), `operation` (string) — all plain strings

### Hook Point (used in mod)
Postfix on `SetInformation(UITutorialInformationData data)` — read `data.title` + `data.description`.

---

## 15. Dialog and Popup System

### UIDialogPresenter
**File:** `UIDialogPresenter.cs`
**Class:** `UIDialogPresenter : UIAnimationPresenterBase`

Key fields: `message` (GameText), `centerMessage` (GameText), `yes`/`no`/`ok` (UIGameTextPresenter)

```csharp
// [CallerCount(6)] — simple yes/no and OK dialogs
void Setup(string message, UIDefine.DialogType type, UIDefine.DialogChoices choice)
```

`UIDefine.DialogType`: None, YesNo, OK
`UIDefine.DialogChoices`: None, Yes, No, Cancel

### UIDialogWindow
**File:** `UIDialogWindow.cs`
**Class:** `UIDialogWindow : WindowComponent`

```csharp
// [CallerCount(1)] — description-style popups (acquired arts, skill info, etc.)
void SetupDescription(string message, string description, UIDefine.DialogType dialogType,
    Il2CppSystem.Action<UIDefine.DialogChoices> onClose = null, int cookingCount = 0,
    Sprite sprite = null, UIDefine.DialogChoices firstChoice = No, bool canCancel = false)
```

### Hook Points (used in mod)
- Postfix on `UIDialogPresenter.Setup(string, DialogType, DialogChoices)` — announces question + initial focused button together
- Postfix on `UIDialogPresenter.SelectChoices(DialogChoices, float)` [CallerCount(4)] — announces focused button on navigation; first call after Setup is suppressed via flag (it fires automatically during init and would cut off the question)
- Postfix on `UIDialogWindow.SetupDescription` (name only, method is unique) — description popups

**Important pattern:** Setup fires → sets `_skipNextSelectChoices = true` → SelectChoices fires once for init (suppressed) → subsequent SelectChoices calls on real navigation announce normally.

---

## 16. Field Navigation Entity System

### Overview
`FieldManager.Instance` exposes public list properties for every entity category
on the current map. All entity types inherit from `FieldObject` (or a Unity
MonoBehaviour base) and have a Unity `transform.position` for world coordinates.

### Player Position and Direction
```csharp
FieldPlayer player = FieldManager.Instance.GetControlPlayer();
Vector3 playerPos     = player.transform.position;
Vector3 playerForward = player.transform.forward;  // facing direction (for stereo panning)
```
Other accessors:
- `FieldManager.Instance.FieldPlayer` — property returning current player
- `FieldManager.Instance.GetFieldPlayer(PlayerID)` — by character ID
- `FieldManager.Instance.FieldPlayerList` — all party members on field

### Current Map ID
```csharp
FieldmapID mapID = FieldManager.Instance.currentFieldmapID;
// FieldmapID enum values are technical codes: INVALID, EXPEL, NEDE, MF_0001_01A, etc.
// No string table exists in the game code for human-readable map names.
// Must maintain a custom lookup table for user-facing names.
```

### Entity Lists (all on FieldManager.Instance)
```csharp
List<FieldNpcCharacter>      FieldNpcCharacterList
List<FieldTreasureBox>       FieldTreasureBoxList
List<FieldMapjumpCollision>  FieldMapjumpCollisionList
List<FieldLocationPoint>     FieldLocationPointList
List<FieldDoor>              FieldDoorList
List<FieldStairs>            FieldStairsList
List<FieldObject>            FieldObjectList         // all objects (broad)
List<FieldObject>            FieldCollisionList      // collision/interaction zones
```
These are `Il2CppSystem.Collections.Generic.List<T>` types.
Use index-based loops: `for (int i = 0; i < list.Count; i++)` — safer than foreach.

### NPCs — FieldNpcCharacter
**File:** `FieldNpcCharacter.cs`
**Inheritance:** FieldNpcCharacter → FieldBillboardObject → FieldObject
```csharp
int     NpcIndex          // runtime-assigned index
NpcType npcType           // INVALID, NORMAL, INN, SHOP_EQUIPMENT, SHOP_ITEM, GUILD,
                          // CHECK, OTHER, FACILITY, FISH_COLLECTOR, SHOP_FOOD, BED, PSYNARD, MAX
ShopID  shopID
string  defaultAnimationName
bool    isPlayerObstacle
bool    isAngry
Vector3 initialPosition   // spawn position (use for name matching)
string  eventFunction
```
**Getting the display name:** Match `npc.initialPosition` to `ConstNpcParameter.position`
(tolerance ~0.5f). See NPC Parameter System below.

### NPC Parameter System — ConstNpcParameter
**File:** `ConstNpcParameter.cs`
Key fields:
```csharp
string     Name              // DISPLAY NAME — human-readable NPC name
string     modelName
FieldmapID fieldmapID        // which map this NPC belongs to
Vector3    position          // spawn position (matches FieldNpcCharacter.initialPosition)
NpcType    npcType
ShopID     shopID
string     eventFunction
float      conversationDistance
float      conversationAngle
```
**Lookup pattern:**
```csharp
// Get all NPC parameters for current map
var npcParams = ParameterManager.Instance.GetNpcParameter(FieldManager.Instance.currentFieldmapID);
// Match by position
for (int i = 0; i < npcParams.Count; i++) {
    if (Vector3.Distance(npc.initialPosition, npcParams[i].position) < 0.5f)
        return npcParams[i].Name;
}
```
**ParameterManager.GetNpcParameter overloads:**
```csharp
List<ConstNpcParameter> GetNpcParameter(FieldmapID fieldmapID)       // all NPCs on a map
List<ConstNpcParameter> GetNpcParameter(int privateActionEventID)
List<ConstNpcParameter> GetNpcParameterList(string name)
ConstNpcParameter       GetNpcParameter(ShopID shopID)
ConstNpcParameter       GetNpcParameter(string placementName)
```

### Treasure Chests — FieldTreasureBox
**File:** `FieldTreasureBox.cs`
**Inheritance:** FieldTreasureBox → Field3DObject → FieldObject
```csharp
bool       isAcquired    // true = already opened
RewardType rewardType
int        treasureValue
int        count
int        flag          // save flag recording "opened"; property Flag. Unique per chest → the mod's
                         // stable-number identity ("chest:" + Flag). 0 = no flag, use the position.
```
Position via `chest.transform.position`. A battle unloads and reloads the field scene
(mf_ → mb_ → mf_): every chest comes back as a NEW object with a new instance ID while
`currentFieldmapID` stays the same — never key anything on the object or its ID.

### Map Exits / Transitions — FieldMapjumpCollision
**File:** `FieldMapjumpCollision.cs`
**Inheritance:** EventCollision (Unity Component — has transform)
```csharp
MapjumpID   mapjumpID       // unique ID for this exit (enum, codes only: MAPJUMP_001 etc.)
FieldmapID  fieldmapID      // destination map
Vector3     toPosition      // player spawn position at destination
Vector3     toDirection
MapIconType iconType        // door, portal, stairs icon type
MapIconType subIconType
MapjumpType mapjumpType
string      controlObjectName
```
Position via `exit.transform.position` (wrap in try-catch — inherits from EventCollision,
not FieldObject, so transform may behave differently).

### Map jump layout data — MapjumpLayoutData (where a jump puts the player)
**Files:** `MapjumpLayoutData.cs`, `MapjumpParameterCollection.cs`, `ParameterManager.cs`
The parameter-side record of every map jump, available without the jump's scene being loaded.
```csharp
MapjumpID  MapjumpID
FieldmapID FieldmapID      // map the jump trigger sits in
Vector3    Position        // trigger position in that map
FieldmapID ToFieldmapID    // destination map (FieldmapID.EXPEL / NEDE = the world maps)
Vector3    ToPosition      // where the player appears in the destination map
float      ToDirection, CameraDirection
Vector3    PsynardPosition // plus PsynardCollisionPosition / Size / Direction
```
Lookup: `ParameterManager.Instance.GetMapjumpLayoutParameter(MapjumpID)` (11 game callers, returns null
for IDs without a record). Enumerate with `Enum.GetValues(typeof(MapjumpID))` (575 IDs) — avoids iterating the
IL2CPP dictionary from `MapjumpParamCollection.GetMapjumpDataDictionary()`. The overload taking a `FieldmapID`
has no game callers; not used. Use the PascalCase properties.
**Use (2026-09-20):** the fishing stand bake anchors its route proofs at `ToPosition` of every record whose
`ToFieldmapID` is the current world map (`WorldmapFishingStandBaker.Anchors.cs`). ⏳ Hypothesis until the first
bake log confirms it: those points equal the logged town exits Hilton (750.8,−170.7) and Arlia (−46.8,−404.5);
the bake checks this itself (3 m tolerance) and dumps the records when it fails.

### Quest / Location Markers — FieldLocationPoint
**File:** `FieldLocationPoint.cs`
**Inheritance:** FieldLocationPoint → FieldObject
```csharp
LocationPointID locationPointID   // enum ID (codes only, not human-readable)
int             rewardID
float           visibleDistance
float           unvisibleDistance
bool            isEnd
string          cameraName
```
Position via `marker.transform.position`.

### Climb Points — FieldGimmick03 (ladders, ivy, cliff scrambles)
**Files:** `FieldGimmick03.cs`, `ConstGimmick03Parameter.cs`, `FieldCharacterLadderBaseTask.cs`
**Inheritance:** FieldGimmick03 → FieldContactGimmick → FieldGimmickBase → FieldBillboardObject
The game's climb is a *contact gimmick*, not a stairs/door object. Proof: the
character state machine has `LadderStart / Ladder / LadderEnd` states
(`FieldCharacterState`), and `FieldCharacterLadderBaseTask` holds a
`fieldGimmick03` field — so Gimmick03 IS the ladder. Walking into its
collision starts the climb natively (`FieldCharacterController.OnLadderStart`,
native-only callers). Earlier mod code mislabelled it "Platform" under Warp Points.
```csharp
// Live objects (spawned for the current map)
var list = FieldManager.Instance.FieldGimmickManager.FieldGimmickList; // List<FieldGimmickBase>
var ladder = list[i].TryCast<FieldGimmick03>();
Vector3 bottomOrTop = ladder.StartPosition;   // PascalCase — never the lowercase field
Vector3 otherEnd    = ladder.EndPosition;
GimmickStartupType t = ladder.GetGimmickStartupType(); // Auto = walk in; Conversation = press confirm

// Static placement table (exists even before the object spawns)
var table = ParameterManager.Instance.GetGimmick03ParameterList(mapID); // List<ConstGimmick03Parameter>
table[i].ColPosition;  // List<Vector3> — contact collision centres (walk targets)
table[i].ColDirection; // List<float>
table[i].ColSize;      // List<Vector3>
table[i].Position;     // List<Vector3> — climb end points (pairing with ColPosition NOT yet verified — read the NAV:CLIMB log)
```
The mod lists these in the **Stairs** category as "Climb up / Climb down / Climb point"
(`NavigationHandler.Build.Climb.cs`, fed by the one gimmick scan of `Build.Gimmicks.cs`). Every other
gimmick class is in the inventory below.
Dialogue hint that a climb point is nearby: "We could probably climb up here if we tried."

### Field gimmick inventory (all FieldGimmickNN classes) — registry `Interactables.cs`
**Files:** `FieldGimmickBase.cs`, `FieldContactGimmick.cs`, `FieldGimmickManager.cs`, `FieldGimmick01..17*.cs`
Every field gimmick is `FieldGimmickBase : FieldBillboardObject : FieldObject`. Live objects:
`FieldManager.Instance.FieldGimmickManager.FieldGimmickList` (IL2CPP list, index loop). Base virtuals:
`GetGimmickStartupType()` {Auto = walk in, Conversation = press confirm}, `GetOperationMessageID()`
(System text key of the prompt; only 01 overrides it — `SYS_3700` = "&lt;sprite name=Cross&gt;Jump"),
`GetMapIconType()`, `IsControlPlayerOnly()`; `FieldObject.IsConversation()` is overridden on 05, 09, 11,
11Switch, 11Door, 12StoneStatue, 14Door, 16Switch (and FieldTreasureBox, FieldNpcCharacter, FieldSavePoint).
The contact the player is in right now: `FieldGimmickManager.PlayerContactGimmick` (FieldGimmickBase) and
`FieldManager.ConversationTarget` (FieldObject: NPC, chest, save point, conversation gimmicks).
Enums: `GimmickStartupType {Invalid, Auto, Conversation}`, `GimmickType {Invalid, Contact}`; there is NO
numeric gimmick-kind ID — classes are told apart by name (`GetIl2CppType().Name`).

The mod classifies each object ONCE per list build (`InteractableRegistry.Classify`, by type name) into an
`InteractableKind`; the same kind drives the nav row, the beacon and the prompt log. Placement as of 2026-09-23:
- **01** contact point / ledge — Stairs, label = resolved prompt text ("Jump") else "Ledge", numbered per map
- **02** scenario contact trigger (`targetObjectName`, progress window) — not listed
- **03** ladder / climb (`StartPosition`, `EndPosition`) — Stairs via `Build.Climb.cs` (see above)
- **04** trapped chest (spawns `symbolIDList` on open) — not listed (the chest itself is a FieldTreasureBox row)
- **05** gathering point (`ItemID`, `EventFunction`, `ScenarioProgressStart/End`, `DisableFlagList`) —
  Interactables "Gathering point N" (always numbered), beacon `NavCueKind.Gather`. Item ID in the log ONLY.
- **06** ambush (`CheckCleared`) — not listed
- **07Main / 07Sub** water-level bridge puzzle (contact) — Mechanism when startup = Conversation, else not listed;
  **07Door** — Doors "Door N"
- **08** depth-damage hazard (controller only, no object) — n/a
- **09** warp panel (`destination`, `panelColor`) — Warp Points "Warp panel N"
- **10** contact trap (spawns enemies) — not listed
- **11** colour panel (`colorTypeList`) — Mechanism; **11Switch** — Interactables "Switch N"; **11Door** — Doors
- **12StoneStatue** (`direction`, `answerDirection`) — Interactables "Statue N"
- **13** floor panels stepped in order — Interactables "Floor panel N"
- **14** room maze (`roomType`) — not listed; **14Panel** — "Floor panel N"; **14Door** — Doors
- **15** sleeping guard "Berk" (stealth) — not listed
- **16** breakable rock (`rockObject`, `navMeshObstacle`, `BreakRock()`) — Doors "Boulder N";
  **16Switch** (`switchObject`, `fuseObject`) — "Switch N" (never speak `SwitchName`/`FuseName`: asset names)
- **17** magic circle warp (`destination`, `IsEnable()`, `isDisableWarp`) — Warp Points "Magic circle N"
- **18** avalanche (controller only) — n/a
- **unknown class**: startup Conversation → Interactables "Mechanism N"; Auto → not listed. Every object is
  logged as `NAV:GIMMICK [i] <class> kind=… startup=… listed=… pos=…` so a map that lists nothing explains why.
Listability: `gameObject.activeInHierarchy`; magic circles also `IsEnable() && !isDisableWarp`.
Fairness (user rule 2026-09-23): a sighted player sees a sparkle, a switch, a statue — never what a spot holds,
so labels are generic; IDs, event functions and asset names go to the debug log only. Switch colour and statue
facing ARE visible to sighted players and may be spoken later.
Sanctuary of Linga (`MF_0020_01A`, flags `FLAG_LNGSAC_HERBA1…6`): 11 × 05 on two floors, 17 × 01 in chains
stepping y 0 → −12 (the way between floors); confirmed 2026-09-23 in the log.

### Map Name Issue (known limitation)
- `FieldmapID` enum values are technical codes (`MF_0001_01A`, `MF_0002_01A`, etc.)
- `MapjumpID` values are also codes (`MAPJUMP_001`, `MAPJUMP_002`, etc.)
- No string table in the game code maps these to human-readable names
- A custom lookup table must be maintained (task: map the demo's ~5–10 maps manually)

---

## 17. Camp Menu System

### Overview
The camp menu opens when the player presses the camp button (InputAction.CampMenu = 38) in the
field. It presents a root command list, then each command leads to a sub-screen.

### Root Command Menu — UICampCommandSelector
**File:** `UICampCommandSelector.cs`
**Class:** `UICampCommandSelector : UIListSelectorBase`

Key fields:
- `commandListPresenter` (UICommonListPresenter) — the list view
- `currentCommand` (GameText) — GameText showing selected command text
- `currentCommandName` (GameText) — GameText for command name

Inherited from `UIListSelectorBase`:
- `currentIndex` (int) — zero-based index of the focused item
- `currentDataList` (List\<ListItemDataBase\>) — full item list; items are `UICampCommandListItemData`
- `DataCount` (int property)

Key methods:
- `Show()` — fires when the camp menu opens [CallerCount(0) on subclass]
- `UpdatePresenter()` — fires on navigation [CallerCount(2)]
- `OnDecision()` — fires on confirm
- `OnCancel()` — fires on cancel

### Command List Item — UICampCommandListItemData / UICampCommandListItemPresenter
**Files:** `UICampCommandListItemData.cs`, `UICampCommandListItemPresenter.cs`

**UICampCommandListItemData : ListItemDataBase**
- `commandName` (string) — the display name of the command (e.g. "Status", "Item", "Skills")

**UICampCommandListItemPresenter : UICanSelectedListItemPresenterBase**
- `commandName` (GameText) — the rendered command name text
- Inherits `OnSelected(ListItemDataBase)` from `UICanSelectedListItemPresenterBase`
- `CanSelected()` — returns true if the item can be selected

### Character Status Data — CampCharacterStatusParameterData
**File:** `CampCharacterStatusParameterData.cs`
**Class:** `CampCharacterStatusParameterData : Il2CppSystem.Object`

Fields (all useful for status announcements):
- `characterName` (string) — character's display name
- `level` (int) — current level
- `positionText` (string) — role/class name
- `hp` / `maxHp` (int) — current and max HP
- `mp` / `maxMp` (int) — current and max MP
- `canDecisioned` (bool) — whether this character can be selected
- `isGuest` (bool) — guest character flag
- `characterPosition` (UIDefine.CharacterPosition enum) — party slot

### Character Selection Bar — UICampBattleMemberListSelector
**File:** `UICampBattleMemberListSelector.cs`
**Class:** `UICampBattleMemberListSelector : UIListSelectorBase`

- Used for the horizontal character tabs shown within most camp sub-screens
- Items are `UICampBattleMemberSelectItemData` which holds `statusParameterData` (CampCharacterStatusParameterData)
- Has `Show()` and navigation methods inherited from base
- No `OnMoveCursor` override — would need to patch base class to catch character tab navigation

### Hook Points (used in mod)
- Postfix on `UICampCommandSelector.Show()` — announces "Camp menu." on open
- Postfix on `UICampCommandListItemPresenter.OnSelected(ListItemDataBase)` — announces focused
  command name + position. Note: `OnSelected` is inherited from `UICanSelectedListItemPresenterBase`;
  Harmony finds it by traversing the type hierarchy. The postfix filters via
  `TryCast<UICampCommandListItemData>()` to avoid firing on other list navigations.

---

## 18. Field Icons & On-Screen Notifications (UIFieldController)

### Overview
`UIFieldController` (`UIFieldController.cs`) is the central controller for everything that
floats in the field world space or pops up as a field notification. The "X Jump" prompt over
the player's head, location-point / fishing icons, NPC emotion bubbles, area-name banners, and
the corner info toasts (item get, EXP, level-up, etc.) are ALL driven from here. There is no
single "FieldGuideType" enum — each notification family is its own method + presenter.

The `[CallerCount(n)]` attribute is the key hook signal (same rule as the rest of this project):
`n >= 1` = at least one managed caller, so the managed stub runs and a Harmony hook FIRES;
`n == 0` = native-only caller, hook will NOT fire (poll the presenter instead).

### Floating button prompt — the "X Jump" family (HOOKABLE)
The over-the-head button guide (e.g. `X  Jump` at a one-way ledge) is an "operation" prompt,
NOT a FieldIconType. Two render paths exist:
- `UIFieldController.ShowOperation(List<string> operationList, Transform followTransform,
  ref Vector3 worldOffset, bool isCancelLocalPosition, bool isPlayer = false,
  List<Color> textColorList = null)` — [CallerCount(2)]. `isPlayer = true` positions it over
  the player (the jump-prompt case). `operationList` holds the guide strings.
- `UIFieldController.ShowLabelOperation(string label, string operation, Transform followTransform,
  ref Vector3 worldOffset, bool isPlayer = false)` — [CallerCount(0)] (likely native-only).
- `UIFieldController.HideOperation()` / `HideLabelOperation()` — both [CallerCount(0)]
  (native-only — a hook will NOT fire on hide; detect disappearance by polling).

**BEST HOOK — `UIFieldOperationPresenter.Set(...)`** (`UIFieldOperationPresenter.cs`,
class `UIFieldOperationPresenter : UIAnimationPresenterBase`):
- `Set(List<string> operationList, Transform followTransform, Canvas canvas,
  ref Vector3 worldOffset, bool isCancelLocalPosition, bool isPlayer = false,
  List<Color> textColorList = null)` — **[CallerCount(7)]**, hookable, receives the prompt
  strings directly. Preferred over `ShowOperation` (more callers, has the data in-args).
- `operationTextList` (List\<GameText\>) — the actual rendered text, READABLE at runtime, so the
  literal on-screen text ("Jump", button glyph) can be pulled even if the input strings are keys.
- `Hide()` / `ForceHide()` — both [CallerCount(0)] → poll `gameObject.activeInHierarchy` to
  know when the prompt clears.
- The selector wrapper is `UIFieldIconSelector`'s sibling — the presenter lives under
  `UIFieldController.operationPresenter` (field, type `UIFieldOperationPresenter`).

CONFIRMED: the jump prompt arrives as `operationList[0] = "<sprite name=Cross>Jump"` with
`isPlayer = false`. The presenter is shared by every button guide (Talk, Open, Examine, Jump,
Talk|Pickpocket …). Since 2026-09-23 `FieldPromptHandler` speaks ALL of them with the game's own
words (`prompt_generic` "Press {0} to {1}." per entry): state is kept PER PRESENTER INSTANCE
(`Dictionary<int, PromptState>`), a prompt is spoken when its raw text changes or when it is
re-shown after the player moved ≥ 2 m from where it was last spoken (the game blinks bubbles
while standing still — a re-show without movement stays silent; a ledge chain speaks per ledge).
Hidden = presenter inactive or every `operationTextList` text empty (polled). The jump SOUND
needs the kind: `FieldGimmickManager.PlayerContactGimmick` classified by `InteractableRegistry`
(FieldGimmick01 = ledge), else the parsed action equals the resolved `SYS_3700` text — never an
English literal. Every announce logs `FieldPrompt <why>: kind=… contact=<class> target=<class>`
(evidence for whether the contact routing holds; the speech never depends on it).

### World-space icons — UIFieldIconSelector (only 2 types)
`UIFieldController.ShowIcon(string fieldObjectName, UIDefine.FieldIconType type,
ref Vector3 worldOffset)` — [CallerCount(3)]. Backed by `UIFieldIconSelector.ShowFieldIcon(...)`
(several overloads, [CallerCount(1)]). `UIDefine.FieldIconType` has ONLY:
- `LocationPoint` — discoverable map location sparkle (already handled via
  `UIFieldLocationPointPresenter.Set`, see Location Discovery in MEMORY).
- `Fishing` — the fishing BUBBLE above the player's head: the game's authoritative
  "press action button to fish here" signal. `FieldPromptHandler` detects it by polling
  `UIFieldIconSelector.iconPresenterList` for a visible presenter whose `icon.sprite`
  matches `spriteList[(int)FieldIconType.Fishing]` (visible = activeInHierarchy +
  canvasGroup alpha ≥ 0.5). Cannot hook the Show path: `ShowFieldIcon` overloads all
  carry `ref Vector3` (hook = native crash).
`HideIcon(string)` / `HideIcon(Transform)` / `HideAllIcon()` — [CallerCount(0/0/2)].

⚠️ **`FieldManager.GetContactFishingWaterPlaceID()` is NOT a "can fish" signal**
(proven 2026-08-29): it reports contact with the fishing water-place VOLUME, an AABB
that can span 200m+ of land — world-map spot id=25's volume overlaps the Krosse City
exit, so the ID went non-zero the moment the player left town. Announcing or stopping
auto-walk on it caused false "You can fish here" prompts and false fishing arrivals.
Use the Fishing icon bubble (above) as the truth instead.

### NPC / player emotion bubbles — ShowEmotion (HOOKABLE)
`UIFieldController.ShowEmotion(...)` overloads — [CallerCount(8)] (string name overload),
[CallerCount(1)] (FieldObject overloads). Takes `UIDefine.EmotionType`:
Sweat, Exclamation, Question, Notice, Angry, Note, Gloomy, Heart, LightBulb, ColdSweat, Laugh,
TurnPale, Exclamation2, Silence, SweatReverse, NoticeReverse, Sleep.
`HideEmotion(string)` [CallerCount(15)], `HideAllEmotion()` [CallerCount(1)]. These are the
"!" / "?" bubbles over NPCs (alerted enemies, reaction cues) — candidate for an optional cue.

### Area / mode banners
- `ShowSymbolName(string)` / `ShowSymbolName(string, float)` — area-name banner [CallerCount(0)].
- `ShowSubSymbolName(string, float, Action)` — sub-area name [CallerCount(0)].
- `HideSymbolName()` [CallerCount(4)], `ShowMode(string, bool)` [CallerCount(4)] /
  `HideMode()` [CallerCount(2)] — mode banner (e.g. stealth/scout mode label).
- `ShowOnTransition(bool isShowMapName, ...)` — [CallerCount(9)], fires on map transitions.

### Corner info toasts (acquisition / progression notifications)
All on `UIFieldController`. CallerCount in brackets ( >=1 = hookable directly ):
- `ShowItemInformation(int itemID, int count, FactorID)` [4] — item acquired
- `ShowGetMoneyInformation(int money)` [4] — Fol gained
- `ShowExpInformation(int exp)` [3] — EXP gained
- `ShowSkillPointInformation(int sp)` [0], `ShowBattlePointInformation(int bp)` [0]
- `ShowLevelUpInformation(PlayerID, int preLevel, int level)` [4] — level up
- `ShowLearningBattleSkillInformation(PlayerID, List<BattleSkillID>)` [4] — skill learned
- `ShowOpenTalent(PlayerID, TalentID)` [2] — talent unlocked
- `ShowFamliarInformation(FamiliarBirdType)` [1], `ShowFavorabilityInformation()` [0],
  `AddFavorabilityNotification(PlayerID)` [4]
- `ShowPlayerInformation(PlayerID, string, string soundName)` [2] — generic player toast
- `ShowBouncedCheckInformation(int money)` [1]
- `ShowCookingMasterFoodInformation(int itemID, int count)` [4] / `ShowCookingMasterStorageInformation()` [0]
- `ShowInformation(string information)` [0]
NOTE: several of these (item/EXP/level/skill) overlap with rewards the mod already surfaces via
other hooks (Location Discovery, battle results). Re-using these as the single source of truth
for "what did I just get" is worth evaluating — but watch for CallerCount(0) ones being
native-only.

### Party member change notifications
`ShowChangeMemberNotification()` [3], `AddChangeBattleMemberNotification(List<PlayerID>)` [0],
`AddChangeAssistMemberNotification(List<AssistID>)` [0],
`AddBreakawayMemberNotification(List<UpdatedMember>)` [0].

### Jump-down mechanism (for the cue's trigger context)
Related field classes (the ledge/jump machinery, separate from the UI prompt):
`FieldMapjumpCollision.cs` (ledge/exit collision trigger; has `iconType` / `subIconType`),
`FieldMapJumpInfo.cs`, `FieldCharacterJumpTask.cs` (states Invalid/StartJump/Jump/EndJump —
the actual descent animation). The jump still requires a manual X press (confirmed in-game);
the prompt appears when the player parks at the ledge.

## 19. Universal Menu Hooks & Missed Text Sources (learned from ScreenReaderMOD analysis)

Source: API research on Galaxy Laboratory MM's "ScreenReaderMOD" (BepInEx, decompiled to
`reference-mods/` — gitignored, NOT ours, never copy code verbatim). All hook targets below
verified present in our own `decompiled/` game source.

### Universal list-selection hook (Harmony DOES fire here!)
`UICanSelectedListItemPresenterBase.OnSelected(ListItemDataBase itemData)` — postfix fires for
EVERY list-item selection game-wide: camp item/equip/skill/formation/operations lists, shop
lists, quest (guild) lists, picture books, config menu, battle menu lists. This refines our
"native menus fire no hooks" rule: cursor *movement* on native command menus still fires
nothing, but list-item *selection focus* does fire this. Pattern that works: in the postfix,
`TryCast<>` to the concrete presenter type to know which screen; store as "pending" + timestamp,
then read/announce ~0.05–0.4s later from the main Update poll (lets the game finish populating
the row). Generic fallback for unknown lists: read the TMP text from the selected GameObject
(or its parent).

**Implemented 2026-08-29 in `ListSelectionHandler.cs`:** suppression by concrete type name for
all screens with dedicated handlers (+ camp/shop open gates for generic row types), generic
GameText-children readout after 0.15s settle for everything else, debug log of every fire's
type + decision. The 58 concrete subclasses of UICanSelectedListItemPresenterBase are listed
by `grep ": UICanSelectedListItemPresenterBase" decompiled/`.

Validated at the guild (Test G1, 2026-08-29): the guild counter's first command menu
(Accept/Report) rows are `UIShopMenuListItemPresenter` — the same type as the shop's
Buy/Sell root menu (which stays suppressed via the IsShopOpen gate; ShopHandler polls it).
The generic fallback reads them cleanly ("Accept" / "Report").

**Stale-wake discriminator:** opening the guild command menu wakes the quest selector with
stale data (activeInHierarchy + populated currentDataList) WITHOUT input focus, and no
OnSelected fires. Real focus (list entry, cursor move) always fires OnSelected for the row
type. `ListSelectionHandler.WasRecentlySelected(typeName, window)` exposes this;
GuildHandler.PollGuildQuests requires a recent `UIQuestListItemPresenter` selection before
announcing — the pattern to reuse for any other selector that wakes stale.

### Party Formation ↔ Assault Formation swap with R2 / L2 (2026-09-06)

- The two Operations children are sibling screens: on the Party Formation screen
  (`UICampWindow.selectCharacterSelector`) R2 swaps straight to Assault Formation
  (`assistSettingSelector`, root enum `AssistFormation`, on-screen label "Assault
  Formation") and L2 swaps back, WITHOUT returning to the root menu. The root cursor
  never moves, so `_lastRootMenuItemName` stayed "PartyFormation" and the assault
  screen was silent (log 2026-09-06 11:07: only `ListSelection ... suppressed
  (dedicated handler owns it)` lines, no heading, no rows).
- Detection (`CampMenuHandler.Party.cs` `SyncFormationSiblingScreen` /
  `SwitchFormationSibling`): poll `UICampWindow.selectorStack` (inherited from
  `UIStackSelectorWindowBase`, an `Il2CppSystem` `Stack<UISelectorBase>`) and compare
  `Peek().Pointer` with the cached selectors each frame; plus Harmony postfixes on
  `UICampAssistSettingSelector.Show()` and `UICampSelectCharacterSelector.Show()`
  (parameterless, safe). Both paths log `CampMenu: X → Y (via stack|Show)` so the
  log shows which one fired. The switch only flips between the two siblings; opening
  either from the root menu keeps the normal path.
- Rule: any sub-screen gated on `_lastRootMenuItemName` is blind to in-screen
  shortcuts that swap selectors. When a screen has such a shortcut, watch the
  selector stack top instead of trusting the root cursor.

### Camp quick heal — D-pad Right on the camp root menu (2026-09-06)

- A game feature (binding `CampQuickRecovery` = D-pad Right / key 2): the same
  Yes/No recovery dialog as the field quick heal, but a separate class,
  `UICampQuickRecoverySelector` (field `quickRecoverySelector` on
  `UICampMenuSelector`), with the same members as the field one
  (`recoveryDataList` of `UICommonSelectCharacterStatusSelectItemData`,
  `currentChoice`). Both execute through `GameManager.QuickRecovery`.
- While it shows, `UICampWindow.IsOpened` is FALSE — the camp handler logged
  "window closed" the moment D-pad Right was pressed and "window opened" again when
  the dialog ended (log 2026-09-06 11:49:19 → 11:49:19.7). So it cannot be found via
  the camp window / selector stack, and `FieldState.IsFieldFree()` would say free.
- Handling (`QuickRecoveryHandler`): postfixes on the selector's `Show()`,
  `Hide(Action)` and `ForceHide()` set/clear `_campOpen`; the field/camp variant is
  read through `CurrentList()` / `CurrentChoice()`. `IsCampRecoveryOpen` is a new
  `IsFieldFree()` gate so beacons, wall tones and guidance stay quiet during it.
  Safety: the flag is dropped if the selector goes inactive without a Hide().

### Bonus gauge — what the methods really mean (2026-09-18)

- `BattleManager.sphereBonusBuffLevel` + `GetBattleSphereBonusCurrentLevelRatio()` (0..1 within the level)
  are the truth; both PERSIST between battles.
- Level-up: inside native `IncreaseSphereBonusPoint(int)` (from `OnGetBonusSphere`). No hookable
  level-up method is called — poll the level.
- `BreakBonusGauge(bool isDirection)` + `Start/EndBonusGaugeBreakDirecting` = the gauge being LOST
  (the shatter), NOT a level-up. Also called on an empty gauge, so compare level/ratio before and after.
- `ResetBonusGauge(bool isAllReset)` also exists (unobserved; the poll reports any level drop it causes).
- Active bonuses: `GetSphereBonusBuffValueCache(BonusBuffType)` > 0; which bonuses a level grants
  comes from the formation's sphere allocation (`ConstBattleSphereBonusParameter`).

### Field quick heal — open state (2026-09-18)

- `UIFieldQuickRecoverySelector` is NOT a reliable open signal: after its first use it stays
  `activeInHierarchy` with `recoveryDataList` populated while closed (log 2026-09-18: silent
  real opens, heading re-read after every camp close).
- Authority: the owning `UIFieldWindow` (a parent of the selector; field `quickRecoverySelector`).
  Open = `IsOpened` AND `OpenFieldState == UIDefine.FieldState.QuickRecovery`. The same window
  hosts PickPocket, FishingBait, FishingResult and LocationPoint states.
- Opened by `GameUIManager.OpenFieldQuickRecoveryWindow()`; closed through
  `UIFieldWindow.OnEndQuickRecovery`. Show()/Hide()/ForceHide() on the selector are virtual
  overrides (hookable, the camp variant's pattern) — kept as the fallback if the window check fails.

### Camp menu story hint (speech balloon)
- Trigger: postfix on `UICampWindow.SetSpeechBalloon(List<UIDotCharacterData>, bool)` — fires
  when the camp screen (re)builds the dot-character strip. Wait ~0.4s then read.
- Text source: find active `UICampDotCharacterPresenter` objects; child transform path
  `ui_camp_speech_balloon_presenter/SpeechBalloon/Text` → `TMP_Text` (or `GameText`) `.text` =
  the current story objective/hint. Filter placeholders ("0000", "目的").
- On field (no camp open): `UIDestinationPresenter` objects hold destination/story text.

### On-screen dialogue we don't currently read (UIConversationWindow methods)
These carry messageIDs for the floating/auto conversation bubbles and center-screen messages
(likely the dialogue our mod misses). All hookable as postfix on `UIConversationWindow`:
- `SetConversationAutoMessage(string messageID, string fieldObjectName, float stopTime, bool isPrevChoiceMessage)` — timed auto bubbles
- `SetConversationMessageFollowObject(string messageID, string fieldObjectName)` — bubble following an object
- `ShowCenterMessage(string messageID)` / `ShowEntireMessage(string messageID)` — center/full-screen text
- `ShowEventInformation(string title, string description)` — event info panel
Resolve messageID → text via `TextManager.GetMessage(id, MessageType)` trying types 0, 100, 200
in order; if result == id (unresolved), suppress rather than speak the raw key.

### Guild quest screen (working readout recipe)
Selection arrives via the universal `OnSelected` hook, `TryCast<UIQuestListItemPresenter>`:
- Name: `presenter.missionName` (TMP_Text) → fallback `UIQuestListItemData.missionName` →
  fallback child GameObject "MissionName".
- State: `presenter.statePresenter.stateMessage` (TMP_Text) → fallback child
  "ui_mission_state_presenter" → fallback flags on `UIQuestListItemData`:
  `isEnd` / `isReportable` / `isReceived`.
- Description: `UIQuestDescriptionPresenter.questDescription` (find active instance).
- Rewards: `UIQuestDescriptionPresenter.rewardElementPresenterList` →
  `UIQuestRewardElementPresenter.rewardName` / `.rewardValue`.
- Achievable members: `UIQuestSelector.canAchievedPlayerList` (List<PlayerID>).

### Global TMP text tap (their catch-all, use sparingly)
They postfix `TMP_Text.set_text` / `SetText` game-wide and filter by GameObject name/path
keywords (dialog/conversation/popup/message/guide/description/footer...) with dedupe + dummy
filters. Powerful catch-all for text we can't source, but noisy by design — prefer the typed
hooks above; consider the TMP tap only as a targeted last resort (e.g. path-filtered to one
window).

### L1/R1 tab strips in camp — every screen that has one (2026-09-02)

Several camp sub-screens put a tab strip above the list. L1/R1 cycles it, the list
underneath is replaced, and the cursor row keeps its index — so nothing the mod polls
changes and the switch is silent unless it is detected explicitly. Two kinds:

**Character tabs** — every class carrying a `characterTabPresenter`, plus everything
deriving from `UICharacterTabListSelectorBase`. The complete camp set:
- `UICampEquipSelector` — `currentPlayerID` (own field)
- `UICampBattleSkillSettingSelector` — `currentPlayerID` (own field), Equip state only
- `UISelectBattleSkillSelector` : `UICharacterTabListSelectorBase` — battle skill list
  (root BattleSkill and Enhance → BattleSkillPoint)
- `UICampCombatSkillSelector` : `UICharacterTabListSelectorBase` — Enhance → CombatPoint
- `UICampSkillSelector` : `UICharacterTabListSelectorBase` — Enhance → Skill
- `UICampStatusSelector` — no player field; poll `pageIndex` instead
- Item Creation action lists — `currentTabIndex` into `executablePlayerIDList`
- (`UIBattleSpellSelector` has one too, but that is the battle screen, not camp.)

Name for a `PlayerID`: `ParameterManager.Instance.GetCharacterFirstName(playerID)`.

**Item category tabs** — `UIItemListSelectorBase.currentCategory`
(`UIItemListSelectorBase.Category`: New, All, Field, Eat, Weapon, Armor, Accessory,
Material, Other, Battle, KeyItem) is the change trigger. The spoken name comes from
`itemTabPresenter.itemTabDataList` — one `UIItemTabItemData` per Category value, indexed
by the enum, each with `tabName` (localized), `isDisplay` (a save with All hidden simply
skips it while cycling) and `canSelected`. That table is static, so the name always
belongs to the category you just detected.

⚠ TWO TRAPS HERE, both cost a test round on 2026-09-02:
- **Do not read `itemTabLabel.currentText`.** It is a `UICommonSelectTextPresenter`
  mid-animation and holds the PREVIOUS category. Log 26-9-2_20-17-0 announced ten
  switches and every one named the tab the user had just left — provable by pairing each
  announced name with the item list that came with it ("Weapons" arrived with Tuna
  Sashimi, "Armor" with a Longsword), and the first switch of a visit had no previous
  name at all, so it fell through to the generic fallback line.
- **Do not hook `UIItemTabPresenter`.** `UpdateTabName` is CallerCount 0. `SetTabName`
  looks safe at CallerCount 2 — it was patched, the patch applied, and it never fired
  once. Both are inlined into their callers, exactly like the caption methods.

**Announce it as one utterance, not two.** `ScreenReader.Say` interrupts, so speaking
the tab label and then the new row cuts the label off. `CampMenuHandler.TabSwitch.cs`
(`TabSwitchAnnouncer`) parks the label and the screen's own row announcement prefixes
it — `HasChanged(tab)` → `Park(label)` → force the row to re-read → `Decorate(text)`.
Screens announce their row from different places (equip slots poll, battle skills come
from `UIBattleSkillInformationPresenter.Set`), so the hook-driven ones call
`HasChanged` from inside the hook: the hook runs before the mod's per-frame poll, and
detecting the switch there is what lets the name merge into the row it is about to
speak. The per-frame poll stays as a safety net — `FlushIfStale` speaks a label nothing
claimed after 0.3 s, so a switch is never silent.

**Two speakers, one cursor move (root Special Arts/Spells).** That screen shows the
button-slot list AND a skill information panel for the same row, and the mod had a
handler on each: `UIBattleSkillInformationPresenter.Set` (rich readout) fires ~14 ms
before the slot poll, so both spoke and the shorter slot line cut the rich one off —
the details survived only on the moves where the slot index happened not to change.
Whenever two of the mod's own paths describe one cursor move, the LAST one to run must
be the only speaker: the hook now caches its text (`_battleSkillRootInfo`, with a
freshness stamp) and the slot poll speaks both as one sentence. A cached readout the
poll never claims is spoken alone a quarter-second later rather than dropped.

**Seed character tabs on a real PlayerID.** A camp sub-screen reports
`PlayerID.INVALID` (enum value 0) until the game populates it — up to a second after the
mod starts polling. Seeding a tracker on that value turns the first real character into
a phantom switch, and the screen greets the user with a name they did not ask for.
`TrackCharacterTab` ignores INVALID for exactly this reason.


### Localized menu labels — read the rendered text, not the data (2026-08-31)
`UICampMenuItemData` carries NO display text — only the `UIDefine.CampMenuItem` enum.
`menuItem.ToString()` is the English-only C# identifier and must never be spoken. The
localized label lives on the row presenter: `UICampMenuItemPresenter.gameText` (a GameText /
TMP_Text) holds the on-screen text the game already localized. General rule (how the
reference mod works in every language): when a data object has no text field, read the
presenter's rendered text components — the game has done the localization for you.

⚠ CAPTURE POINT MATTERS: `UICampMenuItemPresenter` NEVER fires the universal OnSelected
hook — camp root cursor movement is fully native (log-verified 2026-08-31; a first fix
built on OnSelected silently never ran). The working capture point is a postfix on
`UICampMenuItemPresenter.UpdateShow(ListItemDataBase)`, which fires from managed code when
the game POPULATES the row (menu build) — before any announcement. Cache the label keyed by
the CampMenuItem enum value. The same selector/data/presenter types also serve the camp's
second-level menus (System, Database children, Enhance children, Operation children), so
one hook localizes them all. Enum identifiers remain correct for internal gating
(`_lastRootMenuItemName`) — they are language-independent.

Counter-example: equip slot rows (`UIEquipListItemPresenter`) render only an icon + item
name, no slot label — invented labels like "Weapon"/"Accessory 1" must come from the mod's
own Loc files (`camp_equip_slot_*`).

### Localization gate rule (2026-08-31 sweep)
NEVER gate logic on text the game supplies (it is localized): the IC Train/Scout pollers
were gated on `data.categoryName == "Train"/"Scouting"` and went silently dead in French.
Language-safe identity for special skills: `UICampSelectSpecialSkillSelector.
GetCurrentSelectedSpecialSkillID()` → `SpecialSkillID` enum (TRAINING, SCOUT, ...);
per-skill selectors also expose `UICampSpecialSkillSelectorBase.CurrentSpecialSkill`.
Mod-internal label comparisons must compare against the same `Loc.Get(key)` expression,
never a literal (see nav generic-NPC numbering).

### Captions & cutscene subtitles (2026-09-01) — the text layer we were missing
Captions are a SEPARATE system from the dialogue box (`UIConversationPresenter`).
They cover the subtitle line under pre-rendered movies and the caption balloons
events place above characters. Nothing read them before `SubtitleHandler`.

Chain (all in `Il2CppGame`):
- `MovieCaptionClip` / `MovieCaptionBehaviour` — Timeline clips carrying `messageID`,
  `voiceType`, `voiceLang`. `OnBehaviourPlay` pushes the line, `OnBehaviourPause` clears it.
- `UICaptionController` — `ShowCaption(string caption, string messageID)`,
  `ShowCaption(string caption, Vector2 anchoredPosition, string messageID)`,
  `HideCaption(messageID)`, `HideMovieCaption()`, `HideAll()`.
- `UICaptionSelector` — `ShowCaption(message, anchoredPosition, messageID, isSubTitles)`,
  `ShowMovieCaption(message, anchoredPosition)`; holds `captionPresenter`,
  `movieCaptionPresenter`, `captionPresenterList`, plus `CreateCaptionPresenter()`.
- `UICaptionPresenter` — `SetCaption(string caption, bool isSubtitles)`, field `caption`
  (`GameText`) and `isSubtitles`.

**THE CAPTION METHODS DO NOT FIRE HARMONY HOOKS (log-proven 2026-09-01).** All four
patches attach ("4/4 caption patches applied") and NONE of them fired during a movie,
while the subtitle text was demonstrably on screen. They are small forwarding methods,
so the native build inlines them and the standalone copies Harmony patches are never
called — the same reason camp/shop menus are polled. Do not "fix" this by hooking
harder; anything upstream (`ShowCaption`, `ShowMovieCaption`) is inlined too.

**Working recipe — poll the presenter's own GameText:**
- `FindObjectsOfType<UICaptionSelector>(true)` — **includeInactive matters**: the caption
  UI root sits inactive between movies, so an active-only scan finds the selector a
  second AFTER the first subtitle has come and gone.
- Per selector read `movieCaptionPresenter`, `captionPresenter`, and every entry of
  `captionPresenterList` (event captions are created on demand and appended there).
- Per presenter read `caption` (a `GameText`, which derives from `TextMeshProUGUI`) and
  speak it when the string CHANGES. Gate on `gameObject.activeInHierarchy` — for
  captions this really is the show/hide signal (unlike most SO2R overlays, which stay
  active while hidden), so leftover text in a hidden presenter is never read.

Observed live (opening movie `ev_0100000_c`), subtitle lines ~6 s apart at:
`System/UIManager/uiRoot/Endroll/UICaptionController/UICaptionSelector/ui_movie_caption_presenter/Caption`
— note the **Endroll** UI root hosts the opening movie's captions, not just the credits.

`GameMovieManager.Instance` exposes `IsPlaying`, `IsPlayingOnUI`, `State` (MovieState),
`messageFileName`, `CurrentVoiceType`, `CurrentLanguage` — a reliable "a movie is running"
gate for diagnostics.

### Other notable techniques (for future reference, not copied)
- Camp menu footer description: `UICampMenuFooterPresenter.SetMenuDescription` +
  MenuDescription TMP object under `ui_footer_presenter/LayoutParent/MenuDescription`.
- Their beacons are pre-rendered panned WAV variants (`beacon_x_p{pitch}_pan{0-6}.wav`) played
  via winmm — same conclusion we reached about IL2CPP audio.
- NPC names: learned at talk-time and persisted to a JSON dictionary (they cannot resolve
  charaNameID natively either — matches our TextManager finding).
- Battle: `BattleManager.AddEnemy/OnDead/StartBattle/FinishBattle` patches for enemy
  announcements and defeat notifications.

## 20. Field Physics, Layers and the Wall Probe (manual navigation sounds)

Learned the hard way across 2026-03 to 2026-09; the rules that survived.

**Layer masks over-report.** `GameRenderManager.LayerMaskWall` (foot mask `0x04E28000` =
L15 ObjectWall + L17 PsynardWall + L21 GimmickWall + L22 Wall + L23 CharacterWall + L26
CameraDitherWall) includes layers that do NOT block the player: L15 `collider` volumes and
L22 `Col_Obstacle_Col*` boxes are walked through freely (SphereCast path validation had to
be removed 2026-03-15 for exactly this). `FieldPlayer.GetLayerMaskWall()` is per-form
virtual (bunny `0x00620000`, psynard L17 only). **Never decide "wall" from a layer hit.**

**Slope limits are not in the C# side.** Movement is native (`GameCharacterController`
task state machine, `FieldCharacterController.OnMove`); no `slopeLimit`/`stepOffset`
anywhere in `decompiled/`. Blocking comes from colliders, not terrain grade — the world
map allows ~84° climbs on foot. Any walkability rule must be validated against recorded
breadcrumbs, never assumed.

**Floor rules that passed the breadcrumb audit 100 % (Lasgus MF_0014_01A, Krosse Cave
MF_0008_01A, 2026-09-05)** — `FloorProbeGrid`:
- Downward `Physics.RaycastAll`, mask `~(1 << 6)` (everything but the player layer),
  `QueryTriggerInteraction.Ignore`.
- Floor = `hit.normal.y >= 0.4`; ignore triggers, `CapsuleCollider`, `SphereCollider`,
  `CharacterController` (character bodies) — `FloorProbeGrid.IsSolidFloorCollider`.
- Walkable step between samples: `|Δy| <= 0.67 × horizontal distance` (about 34°).
- Player capsule: radius 0.50 m, height 1.70 m (a 0.51 m gap wedges the player).

**`WallProbe` (2026-09-05).** Per horizontal direction: (A) floor walk — samples every
0.75 m to 6 m, downward ray from 2 m above the previous floor, 4 m long, pick the hit
nearest the previous height (tracks the current floor under bridges); `|Δy| > 0.5 m` →
obstacle (`FloorStep`), no floor → `FloorGap`. (B) two horizontal `RaycastAll`s at 0.35 m
and 0.9 m; only hits with `|normal.y| < 0.4` (near-vertical faces) count (`Face`) — slope
faces are ignored so slopes are judged by (A) alone. Nearer verdict wins. Audit: F11
`RunWallProbeAudit` replays every breadcrumb edge (minus ledges) and counts false walls;
the log names collider + layer for each so exclusions are evidence-based.

**Audio in IL2CPP.** `AudioClip.Create` is broken; use winmm. `PlaySound` = one one-shot
at a time per process (`AudioCuePlayer`). Loops and anything simultaneous go through
`LoopMixer` (own `waveOutOpen` handle, 44.1 kHz stereo 16-bit, 4 × 25 ms buffers, N
`MixerVoice`s summed in float). Cues must be integer PCM; `SoundBank` caches decoded mono
`short[]` and resamples. A voice not `Set()` for 0.5 s fades itself — handlers stall when
the mod menu is open (`Main.UpdateHandlers` is skipped), so never rely on a Stop() call.

## 21. World Map Manual Navigation (beacons, wall tones, spoken directions)

Added 2026-09-06 when the manual-navigation aids were extended to the world map.

**What exists on the world map (survey + data).** `ConstWorldmapSymbolParameter` (via
`ParameterManager.GetWorldmapSymbolParameter(WorldmapID)`) carries `mapIconType`; the shared
`Il2CppGame.MapIconType` enum has 31 members but the world map data only ever yields `CITY`
(12) and `DUNGEON` (13) plus a few `INVALID` `ob_*` objects — no ports, ships, stables or
castles. `ConstLocalityParameter.isDungeon` / `isPrivateAction` are cleaner flags than the
icon. `FieldManager.FieldLocationPointList` holds the 6 Expel discovery landmarks (class has
`worldmapVisibleDistance` fields — a world map feature by design); chests (`FieldTreasureBox`)
and enemy symbols (`FieldEnemy`) exist as scene objects; fishing spots come from
`ConstFishingWaterPlaceParameter` (no scene objects). A landed psynard (`FieldManager.FieldPsynard`,
`IsContactPsynard()`) and the world edge (`FieldManager.IsWorldMapOutsideArea(obj, out dir)`)
are the only other point-like things — unused so far.

**Beacons.** `NavigationHandler.Beacons.cs` maps `CAT_LOCATION` → `NavCueKind.City`/`Dungeon`
(by `NavItem.IsDungeon`), `CAT_MARKER` → `Location`, `CAT_CHEST` → `Chest`, fishing
(`NavItem.IsFishing`, any map) → `Fishing` sounding from `FacePosition` (the water). Range
comes from `ModSettings.WorldmapBeaconRangeMeters` (20–300, default 100) with a 10 m
full-volume distance. Every spatial cue pans through `SpatialPan.Compute` (camera-relative).
Psynard flight mutes the whole handler (`WorldmapTravel.CurrentMode()`).

**Wall tones.** `WallProbe.ProbeProfile`: `Field` = the audited field rules; `Worldmap(mask)`
= no slope verdicts at all (the map allows ~84° climbs; blocking is colliders), floor found
with `GameUtility.CalcHeight(origin 25 m up, 50 m)` so only "no ground" (ocean, void) is a
`FloorGap`, face rays on the LIVE per-form mask `player.GetLayerMaskWall()` (foot
`0x04E28000`, bunny `0x00620000` — the bunny crosses region walls) plus L24 streamed rock
bodies. Own switch `ModSettings.WorldmapWallTonesEnabled` (default off). Audit: `WorldmapTrail`
records a debug-only session trail (1.5 m spacing, links ≤ 4 m, same travel mode); F11 on
the world map runs `RunWorldmapWallProbeAudit` (`[WMWALLAUDIT]`, steep edges judged too)
before the pathfinding diagnostics.

**Spoken directions.** `NavigationHandler.Guidance.Worldmap.cs`. Route = `PlanWorldmapRoute`
(shared with auto-walk: entrance ring via `ComputeEnterTriggerTarget`; fishing items already
carry their baked stand, §23) then a private copy of `_wmPathWaypoints`. Simplify with
Douglas–Peucker (1.5 m) + a per-chord `WorldmapPathfinder.IsWalkableWorld` check, then the
shared leg builder (min leg 3 m, reached 2.5 m; 1 m each in `_wmTightTerrain`). No timed
re-plan: only stuck (`ManualNavHandler.TryGetMoveIntent` + no displacement 0.6 s → stamp
`_wmBlockedPositions`, max 5), drift (> 12 m / 4 m tight for 1.5 s) or a blocked straight
line to the aim, all via `WorldmapPathfinder.FindPath(..., skipComfortTier: floorTier)`,
min 4 s apart. Arrival: location = `EnterPromptMatches(label)` only (within 4 m of the ring
the legs aim at the town centre and say so); fishing = `FieldPromptHandler.FishPromptShowing`,
else a "face the water" hold with an 8 s timeout; others by radius. `CancelAutoWalk()` clears
`_isWorldmap`, so `GuideTo` restores it before planning. Traps: `_pathCorners` is a stub on
the world map; `IsFieldFree` flickers ~10 frames at terrain transitions (tolerated).

## 22. Private Action Availability (the real gate)

Added 2026-09-07 after a bug report: the PA cue played in Kurik (MF_0009_41A, main scenario
progress 1500000) although Square did nothing there.

- `ConstLocalityParameter.IsPrivateAction` is a **static** per-town flag: "this town has PAs
  at all". It never changes and is NOT the gate the game uses.
- The game gate is `GameManager.CanChangeToPrivateAction(FieldmapID)` (static; the overload
  the game itself calls, 5 callers; `LocalityID` and parameterless overloads also exist, plus
  an instance `FieldManager.CanChangeToPrivateAction()`). Native body, but its lambda
  `_CanChangeToPrivateAction_b__107_0(ConstDisableSystemParameter x)` shows it walks
  `ParameterManager.GetDisableSystemParameter(LocalityID)`.
- `ConstDisableSystemParameter` = per-locality scenario windows (`StartScenarioProgress`..
  `EndScenarioProgress`, optional `ScenarioFlag` list, optional `HeroID`) with switches
  `IsDisablePrivateAction`, `IsDisableSave`, `IsDisableAutoSave`, `IsDisableFastTravel`,
  `IsDisableCampMenu`, `IsDisableGuideMap`. Sibling gates: `GameManager.CanBeSave(bool)`,
  `FieldManager.CanBeFastTravel()`.
- "Currently IN PA mode" is a different thing: `GameManager.IsPrivateAction()` /
  `FieldManager.IsPrivateAction()`, and `FieldBitFlag.PrivateAction` (5) /
  `PrivateActionChanging` (10).
- `PrivateActionHandler` now polls the FieldmapID overload once a second and plays the cue on
  every false-to-true transition (re-arms when a town opens up mid-visit).

## 23. World Grid Data, Fishing Truth and the Baked Fishing Stands

Added 2026-09-07 (session 16) when the world map fishing spots moved from runtime shoreline
scans to a one-off bake. All game methods are native (stubs only) — behaviour below is from
the data model and the logs.

**The painted world grid.** `FieldManager.GetWorldGridData(ref Vector3)` (and the
parameterless overload = the player) returns a `WorldGridData` cell from a quadtree
(`ScriptableGridUnitData<WorldGridData>`, cell size via `GetGridSize()`): fields
`FishingWaterPlaceID` (byte, 0 = none — THE fishing paint, covering the water and its shore
band: contact ID 30 was reported 4 m up a bank), `ContinentID` (byte, land vs. sea),
`FootstepType`, `AlightFlag` (psynard landing), `EncountIDList`, `SurvivalAreaID`,
`LocationID`. Pure data lookup: no ray, no streamed chunks. Related:
`IsExistWorldGridData()`, `CanMove(int x, int y)` (the game's own grid walkability),
`GetWorldGridDataGridPosition(ref pos)`, `GetWorldGridDataPosition(x, y)`.
`GetContactFishingWaterPlaceID()` is simply this paint under the player — NOT "can fish".

**The bubble's own test.** `FieldManager.CheckWorldmapFishingPoint(ref Vector3 position,
ref Vector3 direction)` from the feet + facing (`FieldPromptHandler` FISHDIAG proves it returns
True at real spots). Static inputs `worldmapFishingFrontDistance` (5 m ahead must be painted
water), `fishingGroundDistance` (7 m max drop), `fishingCollisionDistanceRate` (1.4),
`worldmapFishingCharacterHeight` (10). Contains a physics ray, so far from the player it needs
the streamed collision loaded (bake: `WorldmapChunkLoader.LoadTile`). Pass COPIES by ref.
`IsWorldmapFishingPoint(point, out hitPosition)` is its water probe. The game's own per-frame
flag: `FieldManager.IsFieldFlag(FieldBitFlag.FishingPoint)` (= 3); `DisableFishingCheck` (= 23)
switches the check off. Per-map whitelists: `ParameterManager.GetFishingEnableAreaParameterList /
GetFishingDisableAreaParameterList(FieldmapID)`. `FlavorChatManager.disableFishingPointIDList` is
flavour-chat de-dup by GameObject instance ID — not a water place disable.

**Fishing skill gate (readable).** `ParameterManager.Instance.UserParameter.PartyParameter.LeaderID`
→ `UserParameter.GetCharacterParameter(PlayerID)` → `CharacterParameter.IsLearnedSpecialSkill(
SpecialSkillID.FISHING)` (FISHING = 18). `PartyManager` has NO static Instance. Used by
`NavigationHandler.LeaderHasFishingSkill` (fail open: unreadable = has the skill).

**Water height helpers (unused so far).** `FieldFishShadow.CheckWaterSurface(pos, out hit,
isMinHeight, radius, checkDistance)` [10 callers], `GameRenderManager.LayerMaskFishingAndHeight`,
`GetSeaWaterHeight(pos)`, `IsSeaCollider(col)`.

**The bake (Insert, debug mode, world map).** `WorldmapFishingStandBaker`: shoreline cells of
the mod grid (foot-passable with a non-passable 4-neighbour) inside each water box + 16 m whose
neighbour (or the cell itself) is painted → `CheckWorldmapFishingPoint` per tile with chunks
loaded, toward-water direction first then the compass → comfort regions (`BuildRegions` with a
0.60 m clearance floor) → one designated stand per water place (largest comfort region, widest
clearance, nearest the parameter position) + ≤ 5 alternates ≥ 16 m apart. File:
`UserData\SO2RAccess\stands\worldmap_expel.json` (wins) or the embedded `stands\*.json`;
`WorldmapFishingStands.Load`. Runtime (`CollectWorldmapFishingSpots`): designated stand unless
its region is proven off the player's start regions and an alternate matches (O(1)
`GetRegionId`); bunny needs `BunnyOk`; unreachable = annotated like towns. One route per walk
(`FindPath` falls to the floor tier itself). Arrival: creep onto the stand (0.3 m), face the
baked water point, 2 s bubble wait, then the skill-aware verdict.

**Phase 5 — the route proof (added 2026-09-08, session 18).** Why: the grid cannot tell a
beach from a ledge. Rock bodies (layer 24 `Mesh_Col` / `Mesh_L0`) are GROUND in the grid and
any height step up to 5 m per 0.5 m cell counts as walkable, so every mainland stand sits in
the same comfort region (1 247 065 cells on Expel) and the Phase 4 rank never separated
reachable stands from cliff-foot ones; the walk's pre-walk body sweep then refused every
fishing route from the Krosse gate (26 / 23 / 212 wedges, log 26-9-8_19-28-38). The proof
(`WorldmapFishingStandBaker.Proof.cs`) plans a real `WorldmapPathfinder.FindPath` route to
each kept stand from its nearest ENTRANCE ANCHORS — the map jumps with a ground-level trigger
ring (`WorldmapMapjumps.CollectAll`, the scan shared with the reachability cache and the
safe-exit picker; label = destination fieldmap ID) in the same foot region (region 0 never
skips) — loads every 64 m tile the route crosses (`WorldmapChunkLoader.LoadTile` accumulates
until `UnloadTile`) and sweeps the body capsule along it with `NavigationHandler.
SweepSegmentBlocked` and the live `ResolveBodySweepMask`. Exemptions: 16 m at the anchor
(`WmSweepEndpointExemptDist`, gate pinches are sweep-conservative) but only 2 m at the stand —
the last metres ARE the cliff question. Wedges are stamped and re-planned (3 rounds, comfort
pass skipped once it fell to the floor, as in the walk). Budget: 2 proven stands or 8 attempts
per place, 3 anchors per stand, 300 s per bake (places past it stay "proof unknown"). Stands
re-rank: proven comfort > proven floor > unproven. JSON v2 fields: stand `ProvenFrom`,
`ProofTier`, `ProofRouteMeters`, `ProofRounds`; place `ProofAttempted`, `ProvenStands`,
`ProofAttempts`; file `ProofsBaked`, `ProofAnchors`. Runtime rule (`ChooseFishingStand`): on
foot with `ProofsBaked && ProofAttempted`, only proven stands are candidates and a place with
none is annotated "unreachable on foot"; a v1 file or a budget-skipped place is "proof
unknown" (never annotated); bunny keeps the region rule only (proofs are foot sweeps). F7's
route auditor also audits the designated stand + first alternate of every place from the
player (`AuditOneTarget`), printing the first wedge's distance from the player and from the
target — wedges near the player = start-side problem, near the stand = proof too lenient.

**2026-09-09 (session 19) — dense shoreline, nearest stand, gate pinch rule, bubble creep.**
Log evidence (Expel, 20:31–20:41): the Krosse lake (place 25) had 1492 verified shore cells
but the v2 file kept 6, all on the far shore, so the list said "77 m" while the player fished
27 m from the gate (contact ID 25 at (−83.7, −80.3)). Changes: (1) JSON **v3** keeps the whole
verified shoreline thinned to one stand per 5 m (`StandSpacingMeters`, cap 400 per place,
candidates cap 8000; Phase 1 `DistToParam` now measured against the PAINTED place — it was
the box being scanned). (2) `ChooseFishingStand(file, place, mode, startRegions, playerPos,
…)` picks the stand NEAREST the player among mode-passable, region-matching stands; on foot with
proofs, an unproven nearest stand carries the nearest proven stand as `NavItem.FishingFallback`
and `PlanWorldmapRoute` retargets to it ONCE, silently, when the first plan is refused (user
decision). (3) **Gate pinch rule**: a blocked sweep segment within `WorldmapMapjumps.
RingWedgeMeters` = 5 m of an entrance trigger ring is neither counted nor stamped — in
`SweepRoute` (bake), `CountRouteWedges` (walk) and the F7 `SweepLeg`. Calibration: Krosse gate
wedges 0.0–3.0 m from MF_0006_01A, Arlia 4.2–4.4 m from MF_0003_01A, rock-belt wedges ≥ 23 m.
`EnsureWmMapjumpCache` rescans at plan/audit time: the `FieldMapjumpCollision` objects do NOT
exist for the first seconds after a map load, so the list-build scan right after leaving a town
found none and every later sweep ran with an empty ring list. (4) **The bake's stand test is
necessary, not sufficient for the bubble**: at the Salva stand (−137.5, −451) the game showed
the prompt only ~2.7 m closer to the water (three sightings at z ≈ −453.8), with
`CheckWorldmapFishingPoint` flickering true/false and `FieldBitFlag.FishingPoint` false on the
stand. The arrival therefore creeps ON from the stand toward the baked water point until the
edge stalls the player (< 0.15 m in 0.7 s), 8 m or 8 s, then holds 2 s (`UpdateWaterCreep`).
The `GetContactFishingWaterPlaceID` paint reads true well inland (ID 21/25 at the Krosse gate).

**2026-09-09 round 2 (log 21:25–21:33).** (5) **The game's facing comes from INPUT, not the
transform.** Three arrivals ended at the water's edge with `player.transform.rotation` written
toward the water every frame, `CheckWorldmapFishingPoint(feet, transform.forward)` false, no
bubble; seconds later, at the same position, the bubble appeared as soon as the player turned
or pushed the stick by hand. The feet+forward call also flickers true/false at a fixed spot
(party followers in the ray?). So the arrival now keeps pressing the stick gently toward the
water and sweeps the push direction (0/±35/±70°, `UpdateFacingSweep`); diagnostics log
`CheckFishingPoint(FieldPlayer)` beside the feet+forward call to learn which one the game
trusts. **ANSWER (log 2026-09-19 15:49:53, Lacuer east lake, water place 33): the game trusts
`CheckFishingPoint(FieldPlayer)`** — the bubble appeared in the very frame it turned true, at
(1019.0,−355.7) y −0.4 (shallow water, shore without an edge), while the feet+forward
`CheckWorldmapFishingPoint` (the bake's test) had been true since the stand 8–9 m earlier. The
arrival therefore polls the player check every frame (`UpdateStandStill`) and no longer stops at
the baked water point. The player check needs the live player, so the bake cannot call it per tile.
**2026-09-19 16:17 follow-up:** from the same stand (1011,−351) a 12 m creep SOUTH never turned the
player check true; by hand the bubble came at (1018.4,−360.9), also walking south — the zone is a
~2 m wide strip 7 m BESIDE the creep line, so position decides, not facing. Hence (a)
`WorldmapBubbleMemory`: every real world map bubble is saved (`stands\bubbles_*.json`, embedded seed +
UserData) and the creep aims at the nearest remembered point within 25 m of the stand; (b) the shore
search (`NavigationHandler.Worldmap.FishingSearch.cs`): lanes 3 m apart beside the creep line, both
sides, player check polled every frame. (6) **Gate pinch = the town's own collider, not just "near the ring".** The 5 m rule
forgave an Arlia riverbank `Col_Obstacle` L23 4.6 m from ring MF_0003_01A and the walk stuck
on `Mesh_Col` exactly there. `WorldmapMapjumps.IsGateCollider`: the blocker has a
`FieldMapjumpCollision` among its parents (`GetComponentInParent`); `IsGatePinch` requires
both and logs the blocker's transform chain — if Krosse refuses again, that line shows the real
hierarchy. (7) **A battle destroys the cached ring colliders**: the world map scene reloads
(`mb_1001_11a` and back), every cached `Collider` goes Unity-null and `ClosestPoint` throws →
"no ring data" from a full cache. `WorldmapMapjumps.IsUsable` (null / `bounds` throws) drives
the rescan. (8) A showing bubble counts as arrival only within 12 m of the stand
(`WmFishBubbleArrivalMeters`): the prompt was up at the Krosse gate when a 16 m walk started.
(9) **2026-09-13: the bubble check does not prove the cell is standable.** The Arlia stand 5 m south
of the town gate (−43,−410) passed `CheckWorldmapFishingPoint` (its ray toward the water sees water)
but sat 0.2 m inside Arlia's own wall boxes (`Col_Obstacle` L22 `Wall_Arlia/Wall/mp_1001_001a_Col/Map`
and L23 `CharaWall_ArliaSalba`): a strip of shore on the far side of the town wall. The walk grid does
not carry that wall, the bake proof exempted the entire 4.5 m route (16 m start + 2 m goal exemptions)
and the walk's sweep exempts 16 m at both ends, so nothing refused it; the player stalled 1.2 m short
every time and from there the same ray hits the wall (gameCheck false for every facing). Fix: phase 2
now runs `NavigationHandler.BodyWallClearance` after the bubble check — the SAME capsule, ground probe
and mask as `SweepSegmentBlocked`; `Physics.OverlapCapsule` standing still → dropped; otherwise 8
compass `CapsuleCast`s up to 2 m give `FishingStandEntry.WallClearance` (evidence in the file, the
"Phase 2: place N" histogram and the F7 `wallClear=` label). Runtime: a stand creep ending more than
0.6 m from the cell speaks "Stopped N meters short" instead of "try stepping toward the water".
Same day, second round: the pocket had another cell 1.5 m east (clearance 0.73 m, proven "5 m") and
the walk stuck on the wall strip. Evidence that the proof's 2 m goal exemption must stay: working
stands (Arlia place 19, Salva place 21) also show a sweep wedge 1 m from the target in the
no-exemption audit; clearance alone does not separate good (0.53) from bad (0.73). Two rules added
(user-approved, both evidence-logged): cells INSIDE a town entrance trigger are dropped
(`ApplyEntranceRingRule`, `RingDistance` field — Cross means Enter there), and the fit test uses the
game's capsule radius 0.5 (`StandBodyRadius`). `Save` backs the old file up as
`worldmap_<map>.previous.json` and `LogBakeDiff` lists every old stand that is GONE or moved with the
rule that dropped it, so a rule is judged by what it took away.
(10) **2026-09-13, the actual root: the walk GRID had the wall as open ground, because the F9 bake's
entrance-clearing post-pass lifted every blocked bit inside each entrance trigger's bounding box —
probe walls included.** Evidence: the F10 grid-truth probe (`WorldmapGridDiagnostics.TruthProbe.cs`,
a live replay of the bake's own probe `WorldmapGridProbe.ProbeObstacles`) BLOCKS every Arlia wall-strip
cell (L22 `Wall_Arlia` 0.00–0.42 m away) that the grid holds open with no clearance record, and each
of those cells reads `MapJump … 0.00 m` = inside the trigger. A fresh bake reproduced it exactly.
Fix: `FloodFillSeal` sets a per-mode seal bit (`FlagSealedInterior` = foot, `FlagBunnySealed` = 8);
`ClearEntranceTriggers` lifts only seal bits and logs "wall cells kept blocked" per trigger.
Downstream, the Arlia pocket becomes an enclosed region: the fishing candidate scan may still list
its cells, but region matching rejects them and the proof finds no anchor in their region.
Tools that stay: F10 `[WMTruth]` (FOCUS = nearest stand + neighbours, `seen:` = every collider
within 1 m on any layer), `SaveGrid` backup to `worldmap_<map>.previous.grid`, scratchpad `grid_diff.py`.
(11) **Resolution chosen (same evening): the walk grid stays as validated in July; the fishing proof
carries the fix.** A gate-wall patch of the grid (`WorldmapGridGatePatch`, Delete key) was built, applied
and REVERTED: an offline flood on both grids showed it removes the comfort tier of validated roads (Marze →
Krosse Cave, Krosse → Harley) because July's comfort network crossed gate boxes through the erased wall
cells; the fixed F9 clearing pass (`FlagBunnySealed`, seals only) stays for future bakes. Removing the
proof's 16 m start exemption over-refused (17 → 7 proven lakes: at every gate the blocker on the ring is
the town's WALL, so `IsGatePinch` never fires there). Final rule in `SweepRoute`: the 16 m start
exemption is back, those segments are still swept and blocked non-gate ones are counted as hidden
start wedges; a proven stand with any must pass `IsEnclosed` — a body-swept flood (8 neighbours, climb
rule, `SweepSegmentBlocked` per step, gate pinches forgiven) within 14 m that must reach a cell 12 m
away. Enclosure within the box implies no route from outside, so it can only refuse spots the body
cannot leave; the July rebake confirmed 17 proven lakes, 0 proofs lost, 8 enclosed gate pockets
(`FishingStandEntry.Enclosed`). Bake-position variance remains: the Arlia pocket cell passes the bubble
check when baked from Krosse and fails from Arlia (flicker test 0/297 within one bake).

## Change History

- **2026-09-13 (session 21):** §23 (9) — bubble-verified stand inside Arlia's town wall; bake body-fit test (`BodyWallClearance`), `WallClearance` file field, "stopped short" verdict.
- **2026-09-09 (session 19):** §23 addendum — stands file v3 (dense shoreline), nearest-stand rule + silent proven fallback, gate pinch rule (5 m from an entrance ring; calibration numbers), mapjump colliders absent right after a map load, bubble band ~3 m beyond the bake-verified stand → water creep

- **2026-09-07:** §22 added — private action availability: `GameManager.CanChangeToPrivateAction(FieldmapID)` + `ConstDisableSystemParameter` scenario windows; the locality flag is static and wrong as a gate
- **2026-09-06:** §21 added — world map manual navigation: symbol/landmark survey, beacon mapping, world map probe profile + trail audit, guidance follow policy
- **2026-09-05:** §20 added — field physics/layer rules, WallProbe design, LoopMixer audio notes
- **2026-02-22:** File created during setup
- **2026-02-22:** Full Tier 1 analysis complete — input system, UI, text, scenes, singletons documented
- **2026-02-22:** Config menu analysis complete — UIConfigMenuSelector, UIConfigGroupSelectorBase, UIConfigGroupSelectItemSelector documented
- **2026-02-23:** Hero select + new game settings analysis — UITitleSelectHeroSelector, UITitleSelectVoiceSelector, UITitleSelectVoiceMenuSelectItemPresenter, UICommonSelectTextPresenter documented
- **2026-02-23:** Gamepad binding menu analysis — UIConfigGamePadSelector, UIKeyConfigSelector, UIKeyConfigSelectItemPresenter documented. Key finding: `icon` field holds the assigned button sprite; `pressKeyText` is the capture-mode prompt text, not the assignment.
- **2026-02-23:** Save/load menu analysis — UISaveLoadWindow, UISaveLoadSelector, UISaveLoadListItemPresenter, UISaveLoadListItemData documented. All slot info pre-formatted as strings in the data object.
- **2026-02-23:** Dialogue, tutorial, and popup analysis — UIConversationPresenter, UITutorialInformationPresenter, UITutorialInformationData, UIDialogPresenter, UIDialogWindow documented.
- **2026-06-13:** Field icons & notifications analysis (Section 18) — UIFieldController is the central field-notification controller. Key finding: the "X Jump" prompt is an "operation" (button guide), not a FieldIconType (which only has LocationPoint/Fishing). Best hook is `UIFieldOperationPresenter.Set` [CallerCount(7)] with readable `operationTextList`; `HideOperation`/`Hide` are [CallerCount(0)] (poll activeInHierarchy). Also catalogued: ShowEmotion (17 EmotionTypes), area/mode banners, and ~15 corner info toasts (item/money/EXP/level/skill/talent/member).
- **2026-08-29:** Section 19 added — analysis of third-party ScreenReaderMOD (Galaxy Laboratory MM). Key findings: `UICanSelectedListItemPresenterBase.OnSelected` is a universal Harmony-hookable selection event; camp story hint via `UICampWindow.SetSpeechBalloon` + `UICampDotCharacterPresenter` balloon text; missed dialogue via `UIConversationWindow` auto/center/entire message methods (messageID → TextManager); full guild quest readout recipe.
- **2026-09-01:** Captions & cutscene subtitles (Section 19) — `UICaptionPresenter.SetCaption(string, bool)` is the single funnel for movie subtitles and event caption balloons; separate system from `UIConversationPresenter`. Also noted: the config category list is built from generic `UICommonListItemPresenter` rows, so the universal OnSelected net must be suppressed while `UIConfigWindow.IsOpened` (was double-speaking every config category).
- **2026-09-01 (later):** Caption hooks proven dead — `UICaptionPresenter.SetCaption` and the `ShowCaption`/`ShowMovieCaption` methods above it are inlined natively, so Harmony postfixes attach but never fire. Subtitles must be POLLED off `UICaptionPresenter.caption` (GameText); selectors found with `FindObjectsOfType<UICaptionSelector>(true)` — includeInactive required. Opening-movie captions live under the **Endroll** UI root.
- **2026-09-07 (session 16):** Section 23 added — world grid data (`WorldGridData.FishingWaterPlaceID` paint via `FieldManager.GetWorldGridData`), the bubble test `CheckWorldmapFishingPoint` and its statics, `FieldBitFlag.FishingPoint/DisableFishingCheck`, the readable Fishing-skill gate (`PartyParameter.LeaderID` → `IsLearnedSpecialSkill`; `PartyManager` has no Instance), `FlavorChatManager.disableFishingPointIDList` = flavour-chat de-dup, and the baked fishing stands (Insert key bake, stands file, runtime lookup).
