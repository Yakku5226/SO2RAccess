using Il2CppGame;
using System;
using System.Text;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The fishing bubble: the world-space icon (no text) the game shows above
    /// the player exactly while fishing can be started. It is native-driven UI
    /// (ShowFieldIcon has a ref Vector3 param, unhookable), so the icon
    /// presenters are polled each frame. Announced with the bubble sound, once
    /// per approach; also the arrival truth for world-map fishing auto-walk and
    /// the source of the remembered bubble points.
    /// </summary>
    public partial class FieldPromptHandler
    {
        #region Fields

        /// <summary>True while the fishing bubble is showing (announce-once edge state).</summary>
        private static bool _fishShowing = false;

        /// <summary>
        /// True while the game shows its fishing bubble — the world-space icon above the
        /// player's head that means "press the action button to fish". World-map auto-walk
        /// to a fishing spot treats this as an authoritative arrival signal. Detected by
        /// POLLING the UIFieldIconSelector's presenters for a visible FieldIconType.Fishing
        /// sprite each frame: the bubble is shown via ShowFieldIcon(..., ref Vector3, ...),
        /// which cannot be hooked (ref IL2CPP value-type param = native crash). The earlier
        /// FieldManager.GetContactFishingWaterPlaceID poll was proven WRONG 2026-08-29: it
        /// is contact with the water-place VOLUME (some span 200m+ over land — one overlaps
        /// the Krosse City exit), not "can fish now", causing false prompts and false
        /// auto-walk arrivals.
        /// </summary>
        public static bool FishPromptShowing => _fishShowing;

        /// <summary>Cached world-space icon selector that draws the fishing bubble.</summary>
        private static UIFieldIconSelector _iconSelector = null;

        /// <summary>Next allowed FindObjectOfType time for the icon selector (throttle).</summary>
        private static float _iconSelectorNextFindTime = 0f;

        /// <summary>Seconds between icon-selector find attempts while it is unresolved.</summary>
        private const float IconSelectorFindInterval = 2f;

        /// <summary>Instance ID of the fishing icon sprite, 0 while unresolved.</summary>
        private static int _fishingSpriteId = 0;

        /// <summary>
        /// True after the bubble was announced for the current approach. The game
        /// BLINKS the bubble (hides/re-shows it in cycles while the player stands still —
        /// observed 2026-08-29), so each re-show must not re-announce. Cleared only once
        /// the player moves away from the announcement position (see
        /// <see cref="FishReannounceDistance"/>), so a genuine re-approach announces again.
        /// </summary>
        private static bool _fishAnnounceLatched = false;

        /// <summary>Player position at the last fishing announcement.</summary>
        private static Vector3 _fishAnnouncePos;

        /// <summary>Meters the player must move from the announcement position before
        /// the bubble may announce again (blink-proofing, not a rate limit).</summary>
        private const float FishReannounceDistance = 3f;

        /// <summary>Debug-only: last logged fishing diagnostic state (log-on-change).</summary>
        private static string _lastFishDiagSignature = "";

        private static bool _fishParamsLogged;

        #endregion

        #region Poll

        /// <summary>
        /// Per-frame poll of the game's fishing bubble. Edge-triggered: announces
        /// once when the bubble appears, clears when it hides. In debug mode, every
        /// change of bubble/contact/visible-icon state is logged for evidence.
        /// </summary>
        private static void UpdateFishingBubblePoll()
        {
            bool bubble = IsFishingBubbleShowing(out string visibleIcons);

            if (Main.DebugMode)
                LogFishingDiag(bubble, visibleIcons);

            // Re-arm the announcement once the player has left the spot: the game
            // blinks the bubble while standing still, so hiding alone must NOT
            // re-arm — only real movement away from where it was announced.
            if (!bubble && _fishAnnounceLatched &&
                TryGetPlayerPos(out var pos) &&
                (pos - _fishAnnouncePos).sqrMagnitude >
                    FishReannounceDistance * FishReannounceDistance)
            {
                _fishAnnounceLatched = false;
                DebugLogger.LogState(
                    "FieldPrompt: fishing announce re-armed (moved away).");
            }

            if (bubble == _fishShowing) return;
            _fishShowing = bubble;

            if (bubble)
            {
                if (_fishAnnounceLatched)
                {
                    // A blink re-show at the same spot — stay quiet.
                    DebugLogger.LogState(
                        "FieldPrompt: fishing bubble re-shown (blink), announce suppressed.");
                    return;
                }

                // Bubble sound instead of speech (user decision 2026-08-30);
                // speech only as fallback when the WAV is missing/unparseable so
                // the prompt never goes silent by accident.
                if (AudioCuePlayer.IsFishPromptSoundLoaded)
                {
                    if (ModSettings.FishPromptSoundEnabled)
                        AudioCuePlayer.PlayFishPromptCue();
                }
                else if (ModSettings.PromptSpeechEnabled)
                {
                    ScreenReader.Say(Loc.Get("fish_prompt"));
                }
                _fishAnnounceLatched = true;
                if (!TryGetPlayerPos(out _fishAnnouncePos))
                    _fishAnnouncePos = Vector3.zero;
                DebugLogger.LogGameValue("FieldPrompt", "fishing bubble shown");
                RememberWorldmapBubble();
            }
            else
            {
                DebugLogger.LogState("FieldPrompt: fishing bubble hidden.");
            }
        }

        /// <summary>
        /// World map only: saves where the bubble just appeared so later walks to this
        /// water go straight there (<see cref="WorldmapBubbleMemory"/>).
        /// </summary>
        private static void RememberWorldmapBubble()
        {
            try
            {
                var fm = FieldManager.Instance;
                var player = fm?.GetControlPlayer();
                if (player == null || !fm.IsWorldmap()) return;
                WorldmapBubbleMemory.Record(fm.WorldmapID, fm.GetContactFishingWaterPlaceID(),
                    player.transform.position, player.transform.forward);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FieldPrompt: bubble memory failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the control player's world position. False during scene
        /// transitions or when no player exists.
        /// </summary>
        private static bool TryGetPlayerPos(out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player == null) return false;
                pos = player.transform.position;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True while any world-space icon presenter is visibly showing the fishing
        /// sprite. Outputs the names of all visible icon sprites in debug mode (for
        /// cataloguing — empty otherwise) so a wrong sprite-index assumption shows up
        /// as log evidence instead of silence.
        /// </summary>
        private static bool IsFishingBubbleShowing(out string visibleIcons)
        {
            visibleIcons = "";
            var sel = GetIconSelector();
            if (sel == null || _fishingSpriteId == 0) return false;

            bool fishing = false;
            StringBuilder catalog = Main.DebugMode ? new StringBuilder() : null;

            try
            {
                var list = sel.iconPresenterList;
                if (list == null) return false;

                for (int i = 0; i < list.Count; i++)
                {
                    var presenter = list[i];
                    if (!IsPresenterVisible(presenter)) continue;

                    var img = presenter.icon;
                    var sprite = img != null ? img.sprite : null;
                    if (sprite == null) continue;

                    if (sprite.GetInstanceID() == _fishingSpriteId)
                        fishing = true;

                    if (catalog != null)
                    {
                        if (catalog.Length > 0) catalog.Append(", ");
                        catalog.Append(sprite.name);
                    }
                    else if (fishing)
                    {
                        break;  // no catalog wanted — first hit is enough
                    }
                }
            }
            catch
            {
                // Presenters destroyed mid-transition — treat as not showing this frame.
                return false;
            }

            if (catalog != null) visibleIcons = catalog.ToString();
            return fishing;
        }

        /// <summary>
        /// True if the icon presenter is actually visible on screen: active in the
        /// hierarchy and not faded out by its canvas group. Any IL2CPP access failure
        /// (destroyed object) is treated as "not visible".
        /// </summary>
        private static bool IsPresenterVisible(UIFieldIconPresenter presenter)
        {
            try
            {
                if (presenter == null) return false;
                if (!presenter.gameObject.activeInHierarchy) return false;

                var canvasGroup = presenter.canvasGroup;
                if (canvasGroup != null && canvasGroup.alpha < 0.5f) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the cached UIFieldIconSelector, re-finding it (throttled) after scene
        /// changes destroy it. Caches the fishing sprite's instance ID alongside — the
        /// sprite at index FieldIconType.Fishing of the selector's sprite list.
        /// </summary>
        private static UIFieldIconSelector GetIconSelector()
        {
            try
            {
                // Touching gameObject validates the cached instance; a destroyed
                // selector throws and falls through to the re-find below.
                if (_iconSelector != null && _iconSelector.gameObject != null)
                    return _iconSelector;
            }
            catch
            {
                _iconSelector = null;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _iconSelectorNextFindTime) return null;
            _iconSelectorNextFindTime = now + IconSelectorFindInterval;

            try
            {
                // includeInactive: the selector may be disabled while no icon shows.
                _iconSelector =
                    UnityEngine.Object.FindObjectOfType<UIFieldIconSelector>(true);
                if (_iconSelector == null) return null;

                _fishingSpriteId = 0;
                var sprites = _iconSelector.spriteList;
                int fishingIndex = (int)UIDefine.FieldIconType.Fishing;
                if (sprites != null && sprites.Count > fishingIndex &&
                    sprites[fishingIndex] != null)
                {
                    _fishingSpriteId = sprites[fishingIndex].GetInstanceID();
                }

                DebugLogger.LogState(
                    $"FieldPrompt: UIFieldIconSelector cached " +
                    $"(sprites={(sprites != null ? sprites.Count : -1)}, " +
                    $"fishingSpriteId={_fishingSpriteId}).");
                if (_fishingSpriteId == 0)
                    DebugLogger.LogState(
                        "FieldPrompt: fishing sprite NOT resolved — bubble " +
                        "detection inactive (sprite list too short or null entry).");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"FieldPrompt: icon selector find failed: {ex.Message}");
                _iconSelector = null;
            }
            return _iconSelector;
        }

        #endregion

        #region Debug

        /// <summary>
        /// Debug-only, log-on-change: correlates the bubble state with the old
        /// water-place contact signal and the visible icon sprites, plus the player
        /// position — the evidence trail for tuning the bubble detection.
        /// </summary>
        private static void LogFishingDiag(bool bubble, string visibleIcons)
        {
            int contactId = 0;
            string gameCheck = "n/a";
            try
            {
                var fm = FieldManager.Instance;
                var player = fm?.GetControlPlayer();
                if (player != null)
                {
                    contactId = fm.GetContactFishingWaterPlaceID();
                    // The game's own per-frame world map test from the player's
                    // feet and facing (2026-09-06: does it agree with the bubble?).
                    if (fm.IsWorldmap())
                    {
                        Vector3 feet = player.transform.position;
                        Vector3 forward = player.transform.forward;
                        gameCheck = fm.CheckWorldmapFishingPoint(ref feet, ref forward).ToString();
                        // The game's own player-based entry (2026-09-09): does it
                        // agree with the feet+forward call, which flickered?
                        // Not asked on a mount: the game's check throws there
                        // every frame (bunny, log 2026-09-26 15:35).
                        if (WorldmapTravel.CurrentMode() != WorldmapTravelMode.Foot)
                            gameCheck += "/p-mounted";
                        else
                        {
                            try { gameCheck += "/p" + fm.CheckFishingPoint(player); }
                            catch (Exception ex) { gameCheck += "/p-err:" + ex.Message; }
                        }
                    }
                    if (!_fishParamsLogged)
                    {
                        _fishParamsLogged = true;
                        DebugLogger.LogGameValue("FieldPrompt",
                            $"FISHPARAMS wmCharacterHeight={fm.WorldmapFishingCharacterHeight:F2} " +
                            $"wmFrontDistance={fm.WorldmapFishingFrontDistance:F2} " +
                            $"groundDistance={fm.FishingGroundDistance:F2} " +
                            $"collisionDistanceRate={fm.FishingCollisionDistanceRate:F2}");
                    }
                }
            }
            catch (Exception ex)
            {
                gameCheck = "error:" + ex.Message;
            }

            string signature = $"{bubble}|{contactId}|{gameCheck}|{visibleIcons}";
            if (signature == _lastFishDiagSignature) return;
            _lastFishDiagSignature = signature;

            string pos = TryGetPlayerPos(out var p)
                ? $"({p.x:F1},{p.y:F1},{p.z:F1})" : "?";

            DebugLogger.LogGameValue("FieldPrompt",
                $"FISHDIAG bubble={bubble} contactID={contactId} gameCheck={gameCheck} " +
                $"icons=[{visibleIcons}] pos={pos}");
        }

        #endregion
    }
}
