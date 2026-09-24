using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Field gimmicks (one scan for every kind)

        /// <summary>One live gimmick the registry recognised, with its position read once.</summary>
        private struct GimmickHit
        {
            public InteractableKind Kind;
            public FieldGimmickBase Obj;
            public Vector3          Pos;
            public string           TypeName;
            /// <summary>Stable-number identity ("switch16:7"); null = position.</summary>
            public string           Identity;
            /// <summary>Loc key wrapped around the numbered label ("{0}, pressed"); null = none.</summary>
            public string           NoteKey;
            /// <summary>Identity of the boulder a switch feeds (numbered in the boulders' group); null = none.</summary>
            public string           GroupIdentity;
        }

        /// <summary>
        /// Reads the game's gimmick list ONCE per list build and classifies every
        /// object through <see cref="InteractableRegistry"/>. Every object is
        /// logged (class, kind, startup type, active, position) so a map whose
        /// gimmicks are missing from the list explains itself in the log.
        /// Objects the game switched off, disabled magic circles and walk-in
        /// triggers are dropped here; the climb points (FieldGimmick03) are
        /// returned too and consumed by <see cref="BuildClimbPoints"/>.
        /// </summary>
        private List<GimmickHit> CollectGimmickHits(FieldManager fm)
        {
            var hits = new List<GimmickHit>();
            Il2CppSystem.Collections.Generic.List<FieldGimmickBase> list = null;
            try { list = fm.FieldGimmickManager?.FieldGimmickList; }
            catch (Exception ex) { DebugLogger.LogState($"NAV:GIMMICK list read failed: {ex.Message}"); }
            if (list == null)
            {
                DebugLogger.LogState("NAV:GIMMICK gimmick list is null.");
                return hits;
            }

            DebugLogger.LogState($"NAV:GIMMICK list has {list.Count} entries.");
            var puzzles = SwitchPuzzleModel.Read(list);
            for (int i = 0; i < list.Count; i++)
            {
                var gimmick = list[i];
                if (gimmick == null) continue;

                try
                {
                    var kind = InteractableRegistry.Classify(gimmick, out string typeName);
                    Vector3 pos = gimmick.transform.position;
                    bool listable = InteractableRegistry.IsListable(kind, gimmick);
                    string puzzleNote = "";
                    string identity   = null;
                    string noteKey    = null;
                    string group      = null;
                    if (listable)
                        listable = ApplyPuzzleState(puzzles, kind, gimmick,
                            out identity, out noteKey, out group, out puzzleNote);

                    DebugLogger.LogGameValue("NAV:GIMMICK",
                        $"[{i}] {typeName} kind={kind} startup={InteractableRegistry.StartupText(gimmick)} " +
                        $"listed={listable} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}){DescribeGimmick(kind, gimmick)}{puzzleNote}");

                    if (!listable) continue;
                    hits.Add(new GimmickHit
                    {
                        Kind = kind, Obj = gimmick, Pos = pos, TypeName = typeName,
                        Identity = identity, NoteKey = noteKey, GroupIdentity = group,
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV:GIMMICK [{i}] read failed: {ex.Message}");
                }
            }
            return hits;
        }

        /// <summary>
        /// Switch-and-boulder puzzle state (see <see cref="SwitchPuzzleModel"/>):
        /// a broken boulder is not listed; a dead switch (every boulder it feeds
        /// is broken) is not listed; a live switch names the boulder it feeds
        /// ("Switch 3, boulder 2": the fuse from the switch to the boulder's
        /// magic circle is drawn on screen, so the link is fair to speak) and a
        /// pressed one gets the "pressed" note. Other kinds pass through
        /// untouched. Returns false to drop the object; <paramref name="note"/>
        /// is the log evidence either way.
        /// </summary>
        private static bool ApplyPuzzleState(SwitchPuzzleModel puzzles, InteractableKind kind, FieldGimmickBase gimmick,
            out string identity, out string noteKey, out string groupIdentity, out string note)
        {
            identity      = null;
            noteKey       = null;
            groupIdentity = null;
            note          = "";
            if (kind == InteractableKind.Rock)
            {
                var rock = gimmick.TryCast<FieldGimmick16>();
                if (rock == null) return true;
                bool broken = SwitchPuzzleModel.IsBoulderBroken(rock, out string detail);
                note = $" puzzle: {detail}" + (broken ? " -> broken, dropped" : "");
                try { identity = SwitchPuzzleModel.IdentityFor(rock.MainSwitchID); }
                catch (Exception ex) { DebugLogger.LogState($"NAV:PUZZLE boulder main switch read failed: {ex.Message}"); }
                return !broken;
            }
            if (kind == InteractableKind.Switch)
            {
                var sw = gimmick.TryCast<FieldGimmick16Switch>();
                if (sw == null) return true;   // FieldGimmick11Switch: no boulder link
                puzzles.SwitchState(sw, out bool dead, out bool pressed, out int switchID,
                    out groupIdentity, out string detail);
                note = $" puzzle: {detail}" + (dead ? " -> dead, dropped" : pressed ? " -> pressed" : "");
                if (switchID >= 0) identity = "switch16:" + switchID;
                if (pressed && !dead) noteKey = "nav_switch_pressed";
                return !dead;
            }
            return true;
        }

        /// <summary>
        /// Appends every recognised gimmick except climb points to its category:
        /// gathering points, switches, statues, floor panels and unknown
        /// button-press mechanisms to Interactables; ledges to Stairs; warp panels
        /// and magic circles to Warp Points; puzzle doors and boulders to Doors.
        /// Labels come from the registry (a ledge uses the game's own prompt text,
        /// "Jump", else "Ledge"); numbers are stable per map and per kind (see
        /// <see cref="GetStableNumber"/>), handed out only when a kind occurs more
        /// than once, except gathering points which are always numbered.
        /// Interactables and ledges stay listed when no path exists ("…, no
        /// path"): a dungeon floor the NavMesh does not connect is walked by hand
        /// and the beacon needs the target to exist for that. Must run AFTER the
        /// builders that clear Stairs and Doors.
        /// </summary>
        private void BuildGimmicks(List<GimmickHit> hits, Vector3 playerPos)
        {
            if (hits.Count == 0) return;

            // Boulders are numbered first, nearest = 1, so a switch's "boulder N"
            // note can quote the number before the Doors category is built.
            var boulders = hits.FindAll(h => h.Kind == InteractableKind.Rock && h.Identity != null);
            boulders.Sort((a, b) => Vector3.Distance(playerPos, a.Pos).CompareTo(Vector3.Distance(playerPos, b.Pos)));
            var rockGroup = InteractableRegistry.Info(InteractableKind.Rock).NumberGroup;
            foreach (var boulder in boulders)
                GetStableNumber(rockGroup, new NavItem { Identity = boulder.Identity, Position = boulder.Pos });

            var byPlacement = new Dictionary<NavPlacement, List<NavItem>>();
            var countByKind = new Dictionary<InteractableKind, int>();
            foreach (var hit in hits)
            {
                if (hit.Kind == InteractableKind.Climb) continue;   // BuildClimbPoints owns these
                var info = InteractableRegistry.Info(hit.Kind);
                if (info == null || info.Placement == NavPlacement.None) continue;

                if (!byPlacement.TryGetValue(info.Placement, out var items))
                {
                    items = new List<NavItem>();
                    byPlacement[info.Placement] = items;
                }
                items.Add(new NavItem
                {
                    Kind          = hit.Kind,
                    Label         = PlainLabel(hit, info),
                    Distance      = Vector3.Distance(playerPos, hit.Pos),
                    Position      = hit.Pos,
                    LiveTransform = null,
                    Identity      = hit.Identity,
                    NoteKey       = hit.NoteKey,
                    GroupIdentity = hit.GroupIdentity,
                });
                countByKind[hit.Kind] = countByKind.TryGetValue(hit.Kind, out int n) ? n + 1 : 1;
            }

            foreach (var pair in byPlacement)
            {
                var items = pair.Value;

                // Distance order first, so the nearest thing of a kind is number 1
                // the first time this map is seen.
                items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var info = InteractableRegistry.Info(item.Kind);
                    if (info.AlwaysNumbered || countByKind[item.Kind] > 1)
                        item.Label = NumberedLabel(item, info, GetStableNumber(info.NumberGroup, item));
                    // Group and state notes after the number, before any "no path"
                    // mark: "Switch 3, boulder 2, pressed, no path".
                    if (item.GroupIdentity != null)
                        item.Label = Loc.Get("nav_switch_boulder", item.Label,
                            GetStableNumber(rockGroup, new NavItem { Identity = item.GroupIdentity, Position = item.Position }));
                    if (item.NoteKey != null)
                        item.Label = Loc.Get(item.NoteKey, item.Label);
                    items[i] = item;
                }

                bool keepUnreachable = pair.Key == NavPlacement.Interactables || pair.Key == NavPlacement.Stairs;
                SortAndFilterUnreachable(items, playerPos, keepUnreachable);
                _categories[CategoryOf(pair.Key)].AddRange(items);

                foreach (var item in items)
                    DebugLogger.LogGameValue("NAV:GIMMICK",
                        $"listed [{item.Label}] kind={item.Kind} dist={item.Distance:F1}");
            }
        }

        /// <summary>The unnumbered label: the game's prompt text for a ledge, else the kind's Loc text.</summary>
        private static string PlainLabel(GimmickHit hit, InteractableInfo info)
        {
            if (hit.Kind == InteractableKind.Ledge)
            {
                string text = null;
                try { text = TextUtil.ResolveSystemText(hit.Obj.GetOperationMessageID()); }
                catch (Exception ex) { DebugLogger.LogState($"NAV:GIMMICK ledge prompt text failed: {ex.Message}"); }
                return string.IsNullOrEmpty(text) ? Loc.Get("nav_ledge") : text;
            }
            // A kind without a plain key (gathering points) is always numbered, so
            // the plain label is never spoken; never ask Loc for a null key.
            return info.LabelKey == null ? "" : Loc.Get(info.LabelKey);
        }

        /// <summary>The numbered label: "Switch 2", "Gathering point 1", or "Jump 3" for a game-labelled ledge.</summary>
        private static string NumberedLabel(NavItem item, InteractableInfo info, int number) =>
            info.LabelKey == null && item.Kind == InteractableKind.Ledge
                ? Loc.Get(info.NumberedLabelKey, item.Label, number)
                : Loc.Get(info.NumberedLabelKey, number);

        /// <summary>Maps the registry's placement to the list's category index.</summary>
        private static int CategoryOf(NavPlacement placement)
        {
            switch (placement)
            {
                case NavPlacement.Interactables: return CAT_INTERACTABLE;
                case NavPlacement.Stairs:        return CAT_STAIRS;
                case NavPlacement.Warp:          return CAT_WARP;
                case NavPlacement.Doors:         return CAT_DOOR;
                default:
                    throw new ArgumentOutOfRangeException(nameof(placement), placement, "no category");
            }
        }

        /// <summary>
        /// Debug-log extras per kind. Item IDs, event functions and prompt keys
        /// are evidence for the log only and never reach the label (fairness:
        /// a sighted player sees a sparkle, not what it holds).
        /// </summary>
        private static string DescribeGimmick(InteractableKind kind, FieldGimmickBase gimmick)
        {
            try
            {
                switch (kind)
                {
                    case InteractableKind.Gather:
                    {
                        var pickup = gimmick.TryCast<FieldGimmick05>();
                        if (pickup == null) return "";
                        return $" item={pickup.ItemID} event='{pickup.EventFunction}' " +
                               $"progress={pickup.ScenarioProgressStart}..{pickup.ScenarioProgressEnd} " +
                               $"disableFlags={FormatFlags(pickup.DisableFlagList)}";
                    }
                    case InteractableKind.Ledge:
                    {
                        var contact = gimmick.TryCast<FieldGimmick01>();
                        if (contact == null) return "";
                        return $" msg='{contact.GetOperationMessageID()}' icon={contact.GetMapIconType()} " +
                               $"showIcon={contact.IsShowMapIcon}";
                    }
                    default:
                        return "";
                }
            }
            catch (Exception ex)
            {
                return $" (details failed: {ex.Message})";
            }
        }

        #endregion
    }
}
