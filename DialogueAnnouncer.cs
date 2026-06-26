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
    ///   talk from other characters wandering around, not part of the main story). The
    ///   PLAYER's own bark lines use the exact same UI prefab, but rooted under "Player/"
    ///   instead of an NPC name (confirmed live: "Player/Bubble Template Standard Bark
    ///   UI/...") - those are the character's own reactions ("O barril está vazio.", "Eu
    ///   não sei como usar isto.") and are kept; only non-Player ones are ambient noise.
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
        // User couldn't tell which key actually performs an action (tried Q, F, E, mouse
        // click guessing at "Limpar") - the key was right there in the game's own on-screen
        // hint the whole time ("[E] Limpar"), just stripped out before announcing. Capturing
        // it instead of discarding it.
        private static readonly Regex ActionPromptKeyPattern = new Regex(@"^\[(\w)\]");

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
        private HashSet<string> _activeActionPrompts = new HashSet<string>();

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
            // Widened from TextMeshProUGUI to TMP_Text (the common base, also covers
            // world-space TextMeshPro) - user reported a message appearing after clicking a
            // table to clean it that's never read; a floating world-space feedback popup
            // (not a UI element) would have been invisible to the narrower UI-only scan.
            var labels = Object.FindObjectsOfType<TMP_Text>()
                .Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text))
                .ToList();

            CheckLoadingBar(labels);

            var ready = new List<(float y, string text, string path)>();
            var actionPromptsThisScan = new HashSet<string>();

            foreach (var label in labels)
            {
                int id = label.GetInstanceID();
                string clean = UITextExtractor.GetReadableText(label);
                if (string.IsNullOrEmpty(clean) || clean.Length < 2) continue;

                if (ActionPromptPattern.IsMatch(clean))
                {
                    actionPromptsThisScan.Add(clean);
                    // User's explicit request: hear SOMETHING when near an interactable
                    // (door, bed, etc.) while exploring, instead of getting no feedback at
                    // all. This is the game's own "[E] ..." action-bar hint - previously
                    // filtered as noise. Announced once per appearance/change (not every
                    // frame), "[E] "/"[Q] " prefix stripped.
                    //
                    // Tracked as a SET (not a single last-seen value): confirmed live that
                    // some objects (e.g. a fireplace) show TWO prompts at once ("Abrir" +
                    // "Combustível") - a single-value tracker ping-ponged between the two
                    // every frame and announced both forever, instead of once each.
                    if (!_activeActionPrompts.Contains(clean))
                    {
                        string stripped = ActionPromptPattern.Replace(clean, "").Trim();
                        string key = ActionPromptKeyPattern.Match(clean) is { Success: true } keyMatch ? keyMatch.Groups[1].Value.ToUpperInvariant() : null;
                        // Best-effort - user asked for the object's name, not just the verb
                        // ("Abrir" alone doesn't say if it's a door, chest, etc.). Approximate
                        // when 2 different objects each show their own prompt at once - see
                        // WorldNavigationHandler.GetNearestInteractionTarget().
                        var audioInfo = WorldNavigationHandler.GetNearestInteractionAudioInfo();
                        string targetName = audioInfo?.name;
                        string promptText = string.IsNullOrEmpty(targetName) ? stripped : $"{targetName}: {stripped}";
                        if (!string.IsNullOrEmpty(key)) promptText += $" (tecla {key})";
                        CustomSounds.PlayItemNearby(audioInfo?.name, audioInfo?.pitch ?? 1f, audioInfo?.pan ?? 0f);
                        ScreenReader.Say($"Próximo: {promptText}", interrupt: false);
                        DebugLogger.LogState($"Action prompt announced: \"{promptText}\"");
                    }
                    continue;
                }
                if (IsKnownHudNoise(clean)) continue;

                string path = HierarchyPath(label.transform);
                if (IsKnownHudPath(path)) continue;

                // Found the bug: last round narrowed IsAmbientNpcBark down to just the
                // "Mudanza" family, but the actual skip/continue that MUTES a match had
                // been removed entirely the round before (when the filter was opened up
                // fully to hunt for the cat's line) - so narrowing the matcher list alone
                // did nothing, the family kept getting announced just like everything else.
                // Restoring the skip here, now scoped to just that family.
                if (IsAmbientNpcBark(path)) continue;

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

            // Replace wholesale: anything missing from this scan is gone, so walking back
            // up to the SAME interactable later announces it again instead of staying
            // silent forever; anything still present stays suppressed (no repeats).
            _activeActionPrompts = actionPromptsThisScan;

            if (ready.Count == 0) return;

            var ordered = ready.OrderByDescending(r => r.y).ToList();
            var storyEntries = ordered.Where(r => !IsAmbientNpcBark(r.path)).ToList();
            var ambientEntries = ordered.Where(r => IsAmbientNpcBark(r.path)).ToList();

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

        private void CheckLoadingBar(List<TMP_Text> labels)
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

        // User's explicit request: re-filter only this specific family ("Mudanza" moving
        // event - BuzzNPC/DoorNPC, confirmed in the log) instead of every non-Player bark.
        // Opening the filter fully (previous round) didn't surface the cat's missing line
        // either, so a blanket mute isn't earning its keep - keep other NPC barks audible.
        private static readonly string[] FilteredAmbientNpcNames = { "BuzzNPC", "DoorNPC", "Mudanza" };

        private static bool IsAmbientNpcBark(string path)
        {
            if (!path.Contains("Bark UI")) return false;
            foreach (var name in FilteredAmbientNpcNames)
            {
                if (path.Contains(name)) return true;
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
