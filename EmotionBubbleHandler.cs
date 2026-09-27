using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces emotion bubbles shown over party members: the heart (or its
    /// gloomy counterpart) after a private action is the game's only signal that
    /// a relationship changed (Celine fortune-teller scene 2026-09-27: sound heard,
    /// nothing spoken, nothing logged).
    ///
    /// Polling, not a hook: every <c>UIFieldController.ShowEmotion</c> overload
    /// takes <c>ref Vector3</c>, and hooking a ref IL2CPP value-type parameter
    /// crashes native code (hard project rule). So each frame the presenters in
    /// <c>UIFieldEmotionSelector.emotionPresenterList</c> are checked; a presenter
    /// that becomes active, or changes its emotion / followed object while active,
    /// is announced once. Bubbles over anything that is not a party member
    /// (alerted enemies, town NPC reactions) are logged only.
    /// </summary>
    public class EmotionBubbleHandler
    {
        #region Fields

        /// <summary>Presenter pointer → "emotion|object" signature spoken for it while active.</summary>
        private readonly Dictionary<IntPtr, string> _shown = new Dictionary<IntPtr, string>();

        /// <summary>Pointers seen active this frame; reused to avoid per-frame allocation.</summary>
        private readonly List<IntPtr> _activeNow = new List<IntPtr>();

        #endregion

        #region Update (Polling)

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). Runs during events too,
        /// because private-action bubbles appear while the scene is still playing.
        /// </summary>
        public void Update()
        {
            try
            {
                var selector = GameUIManager.Instance?.UIFieldController?.emotionSelector;
                if (selector == null)
                {
                    _shown.Clear();
                    return;
                }

                var list = selector.emotionPresenterList;
                if (list == null) return;

                _activeNow.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    var presenter = list[i];
                    if (presenter == null || presenter.gameObject == null
                        || !presenter.gameObject.activeInHierarchy)
                        continue;

                    IntPtr ptr = presenter.Pointer;
                    _activeNow.Add(ptr);

                    string target = FollowedObjectName(presenter);
                    string signature = $"{presenter.EmotionType}|{target}";
                    if (_shown.TryGetValue(ptr, out string previous) && previous == signature)
                        continue;

                    _shown[ptr] = signature;
                    Announce(presenter, target);
                }

                // Forget presenters that went inactive so a re-show speaks again.
                if (_shown.Count > 0)
                {
                    var gone = new List<IntPtr>();
                    foreach (var kv in _shown)
                        if (!_activeNow.Contains(kv.Key)) gone.Add(kv.Key);
                    foreach (var key in gone) _shown.Remove(key);
                }
            }
            catch (Exception ex)
            {
                // Presenters are destroyed on scene changes; start over next frame.
                DebugLogger.LogState($"EmotionBubbleHandler.Update: {ex.Message}");
                _shown.Clear();
            }
        }

        #endregion

        #region Helpers

        /// <summary>Speaks a party member's bubble; logs every bubble with its type.</summary>
        private static void Announce(UIFieldEmotionPresenter presenter, string target)
        {
            UIDefine.EmotionType type = presenter.EmotionType;
            FieldPlayer player = FollowedPlayer(presenter);
            PlayerID id = player != null ? PlayerIdOf(player) : PlayerID.INVALID;
            // During events a party member is a "cp_0003_01(Clone)" clone that is
            // not a FieldPlayer (Celine in the fortune-teller PA, log 10:22): the
            // four digits after "cp_" are the PlayerID.
            if (id == PlayerID.INVALID) id = PlayerIdFromCloneName(target);
            string name = id != PlayerID.INVALID ? CharacterName(id, target) : null;

            DebugLogger.LogGameValue("EmotionBubble",
                $"type={type} object='{target}' party={(name != null)} name='{name ?? ""}' " +
                $"imageVisible={presenter.isGameImageVisible} displayTime={presenter.displayTime:F1}");

            if (name == null || !ModSettings.EmotionBubblesEnabled) return;

            string label = Loc.Get("emotion_" + type.ToString().ToLowerInvariant());
            ScreenReader.Say(Loc.Get("emotion_bubble", name, label));
        }

        /// <summary>PlayerID from a party clone's GameObject name such as "cp_0003_01(Clone)".</summary>
        private static PlayerID PlayerIdFromCloneName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith("cp_")
                || objectName.Length < 7)
                return PlayerID.INVALID;
            if (!int.TryParse(objectName.Substring(3, 4), out int number)) return PlayerID.INVALID;
            var id = (PlayerID)number;
            return id > PlayerID.INVALID && id < PlayerID.MAX ? id : PlayerID.INVALID;
        }

        /// <summary>The GameObject name the bubble follows, or "?" when unknown.</summary>
        private static string FollowedObjectName(UIFieldEmotionPresenter presenter)
        {
            try
            {
                var follow = presenter.followTask?.followObjectTransform;
                return follow != null ? follow.name : "?";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>The party member the bubble follows, or null for NPCs and enemies.</summary>
        private static FieldPlayer FollowedPlayer(UIFieldEmotionPresenter presenter)
        {
            try
            {
                var follow = presenter.followTask?.followObjectTransform;
                return follow != null ? follow.GetComponentInParent<FieldPlayer>() : null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"EmotionBubbleHandler.FollowedPlayer: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The PlayerID of a field player: the CharacterID on its character
        /// parameter, else the GameObject name ("CELINE" is the enum name).
        /// </summary>
        private static PlayerID PlayerIdOf(FieldPlayer player)
        {
            PlayerID id = PlayerID.INVALID;
            try
            {
                var param = player.CharacterParameter;
                if (param != null) id = (PlayerID)param.CharacterID;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"EmotionBubbleHandler.PlayerIdOf: {ex.Message}");
            }
            if (id <= PlayerID.INVALID || id >= PlayerID.MAX)
            {
                try { Enum.TryParse(player.name, true, out id); } catch { id = PlayerID.INVALID; }
            }
            return id > PlayerID.INVALID && id < PlayerID.MAX ? id : PlayerID.INVALID;
        }

        /// <summary>The character's first name from the game's parameter table, else the object name.</summary>
        private static string CharacterName(PlayerID id, string objectName)
        {
            string name = null;
            try { name = ParameterManager.Instance?.GetCharacterFirstName(id); }
            catch (Exception ex)
            {
                DebugLogger.LogState($"EmotionBubbleHandler.CharacterName lookup: {ex.Message}");
            }
            return string.IsNullOrWhiteSpace(name) ? objectName : name;
        }

        #endregion
    }
}
