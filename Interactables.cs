using Il2CppGame;
using System;

namespace SO2RAccess
{
    /// <summary>
    /// What kind of thing an interactable object is, independent of the game
    /// class that implements it. Shared by the navigation list (which category
    /// and label a thing gets), the interaction prompt handler (which bubble
    /// just appeared) and the beacon system (which cue plays). Adding a kind is
    /// one enum value plus one row in <see cref="InteractableRegistry"/>.
    /// </summary>
    internal enum InteractableKind
    {
        /// <summary>Not an interactable the mod lists (walk-in triggers, traps, plain objects).</summary>
        None = 0,
        /// <summary>A fishing spot (FieldFishingWaterPlace or a baked world map stand). Not a gimmick class.</summary>
        Fishing,
        /// <summary>FieldGimmick05: a sparkle on the ground that gives an item when examined.</summary>
        Gather,
        /// <summary>FieldGimmick01: a ledge the character jumps down or climbs ("Jump" prompt).</summary>
        Ledge,
        /// <summary>FieldGimmick03: a ladder, ivy wall or cliff scramble with two end points.</summary>
        Climb,
        /// <summary>FieldGimmick09: a warp panel.</summary>
        WarpPanel,
        /// <summary>FieldGimmick17: a magic circle that warps.</summary>
        MagicCircle,
        /// <summary>FieldGimmick11Switch / FieldGimmick16Switch: a switch or fuse to operate.</summary>
        Switch,
        /// <summary>FieldGimmick12StoneStatue: a statue to turn.</summary>
        Statue,
        /// <summary>FieldGimmick07Door / 11Door / 14Door: a puzzle door.</summary>
        Door,
        /// <summary>FieldGimmick16: a boulder that switches or a magic circle break.</summary>
        Rock,
        /// <summary>FieldGimmick13 / FieldGimmick14Panel: a floor panel that reacts when stepped on.</summary>
        FloorPanel,
        /// <summary>Any other gimmick that waits for a button press. The generic, fair label.</summary>
        Mechanism
    }

    /// <summary>
    /// Which navigation category a kind is listed in. Kept apart from the
    /// category indices inside <see cref="NavigationHandler"/> so the registry
    /// stays independent of the list's internals.
    /// </summary>
    internal enum NavPlacement
    {
        None = 0,
        Interactables,
        Stairs,
        Warp,
        Doors
    }

    /// <summary>One kind's static facts: where it is listed, how it is named and numbered, which beacon it gets.</summary>
    internal sealed class InteractableInfo
    {
        /// <summary>Navigation category.</summary>
        public NavPlacement Placement { get; }
        /// <summary>Loc key of the plain label ("Switch"); null when the label comes from the game (ledges).</summary>
        public string LabelKey { get; }
        /// <summary>Loc key of the numbered label ("Switch {0}"); for a game-labelled kind the generic "{0} {1}".</summary>
        public string NumberedLabelKey { get; }
        /// <summary>Stable-number group, one sequence per kind (see NavigationHandler.GetStableNumber).</summary>
        public string NumberGroup { get; }
        /// <summary>Beacon cue for this kind, null for silence.</summary>
        public NavCueKind? Beacon { get; }
        /// <summary>True when even a single one is numbered ("Gathering point 1").</summary>
        public bool AlwaysNumbered { get; }

        public InteractableInfo(NavPlacement placement, string labelKey, string numberedLabelKey,
            string numberGroup, NavCueKind? beacon = null, bool alwaysNumbered = false)
        {
            Placement        = placement;
            LabelKey         = labelKey;
            NumberedLabelKey = numberedLabelKey;
            NumberGroup      = numberGroup;
            Beacon           = beacon;
            AlwaysNumbered   = alwaysNumbered;
        }
    }

    /// <summary>
    /// The single table that says what each of the game's field gimmick classes
    /// is. Classification goes by the IL2CPP type name (one call per object;
    /// a TryCast chain would cost one native call per class per object), and
    /// only the kinds that need members are cast. A class the table does not
    /// know is still handled: if it waits for a button press it becomes a
    /// "Mechanism", so a future or overlooked gimmick shows up in the list and
    /// the log instead of staying invisible (the gathering points of the
    /// Sanctuary of Linga were invisible for weeks, 2026-09-23).
    ///
    /// Fairness rule: labels never reveal what a sighted player cannot see.
    /// Item IDs, event functions and asset names go to the debug log only.
    /// </summary>
    internal static class InteractableRegistry
    {
        /// <summary>Kind → facts. Placement and numbering mirror what the separate builders did before 2026-09-23.</summary>
        private static readonly System.Collections.Generic.Dictionary<InteractableKind, InteractableInfo> _info =
            new System.Collections.Generic.Dictionary<InteractableKind, InteractableInfo>
            {
                { InteractableKind.Fishing,     new InteractableInfo(NavPlacement.Interactables, "nav_fishing",        "nav_fishing_n",        "fishing", NavCueKind.Fishing) },
                { InteractableKind.Gather,      new InteractableInfo(NavPlacement.Interactables, null,                 "nav_gather_n",         "gather",  NavCueKind.Gather, alwaysNumbered: true) },
                { InteractableKind.Ledge,       new InteractableInfo(NavPlacement.Stairs,        null,                 "nav_name_n",           "contact") },
                { InteractableKind.Climb,       new InteractableInfo(NavPlacement.Stairs,        "nav_climb",          "nav_climb_n",          "climb") },
                { InteractableKind.WarpPanel,   new InteractableInfo(NavPlacement.Warp,          "nav_warp_panel",     "nav_warp_panel_n",     "warp_panel") },
                { InteractableKind.MagicCircle, new InteractableInfo(NavPlacement.Warp,          "nav_warp_circle",    "nav_warp_circle_n",    "warp_circle") },
                { InteractableKind.Switch,      new InteractableInfo(NavPlacement.Interactables, "nav_switch",         "nav_switch_n",         "switch") },
                { InteractableKind.Statue,      new InteractableInfo(NavPlacement.Interactables, "nav_statue",         "nav_statue_n",         "statue") },
                { InteractableKind.Door,        new InteractableInfo(NavPlacement.Doors,         "nav_gimmick_door",   "nav_gimmick_door_n",   "gimmick_door") },
                { InteractableKind.Rock,        new InteractableInfo(NavPlacement.Doors,         "nav_rock",           "nav_rock_n",           "rock") },
                { InteractableKind.FloorPanel,  new InteractableInfo(NavPlacement.Interactables, "nav_floor_panel",    "nav_floor_panel_n",    "floor_panel") },
                { InteractableKind.Mechanism,   new InteractableInfo(NavPlacement.Interactables, "nav_mechanism",      "nav_mechanism_n",      "mechanism") },
            };

        /// <summary>Facts about a kind; null for <see cref="InteractableKind.None"/>.</summary>
        public static InteractableInfo Info(InteractableKind kind) =>
            _info.TryGetValue(kind, out var info) ? info : null;

        /// <summary>Beacon cue for a kind, null when the kind is silent.</summary>
        public static NavCueKind? BeaconFor(InteractableKind kind) => Info(kind)?.Beacon;

        /// <summary>
        /// Classifies a live gimmick object by its game class. Unknown classes
        /// that wait for a button press (startup type Conversation) count as a
        /// Mechanism; unknown walk-in classes and the known traps are None.
        /// <paramref name="typeName"/> is the IL2CPP class name for the log.
        /// </summary>
        public static InteractableKind Classify(FieldGimmickBase gimmick, out string typeName)
        {
            typeName = "?";
            if (gimmick == null) return InteractableKind.None;

            try { typeName = gimmick.GetIl2CppType()?.Name ?? "?"; }
            catch (Exception ex) { DebugLogger.LogState($"Interactables: type name failed: {ex.Message}"); }

            switch (typeName)
            {
                case "FieldGimmick01":            return InteractableKind.Ledge;
                case "FieldGimmick03":            return InteractableKind.Climb;
                case "FieldGimmick05":            return InteractableKind.Gather;
                case "FieldGimmick09":            return InteractableKind.WarpPanel;
                case "FieldGimmick17":            return InteractableKind.MagicCircle;
                case "FieldGimmick11Switch":
                case "FieldGimmick16Switch":      return InteractableKind.Switch;
                case "FieldGimmick12StoneStatue": return InteractableKind.Statue;
                case "FieldGimmick07Door":
                case "FieldGimmick11Door":
                case "FieldGimmick14Door":        return InteractableKind.Door;
                case "FieldGimmick16":            return InteractableKind.Rock;
                case "FieldGimmick13":
                case "FieldGimmick14Panel":       return InteractableKind.FloorPanel;

                // Walk-in story triggers, trapped chests, ambushes, traps and the
                // sleeping guard: nothing a player chooses to walk to.
                case "FieldGimmick02":
                case "FieldGimmick04":
                case "FieldGimmick06":
                case "FieldGimmick10":
                case "FieldGimmick15":            return InteractableKind.None;

                default:
                    return WaitsForButton(gimmick) ? InteractableKind.Mechanism : InteractableKind.None;
            }
        }

        /// <summary>
        /// True when the object should be in the list right now: switched on by
        /// the game, and for a magic circle enabled and not warp-disabled.
        /// </summary>
        public static bool IsListable(InteractableKind kind, FieldGimmickBase gimmick)
        {
            if (kind == InteractableKind.None || gimmick == null) return false;
            try
            {
                if (!gimmick.gameObject.activeInHierarchy) return false;
                if (kind == InteractableKind.MagicCircle)
                {
                    var circle = gimmick.TryCast<FieldGimmick17>();
                    if (circle != null && (!circle.IsEnable() || circle.isDisableWarp)) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Interactables: listable check failed for {kind}: {ex.Message}");
                return false;
            }
        }

        /// <summary>The gimmick's startup type as text for the log ("Auto", "Conversation", or the error).</summary>
        public static string StartupText(FieldGimmickBase gimmick)
        {
            try { return gimmick.GetGimmickStartupType().ToString(); }
            catch (Exception ex) { return "error: " + ex.Message; }
        }

        /// <summary>
        /// True when the gimmick starts on a button press rather than on contact:
        /// startup type Conversation, or the object answers to the game's
        /// conversation check (gathering points report startup "Invalid" yet are
        /// examined with a button — log 2026-09-23).
        /// </summary>
        private static bool WaitsForButton(FieldGimmickBase gimmick)
        {
            try
            {
                return gimmick.GetGimmickStartupType() == GimmickStartupType.Conversation
                    || gimmick.IsConversation();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Interactables: startup type failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// The gimmick the player is in contact with right now, classified, plus
        /// the class names of it and of the game's conversation target for the
        /// log. Used by the prompt handler to tell which bubble just appeared.
        /// Returns None when nothing is in contact or the managers are away.
        /// </summary>
        public static InteractableKind CurrentContact(out string contactType, out string targetType)
        {
            contactType = "-";
            targetType  = "-";
            var kind = InteractableKind.None;
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return kind;

                var contact = fm.FieldGimmickManager?.PlayerContactGimmick;
                if (contact != null) kind = Classify(contact, out contactType);

                var target = fm.ConversationTarget;
                if (target != null) targetType = target.GetIl2CppType()?.Name ?? "?";
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Interactables: contact read failed: {ex.Message}");
            }
            return kind;
        }
    }
}
