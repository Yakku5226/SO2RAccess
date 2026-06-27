using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the field Quick Recovery ("quick heal") menu, opened by pressing
    /// Right on the D-pad. Reads the Yes/No confirmation cursor and, on demand
    /// (NumPad 0), the party HP/MP status with pending recovery amounts. After the
    /// heal is confirmed it announces the result (who recovered, who spent MP casting).
    ///
    /// The game owns the D-pad Right key — this handler only detects the overlay
    /// (UIFieldQuickRecoverySelector) and reads it. Navigation is native-only
    /// (OnUp/OnDown/OnDecision are CallerCount 0), so the cursor is polled, same
    /// pattern as <see cref="PickpocketHandler"/> and the camp menus.
    ///
    /// Recovery is spell-based: party members cast their learned recovery spells and
    /// spend MP. Each status entry's changeHp/changeMp is the PROJECTED post-recovery
    /// total, so the actual amount restored is changeHp - hp (and a healer's MP drops,
    /// changeMp &lt; mp). Result announcement is driven by a Harmony postfix on
    /// GameManager.QuickRecovery (the execution point, shared with the camp variant —
    /// gated to a fresh field snapshot so camp recovery does not trigger it).
    /// </summary>
    public class QuickRecoveryHandler
    {
        /// <summary>Projected per-member outcome captured while the menu is open.</summary>
        private sealed class MemberSnap
        {
            public string Name;
            public int Hp, HpMax, ChangeHp, Mp, MpMax, ChangeMp;
        }

        private UIFieldQuickRecoverySelector _selector;
        private bool _wasActive;
        private UIDefine.DialogChoices _lastChoice = UIDefine.DialogChoices.None;
        private float _nextFindTime;
        private float _settleUntil;

        // Cached conversation window for the cutscene/dialogue gate (see
        // IsBlockedByEventOrDialogue). Refreshed lazily, throttled.
        private UIConversationWindow _conversationWindow;
        private float _nextConversationFindTime;

        private List<MemberSnap> _snapshot;
        private float _snapshotTime = -999f;

        // Set by the GameManager.QuickRecovery postfix; consumed in Update.
        private static bool _healExecuted;
        private static float _healExecutedTime = -999f;
        private static bool _patchesApplied;

        // A snapshot older than this (seconds) is considered stale — guards against the
        // camp quick recovery firing the shared execution hook.
        private const float SnapshotFreshWindow = 2f;

        #region Patch Application

        /// <summary>
        /// Patches GameManager.QuickRecovery so the mod knows when a heal actually
        /// executes (the menu's own OnDecision is native-only and cannot be hooked).
        /// Safe to call repeatedly — applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(GameManager).TypeHandle);

                // Only one QuickRecovery overload exists, so a name-only lookup is safe.
                harmony.Patch(
                    AccessTools.Method(typeof(GameManager), "QuickRecovery"),
                    postfix: new HarmonyMethod(typeof(QuickRecoveryHandler),
                        nameof(GameManager_QuickRecovery_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("QuickRecoveryHandler: patch applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"QuickRecoveryHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        /// <summary>Postfix for GameManager.QuickRecovery — flags that a heal executed.</summary>
        private static void GameManager_QuickRecovery_Postfix()
        {
            _healExecuted = true;
            _healExecutedTime = UnityEngine.Time.time;
            DebugLogger.LogState("QuickRecovery: GameManager.QuickRecovery executed.");
        }

        #endregion

        /// <summary>
        /// Called on map/scene transitions. Resets detection state and starts a short
        /// settle window so a recovery overlay lingering active with stale data after a
        /// transition is absorbed silently rather than announced. See <see cref="Update"/>.
        /// </summary>
        public void OnSceneChanged()
        {
            _selector = null;
            _wasActive = false;
            _lastChoice = UIDefine.DialogChoices.None;
            _nextFindTime = 0f;
            _settleUntil = UnityEngine.Time.time + 3f;
            _conversationWindow = null;
            _nextConversationFindTime = 0f;
        }

        /// <summary>
        /// Polls the Quick Recovery overlay each frame: announces the heading on open,
        /// the Yes/No choice on change, the party status when NumPad 0 is pressed, and
        /// the recovery result once the heal executes.
        /// </summary>
        public void Update()
        {
            // Result first — it must fire even after the menu has closed.
            if (_healExecuted)
            {
                _healExecuted = false;
                TryAnnounceResult();
            }

            // Find or verify the selector. activeInHierarchy alone is unreliable for
            // these field overlays (stays true when hidden), so also require recovery data.
            bool isActive = UiFinder.TryGetActiveOverlay(
                ref _selector, ref _nextFindTime,
                s => s.gameObject?.activeInHierarchy == true
                     && s.recoveryDataList?.Count > 0);

            // The recovery overlay stays active with populated data even while a
            // scripted event or conversation is running, which previously produced a
            // false "Quick Recovery. Recover party?..." announcement mid-cutscene
            // (the menu's own isPause flag is False during these, so it can't be
            // used). The menu is only legitimately reachable during free field
            // control, so suppress detection entirely otherwise.
            if (isActive && IsBlockedByEventOrDialogue())
            {
                isActive = false;
            }

            // Post-transition settle window: silently adopt the overlay's state so a
            // stale recovery menu lingering after a map change isn't read as a fresh
            // open (same guard as PickpocketHandler).
            if (UnityEngine.Time.time < _settleUntil)
            {
                _wasActive = isActive;
                if (isActive && _selector != null)
                {
                    try { _lastChoice = _selector.currentChoice; }
                    catch { _lastChoice = UIDefine.DialogChoices.None; }
                }
                else
                {
                    _lastChoice = UIDefine.DialogChoices.None;
                }
                return;
            }

            if (!isActive)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    _lastChoice = UIDefine.DialogChoices.None;
                }
                return;
            }

            // Keep a fresh projected-outcome snapshot for the result announcement.
            CaptureSnapshot();

            // Announce heading on open (skip first frame, like pickpocket).
            if (!_wasActive)
            {
                _wasActive = true;
                _lastChoice = UIDefine.DialogChoices.None;
                try
                {
                    UIDefine.DialogChoices choice = _selector.currentChoice;
                    _lastChoice = choice;
                    ScreenReader.Say(Loc.Get("quickheal_heading", ChoiceText(choice)));
                    DebugLogger.LogState($"QuickRecovery: menu opened. choice={choice}, "
                        + $"members={_selector.recoveryDataList?.Count ?? 0}, "
                        + $"isPause={_selector.isPause}, isDisableInput={_selector.isDisableInput}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"QuickRecovery: open error: {ex.Message}");
                }
                return;
            }

            // On-demand party status — NumPad 0 (keyboard) or L3 / left-stick click (gamepad).
            try
            {
                var kb = Keyboard.current;
                bool fromKeyboard = kb != null && kb[Key.Numpad0].wasPressedThisFrame;

                // Require L1 NOT held so plain L3 reads status while L1+L3 stays the
                // mod-menu toggle handled in Main.
                var gp = Gamepad.current;
                bool fromGamepad = gp != null
                    && gp.leftStickButton.wasPressedThisFrame
                    && !gp.leftShoulder.isPressed;

                if (fromKeyboard || fromGamepad)
                {
                    AnnouncePartyStatus();
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"QuickRecovery: status key error: {ex.Message}");
            }

            // Poll the Yes/No cursor.
            try
            {
                UIDefine.DialogChoices choice = _selector.currentChoice;
                if (choice == _lastChoice) return;
                _lastChoice = choice;

                if (choice == UIDefine.DialogChoices.Yes || choice == UIDefine.DialogChoices.No)
                {
                    ScreenReader.Say(ChoiceText(choice));
                    DebugLogger.LogState($"QuickRecovery: choice -> {choice}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"QuickRecovery: poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// True when the field is NOT in a state where the quick-recovery menu can
        /// legitimately be open — i.e. a scripted event/cutscene is running, the game
        /// is paused, or a conversation is on screen. Used to ignore the overlay's
        /// stale active+populated state during cutscenes (see <see cref="Update"/>).
        /// </summary>
        private bool IsBlockedByEventOrDialogue()
        {
            // FieldState covers the common cases: no field/player, game paused,
            // EventManager running (cutscenes/scripted scenes), camp or shop open.
            if (!FieldState.IsFieldFree()) return true;

            // Belt-and-suspenders: a conversation can be showing for a frame while
            // the field still reports free. Check the conversation window directly.
            try
            {
                if (_conversationWindow == null
                    && UnityEngine.Time.time >= _nextConversationFindTime)
                {
                    _nextConversationFindTime = UnityEngine.Time.time + 1f;
                    _conversationWindow =
                        UnityEngine.Object.FindObjectOfType<UIConversationWindow>();
                }

                if (_conversationWindow != null && _conversationWindow.IsShowingConversation)
                    return true;
            }
            catch (Exception ex)
            {
                // Window destroyed on scene change, etc. — drop the cache and treat
                // as not-blocking (FieldState already handled the important cases).
                _conversationWindow = null;
                DebugLogger.LogState($"QuickRecovery: conversation check error: {ex.Message}");
            }

            return false;
        }

        /// <summary>Maps a Yes/No dialog choice to its localized spoken label.</summary>
        private static string ChoiceText(UIDefine.DialogChoices choice)
        {
            return choice == UIDefine.DialogChoices.No
                ? Loc.Get("quickheal_no")
                : Loc.Get("quickheal_yes");
        }

        /// <summary>Resolves a party member's display name from its PlayerID.</summary>
        private static string ResolveName(PlayerID playerID)
        {
            try
            {
                var pm = ParameterManager.Instance;
                var charParam = pm?.UserParameter?.GetCharacterParameter(playerID);
                string name = charParam?.CharacterName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { /* fall through */ }

            // Fallback: title-case the enum (e.g. CLAUDE -> Claude).
            string raw = playerID.ToString();
            if (string.IsNullOrEmpty(raw)) return raw;
            return char.ToUpper(raw[0]) + raw.Substring(1).ToLower();
        }

        /// <summary>Captures the current projected-outcome snapshot from recoveryDataList.</summary>
        private void CaptureSnapshot()
        {
            try
            {
                var list = _selector?.recoveryDataList;
                int count = list?.Count ?? 0;
                if (count <= 0) return;

                var snap = new List<MemberSnap>(count);
                for (int i = 0; i < count; i++)
                {
                    var data = list[i];
                    if (data == null) continue;
                    snap.Add(new MemberSnap
                    {
                        Name = ResolveName(data.playerID),
                        Hp = data.hp, HpMax = data.hpMax, ChangeHp = data.changeHp,
                        Mp = data.mp, MpMax = data.mpMax, ChangeMp = data.changeMp,
                    });
                }
                _snapshot = snap;
                _snapshotTime = UnityEngine.Time.time;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"QuickRecovery: snapshot error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the whole party's HP/MP from recoveryDataList, including the amount
        /// each member will recover (changeHp - hp) when not at full health.
        /// </summary>
        private void AnnouncePartyStatus()
        {
            var list = _selector?.recoveryDataList;
            int count = list?.Count ?? 0;
            if (count <= 0)
            {
                ScreenReader.Say(Loc.Get("quickheal_empty"));
                return;
            }

            var members = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var data = list[i];
                if (data == null) continue;

                string name = ResolveName(data.playerID);
                int hp = data.hp, hpMax = data.hpMax, changeHp = data.changeHp;
                int mp = data.mp, mpMax = data.mpMax, changeMp = data.changeMp;

                // If at full HP and MP with nothing to recover, say so briefly.
                if (hp >= hpMax && mp >= mpMax && changeHp <= hp && changeMp <= mp)
                {
                    members.Add(Loc.Get("quickheal_status_full", name));
                    continue;
                }

                var parts = new List<string> { Loc.Get("quickheal_status_hp", name, hp, hpMax) };
                if (changeHp > hp) parts.Add(Loc.Get("quickheal_status_recovering", changeHp - hp));
                parts.Add(Loc.Get("quickheal_status_mp", mp, mpMax));
                if (changeMp > mp) parts.Add(Loc.Get("quickheal_status_recovering", changeMp - mp));

                members.Add(string.Join(", ", parts));
            }

            ScreenReader.Say(string.Join(". ", members) + ".");
            DebugLogger.LogState($"QuickRecovery: read party status ({count} members).");
        }

        /// <summary>
        /// Announces the recovery result from the last fresh snapshot: which members
        /// recovered HP and which spent MP casting (the "what was used"). Ignored if the
        /// snapshot is stale (e.g. a camp recovery fired the shared execution hook).
        /// </summary>
        private void TryAnnounceResult()
        {
            if (_snapshot == null) return;
            if (_healExecutedTime - _snapshotTime > SnapshotFreshWindow)
            {
                DebugLogger.LogState("QuickRecovery: heal executed but no fresh field snapshot — skipping result.");
                return;
            }

            var lines = new List<string> { Loc.Get("quickheal_result_heading") };
            foreach (var m in _snapshot)
            {
                if (m.ChangeHp > m.Hp)
                    lines.Add(Loc.Get("quickheal_result_hp", m.Name, m.ChangeHp));
            }
            foreach (var m in _snapshot)
            {
                if (m.ChangeMp < m.Mp)
                    lines.Add(Loc.Get("quickheal_result_used_mp", m.Name, m.Mp - m.ChangeMp));
            }

            ScreenReader.Say(string.Join(". ", lines) + ".");
            DebugLogger.LogState($"QuickRecovery: announced result ({_snapshot.Count} members).");

            _snapshot = null;
        }
    }
}
