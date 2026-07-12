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
        private InventoryTransferHandler _inventoryTransferHandler;
        private DecorationModeHandler _decorationModeHandler;
        private FuelStationHandler _fuelStationHandler;
        private DrinkServingHandler _drinkServingHandler;
        private TableArrangeHandler _tableArrangeHandler;
        private MissionDiagnosticHandler _missionDiagnosticHandler;
        private float _lastGateLog; // throttle for the read-only input-gate diagnostic

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
            _inventoryTransferHandler = new InventoryTransferHandler();
            _decorationModeHandler = new DecorationModeHandler();
            _fuelStationHandler = new FuelStationHandler();
            _drinkServingHandler = new DrinkServingHandler();
            _tableArrangeHandler = new TableArrangeHandler();
            _missionDiagnosticHandler = new MissionDiagnosticHandler();
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
                    HotbarBlockPatch.Apply(_harmony);
                    HotbarSwapPatch.Apply(_harmony);
                    CleaningDebugPatch.Apply(_harmony);
                    TavernStatsPatch.Apply(_harmony);
                    ToolSoundPatch.Apply(_harmony);
                    ToolCursorAimPatch.Apply(_harmony);
                    ConstructionTableHandler.Apply(_harmony);
                    ConstructionInputInjector.Apply(_harmony);
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
            // While the typed-amount modal (Alt+Enter) is active, ONLY it runs - the digit/Enter/
            // Escape keys must not leak into the navigator or world handlers (mirrors the
            // navigator's own _editingInputField gate).
            if (_inventoryTransferHandler.IsTypingAmount)
            {
                _inventoryTransferHandler.UpdateTypingAmount();
                return;
            }

            bool anyUiOpen = MainUI.IsAnyUIOpen(1);

            // NOTE: an earlier "input unstick / input-blocker" experiment lived here (it read and
            // cleared PlayerInputs.inputBlockers and the EventSystem selection every frame to recover
            // a stuck E/Q/Esc gate). It was REMOVED: the mod-disabled test proved that block was what
            // HUNG area transitions (leaving the tavern) and added per-frame cost. The mod must not
            // touch the game's input-blocker bookkeeping.
            //
            // READ-ONLY diagnostic (NO mutation): logs the input gate + stuck blockers once/second so
            // we can see WHAT is left blocking input after an area transition, without touching it.
            if (DebugMode && !anyUiOpen && Time.unscaledTime - _lastGateLog > 1f)
            {
                _lastGateLog = Time.unscaledTime;
                try
                {
                    var pi = PlayerInputs.GetPlayer(1);
                    if (pi != null && !PlayerInputs.InputsEnabled(1) && pi.inputBlockers != null)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        foreach (var mb in pi.inputBlockers) parts.Add(mb == null ? "null" : $"{mb.GetType().Name}:{mb.gameObject.name}");
                        DebugLogger.LogState($"GateRead: InputsEnabled=False blockers=[{string.Join(", ", parts)}]");

                        // ROOT-CAUSE PROBE (read-only). The stuck input/movement blockers (TitleScreen
                        // at load, TravelZone after leaving an area) are BOTH released only once the
                        // destination area's terrain build finishes: TitleScreen.LoadingSeasonTilesProgressBar
                        // waits on TitleScreen.allTerrainUpdated (set by the Road TilemapScene terrain
                        // coroutine), and the TravelZone fade-in coroutine (LFDIBPFMMAK) waits on
                        // TilemapScene.updatingTerrain going false. If that one terrain coroutine never
                        // completes, BOTH symptoms appear (menus dead + can't change area). This logs the
                        // exact terrain/fade state so the next single test proves whether the terrain
                        // build is stuck (progress frozen => coroutine aborted) or the fade is stuck.
                        LogTransitionDiagnostic();
                    }
                }
                catch { }
            }

            // While ANY area is mid terrain-build (initial world load OR an area transition), the game
            // keeps the player's input+movement blocked until the build finishes. The mod has nothing
            // useful to do in that window, and its heavy per-frame work (world scans + F12 debug logging
            // = disk I/O) was STARVING the frame-bound terrain build: progress crawled ~0.001/s so it
            // effectively never finished, the blockers never released, and everything stayed frozen
            // (menus dead + can't leave an area). Skip ALL handlers during the build so it gets full
            // frames and completes quickly. Input is blocked anyway, so nothing is lost.
            if (WorldNavigationHandler.AnyTerrainUpdating()) return;

            _keyboardNavigator.Update();
            _worldNavigationHandler.Update(anyUiOpen);
            // Hooked unconditionally (not just while a UI is open): the user's explicit
            // request to announce which hotbar item got selected happens while walking
            // around the world (plain 1-8, the game's own native control), not in a menu.
            _inventoryTransferHandler.EnsureHotbarSelectionAnnouncer();
            CleaningDebugPatch.PollFocus();
            _decorationModeHandler.Update();
            _fuelStationHandler.Update();
            _drinkServingHandler.Update();
            _tableArrangeHandler.Update(anyUiOpen);
            _missionDiagnosticHandler.Update();
            ConstructionTableHandler.Update();
            HuntingHandler.Update();
            // The FuelUI (oven/malt/fermentation fuel) is owned entirely by FuelStationHandler.
            // Skip the transfer handler there so its Ctrl+Enter routing can't fire alongside the
            // fuel add (that double-handling is what scrambled the hotbar in the reverted attempts).
            bool fuelOpen = false;
            try { var fu = FuelUI.Get(1); fuelOpen = fu != null && fu.IsOpen(); } catch { }
            if (anyUiOpen && !fuelOpen) _inventoryTransferHandler.Update(_keyboardNavigator.GetCurrentSelectedGameObject());

            if (_dialogueAnnouncerSuppressFrames > 0)
            {
                _dialogueAnnouncerSuppressFrames--;
                return;
            }

            _dialogueAnnouncer.Update(anyUiOpen);
        }

        /// <summary>
        /// Read-only probe (NO mutation). Logs the terrain-build + fade state during a stuck
        /// input-gate window so we can see WHY the blockers never clear. Called only in DebugMode,
        /// throttled to once/second, and only while input is disabled (the stuck window).
        /// </summary>
        private void LogTransitionDiagnostic()
        {
            // TitleScreen loading gate: the title/loading blocker is removed only after
            // allTerrainUpdated flips true. If progress is frozen below 1, the Road terrain
            // coroutine aborted mid-loop (an exception the game swallows - not in this log).
            try
            {
                var ts = TitleScreen.GetInstance();
                if (ts != null)
                    DebugLogger.LogState($"DIAG: TitleScreen.allTerrainUpdated={ts.allTerrainUpdated} progress={ts.allTerrainUpdatedProgress:F3}");
            }
            catch { }

            // Per-area terrain build state: any scene still 'updatingTerrain' is what the
            // TravelZone fade-in coroutine blocks on. A scene stuck at updatingTerrain=true is
            // the smoking gun.
            try
            {
                var mgr = TravelZonesManager.GGFJGHHHEJC;
                if (mgr != null && mgr.allTilemapScenes != null)
                {
                    var busy = new System.Collections.Generic.List<string>();
                    foreach (var kv in mgr.allTilemapScenes)
                        if (kv.Value != null && kv.Value.updatingTerrain) busy.Add(kv.Key.ToString());
                    DebugLogger.LogState($"DIAG: updatingTerrain scenes=[{string.Join(", ", busy)}]");
                }
            }
            catch { }

            // Fade state: distinguishes 'stuck before fade-in' (terrain) from 'fade never finishes'.
            try
            {
                var fc = FadeCamera.GetPlayer(1);
                if (fc != null)
                    DebugLogger.LogState($"DIAG: Fade isFading={fc.IsFading()} isBlack={fc.IsBlack()} isClear={fc.IsClear()}");
            }
            catch { }
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
