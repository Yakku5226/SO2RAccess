using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler.Build: NPC scanning + name resolution (BuildNpcs, ResolveNpcName, QualifyNpcName, ParseNpcCodeName).
    public partial class NavigationHandler
    {
        #region Private — Build
        /// <summary>
        /// Scans for NPCs in the current field.
        /// Resolves each NPC's display name via ConstNpcParameter position matching
        /// and code name parsing. Falls back to NPC type label if no match.
        /// Generic NPCs (no specific type) are numbered by distance: NPC 1, NPC 2, etc.
        /// Party members (within 2 units of the player) are filtered out.
        /// </summary>
        private void BuildNpcs(Vector3 playerPos, FieldmapID mapID)
        {
            _categories[CAT_NPC].Clear();
            _categories[CAT_EVENT].Clear(); // PA NPCs go here; BuildEvents appends later

            var npcParams = TryGetNpcParams(mapID);
            DebugLogger.LogState(
                $"NAV: npcParams for map {mapID}: " +
                (npcParams == null ? "null" : $"{npcParams.Count} entries"));

            var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
            if (found == null) return;

            var npcItems = new List<NavItem>();
            var interactItems = new List<NavItem>();
            var paItems = new List<NavItem>();
            // Event-carrying NPCs (active "!") routed into the Events category when the
            // EventNpcDisplay setting is EventsList or Both (see BuildNpcs tail).
            var eventNpcItems = new List<NavItem>();
            foreach (var npc in found)
            {
                if (npc == null) continue;

                Vector3 pos  = npc.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                // Skip enemies — they have their own category
                if (npc.TryCast<FieldEnemy>() != null)
                {
                    DebugLogger.LogState(
                        $"NAV:NPCSKIP '{SafeNpcName(npc)}' dist={dist:F1} — FieldEnemy.");
                    continue;
                }

                // Skip player character and party members — not interactable on field.
                // DIAGNOSTIC: split-off party members (e.g. Celine after the party leaves,
                // GameObject "cp_0003_01") may be spawned as FieldPlayer instances and
                // dropped here. Log + flag whether this is the actual control player so we
                // can tell the real player from a placed party character.
                var asPlayer = npc.TryCast<FieldPlayer>();
                if (asPlayer != null)
                {
                    bool isControl = false;
                    try
                    {
                        var cp = FieldManager.Instance?.GetControlPlayer();
                        isControl = cp != null && cp.GetInstanceID() == asPlayer.GetInstanceID();
                    }
                    catch { /* best-effort flag */ }
                    DebugLogger.LogState(
                        $"NAV:NPCSKIP '{SafeNpcName(npc)}' dist={dist:F1} — FieldPlayer (control={isControl}).");
                    continue;
                }
                if (npc.TryCast<FieldFollowCharacter>() != null)
                {
                    // DIAGNOSTIC (debug-only): event-placed party characters (code
                    // name prefix "cp_", e.g. Celine after the party splits) may land
                    // here and be silently dropped. Log the GameObject name + reason so
                    // we can confirm why such a story-trigger NPC is missing from nav.
                    DebugLogger.LogState(
                        $"NAV:NPCSKIP '{SafeNpcName(npc)}' dist={dist:F1} — FieldFollowCharacter.");
                    continue;
                }

                // Skip INVALID-type NPCs — background/decoration NPCs not meant for interaction
                if (npc.NpcType == NpcType.INVALID)
                {
                    DebugLogger.LogState(
                        $"NAV:NPCSKIP '{SafeNpcName(npc)}' dist={dist:F1} — NpcType.INVALID.");
                    continue;
                }

                string label = ResolveNpcName(npc, npcParams, out string codeName);

                // Tag NPCs that CURRENTLY have an active event the player can trigger
                // (the game's red "!" map icon) so they stand out from identically-named
                // background NPCs (e.g. the one story "Soldier" among a dozen). Kept in
                // the NPC category per user preference.
                //
                // Uses the DYNAMIC GetEnable* signals — non-null only while the event's
                // conditions are satisfied — NOT the static "ev_" code name. Confirmed via
                // NAV:NPCEVT log (2026-06-27): the gatekeeper soldier read scenario=True
                // before triggering and scenario=False after, while its "ev_" name and
                // HasEvent() stayed true the whole time (which is why the prefix tag went
                // stale). This also catches triggers with ordinary code names (e.g. a
                // spectator that becomes a story trigger), which the prefix would miss.
                // PA NPCs are handled separately below, so PA is excluded here.
                bool enScenario = NpcEnableScenario(npc);
                bool enSub      = NpcEnableSub(npc);
                bool hasActiveEvent = enScenario || enSub;
                if (hasActiveEvent)
                {
                    label = Loc.Get("nav_npc_event_tag", label);
                    DebugLogger.LogState(
                        $"NAV:NPCEVT tagged '{label}' codeName={codeName} "
                        + $"scenario={enScenario} sub={enSub}");
                }
                bool isCounter = IsFunctionalNpcType(npc.npcType);
                bool isInteractable = IsInteractableNpcType(npc.npcType);

                // Read contactDistance to detect counter NPCs that aren't a
                // functional type (e.g. castle receptionist behind a desk).
                // High contactDistance (>= 1.0) means the game expects interaction
                // from farther away — typical of behind-counter NPCs.
                float contactDist = 0.5f;
                try
                {
                    contactDist = npc.ContactDistance;
                    DebugLogger.LogGameValue("NAV:NPC:CONTACT",
                        $"[{label}] contactDistance={contactDist:F2} codeName={codeName}");
                }
                catch
                {
                    DebugLogger.LogGameValue("NAV:NPC:CONTACT",
                        $"[{label}] contactDistance=READ_ERROR codeName={codeName}");
                }

                if (!isCounter && contactDist >= 1.0f)
                    isCounter = true;

                // Private action NPCs have code names starting with "pa_"
                bool isPrivateAction = codeName != null
                    && codeName.StartsWith("pa_", StringComparison.OrdinalIgnoreCase);

                // For PA NPCs, extract the character name from the code name
                // (e.g. "pa_04_c_001_01_RENA" → "Rena") because the dialogue-
                // derived name may be the wrong character (the first speaker).
                string paLabel = null;
                if (isPrivateAction)
                {
                    string paName = ParseNpcCodeName(codeName);
                    paLabel = string.IsNullOrEmpty(paName)
                        ? Loc.Get("nav_event_pa")
                        : $"{Loc.Get("nav_event_pa")} ({paName})";
                }

                var item = new NavItem
                {
                    Label         = isPrivateAction
                        ? paLabel
                        : label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = npc.transform,
                    IsCounterNpc  = isCounter,
                };

                if (isPrivateAction)
                {
                    DebugLogger.LogGameValue("NAV:EVENT",
                        $"[{item.Label}] (PA NPC) dist={dist:F1} pos={pos}");
                    paItems.Add(item);
                }
                else
                {
                    DebugLogger.LogGameValue(isInteractable ? "NAV:INTERACT" : "NAV:NPC",
                        $"[{label}] type={npc.npcType} dist={dist:F1} pos={pos}");

                    // Event-carrying NPCs (active "!") can be shown in the NPCs category,
                    // the Events category, or both, per the EventNpcDisplay setting — so
                    // story triggers are easy to find in crowded maps. Non-event NPCs are
                    // unaffected and always go to their usual category.
                    var mode = ModSettings.EventNpcDisplay;
                    bool toNpcList = !hasActiveEvent || mode != EventNpcDisplayMode.EventsList;
                    bool toEvents  = hasActiveEvent && mode != EventNpcDisplayMode.NpcList;

                    if (toNpcList)
                    {
                        if (isInteractable)
                            interactItems.Add(item);
                        else
                            npcItems.Add(item);
                    }
                    if (toEvents)
                        eventNpcItems.Add(item); // NavItem is a struct → independent copy
                }
            }

            SortAndFilterUnreachable(npcItems, playerPos);
            SortAndFilterUnreachable(interactItems, playerPos);
            SortAndFilterUnreachable(paItems, playerPos);
            SortAndFilterUnreachable(eventNpcItems, playerPos);

            // Number any NPCs that still carry the generic "NPC" label.
            int npcNum = 1;
            for (int i = 0; i < npcItems.Count; i++)
            {
                var item = npcItems[i];
                if (item.Label == "NPC")
                {
                    item.Label = Loc.Get("nav_npc_n", npcNum++);
                    npcItems[i] = item;
                }
            }

            _categories[CAT_NPC].AddRange(npcItems);
            _categories[CAT_INTERACTABLE].AddRange(interactItems);
            _categories[CAT_EVENT].AddRange(paItems);
            _categories[CAT_EVENT].AddRange(eventNpcItems);
        }

        /// <summary>
        /// Returns the NPC GameObject name for diagnostics, guarded against IL2CPP
        /// access errors. Used to identify skipped story-trigger NPCs (e.g. "cp_0003_01").
        /// </summary>
        private static string SafeNpcName(FieldNpcCharacter npc)
        {
            try { return npc.name; }
            catch { return "?"; }
        }

        /// <summary>
        /// True if the NPC currently has an ENABLED scenario event — its conditions are
        /// satisfied right now (the game's red "!" is showing). Goes false once the event
        /// has fired, unlike the static code name. IL2CPP-guarded.
        /// </summary>
        private static bool NpcEnableScenario(FieldNpcCharacter npc)
        {
            try { return npc.GetEnableScenarioEvent() != null; } catch { return false; }
        }

        /// <summary>True if the NPC currently has an ENABLED sub-event (see
        /// <see cref="NpcEnableScenario"/>). IL2CPP-guarded.</summary>
        private static bool NpcEnableSub(FieldNpcCharacter npc)
        {
            try { return npc.GetEnableSubEvent() != null; } catch { return false; }
        }

        /// <summary>
        /// Resolves a human-readable name for an NPC.
        ///
        /// Steps:
        ///   1. Check DialogueHandler.NpcDisplayNames — if the player has already spoken
        ///      to this NPC the real display name is returned, qualified with the NPC's
        ///      functional role when relevant (e.g. "Equipment shop (Hahn)").
        ///   2. Try matching the NPC's initial position against ConstNpcParameter entries.
        ///   3. If a persistent dialogue name is found, return it qualified as above.
        ///   4. Parse the code name (e.g. NPC_..._GIRL1 → "Girl 1").
        ///   5. Fall back to the NPC type category label (e.g. "Item shop").
        ///   6. Final fallback: return "NPC" so the caller can number it.
        /// </summary>
        private static string ResolveNpcName(
            FieldNpcCharacter npc,
            Il2CppSystem.Collections.Generic.List<ConstNpcParameter> npcParams,
            out string resolvedCodeName)
        {
            resolvedCodeName = null;

            // Always try to resolve the code name via position matching,
            // even if we already know the display name from dialogue.
            if (npcParams != null && npcParams.Count > 0)
            {
                try
                {
                    Vector3 spawn = npc.InitialPosition;
                    for (int i = 0; i < npcParams.Count; i++)
                    {
                        var param = npcParams[i];
                        if (param == null) continue;
                        if (Vector3.Distance(spawn, param.Position) < 2.0f)
                        {
                            resolvedCodeName = param.Name;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV: ResolveNpcName codeName lookup error: {ex.Message}");
                }
            }

            // Resolve the NPC's functional category up front — used to qualify dialogue names.
            // e.g. NpcType.SHOP_EQUIPMENT → "Equipment shop", NpcType.NPC → "NPC"
            string category = GetNpcCategory(npc.npcType);

            // Prefer the real dialogue name if we've already talked to this NPC.
            int instanceID = npc.GetInstanceID();
            if (DialogueHandler.NpcDisplayNames.TryGetValue(instanceID, out string knownName))
            {
                string qualified = QualifyNpcName(knownName, category);
                DebugLogger.LogState(
                    $"NAV: NPC id={instanceID} → '{qualified}' (from dialogue map)");
                return qualified;
            }

            if (!string.IsNullOrEmpty(resolvedCodeName))
            {
                // Prefer a real name learned from dialogue (persists across sessions).
                if (DialogueHandler.PersistentNpcNames.TryGetValue(
                        resolvedCodeName, out string persistedName))
                {
                    string qualified = QualifyNpcName(persistedName, category);
                    DebugLogger.LogState(
                        $"NAV: NPC '{resolvedCodeName}' → '{qualified}' (persistent)");
                    return qualified;
                }

                string readable = ParseNpcCodeName(resolvedCodeName);
                if (!string.IsNullOrEmpty(readable))
                {
                    DebugLogger.LogState(
                        $"NAV: NPC '{resolvedCodeName}' → '{readable}'");
                    return readable;
                }
            }

            // Fall back to the NPC type category (e.g. "Item shop", "Innkeeper").
            return category;
        }

        /// <summary>
        /// Combines a dialogue display name with a functional NPC category.
        /// Returns "[category] ([displayName])" for functional NPCs (shop, inn, guild, etc.)
        /// and just "[displayName]" for plain NPCs with no specific role.
        /// This ensures "Hahn" becomes "Equipment shop (Hahn)" while "Elderly person"
        /// stays as "Elderly person".
        /// </summary>
        private static string QualifyNpcName(string displayName, string category)
        {
            if (category == "NPC") return displayName;
            return $"{category} ({displayName})";
        }

        /// <summary>
        /// Parses an NPC internal code name into a human-readable label.
        ///
        /// Format: NPC_{mapArea}_{mapSub}_{orderNum}_{DESCRIPTOR}{num}
        /// e.g. NPC_0003_01a_18_GIRL1 → "Girl 1"
        ///      NPC_0003_01a_17_GRANDFATHER2 → "Grandfather 2"
        ///      NPC_0003_01a_26_WEAPONSHOP1  → "Weaponshop 1"
        ///
        /// The last underscore segment is extracted, trailing digits are split from
        /// the descriptor text, and the text is title-cased.
        /// Returns null if the code name cannot be parsed.
        /// </summary>
        private static string ParseNpcCodeName(string codeName)
        {
            if (string.IsNullOrEmpty(codeName)) return null;

            // Take the last segment after the final underscore.
            int lastUnder = codeName.LastIndexOf('_');
            if (lastUnder < 0 || lastUnder >= codeName.Length - 1) return null;

            string suffix = codeName.Substring(lastUnder + 1); // e.g. "GIRL1"

            // Split trailing digits from the descriptor text.
            int numStart = suffix.Length;
            while (numStart > 0 && char.IsDigit(suffix[numStart - 1]))
                numStart--;

            string text = suffix.Substring(0, numStart);       // e.g. "GIRL"
            string num  = suffix.Substring(numStart);           // e.g. "1"

            if (string.IsNullOrEmpty(text)) return null;

            // Title-case: first letter uppercase, rest lowercase.
            string readable = char.ToUpper(text[0]) + text.Substring(1).ToLower();

            return string.IsNullOrEmpty(num) ? readable : $"{readable} {num}";
        }
        #endregion
    }
}
