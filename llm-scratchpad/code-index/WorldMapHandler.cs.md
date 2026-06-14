# WorldMapHandler.cs (308 lines)

Announces world map fast travel menu navigation to the screen reader.
Detection: UIWorldMapWindow via FindObjectOfType (lazy, throttled), IsOpened polled each frame.
All navigation is native C++ — no Harmony hooks; polling approach (same as camp/shop menus).
Location list: UIWorldMapLocationSelector polled for tab (City/Dungeon) and index changes.
Point list: UIWorldMapPointSelector polled; handles two data types: UIWorldMapLocationListItemData
(sub-areas) and UIWorldMapLocationListItemFastTravelData (fast travel destinations).
namespace: SO2RAccess (line 3)
usings (non-System / notable only): Il2CppGame

## class WorldMapHandler (line 23)
Announces world map fast travel menu navigation to the screen reader.

fields/properties (declaration order):
- _window : UIWorldMapWindow (line 27)
- _isOpen : bool (line 28)
- _findCooldown : int (line 29)  — throttle counter (60 frames) for FindObjectOfType calls
- _fastTravelSelector : UIWorldMapFastTravelSelector (line 32)
- _locationSelector : UIWorldMapLocationSelector (line 33)
- _pointSelector : UIWorldMapPointSelector (line 34)
- _locationBase : UIListSelectorBase (line 35)
- _pointBase : UIListSelectorBase (line 36)
- _lastState : UIWorldMapFastTravelSelector.State (line 39)
- _lastTabType : UIWorldMapLocationSelector.TabType (line 40)
- _lastLocationIndex : int (line 41)
- _lastPointIndex : int (line 42)

methods (declaration order):
- void OnSceneChanged() (line 51)
  - note: Clears _window and all selectors on scene change.
- void Update() (line 63)
  - note: Called every frame from Main.UpdateHandlers(). Calls DetectWindow(); if open, reads currentState, handles Location<->Point transitions (resets point index on enter, seeds location index on return), then delegates to UpdateLocationList or UpdatePointList.
- void DetectWindow() (line 110)
  - note: Lazy-finds UIWorldMapWindow (throttled to every 60 frames). On open transition: calls CacheSelectors, announces worldmap_open. On close: calls ClearSelectors.
- void CacheSelectors() (line 157)
  - note: Grabs fastTravelSelector, locationSelector, pointSelector, casts to UIListSelectorBase; seeds _lastLocationIndex from current index to avoid stale announcement on open.
- void ClearSelectors() (line 184)
  - note: Nulls all selector references and resets all polling state.
- void UpdateLocationList() (line 204)
  - note: Checks currentTabType for City/Dungeon tab change (announces tab name, resets location index). Then checks currentIndex change; reads UIWorldMapLocationListItemData from currentDataList; appends "unavailable" prefix if !canSelected.
- void UpdatePointList() (line 256)
  - note: Checks currentIndex change. Tries TryCast to UIWorldMapLocationListItemFastTravelData (fast travel dest, canDecisioned) first, then UIWorldMapLocationListItemData (sub-area, canSelected). Announces name with unavailable prefix if not selectable.
