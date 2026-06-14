using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces dialogue choice menus to the screen reader. These appear during
    /// NPC interactions (inns, shops asking yes/no), private actions, story events,
    /// and other dialogue sequences when the player must choose a response.
    ///
    /// Uses polling + hook pattern:
    ///   Polling (primary) — each frame, checks whether the UISelectChoiceSelector's
    ///       presenter became visible. This catches ALL choice menus including those
    ///       opened entirely from native code (inns, some story events).
    ///   ShowSelectChoiceMessage hook (bonus) — fires for some choice menus opened
    ///       from managed code. Provides the title/prompt text when available.
    ///   Index polling — tracks selectChoiceIndex changes each frame since navigation
    ///       is native-only (no Harmony hooks fire for cursor movement).
    /// </summary>
    public class DialogueChoiceHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        /// <summary>Cached conversation window for accessing the choice selector.</summary>
        private UIConversationWindow _window;

        /// <summary>Cached selector reference from the conversation window.</summary>
        private static UISelectChoiceSelector _selector;

        /// <summary>Cached choice texts for the current menu.</summary>
        private static string[] _choiceTexts;

        /// <summary>Last announced choice index (-1 = none).</summary>
        private static int _lastIndex = -1;

        /// <summary>Whether the choice menu is currently active.</summary>
        private static bool _isActive = false;

        /// <summary>Whether the presenter was visible last frame (for edge detection).</summary>
        private bool _wasPresenterVisible = false;

        /// <summary>Title/prompt text set by hook, consumed on next activation.</summary>
        private static string _pendingTitle;

        /// <summary>
        /// When true, the presenter just became visible and we are waiting one frame
        /// for the game to set the correct selectChoiceIndex before announcing.
        /// </summary>
        private bool _activationPending = false;

        /// <summary>Title captured at the moment activation was deferred.</summary>
        private string _deferredTitle;

        /// <summary>Throttle FindObjectOfType calls.</summary>
        private float _findWindowTimer = 0f;
        private const float FindWindowInterval = 2f;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for dialogue choice announcements.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UISelectChoiceSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISelectChoicePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIChoicePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIConversationWindow).TypeHandle);

                // Fires when a dialogue choice menu opens with a title prompt.
                // CallerCount(5) — hookable. Used to capture title text.
                harmony.Patch(
                    AccessTools.Method(typeof(UISelectChoiceSelector),
                        nameof(UISelectChoiceSelector.ShowSelectChoiceMessage),
                        new Type[]
                        {
                            typeof(string),
                            typeof(Il2CppSystem.Collections.Generic.List<string>)
                        }),
                    postfix: new HarmonyMethod(typeof(DialogueChoiceHandler),
                        nameof(ShowSelectChoiceMessage_Postfix))
                );

                // Also hook the overload without title (CallerCount 0, but may
                // be called from native — still safe to patch as a postfix).
                harmony.Patch(
                    AccessTools.Method(typeof(UISelectChoiceSelector),
                        nameof(UISelectChoiceSelector.ShowSelectChoiceMessage),
                        new Type[]
                        {
                            typeof(Il2CppSystem.Collections.Generic.List<string>)
                        }),
                    postfix: new HarmonyMethod(typeof(DialogueChoiceHandler),
                        nameof(ShowSelectChoiceMessageNoTitle_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("DialogueChoiceHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"DialogueChoiceHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for ShowSelectChoiceMessage(string message, List&lt;string&gt; choiceMessageList).
        /// Captures title text for the next activation. Polling handles the actual announcement.
        /// </summary>
        private static void ShowSelectChoiceMessage_Postfix(
            UISelectChoiceSelector __instance, string message)
        {
            try
            {
                _pendingTitle = message;
                DebugLogger.LogState($"DialogueChoiceHandler: hook captured title='{message}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ShowSelectChoiceMessage_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ShowSelectChoiceMessage(List&lt;string&gt; choiceMessageList).
        /// Fires when a choice menu opens without a title.
        /// </summary>
        private static void ShowSelectChoiceMessageNoTitle_Postfix(
            UISelectChoiceSelector __instance)
        {
            try
            {
                _pendingTitle = null;
                DebugLogger.LogState("DialogueChoiceHandler: hook captured (no title).");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ShowSelectChoiceMessageNoTitle_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Update (Polling)

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). Detects choice menu activation
        /// via presenter visibility polling, then tracks index changes for navigation.
        /// </summary>
        public void Update()
        {
            try
            {
                // Ensure we have the selector cached.
                if (_selector == null)
                {
                    TryCacheSelector();
                    if (_selector == null) return;
                }

                // Check presenter visibility.
                UISelectChoicePresenter presenter = null;
                bool presenterVisible = false;
                try
                {
                    presenter = _selector.selectChoicePresenter;
                    presenterVisible = presenter != null
                        && presenter.gameObject != null
                        && presenter.gameObject.activeInHierarchy;
                }
                catch
                {
                    // Selector may have been destroyed.
                    _selector = null;
                    _window = null;
                    _isActive = false;
                    _wasPresenterVisible = false;
                    return;
                }

                // Edge detection: presenter just became visible → defer activation by one frame.
                // selectChoiceIndex is stale on the first visible frame (holds value from
                // the previous menu). Waiting one frame lets the game reset it to 0.
                if (presenterVisible && !_wasPresenterVisible)
                {
                    _activationPending = true;
                    _deferredTitle = _pendingTitle;
                    _pendingTitle = null;
                    DebugLogger.LogState("DialogueChoiceHandler: presenter visible, deferring activation by 1 frame.");
                }
                // Second frame after presenter appeared → now activate with correct index.
                else if (presenterVisible && _activationPending)
                {
                    _activationPending = false;
                    ActivateChoiceMenu(_deferredTitle);
                    _deferredTitle = null;
                }

                // Edge detection: presenter just became hidden → choice menu closed.
                if (!presenterVisible && _wasPresenterVisible)
                {
                    _isActive = false;
                    _activationPending = false;
                    _choiceTexts = null;
                    _lastIndex = -1;
                    DebugLogger.LogState("DialogueChoiceHandler: choice menu closed.");
                }

                _wasPresenterVisible = presenterVisible;

                // If active, poll for index changes.
                if (!_isActive) return;

                int currentIndex = _selector.selectChoiceIndex;
                if (currentIndex == _lastIndex) return;

                _lastIndex = currentIndex;

                string choiceText = GetChoiceText(currentIndex);
                if (string.IsNullOrEmpty(choiceText)) return;

                int total = _choiceTexts?.Length ?? 0;
                string announcement = Loc.Get("dialogue_choice_item",
                    choiceText, currentIndex + 1, total);

                ScreenReader.Say(announcement);
                DebugLogger.LogGameValue("DialogueChoice",
                    $"index={currentIndex} text='{choiceText}' total={total}");
            }
            catch (Exception ex)
            {
                _isActive = false;
                _activationPending = false;
                _selector = null;
                _window = null;
                _wasPresenterVisible = false;
                DebugLogger.LogState($"DialogueChoiceHandler.Update: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Tries to find and cache the UISelectChoiceSelector from the conversation window.
        /// Throttled to avoid expensive FindObjectOfType calls every frame.
        /// </summary>
        private void TryCacheSelector()
        {
            _findWindowTimer += Time.deltaTime;
            if (_findWindowTimer < FindWindowInterval) return;
            _findWindowTimer = 0f;

            try
            {
                if (_window == null)
                {
                    _window = UnityEngine.Object.FindObjectOfType<UIConversationWindow>();
                    if (_window == null) return;
                    DebugLogger.LogState("DialogueChoiceHandler: found UIConversationWindow.");
                }

                _selector = _window.selectChoiceSelector;
                if (_selector != null)
                    DebugLogger.LogState("DialogueChoiceHandler: cached selectChoiceSelector.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueChoiceHandler.TryCacheSelector: {ex.Message}");
                _window = null;
            }
        }

        /// <summary>
        /// Reads choice texts from the presenter and announces the choice menu opening
        /// with the initial selection. Called when the presenter becomes visible.
        /// </summary>
        private void ActivateChoiceMenu(string title)
        {
            _isActive = true;

            // Read choice texts from the presenter's choice presenter list.
            ReadChoiceTexts();

            // Announce heading + initial item as one combined string so the
            // screen reader doesn't interrupt the heading with the item.
            int total = _choiceTexts?.Length ?? 0;
            int initialIndex = _selector.selectChoiceIndex;
            string initialChoice = GetChoiceText(initialIndex);
            string cleanTitle = !string.IsNullOrEmpty(title)
                ? NotificationHandler.StripTagsPublic(title) : "";

            string announcement;
            if (!string.IsNullOrEmpty(cleanTitle) && !string.IsNullOrEmpty(initialChoice))
            {
                announcement = Loc.Get("dialogue_choice_open_with_title",
                    cleanTitle, total, initialChoice, initialIndex + 1);
            }
            else if (!string.IsNullOrEmpty(initialChoice))
            {
                announcement = Loc.Get("dialogue_choice_open",
                    total, initialChoice, initialIndex + 1);
            }
            else if (!string.IsNullOrEmpty(cleanTitle))
            {
                announcement = Loc.Get("dialogue_choice_open_no_items", cleanTitle, total);
            }
            else
            {
                announcement = Loc.Get("dialogue_choice_open_no_items", "", total);
            }

            _lastIndex = initialIndex;

            ScreenReader.Say(announcement);
            DebugLogger.LogGameValue("DialogueChoiceOpen",
                $"title='{cleanTitle}' choices={total} initial={initialIndex}");
        }

        /// <summary>
        /// Reads all choice display texts from the presenter's choicePresenterList.
        /// </summary>
        private void ReadChoiceTexts()
        {
            _choiceTexts = null;

            if (_selector == null) return;
            var presenter = _selector.selectChoicePresenter;
            if (presenter == null) return;

            var list = presenter.choicePresenterList;
            if (list == null || list.Count == 0) return;

            // choiceMessageIDList.Count gives the actual number of active choices.
            // MaxChoiceIndex / choicePresenterList.Count return pre-allocated slots.
            int count = 0;
            try
            {
                var idList = _selector.choiceMessageIDList;
                if (idList != null) count = idList.Count;
            }
            catch { /* fallback below */ }

            if (count <= 0 || count > list.Count) count = list.Count;

            _choiceTexts = new string[count];
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var choicePresenter = list[i];
                    if (choicePresenter?.message != null)
                    {
                        string text = choicePresenter.message.text ?? "";
                        _choiceTexts[i] = NotificationHandler.StripTagsPublic(text);
                    }
                    else
                    {
                        _choiceTexts[i] = "";
                    }
                }
                catch
                {
                    _choiceTexts[i] = "";
                }
            }
        }

        /// <summary>
        /// Returns the choice text for the given index, or empty string if out of range.
        /// </summary>
        private static string GetChoiceText(int index)
        {
            if (_choiceTexts == null || index < 0 || index >= _choiceTexts.Length)
                return "";
            return _choiceTexts[index];
        }

        #endregion
    }
}
