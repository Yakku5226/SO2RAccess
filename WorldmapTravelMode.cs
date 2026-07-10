using Il2CppGame;
using System;

namespace SO2RAccess
{
    /// <summary>
    /// The player's current travel mode on the world map. Reachability
    /// differs per mode: on foot the player is blocked by rocks and region
    /// walls, the giant bunny crosses most land obstacles (but still needs
    /// ground — no ocean), and the psynard flies over everything.
    /// </summary>
    public enum WorldmapTravelMode
    {
        /// <summary>Walking as a normal FieldPlayer.</summary>
        Foot,

        /// <summary>Riding the giant bunny (FieldBunny control player).</summary>
        Bunny,

        /// <summary>Flying the psynard (separate FieldPsynard object).</summary>
        Psynard,
    }

    /// <summary>
    /// Detects the player's current world-map travel mode each frame.
    /// Primary signal: <c>FieldManager.IsFieldFlag</c> with
    /// <c>FieldBitFlag.Bunny</c>/<c>FieldBitFlag.Psynard</c> (the game's own
    /// ride-state bits). Fallback: the concrete type of the control player
    /// (<c>FieldBunny</c> is a FieldPlayer subclass that replaces the control
    /// player while mounted). The psynard is NOT a FieldPlayer, so only the
    /// bit flag can report it.
    /// </summary>
    public static class WorldmapTravel
    {
        /// <summary>
        /// Returns the current travel mode. Defaults to Foot when the field
        /// manager is unavailable or the IL2CPP calls fail (fail-open: Foot
        /// is the most restrictive mode for pathfinding, but reachability
        /// callers must already treat unknown as reachable).
        /// </summary>
        public static WorldmapTravelMode CurrentMode()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return WorldmapTravelMode.Foot;

                if (fm.IsFieldFlag(FieldBitFlag.Psynard))
                    return WorldmapTravelMode.Psynard;
                if (fm.IsFieldFlag(FieldBitFlag.Bunny))
                    return WorldmapTravelMode.Bunny;

                // Fallback: flag polling can lag a frame around mount
                // transitions — the concrete control-player type is the
                // ground truth for the bunny.
                var player = fm.GetControlPlayer();
                if (player != null && player.TryCast<FieldBunny>() != null)
                    return WorldmapTravelMode.Bunny;

                return WorldmapTravelMode.Foot;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM CurrentMode error: {ex.Message}");
                return WorldmapTravelMode.Foot;
            }
        }
    }
}
