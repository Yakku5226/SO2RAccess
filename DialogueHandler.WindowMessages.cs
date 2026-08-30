using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the on-screen messages UIConversationWindow shows OUTSIDE the normal
    /// dialogue text box (game-api.md section 19) — previously missed entirely:
    ///   - SetConversationAutoMessage: timed ambient bubbles over NPCs/objects
    ///   - SetConversationMessageFollowObject: bubbles following a field object
    ///   - ShowCenterMessage / ShowEntireMessage: center-screen and full-screen text
    ///   - ShowEventInformation: event info panel (title + description)
    ///
    /// These methods receive a messageID, resolved via TextManager (System → Skill →
    /// Item tables). Unresolvable IDs are suppressed rather than spoken as raw keys.
    ///
    /// SAFETY: UIConversationWindow also has overloads of these methods taking
    /// <c>ref Vector3</c> — hooking those crashes native code (known rule). Only the
    /// plain-parameter overloads below are patched; the reference-mod analysis
    /// confirmed they carry the game's actual traffic.
    /// </summary>
    public partial class DialogueHandler
    {
        #region Window Message Fields

        // Dedupe: ambient bubbles can re-fire the same text in quick succession.
        private static string _lastWindowMessage = "";
        private static float _lastWindowMessageTime = -999f;
        private const float WindowMessageDedupeWindow = 1.0f;

        #endregion

        #region Window Message Patches

        /// <summary>
        /// Applies the UIConversationWindow message patches. Called from ApplyPatches.
        /// </summary>
        private void ApplyWindowMessagePatches(HarmonyLib.Harmony harmony)
        {
            RuntimeHelpers.RunClassConstructor(typeof(UIConversationWindow).TypeHandle);

            harmony.Patch(
                AccessTools.Method(typeof(UIConversationWindow), "SetConversationAutoMessage",
                    new Type[] { typeof(string), typeof(string), typeof(float), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(DialogueHandler),
                    nameof(WindowMessageID_Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(UIConversationWindow), "SetConversationMessageFollowObject",
                    new Type[] { typeof(string), typeof(string) }),
                postfix: new HarmonyMethod(typeof(DialogueHandler),
                    nameof(WindowMessageID_Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(UIConversationWindow), "ShowCenterMessage",
                    new Type[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(DialogueHandler),
                    nameof(WindowMessageID_Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(UIConversationWindow), "ShowEntireMessage",
                    new Type[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(DialogueHandler),
                    nameof(WindowMessageID_Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(UIConversationWindow), "ShowEventInformation",
                    new Type[] { typeof(string), typeof(string) }),
                postfix: new HarmonyMethod(typeof(DialogueHandler),
                    nameof(ShowEventInformation_Postfix))
            );

            DebugLogger.LogState("DialogueHandler: window message patches applied.");
        }

        /// <summary>
        /// Shared postfix for all messageID-based window messages. Harmony matches the
        /// first parameter by name, so every hooked overload lands here.
        /// </summary>
        private static void WindowMessageID_Postfix(string messageID)
        {
            try
            {
                if (string.IsNullOrEmpty(messageID)) return;

                string resolved = ResolveWindowMessage(messageID);
                if (resolved == null)
                {
                    DebugLogger.LogState(
                        $"DialogueHandler: window message '{messageID}' unresolved — suppressed.");
                    return;
                }

                SpeakWindowMessage(resolved);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueHandler.WindowMessageID_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Event info panel: title and description arrive as either display text or
        /// messageIDs — resolve what resolves, keep the rest verbatim.
        /// </summary>
        private static void ShowEventInformation_Postfix(string title, string description)
        {
            try
            {
                string titleText = ResolveWindowMessage(title) ?? TextUtil.StripTags(title);
                string descText = ResolveWindowMessage(description) ?? TextUtil.StripTags(description);

                string combined = TextUtil.JoinSentences(new[] { titleText, descText });
                if (string.IsNullOrWhiteSpace(combined)) return;

                SpeakWindowMessage(combined + ".");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueHandler.ShowEventInformation_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Window Message Helpers

        /// <summary>
        /// Resolves a messageID through the TextManager tables (System, Skill, Item).
        /// Returns the cleaned display text, or null when no table resolves it
        /// (GetMessage echoes the key back for unknown IDs).
        /// </summary>
        private static string ResolveWindowMessage(string messageID)
        {
            if (string.IsNullOrEmpty(messageID)) return null;

            var tm = TextManager.Instance;
            if (tm == null) return null;

            foreach (var type in new[]
            {
                TextManager.MessageType.System,
                TextManager.MessageType.Skill,
                TextManager.MessageType.Item,
            })
            {
                string text = tm.GetMessage(messageID, type);
                if (!string.IsNullOrEmpty(text) && text != messageID)
                    return TextUtil.StripTags(text);
            }
            return null;
        }

        /// <summary>Speaks a window message once, dropping quick repeats of the same text.</summary>
        private static void SpeakWindowMessage(string text)
        {
            if (text == _lastWindowMessage
                && Time.unscaledTime - _lastWindowMessageTime < WindowMessageDedupeWindow)
            {
                DebugLogger.LogState($"DialogueHandler: window message duplicate dropped: '{text}'");
                return;
            }
            _lastWindowMessage = text;
            _lastWindowMessageTime = Time.unscaledTime;

            ScreenReader.Say(text);
            DebugLogger.LogGameValue("DialogueHandler.windowMessage", text);
        }

        #endregion
    }
}
