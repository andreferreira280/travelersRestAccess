using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TravellersRestAccess
{
    /// <summary>
    /// Keyboard-only navigation for UI menus, using our own virtual focus cursor.
    ///
    /// Travellers Rest's input module is built for mouse/gamepad: it clears
    /// EventSystem.currentSelectedGameObject back to null every frame when no gamepad is
    /// active, so we can't rely on Unity's built-in selection state at all (confirmed via
    /// testing). Options' tab switching does not reliably toggle GameObject.SetActive or
    /// Selectable.interactable per tab (confirmed via diagnostic logs - both signals leaked
    /// elements from non-selected tabs), so instead we track which tab button WE most
    /// recently activated and only scan that one tab's content ourselves.
    /// </summary>
    public class KeyboardUINavigator
    {
        private class NavItem
        {
            public Selectable Anchor;
            public ToggleButton ToggleButton;
            public VolumeSliderUI VolumeSlider;
            public bool IsAudioVolume;
            public Button MultiSelectPrev;
            public Button MultiSelectNext;
            public Func<string> MultiSelectValueReader;
        }

        private static readonly Dictionary<string, Type> OptionsTabPanelTypes = new Dictionary<string, Type>
        {
            { "GraphicsMenu", typeof(GraphicsMenuUI) },
            { "SoundMenu", typeof(SoundMenuUI) },
            { "Keybind", typeof(KeybindUI) },
            { "OthersMenu", typeof(OthersMenuUI) },
        };

        private const float StabilizeDelay = 0.25f;

        private List<NavItem> _items = new List<NavItem>();
        private List<NavItem> _pendingItems;
        private float _pendingSince;
        private bool _pendingIsFirstForOpen;
        private int _currentIndex = -1;
        private bool _wasOpen;

        private OptionsMenuUI _trackedOptionsMenu;
        private Type _selectedOptionsPanelType = typeof(GraphicsMenuUI);

        // Modal "adjusting" state shared by volume controls and Previous/Next style controls
        // (resolution, quality, camera zoom): Enter starts it, left/right adjusts, Enter or
        // Escape confirms and exits. One control at a time.
        private bool _adjustingActive;
        private string _adjustingLabel;
        private Action _adjustingLeft;
        private Action _adjustingRight;
        private Func<string> _adjustingValueReader;

        public void Update()
        {
            bool anyOpen = MainUI.IsAnyUIOpen(1);

            if (!anyOpen)
            {
                _wasOpen = false;
                _items.Clear();
                _pendingItems = null;
                _currentIndex = -1;
                _adjustingActive = false;
                return;
            }

            if (_adjustingActive)
            {
                UpdateAdjusting();
                return;
            }

            var freshItems = CollectItems(justOpened: !_wasOpen);

            if (!_wasOpen)
            {
                _wasOpen = true;
                _items = new List<NavItem>();
                _pendingItems = freshItems;
                _pendingSince = Time.unscaledTime;
                _pendingIsFirstForOpen = true;
            }
            else if (!SameItems(freshItems, _items))
            {
                if (_pendingItems == null || !SameItems(freshItems, _pendingItems))
                {
                    _pendingItems = freshItems;
                    _pendingSince = Time.unscaledTime;
                }
            }
            else
            {
                _pendingItems = null;
            }

            // Only commit (and announce) once the list has stayed the same for a short
            // moment - avoids announcing a transient state caught mid-animation/mid-load,
            // both on first open and on later changes (e.g. switching tabs).
            if (_pendingItems != null && Time.unscaledTime - _pendingSince >= StabilizeDelay)
            {
                Commit(_pendingItems, interrupt: !_pendingIsFirstForOpen);
                _pendingItems = null;
                _pendingIsFirstForOpen = false;
            }

            if (_items.Count == 0) return;

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                Move(1);
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                Move(-1);
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            {
                Activate();
            }
        }

        private void UpdateAdjusting()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _adjustingLeft();
                ScreenReader.Announce($"{_adjustingLabel}: {_adjustingValueReader()}");
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                _adjustingRight();
                ScreenReader.Announce($"{_adjustingLabel}: {_adjustingValueReader()}");
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))
            {
                ScreenReader.Announce($"{_adjustingLabel}: {_adjustingValueReader()} confirmed");
                DebugLogger.LogState($"Adjust confirmed: {_adjustingLabel} = {_adjustingValueReader()}");
                _adjustingActive = false;
            }
        }

        private void StartAdjusting(string label, Action onLeft, Action onRight, Func<string> readValue)
        {
            _adjustingActive = true;
            _adjustingLabel = label;
            _adjustingLeft = onLeft;
            _adjustingRight = onRight;
            _adjustingValueReader = readValue;
            ScreenReader.Announce($"Adjusting {label}: {readValue()}. Left and right to adjust, Enter or Escape to confirm.");
        }

        private void Commit(List<NavItem> items, bool interrupt = true)
        {
            Selectable previousAnchor = (_currentIndex >= 0 && _currentIndex < _items.Count) ? _items[_currentIndex].Anchor : null;
            bool sameSet = SameSet(_items, items);

            _items = items;

            int preservedIndex = previousAnchor != null ? _items.FindIndex(i => i.Anchor == previousAnchor) : -1;
            _currentIndex = preservedIndex >= 0 ? preservedIndex : (_items.Count > 0 ? 0 : -1);

            DebugLogger.LogState($"Menu items refreshed: {_items.Count} found");
            DumpDiagnostics(_items);

            // Don't re-announce when it's the exact same set of controls just reshuffled
            // (e.g. a value change nudging positions) - that's not a real navigation event,
            // and re-announcing on every such hiccup is what made Next/Previous feel like it
            // teleported back to the first item.
            if (!sameSet)
            {
                AnnounceCurrent(interrupt);
            }
        }

        // Verbose structural dump (DebugMode only) so real hierarchy/component data can be
        // read from Latest.log instead of guessed at from descriptions.
        private void DumpDiagnostics(List<NavItem> items)
        {
            if (!Main.DebugMode) return;

            for (int i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                var go = entry.Anchor.gameObject;
                string path = HierarchyPath(go.transform, 5);
                string components = string.Join("+", new[]
                {
                    go.GetComponent<Button>() != null ? "Button" : null,
                    go.GetComponent<Toggle>() != null ? "Toggle" : null,
                    entry.ToggleButton != null ? "ToggleButton" : null,
                    go.GetComponent<Slider>() != null ? "Slider" : null,
                    entry.VolumeSlider != null ? "VolumeSliderUI" : null,
                    entry.MultiSelectPrev != null ? "MultiSelect" : null,
                }.Where(s => s != null));

                DebugLogger.LogState($"  [{i}] \"{go.name}\" path={path} components=[{components}] interactable={entry.Anchor.interactable}");
            }
        }

        private string HierarchyPath(Transform t, int maxLevels)
        {
            var names = new List<string>();
            int count = 0;
            while (t != null && count < maxLevels)
            {
                names.Insert(0, t.name);
                t = t.parent;
                count++;
            }
            return string.Join("/", names);
        }

        private List<NavItem> CollectItems(bool justOpened)
        {
            var openWindows = MainUI.GetCurrentOpenWindows(1);
            UIWindow topWindow = (openWindows != null && openWindows.Count > 0) ? openWindows.Last.Value : null;

            if (topWindow == null)
            {
                return new List<NavItem>();
            }

            // group 0 = the window's own content (e.g. the tab strip) - always listed first.
            // group 1 = the selected tab's content - listed after.
            var rootGroups = new List<(GameObject root, int group)>();
            if (topWindow.content != null) rootGroups.Add((topWindow.content, 0));

            var optionsMenu = topWindow as OptionsMenuUI;
            SoundMenuUI soundMenu = null;
            GraphicsMenuUI graphicsMenu = null;
            OthersMenuUI othersMenu = null;
            if (optionsMenu != null)
            {
                if (optionsMenu != _trackedOptionsMenu || justOpened)
                {
                    _trackedOptionsMenu = optionsMenu;
                    _selectedOptionsPanelType = typeof(GraphicsMenuUI); // matches the game's own default tab
                }

                if (optionsMenu.panelsUI != null)
                {
                    soundMenu = optionsMenu.panelsUI.OfType<SoundMenuUI>().FirstOrDefault();
                    graphicsMenu = optionsMenu.panelsUI.OfType<GraphicsMenuUI>().FirstOrDefault();
                    othersMenu = optionsMenu.panelsUI.OfType<OthersMenuUI>().FirstOrDefault();

                    var selectedPanel = optionsMenu.panelsUI.FirstOrDefault(p => p != null && p.GetType() == _selectedOptionsPanelType);
                    if (selectedPanel != null && selectedPanel.content != null)
                    {
                        rootGroups.Add((selectedPanel.content, 1));
                    }
                }
            }

            var found = new List<(Selectable selectable, int group)>();
            foreach (var (root, group) in rootGroups)
            {
                foreach (var s in root.GetComponentsInChildren<Selectable>(includeInactive: false))
                {
                    found.Add((s, group));
                }
            }

            // Hide elements that aren't actually usable right now (the game marks empty
            // save slots this way), and hide the individual Previous/Next buttons of a
            // multi-select row - they get folded into the row's own entry below.
            var visible = found
                .Where(f => f.selectable != null && f.selectable.gameObject.activeInHierarchy
                    && f.selectable.interactable && !(f.selectable is Scrollbar)
                    && !IsMultiSelectNavButton(f.selectable.gameObject))
                .Distinct()
                .OrderBy(f => f.group)
                .ThenByDescending(f => Mathf.Round(f.selectable.transform.position.y / 30f))
                .ThenBy(f => f.selectable.transform.position.x)
                .ToList();

            // Collapse each volume/stepped control (increase + decrease buttons, plus the
            // row's own button) into a single entry. The VolumeSliderUI component can sit on
            // an ancestor of the buttons (typical) or be found on a child "Slider" object
            // hanging off the row's own button (Music/SFX rows) - check both directions.
            var result = new List<NavItem>();
            var seenVolumeSliders = new HashSet<VolumeSliderUI>();

            foreach (var (selectable, _) in visible)
            {
                // Every on/off setting in this game's Options (difficulty, vibration,
                // fullscreen, chat, etc.) is declared as a ToggleButton in the game's own
                // code - confirmed by reading the decompiled source. Some of these rows
                // also happen to sit near an unrelated VolumeSliderUI in the hierarchy
                // (false match via GetComponentInParent/Children), so always look for the
                // real ToggleButton first and skip the volume-style handling if found.
                ToggleButton toggleButton = selectable.GetComponent<ToggleButton>()
                    ?? selectable.GetComponentInChildren<ToggleButton>()
                    ?? selectable.GetComponentInParent<ToggleButton>();
                bool isToggleButton = toggleButton != null;

                VolumeSliderUI volumeSlider = isToggleButton ? null
                    : (selectable.GetComponentInParent<VolumeSliderUI>() ?? selectable.GetComponentInChildren<VolumeSliderUI>());

                if (volumeSlider != null)
                {
                    if (seenVolumeSliders.Contains(volumeSlider)) continue;
                    seenVolumeSliders.Add(volumeSlider);
                }

                bool isAudio = volumeSlider != null && soundMenu != null
                    && (volumeSlider == soundMenu.musicSlider || volumeSlider == soundMenu.sfxSlider);

                Button multiPrev = null, multiNext = null;
                Func<string> multiValueReader = null;
                if (volumeSlider == null)
                {
                    var multiSelection = selectable.transform.Find("MultiSelection");
                    if (multiSelection != null)
                    {
                        multiPrev = multiSelection.Find("PreviousButton")?.GetComponent<Button>();
                        multiNext = multiSelection.Find("NextButton")?.GetComponent<Button>();
                        if (multiPrev != null && multiNext != null)
                        {
                            multiValueReader = BuildMultiSelectValueReader(selectable.gameObject.name, graphicsMenu, othersMenu)
                                ?? (() => UITextExtractor.GetReadableText(selectable.gameObject));
                        }
                    }
                }

                result.Add(new NavItem
                {
                    Anchor = selectable,
                    ToggleButton = toggleButton,
                    VolumeSlider = volumeSlider,
                    IsAudioVolume = isAudio,
                    MultiSelectPrev = multiPrev,
                    MultiSelectNext = multiNext,
                    MultiSelectValueReader = multiValueReader,
                });
            }

            return result;
        }

        // The current-value TextMeshProUGUI for a Previous/Next row isn't a child of the row
        // itself - it's a separate field on the panel script - so we match it by row name.
        private Func<string> BuildMultiSelectValueReader(string rowName, GraphicsMenuUI graphicsMenu, OthersMenuUI othersMenu)
        {
            switch (rowName)
            {
                case "Resolution": return graphicsMenu != null ? () => UITextExtractor.GetReadableText(graphicsMenu.resolutionText) : null;
                case "Quality": return graphicsMenu != null ? () => UITextExtractor.GetReadableText(graphicsMenu.qualityText) : null;
                case "CameraZoom": return graphicsMenu != null ? () => UITextExtractor.GetReadableText(graphicsMenu.zoomText) : null;
                case "Language": return othersMenu != null ? () => UITextExtractor.GetReadableText(othersMenu.languageText) : null;
                default: return null;
            }
        }

        private bool IsMultiSelectNavButton(GameObject go)
        {
            return (go.name == "PreviousButton" || go.name == "NextButton")
                && go.transform.parent != null && go.transform.parent.name == "MultiSelection";
        }

        private bool SameItems(List<NavItem> a, List<NavItem> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Anchor != b[i].Anchor) return false;
            }
            return true;
        }

        private bool SameSet(List<NavItem> a, List<NavItem> b)
        {
            if (a.Count != b.Count) return false;
            var setA = new HashSet<Selectable>(a.Select(i => i.Anchor));
            return b.All(i => setA.Contains(i.Anchor));
        }

        private void Move(int direction)
        {
            if (_items.Count == 0) return;
            _currentIndex = (_currentIndex + direction + _items.Count) % _items.Count;
            AnnounceCurrent();
        }

        private void AnnounceCurrent(bool interrupt = true)
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count) return;

            var entry = _items[_currentIndex];
            var item = entry.Anchor;
            string text = UITextExtractor.GetReadableText(item.gameObject);
            string message;

            if (entry.VolumeSlider != null)
            {
                message = $"{text}: {LevelText(entry.VolumeSlider, entry.IsAudioVolume)} ({_currentIndex + 1} of {_items.Count})";
            }
            else if (entry.MultiSelectPrev != null && entry.MultiSelectNext != null)
            {
                message = $"{text}: {entry.MultiSelectValueReader()} ({_currentIndex + 1} of {_items.Count})";
            }
            else
            {
                var toggle = item.GetComponent<Toggle>();
                if (entry.ToggleButton != null)
                {
                    message = $"{text}: {(entry.ToggleButton.DINJBIOPIOH ? "on" : "off")} ({_currentIndex + 1} of {_items.Count})";
                }
                else if (toggle != null)
                {
                    message = $"{text}: {(toggle.isOn ? "on" : "off")} ({_currentIndex + 1} of {_items.Count})";
                }
                else
                {
                    message = $"{text} ({_currentIndex + 1} of {_items.Count})";
                }
            }

            ScreenReader.Say(message, interrupt);
            DebugLogger.LogState($"Focus: {item.gameObject.name} -> \"{text}\"");
        }

        private void Activate()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count) return;

            var entry = _items[_currentIndex];
            var item = entry.Anchor;
            string label = UITextExtractor.GetReadableText(item.gameObject);

            if (entry.VolumeSlider != null)
            {
                var vs = entry.VolumeSlider;
                bool isAudio = entry.IsAudioVolume;
                StartAdjusting(label, vs.DecrementLevel, vs.IncrementLevel, () => LevelText(vs, isAudio));
                return;
            }

            if (entry.MultiSelectPrev != null && entry.MultiSelectNext != null)
            {
                var prevButton = entry.MultiSelectPrev;
                var nextButton = entry.MultiSelectNext;
                var reader = entry.MultiSelectValueReader;
                StartAdjusting(label, () => prevButton.onClick.Invoke(), () => nextButton.onClick.Invoke(), reader);
                return;
            }

            var toggle = item.GetComponent<Toggle>();
            var button = item.GetComponent<Button>();

            if (button != null)
            {
                button.onClick.Invoke();
            }
            else
            {
                ExecuteEvents.Execute(item.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            }

            if (entry.ToggleButton != null)
            {
                ScreenReader.Announce(entry.ToggleButton.DINJBIOPIOH ? "On" : "Off");
            }
            else if (toggle != null)
            {
                ScreenReader.Announce(toggle.isOn ? "On" : "Off");
            }
            else if (OptionsTabPanelTypes.TryGetValue(item.gameObject.name, out var panelType))
            {
                // Remember which Options tab was just selected - the game doesn't reliably
                // expose this itself (confirmed via diagnostic logs), so we track it.
                _selectedOptionsPanelType = panelType;
            }

            DebugLogger.LogInput("Enter", $"Activate {item.gameObject.name}");
        }

        // Audio volumes (music/SFX) read naturally as a percentage. Everything else that
        // happens to reuse this same stepped-value widget (resolution, difficulty, etc.)
        // reads as a level out of 10 instead - "0%" is misleading for non-audio settings.
        private string LevelText(VolumeSliderUI volumeSlider, bool isAudio)
        {
            int level = Mathf.Clamp(volumeSlider.FJAAIIJEKIE, 0, 10);
            return isAudio ? $"{level * 10}%" : $"level {level} of 10";
        }
    }
}
