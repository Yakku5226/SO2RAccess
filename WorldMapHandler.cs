using Il2CppGame;
using System;

namespace SO2RAccess
{
    /// <summary>
    /// Announces world map fast travel menu navigation to the screen reader.
    ///
    /// Features:
    ///   - Location list (cities/dungeons): polled via UIWorldMapLocationSelector.currentIndex.
    ///     Announces location name and tab type (City/Dungeon) on change.
    ///   - Point list (fast travel destinations within a location): polled via
    ///     UIWorldMapPointSelector.currentIndex. Announces point name and availability.
    ///   - Heading announced on open ("Fast travel.") and on Location↔Point transitions.
    ///
    /// Detection: UIWorldMapWindow found via FindObjectOfType (lazy init with throttle),
    /// IsOpened polled each frame to detect open/close transitions.
    ///
    /// All navigation in this menu is native C++ — no Harmony hooks fire for cursor
    /// movement. Polling is the correct approach (same as camp/shop menus).
    /// </summary>
    public class WorldMapHandler
    {
        #region Fields

        // Window — found via FindObjectOfType, cached permanently per scene.
        private UIWorldMapWindow _window = null;
        private bool _isOpen = false;
        private int _findCooldown = 0;

        // Selectors cached from window on open.
        private UIWorldMapFastTravelSelector _fastTravelSelector = null;
        private UIWorldMapLocationSelector _locationSelector = null;
        private UIWorldMapPointSelector _pointSelector = null;
        private UIListSelectorBase _locationBase = null;
        private UIListSelectorBase _pointBase = null;

        // Polling state.
        private UIWorldMapFastTravelSelector.State _lastState = UIWorldMapFastTravelSelector.State.Invalid;
        private UIWorldMapLocationSelector.TabType _lastTabType = (UIWorldMapLocationSelector.TabType)(-1);
        private int _lastLocationIndex = -1;
        private int _lastPointIndex = -1;

        #endregion

        #region Public API

        /// <summary>
        /// Clears cached window reference on scene change.
        /// </summary>
        public void OnSceneChanged()
        {
            _window = null;
            _isOpen = false;
            _findCooldown = 0;
            ClearSelectors();
        }

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Detects fast travel menu open/close and polls selectors.
        /// </summary>
        public void Update()
        {
            DetectWindow();
            if (!_isOpen) return;

            try
            {
                var state = _fastTravelSelector.currentState;

                // Detect Location ↔ Point transitions.
                if (state != _lastState)
                {
                    _lastState = state;

                    if (state == UIWorldMapFastTravelSelector.State.Point)
                    {
                        // Entering point list — reset point index so first item announces.
                        _lastPointIndex = -1;
                    }
                    else if (state == UIWorldMapFastTravelSelector.State.Location)
                    {
                        // Returning to location list — seed location index to avoid
                        // re-announcing the already-selected location.
                        if (_locationBase != null)
                            _lastLocationIndex = _locationBase.currentIndex;
                    }
                }

                if (state == UIWorldMapFastTravelSelector.State.Location)
                    UpdateLocationList();
                else if (state == UIWorldMapFastTravelSelector.State.Point)
                    UpdatePointList();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"WorldMap: poll error: {ex.Message}");
            }
        }

        #endregion

        #region Detection

        /// <summary>
        /// Finds UIWorldMapWindow lazily (throttled) and polls IsOpened
        /// to detect open/close transitions.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _window = UnityEngine.Object.FindObjectOfType<UIWorldMapWindow>();
                    if (_window != null)
                        DebugLogger.LogState("WorldMap: cached UIWorldMapWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"WorldMap: detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                bool isOpen = _window.IsOpened;

                if (isOpen && !_isOpen)
                {
                    _isOpen = true;
                    CacheSelectors();
                    ScreenReader.Say(Loc.Get("worldmap_open"));
                    DebugLogger.LogState("WorldMap: opened.");
                }
                else if (!isOpen && _isOpen)
                {
                    _isOpen = false;
                    ClearSelectors();
                    DebugLogger.LogState("WorldMap: closed.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"WorldMap: IsOpened poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// Caches all selectors from the window on open and seeds polling state.
        /// </summary>
        private void CacheSelectors()
        {
            try
            {
                _fastTravelSelector = _window.fastTravelSelector;
                _locationSelector = _fastTravelSelector?.locationSelector;
                _pointSelector = _fastTravelSelector?.pointSelector;
                _locationBase = _locationSelector?.TryCast<UIListSelectorBase>();
                _pointBase = _pointSelector?.TryCast<UIListSelectorBase>();

                _lastState = _fastTravelSelector?.currentState ?? UIWorldMapFastTravelSelector.State.Invalid;
                _lastTabType = (UIWorldMapLocationSelector.TabType)(-1);
                _lastLocationIndex = -1;
                _lastPointIndex = -1;

                // Seed location index to avoid stale announcement if already on an item.
                if (_locationBase != null)
                    _lastLocationIndex = _locationBase.currentIndex;

                DebugLogger.LogState($"WorldMap: selectors cached, state={_lastState}, locIdx={_lastLocationIndex}.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"WorldMap: CacheSelectors error: {ex.Message}");
            }
        }

        private void ClearSelectors()
        {
            _fastTravelSelector = null;
            _locationSelector = null;
            _pointSelector = null;
            _locationBase = null;
            _pointBase = null;
            _lastState = UIWorldMapFastTravelSelector.State.Invalid;
            _lastTabType = (UIWorldMapLocationSelector.TabType)(-1);
            _lastLocationIndex = -1;
            _lastPointIndex = -1;
        }

        #endregion

        #region Polling

        /// <summary>
        /// Polls the location list selector for tab and index changes.
        /// </summary>
        private void UpdateLocationList()
        {
            if (_locationSelector == null || _locationBase == null) return;

            // Check tab type change (City ↔ Dungeon).
            var tab = _locationSelector.currentTabType;
            if (tab != _lastTabType)
            {
                _lastTabType = tab;
                string tabName = tab == UIWorldMapLocationSelector.TabType.Dungeon
                    ? Loc.Get("worldmap_tab_dungeon")
                    : Loc.Get("worldmap_tab_city");
                ScreenReader.Say(tabName);
                DebugLogger.LogState($"WorldMap: tab changed to {tab}.");

                // Reset index so the first item in the new tab announces.
                _lastLocationIndex = -1;
            }

            // Check index change.
            int idx = _locationBase.currentIndex;
            if (idx == _lastLocationIndex) return;
            _lastLocationIndex = idx;

            try
            {
                var list = _locationBase.currentDataList;
                if (list == null || idx < 0 || idx >= list.Count) return;

                var item = list[idx]?.TryCast<UIWorldMapLocationListItemData>();
                if (item == null) return;

                string name = item.locationName ?? "";
                if (!item.canSelected)
                    name = Loc.Get("worldmap_unavailable", name);
                name = AppendRowExtras(name, item.localityID, item.iconList, out string extras);

                ScreenReader.Say(name);
                DebugLogger.LogGameValue("WorldMap:Location",
                    $"idx={idx} name={name} canBeFastTravel={item.canBeFastTravel} {extras}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"WorldMap: location read error: {ex.Message}");
            }
        }

        /// <summary>
        /// Icon sprite name → spoken label. Measured 2026-09-27 (log 18:42):
        /// icon_map_pa_event (Fun City), icon_map_pa_event_limited (Princebridge,
        /// a limited-time private action) and icon_map_scenario_event (the
        /// Centropolis, a main story event). Speaking the drawn sprites keeps
        /// the row exactly as fair as the screen; unknown sprites are only logged.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> RowIconLabels =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "icon_map_pa_event",         "worldmap_pa_available" },
                { "icon_map_pa_event_limited", "worldmap_pa_limited" },
                { "icon_map_scenario_event",   "worldmap_scenario_event" },
            };

        /// <summary>
        /// Adds what the row's icons tell a sighted player (private action,
        /// limited-time private action, story event). The game's own
        /// EventUtility.IsExistPrivateActionEvent verdict is logged next to the
        /// sprite names as a cross-check (they agreed on every row in the 18:42
        /// log), and any sprite without a label is logged for a future mapping.
        /// </summary>
        private static string AppendRowExtras(string name, LocalityID localityID,
            Il2CppSystem.Collections.Generic.List<UnityEngine.Sprite> icons, out string logExtras)
        {
            bool privateAction = false;
            try { privateAction = EventUtility.IsExistPrivateActionEvent(localityID, true, true); }
            catch (Exception ex) { DebugLogger.LogState($"WorldMap: private action lookup failed: {ex.Message}"); }

            var parts = new System.Collections.Generic.List<string> { name };
            var iconNames = new System.Collections.Generic.List<string>();
            try
            {
                if (icons != null)
                {
                    for (int i = 0; i < icons.Count; i++)
                    {
                        string spriteName = icons[i] != null ? icons[i].name : "null";
                        iconNames.Add(spriteName);
                        if (RowIconLabels.TryGetValue(spriteName, out string key))
                            parts.Add(Loc.Get(key));
                        else
                            DebugLogger.LogState($"WorldMap: row icon '{spriteName}' has no spoken label yet.");
                    }
                }
            }
            catch (Exception ex) { iconNames.Add($"unreadable: {ex.Message}"); }

            logExtras = $"locality={localityID} privateAction={privateAction} icons=[{string.Join(", ", iconNames)}]";
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Polls the point list selector for index changes.
        /// The point selector shows two different data types depending on depth:
        ///   - Sub-areas within a location: UIWorldMapLocationListItemData (e.g. "Arlia")
        ///   - Fast travel destinations: UIWorldMapLocationListItemFastTravelData (e.g. "Entrance")
        /// We try both casts to handle both levels.
        /// </summary>
        private void UpdatePointList()
        {
            if (_pointSelector == null || _pointBase == null) return;

            int idx = _pointBase.currentIndex;
            if (idx == _lastPointIndex) return;
            _lastPointIndex = idx;

            try
            {
                var list = _pointBase.currentDataList;
                if (list == null || idx < 0 || idx >= list.Count) return;

                var raw = list[idx];
                if (raw == null) return;

                // Try fast travel destination first (deeper level).
                var ftItem = raw.TryCast<UIWorldMapLocationListItemFastTravelData>();
                if (ftItem != null)
                {
                    string name = ftItem.locationName ?? "";
                    if (!ftItem.canDecisioned)
                        name = Loc.Get("worldmap_unavailable", name);

                    ScreenReader.Say(name);
                    DebugLogger.LogGameValue("WorldMap:Point", $"idx={idx} name={name}");
                    return;
                }

                // Otherwise it's a sub-area (middle level).
                var locItem = raw.TryCast<UIWorldMapLocationListItemData>();
                if (locItem != null)
                {
                    string name = locItem.locationName ?? "";
                    if (!locItem.canSelected)
                        name = Loc.Get("worldmap_unavailable", name);
                    name = AppendRowExtras(name, locItem.localityID, locItem.iconList, out string extras);

                    ScreenReader.Say(name);
                    DebugLogger.LogGameValue("WorldMap:SubArea", $"idx={idx} name={name} {extras}");
                    return;
                }

                DebugLogger.LogState($"WorldMap: point idx={idx} unknown data type.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"WorldMap: point read error: {ex.Message}");
            }
        }

        #endregion
    }
}
