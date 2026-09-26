using Il2CppGame;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The bake's own VERDICT: the finished route proofs are checked against
    /// ground truth the project already owns, so nobody has to walk to every
    /// fishing spot to trust a new stands file. Two kinds of truth: every
    /// remembered bubble (a place the player really fished at) must keep a
    /// proven stand, and stands that are known to be walled off from their gate
    /// must not come back proven. A bake that disagrees is written aside as a
    /// rejected file and the stands in use stay untouched.
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        /// <summary>A proven stand this close (m, flat) to a known-bad stand IS that stand.</summary>
        private const float KnownBadStandMeters = 1.5f;

        /// <summary>
        /// Stands, per planet, that were "proven" from the town symbol centre
        /// and refused by the real walk from the gate (Expel: logs 2026-09-20).
        /// A planet without entries has no such ground truth yet.
        /// </summary>
        private static readonly Dictionary<WorldmapID, (int placeId, float x, float z, string why)[]> KnownBadStandsByMap =
            new Dictionary<WorldmapID, (int placeId, float x, float z, string why)[]>
            {
                [WorldmapID.EXPEL] = new[]
                {
                    (4, 737f, -172.5f, "Hilton: behind the gate fences, walk wedged at (745.0,-173.8)"),
                    (4, 737f, -167.5f, "Hilton: behind the gate fences"),
                    (1, -37f, -395f, "Arlia: walk slid along Wall_Arlia for 28 s"),
                },
            };

        /// <summary>
        /// Logs the verdict table and returns true when the bake agrees with every
        /// piece of ground truth. A file without baked proofs fails: there is
        /// nothing to agree with.
        /// </summary>
        private static bool VerdictPasses(WorldmapID wmID, FishingStandFile file, FishingStandFile previous)
        {
            if (!file.ProofsBaked)
            {
                Log("VERDICT: FAIL — route proofs were not baked.");
                return false;
            }

            int checks = 0, failed = 0;
            foreach (var bubble in WorldmapBubbleMemory.All(wmID))
            {
                checks++;
                var place = WorldmapFishingStands.TryGetPlace(file, bubble.WaterPlaceId);
                var proven = place?.Stands.FindAll(s => s.Proven) ?? new List<FishingStandEntry>();
                bool ok = proven.Count > 0;
                if (!ok) failed++;
                string detail = place == null ? "place missing from the bake"
                    : !ok ? $"NO proven stand ({place.Stands.Count} stands, {place.ProofAttempts} attempts)"
                    : $"{proven.Count} proven, nearest {NearestStandMeters(proven, bubble.Position):F1} m from the bubble";
                Log($"Verdict: real bubble ({bubble.X:F1},{bubble.Z:F1}) place {bubble.WaterPlaceId} seen {bubble.Seen}×: " +
                    $"{detail} — {(ok ? "PASS" : "FAIL")}.");
            }

            if (KnownBadStandsByMap.TryGetValue(wmID, out var knownBad))
            {
                foreach (var (placeId, x, z, why) in knownBad)
                {
                    checks++;
                    var place = WorldmapFishingStands.TryGetPlace(file, placeId);
                    var pos = new Vector3(x, 0f, z);
                    var stand = place?.Stands.Find(s =>
                        FlatDistanceSq(s.Position, pos) <= KnownBadStandMeters * KnownBadStandMeters);
                    bool ok = stand == null || !stand.Proven;
                    if (!ok) failed++;
                    Log($"Verdict: known-bad stand ({x:F1},{z:F1}) place {placeId} ({why}): " +
                        (stand == null ? "not a stand any more"
                            : stand.Proven ? $"PROVEN AGAIN from {stand.ProvenFrom} ({stand.ProofTier}, {stand.ProofRouteMeters:F0} m)"
                            : "unproven") +
                        $" — {(ok ? "PASS" : "FAIL")}.");
                    if (place != null && place.HasProvenStand && ok)
                    {
                        var d = place.Stands[0];
                        Log($"Verdict: place {placeId} now offers ({d.X:F1},{d.Z:F1}) proven from {d.ProvenFrom} " +
                            $"({d.ProofTier}, {d.ProofRouteMeters:F0} m).");
                    }
                }
            }

            if (previous != null) LogProofChanges(previous, file);
            Log($"VERDICT: {(failed == 0 ? "PASS" : "FAIL")} — {checks} ground truth checks, {failed} failed.");
            return failed == 0;
        }

        /// <summary>Log-only: the places whose "has a proven stand" answer differs from the previous file.</summary>
        private static void LogProofChanges(FishingStandFile previous, FishingStandFile fresh)
        {
            var lost = new List<string>();
            var gained = new List<string>();
            foreach (var place in fresh.Places)
            {
                var old = WorldmapFishingStands.TryGetPlace(previous, place.WaterPlaceId);
                if (old == null || old.HasProvenStand == place.HasProvenStand) continue;
                (place.HasProvenStand ? gained : lost).Add(place.WaterPlaceId.ToString());
            }
            Log($"Verdict: against the previous file, places that LOST their proof [{string.Join(",", lost)}], " +
                $"gained one [{string.Join(",", gained)}].");
        }

        private static float NearestStandMeters(List<FishingStandEntry> stands, Vector3 pos)
        {
            float best = float.MaxValue;
            foreach (var s in stands) best = Mathf.Min(best, FlatDistanceSq(s.Position, pos));
            return Mathf.Sqrt(best);
        }
    }
}
