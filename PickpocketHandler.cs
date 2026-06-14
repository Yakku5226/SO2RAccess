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
        private float _settleUntil;

        /// <summary>
        /// Called on map/scene transitions. A closed pickpocket overlay can linger
        /// active with stale choiceDataList after a transition, so start a short
        /// settle window during which the overlay's state is absorbed silently
        /// (see <see cref="Update"/>). Prevents a carried-over menu from being
        /// announced when the player walks through a building door.
        /// </summary>
        public void OnSceneChanged()
        {
            _selector = null;
            _wasActive = false;
            _lastIndex = -1;
            _nextFindTime = 0f;
            _settleUntil = UnityEngine.Time.time + 3f;
        }

        public void Update()
        {
            // Find or verify the selector. activeInHierarchy alone is unreliable
            // (stays true when hidden), so also require the choice list to have items.
            bool isActive = UiFinder.TryGetActiveOverlay(
                ref _selector, ref _nextFindTime,
                s => s.gameObject?.activeInHierarchy == true
                     && s.choiceDataList?.Count > 0);

            // Post-transition settle window: silently adopt whatever the overlay is
            // doing so a stale menu carried across a map change is never read as a
            // fresh open. A real pickpocket can't occur this soon after a load.
            if (UnityEngine.Time.time < _settleUntil)
            {
                _wasActive = isActive;
                if (isActive && _selector != null)
                {
                    try { _lastIndex = _selector.selectChoiceIndex; }
                    catch { _lastIndex = -1; }
                }
                else
                {
                    _lastIndex = -1;
                }
                return;
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
                TextUtil.AppendPosition(sb, idx, count);

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
