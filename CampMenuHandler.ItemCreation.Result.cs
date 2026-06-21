using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler (Item Creation): result-screen polling, new-result detection, result diagnostics.
    public partial class CampMenuHandler
    {
        #region Screen 3: Result

        private void UpdateICResult()
        {
            if (_icResultSelector == null) return;

            // Detect a freshly appeared result and schedule its announcement. The
            // create-count flow (PollCreateMode) handles regular item creation, but
            // appraisal bypasses it — so trigger off the result list gaining content.
            // Both paths funnel through the single _icResultReadyTime, so a regular
            // creation that fires both still resets LastIndex once -> one announcement.
            DetectNewResult();

            // Delayed reset: wait for result animation before announcing.
            if (_icResultReadyTime > 0f && UnityEngine.Time.time >= _icResultReadyTime)
            {
                _icResultState.LastIndex = -1;
                _icResultReadyTime = 0f;
                DebugLogger.LogState("CampIC: result index reset after delay.");
            }

            bool isActive;
            try { isActive = _icResultSelector.gameObject.activeInHierarchy; }
            catch { return; }

            bool shouldPoll = _icResultState.CheckEntry(
                isActive,
                () => ScreenReader.Say(Loc.Get("ic_result_heading")),
                "CampIC_Result");

            if (!shouldPoll) return;

            try
            {
                var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
                if (listBase == null) return;

                int idx = listBase.currentIndex;
                if (idx == _icResultState.LastIndex) return;
                _icResultState.LastIndex = idx;

                var list = listBase.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0 || idx < 0 || idx >= count) return;

                var item = list[idx]?.TryCast<UICampSpecialSkillResultListItemData>();
                if (item == null) return;

                string name = item.itemName ?? Loc.Get("ic_unknown_item");
                string resultText = item.result ?? "";
                string status = item.isSuccess
                    ? Loc.Get("ic_result_success")
                    : Loc.Get("ic_result_failure");

                var sb = new StringBuilder();
                sb.Append(name).Append(". ").Append(status);
                // Skip resultText when it just repeats the success/failure status
                // (appraisal stores "Success"/"Failure" in both fields).
                if (!string.IsNullOrEmpty(resultText) &&
                    !string.Equals(resultText.Trim(), status.Trim(), StringComparison.OrdinalIgnoreCase))
                    sb.Append(". ").Append(resultText);
                TextUtil.AppendPosition(sb, idx, count);

                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"CampIC: result [{idx}] {name} - {status}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: result error: {ex.Message}");
            }
        }

        /// <summary>
        /// Detects when the result list gains a new result (e.g. an appraisal outcome)
        /// by watching its first item's content signature, and schedules the
        /// announcement via the shared <see cref="_icResultReadyTime"/>. Regular item
        /// creation is handled by the create-count flow (PollCreateMode); appraisal
        /// bypasses that, so this is what makes its result speak. Both paths write the
        /// single _icResultReadyTime, so a creation that triggers both still resets the
        /// cursor once -> one announcement.
        ///
        /// Known limitation: two appraisals in a row that yield an identical result
        /// string won't re-announce the second (no content change to detect). Distinct
        /// results — the normal case — always read.
        /// </summary>
        private void DetectNewResult()
        {
            string sig = GetResultSignature();

            if (sig == _icResultSeenSig) return;
            _icResultSeenSig = sig;

            if (!string.IsNullOrEmpty(sig))
            {
                // Small delay lets the result view settle; the ready-time handler then
                // resets LastIndex so the poll reads the result item.
                _icResultReadyTime = UnityEngine.Time.time + 0.5f;
                DebugLogger.LogState($"CampIC: new result detected ({sig}), announce scheduled.");
            }
        }

        /// <summary>
        /// Re-seeds the result sub-screen state when the user re-enters Item Creation
        /// (root cursor lands on it). Mirrors the camp-open seed: marks the current
        /// result signature as already-seen and pre-seeds the cursor index with the
        /// heading suppressed, so leftover result data from an earlier creation this
        /// session is not re-announced. A later, genuinely new creation changes the
        /// signature, so DetectNewResult still fires for real results.
        /// </summary>
        private static void SeedResultStateOnEntry()
        {
            _icResultSeenSig = GetResultSignature();
            try
            {
                var listBase = _icResultSelector?.TryCast<UIListSelectorBase>();
                if (listBase != null)
                    _icResultState.SeedOnOpen(listBase.currentIndex);
                else
                    _icResultState.SuppressNextHeading();
            }
            catch
            {
                _icResultState.SuppressNextHeading();
            }
            DebugLogger.LogState("CampIC_Result: re-seeded on IC re-entry.");
        }

        /// <summary>
        /// Computes the content signature ("count:name:success") of the result list's
        /// first item, or "" when the list is empty/unavailable. Used both to seed the
        /// seen-signature on camp open — so stale result data left in the selector from
        /// a previous creation isn't mistaken for a fresh result when the user merely
        /// highlights ItemCreation in the root menu — and to detect genuinely new results.
        /// </summary>
        private static string GetResultSignature()
        {
            try
            {
                var listBase = _icResultSelector?.TryCast<UIListSelectorBase>();
                var list = listBase?.currentDataList;
                if (list != null && list.Count > 0)
                {
                    var it = list[0]?.TryCast<UICampSpecialSkillResultListItemData>();
                    if (it != null)
                        return $"{list.Count}:{it.itemName}:{it.isSuccess}";
                }
            }
            catch { /* fall through to empty */ }
            return "";
        }

        #endregion
    }
}
