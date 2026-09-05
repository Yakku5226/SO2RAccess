using Il2CppGame;
using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Sub-event table diagnostic (debug mode only)

        /// <summary>
        /// Debug-only: dumps every sub-event the game defines for the current map,
        /// with the conditions that enable it, plus the player's current story and
        /// sub-scenario progress. A single event trigger (one `ev_sub_...` object)
        /// can carry several stages that unlock at different progress values; the
        /// live trigger only reports the stage that is enabled NOW. This dump shows
        /// the later stages too — e.g. a "we could climb up here if we tried" hint
        /// stage followed by the actual climb stage and what gates it.
        /// Reads ParameterManager.GetSubEventParameter(FieldmapID) and
        /// UserParameter.GetSubScenarioProgress(SubScenarioID).
        /// </summary>
        private static void LogSubEventTable(FieldmapID mapID)
        {
            if (!Main.DebugMode) return;

            try
            {
                var pm = ParameterManager.Instance;
                var user = pm?.UserParameter;
                var table = pm?.GetSubEventParameter(mapID);
                if (table == null)
                {
                    DebugLogger.LogState($"NAV:SUBEVT table for {mapID} is null.");
                    return;
                }

                int mainProgress = -1;
                try { if (user != null) mainProgress = user.MainScenarioProgress; }
                catch (Exception ex) { DebugLogger.LogState($"NAV:SUBEVT main progress: {ex.Message}"); }

                DebugLogger.LogState(
                    $"NAV:SUBEVT table for {mapID}: {table.Count} entries. mainScenarioProgress={mainProgress}");

                for (int i = 0; i < table.Count; i++)
                {
                    var e = table[i];
                    if (e == null) continue;

                    int subProgress = -1;
                    try { if (user != null) subProgress = user.GetSubScenarioProgress(e.SubScenarioID); }
                    catch (Exception ex) { DebugLogger.LogState($"NAV:SUBEVT sub progress: {ex.Message}"); }

                    DebugLogger.LogState(
                        $"NAV:SUBEVT [{i}] placement='{e.PlacementID}' start={e.EventStartType} " +
                        $"function='{e.EventFunction}' sub={e.SubScenarioID} " +
                        $"subRange={e.StartSubScenarioProgress}..{e.EndSubScenarioProgress} next={e.NextSubScenarioProgress} " +
                        $"(current={subProgress}) mainRange={e.StartMainScenarioProgress}..{e.EndMainScenarioProgress} " +
                        $"mapjump={e.MapjumpID} nextMap={e.NextFieldmapID} treasure={e.TreasureID} enemyParty={e.EnemyPartyID} " +
                        $"enablePlayer={e.EnablePlayerID} disablePlayer={e.DisablePlayerID} partyCount={e.PartyCount} " +
                        $"enableFlags={FormatFlags(e.EnableFlag)} disableFlags={FormatFlags(e.DisableFlag)} " +
                        $"hiddenIcon={e.IsDisableIcon}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:SUBEVT dump failed: {ex.Message}");
            }
        }

        /// <summary>Formats a scenario-flag list for the log.</summary>
        private static string FormatFlags(Il2CppSystem.Collections.Generic.List<ScenarioFlag> flags)
        {
            if (flags == null || flags.Count == 0) return "[]";
            var names = new List<string>(flags.Count);
            for (int i = 0; i < flags.Count; i++) names.Add(flags[i].ToString());
            return "[" + string.Join(",", names) + "]";
        }

        #endregion
    }
}
