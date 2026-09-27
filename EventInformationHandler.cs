using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the event information panel: the framed title + description the
    /// game shows during events, e.g. every North City library (Nedepedia) topic.
    ///
    /// Polling, not a hook: the panel is filled through
    /// <c>UIConversationWindow.ShowEventInformation</c>, whose Harmony postfix never
    /// fired (log 2026-09-27: nothing at all while a library topic was open — the
    /// caption methods behaved the same, see game-api.md). So each frame this reads
    /// <c>UIConversationWindow.informationSelector</c> and speaks its title and
    /// description when the panel becomes visible or its text changes while shown.
    /// </summary>
    public class EventInformationHandler
    {
        #region Fields

        /// <summary>Cached conversation window that owns the information selector.</summary>
        private UIConversationWindow _window;

        /// <summary>Whether the panel was showing last frame (edge detection).</summary>
        private bool _wasShowing;

        /// <summary>Text spoken for the panel currently shown, to speak page changes once.</summary>
        private string _lastSpoken;

        /// <summary>Throttle for FindObjectOfType while no window is cached.</summary>
        private float _findWindowTimer;
        private const float FindWindowInterval = 2f;

        #endregion

        #region Update (Polling)

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). Speaks the information
        /// panel on show and whenever its text changes while it stays open.
        /// </summary>
        public void Update()
        {
            try
            {
                if (_window == null)
                {
                    TryCacheWindow();
                    if (_window == null) return;
                }

                var selector = _window.informationSelector;
                bool showing = selector != null && selector.IsShowing;

                if (!showing)
                {
                    if (_wasShowing)
                        DebugLogger.LogState("EventInformationHandler: panel hidden.");
                    _wasShowing = false;
                    _lastSpoken = null;
                    return;
                }

                string text = ReadPanelText(selector);
                if (!_wasShowing)
                    DebugLogger.LogState("EventInformationHandler: panel shown.");
                _wasShowing = true;

                // The text can still be empty on the first visible frame; keep
                // polling until the game has filled it, then speak it once.
                if (string.IsNullOrWhiteSpace(text) || text == _lastSpoken) return;

                _lastSpoken = text;
                ScreenReader.Say(text);
                DebugLogger.LogGameValue("EventInformation", text);
            }
            catch (Exception ex)
            {
                // The window is destroyed on scene changes; drop the reference and re-find it.
                DebugLogger.LogState($"EventInformationHandler.Update: {ex.Message}");
                _window = null;
                _wasShowing = false;
                _lastSpoken = null;
            }
        }

        #endregion

        #region Helpers

        /// <summary>Finds the conversation window, throttled to avoid per-frame scene scans.</summary>
        private void TryCacheWindow()
        {
            _findWindowTimer += Time.deltaTime;
            if (_findWindowTimer < FindWindowInterval) return;
            _findWindowTimer = 0f;

            _window = UnityEngine.Object.FindObjectOfType<UIConversationWindow>();
            if (_window != null)
                DebugLogger.LogState("EventInformationHandler: found UIConversationWindow.");
        }

        /// <summary>Title and description of the panel as one spoken text, tags stripped.</summary>
        private static string ReadPanelText(uiEventInformationSelector selector)
        {
            string title = selector.title != null
                ? TextUtil.StripTags(selector.title.text ?? "") : "";
            string description = selector.description != null
                ? TextUtil.StripTags(selector.description.text ?? "") : "";
            return TextUtil.JoinSentences(new[] { title, description });
        }

        #endregion
    }
}
