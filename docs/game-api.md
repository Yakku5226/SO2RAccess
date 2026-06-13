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

Keys confirmed NOT in the InputAction enum — safe to use for mod functions:

- **F1** — reserved for mod Help
- **F10** — available
- **F11** — available
- **F12** — reserved for debug mode toggle
- **NumPad keys** — all appear safe
- **Note:** Game uses controller-mapped actions, not raw F-keys or NumPad

---

## 4. Input System API

**File:** `decompiled/Assembly-CSharp/Il2CppGame/GameInputManager.cs`
**Class:** `GameInputManager : SingletonMonoBehaviour<GameInputManager>`

### Key Methods

```csharp
bool IsDown(InputAction inputAction)          // held down this frame
bool IsRelease(InputAction inputAction)       // released this frame
bool IsRepeat(InputAction inputAction)        // repeated press
InputKey GetBindInputKey(InputAction action)  // current bound key
InputKey GetDefaultBindInputKey(InputAction action)
InputAction GetAliasInputAction(InputAction action)
Vector2 GetRightStick()
Vector2 GetLeftStick()
Vector2 GetDPad()
bool IsMouseLeftClickDown()
bool IsMouseRightClickDown()
void SetInputTask(InputTask inputTask)        // set active input handler
```

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
int        flag
```
Position via `chest.transform.position`.

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

CAVEAT (verify in-game): confirm the jump prompt actually flows through `Set` by logging
`operationList` contents + `isPlayer` in a temporary postfix. The shared presenter is also used
for non-jump button guides (e.g. talk/interact prompts), so the cue must filter on the prompt
content (jump action) and/or `isPlayer = true`, not just "any operation shown".

### World-space icons — UIFieldIconSelector (only 2 types)
`UIFieldController.ShowIcon(string fieldObjectName, UIDefine.FieldIconType type,
ref Vector3 worldOffset)` — [CallerCount(3)]. Backed by `UIFieldIconSelector.ShowFieldIcon(...)`
(several overloads, [CallerCount(1)]). `UIDefine.FieldIconType` has ONLY:
- `LocationPoint` — discoverable map location sparkle (already handled via
  `UIFieldLocationPointPresenter.Set`, see Location Discovery in MEMORY).
- `Fishing` — fishing-spot icon (fishing nav already handled).
`HideIcon(string)` / `HideIcon(Transform)` / `HideAllIcon()` — [CallerCount(0/0/2)].

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

## Change History

- **2026-02-22:** File created during setup
- **2026-02-22:** Full Tier 1 analysis complete — input system, UI, text, scenes, singletons documented
- **2026-02-22:** Config menu analysis complete — UIConfigMenuSelector, UIConfigGroupSelectorBase, UIConfigGroupSelectItemSelector documented
- **2026-02-23:** Hero select + new game settings analysis — UITitleSelectHeroSelector, UITitleSelectVoiceSelector, UITitleSelectVoiceMenuSelectItemPresenter, UICommonSelectTextPresenter documented
- **2026-02-23:** Gamepad binding menu analysis — UIConfigGamePadSelector, UIKeyConfigSelector, UIKeyConfigSelectItemPresenter documented. Key finding: `icon` field holds the assigned button sprite; `pressKeyText` is the capture-mode prompt text, not the assignment.
- **2026-02-23:** Save/load menu analysis — UISaveLoadWindow, UISaveLoadSelector, UISaveLoadListItemPresenter, UISaveLoadListItemData documented. All slot info pre-formatted as strings in the data object.
- **2026-02-23:** Dialogue, tutorial, and popup analysis — UIConversationPresenter, UITutorialInformationPresenter, UITutorialInformationData, UIDialogPresenter, UIDialogWindow documented.
- **2026-06-13:** Field icons & notifications analysis (Section 18) — UIFieldController is the central field-notification controller. Key finding: the "X Jump" prompt is an "operation" (button guide), not a FieldIconType (which only has LocationPoint/Fishing). Best hook is `UIFieldOperationPresenter.Set` [CallerCount(7)] with readable `operationTextList`; `HideOperation`/`Hide` are [CallerCount(0)] (poll activeInHierarchy). Also catalogued: ShowEmotion (17 EmotionTypes), area/mode banners, and ~15 corner info toasts (item/money/EXP/level/skill/talent/member).
