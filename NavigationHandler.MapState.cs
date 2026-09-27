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
                if (fm == null) return;
                if (fm.IsWorldmap())
                {
                    RecordWorldmapTrail(fm);
                    return;
                }

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
        /// Longest a bridge's NavMesh path may be, relative to the straight hop:
        /// a path that wanders is going round something the player would walk
        /// into on the straight line the bridge edge stands for.
        /// </summary>
        private const float BridgeDetourFactor = 1.3f;

        /// <summary>
        /// Decides whether a fresh breadcrumb may link to an older one a few
        /// metres away (see <see cref="TraversalGraph.BridgeVerifier"/>). Both
        /// ends must lie on the NavMesh at their own height, the NavMesh must
        /// connect them with a COMPLETE path hardly longer than the straight hop,
        /// and no wall face may stand across that path (the same probe that
        /// catches NavMesh lies on event-copy maps). Rejections are logged with
        /// their reason.
        /// </summary>
        private bool VerifyBreadcrumbBridge(Vector3 from, Vector3 to)
        {
            string hop = $"({from.x:F1},{from.y:F1},{from.z:F1})->({to.x:F1},{to.y:F1},{to.z:F1})";
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 1.0f, NavMesh.AllAreas) ||
                Mathf.Abs(a.position.y - from.y) > FloorChangeThreshold)
            {
                DebugLogger.LogState($"TRAVERSAL bridge {hop} rejected: start is off the NavMesh.");
                return false;
            }
            if (!NavMesh.SamplePosition(to, out NavMeshHit b, 1.0f, NavMesh.AllAreas) ||
                Mathf.Abs(b.position.y - to.y) > FloorChangeThreshold)
            {
                DebugLogger.LogState($"TRAVERSAL bridge {hop} rejected: end is off the NavMesh.");
                return false;
            }

            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _bridgePath);
            if (_bridgePath.status != NavMeshPathStatus.PathComplete)
            {
                DebugLogger.LogState($"TRAVERSAL bridge {hop} rejected: NavMesh path {_bridgePath.status}.");
                return false;
            }

            Vector3[] corners = CopyCorners(_bridgePath);
            float pathLength = 0f;
            for (int i = 1; i < corners.Length; i++)
                pathLength += Vector3.Distance(corners[i - 1], corners[i]);
            float straight = Vector3.Distance(a.position, b.position);
            if (pathLength > straight * BridgeDetourFactor + 0.5f)
            {
                DebugLogger.LogState(
                    $"TRAVERSAL bridge {hop} rejected: NavMesh path {pathLength:F1} m for a {straight:F1} m hop.");
                return false;
            }

            if (NavMeshPathCrossesWall(corners, out string blocker))
            {
                DebugLogger.LogState($"TRAVERSAL bridge {hop} rejected: crosses {blocker}.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// World map counterpart of the breadcrumb recording: a session-only trail
        /// for the F11 wall audit, kept only in debug mode (nothing persistent is
        /// learned from the world map). Any loss of control breaks the trail.
        /// </summary>
        private void RecordWorldmapTrail(FieldManager fm)
        {
            if (!Main.DebugMode || !IsFieldFree())
            {
                _wmTrail.Break();
                return;
            }
            var player = fm.GetControlPlayer();
            if (player == null)
            {
                _wmTrail.Break();
                return;
            }
            _wmTrail.Record(player.transform.position, WorldmapTravel.CurrentMode());
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
                    }
                    return;
                }

                FieldmapID current = fm.currentFieldmapID;
                if (current == _lastFieldmapID) return;

                FieldmapID previous = _lastFieldmapID;
                _lastFieldmapID = current;

                // The background nav list belongs to the old map — drop it so
                // the next modeless key rebuilds for this one (belt-and-braces
                // with EnsureListReady's own map comparison).
                InvalidateNavList();

                // The guidance destination belonged to the old map, and its
                // route is meaningless here — drop it silently (the map-name
                // announcement below is the feedback the player needs).
                StopGuidance("map change");
                InvalidateFloorGrid();
                _wmTrail.Clear();

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
        /// Resolves a FieldmapID to a human-readable destination name.
        /// Priority: manual overrides → game data (ConstFieldParameter + TextManager) → code suffix.
        /// Results are cached for the session.
        /// </summary>
        private static string ResolveMapName(FieldmapID destId)
        {
            string destCode = destId.ToString();

            // 1. The two world maps have spoken names of their own (the game's
            //    field name data does not cover them).
            if (destId == FieldmapID.EXPEL) return Loc.Get("wm_name_expel");
            if (destId == FieldmapID.NEDE) return Loc.Get("wm_name_nede");

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

        // Localized NPC category label. Callers that need to detect the generic
        // fallback compare against Loc.Get("nav_npc_cat_generic") — never a literal,
        // so the comparison stays consistent in every language.
        private static string GetNpcCategory(NpcType type)
        {
            return Loc.Get(type switch
            {
                NpcType.INN            => "nav_npc_cat_innkeeper",
                NpcType.SHOP_EQUIPMENT => "nav_npc_cat_equipment_shop",
                NpcType.SHOP_ITEM      => "nav_npc_cat_item_shop",
                NpcType.SHOP_FOOD      => "nav_npc_cat_food_shop",
                NpcType.GUILD          => "nav_npc_cat_guild",
                NpcType.FISH_COLLECTOR => "nav_npc_cat_collector",
                NpcType.FACILITY       => "nav_npc_cat_facility",
                NpcType.CHECK          => "nav_npc_cat_switch",
                NpcType.BED            => "nav_npc_cat_bed",
                NpcType.PSYNARD        => "nav_npc_cat_psynard",
                _                      => "nav_npc_cat_generic"
            });
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
