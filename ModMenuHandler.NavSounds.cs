using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// The two manual-navigation submenus of the mod settings menu:
    /// "Wall sounds" (four tones, each on/off + volume) and "Object beacons"
    /// (one cue per object kind, each on/off + volume, plus range and the
    /// behind-you treatment). Every sound row previews with Space / Square,
    /// through the same mixer the game-time cues use, so what you hear in the
    /// menu is what you will hear in the field.
    /// </summary>
    public partial class ModMenuHandler
    {
        #region Screens

        /// <summary>Opens the wall sounds submenu.</summary>
        private void OpenWallSoundsScreen()
        {
            var items = new List<ModMenuItem>();
            foreach (var kind in NavCues.Walls)
            {
                float pan = kind == NavCueKind.WallRight ? 1f : kind == NavCueKind.WallLeft ? -1f : 0f;
                AddCueRows(items, kind, pan);
            }

            items.Add(Metres("mod_menu_label_wall_range",
                () => ModSettings.WallRangeMeters,
                v => ModSettings.WallRangeMeters = v,
                ModSettings.WallRangeMin, ModSettings.WallRangeMax));

            OpenSettingsSubmenu("mod_menu_wall_sounds_group_open", items);
        }

        /// <summary>Opens the object beacons submenu.</summary>
        private void OpenBeaconSoundsScreen()
        {
            var items = new List<ModMenuItem>();
            foreach (var kind in NavCues.All)
            {
                if (NavCues.IsWall(kind)) continue;
                AddCueRows(items, kind, 0f);
            }

            items.Add(Metres("mod_menu_label_beacon_range",
                () => ModSettings.BeaconRangeMeters,
                v => ModSettings.BeaconRangeMeters = v,
                ModSettings.BeaconRangeMin, ModSettings.BeaconRangeMax));

            // The rear treatment previews the NPC beacon as if it were straight
            // behind you, so the two modes can be compared by ear.
            items.Add(new ModMenuItem
            {
                LabelKey = "mod_menu_label_beacon_rear",
                GetValue = () => Loc.Get(ModSettings.BeaconRear == BeaconRearMode.Muffled
                    ? "mod_menu_beacon_rear_muffled"
                    : "mod_menu_beacon_rear_quiet"),
                Change = _ => ModSettings.BeaconRear = ModSettings.BeaconRear == BeaconRearMode.Muffled
                    ? BeaconRearMode.QuieterOnly
                    : BeaconRearMode.Muffled,
                Preview = PreviewRearBeacon
            });

            OpenSettingsSubmenu("mod_menu_beacon_sounds_group_open", items);
        }

        #endregion

        #region Rows

        /// <summary>Adds the on/off row and the volume row for one cue, both previewing it.</summary>
        private void AddCueRows(List<ModMenuItem> items, NavCueKind kind, float previewPan)
        {
            string labelKey = NavCues.LabelKey(kind);
            Func<bool> preview = () => PreviewLoop(NavCues.FileName(kind), ModSettings.NavCue(kind).Volume, previewPan);

            items.Add(Toggle(labelKey,
                () => ModSettings.NavCue(kind).Enabled,
                v => ModSettings.NavCue(kind).Enabled = v, preview));
            items.Add(Volume(labelKey + "_volume",
                () => ModSettings.NavCue(kind).Volume,
                v => ModSettings.NavCue(kind).Volume = v, preview));
        }

        /// <summary>Plays the NPC beacon with the current rear treatment at full strength.</summary>
        private bool PreviewRearBeacon()
        {
            float volume = ModSettings.NavCue(NavCueKind.Npc).Volume;
            bool muffled = ModSettings.BeaconRear == BeaconRearMode.Muffled;
            float gain = volume * (muffled ? MuffledRearPreviewGain : QuietRearPreviewGain);
            return PreviewLoop(NavCues.FileName(NavCueKind.Npc), gain, 0f, muffled ? 1f : 0f);
        }

        /// <summary>Mirror of ManualNavHandler's rear gains, so the preview matches the field.</summary>
        private const float MuffledRearPreviewGain = 0.6f;
        private const float QuietRearPreviewGain = 0.5f;

        #endregion
    }
}
