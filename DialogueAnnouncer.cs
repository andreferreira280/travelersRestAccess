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

        // Confirmed live: starting a New Game (still inside the title screen scene, no
        // actual Unity scene change) caused a burst of unrelated HUD labels to all
        // "stabilize" at once and get read together as one nonsensical blob: "00. 00. 00.
        // Fechado. <farming tip>. 1st Floor. v0.7.5.3.0" - none of that is dialogue.
        // Filtered by hierarchy path (more reliable than guessing at text patterns for
        // arbitrary HUD numbers like "00").
        private static readonly string[] KnownHudPaths =
        {
            "TimeAndWeather", "PlayerMoney", "TavernInfoUIRight", "Version Number",
        };

        private readonly Dictionary<int, string> _lastAnnounced = new Dictionary<int, string>();
        private readonly Dictionary<int, (string text, float since)> _pending = new Dictionary<int, (string text, float since)>();
        private readonly HashSet<int> _loggedClickables = new HashSet<int>();
        // Tracked separately so re-reading one doesn't lose the other - confirmed live that
        // a bark arriving right after a story line was overwriting it, making Up/Down only
        // ever repeat the bark.
        private string _lastStoryMessage;
        private string _lastAmbientMessage;

        // Starting a New Game shows a loading tip WITHOUT any real Unity scene change (it's
        // an overlay inside the title screen scene, "MenuUI/TitleScreen/Loading Bar/Tips") -
        // confirmed live this never triggers Main.cs's OnSceneWasLoaded("LoadingScene")
        // announcement at all, since no scene change actually happens. Detected here
        // instead, by the tip panel itself appearing/disappearing.
        private bool _loadingBarSeen;
        private string _lastLoggedActionPrompt;

        // Dialogue response choices ("Response Menu Panel") - confirmed live this is a
        // SEPARATE thing from the linear Continue Button: the user got stuck for several
        // minutes on a conversation with a real choice ("Eu não sou um saqueador...") because
        // it was only being read as plain dialogue text (looping the same announcement every
        // time it re-stabilized) - Space/Enter did nothing since there was no Continue
        // Button, only this response button, which we never looked for or could navigate.
        private readonly List<Button> _responseButtons = new List<Button>();
        private int _responseIndex;

        public void Update(bool anyUiOpen)
        {
            // When a real navigable menu is open (Options, Save, Character Creator, etc.),
            // KeyboardUINavigator already owns announcements for it - scanning here too
            // caused duplicate readouts (confirmed live: "Carregar. Novo" and the whole
            // Character Creator screen got read as one blob on top of the normal per-item
            // announcements). Only scan/announce when nothing else is handling the screen.
            //
            // Gated on MainUI.IsAnyUIOpen(1) directly, not KeyboardUINavigator.ItemCount -
            // confirmed live the item count lags a few frames behind the window actually
            // opening (content/layout needs a moment to populate), and during that gap this
            // scan was picking up the panel's own placeholder body text (e.g. "Sem receitas
            // ainda") and overwriting _lastStoryMessage with it, which is why Up stopped
            // re-reading the real last story/objective line after opening a MainPanelUI tab.
            if (anyUiOpen) return;

            ScanResponseButtons();
            if (_responseButtons.Count > 0)
            {
                // A real dialogue choice is on screen - takes over Up/Down/Space entirely
                // until the player picks one (see field comment). Skipping the normal text
                // scan also avoids the response text getting read again as plain narration.
                HandleResponseInput();
            }
            else
            {
                ScanAndAnnounceText();
                HandleAdvanceAndRereadInput();
            }

            // Diagnostic only: log every clickable element we can find (not just Button),
            // to spot future custom controls.
            if (Main.DebugMode)
            {
                LogStrayClickables();
            }
        }

        private void ScanResponseButtons()
        {
            // Sorted by sibling index, not screen position - confirmed live every response
            // seen so far had only 1 option, but a horizontal response layout would give
            // near-identical Y values for all of them, making position-based ordering flicker
            // frame to frame (sibling index is what Unity layout groups actually use, stable
            // regardless of layout direction).
            var found = Object.FindObjectsOfType<Button>()
                .Where(b => b.gameObject.activeInHierarchy && b.interactable && HierarchyPath(b.transform).Contains("Response Menu Panel"))
                .OrderBy(b => b.transform.GetSiblingIndex())
                .ToList();

            // Compare as a SET, not an ordered sequence - avoids resetting _responseIndex
            // (and re-announcing) every frame over harmless reordering noise; only a real
            // change in WHICH buttons exist should restart the selection.
            bool sameSet = found.Count == _responseButtons.Count && found.All(b => _responseButtons.Contains(b));
            if (sameSet) return;

            _responseButtons.Clear();
            _responseButtons.AddRange(found);
            _responseIndex = 0;

            if (_responseButtons.Count > 0)
            {
                AnnounceCurrentResponse();
            }
        }

        private void AnnounceCurrentResponse()
        {
            string text = UITextExtractor.GetReadableText(_responseButtons[_responseIndex].gameObject);
            string message = _responseButtons.Count > 1
                ? $"Escolha {_responseIndex + 1} de {_responseButtons.Count}: {text}"
                : text;
            ScreenReader.Announce(message);
            DebugLogger.LogState($"Dialogue response: \"{text}\" ({_responseIndex + 1} of {_responseButtons.Count})");
        }

        private void HandleResponseInput()
        {
            // No ">1" guard here on purpose - confirmed live every response screen so far
            // had exactly 1 option, and Up/Down were doing NOTHING at all in that case
            // (this method fully replaces the normal re-read handling while active), which
            // read as "arrows broken" to the user. With 1 option this just re-announces the
            // same text, same as a normal re-read - harmless, and gives feedback the key did
            // something.
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                bool atBoundary = _responseButtons.Count == 1 || _responseIndex == 0;
                _responseIndex = (_responseIndex - 1 + _responseButtons.Count) % _responseButtons.Count;
                UISound.PlayNavigate();
                if (atBoundary) UISound.PlayBoundaryDelayed();
                DebugLogger.LogInput("Up", "Previous dialogue response");
                AnnounceCurrentResponse();
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                bool atBoundary = _responseButtons.Count == 1 || _responseIndex == _responseButtons.Count - 1;
                _responseIndex = (_responseIndex + 1) % _responseButtons.Count;
                UISound.PlayNavigate();
                if (atBoundary) UISound.PlayBoundaryDelayed();
                DebugLogger.LogInput("Down", "Next dialogue response");
                AnnounceCurrentResponse();
                return;
            }

            // Same reasoning as the Continue Button: Enter is unreliable here (used by the
            // game elsewhere), Space is free and already the established way to confirm.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Button chosen = _responseButtons[_responseIndex];
                UISound.PlayChoiceConfirm();
                DebugLogger.LogInput("Space", $"Choose dialogue response: {UITextExtractor.GetReadableText(chosen.gameObject)}");
                chosen.onClick.Invoke();
                _responseButtons.Clear();
                _responseIndex = 0;
            }
        }

        private void ScanAndAnnounceText()
        {
            var labels = Object.FindObjectsOfType<TextMeshProUGUI>()
                .Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text))
                .ToList();

            CheckLoadingBar(labels);

            var ready = new List<(float y, string text, string path)>();

            foreach (var label in labels)
            {
                int id = label.GetInstanceID();
                string clean = UITextExtractor.GetReadableText(label);
                if (string.IsNullOrEmpty(clean) || clean.Length < 2) continue;

                if (Main.DebugMode && ActionPromptPattern.IsMatch(clean) && clean != _lastLoggedActionPrompt)
                {
                    // Currently filtered as noise - logged (not suppressed silently) so we
                    // have real path/text data to design ambient interactable narration
                    // from later (the user wants to know what's nearby while walking
                    // around - this prompt is the game's own "press E to ..." hint, which is
                    // exactly that signal, just never used as one yet).
                    _lastLoggedActionPrompt = clean;
                    DebugLogger.LogState($"Action prompt seen (filtered): \"{clean}\" path={HierarchyPath(label.transform)}");
                }
                if (IsKnownHudNoise(clean)) continue;

                string path = HierarchyPath(label.transform);
                if (IsKnownHudPath(path)) continue;

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
                    ready.Add((label.transform.position.y, clean, path));
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
                // Root cause found by reading the user's own Latest.log across several
                // sessions: requiring `.interactable` here meant this NEVER matched, not
                // once, in any session, even though dialogue visibly advanced one line per
                // Space press the whole time - the game advances it natively by itself,
                // bypassing this Button's onClick entirely (it's seemingly never
                // `interactable`, by design, regardless of mode). So we no longer try to
                // invoke it ourselves (that risked double-advancing, racing the game's own
                // handler) - just detect it's showing (`activeInHierarchy` only) to play the
                // confirm sound alongside whatever the game itself is about to do.
                if (IsContinueButtonShowing())
                {
                    UISound.PlayChoiceConfirm();
                    DebugLogger.LogInput("Space", "Advance dialogue (sound only - game handles the actual advance)");
                }
                else if (Main.DebugMode)
                {
                    DebugLogger.LogState("Space pressed but no Continue Button showing");
                }
            }
        }

        private static bool IsContinueButtonShowing()
        {
            foreach (var button in Object.FindObjectsOfType<Button>())
            {
                if (button.gameObject.name == "Continue Button" && button.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsKnownHudNoise(string clean)
        {
            return ClockPattern.IsMatch(clean) || DayCounterPattern.IsMatch(clean) || ActionPromptPattern.IsMatch(clean);
        }

        private void CheckLoadingBar(List<TextMeshProUGUI> labels)
        {
            bool loadingBarActive = labels.Any(l => HierarchyPath(l.transform).Contains("Loading Bar"));

            if (loadingBarActive && !_loadingBarSeen)
            {
                ScreenReader.Announce("Carregando jogo...");
            }

            _loadingBarSeen = loadingBarActive;
        }

        private static bool IsKnownHudPath(string path)
        {
            foreach (var fragment in KnownHudPaths)
            {
                if (path.Contains(fragment)) return true;
            }
            return false;
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
