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

            LogResultDiag();

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

                string name = item.itemName ?? "Unknown";
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
                sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');

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
            string sig;
            try
            {
                var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
                var list = listBase?.currentDataList;
                sig = "";
                if (list != null && list.Count > 0)
                {
                    var it = list[0]?.TryCast<UICampSpecialSkillResultListItemData>();
                    if (it != null)
                        sig = $"{list.Count}:{it.itemName}:{it.isSuccess}";
                }
            }
            catch { return; }

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
        /// Debug-only diagnostic for the result selector. Appraisal does not go through
        /// the create-count flow that schedules the normal result announcement, so its
        /// result is currently never read. This logs the result selector's observable
        /// state — activeInHierarchy, list count, resultDataList count, specialSkillID,
        /// and the first result item's name/result — whenever it changes, so we can see
        /// exactly how an appraisal result surfaces and pick the right trigger.
        /// Change-gated; build cost gated behind Main.DebugMode (zero overhead when off).
        /// </summary>
        private void LogResultDiag()
        {
            if (!Main.DebugMode) return;

            bool act = false;
            try { act = _icResultSelector.gameObject.activeInHierarchy; } catch { }

            var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
            int cur = listBase?.currentDataList?.Count ?? -1;

            int rdl = -1;
            try { rdl = _icResultSelector.resultDataList?.Count ?? -1; } catch { }

            string sid = "?";
            try { sid = _icResultSelector.specialSkillID.ToString(); } catch { }

            string first = "none";
            try
            {
                var list = listBase?.currentDataList;
                if (list != null && list.Count > 0)
                {
                    var it = list[0]?.TryCast<UICampSpecialSkillResultListItemData>();
                    if (it != null)
                        first = $"{it.itemName}/{it.result}/{(it.isSuccess ? "ok" : "fail")}";
                }
            }
            catch { }

            string sig = $"act={act} cur={cur} rdl={rdl} sid={sid} first={first} ready={_icResultReadyTime > 0f} last={_icResultState.LastIndex}";
            if (sig == _icResultDiagSig) return;
            _icResultDiagSig = sig;
            DebugLogger.LogState($"CampIC_Result DIAG: {sig}");
        }

        #endregion
    }
}
