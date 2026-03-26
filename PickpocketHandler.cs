using Il2CppGame;
using System;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces pickpocket menu items on the field.
    /// The menu appears when pickpocketing is active and the player interacts with an NPC.
    /// Navigation is native-only (polling required).
    /// Data: UIFieldPickPocketSelector.selectChoiceIndex, choiceDataList (UIChoiceData items).
    /// Each item has message (name), rate (success %), itemCount, canDecision.
    /// </summary>
    public class PickpocketHandler
    {
        private UIFieldPickPocketSelector _selector;
        private bool _wasActive;
        private int _lastIndex = -1;
        private float _nextFindTime;

        public void Update()
        {
            bool isActive = false;

            // Find or verify the selector.
            if (_selector != null)
            {
                try
                {
                    // activeInHierarchy alone is unreliable (stays true when not shown).
                    // Also require the choice list to have items.
                    isActive = _selector.gameObject?.activeInHierarchy == true
                        && _selector.choiceDataList?.Count > 0;
                }
                catch { _selector = null; }
            }

            if (_selector == null && UnityEngine.Time.time >= _nextFindTime)
            {
                _nextFindTime = UnityEngine.Time.time + 1f;
                try
                {
                    _selector = UnityEngine.Object.FindObjectOfType<UIFieldPickPocketSelector>();
                    if (_selector != null)
                    {
                        try
                        {
                            isActive = _selector.gameObject?.activeInHierarchy == true
                                && _selector.choiceDataList?.Count > 0;
                        }
                        catch { _selector = null; }
                    }
                }
                catch { /* skip */ }
            }

            if (!isActive)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    _lastIndex = -1;
                }
                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;
                _lastIndex = -1;
                // Announce heading on open.
                ScreenReader.Say(Loc.Get("pickpocket_heading"));
                DebugLogger.LogState("Pickpocket: menu opened.");
                return; // Skip first frame.
            }

            // Poll cursor index.
            try
            {
                int idx = _selector.selectChoiceIndex;
                if (idx == _lastIndex) return;
                _lastIndex = idx;

                var list = _selector.choiceDataList;
                int count = list?.Count ?? 0;
                if (count <= 0 || idx < 0 || idx >= count) return;

                var baseItem = list[idx];
                if (baseItem == null) return;

                // Try cast to UIFieldPickPocketChoiceData for the rate field.
                var ppItem = baseItem.TryCast<UIFieldPickPocketChoiceData>();

                string name = baseItem.message ?? "";
                string rate = ppItem?.rate;
                bool canDo = baseItem.canDecision;

                var sb = new StringBuilder();
                sb.Append(name);
                if (!string.IsNullOrEmpty(rate))
                {
                    // Rate already contains "%" — strip it for clean TTS.
                    string rateClean = rate.Replace("%", "").Trim();
                    if (!string.IsNullOrEmpty(rateClean))
                        sb.Append(", ").Append(Loc.Get("pickpocket_rate", rateClean));
                }
                if (!canDo)
                    sb.Append(", ").Append(Loc.Get("ic_unavailable"));
                sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');

                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"Pickpocket: [{idx}] {name} rate={rate}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Pickpocket: poll error: {ex.Message}");
            }
        }
    }
}
