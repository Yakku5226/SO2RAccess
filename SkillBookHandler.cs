using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the result of using a skill guidebook from the camp Items screen.
    /// The game raises the skill silently — it shows no text at all afterwards
    /// (probe 2026-09-19) — so the only feedback was the book count dropping.
    /// Hook: ItemSkillLevelup.OnProcess (prefix snapshots every party member's level
    /// in the book's skill, postfix speaks whoever changed).
    /// </summary>
    public class SkillBookHandler
    {
        private static bool _patchesApplied;

        /// <summary>Level of the book's skill per party member, taken just before the use.</summary>
        private static readonly Dictionary<PlayerID, int> _levelsBefore = new Dictionary<PlayerID, int>();

        /// <summary>Registers the Harmony hook on ItemSkillLevelup.OnProcess.</summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(ItemSkillLevelup).TypeHandle);

                var method = AccessTools.Method(typeof(ItemSkillLevelup), "OnProcess", Type.EmptyTypes);
                if (method == null)
                {
                    MelonLogger.Warning("SkillBookHandler: ItemSkillLevelup.OnProcess not found.");
                    return;
                }

                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(SkillBookHandler), nameof(OnProcess_Prefix)),
                    postfix: new HarmonyMethod(typeof(SkillBookHandler), nameof(OnProcess_Postfix)));

                _patchesApplied = true;
                MelonLogger.Msg("SkillBookHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SkillBookHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        private static void OnProcess_Prefix(ItemSkillLevelup __instance)
        {
            _levelsBefore.Clear();
            if (__instance == null) return;
            try
            {
                ReadLevels(__instance.skillID, _levelsBefore);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SkillBook: prefix read error: {ex.Message}");
            }
        }

        private static void OnProcess_Postfix(ItemSkillLevelup __instance)
        {
            if (__instance == null || _levelsBefore.Count == 0) return;
            try
            {
                var skillId = __instance.skillID;
                var after = new Dictionary<PlayerID, int>();
                ReadLevels(skillId, after);

                string skillName = ResolveSkillName(skillId);
                var lines = new List<string>();
                foreach (var pair in after)
                {
                    if (!_levelsBefore.TryGetValue(pair.Key, out int before) || pair.Value == before) continue;
                    string who = ParameterManager.Instance?.GetCharacterFirstName(pair.Key);
                    if (string.IsNullOrEmpty(who)) who = pair.Key.ToString();
                    lines.Add(Loc.Get("skillbook_level_up", who, skillName, pair.Value));
                }

                DebugLogger.LogState($"SkillBook: {skillId} ('{skillName}') used, changes=[{string.Join("; ", lines)}].");
                if (lines.Count > 0)
                    ScreenReader.Say(string.Join(" ", lines));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SkillBook: postfix error: {ex.Message}");
            }
            finally
            {
                _levelsBefore.Clear();
            }
        }

        /// <summary>Fills <paramref name="target"/> with each party member's level in the skill.</summary>
        private static void ReadLevels(SkillID skillId, Dictionary<PlayerID, int> target)
        {
            var members = PartyManager.Instance?.GetPartyMembers();
            var user = ParameterManager.Instance?.UserParameter;
            if (members == null || user == null) return;

            foreach (var id in members)
            {
                if (id == PlayerID.INVALID || id == PlayerID.MAX) continue;
                var cp = user.GetCharacterParameter(id);
                if (cp != null) target[id] = cp.GetSkillLevel(skillId);
            }
        }

        /// <summary>The skill's display name in the game's language; the generic word when unresolved.</summary>
        private static string ResolveSkillName(SkillID skillId)
        {
            try
            {
                var pm = ParameterManager.Instance;
                string nameId = pm?.GetSkillParameter(skillId)?.skillNameID;
                if (!string.IsNullOrEmpty(nameId))
                {
                    string name = pm.GetSkillMessage(nameId);
                    if (!string.IsNullOrEmpty(name) && name != nameId) return name;
                }
                DebugLogger.LogState($"SkillBook: no display name for {skillId} (nameId='{nameId}').");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SkillBook: skill name error: {ex.Message}");
            }
            return Loc.Get("skillbook_skill_fallback");
        }
    }
}
