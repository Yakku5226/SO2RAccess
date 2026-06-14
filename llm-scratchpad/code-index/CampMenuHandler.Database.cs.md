# CampMenuHandler.Database.cs (780 lines)

Database sub-menu accessibility partial class: Tutorial, Enemy Picture Book, Item Picture Book, Fish Picture Book, Location Picture Book, and Player Data (virtual cursor). All navigation is polling-based (native-only; no Harmony hooks fire for cursor movement). Browse announces name + New flag + locked status + position; confirm (Decision key) reads full detail. Locked entries always announced.
namespace: SO2RAccess (line 6)
usings (non-System / notable only): Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 17)
Partial class — this file adds Database sub-screen fields and update methods. Base class and other partials live in separate files.

fields/properties (declaration order):
- _tutorialSelector : UICampTutorialListSelector (line 22)
- _tutorialListBase : UIListSelectorBase (line 23)
- _tutorialState : SubScreenState (line 24)
- _enemyPBSelector : UICampEnemyPictureBookSelector (line 27)
- _enemyPBListBase : UIListSelectorBase (line 28)
- _enemyPBState : SubScreenState (line 29)
- _itemPBSelector : UICampItemPictureBookSelector (line 32)
- _itemPBListBase : UIListSelectorBase (line 33)
- _itemPBState : SubScreenState (line 34)
- _fishPBSelector : UICampFishPictureBookSelector (line 37)
- _fishPBListBase : UIListSelectorBase (line 38)
- _fishPBState : SubScreenState (line 39)
- _locationPBSelector : UICampLocationPictureBookSelector (line 42)
- _locationPBListBase : UIListSelectorBase (line 43)
- _locationPBState : SubScreenState (line 44)
- _playerDataSelector : UICampPlayerDataSelector (line 47)
- _playerDataPresenter : UICampPlayerDataPresenter (line 48)
- _playerDataState : SubScreenState (line 49)
- _playerDataIndex : int (line 50)
- _playerDataLastIndex : int (line 51)
- _playerDataTotal : int (line 52)

methods (declaration order):
- void UpdateTutorialSelector() (line 62)  [private instance]
  - note: gates on _lastRootMenuItemName == "Tutorial"; casts selector to UIListSelectorBase on first call; calls CheckTutorialDetailPress if index unchanged; distinguishes locked (empty name or empty informationDataList), isNew, and normal entries
- void AnnounceTutorialBase(UICommonBookListItemData item, int idx, int total) (line 131)  [private static]
  - note: fallback used when TryCast to UICampTutorialListItemData fails
- void CheckTutorialDetailPress(int idx) (line 144)  [private instance]
  - note: reads first informationDataList entry (title + description) on Decision key press; silent if locked
- void UpdateEnemyPictureBook() (line 191)  [private instance]
  - note: gates on _lastRootMenuItemName == "EnemyList"; uses info.isRelease to detect locked; calls CheckEnemyDetailPress if index unchanged
- void CheckEnemyDetailPress(int idx) (line 254)  [private instance]
  - note: builds StringBuilder with name, isBoss, hp, exp, money, dropItemList (joined), livingPlace; announces on Decision key press
- void UpdateItemPictureBook() (line 313)  [private instance]
  - note: gates on _lastRootMenuItemName == "ItemPictureBook"; locked when itemName is null/empty
- void CheckItemPBDetailPress(int idx) (line 375)  [private instance]
  - note: announces itemName + itemDescription on Decision key press
- void UpdateFishPictureBook() (line 412)  [private instance]
  - note: gates on _lastRootMenuItemName == "FishPictureBook"; locked when info == null or !info.isRelease
- void CheckFishDetailPress(int idx) (line 475)  [private instance]
  - note: builds full fish detail: name, isRare, isCrown, description, fishShadow, livingPlace, fishingCount, maxLength
- void UpdateLocationPictureBook() (line 526)  [private instance]
  - note: gates on _lastRootMenuItemName == "Location"; locked when info == null or !info.isRelease
- void CheckLocationDetailPress(int idx) (line 589)  [private instance]
  - note: announces locationName + discoveryName + description on Decision key press
- void UpdatePlayerData() (line 633)  [private instance]
  - note: virtual cursor (no native list selector); intercepts Up/Down via GameInputManager.IsDown; announces category name when crossing boundaries between Battle/Collection/Other
- int GetPlayerDataTotal() (line 710)  [private static]
  - note: sums counts of battleDataPresenterList + collectionDataPresenterList + othersDataPresenterList
- int GetPlayerDataCategory(int flatIndex) (line 722)  [private static]
  - note: maps flat index to category 0=Battle, 1=Collection, 2=Other by counting each list
- string GetPlayerDataCategoryName(int flatIndex) (line 733)  [private static]
- string GetPlayerDataStat(int flatIndex) (line 744)  [private static]
  - note: returns "label: value" from the presenter's label/value TMP text fields; returns "???" if item is null
- UICampPlayerDataItemPresenter GetPlayerDataItem(int flatIndex) (line 755)  [private static]
  - note: walks the three presenter lists in order to resolve a flat index into the correct presenter
