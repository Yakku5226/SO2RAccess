using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler.Build: World-map location scanning (BuildWorldmapLocations + mapjump diagnostic).
    public partial class NavigationHandler
    {
        #region Private — Build
        /// <summary>
        /// Builds the Locations category for world map navigation.
        /// Uses the game's ConstWorldmapSymbolParameter database, filtered by
        /// current scenario progress. Resolves display names from locality data.
        /// Matches runtime WorldmapSymbol objects for LiveTransform tracking.
        /// </summary>
        private void BuildWorldmapLocations(Vector3 playerPos, WorldmapID wmID)
        {
            _categories[CAT_LOCATION].Clear();

            if (wmID == WorldmapID.INVALID) return;

            var pm = ParameterManager.Instance;
            if (pm == null) return;

            Il2CppSystem.Collections.Generic.List<ConstWorldmapSymbolParameter> symbols = null;
            try { symbols = pm.GetWorldmapSymbolParameter(wmID); }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: GetWorldmapSymbolParameter error: {ex.Message}");
                return;
            }

            if (symbols == null || symbols.Count == 0) return;

            int progress = 0;
            try { progress = pm.UserParameter.MainScenarioProgress; }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: MainScenarioProgress error: {ex.Message}");
            }

            var tm = TextManager.Instance;

            // Collect runtime WorldmapSymbol objects for LiveTransform matching.
            WorldmapSymbol[] runtimeSymbols = null;
            try { runtimeSymbols = UnityEngine.Object.FindObjectsOfType<WorldmapSymbol>(); }
            catch { }

            var items = new List<NavItem>();

            for (int i = 0; i < symbols.Count; i++)
            {
                try
                {
                    var sym = symbols[i];
                    if (sym == null) continue;

                    var iconType = sym.mapIconType;
                    // Only show cities and dungeons as navigable locations.
                    if (iconType != MapIconType.CITY && iconType != MapIconType.DUNGEON)
                        continue;

                    // Filter by scenario progress.
                    int start = sym.StartScenarioProgress;
                    int end   = sym.EndScenarioProgress;
                    if (progress < start || (end > 0 && progress > end))
                        continue;

                    // Resolve display name: localityID → locality parameter → name.
                    string name = null;
                    var localityID = sym.localityID;
                    try
                    {
                        var localityParam = pm.GetLocalityParameter(localityID);
                        if (localityParam != null)
                        {
                            string nameKey = localityParam.localityNameID;
                            if (!string.IsNullOrEmpty(nameKey) && tm != null)
                                name = tm.GetMessage(nameKey, TextManager.MessageType.System);
                        }
                    }
                    catch { }

                    // Fallback: use symbolName if locality resolution failed.
                    if (string.IsNullOrEmpty(name))
                    {
                        name = sym.SymbolName;
                        if (string.IsNullOrEmpty(name))
                            name = $"Location {i}";
                    }

                    // Label: plain name for cities, "(Dungeon)" suffix for dungeons.
                    string label = iconType == MapIconType.DUNGEON
                        ? Loc.Get("nav_location_dungeon", name)
                        : name;

                    // Position from game data (static, not subject to wrapping).
                    Vector3 pos = sym.Position;
                    float dist = Vector3.Distance(playerPos, pos);

                    // Find matching runtime WorldmapSymbol for LiveTransform.
                    Transform liveTransform = null;
                    if (runtimeSymbols != null)
                    {
                        try
                        {
                            foreach (var rs in runtimeSymbols)
                            {
                                if (rs != null && rs.LocalityID == localityID)
                                {
                                    liveTransform = rs.transform;
                                    pos = liveTransform.position;
                                    dist = Vector3.Distance(playerPos, pos);
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    items.Add(new NavItem
                    {
                        Label         = label,
                        Distance      = dist,
                        Position      = pos,
                        LiveTransform = liveTransform,
                    });

                    DebugLogger.LogGameValue("NAV:LOCATION",
                        $"[{label}] dist={dist:F0} icon={iconType} " +
                        $"progress=[{start},{end}] locality={localityID}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV: BuildWorldmapLocations item {i}: {ex.Message}");
                }
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            _categories[CAT_LOCATION].AddRange(items);
        }
        #endregion
    }
}
