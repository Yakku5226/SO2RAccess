using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// The looping cues of the manual-navigation system: four wall tones and one
    /// beacon per kind of object. Each has its own on/off and volume in the mod
    /// menu — there is deliberately no master switch (user decision 2026-09-05).
    /// </summary>
    public enum NavCueKind
    {
        WallFront = 0,
        WallRight = 1,
        WallBehind = 2,
        WallLeft = 3,
        Npc = 4,
        Chest = 5,
        Door = 6,
        Location = 7,
        Jump = 8,
        Stairs = 9,
        /// <summary>Save points got their own cue on 2026-09-06: sharing the location shimmer confused them with landmarks.</summary>
        Save = 10,
        /// <summary>
        /// One-shot "bump" when the player pushes the stick but does not move: a wall
        /// you are actually touching, which cannot be a false positive. Framework only
        /// (2026-09-06): no sound chosen yet, so it is left out of <see cref="NavCues.All"/>
        /// and therefore out of the menu. TODO: pick NavBump.wav, add Bump to All.
        /// </summary>
        Bump = 11
    }

    /// <summary>How beacons behind the player are told apart from those in front.</summary>
    public enum BeaconRearMode
    {
        /// <summary>Low-pass filtered ("through a wall") and a little quieter.</summary>
        Muffled = 0,
        /// <summary>Only quieter, no filtering.</summary>
        QuieterOnly = 1
    }

    /// <summary>One cue's user settings (persisted by <see cref="ModSettings"/>).</summary>
    public sealed class CueSetting
    {
        public bool Enabled { get; set; }
        public float Volume { get; set; }
    }

    /// <summary>Static facts about each manual-navigation cue: file, kind, defaults.</summary>
    public static class NavCues
    {
        /// <summary>Cue file name for each kind. NavStairs.wav is a placeholder the user will fill.</summary>
        private static readonly Dictionary<NavCueKind, string> _files = new Dictionary<NavCueKind, string>
        {
            { NavCueKind.WallFront,  "Wall_front.wav" },
            { NavCueKind.WallRight,  "Wall_right.wav" },
            { NavCueKind.WallBehind, "Wall_behind.wav" },
            { NavCueKind.WallLeft,   "Wall_left.wav" },
            { NavCueKind.Npc,        "NavNpc.wav" },
            { NavCueKind.Chest,      "NavChest.wav" },
            { NavCueKind.Door,       "NavDoor.wav" },
            { NavCueKind.Location,   "NavLocation.wav" },
            { NavCueKind.Jump,       "NavJump.wav" },
            { NavCueKind.Stairs,     "NavStairs.wav" },
            { NavCueKind.Save,       "NavSave.wav" },
            { NavCueKind.Bump,       "NavBump.wav" }
        };

        /// <summary>
        /// Every kind the player can see, in menu order (walls first, then beacons
        /// with the save point next to the location cue it was split from). Kept
        /// explicit so a kind added later can be placed where it belongs instead of
        /// at the end. <see cref="NavCueKind.Bump"/> is deliberately absent until it
        /// has a sound (TODO: insert it after WallLeft in the Wall sounds menu).
        /// </summary>
        public static readonly NavCueKind[] All =
        {
            NavCueKind.WallFront, NavCueKind.WallRight, NavCueKind.WallBehind, NavCueKind.WallLeft,
            NavCueKind.Npc, NavCueKind.Chest, NavCueKind.Door, NavCueKind.Location, NavCueKind.Save,
            NavCueKind.Jump, NavCueKind.Stairs
        };

        /// <summary>Every kind that exists, including hidden ones (settings storage).</summary>
        private static readonly NavCueKind[] _every = (NavCueKind[])Enum.GetValues(typeof(NavCueKind));

        /// <summary>The four wall kinds, indexed like <see cref="WallProbe.ProbeAround"/>: front, right, behind, left.</summary>
        public static readonly NavCueKind[] Walls =
            { NavCueKind.WallFront, NavCueKind.WallRight, NavCueKind.WallBehind, NavCueKind.WallLeft };

        /// <summary>Cue file name for a kind.</summary>
        public static string FileName(NavCueKind kind) => _files[kind];

        /// <summary>True for the four wall tones.</summary>
        public static bool IsWall(NavCueKind kind) => kind <= NavCueKind.WallLeft;

        /// <summary>
        /// Fresh default settings. Wall tones start OFF until the wall-probe audit
        /// has passed on the user's maps (rule: no geometry-derived aid before its
        /// breadcrumb audit); beacons start on.
        /// </summary>
        public static Dictionary<NavCueKind, CueSetting> Defaults()
        {
            var d = new Dictionary<NavCueKind, CueSetting>();
            foreach (var kind in _every)
                d[kind] = new CueSetting { Enabled = !IsWall(kind) && kind != NavCueKind.Bump, Volume = 0.7f };
            return d;
        }

        /// <summary>Menu label key for a kind (its volume row appends "_volume").</summary>
        public static string LabelKey(NavCueKind kind)
        {
            switch (kind)
            {
                case NavCueKind.WallFront:  return "mod_menu_label_wall_front";
                case NavCueKind.WallRight:  return "mod_menu_label_wall_right";
                case NavCueKind.WallBehind: return "mod_menu_label_wall_behind";
                case NavCueKind.WallLeft:   return "mod_menu_label_wall_left";
                case NavCueKind.Npc:        return "mod_menu_label_beacon_npc";
                case NavCueKind.Chest:      return "mod_menu_label_beacon_chest";
                case NavCueKind.Door:       return "mod_menu_label_beacon_door";
                case NavCueKind.Location:   return "mod_menu_label_beacon_location";
                case NavCueKind.Jump:       return "mod_menu_label_beacon_jump";
                case NavCueKind.Save:       return "mod_menu_label_beacon_save";
                case NavCueKind.Bump:       return "mod_menu_label_wall_bump";
                default:                    return "mod_menu_label_beacon_stairs";
            }
        }
    }
}
