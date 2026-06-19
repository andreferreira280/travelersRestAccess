using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TravellersRestAccess
{
    /// <summary>
    /// Announces story/NPC dialogue text as it appears, and lets the player advance/re-read
    /// it with the keyboard.
    ///
    /// The dialogue box isn't tracked by MainUI's window stack (confirmed live: no "UI
    /// opened" log for it during the whole intro), so this scans the scene directly for
    /// changing text instead of going through MainUI/UIWindow.
    ///
    /// Live testing identified the real structure:
    /// - "Dialogue UI Intro Variant/.../Subtitle Panel/Subtitle Text" - spoken conversation
    ///   lines (advanced manually via a "Continue Button", a UIButtonExtended : Button).
    /// - "Intro Canvas/Intro/Story Parent/Text Panel/Subititles" - the legend/narration
    ///   track, which auto-advances on its own and can be skipped by holding ESC (the game's
    ///   own hint, not ours).
    /// - ".../Bubble Template Standard Bark UI/.../Text (TMP)" - ambient NPC barks (small
    ///   talk from other characters wandering around, not part of the main story).
    ///
    /// Waits for text to stop changing for a short moment before announcing, so a
    /// typewriter/letter-by-letter reveal effect doesn't spam partial words.
    /// </summary>
    public class DialogueAnnouncer
    {
        private const float StabilizeDelay = 0.4f;

        // Known HUD noise picked up by the global scan, confirmed via live log:
        // in-game clock ("06:05"), day counter ("Seg. 1"), action-bar prompts ("[E] ...").
        private static readonly Regex ClockPattern = new Regex(@"^\d{1,2}:\d{2}$");
        private static readonly Regex DayCounterPattern = new Regex(@"^[A-Za-zÀ-ÿ]{3}\.\s*\d+$");
        private static readonly Regex ActionPromptPattern = new Regex(@"^\[\w\]");

        private readonly Dictionary<int, string> _lastAnnounced = new Dictionary<int, string>();
        private readonly Dictionary<int, (string text, float since)> _pending = new Dictionary<int, (string text, float since)>();
        private readonly HashSet<int> _loggedClickables = new HashSet<int>();
        // Tracked separately so re-reading one doesn't lose the other - confirmed live that
        // a bark arriving right after a story line was overwriting it, making Up/Down only
        // ever repeat the bark.
        private string _lastStoryMessage;
        private string _lastAmbientMessage;

        public void Update(int activeMenuItemCount)
        {
            // When a real navigable menu is open (Options, Save, Character Creator, etc.),
            // KeyboardUINavigator already owns announcements for it - scanning here too
            // caused duplicate readouts (confirmed live: "Carregar. Novo" and the whole
            // Character Creator screen got read as one blob on top of the normal per-item
            // announcements). Only scan/announce when nothing else is handling the screen.
            if (activeMenuItemCount > 0) return;

            ScanAndAnnounceText();
            HandleAdvanceAndRereadInput();

            // Diagnostic only: log every clickable element we can find (not just Button),
            // to spot future custom controls.
            if (Main.DebugMode)
            {
                LogStrayClickables();
            }
        }

        private void ScanAndAnnounceText()
        {
            var labels = Object.FindObjectsOfType<TextMeshProUGUI>()
                .Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text));

            var ready = new List<(float y, string text, string path)>();

            foreach (var label in labels)
            {
                int id = label.GetInstanceID();
                string clean = UITextExtractor.GetReadableText(label);
                if (string.IsNullOrEmpty(clean) || clean.Length < 2) continue;
                if (IsKnownHudNoise(clean)) continue;

                _lastAnnounced.TryGetValue(id, out var alreadyAnnounced);
                if (clean == alreadyAnnounced) continue;

                if (!_pending.TryGetValue(id, out var pending) || pending.text != clean)
                {
                    _pending[id] = (clean, Time.unscaledTime);
                    continue;
                }

                if (Time.unscaledTime - pending.since >= StabilizeDelay)
                {
                    _lastAnnounced[id] = clean;
                    _pending.Remove(id);
                    ready.Add((label.transform.position.y, clean, HierarchyPath(label.transform)));
                }
            }

            if (ready.Count == 0) return;

            var ordered = ready.OrderByDescending(r => r.y).ToList();
            var storyEntries = ordered.Where(r => !r.path.Contains("Bark UI")).ToList();
            var ambientEntries = ordered.Where(r => r.path.Contains("Bark UI")).ToList();

            string storyPart = storyEntries.Count > 0 ? string.Join(". ", storyEntries.Select(r => r.text)) : null;
            string ambientPart = ambientEntries.Count > 0 ? string.Join(". ", ambientEntries.Select(r => $"Conversa ao redor: {r.text}")) : null;

            string message = string.Join(". ", new[] { storyPart, ambientPart }.Where(s => s != null));
            ScreenReader.Announce(message);

            if (storyPart != null) _lastStoryMessage = storyPart;
            if (ambientPart != null) _lastAmbientMessage = ambientPart;

            foreach (var entry in ordered)
            {
                DebugLogger.LogState($"Dialogue text: \"{entry.text}\" path={entry.path}");
            }
        }

        private void LogStrayClickables()
        {
            foreach (var clickable in Object.FindObjectsOfType<MonoBehaviour>().OfType<IPointerClickHandler>())
            {
                var behaviour = (MonoBehaviour)clickable;
                if (behaviour == null || !behaviour.gameObject.activeInHierarchy) continue;
                int id = behaviour.GetInstanceID();
                if (_loggedClickables.Contains(id)) continue;
                _loggedClickables.Add(id);

                DebugLogger.LogState($"DialogueAnnouncer: stray clickable \"{behaviour.gameObject.name}\" ({behaviour.GetType().Name}) path={HierarchyPath(behaviour.transform)}");
            }
        }

        private void HandleAdvanceAndRereadInput()
        {
            // Up re-reads the main story/conversation line; Down re-reads the last ambient
            // bark - kept separate so one doesn't clobber the other (see field comments).
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (!string.IsNullOrEmpty(_lastStoryMessage))
                {
                    DebugLogger.LogInput("Up", "Re-read story dialogue");
                    ScreenReader.Announce(_lastStoryMessage);
                }
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (!string.IsNullOrEmpty(_lastAmbientMessage))
                {
                    DebugLogger.LogInput("Down", "Re-read ambient dialogue");
                    ScreenReader.Announce(_lastAmbientMessage);
                }
                return;
            }

            // Enter is already used by the game elsewhere (confirmed in game-api.md), which
            // made it unreliable for advancing here - Space is free and tested working.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Button continueButton = FindActiveContinueButton();
                if (continueButton != null)
                {
                    DebugLogger.LogInput("Space", "Advance dialogue");
                    continueButton.onClick.Invoke();
                }
            }
        }

        private static Button FindActiveContinueButton()
        {
            foreach (var button in Object.FindObjectsOfType<Button>())
            {
                if (button.gameObject.name == "Continue Button" && button.gameObject.activeInHierarchy && button.interactable)
                {
                    return button;
                }
            }
            return null;
        }

        private static bool IsKnownHudNoise(string clean)
        {
            return ClockPattern.IsMatch(clean) || DayCounterPattern.IsMatch(clean) || ActionPromptPattern.IsMatch(clean);
        }

        private static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            int depth = 0;
            while (t != null && depth < 6)
            {
                parts.Add(t.name);
                t = t.parent;
                depth++;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
