using MelonLoader;
using UnityEngine;
using System.Collections;

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

        /// <summary>
        /// Debug mode - when true, logs all screenreader output and detailed game state.
        /// Toggle with F12.
        /// </summary>
        public static bool DebugMode = false;

        // Handlers - one per feature/screen, added as features are implemented:
        // private InventoryHandler _inventoryHandler;
        private MenuAnnouncer _menuAnnouncer;
        private KeyboardUINavigator _keyboardNavigator;

        #endregion

        #region Lifecycle

        public override void OnInitializeMelon()
        {
            ScreenReader.Initialize();
            InitializeHandlers();
            MelonCoroutines.Start(AnnounceStartupDelayed());
        }

        private void InitializeHandlers()
        {
            _menuAnnouncer = new MenuAnnouncer();
            _menuAnnouncer.Initialize();
            _keyboardNavigator = new KeyboardUINavigator();
        }

        private IEnumerator AnnounceStartupDelayed()
        {
            // Short delay so the screen reader is ready
            yield return new WaitForSeconds(1f);
            ScreenReader.Announce("TravellersRestAccess loaded. F1 for help.");
        }

        public override void OnUpdate()
        {
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
            }

            return _gameReady;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene loaded: {sceneName}");
            DebugLogger.LogState($"Scene changed to: {sceneName}");
            _gameReady = false;
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
            _keyboardNavigator.Update();
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
