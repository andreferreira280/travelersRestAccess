using MelonLoader;
using UnityEngine;
using System.Collections;
using HarmonyLib;

// ============================================================================
// CRITICAL: Accessing game code
// ============================================================================
// Accessing game classes BEFORE the game is fully loaded crashes!
//
// FORBIDDEN in OnInitializeMelon() or earlier:
//   - Game manager singletons (GameManager.instance, etc.)
//   - typeof(GameClass) in Harmony attributes
//
// ONLY allowed from OnSceneWasLoaded() / once CheckGameReady() is true.
// ============================================================================

[assembly: MelonInfo(typeof(TravellersRestAccess.Main), "TravellersRestAccess", "0.1.0", "riknagaru")]
[assembly: MelonGame("Louqou", "TravellersRest")]

namespace TravellersRestAccess
{
    /// <summary>
    /// Main mod entry point. Coordinates all handlers and processes global hotkeys.
    ///
    /// Keep this class SMALL - only lifecycle methods and global hotkey dispatch.
    /// Put all feature logic in separate Handler classes.
    /// </summary>
    public class Main : MelonMod
    {
        #region Fields

        private bool _gameReady = false;
        private HarmonyLib.Harmony _harmony;
        private bool _patchesApplied;

        /// <summary>
        /// Debug mode - when true, logs all screenreader output and detailed game state.
        /// Toggle with F12.
        /// </summary>
        public static bool DebugMode = false;

        // Handlers - one per feature/screen, added as features are implemented:
        // private InventoryHandler _inventoryHandler;
        private MenuAnnouncer _menuAnnouncer;
        private KeyboardUINavigator _keyboardNavigator;
        private DialogueAnnouncer _dialogueAnnouncer;
        private WorldNavigationHandler _worldNavigationHandler;

        // "Carregando jogo..." kept getting cut off almost immediately by
        // DialogueAnnouncer announcing the loading screen's tip text (confirmed: MainUI
        // persists across scene loads, so _gameReady flips back true within a frame or two,
        // and Announce() always interrupts whatever is currently speaking) - give our own
        // announcement a clear run before any other announcer is allowed to speak. Counted
        // in FRAMES, not seconds: a real loading stall can make Time.unscaledTime jump by
        // several seconds in a single tick once it's done, which would silently skip past a
        // time-based window the instant things resume.
        private int _dialogueAnnouncerSuppressFrames;

        #endregion

        #region Lifecycle

        public override void OnInitializeMelon()
        {
            ScreenReader.Initialize();
            InitializeHandlers();
            _harmony = new HarmonyLib.Harmony("TravellersRestAccess");
            MelonCoroutines.Start(AnnounceStartupDelayed());
        }

        private void InitializeHandlers()
        {
            _menuAnnouncer = new MenuAnnouncer();
            _menuAnnouncer.Initialize();
            _keyboardNavigator = new KeyboardUINavigator();
            _dialogueAnnouncer = new DialogueAnnouncer();
            _worldNavigationHandler = new WorldNavigationHandler();
        }

        private IEnumerator AnnounceStartupDelayed()
        {
            // Short delay so the screen reader is ready
            yield return new WaitForSeconds(1f);
            ScreenReader.Announce("TravellersRestAccess loaded. F1 for help.");
        }

        public override void OnUpdate()
        {
            DebugLogger.LogRawKeyDowns();

            // Global hotkeys (F1 help, F12 debug toggle) work regardless of game state.
            if (ProcessHotkeys()) return;

            if (!CheckGameReady()) return;

            UpdateHandlers();
        }

        private bool CheckGameReady()
        {
            if (_gameReady) return true;

            if (MainUI.GetInstance() != null)
            {
                _gameReady = true;
                MelonLogger.Msg("Game ready");
                // User reported no custom sound at all this round - log confirmed ZERO
                // "CustomSounds:" lines (not even "loaded parede.wav", which always logged
                // in every earlier session). That points to loading itself silently failing
                // somewhere before this round's volume change, not the 60% volume value
                // itself (0.6 wouldn't go silent). Logging unconditionally + catching here to
                // pin down exactly where the chain breaks next test.
                DebugLogger.LogState("Main: calling CustomSounds.EnsureLoaded");
                try
                {
                    CustomSounds.EnsureLoaded();
                }
                catch (System.Exception ex)
                {
                    DebugLogger.LogState($"Main: CustomSounds.EnsureLoaded threw: {ex}");
                }

                if (!_patchesApplied)
                {
                    SpaceClosePatch.Apply(_harmony);
                    TutorialTracePatch.Apply(_harmony);
                    MovementAxisPatch.Apply(_harmony);
                    // User's explicit request 2026-06-19: arrow keys should never move the
                    // character, even outside menus (Up/Down stay free for re-reading
                    // dialogue - that's handled separately in DialogueAnnouncer, unaffected
                    // by this). Permanently on, not just while a nav screen is open.
                    MovementAxisPatch.SuppressArrowMovement = true;
                    _patchesApplied = true;
                }
            }

            return _gameReady;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene loaded: {sceneName}");
            DebugLogger.LogState($"Scene changed to: {sceneName}");
            _gameReady = false;

            // The loading screen's tip text gets picked up by DialogueAnnouncer's scene
            // scan on its own, but there was no announcement that the game was actually
            // loading at all - just a tip with no context (confirmed live).
            if (sceneName == "LoadingScene")
            {
                ScreenReader.Announce("Carregando jogo...");
                _dialogueAnnouncerSuppressFrames = 90;
            }
        }

        public override void OnApplicationQuit()
        {
            ScreenReader.Shutdown();
        }

        #endregion

        #region Hotkeys

        /// <summary>
        /// Processes global hotkeys. Returns true if a key was handled.
        /// Only dispatch to handlers here - don't put logic in Main!
        /// </summary>
        private bool ProcessHotkeys()
        {
            // F12 = Toggle debug mode
            if (Input.GetKeyDown(KeyCode.F12))
            {
                DebugMode = !DebugMode;
                var status = DebugMode ? "enabled" : "disabled";
                MelonLogger.Msg($"Debug mode {status}");
                ScreenReader.Say($"Debug mode {status}");
                return true;
            }

            // F1 = Help (always in Main)
            if (Input.GetKeyDown(KeyCode.F1))
            {
                DebugLogger.LogInput("F1", "Help");
                AnnounceHelp();
                return true;
            }

            // Other F-keys will dispatch to handlers as features are added.

            return false;
        }

        #endregion

        #region Handler Updates

        private void UpdateHandlers()
        {
            bool anyUiOpen = MainUI.IsAnyUIOpen(1);

            _keyboardNavigator.Update();
            _worldNavigationHandler.Update(anyUiOpen);

            if (_dialogueAnnouncerSuppressFrames > 0)
            {
                _dialogueAnnouncerSuppressFrames--;
                return;
            }

            _dialogueAnnouncer.Update(anyUiOpen);
        }

        #endregion

        #region Help

        private void AnnounceHelp()
        {
            string help = "Keys: F1 Help. F12 toggle debug mode.";

            ScreenReader.Announce(help);
        }

        #endregion
    }
}
