using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler: map/area state tracking —
    // observed-traversal recording, fieldmap-change and floor-change detection,
    // map-name resolution, and NPC-type classification helpers.
    public partial class NavigationHandler
    {
        #region Map State, Floor & Traversal
        /// <summary>Flushes recorded breadcrumbs to disk (call on quit).</summary>
        public void SaveTraversal()
        {
            try { _traversal.Save(); } catch { }
        }

        /// <summary>
        /// Debug diagnostic (F11): logs how many breadcrumbs the traversal graph
        /// holds for this map and, for each treasure chest, whether it is
        /// reachable via a complete NavMesh path or via recorded traversals.
        /// </summary>
        public void LogTraversalDiagnostic(Vector3 playerPos)
        {
            try
            {
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] TRAVERSAL DIAG: {_traversal.NodeCount} breadcrumbs, " +
                    $"player snap node {_traversal.SnapToNode(playerPos)}.");

                // One-way drops detected (jump-down ledges): should match the known
                // ledges on the map. Their uphill direction is blocked.
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] TRAVERSAL DIAG: {_traversal.DropSummary()}");

                // Save points (the reported failure target). Report reachability so
                // we can tell "routes the long way" from "genuinely cannot climb up".
                var fm = FieldManager.Instance;
                var saves = fm?.FieldSavePointList;
                if (saves != null)
                {
                    for (int i = 0; i < saves.Count; i++)
                    {
                        var sp = saves[i];
                        if (sp == null) continue;
                        Vector3 p = sp.transform.position;
                        bool nav = HasCompleteNavMeshPath(playerPos, p);
                        bool trav = _traversal.HasData && _traversal.IsReachable(playerPos, p);
                        MelonLoader.MelonLogger.Msg(
                            $"[SO2RAccess] TRAVERSAL DIAG: save {i} " +
                            $"({p.x:F1},{p.y:F1},{p.z:F1}) navMeshComplete={nav} traversal={trav}");
                    }
                }

                var chests = UnityEngine.Object.FindObjectsOfType<FieldTreasureBox>();
                if (chests == null) return;
                int n = 0;
                foreach (var c in chests)
                {
                    if (c == null) continue;
                    Vector3 p = c.transform.position;
                    bool nav = HasCompleteNavMeshPath(playerPos, p);
                    bool trav = _traversal.HasData && _traversal.IsReachable(playerPos, p);
                    string label = c.IsAcquired ? $"opened {n}" : $"UNOPENED {n}";
                    MelonLoader.MelonLogger.Msg(
                        $"[SO2RAccess] TRAVERSAL DIAG: chest {label} " +
                        $"({p.x:F1},{p.y:F1},{p.z:F1}) navMeshComplete={nav} traversal={trav}");
                    n++;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] TRAVERSAL DIAG error: {ex.Message}");
            }
        }

        /// <summary>
        /// Records the player's position as a breadcrumb while walking a field
        /// map (manual or auto), building the observed-traversal graph. Autosaves
        /// periodically so a long manual exploration isn't lost.
        /// </summary>
        private void CheckTraversalRecording()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || fm.IsWorldmap()) return;

                // Only record when the player is actually in control (not in
                // camp/battle/dialogue) so cutscene motion doesn't pollute the map.
                if (!IsFieldFree()) { _traversal.BreakTrail(); return; }

                var player = fm.GetControlPlayer();
                if (player == null) { _traversal.BreakTrail(); return; }

                _traversal.RecordPosition(player.transform.position);

                _traversalSaveTimer += Time.deltaTime;
                if (_traversalSaveTimer >= 10f)
                {
                    _traversalSaveTimer = 0f;
                    _traversal.Save();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"TRAVERSAL record error: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the current fieldmap has changed and announces the new map name.
        /// Called every frame from Update(). Skips the first detection to avoid
        /// announcing on game load.
        /// </summary>
        private void CheckFieldmapChange()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null)
                {
                    // Not on a field — reset so next field entry announces.
                    if (_fieldmapInitialized)
                    {
                        _fieldmapInitialized = false;
                        _lastFieldmapID = FieldmapID.INVALID;
                        _lastPlayerY = float.NaN;
                    }
                    return;
                }

                FieldmapID current = fm.currentFieldmapID;
                if (current == _lastFieldmapID) return;

                // New map — reset floor tracking so we don't announce a floor change
                // from the old map's Y position to the new map's Y position.
                _lastPlayerY = float.NaN;

                FieldmapID previous = _lastFieldmapID;
                _lastFieldmapID = current;

                // Skip INVALID transitions.
                if (current == FieldmapID.INVALID)
                {
                    if (!_fieldmapInitialized)
                        _fieldmapInitialized = true;
                    return;
                }

                // Skip map name announcement on the very first detection
                // (game load / initial scene), but still load breadcrumbs below.
                if (!_fieldmapInitialized)
                {
                    _fieldmapInitialized = true;
                }
                else
                {
                    string name = ResolveMapName(current);
                    if (!string.IsNullOrEmpty(name))
                    {
                        ScreenReader.Say(name);
                        DebugLogger.LogState($"MapChange: {previous} → {current} = '{name}'");
                    }
                }

                // Load the new map's recorded breadcrumbs (field maps only).
                // On the world map there's nothing to record — just flush.
                if (!fm.IsWorldmap())
                {
                    // Load this map's recorded breadcrumbs (saves the previous map).
                    try { _traversal.StartMap(current.ToString()); }
                    catch (Exception ex) { DebugLogger.LogState($"TRAVERSAL StartMap error: {ex.Message}"); }
                }
                else
                {
                    try { _traversal.Save(); } catch { }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CheckFieldmapChange error: {ex.Message}");
            }
        }

        /// <summary>
        /// Monitors the player's Y position each frame. When it changes by more than
        /// <see cref="FloorChangeThreshold"/>, announces "Went upstairs" or "Went downstairs".
        /// Uses a cooldown to avoid rapid-fire announcements on long staircases.
        /// Resets on fieldmap change (called from CheckFieldmapChange).
        /// </summary>
        private void CheckFloorChange()
        {
            // Use FieldManager.IsWorldmap() directly instead of _isWorldmap field,
            // because CancelAutoWalk clears _isWorldmap — causing stale floor change
            // announcements on the world map when auto-walk ends.
            try
            {
                if (FieldManager.Instance?.IsWorldmap() == true) return;
            }
            catch { /* FieldManager unavailable — proceed with floor check */ }

            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return;

                var player = fm.GetControlPlayer();
                if (player == null) return;

                float currentY = player.transform.position.y;

                // First reading — seed without announcing.
                if (float.IsNaN(_lastPlayerY))
                {
                    _lastPlayerY = currentY;
                    return;
                }

                // Tick down cooldown.
                if (_floorChangeCooldownTimer > 0f)
                {
                    _floorChangeCooldownTimer -= Time.deltaTime;
                    // Keep tracking Y during cooldown so the baseline stays current.
                    _lastPlayerY = currentY;
                    return;
                }

                float deltaY = currentY - _lastPlayerY;
                if (Mathf.Abs(deltaY) >= FloorChangeThreshold)
                {
                    string key = deltaY > 0 ? "nav_floor_up" : "nav_floor_down";
                    ScreenReader.Say(Loc.Get(key));
                    DebugLogger.LogState(
                        $"NAV floor change: Y {_lastPlayerY:F1} → {currentY:F1} " +
                        $"(delta={deltaY:F1})");
                    _lastPlayerY = currentY;
                    _floorChangeCooldownTimer = FloorChangeCooldown;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CheckFloorChange error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves a FieldmapID to a human-readable destination name.
        /// Priority: manual overrides → game data (ConstFieldParameter + TextManager) → code suffix.
        /// Results are cached for the session.
        /// </summary>
        private static string ResolveMapName(FieldmapID destId)
        {
            string destCode = destId.ToString();

            // 1. Manual overrides (EXPEL → "Overworld", etc.)
            if (_mapNameOverrides.TryGetValue(destCode, out string overrideName))
                return overrideName;

            // 2. Check cache
            if (_mapNameCache.TryGetValue(destCode, out string cached))
                return cached;

            // 3. Try game data: ConstFieldParameter.FieldmapNameID → TextManager
            string resolved = null;
            try
            {
                var paramMgr = ParameterManager.Instance;
                if (paramMgr != null)
                {
                    var fieldParam = paramMgr.GetFieldParameter(destId);
                    if (fieldParam != null)
                    {
                        string nameKey = fieldParam.FieldmapNameID;
                        DebugLogger.LogGameValue("NAV:MAP_KEY",
                            $"{destCode} → FieldmapNameID='{nameKey}'");

                        if (!string.IsNullOrEmpty(nameKey))
                        {
                            // Try resolving through the game's text system
                            var textMgr = TextManager.Instance;
                            if (textMgr != null)
                            {
                                string text = textMgr.GetMessage(
                                    nameKey, TextManager.MessageType.System);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    resolved = text;
                                    DebugLogger.LogGameValue("NAV:MAP_NAME",
                                        $"{destCode} → '{resolved}' (via TextManager)");
                                }
                            }

                            // If TextManager didn't resolve, use the raw key
                            // (it might already be a readable name)
                            if (resolved == null)
                            {
                                resolved = nameKey;
                                DebugLogger.LogGameValue("NAV:MAP_NAME",
                                    $"{destCode} → '{resolved}' (raw key)");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:MAP_NAME error for {destCode}: {ex.Message}");
            }

            // 4. Fallback: last underscore segment (e.g. "MF_0003_22A" → "22A")
            if (resolved == null)
            {
                int last = destCode.LastIndexOf('_');
                resolved = (last >= 0 && last < destCode.Length - 1)
                    ? destCode.Substring(last + 1)
                    : destCode;
                DebugLogger.LogGameValue("NAV:MAP_NAME",
                    $"{destCode} → '{resolved}' (fallback suffix)");
            }

            _mapNameCache[destCode] = resolved;
            return resolved;
        }

        /// <summary>
        /// Returns true for NPC types that represent interactable objects
        /// (switches, beds, inspection points) rather than characters.
        /// These go into the Interactables nav category instead of NPCs.
        /// </summary>
        private static bool IsInteractableNpcType(NpcType type)
        {
            return type switch
            {
                NpcType.CHECK => true,
                NpcType.BED   => true,
                _             => false
            };
        }

        /// <summary>
        /// Returns true for NPC types that are commonly placed behind counters
        /// or barriers (shops, inns, guilds). These NPCs should not be filtered
        /// by NavMesh reachability because the game allows interaction over
        /// the counter even though no walkable path exists.
        /// </summary>
        private static bool IsFunctionalNpcType(NpcType type)
        {
            return type switch
            {
                NpcType.INN            => true,
                NpcType.SHOP_EQUIPMENT => true,
                NpcType.SHOP_ITEM      => true,
                NpcType.SHOP_FOOD      => true,
                NpcType.GUILD          => true,
                NpcType.FISH_COLLECTOR => true,
                NpcType.FACILITY       => true,
                _                      => false
            };
        }

        private static string GetNpcCategory(NpcType type)
        {
            return type switch
            {
                NpcType.INN            => "Innkeeper",
                NpcType.SHOP_EQUIPMENT => "Equipment shop",
                NpcType.SHOP_ITEM      => "Item shop",
                NpcType.SHOP_FOOD      => "Food shop",
                NpcType.GUILD          => "Guild",
                NpcType.FISH_COLLECTOR => "Collector",
                NpcType.FACILITY       => "Facility",
                NpcType.CHECK          => "Switch",
                NpcType.BED            => "Bed",
                NpcType.PSYNARD        => "Psynard",
                _                      => "NPC"
            };
        }

        private static Il2CppSystem.Collections.Generic.List<ConstNpcParameter> TryGetNpcParams(
            FieldmapID mapID)
        {
            try
            {
                return ParameterManager.Instance?.GetNpcParameter(mapID);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV: TryGetNpcParams failed for map {mapID}: {ex.Message}");
                return null;
            }
        }
        #endregion
    }
}
