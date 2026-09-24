using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The switch-and-boulder puzzles of a map (FieldGimmick16 = a boulder
    /// sealed by a magic circle, FieldGimmick16Switch = a switch that feeds it;
    /// Hoffman Ruins has 25 switches and 15 boulders). The game never switches
    /// a used switch or a broken boulder off, so the navigation list would keep
    /// every one of them forever (log 2026-09-24). This model reads the links
    /// the game itself stores — each boulder names its main switch and its sub
    /// switches, and each object has a save flag — and answers two questions:
    /// is this boulder broken, and is this switch dead (every boulder it feeds
    /// is broken)? A switch that feeds no boulder is never dead.
    ///
    /// Fairness: a broken boulder and a pressed switch are visible to a sighted
    /// player (the rock is gone, the switch is down, the fuse is lit), so
    /// hiding the dead ones and saying "pressed" gives nothing away.
    ///
    /// The game's method bodies are not readable (IL2CPP), so which flag flips
    /// at which moment is confirmed by the NAV:PUZZLE log lines, one per
    /// boulder and per switch, on every list build in debug mode.
    /// </summary>
    internal sealed class SwitchPuzzleModel
    {
        /// <summary>One boulder's puzzle facts, read once per list build.</summary>
        private sealed class Boulder
        {
            public int          Symbol;
            public int          MainSwitch;
            public List<int>    SubSwitches = new List<int>();
            public bool         Multi;
            public bool         Destroyed;
            public ScenarioFlag Flag;
            public bool         FlagSet;
            public Vector3      Pos;
            /// <summary>Asset names (log only, never spoken): the rock, its magic circle and the extra object.</summary>
            public string       Names;

            /// <summary>Broken when the game marked it destroyed or its save flag is set.</summary>
            public bool Broken => Destroyed || FlagSet;

            /// <summary>Stable-number identity shared by the boulder row and its switches' "boulder N" note.</summary>
            public string Identity => IdentityFor(MainSwitch);

            public override string ToString() =>
                $"symbol={Symbol} main={MainSwitch} subs=[{string.Join(",", SubSwitches)}] multi={Multi} " +
                $"destroyed={Destroyed} flag={Flag}={FlagSet} broken={Broken} pos=({Pos.x:F1},{Pos.y:F1},{Pos.z:F1}) {Names}";
        }

        /// <summary>The identity of the boulder whose main switch has this ID ("boulder16:1506").</summary>
        public static string IdentityFor(int mainSwitchID) => "boulder16:" + mainSwitchID;

        private readonly Dictionary<int, List<Boulder>> _bouldersBySwitch = new Dictionary<int, List<Boulder>>();

        /// <summary>Number of boulders read; 0 on a map without this puzzle kind.</summary>
        public int BoulderCount { get; private set; }

        /// <summary>
        /// Reads every boulder in the game's gimmick list and indexes it by the
        /// switches that feed it. Objects that are not boulders are skipped
        /// (one TryCast per object). Never throws: a boulder that cannot be read
        /// is logged and left out, which keeps its switches listed.
        /// </summary>
        public static SwitchPuzzleModel Read(Il2CppSystem.Collections.Generic.List<FieldGimmickBase> list)
        {
            var model = new SwitchPuzzleModel();
            if (list == null) return model;

            for (int i = 0; i < list.Count; i++)
            {
                FieldGimmick16 rock = null;
                try { rock = list[i]?.TryCast<FieldGimmick16>(); }
                catch (Exception ex) { DebugLogger.LogState($"NAV:PUZZLE [{i}] cast failed: {ex.Message}"); }
                if (rock == null) continue;

                try
                {
                    var boulder = new Boulder
                    {
                        Symbol     = rock.SymbolID,
                        MainSwitch = rock.MainSwitchID,
                        Multi      = rock.IsMultiSwitch,
                        Destroyed  = rock.isDestroyed,
                        Flag       = rock.ScenarioFlag,
                        Pos        = rock.transform.position,
                        Names      = $"rock='{rock.RockName}' circle='{rock.MagicCircleName}' other='{rock.OtherName}'",
                    };
                    boulder.FlagSet = IsFlagSet(boulder.Flag);
                    var subs = rock.SubSwitchIDList;
                    if (subs != null)
                        for (int s = 0; s < subs.Count; s++) boulder.SubSwitches.Add(subs[s]);

                    model.BoulderCount++;
                    model.Link(boulder.MainSwitch, boulder);
                    foreach (int sub in boulder.SubSwitches) model.Link(sub, boulder);
                    DebugLogger.LogGameValue("NAV:PUZZLE", $"boulder {boulder}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV:PUZZLE [{i}] boulder read failed: {ex.Message}");
                }
            }
            return model;
        }

        private void Link(int switchID, Boulder boulder)
        {
            if (!_bouldersBySwitch.TryGetValue(switchID, out var boulders))
            {
                boulders = new List<Boulder>();
                _bouldersBySwitch[switchID] = boulders;
            }
            boulders.Add(boulder);
        }

        /// <summary>True when the boulder is gone: destroyed by the game or its save flag set.</summary>
        public static bool IsBoulderBroken(FieldGimmick16 rock, out string detail)
        {
            detail = "";
            try
            {
                bool destroyed = rock.isDestroyed;
                var flag = rock.ScenarioFlag;
                bool flagSet = IsFlagSet(flag);
                detail = $"destroyed={destroyed} flag={flag}={flagSet}";
                return destroyed || flagSet;
            }
            catch (Exception ex)
            {
                detail = "read failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// The switch's state for the list: dead when every boulder it feeds is
        /// broken (a switch feeding nothing stays alive), pressed when its own
        /// save flag is set (log 2026-09-24: the flag goes on with a correct
        /// press and off again after a wrong-order ambush).
        /// <paramref name="boulderIdentity"/> names the first unbroken boulder
        /// the switch feeds, for the "boulder N" note; null when none.
        /// <paramref name="detail"/> is the log evidence.
        /// </summary>
        public void SwitchState(FieldGimmick16Switch sw, out bool dead, out bool pressed, out int switchID,
            out string boulderIdentity, out string detail)
        {
            dead            = false;
            pressed         = false;
            switchID        = -1;
            boulderIdentity = null;
            detail          = "";
            try
            {
                switchID = sw.SwitchID;
                var flag = sw.ScenarioFlag;
                pressed  = IsFlagSet(flag);

                int linked = 0, broken = 0;
                if (_bouldersBySwitch.TryGetValue(switchID, out var boulders))
                {
                    linked = boulders.Count;
                    broken = boulders.FindAll(b => b.Broken).Count;
                    boulderIdentity = boulders.Find(b => !b.Broken)?.Identity;
                }
                dead = linked > 0 && broken == linked;
                detail = $"id={switchID} flag={flag}={pressed} boulders={linked} broken={broken} dead={dead} " +
                         $"names: switch='{sw.SwitchName}' fuse='{sw.FuseName}' circle='{sw.MagicCircleName}'";
            }
            catch (Exception ex)
            {
                detail = "read failed: " + ex.Message;
            }
        }

        /// <summary>The save flag's current value; false for INVALID or when the save data is away.</summary>
        private static bool IsFlagSet(ScenarioFlag flag)
        {
            if (flag == ScenarioFlag.INVALID) return false;
            try
            {
                var user = ParameterManager.Instance?.UserParameter;
                if (user == null)
                {
                    DebugLogger.LogState("NAV:PUZZLE UserParameter is null, flag read as unset.");
                    return false;
                }
                return user.GetScenarioFlag(flag);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:PUZZLE flag {flag} read failed: {ex.Message}");
                return false;
            }
        }
    }
}
