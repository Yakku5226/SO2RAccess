using Il2CppGame;
using Il2CppInterop.Runtime;
using MelonLoader;
using System;
using System.Runtime.InteropServices;

namespace SO2RAccess
{
    /// <summary>
    /// DEBUG-ONLY diagnostic for the Fish Collector ("Reel") trade menus in towns.
    /// The flow is multi-level: a top menu (UIFishCollectorMenuSelector) → select-fish
    /// list (UIFishCollectorSelectFishSelector) → exchange/trade confirm
    /// (UIFishCollectorExchangeSelector), plus a check-reward list
    /// (UIFishCollectorCheckRewardSelector). All of these extend UIListSelectorBase and
    /// expose their item data through plain managed fields, so — unlike the guild — they
    /// are readable. This logs each selector's current item (change-gated) so the on-screen
    /// text can be matched to the underlying fields before a real reader handler is written.
    ///
    /// Gated on <see cref="Main.DebugMode"/>: no announcements, no behaviour change.
    /// Scaffolding — to be removed once the collector reader is built.
    /// </summary>
    public class FishCollectorDiagnostics
    {
        private UIFishCollectorMenuSelector _menu;
        private UIFishCollectorSelectFishSelector _selectFish;
        private UIFishCollectorExchangeSelector _exchange;
        private UIFishCollectorCheckRewardSelector _reward;
        private float _menuFind, _selectFishFind, _exchangeFind, _rewardFind;
        private string _menuSig, _selectFishSig, _exchangeSig, _rewardSig;

        public void Update()
        {
            if (!Main.DebugMode) return;

            PollMenu();
            PollSelectFish();
            PollExchange();
            PollReward();
        }

        private void PollMenu()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _menu, ref _menuFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { _menuSig = null; return; }
            try
            {
                int idx = _menu.currentIndex;
                var list = _menu.currentDataList;
                int count = list?.Count ?? 0;
                string body = "(out of range)";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var raw = list[idx];
                    var it = raw.TryCast<UIFishCollectorMenuListItemData>();
                    if (it != null)
                    {
                        body = $"menuName='{it.menuName}'";
                    }
                    else
                    {
                        // Wrong type — read the REAL il2cpp runtime class name via reflection
                        // (the managed wrapper's GetType() only reports the cast type).
                        string realType = "?";
                        try
                        {
                            System.IntPtr klass = IL2CPP.il2cpp_object_get_class(raw.Pointer);
                            System.IntPtr nsPtr = IL2CPP.il2cpp_class_get_namespace(klass);
                            System.IntPtr namePtr = IL2CPP.il2cpp_class_get_name(klass);
                            string ns = Marshal.PtrToStringAnsi(nsPtr);
                            string nm = Marshal.PtrToStringAnsi(namePtr);
                            realType = string.IsNullOrEmpty(ns) ? nm : $"{ns}.{nm}";
                        }
                        catch (Exception tex) { realType = $"(type err: {tex.Message})"; }
                        body = $"(cast failed; real type={realType})";
                    }
                }
                Log("MENU", ref _menuSig, idx, count, body);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollectorDiag MENU: {ex.Message}"); }
        }

        private void PollSelectFish()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _selectFish, ref _selectFishFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { _selectFishSig = null; return; }
            try
            {
                int idx = _selectFish.currentIndex;
                var list = _selectFish.currentDataList;
                int count = list?.Count ?? 0;
                string body = "(out of range)";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx].TryCast<UIFishCollectorSelectFishListItemData>();
                    body = it != null
                        ? $"itemName='{it.itemName}' fishSize='{it.fishSize}' use={it.useCount} have={it.haveCount} itemID={it.itemID}"
                        : "(cast failed)";
                }
                Log("SELECTFISH", ref _selectFishSig, idx, count, body);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollectorDiag SELECTFISH: {ex.Message}"); }
        }

        private void PollExchange()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _exchange, ref _exchangeFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { _exchangeSig = null; return; }
            try
            {
                int idx = _exchange.currentIndex;
                var list = _exchange.currentDataList;
                int count = list?.Count ?? 0;
                string body = "(out of range)";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx].TryCast<UIFishCollectorExchangeListItemData>();
                    body = it != null
                        ? $"itemName='{it.itemName}' fishSize='{it.fishSize}' need={it.needCount} exchange={it.exchangeCount} have={it.haveCount}"
                        : "(cast failed)";
                }
                Log("EXCHANGE", ref _exchangeSig, idx, count, body);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollectorDiag EXCHANGE: {ex.Message}"); }
        }

        private void PollReward()
        {
            bool active = UiFinder.TryGetActiveOverlay(ref _reward, ref _rewardFind,
                s => s.gameObject?.activeInHierarchy == true && s.currentDataList?.Count > 0);
            if (!active) { _rewardSig = null; return; }
            try
            {
                int idx = _reward.currentIndex;
                var list = _reward.currentDataList;
                int count = list?.Count ?? 0;
                string body = "(out of range)";
                if (count > 0 && idx >= 0 && idx < count)
                {
                    var it = list[idx].TryCast<UIFishCollectorCheckRewardListItemData>();
                    body = it != null
                        ? $"rewardName='{it.rewardName}' fishCount={it.fishCount} cleared={it.isCleared}"
                        : "(cast failed)";
                }
                Log("REWARD", ref _rewardSig, idx, count, body);
            }
            catch (Exception ex) { DebugLogger.LogState($"FishCollectorDiag REWARD: {ex.Message}"); }
        }

        /// <summary>Change-gated log so each selector only prints when its current item changes.</summary>
        private static void Log(string tag, ref string lastSig, int idx, int count, string body)
        {
            string sig = $"{tag} idx={idx + 1}/{count}: {body}";
            if (sig == lastSig) return;
            lastSig = sig;
            MelonLogger.Msg($"[SO2RAccess] [FISHCOLLECTOR] {sig}");
        }
    }
}
