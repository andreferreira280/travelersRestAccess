using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
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
            public Func<bool> SelectedStateReader;
            public string ActivationAnnouncement;
            public Func<string> LabelReader;
        }

        // CharacterCreatorUI's gender highlight images (maleFocused/femaleFocused) are
        // private fields with no public accessor - read via reflection so we can announce
        // which one is currently selected, the same way ToggleButton rows do.
        private static readonly System.Reflection.FieldInfo CharacterCreatorMaleFocusedField =
            typeof(CharacterCreatorUI).GetField("maleFocused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo CharacterCreatorFemaleFocusedField =
            typeof(CharacterCreatorUI).GetField("femaleFocused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // EEGKEGOFHCA() pulls each colorButtons[i]'s material from the real character data
        // (humanInfo) - the game calls it itself on opening this screen and after a few
        // other actions, but apparently not after closing the color picker, which is why a
        // freshly-picked color never showed up when we read the button back.
        private static readonly System.Reflection.MethodInfo CharacterCreatorRefreshColorsMethod =
            typeof(CharacterCreatorUI).GetMethod("EEGKEGOFHCA", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // MainPanelUI tracks which tab (Inventory/Quests/Recipes/Skills/etc.) is selected in
        // a private int field - confirmed in decompiled source it's consistently kept in
        // sync (used by the game's own FocusMainPanel(HACEDOOFMBE) calls), unlike
        // panel.content.activeInHierarchy, which stays true for every tab at once (non-
        // selected tabs are moved off-screen, not deactivated).
        private static readonly System.Reflection.FieldInfo MainPanelSelectedIndexField =
            typeof(MainPanelUI).GetField("HACEDOOFMBE", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // EncyclopediaUI's currently-shown topic body ("sectionTitle"/"sectionText") is a
        // plain display, not a Button - the topic list buttons that set it are already
        // navigable, but nothing ever announced the body text itself once a topic was chosen
        // (user reported opening "Controles Básicos" and hearing nothing). Both fields are
        // [SerializeField] private in decompiled source - read via reflection.
        private static readonly System.Reflection.FieldInfo EncyclopediaSectionTitleField =
            typeof(EncyclopediaUI).GetField("sectionTitle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo EncyclopediaSectionTextField =
            typeof(EncyclopediaUI).GetField("sectionText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // Round 110: oven crafting UI (GameCraftingUI). recipeName/inputSlots are private on
        // RecipeElementUI - read via reflection to announce the dish name and ingredients.
        private static readonly System.Reflection.FieldInfo RecipeNameField =
            typeof(RecipeElementUI).GetField("recipeName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo RecipeInputSlotsField =
            typeof(RecipeElementUI).GetField("inputSlots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private string _lastAnnouncedEncyclopediaContent;
        private string _lastAnnouncedYesNoQuestion;

        private static readonly Dictionary<string, Type> OptionsTabPanelTypes = new Dictionary<string, Type>
        {
            { "GraphicsMenu", typeof(GraphicsMenuUI) },
            { "SoundMenu", typeof(SoundMenuUI) },
            { "Keybind", typeof(KeybindUI) },
            { "OthersMenu", typeof(OthersMenuUI) },
        };

        private const float StabilizeDelay = 0.25f;

        private List<NavItem> _items = new List<NavItem>();
        public int ItemCount => _items.Count;
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

        // Real Unity focus is required for text fields (the game checks .isFocused), unlike
        // button navigation where we ignore native focus entirely. While editing, we step
        // aside and let Unity's own TMP_InputField handle every key except Enter/Escape.
        private TMP_InputField _editingInputField;
        private string _editingFieldLabel;

        // Opening a popup on top of a window (e.g. the color picker over Character
        // Creator) can leave MainUI.IsAnyUIOpen reporting false for a single frame while
        // windows swap - without this debounce, that one frame was wiping our whole list
        // and resetting the cursor to the top item once the popup closed.
        private const float ClosedDebounceDelay = 0.15f;
        private float? _closedSince;

        // Remembers, per window, which item had the cursor right before we left it for a
        // different window (e.g. opening the color picker over Character Creator) - so when
        // we come back, the cursor returns there instead of resetting to the first item.
        private UIWindow _lastTopWindow;
        private readonly Dictionary<UIWindow, Selectable> _rememberedAnchors = new Dictionary<UIWindow, Selectable>();

        // User's explicit request: switch between the chest's list and the player's
        // inventory list while a chest is open (seta direita/esquerda) - both windows are
        // open simultaneously (a chest opens GameInventoryUI underneath itself), but this
        // navigator deliberately only ever scans ONE window at a time (the same defense that
        // fixed the old "mixed tabs" bug in the main panel - confirmed in
        // docs/modules/main-panel-tabs.md), so it needs to be told which one explicitly.
        private UIWindow _manualWindowOverride;
        // Round 116: tracks the open station window so we can default the focus to the STATION
        // (not the inventory) whenever a new one opens.
        private UIWindow _lastStationWindow;

        // User's explicit request: let other handlers (InventoryTransferHandler) act on
        // whatever slot is currently focused. Confirmed above (class doc) that Unity's own
        // EventSystem.currentSelectedGameObject can't be trusted - the game's input module
        // wipes it every frame without a gamepad - so this is the only reliable source for
        // "what's focused right now."
        public GameObject GetCurrentSelectedGameObject()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count) return null;
            return _items[_currentIndex].Anchor?.gameObject;
        }

        public void Update()
        {
            bool anyOpen = MainUI.IsAnyUIOpen(1);
            // Arrow keys never move the character at all, even outside menus (see
            // MovementAxisPatch.SuppressArrowMovement, set permanently true in Main.cs per
            // the user's request) - WASD remains the only way to walk. Nothing to toggle
            // here anymore.

            if (!anyOpen)
            {
                if (_closedSince == null) _closedSince = Time.unscaledTime;
                if (Time.unscaledTime - _closedSince.Value < ClosedDebounceDelay) return;

                _wasOpen = false;
                _items.Clear();
                _pendingItems = null;
                _currentIndex = -1;
                _adjustingActive = false;
                _editingInputField = null;
                // Everything fully closed - any remembered cursor position from here on is
                // stale. Confirmed live: reopening Encyclopedia later in the same play
                // session jumped straight to item 11/12 instead of starting at the top,
                // because this dictionary is keyed by UIWindow instance and several windows
                // (Encyclopedia included) are persistent singletons, not recreated on each
                // open. The remembering is meant for returning from a NESTED popup while the
                // outer UI stays open (e.g. ColorPicker -> back to CharacterCreator) - not
                // for "where was I last time, minutes ago" once everything has closed.
                _rememberedAnchors.Clear();
                _lastTopWindow = null;
                _lastAnnouncedEncyclopediaContent = null;
                return;
            }
            _closedSince = null;

            if (_editingInputField != null)
            {
                UpdateEditingInputField();
                return;
            }

            if (_adjustingActive)
            {
                UpdateAdjusting();
                return;
            }

            // Round 116: when a station window NEWLY opens, default the focus to the station
            // itself, not the inventory (user: "as vezes está abrindo no inv, a estação é o
            // padrão"). The override could otherwise linger on the inventory from a previous one.
            var currentStation = GetOpenStationWindow();
            if (currentStation != _lastStationWindow)
            {
                // Reset on ANY change (open OR close) so each new station session starts focused on
                // the STATION (the override is cleared, so GetTopWindow returns the station, which
                // is the top window - GameInventoryUI never appears in the open-window list). The
                // user reported it "only worked the second time" when this only fired on open.
                _manualWindowOverride = null;
                _lastStationWindow = currentStation;
            }

            HandleContainerInventorySwitch();
            var topWindowNow = GetTopWindow();
            if (topWindowNow != _lastTopWindow)
            {
                if (_lastTopWindow != null && _currentIndex >= 0 && _currentIndex < _items.Count)
                {
                    _rememberedAnchors[_lastTopWindow] = _items[_currentIndex].Anchor;
                }

                // Returning from the color picker to Character Creator: the row's own
                // ColorButton doesn't update its material/color when a pick is made
                // (confirmed live - it kept reading the same value no matter what was
                // chosen, even after forcing the game's own refresh method here). Logging
                // every step in debug mode since the refresh alone didn't fix it - need to
                // see whether it's even running, and whether colorButtons[i]'s own
                // material actually changes after it runs.
                if (_lastTopWindow is ColorPickerUI && topWindowNow is CharacterCreatorUI characterCreatorReturned)
                {
                    if (Main.DebugMode)
                    {
                        DebugLogger.LogState($"Color refresh: method found = {CharacterCreatorRefreshColorsMethod != null}");
                    }
                    try
                    {
                        CharacterCreatorRefreshColorsMethod?.Invoke(characterCreatorReturned, null);
                        if (Main.DebugMode)
                        {
                            DebugLogger.LogState("Color refresh: invoked OK");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        DebugLogger.LogState($"Color refresh: EXCEPTION {ex}");
                    }
                }

                _lastTopWindow = topWindowNow;
                // Reused singleton popup (same GameObject toggled active/inactive, not a new
                // instance) - without this reset, asking the same question twice in a row
                // (e.g. sleep, cancel, walk back into the trigger) would silently stay quiet
                // the second time since the text wouldn't look "changed".
                _lastAnnouncedYesNoQuestion = null;
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

            // The color picker lays its swatches out horizontally, and the user asked for
            // Left/Right to move between them there specifically (every other screen still
            // uses Up/Down).
            bool isColorPicker = topWindowNow is ColorPickerUI;
            KeyCode nextKey = isColorPicker ? KeyCode.RightArrow : KeyCode.DownArrow;
            KeyCode prevKey = isColorPicker ? KeyCode.LeftArrow : KeyCode.UpArrow;

            if (Input.GetKeyDown(nextKey))
            {
                Move(1);
            }
            else if (Input.GetKeyDown(prevKey))
            {
                Move(-1);
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Activate();
            }
            // Space is intentionally NOT used to activate anything here. It kept closing
            // Character Creator from anywhere on the screen even after restricting it to
            // the Accept button and clearing Unity's real selection every frame - neither
            // fix stopped it (confirmed live twice), so per the user's own suggestion,
            // Space is left alone entirely in menus and reserved only for the dialogue
            // advance/skip key (DialogueAnnouncer).
        }

        private void UpdateEditingInputField()
        {
            // Everything else (letters, backspace, etc.) goes straight to Unity's own
            // TMP_InputField handling, since it now has real EventSystem focus.
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape))
            {
                string finalText = _editingInputField.text;
                _editingInputField.DeactivateInputField();
                DebugLogger.LogInput(Input.GetKeyDown(KeyCode.Escape) ? "Escape" : "Enter", "Confirm text edit");
                _editingInputField = null;
                // Explicit "definido" feedback so it's clear this is a confirmation, not
                // just a passive re-read (requested - the plain value alone wasn't
                // distinguishable from just landing on the field while navigating).
                string label = _editingFieldLabel;
                _editingFieldLabel = null;
                ScreenReader.Announce(string.IsNullOrEmpty(finalText) ? $"{label} vazio" : $"{label} definido: {finalText}");
            }
        }

        private void UpdateAdjusting()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _adjustingLeft();
                ScreenReader.Announce(FormatLabelValue(_adjustingLabel, _adjustingValueReader()));
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                _adjustingRight();
                ScreenReader.Announce(FormatLabelValue(_adjustingLabel, _adjustingValueReader()));
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Escape))
            {
                ScreenReader.Announce($"{FormatLabelValue(_adjustingLabel, _adjustingValueReader())} confirmed");
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
            ScreenReader.Announce($"Adjusting {FormatLabelValue(label, readValue())}. Left and right to adjust, Enter or Escape to confirm.");
        }

        // Some rows (Character Creator's body parts) have no separate label - the anchor is
        // the PreviousButton itself, and its own value text already says what it is (e.g.
        // "Olhos 1"), so prefixing it with the humanized button name would be redundant.
        private static string FormatLabelValue(string label, string value)
        {
            return string.IsNullOrEmpty(label) ? value : $"{label}: {value}";
        }

        private void Commit(List<NavItem> items, bool interrupt = true)
        {
            Selectable previousAnchor = (_currentIndex >= 0 && _currentIndex < _items.Count) ? _items[_currentIndex].Anchor : null;
            bool sameSet = SameSet(_items, items);

            _items = items;

            int preservedIndex = previousAnchor != null ? _items.FindIndex(i => i.Anchor == previousAnchor) : -1;

            if (preservedIndex < 0 && _lastTopWindow != null && _rememberedAnchors.TryGetValue(_lastTopWindow, out var remembered))
            {
                preservedIndex = _items.FindIndex(i => i.Anchor == remembered);
                _rememberedAnchors.Remove(_lastTopWindow);
            }

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

        private UIWindow GetTopWindow()
        {
            var openWindows = MainUI.GetCurrentOpenWindows(1);
            if (openWindows == null || openWindows.Count == 0)
            {
                _manualWindowOverride = null;
                return null;
            }

            if (_manualWindowOverride != null)
            {
                // Confirmed live (debug log): a chest's GameInventoryUI never gets added to
                // this open-window list at all - same root cause family as SlotUI.container
                // being null there (the inventory shown next to a chest doesn't go through
                // the normal "open as a window" flow). So the override can legitimately be
                // a window that's never IN this list - only clear it once the chest itself
                // (the thing that made the override meaningful) isn't open anymore.
                bool chestStillOpen = false;
                foreach (var w in openWindows)
                {
                    if (IsStationWindow(w)) { chestStillOpen = true; break; }
                }
                if (chestStillOpen || openWindows.Contains(_manualWindowOverride))
                {
                    return _manualWindowOverride;
                }
            }

            _manualWindowOverride = null;
            return openWindows.Last.Value;
        }

        // Seta direita/esquerda swap which of the chest's ContainerUI and the player's
        // GameInventoryUI this navigator scans, whenever a chest is open - only the chest
        // window is ever findable via MainUI.GetCurrentOpenWindows (confirmed live: the
        // inventory shown next to it never appears there), so GameInventoryUI is reached
        // directly via its own singleton instead of being looked up the same way.
        // Round 115: a "station" window that opens the player's GameInventoryUI alongside itself,
        // so the right/left arrow can swap between the station's slots and the player inventory -
        // chests (ContainerUI, incl. BigContainerUI menu table) AND the drinks dispenser/beer tap
        // (DrinkDispenserUI : UIWindow, NOT a ContainerUI, which is why the switch used to skip it
        // - confirmed in the log: "switch skipped - no chest open, openWindows=[DrinkDispenserUI]";
        // DrinkDispenserUI.OpenUI calls GameInventoryUI.Get(..).OpenUI()).
        private static bool IsStationWindow(UIWindow w) => w is ContainerUI || w is DrinkDispenserUI;

        private static UIWindow GetOpenStationWindow()
        {
            var windows = MainUI.GetCurrentOpenWindows(1);
            if (windows == null) return null;
            foreach (var w in windows) { if (IsStationWindow(w)) return w; }
            return null;
        }

        private void HandleContainerInventorySwitch()
        {
            if (!Input.GetKeyDown(KeyCode.RightArrow) && !Input.GetKeyDown(KeyCode.LeftArrow)) return;

            var openWindows = MainUI.GetCurrentOpenWindows(1);
            if (openWindows == null) return;

            UIWindow stationWindow = null;
            foreach (var window in openWindows)
            {
                if (IsStationWindow(window)) { stationWindow = window; break; }
            }
            if (stationWindow == null)
            {
                if (Main.DebugMode)
                {
                    string names = string.Join(", ", System.Linq.Enumerable.Select(openWindows, w => w.GetType().Name));
                    DebugLogger.LogState($"KeyboardUINavigator: switch skipped - no station open, openWindows=[{names}]");
                }
                return;
            }

            UIWindow inventoryWindow = GameInventoryUI.Get(1);
            if (inventoryWindow == null)
            {
                if (Main.DebugMode) DebugLogger.LogState("KeyboardUINavigator: switch skipped - GameInventoryUI.Get(1) returned null");
                return;
            }

            UIWindow current = _manualWindowOverride ?? GetTopWindow();
            bool toInventory = current == stationWindow;
            _manualWindowOverride = toInventory ? inventoryWindow : stationWindow;
            if (Main.DebugMode) DebugLogger.LogState($"KeyboardUINavigator: switched focus to {(toInventory ? "inventory" : "station")}");

            // Round 115: announce which side we moved to, and call out an empty inventory instead
            // of going silent (user: "se for isso, deve informar só, sem nada no inv para add").
            if (toInventory)
            {
                int items = CountInventoryItems(inventoryWindow);
                ScreenReader.Say(items == 0 ? "Inventário vazio, nada pra adicionar" : "Inventário", interrupt: true);
            }
            else
            {
                ScreenReader.Say("Estação", interrupt: true);
            }
        }

        // Round 115: count non-empty slots in a GameInventoryUI so we can tell the player when the
        // inventory side has nothing to add (instead of silently focusing empty slots).
        private static int CountInventoryItems(UIWindow inventoryWindow)
        {
            if (inventoryWindow == null) return 0;
            int count = 0;
            foreach (var slot in inventoryWindow.GetComponentsInChildren<SlotUI>(includeInactive: false))
            {
                if (slot != null && slot.IHENCGDNPBL?.itemInstance != null) count++;
            }
            return count;
        }

        // Named by parent folder (PlayerName/TavernName), not the placeholder text, so the
        // label stays correct (and the same) before, during, and after typing.
        private static string GetInputFieldLabel(Selectable selectable)
        {
            return selectable.transform.parent?.name == "PlayerName" ? "Nome"
                : selectable.transform.parent?.name == "TavernName" ? "Nome da taverna"
                : UITextExtractor.GetReadableText(selectable.gameObject);
        }

        private static string DescribeMainQuest(MainQuestItemUI mainQuestItem)
        {
            var texts = mainQuestItem.GetComponentsInChildren<TMPro.TextMeshProUGUI>(includeInactive: false)
                .Select(UITextExtractor.GetReadableText)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
            return texts.Count == 0 ? "Missão" : $"Missão: {string.Join(". ", texts)}";
        }

        private void AnnounceYesNoQuestionIfChanged(YesNoDialogueUI yesNoWindow)
        {
            if (yesNoWindow.boxText == null) return;

            string question = UITextExtractor.GetReadableText(yesNoWindow.boxText);
            if (string.IsNullOrEmpty(question) || question == _lastAnnouncedYesNoQuestion) return;

            // Found via the bed: Bed.OnTriggerEnter2D opens this purely by proximity (no key
            // press at all), with the question ("Dormir?") shown only as plain text in
            // boxText - never read by the generic per-item scan, so the user had no idea a
            // confirmation popup had even appeared and resorted to the mouse.
            _lastAnnouncedYesNoQuestion = question;
            DebugLogger.LogState($"YesNo question announced: \"{question}\"");
            ScreenReader.Say(question, interrupt: false);
        }

        private void AnnounceEncyclopediaContentIfChanged(EncyclopediaUI encyclopediaWindow)
        {
            if (EncyclopediaSectionTitleField == null || EncyclopediaSectionTextField == null) return;

            var titleLabel = EncyclopediaSectionTitleField.GetValue(encyclopediaWindow) as TMPro.TextMeshProUGUI;
            var bodyLabel = EncyclopediaSectionTextField.GetValue(encyclopediaWindow) as TMPro.TextMeshProUGUI;
            if (titleLabel == null || bodyLabel == null || !bodyLabel.gameObject.activeInHierarchy) return;

            string title = UITextExtractor.GetReadableText(titleLabel);
            string body = UITextExtractor.GetReadableText(bodyLabel);
            if (string.IsNullOrEmpty(body)) return;

            string combined = string.IsNullOrEmpty(title) ? body : $"{title}. {body}";
            if (combined == _lastAnnouncedEncyclopediaContent) return;

            _lastAnnouncedEncyclopediaContent = combined;
            DebugLogger.LogState($"Encyclopedia content announced: \"{combined}\"");
            ScreenReader.Say(combined, interrupt: false);
        }

        private List<NavItem> CollectItems(bool justOpened)
        {
            UIWindow topWindow = GetTopWindow();

            if (topWindow == null)
            {
                return new List<NavItem>();
            }

            // group 0 = the window's own content (e.g. the tab strip) - always listed first.
            // group 1 = the selected tab's content - listed after.
            var rootGroups = new List<(GameObject root, int group)>();
            if (topWindow.content != null) rootGroups.Add((topWindow.content, 0));

            var optionsMenu = topWindow as OptionsMenuUI;
            var characterCreator = topWindow as CharacterCreatorUI;
            SoundMenuUI soundMenu = null;
            GraphicsMenuUI graphicsMenu = null;
            OthersMenuUI othersMenu = null;
            MainQuestItemUI mainQuestItem = null;
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

            // MainPanelUI (Inventory/Map/Encyclopedia/Quests/Recipes/Skills/etc. tab strip)
            // has the exact same "tab strip + selected tab's own content" structure as
            // Options. First attempt used "is this tab's content active in the hierarchy"
            // as the selection signal, like Options - but confirmed wrong live: ALL tabs'
            // content stays active simultaneously (non-selected ones are just moved far
            // off-screen, not deactivated - matches a `new Vector3(20000f, 20000f, 0f)`
            // offset seen in decompiled source), so that scanned a scrambled mix of
            // Inventory + Quests + Recipes + Skills + Collections items all at once
            // (confirmed live: 57 items found from one screen, navigation felt random).
            // Reading the game's own private "selected tab index" field instead - same
            // approach already used elsewhere (CharacterCreator gender state, etc.).
            var mainPanel = topWindow as MainPanelUI;
            if (mainPanel != null && mainPanel.panelsUI != null && MainPanelSelectedIndexField != null)
            {
                int selectedIndex = (int)MainPanelSelectedIndexField.GetValue(mainPanel);
                if (selectedIndex >= 0 && selectedIndex < mainPanel.panelsUI.Length)
                {
                    var selectedPanel = mainPanel.panelsUI[selectedIndex];
                    if (selectedPanel != null && selectedPanel.content != null)
                    {
                        rootGroups.Add((selectedPanel.content, 1));

                        // The active main story quest (MainQuestItemUI) used to be announced
                        // separately (side-channel) instead of being a real navigable item -
                        // user explicitly asked to find/interact with it in the list instead of
                        // having it read automatically/out of context. Earlier note that it had
                        // "no Button at all" was wrong: GetComponent<Button>() on
                        // MainQuestItemUI's own root finds nothing, but it has a public `button`
                        // field pointing to a child Button (confirmed in decompiled source,
                        // wired to ButtonClicked() - toggles the focused/pinned mission). Picked
                        // up explicitly below since it may sit outside the tab's `content` root.
                        var questLog = selectedPanel as QuestLogUI;
                        mainQuestItem = questLog?.GetComponentInChildren<MainQuestItemUI>(includeInactive: false);
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

            if (mainQuestItem != null && mainQuestItem.button != null)
            {
                // .Distinct() below covers the case where it turns out to already be inside
                // the tab's `content` root after all.
                found.Add((mainQuestItem.button, 1));
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

            // Encyclopedia's 13 section tab buttons are all named identically
            // ("ButtonComponents", no text child of their own), so they all read back the
            // same generic humanized name ("Button Components") - confirmed live, user
            // reported the list as "bagunçada" (impossible to tell sections apart). The real
            // per-section name exists as a localization key
            // (encyclopediaData.sections[i].sectionTitleID). The digit in "SectionN" isn't a
            // reliable index into that array (confirmed live: hierarchy order was
            // Section1,2..8,11,9,10,12,13, not numeric) - matched by POSITION in `visible`
            // instead (the actual Y/X-sorted order the user hears), against the data array
            // order. Matches how this kind of parallel UI/data list is normally authored
            // (same order on both sides, just not reflected in the GameObject names).
            Dictionary<Selectable, string> encyclopediaSectionTitles = null;
            Selectable encyclopediaBackButton = null;
            var encyclopediaWindow = topWindow as EncyclopediaUI;
            if (encyclopediaWindow != null && encyclopediaWindow.encyclopediaData != null)
            {
                var sectionButtons = visible
                    .Where(f => f.selectable.gameObject.name == "ButtonComponents" && HierarchyPath(f.selectable.transform, 6).Contains("TabsListContent"))
                    .Select(f => f.selectable)
                    .ToList();
                var sections = encyclopediaWindow.encyclopediaData.sections;
                if (sectionButtons.Count != sections.Length)
                {
                    DebugLogger.LogState($"Encyclopedia: {sectionButtons.Count} section buttons found vs {sections.Length} in data - position mapping may be off");
                }

                // The real "Voltar" button is the same recurring unlabeled "VersatileButton"
                // pattern seen elsewhere (MainPanelUI tabs) - confirmed via live debug log
                // (path "EncyclopediaUI/.../VersatileButton/Button"), NOT under
                // "TabsListContent" like the 14 section buttons, which is why the previous
                // guess (matching anything under TabsListContent) found nothing and instead
                // ended up moving an unrelated subsection item around. Matched by exact
                // parent name instead.
                encyclopediaBackButton = visible
                    .Where(f => f.selectable.gameObject.name == "Button" && f.selectable.transform.parent != null && f.selectable.transform.parent.name == "VersatileButton")
                    .Select(f => f.selectable)
                    .FirstOrDefault();
                encyclopediaSectionTitles = new Dictionary<Selectable, string>();
                int titleCount = Math.Min(sectionButtons.Count, sections.Length);
                for (int i = 0; i < titleCount; i++)
                {
                    encyclopediaSectionTitles[sectionButtons[i]] = LocalisationSystem.Get(sections[i].sectionTitleID);
                }

                AnnounceEncyclopediaContentIfChanged(encyclopediaWindow);
            }

            var yesNoWindow = topWindow as YesNoDialogueUI;
            if (yesNoWindow != null)
            {
                AnnounceYesNoQuestionIfChanged(yesNoWindow);
            }

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
                        // Options-style: the row itself is a Selectable wrapping a
                        // "MultiSelection" child that holds Previous/Next.
                        multiPrev = multiSelection.Find("PreviousButton")?.GetComponent<Button>();
                        multiNext = multiSelection.Find("NextButton")?.GetComponent<Button>();
                        if (multiPrev != null && multiNext != null)
                        {
                            multiValueReader = BuildMultiSelectValueReader(selectable.gameObject.name, graphicsMenu, othersMenu)
                                ?? (() => UITextExtractor.GetReadableText(selectable.gameObject));
                        }
                    }
                    else if (selectable.gameObject.name == "PreviousButton")
                    {
                        // Character Creator style: PreviousButton/NextButton are siblings of
                        // each other (no wrapping "MultiSelection" object), with a separate
                        // sibling label holding the current value (e.g. "Olhos 1").
                        var rowParent = selectable.transform.parent;
                        var siblingNext = rowParent?.Find("NextButton")?.GetComponent<Button>();
                        if (siblingNext != null)
                        {
                            multiPrev = selectable.GetComponent<Button>();
                            multiNext = siblingNext;
                            var rowLabel = rowParent.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: false)
                                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.text));
                            multiValueReader = rowLabel != null ? () => UITextExtractor.GetReadableText(rowLabel) : null;
                        }
                    }
                }

                Func<bool> selectedStateReader = null;
                string activationAnnouncement = null;
                Func<string> labelReader = null;

                if (encyclopediaSectionTitles != null && encyclopediaSectionTitles.TryGetValue(selectable, out var sectionTitle))
                {
                    labelReader = () => sectionTitle;
                }

                if (encyclopediaBackButton != null && selectable == encyclopediaBackButton)
                {
                    labelReader = () => "Voltar";
                }

                if (mainQuestItem != null && selectable == mainQuestItem.button)
                {
                    var capturedMainQuestItem = mainQuestItem;
                    labelReader = () => DescribeMainQuest(capturedMainQuestItem);
                }

                if (characterCreator != null)
                {
                    if (selectable.gameObject.name == "Male")
                    {
                        selectedStateReader = () => IsGenderFocused(characterCreator, CharacterCreatorMaleFocusedField);
                    }
                    else if (selectable.gameObject.name == "Female")
                    {
                        selectedStateReader = () => IsGenderFocused(characterCreator, CharacterCreatorFemaleFocusedField);
                    }
                    else if (selectable.transform.parent != null && selectable.transform.parent.name == "Random")
                    {
                        labelReader = () => "Personagem aleatório";
                        activationAnnouncement = "Personagem aleatório";
                    }
                    else if (selectable.gameObject.name == "ButtonLeft")
                    {
                        labelReader = () => "Girar visualização";
                        activationAnnouncement = "Girando visualização";
                    }
                }

                if (selectable.GetComponent<ColorButton>() != null)
                {
                    labelReader = () => DescribeColor(selectable);
                }

                // User reported container slots (e.g. opening a chest) reading the generic
                // GameObject name ("New SlotUI Inventory") instead of the item inside, and
                // one slot reading nothing at all - confirmed in decompiled source: SlotUI
                // exposes its underlying Slot/ItemInstance/Item publicly, no UI text needed.
                // Round 112: oven crafting UI. The recipe LIST entry is a RecipeSlot (confirmed via
                // the round-111 diagnostic: it had neither RecipeElementUI nor SlotUIRecipe;
                // GameCraftingUI builds the list from RecipeSlot prefabs, each holding a public
                // .recipe). Read it as the dish NAME + whether it's craftable + the ingredients it
                // needs (so the player can see requirements BEFORE Enter immediately crafts it - the
                // user's "preciso de um modo para ver o q a receita precisa"). A recipe INGREDIENT
                // slot is a SlotUIRecipe and reads as the required ingredient + owned count.
                var recipeListEntry = selectable.GetComponent<RecipeSlot>();
                var recipeSlot = selectable.GetComponent<SlotUIRecipe>();
                if (Main.DebugMode && (recipeListEntry != null || recipeSlot != null || selectable.gameObject.name.Contains("Recipe")))
                    DebugLogger.LogState($"CraftingNav: \"{selectable.gameObject.name}\" recipeListEntry={(recipeListEntry != null)} recipeSlot={(recipeSlot != null)}");
                if (recipeListEntry != null && recipeListEntry.recipe != null)
                {
                    labelReader = () => DescribeRecipeListEntry(recipeListEntry);
                }
                else if (recipeSlot != null)
                {
                    labelReader = () => DescribeRecipeSlot(recipeSlot);
                }

                var slotUI = selectable.GetComponent<SlotUI>() ?? selectable.GetComponentInParent<SlotUI>();
                if (slotUI != null && recipeListEntry == null && recipeSlot == null)
                {
                    labelReader = () => DescribeSlotUI(slotUI);
                }

                var ownInputField = selectable.GetComponent<TMP_InputField>();
                if (ownInputField != null)
                {
                    string fieldLabel = GetInputFieldLabel(selectable);
                    labelReader = () => $"{fieldLabel}: {(string.IsNullOrEmpty(ownInputField.text) ? "vazio" : ownInputField.text)}";
                }

                // Icon-only buttons with no text child of their own - their GameObject name
                // is just the generic "Button", so GetReadableText falls back to that.
                // Confirmed live in the Pause menu ("botões sem rótulo"). Labeled by parent
                // icon name instead.
                if (selectable.gameObject.name == "Button" && selectable.transform.parent != null)
                {
                    switch (selectable.transform.parent.name)
                    {
                        case "DiscordIcon":
                            labelReader = () => "Discord";
                            break;
                        case "UnstuckIcon":
                            labelReader = () => "Destravar personagem";
                            break;
                    }

                    // "VersatileButton" is a recurring icon-only action button reused across
                    // several MainPanelUI tabs (Inventory, Recipes, Quests, Collections) for
                    // different actions each time - confirmed live in Quests it reads as just
                    // "Button (8 of 9)" with no clue what it does. Not in decompiled source
                    // (pure scene/prefab name, no field to read), so no safe label to assign
                    // yet. Logs its icon sprite name (if any) as a diagnostic instead of
                    // guessing - real per-panel meaning needs more evidence first.
                    if (Main.DebugMode && selectable.transform.parent.name == "VersatileButton")
                    {
                        var icon = selectable.GetComponentInChildren<UnityEngine.UI.Image>();
                        string spriteName = icon != null && icon.sprite != null ? icon.sprite.name : "(sem sprite)";
                        DebugLogger.LogState($"VersatileButton sem rótulo: ícone=\"{spriteName}\" path={HierarchyPath(selectable.transform, 6)}");
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
                    SelectedStateReader = selectedStateReader,
                    ActivationAnnouncement = activationAnnouncement,
                    LabelReader = labelReader,
                });
            }

            if (encyclopediaBackButton != null)
            {
                int backIndex = result.FindIndex(r => r.Anchor == encyclopediaBackButton);
                if (backIndex >= 0 && backIndex != result.Count - 1)
                {
                    var backItem = result[backIndex];
                    result.RemoveAt(backIndex);
                    result.Add(backItem);
                }
            }

            if (encyclopediaWindow != null)
            {
                ReorderEncyclopediaSubsections(result);
            }

            return result;
        }

        // An expanded Encyclopedia section's subsections are Unity-instantiated clones of
        // one prefab, all under a "SubSections" parent - Unity's own duplicate-name rule
        // names the first one with the bare name ("Subsection") and only suffixes the
        // copies ("Subsection (1)".."(4)"), confirmed live: the bare one is really
        // subsection #1 (read as "1.1 Controles Básicos" once reachable), not the others.
        // Its on-screen Y doesn't match that reading order, so the normal Y-sort buried it
        // at the very end of the whole list instead of right after its section button
        // (confirmed live: user couldn't reach "Controles Básicos" at all by arrow keys).
        // Re-sorted here by the (N) suffix instead (bare name = first).
        private static void ReorderEncyclopediaSubsections(List<NavItem> result)
        {
            var groups = result
                .Select((item, index) => (item, index))
                .Where(p => p.item.Anchor.transform.parent != null && p.item.Anchor.transform.parent.name == "SubSections")
                .GroupBy(p => p.item.Anchor.transform.parent.GetInstanceID())
                .ToList();

            foreach (var group in groups)
            {
                var ordered = group.OrderBy(p => SubsectionSuffix(p.item.Anchor.gameObject.name)).Select(p => p.item).ToList();
                int firstIndex = group.Min(p => p.index);
                foreach (var index in group.Select(p => p.index).OrderByDescending(i => i))
                {
                    result.RemoveAt(index);
                }
                result.InsertRange(firstIndex, ordered);
            }
        }

        private static int SubsectionSuffix(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"\((\d+)\)$");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static bool IsGenderFocused(CharacterCreatorUI characterCreator, System.Reflection.FieldInfo focusedImageField)
        {
            var image = focusedImageField?.GetValue(characterCreator) as Image;
            if (image == null) return false;
            // SetMaleGender()/SetFemaleGender() paint the selected side's image full white
            // and the other side gray (confirmed in decompiled source) - no other state flag.
            return image.color.r > 0.9f && image.color.g > 0.9f && image.color.b > 0.9f;
        }

        // Expanded from a 12-color primary palette after live testing showed natural
        // tones (eyes/hair/skin) collapsing into just "cinza"/"marrom" repeatedly - those
        // are rarely pure RGB primaries, so the palette needed more in-between/muted shades.
        private static readonly (string name, Color color)[] NamedColors =
        {
            ("vermelho", new Color(0.8f, 0.1f, 0.1f)),
            ("vermelho escuro", new Color(0.5f, 0.05f, 0.05f)),
            ("verde", new Color(0.2f, 0.6f, 0.2f)),
            ("verde escuro", new Color(0.1f, 0.3f, 0.1f)),
            ("verde oliva", new Color(0.5f, 0.5f, 0.2f)),
            ("azul", new Color(0.2f, 0.3f, 0.8f)),
            ("azul claro", new Color(0.5f, 0.7f, 0.9f)),
            ("azul escuro", new Color(0.1f, 0.15f, 0.4f)),
            ("azul acinzentado", new Color(0.4f, 0.5f, 0.55f)),
            ("amarelo", new Color(0.9f, 0.85f, 0.2f)),
            ("laranja", new Color(0.85f, 0.45f, 0.1f)),
            ("roxo", new Color(0.5f, 0.2f, 0.55f)),
            ("rosa", new Color(0.85f, 0.55f, 0.6f)),
            ("rosa claro", new Color(0.95f, 0.8f, 0.8f)),
            ("avermelhado", new Color(0.75f, 0.4f, 0.35f)),
            ("amarelado", new Color(0.85f, 0.75f, 0.55f)),
            ("castanho claro", new Color(0.55f, 0.4f, 0.25f)),
            ("castanho", new Color(0.4f, 0.27f, 0.15f)),
            ("castanho escuro", new Color(0.25f, 0.15f, 0.08f)),
            ("marrom", new Color(0.35f, 0.22f, 0.1f)),
            ("marrom escuro", new Color(0.18f, 0.1f, 0.05f)),
            ("ruivo", new Color(0.6f, 0.25f, 0.1f)),
            ("loiro", new Color(0.8f, 0.65f, 0.35f)),
            ("preto", new Color(0.05f, 0.05f, 0.05f)),
            ("branco", new Color(0.95f, 0.95f, 0.95f)),
            ("cinza claro", new Color(0.75f, 0.75f, 0.75f)),
            ("cinza", new Color(0.5f, 0.5f, 0.5f)),
            ("cinza escuro", new Color(0.25f, 0.25f, 0.25f)),
            ("ciano", new Color(0.2f, 0.7f, 0.7f)),
        };

        private static readonly System.Reflection.FieldInfo ColorButtonMaterialField =
            typeof(ColorButton).GetField("_material", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo ColorButtonSpriteColorField =
            typeof(ColorButton).GetField("_spriteColor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static string DescribeSlotUI(SlotUI slotUI)
        {
            var slot = slotUI.IHENCGDNPBL;
            var item = slot?.itemInstance?.LHBPOPOIFLE();
            // User questioned whether a chest reporting all slots "Vazio" was a bug or a
            // genuinely empty chest - logging the raw lookup result (slot null? itemInstance
            // null? item null?) instead of guessing which one it is.
            if (Main.DebugMode) DebugLogger.LogState($"DescribeSlotUI: gameObject=\"{slotUI.gameObject.name}\" slot={(slot == null ? "null" : "set")} itemInstance={(slot?.itemInstance == null ? "null" : "set")} item={(item == null ? "null" : item.nameId)}");
            if (item == null) return "Vazio";

            // Found the real bug behind "Vazio" on a chest the user confirmed had the mop
            // inside: the diagnostic above showed item=itemMop (a real item was found), but
            // LocalisationSystem.Get(item.nameId) came back empty regardless - because
            // nameId isn't always a direct lookup key. Item.IABAKHPEOAF() (decompiled) is the
            // item's own proper name-resolution method - it knows about translationByID
            // (which uses a different "Items/item_name_<id>" key instead of nameId) and
            // falls back to the raw asset name if no translation exists at all, so it never
            // incorrectly reports "Vazio" for a real item.
            string name = item.IABAKHPEOAF();
            if (string.IsNullOrEmpty(name)) return "Vazio";
            // User asked for the quantity to be announced when a slot holds a stack
            // (e.g. 10 candles). Slot.Stack (decompiled) is the live count; only announce
            // it when it's a real stack (>1) to avoid noise on single items.
            int stack = slot.Stack;
            return stack > 1 ? $"{name}, {stack}" : name;
        }

        // Round 112: a recipe entry (RecipeSlot) in the oven list - announce the dish name, whether
        // it can be crafted right now, and the ingredients it needs with owned counts, so the
        // player sees the requirements BEFORE Enter crafts it (the user's "preciso de um modo para
        // ver o q a receita precisa"). Recipe.ingredientsNeeded + Recipe.IABAKHPEOAF() (name).
        private static string DescribeRecipeListEntry(RecipeSlot entry)
        {
            var recipe = entry.recipe;
            string name = recipe.IABAKHPEOAF();
            if (string.IsNullOrEmpty(name)) name = "Receita";

            var pi = PlayerInventory.GetPlayer(1);
            var parts = new System.Collections.Generic.List<string>();
            bool canCraft = true;
            if (recipe.ingredientsNeeded != null)
            {
                foreach (var ing in recipe.ingredientsNeeded)
                {
                    if (ing.item == null) continue;
                    string iName = ing.item.IABAKHPEOAF();
                    if (string.IsNullOrEmpty(iName)) iName = "ingrediente";
                    int owned = pi != null ? pi.NumberOfItems(ing.item.JDJGFAACPFC()) : 0;
                    if (owned < ing.amount) canCraft = false;
                    parts.Add($"{iName} {ing.amount}, tem {owned}");
                }
            }
            if (Main.DebugMode) DebugLogger.LogState($"DescribeRecipeListEntry: \"{entry.gameObject.name}\" -> {name} canCraft={canCraft} ingredients={parts.Count}");
            string head = $"Receita: {name}. {(canCraft ? "Dá pra fazer" : "Faltam ingredientes")}";
            return parts.Count > 0 ? $"{head}. Precisa: {string.Join(", ", parts)}" : head;
        }

        // Round 110: a recipe slot - user's request: "no campo de ingredientes informe o que pede,
        // e se houver o ingrediente quanto tem; se não tiver, o nome e 0". Reports the required
        // ingredient, the amount the recipe needs, and how many the player owns
        // (PlayerInventory.NumberOfItems).
        private static string DescribeRecipeSlot(SlotUIRecipe slot)
        {
            var instance = slot.IHENCGDNPBL?.itemInstance;
            var item = instance?.LHBPOPOIFLE();
            if (item == null) return "Vazio";
            string name = item.IABAKHPEOAF();
            if (string.IsNullOrEmpty(name)) name = "Ingrediente";

            int needed = slot.IHENCGDNPBL.Stack;
            int owned = 0;
            var pi = PlayerInventory.GetPlayer(1);
            if (pi != null) owned = pi.NumberOfItems(item.JDJGFAACPFC());

            if (Main.DebugMode) DebugLogger.LogState($"DescribeRecipeSlot: \"{slot.gameObject.name}\" item={name} needed={needed} owned={owned}");
            return needed > 0 ? $"{name}, precisa {needed}, você tem {owned}" : $"{name}, você tem {owned}";
        }

        // The game has no name for each color swatch - this approximates one from the
        // underlying color value (nearest match in a small fixed palette), since exact color
        // names aren't available anywhere in the data. First test showed every swatch
        // reading as "branco" - the button's own Image stays a neutral white tint when the
        // game uses its material-swap system (useCharacterMaterial), so the real sample
        // color has to come from the assigned CharacterMaterial/SpriteColor data instead.
        private static string DescribeColor(Selectable selectable)
        {
            var colorButton = selectable.GetComponent<ColorButton>();
            Color? sample = null;

            if (colorButton != null && colorButton.useCharacterMaterial)
            {
                if (ColorButtonMaterialField?.GetValue(colorButton) is CharacterMaterial material)
                {
                    sample = material.sampleColor;
                }
            }
            else if (colorButton != null && ColorButtonSpriteColorField != null)
            {
                sample = ((SpriteColor)ColorButtonSpriteColorField.GetValue(colorButton)).color;
            }

            if (sample == null)
            {
                var image = selectable.GetComponent<Image>() ?? selectable.GetComponentInChildren<Image>();
                if (image != null) sample = image.color;
            }

            if (sample == null) return UITextExtractor.GetReadableText(selectable.gameObject);

            string best = "desconhecida";
            float bestDist = float.MaxValue;
            foreach (var (name, color) in NamedColors)
            {
                float dr = sample.Value.r - color.r, dg = sample.Value.g - color.g, db = sample.Value.b - color.b;
                float dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist) { bestDist = dist; best = name; }
            }

            if (Main.DebugMode)
            {
                DebugLogger.LogState($"DescribeColor: \"{selectable.gameObject.name}\" rgb=({sample.Value.r:F2},{sample.Value.g:F2},{sample.Value.b:F2}) -> {best}");
            }

            var match = System.Text.RegularExpressions.Regex.Match(selectable.gameObject.name, @"\d+$");
            return match.Success ? $"Cor {match.Value}: {best}" : $"Cor: {best}";
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
            if (go.name != "PreviousButton" && go.name != "NextButton") return false;
            var parent = go.transform.parent;
            if (parent == null) return false;

            // Options-style: both buttons live under a "MultiSelection" wrapper.
            if (parent.name == "MultiSelection") return true;

            // Character Creator style: PreviousButton/NextButton are direct siblings of
            // each other - hide NextButton only, PreviousButton becomes the collapsed row.
            return go.name == "NextButton" && parent.Find("PreviousButton") != null;
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

            // Only one item, or about to wrap past the top/bottom of the list - the normal
            // move sound ALWAYS plays (per the user's clarification: every item should
            // sound the same way regardless of position), with the boundary sound layered
            // ON TOP of it in this case, not replacing it.
            bool atBoundary = _items.Count == 1
                || (direction > 0 && _currentIndex == _items.Count - 1)
                || (direction < 0 && _currentIndex == 0);

            _currentIndex = (_currentIndex + direction + _items.Count) % _items.Count;
            UISound.PlayNavigate();
            if (atBoundary) UISound.PlayBoundaryDelayed();
            AnnounceCurrent();
        }

        private void AnnounceCurrent(bool interrupt = true)
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count) return;

            var entry = _items[_currentIndex];
            var item = entry.Anchor;
            string text = entry.LabelReader != null ? entry.LabelReader() : UITextExtractor.GetReadableText(item.gameObject);
            string message;

            if (entry.VolumeSlider != null)
            {
                message = $"{text}: {LevelText(entry.VolumeSlider, entry.IsAudioVolume)} ({_currentIndex + 1} of {_items.Count})";
            }
            else if (entry.MultiSelectPrev != null && entry.MultiSelectNext != null)
            {
                string rowLabel = item.gameObject.name == "PreviousButton" ? null : text;
                message = $"{FormatLabelValue(rowLabel, entry.MultiSelectValueReader())} ({_currentIndex + 1} of {_items.Count})";
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
                else if (entry.SelectedStateReader != null)
                {
                    message = $"{text}: {(entry.SelectedStateReader() ? "selected" : "not selected")} ({_currentIndex + 1} of {_items.Count})";
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

            // User's request: a sound confirming Enter was accepted (selecting something,
            // closing a window via a button) - sometimes hard to tell if a keypress
            // registered. Reuses the same click sound as navigation, on purpose - it just
            // means "the game saw your input", consistent with the dialogue-advance sound
            // added for the same reason (see DialogueAnnouncer).
            UISound.PlayNavigate();

            var entry = _items[_currentIndex];
            var item = entry.Anchor;
            string label = entry.LabelReader != null ? entry.LabelReader() : UITextExtractor.GetReadableText(item.gameObject);

            var inputField = item.GetComponent<TMP_InputField>();
            if (inputField != null)
            {
                inputField.ActivateInputField();
                EventSystem.current.SetSelectedGameObject(item.gameObject);
                _editingInputField = inputField;
                _editingFieldLabel = GetInputFieldLabel(item);
                DebugLogger.LogInput("Enter", $"Edit {item.gameObject.name}");
                ScreenReader.Announce($"Editando {_editingFieldLabel}. Digite o texto, Enter ou Escape para confirmar.");
                return;
            }

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
                string rowLabel = item.gameObject.name == "PreviousButton" ? null : label;
                StartAdjusting(rowLabel, () => prevButton.onClick.Invoke(), () => nextButton.onClick.Invoke(), reader);
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

            // Re-picking the same Encyclopedia subsection didn't re-announce its body text
            // (the "announce on change" check in AnnounceEncyclopediaContentIfChanged saw the
            // same text and skipped it) - user explicitly asked to be able to re-read it by
            // re-activating. Forcing the next scan to treat it as changed again.
            if (item.transform.parent != null && item.transform.parent.name == "SubSections")
            {
                _lastAnnouncedEncyclopediaContent = null;
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
            else if (entry.SelectedStateReader != null)
            {
                ScreenReader.Announce($"{label} selected");
            }
            else if (entry.ActivationAnnouncement != null)
            {
                ScreenReader.Announce(entry.ActivationAnnouncement);
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
