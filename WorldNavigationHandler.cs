using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TravellersRestAccess
{
    /// <summary>
    /// Object/world navigation feature. Stage 1 (feasibility) confirmed we can read live
    /// coordinates for navigation targets; Stage 2 adds the first real player-facing
    /// control: Page Up/Down cycles between the 2 known targets (tavern entrance door,
    /// player's bed) and announces the name. See docs/modules/world-object-navigation.md
    /// for the full staged plan.
    ///
    /// Also hosts a real accessibility fallback: Ctrl+Enter / Shift+Enter simulate a
    /// left/right mouse click on whatever the game's own interact-range system currently
    /// has in range. User-requested after finding some interactions only respond to a
    /// mouse click, not the keyboard action key shown on screen.
    /// </summary>
    public class WorldNavigationHandler
    {
        private const float LogInterval = 1f;
        private float _lastLogTime;
        private GameObject _lastInteractGO;
        private Door[] _cachedDoors;
        private float _lastDoorCacheTime = -999f;

        // Stage 2 target list. The entrance door has no reliable static reference (its
        // GameObject name "Door" is reused by every door prefab instance in the game, the
        // Cellar Door included) - confirmed live the ONE solid way to identify it is to
        // remember whichever Door the player actually opened via GetCurrentInteractGO(),
        // the first time it happens each session. The bed needs no such workaround -
        // Bed.GetPlayerBedPosition() is a direct static lookup, always available once the
        // bed exists in the scene.
        private Door _rememberedEntranceDoor;
        private int _currentTargetIndex = -1;

        // User's explicit request: group targets by category (Ctrl+Page Up/Down switches
        // category, Page Up/Down moves within the current one). Classified by real
        // component types found in decompiled source (Container, Crafter,
        // Placeable.canBeAddedToInventory) rather than guessed from names.
        // Round 102: "Missão" renamed to "Pendentes" (things still needing action) per user
        // request, and a new "Repositivos" category for placed consumables that are working but
        // will need restocking (candles). Associated benches leave "Pendentes" automatically (see
        // BuildTargetList - only unassociated benches are listed now).
        private static readonly string[] CategoryOrder = { "Portas", "Pendentes", "Repositivos", "Containers", "Máquinas", "Coletáveis", "Decorativos" };

        // Candle item id (confirmed in decompiled SurfaceSortOrder/HouseKeeper: ItemDatabaseAccessor
        // .GetItem(605) is the candle, and the live GameObjects are "605 - Vela(Clone)").
        public const int CandleItemId = 605;

        // User's explicit request: don't start with everything lumped together - default
        // to "Portas" until the player explicitly switches category.
        private string _currentCategory = "Portas";

        // How far counts as "same area" for auto-listing doors the player hasn't opened
        // yet - user's explicit request: entrances should be in the list just by being
        // nearby, not only after being used. No real "zone" tag exists per-Door to check
        // against (Location is only tracked for the PLAYER, not per-object) - distance is
        // a practical stand-in: confirmed live that doors in a different area sit 1000+
        // units away, while the tavern's own doors are single digits apart.
        private const float NearbyDoorRadius = 30f;

        private Location? _lastLocation;
        private static readonly Dictionary<Location, string> LocationNames = new Dictionary<Location, string>
        {
            { Location.Tavern, "Taverna" },
            { Location.Road, "Estrada" },
            { Location.River, "Rio" },
            { Location.Camp, "Acampamento" },
            { Location.Quarry, "Pedreira" },
            { Location.Farm, "Fazenda" },
            { Location.BarnInterior, "Celeiro" },
            { Location.FarmShop, "Loja da Fazenda" },
            { Location.CityOutside, "Cidade" },
            { Location.Mine, "Mina" },
            { Location.QuarryCave, "Caverna da Pedreira" },
            { Location.InnkeepersCave, "Caverna do Estalajadeiro" },
            { Location.Beach, "Praia" },
            { Location.WilsonHouse, "Casa do Wilson" },
            { Location.City, "Cidade" },
            { Location.CityTavern, "Taverna da Cidade" },
            { Location.Sawmill, "Serraria" },
            { Location.Blacksmith, "Ferraria" },
            { Location.ChristmasCave, "Caverna de Natal" },
            { Location.PetShop, "Loja de Animais" },
            { Location.CastleGarden, "Jardim do Castelo" },
            { Location.Port, "Porto" },
            { Location.PirateShip, "Navio Pirata" },
            { Location.PirateCave, "Caverna Pirata" },
            { Location.Castle, "Castelo" },
            { Location.Forest, "Floresta" },
            { Location.Bathhouse, "Casa de Banhos" },
            { Location.BathhouseInterior, "Casa de Banhos (interior)" },
            { Location.ButcherHouse, "Casa do Açougueiro" },
            { Location.KujakuHouse, "Casa do Kujaku" },
            { Location.VampireHouse, "Casa do Vampiro" },
        };

        // Tile size confirmed in decompiled WorldGrid.allNeighbours (0.5 world units per
        // cardinal step) - not guessed. Used for the footstep cue cadence (see
        // HandleFootsteps) and for the guidance "arrived" threshold.
        private const float TileSize = 0.5f;
        private Vector3? _lastFootstepPosition;

        // Stage 3: Home key toggles continuous direction+distance guidance to whichever
        // target is currently selected via Page Up/Down. Stored separately from
        // _currentTargetIndex because BuildTargetList() is rebuilt (and can change size/
        // order) every call as nearby doors come in and out of the 30-unit radius - the
        // index alone isn't a stable reference to keep walking towards.
        private (string name, Vector3 position)? _selectedTarget;
        private bool _guidanceActive;
        private Vector3? _lastGuidancePosition;

        // Real pathfinding (user's explicit priority: straight-line delta was sending
        // people into walls). Uses the game's own PathRequestManager.RequestPath - a real,
        // tested A* that already avoids walls/objects, running on its own background
        // thread with results delivered back via a callback on the main thread (confirmed
        // in decompiled source: PathRequestManager.Update() drains the result queue and
        // invokes the callback there, so it's safe to touch our own state inside it).
        // Wrapped in try/catch everywhere it's called - if the manager's internal static
        // instance isn't ready for any reason, this throws on OUR calling thread (not the
        // background one), which we catch and fall back to the old straight-line behavior
        // instead of risking a crash.
        //
        // Confirmed live the raw waypoint list (one entry every 0.25 units, sometimes
        // diagonal) read out as jittery, sometimes-increasing numbers - re-requesting the
        // route every couple seconds reset progress through it constantly. Re-requesting
        // much less often, and collapsing the raw waypoints into axis-aligned chunks
        // ("4 pra cima" instead of 8 separate 1-tile-apart announcements) per user's
        // explicit request fixes both.
        private const float PathRequestCooldown = 6f;
        private Vector2[] _currentPath;
        private List<(string direction, Vector3 endPosition)> _simplifiedSteps;
        private int _currentStepIndex;
        private Vector3 _lastPathRequestStart;
        private bool _pathRequestPending;
        private float _lastPathRequestTime;
        private bool _isInitialPathRequest;
        private Vector3 _lastRequestFrom;
        private Vector3 _lastRequestTo;
        private bool _isRetryAttempt;

        // User's explicit request: a log comparing how many movement-key taps it actually
        // took to clear a step against the number that was announced for it, so calibration
        // can be checked from hard numbers instead of guessed.
        private int _tapsForCurrentStep;
        private int _lastSpokenCountForStep = -1;

        // Wall-bump sound (empirical thresholds - no existing game signal for "blocked
        // movement" was found, so this compares ACTUAL frame-to-frame movement against the
        // MINIMUM expected for the player's own speed while input is held; if it stays far
        // below that for a short sustained window, the player is very likely pressed
        // against something solid. User's explicit request: raised the confirmation delay
        // by ~100ms (0.5s -> 0.6s) for the CONTINUOUS-hold case, and kept the sound LOOPING
        // for as long as they stay stuck (not discrete retriggers on a cooldown).
        // Round 111: shortened again 0.25->0.08 - user said the bump sound was still slow. The
        // sound starts instantly (persistent volume-toggle source) the moment this short threshold
        // is crossed. Walking ALONG a wall keeps the player displacing (so _wallStuckTime stays 0);
        // only genuinely-blocked movement accrues it, so ~5 frames still means "stuck", not a brush.
        private const float WallStuckSeconds = 0.08f;
        // Round 107: the spoken "Bloqueado por ..." warning fires at a shorter threshold than the
        // bump SOUND, so the player hears WHAT is in the way almost as soon as they push into it
        // ("um pouco mais rápido assim q eu virar para um lado bloqueado").
        private const float BlockerAnnounceSeconds = 0.2f;
        private Vector3? _lastWallCheckPosition;
        private float _wallStuckTime;
        // Round 105: speak WHAT is blocking the player when stuck on an item (round-105 log: the
        // player was wedged against "Grupo Ladrillos" - a tutorial brick pile - right at the tavern
        // door and had no way to know without reading the log). Announced once per blocker.
        private string _lastBumpBlockerSpoken;

        // User's explicit request: a single quick TAP into a wall produced no sound at all
        // (only after ~6 repeated taps, or holding, did it ever play). Root cause: the
        // original design waited for a frame where NO movement key was held to evaluate a
        // tap - but rapid tapping rarely produces such a frame (one key's release and the
        // next key's press land in the same or adjacent frames), so the evaluation almost
        // never ran; what actually played was the unrelated sustained-hold loop below,
        // accumulating wall-stuck time across the gaps between taps until it crossed
        // WallStuckSeconds. Fixed by scheduling an independent check a fixed short delay
        // after EACH key-down, regardless of hold/release state - a new key-down just
        // reschedules it, so a genuinely held key never fires this (the sustained loop
        // handles that case), but an isolated tap gets evaluated on its own.
        private const float SingleTapCheckDelay = 0.15f;
        private float? _pendingTapCheckTime;
        private Vector3? _pendingTapStartPosition;

        // User noticed the character turns to face a direction before actually moving
        // (confirmed real: PlayerController.GetPlayerDirection(1) reads the live facing
        // Direction enum, set independently of movement) and asked for a sound on that
        // turn, panned to match (left/right) or centered (up/down).
        private Direction? _lastFacingDirection;

        // User's explicit request: floor stains never reliably show the game's own "[E]
        // ..." on-screen hint (unlike the table, which does via DialogueAnnouncer's text
        // scan), so this announces it ourselves the moment the game's own proximity system
        // focuses on one - confirmed via CleaningDebugPatch's log that segurar "Interact"
        // (E) is the real, working trigger for FloorDirt specifically.
        private FloorDirt _lastFloorDirtFocus;

        // Unlike FloorDirt/Table, Seat isn't IProximity/registered in
        // InputByProximityManager (confirmed in decompiled source: plain MonoBehaviour, no
        // interfaces) - no game-provided "focused" concept to reuse, so this is a plain
        // distance check instead, same radius already used for item-proximity sounds.
        private Seat _lastNearSeat;

        public void Update(bool anyUiOpen)
        {
            // Confirmed live: PlayerController.GetPlayerPosition(1) (and several methods
            // below that call it) throws a NullReferenceException during brief moments
            // where GetPlayer(1) itself is null - a scene-internal transition/teleport, not
            // a full scene reload (CheckGameReady() in Main.cs doesn't catch this case). One
            // guard here instead of repeating it in every method that needs the player.
            if (PlayerController.GetPlayer(1) == null) return;

            if (!anyUiOpen)
            {
                HandleSimulatedClick();
                HandleTargetCycling();
                HandleCoordinateKey();
                HandleObjectiveKey();
                HandleZoneAnnouncement();
                HandleZoneTypeAnnouncement();
                HandleHomeKey();
                HandleGuidanceUpdate();
                TrackGuidanceTaps();
                HandleFootsteps();
                HandleWallBump();
                HandleDirectionChangeSound();
                HandleDirectionalWallSound();
                HandleItemProximitySounds();
                HandleFloorDirtAnnouncement();
                RefreshSeatSceneCache();
                HandleSeatAnnouncement();
                HandleSeatSlotAnnouncement();
                HandleCandleAnnouncement();
                HandleRatAnnouncement();
                HandleTavernServiceAnnouncements();
                HandleServeKeys();
                HandleCalmKey();
                HandleExpelKey();
                HandleMopBackspace();
            }

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);

            // Checked every frame (not throttled like the rest below) - a brief pass-by
            // near a held-interaction target like the bed turned out to disappear within
            // well under 1s, confirmed live: the action prompt ("[E] Arrumar a Cama") showed
            // up in the user's log, but the throttled version of this check never landed on
            // the same frame, so it was never captured at all. Also where the entrance door
            // gets remembered (see _rememberedEntranceDoor above) - needs to run regardless
            // of debug mode now that Stage 2 depends on it, not just diagnostics.
            var interactGO = InteractObject.BBJCJFJEFKK(1)?.GetCurrentInteractGO();

            // Round 71: tried announcing "Nada para interagir aqui" here on E with
            // interactGO == null (user's repeated report that pressing E near a stale prompt
            // gives no feedback) - REMOVED after one round of testing. Log proof it was wrong:
            // zero "CurrentInteract CHANGED" lines in the whole session (this field never once
            // went non-null), yet the player stood 0.3 units from a door that opened minutes
            // later and the warning still fired on every E press near it. This field tracks
            // some interactables (confirmed for beds, a few rounds ago) but not doors - not a
            // reliable "is there nothing here" signal on its own. Don't reintroduce this without
            // a confirmed-reliable proximity signal (e.g. cross-checking
            // InputByProximityManager's focus too, not just this field alone).

            if (interactGO != _lastInteractGO)
            {
                _lastInteractGO = interactGO;

                if (_rememberedEntranceDoor == null && interactGO != null)
                {
                    var door = interactGO.GetComponent<Door>();
                    if (door != null)
                    {
                        _rememberedEntranceDoor = door;
                        DebugLogger.LogState($"WorldNav: Remembered entrance door at {door.transform.position}");
                    }
                }

                if (Main.DebugMode)
                {
                    if (interactGO != null)
                    {
                        float distance = Vector3.Distance(playerPos, interactGO.transform.position);
                        DebugLogger.LogState($"WorldNav: CurrentInteract CHANGED -> \"{interactGO.name}\" pos={interactGO.transform.position} dist={distance:F1}");
                    }
                    else
                    {
                        DebugLogger.LogState("WorldNav: CurrentInteract CHANGED -> none");
                    }
                }
            }

            if (!Main.DebugMode) return;

            if (Time.unscaledTime - _lastLogTime < LogInterval) return;
            _lastLogTime = Time.unscaledTime;

            DebugLogger.LogState($"WorldNav: Player pos={playerPos}");

            // Round 74: confirmed via the round-73 timers that Object.FindObjectsOfType<Door>()
            // alone costs ~100-113ms in this scene (same root cause as RefreshSeatSceneCache
            // above - the call's cost scales with total scene object count, not the 6 doors it
            // actually returns). This whole block is debug-only diagnostic printing, doors never
            // change mid-session, so there's no reason to pay that cost every second - cached
            // with the same long interval as the seat/table cache instead.
            if (Time.unscaledTime - _lastDoorCacheTime > SeatSceneCacheInterval || _cachedDoors == null)
            {
                _lastDoorCacheTime = Time.unscaledTime;
                var doorScanSw = System.Diagnostics.Stopwatch.StartNew();
                _cachedDoors = Object.FindObjectsOfType<Door>();
                if (doorScanSw.ElapsedMilliseconds > 3) DebugLogger.LogState($"WorldNav: PERF Door FindObjectsOfType took {doorScanSw.ElapsedMilliseconds}ms ({_cachedDoors.Length} doors)");
            }
            foreach (var door in _cachedDoors)
            {
                float distance = Vector3.Distance(playerPos, door.transform.position);
                DebugLogger.LogState($"WorldNav: Door \"{door.gameObject.name}\" pos={door.transform.position} dist={distance:F1}");
            }
        }

        // Round 106: user's explicit request - press C to hear the player's own coordinate, and,
        // if a navigation target is currently selected (tracking something), the target's
        // coordinate too. Coordinates are rounded to whole tiles for readability. The game itself
        // has no "C" binding among the safe keys, so this doesn't fight any game control.
        private void HandleCoordinateKey()
        {
            if (!Input.GetKeyDown(KeyCode.C)) return;
            // Don't fire while a modifier is held (Ctrl/Shift+C are common combos elsewhere).
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;

            Vector3 pos = PlayerController.GetPlayerPosition(1);
            string mine = $"Você está em {Mathf.RoundToInt(pos.x)}, {Mathf.RoundToInt(pos.y)}";
            if (_selectedTarget.HasValue)
            {
                Vector3 t = _selectedTarget.Value.position;
                ScreenReader.Say($"{mine}. Alvo {_selectedTarget.Value.name} em {Mathf.RoundToInt(t.x)}, {Mathf.RoundToInt(t.y)}", interrupt: true);
            }
            else
            {
                ScreenReader.Say(mine, interrupt: true);
            }
            if (Main.DebugMode) DebugLogger.LogInput("C", $"Coordinate readout pos={pos} target={(_selectedTarget.HasValue ? _selectedTarget.Value.position.ToString() : "none")}");
        }

        // Round 108: user's explicit request - press Tab to (re)hear the current objective(s),
        // read LIVE from the tutorial's own objective texts so progress is up to date ("3 ratos",
        // "2 ratos"...). NewTutorialManager.objectives[i].textMesh is the same source the objective
        // panel shows; only the active (shown) ones are read. KeyCode.Tab is unused in the
        // decompiled game code, and this only runs when no UI is open (the gameplay block).
        private void HandleObjectiveKey()
        {
            if (!Input.GetKeyDown(KeyCode.Tab)) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

            var tm = NewTutorialManager.instance;
            var texts = new List<string>();
            if (tm != null && tm.objectives != null)
            {
                foreach (var obj in tm.objectives)
                {
                    if (obj == null || obj.gameObject == null || !obj.gameObject.activeInHierarchy || obj.textMesh == null) continue;
                    string t = UITextExtractor.GetReadableText(obj.textMesh);
                    if (!string.IsNullOrEmpty(t)) texts.Add(t.Trim());
                }
            }
            ScreenReader.Say(texts.Count == 0 ? "Nenhum objetivo ativo agora" : $"Objetivo: {string.Join(". ", texts)}", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogInput("Tab", $"Objective readout: {string.Join(" | ", texts)}");
        }

        private void HandleTargetCycling()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.PageUp)) { CycleCategory(-1); return; }
            if (ctrl && Input.GetKeyDown(KeyCode.PageDown)) { CycleCategory(1); return; }

            if (Input.GetKeyDown(KeyCode.PageUp)) CycleTarget(-1);
            else if (Input.GetKeyDown(KeyCode.PageDown)) CycleTarget(1);
        }

        private void CycleCategory(int direction)
        {
            var allTargets = BuildTargetList();
            var presentCategories = CategoryOrder.Where(c => allTargets.Any(t => t.category == c)).ToList();
            if (presentCategories.Count == 0)
            {
                ScreenReader.Say("Nenhuma categoria disponível", interrupt: true);
                return;
            }

            int currentIndex = _currentCategory != null ? presentCategories.IndexOf(_currentCategory) : -1;
            int nextIndex = ((currentIndex + direction) % presentCategories.Count + presentCategories.Count) % presentCategories.Count;
            _currentCategory = presentCategories[nextIndex];
            _currentTargetIndex = -1;

            int countInCategory = allTargets.Count(t => t.category == _currentCategory);
            UISound.PlayNavigate();
            ScreenReader.Say($"Categoria: {_currentCategory} ({countInCategory})", interrupt: true);
            DebugLogger.LogInput(direction > 0 ? "Ctrl+PageDown" : "Ctrl+PageUp", $"Category selected: {_currentCategory}");
        }

        private void CycleTarget(int direction)
        {
            var allTargets = BuildTargetList();
            var targets = _currentCategory != null
                ? allTargets.Where(t => t.category == _currentCategory).ToList()
                : allTargets;

            if (targets.Count == 0 && _currentCategory != null)
            {
                // Category emptied out (e.g. player walked away from its only items) -
                // fall back to the full list instead of going silent.
                _currentCategory = null;
                targets = allTargets;
            }

            if (targets.Count == 0)
            {
                ScreenReader.Say("Nenhum alvo conhecido ainda", interrupt: true);
                return;
            }

            _currentTargetIndex = ((_currentTargetIndex + direction) % targets.Count + targets.Count) % targets.Count;
            var selected = targets[_currentTargetIndex];
            _selectedTarget = (selected.name, selected.position);

            // Reverted: user prefers using the game's own container UI (opens correctly as
            // a list already) over hearing contents from this Page Up/Down list - that real
            // UI's slot text needs a separate fix instead (see KeyboardUINavigator).
            UISound.PlayNavigate();
            ScreenReader.Say(selected.name, interrupt: true);
            DebugLogger.LogInput(direction > 0 ? "PageDown" : "PageUp", $"Nav target selected: {selected.name} [{selected.category}]");
        }

        private void HandleHomeKey()
        {
            if (!Input.GetKeyDown(KeyCode.Home)) return;

            if (_selectedTarget == null)
            {
                ScreenReader.Say("Nenhum alvo selecionado. Use Page Up ou Page Down primeiro.", interrupt: true);
                return;
            }

            _guidanceActive = !_guidanceActive;
            if (_guidanceActive)
            {
                _lastGuidancePosition = null;
                _currentPath = null;
                _simplifiedSteps = null;
                _currentStepIndex = 0;
                // User reported hearing a wrong, "shortest line" message right when turning
                // guidance on, before the real route arrived (the old code spoke the
                // fallback straight-line guess immediately, then the real route a moment
                // later). Now says nothing premature - just announces it's computing, and
                // OnPathComputed below announces the real first step once it's ready.
                _isInitialPathRequest = true;
                ScreenReader.Say("Calculando rota...", interrupt: true);
                RequestPathToTarget(PlayerController.GetPlayerPosition(1), _selectedTarget.Value.position);
            }
            else
            {
                // Round 113: turning the guide off also UNMARKS the target - the user "desativei o
                // guia" and expected it gone, but the C-key coordinate readout kept announcing the
                // old target. Clear it so C reports just the position until a new target is picked.
                _selectedTarget = null;
                _currentPath = null;
                _simplifiedSteps = null;
                ScreenReader.Say("Guia desativado", interrupt: true);
            }
        }

        // "Atualiza a cada passo" (user's explicit Stage 3 spec) - fixed always-on while
        // guidance is active, no toggle for this part yet (matches the original plan).
        private void HandleGuidanceUpdate()
        {
            if (!_guidanceActive || _selectedTarget == null) return;
            // Found the actual cause of the "two routes spoken" bug: this runs every frame
            // once guidance is active (and the very first call always passes the movement
            // check below, since _lastGuidancePosition starts null), so it was firing and
            // speaking the straight-line fallback BEFORE the first real route came back -
            // interrupting "Calculando rota..." within milliseconds (confirmed in log: both
            // lines logged 8ms apart). OnPathComputed already announces the real first step
            // once it's ready, so this just needs to stay quiet until then.
            if (_isInitialPathRequest) return;

            Vector3 pos = PlayerController.GetPlayerPosition(1);
            if (_lastGuidancePosition.HasValue && Vector3.Distance(_lastGuidancePosition.Value, pos) < TileSize) return;

            _lastGuidancePosition = pos;

            // User reported guidance "getting all messed up" when walking AWAY from the
            // planned route - the periodic refresh alone (every PathRequestCooldown) wasn't
            // reacting fast enough. Now also re-routes immediately (ignoring the cooldown)
            // once perpendicular drift from the current step's line gets too large.
            bool offTrack = IsOffTrack(pos);
            if (offTrack || Time.unscaledTime - _lastPathRequestTime > PathRequestCooldown)
            {
                RequestPathToTarget(pos, _selectedTarget.Value.position);
            }

            AnnounceDirectionToSelectedTarget();
        }

        private bool IsOffTrack(Vector3 pos)
        {
            if (_simplifiedSteps == null || _simplifiedSteps.Count == 0) return false;

            var step = _simplifiedSteps[_currentStepIndex];
            float perpendicular = (step.direction == "cima" || step.direction == "baixo")
                ? Mathf.Abs(step.endPosition.x - pos.x)
                : Mathf.Abs(step.endPosition.y - pos.y);
            return perpendicular > TileSize * 3f;
        }

        private void RequestPathToTarget(Vector3 from, Vector3 to, bool isRetry = false)
        {
            if (_pathRequestPending) return;
            _pathRequestPending = true;
            _lastPathRequestTime = Time.unscaledTime;
            _lastRequestFrom = from;
            _lastRequestTo = to;
            _isRetryAttempt = isRetry;
            try
            {
                // Found why pathfinding always failed (confirmed live: every single
                // request, even 3-tile distances, came back unsuccessful): the A* search
                // works with grid-snapped Vector2 keys (0.25-unit steps - confirmed in
                // Utils.MJEACANINDN), and a real player position is almost never exactly on
                // that grid. The goal-equality check inside the algorithm never matched our
                // un-snapped float position, so it always exhausted its search and failed.
                // Snapping both ends with the same function the game itself uses elsewhere
                // for this exact purpose fixes it.
                _lastPathRequestStart = Utils.MJEACANINDN(from);
                var info = new PathRequestInfo
                {
                    startPos = _lastPathRequestStart,
                    goalPos = Utils.MJEACANINDN(to),
                    pathEnd = to,
                    canWalkDiagonal = true,
                    avoidWalls = true,
                    avoidObjects = true,
                    // Lowered to 1500 a few rounds ago to speed up "Rota calculada", flagged
                    // at the time as a speed/reliability trade-off needing real testing. Now
                    // confirmed bad via log: "Bar" failed every attempt (including the nudged
                    // retry) while far away, then succeeded the moment the player got close -
                    // a budget too small for longer searches, not a genuinely blocked path.
                    // Raised partway back up.
                    maxNodes = 2500,
                    callback = OnPathComputed,
                };
                PathRequestManager.RequestPath(info);
            }
            catch (System.Exception ex)
            {
                // Project rule: never risk a crash touching a game system without a
                // fallback. If the manager's internal static instance isn't ready for any
                // reason, this throws on OUR calling thread (confirmed via decompiled
                // source - the null deref would happen synchronously in RequestPath, not on
                // the background thread), so it's safely catchable here.
                _pathRequestPending = false;
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Pathfinding request threw, falling back to straight line: {ex.Message}");
            }
        }

        private void OnPathComputed(Vector2[] path, bool success)
        {
            _pathRequestPending = false;
            bool wasInitial = _isInitialPathRequest;
            _isInitialPathRequest = false;

            if (!success || path == null || path.Length == 0)
            {
                _currentPath = null;
                _simplifiedSteps = null;
                _currentStepIndex = 0;
                if (Main.DebugMode) DebugLogger.LogState("WorldNav: Pathfinding returned no route");

                // User's explicit request: still give step-by-step guidance toward a
                // blocked destination (e.g. a closed door's exact threshold tile, which the
                // game only marks walkable while open) instead of falling back to the old
                // straight-line message. Retry once, aiming one tile back toward the player
                // along the same line - the block is usually just the very last tile, so
                // this should land on a tile that's already walkable.
                if (!_isRetryAttempt)
                {
                    Vector3 nudge = _lastRequestFrom - _lastRequestTo;
                    if (nudge.sqrMagnitude > 0.0001f)
                    {
                        Vector3 retryGoal = _lastRequestTo + nudge.normalized * TileSize;
                        if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Retrying route nudged toward player: {retryGoal}");
                        _isInitialPathRequest = wasInitial;
                        RequestPathToTarget(_lastRequestFrom, retryGoal, isRetry: true);
                        return;
                    }
                }

                if (wasInitial) ScreenReader.Say("Não encontrei uma rota até lá.", interrupt: true);
                return;
            }

            _currentPath = path;
            _simplifiedSteps = SimplifyPath(_lastPathRequestStart, path);
            _currentStepIndex = 0;
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Pathfinding succeeded, {path.Length} waypoints -> {_simplifiedSteps.Count} etapas");

            // Only the FIRST route after turning guidance on gets an extra "rota calculada"
            // lead-in - periodic background refreshes while walking stay silent here (the
            // ongoing per-tile announcement below already keeps talking) so they don't
            // interrupt the player every few seconds.
            if (wasInitial && _simplifiedSteps.Count > 0)
            {
                ScreenReader.Say($"Rota calculada. {BuildStepGuidanceMessage(PlayerController.GetPlayerPosition(1))}", interrupt: true);
            }
        }

        // User's explicit request: collapse the raw waypoint-per-0.25-unit path into
        // axis-aligned chunks ("4 pra cima" instead of 8 separate near-identical
        // announcements one tile apart) - merges consecutive steps that move in the same
        // cardinal direction. A step whose horizontal and vertical delta are equal (a true
        // diagonal move) is assigned to whichever axis was last in use, to avoid 1-tile
        // direction flip-flopping on an otherwise straight diagonal corridor.
        private static List<(string direction, Vector3 endPosition)> SimplifyPath(Vector3 start, Vector2[] path)
        {
            var steps = new List<(string direction, Vector3 endPosition)>();
            if (path == null || path.Length == 0) return steps;

            Vector3 prev = start;
            string currentDirection = null;
            Vector3 currentEnd = prev;

            foreach (Vector2 point in path)
            {
                Vector3 next = point;
                Vector3 delta = next - prev;
                string direction = Mathf.Abs(delta.y) >= Mathf.Abs(delta.x)
                    ? (delta.y > 0 ? "cima" : "baixo")
                    : (delta.x > 0 ? "direita" : "esquerda");

                if (direction == currentDirection)
                {
                    currentEnd = next;
                }
                else
                {
                    if (currentDirection != null) steps.Add((currentDirection, currentEnd));
                    currentDirection = direction;
                    currentEnd = next;
                }
                prev = next;
            }
            if (currentDirection != null) steps.Add((currentDirection, currentEnd));
            return steps;
        }

        // User's explicit request: stop reading the count as plain Euclidean distance to
        // the step's end point - moving sideways (perpendicular to the step's own axis)
        // was making the number drift even though the player hadn't actually gained or
        // lost any real ground on the axis being announced ("ele tinha de manter a
        // exatidão"). Now measures only along the axis that direction actually refers to.
        private static float ComputeAxisDistance(Vector3 pos, (string direction, Vector3 endPosition) step)
        {
            return (step.direction == "cima" || step.direction == "baixo")
                ? Mathf.Abs(step.endPosition.y - pos.y)
                : Mathf.Abs(step.endPosition.x - pos.x);
        }

        private static int ComputeStepCount(Vector3 pos, (string direction, Vector3 endPosition) step)
        {
            return Mathf.RoundToInt(ComputeAxisDistance(pos, step) / TileSize);
        }

        // Tightened to TileSize*0.3 last round (user felt steps switched too early) - but
        // that caused a worse regression, confirmed in log: a short 2-tile step got stuck
        // oscillating "1 pra direita"/"1 pra esquerda" for over a minute, distance hovering
        // between 0.17 and 0.49 - frequently ABOVE the tighter threshold, so it kept never
        // quite resolving. Reverted to the original, more forgiving rounding boundary.
        private const float StepAdvanceThreshold = TileSize * 0.5f;

        // Confirmed in log (real bug, not a guess): if the player overshoots a step's end
        // point (ends up on the far side of it), the chunk's direction label - fixed back
        // when SimplifyPath built it from the path's original traversal - stays "direita"
        // even though getting back now requires "esquerda". The spoken count correctly grew
        // (distance really was increasing) while the spoken direction stayed wrong - exactly
        // what the user reported ("pediu pra direita, mas a distância só aumentou"). Always
        // recompute the direction live from the player's CURRENT position instead of trusting
        // the precomputed label.
        private static string GetLiveDirection(Vector3 pos, (string direction, Vector3 endPosition) step)
        {
            bool isVertical = step.direction == "cima" || step.direction == "baixo";
            if (isVertical) return step.endPosition.y > pos.y ? "cima" : "baixo";
            return step.endPosition.x > pos.x ? "direita" : "esquerda";
        }

        // Builds the current step-guidance message without speaking it - shared by the
        // per-tile update and by the one-time "rota calculada" lead-in right after
        // activation, so both use the exact same logic/state advancement.
        private string BuildStepGuidanceMessage(Vector3 pos)
        {
            // REVERTED: tried using IProximity.IsAvailableByProximity as an exact "arrived"
            // event last round. Confirmed wrong by testing (door said "Você chegou" the
            // instant guidance turned on, before leaving the corner) - checked the actual
            // decompiled implementations (Placeable/Door) and despite the name, this method
            // has nothing to do with spatial distance to the player - it gates pickup/
            // decoration-mode/rental-zone eligibility instead. No safe drop-in replacement
            // found yet; back to geometric distance below.
            int stepBeforeAdvance = _currentStepIndex;

            // Advance past a step once ITS OWN axis is satisfied (same axis-only measure as
            // the spoken count), not full Euclidean distance - this is what made
            // "Continuando..." show up for steps that were functionally already done on
            // their relevant axis. User's complaint: "Continuando" told them nothing
            // actionable - this means a step now hands off to the next one immediately
            // instead of stalling on it.
            while (_currentStepIndex < _simplifiedSteps.Count - 1
                   && ComputeAxisDistance(pos, _simplifiedSteps[_currentStepIndex]) < StepAdvanceThreshold)
            {
                _currentStepIndex++;
            }

            if (_currentStepIndex != stepBeforeAdvance && _lastSpokenCountForStep >= 0)
            {
                DebugLogger.LogState($"WorldNav: Etapa concluída - número pedido={_lastSpokenCountForStep}, toques de movimento usados={_tapsForCurrentStep}");
                _tapsForCurrentStep = 0;
                _lastSpokenCountForStep = -1;
            }

            var step = _simplifiedSteps[_currentStepIndex];
            bool isLastStep = _currentStepIndex >= _simplifiedSteps.Count - 1;

            if (isLastStep)
            {
                // The final step is the real destination tile, and needs full 2D precision
                // (not just one axis) - a route's last leg is rarely perfectly axis-aligned,
                // so once the chunk's own axis is satisfied this also checks the OTHER axis
                // instead of getting stuck repeating "Continuando..." right at the doorstep.
                Vector3 delta = step.endPosition - pos;
                int dy = Mathf.RoundToInt(Mathf.Abs(delta.y) / TileSize);
                int dx = Mathf.RoundToInt(Mathf.Abs(delta.x) / TileSize);

                if (dy == 0 && dx == 0)
                {
                    // User's explicit request: turn the guide off automatically on arrival
                    // instead of leaving it active and needing a manual Home press to stop.
                    _guidanceActive = false;
                    return "Você chegou";
                }

                return dy >= dx
                    ? $"{dy} pra {(delta.y > 0 ? "cima" : "baixo")}"
                    : $"{dx} pra {(delta.x > 0 ? "direita" : "esquerda")}";
            }

            // User reported hearing "0 pra baixo" - makes no sense as an instruction ("não
            // tem como andar 0 pra baixo"). This can legitimately round to 0 here even though
            // the stricter StepAdvanceThreshold above hasn't moved to the next step yet (the
            // raw distance can sit in the gap between the two thresholds) - never speak 0.
            int count = Mathf.Max(1, ComputeStepCount(pos, step));
            _lastSpokenCountForStep = count;
            string direction = GetLiveDirection(pos, step);
            return $"{count} pra {direction}";
        }

        private void AnnounceDirectionToSelectedTarget()
        {
            Vector3 pos = PlayerController.GetPlayerPosition(1);
            Vector3 targetPos = _selectedTarget.Value.position;

            if (_simplifiedSteps != null && _simplifiedSteps.Count > 0)
            {
                string message = BuildStepGuidanceMessage(pos);
                ScreenReader.Say(message, interrupt: true);
                // pos/endPosition logged so the real world-distance-per-count can be
                // measured directly from two consecutive lines instead of guessed - user
                // wasn't sure if the count is double, half, or something else.
                Vector3 endPos = _simplifiedSteps[_currentStepIndex].endPosition;
                DebugLogger.LogState($"WorldNav: Guidance to \"{_selectedTarget.Value.name}\" -> {message} (etapa {_currentStepIndex + 1}/{_simplifiedSteps.Count}) pos={pos} end={endPos}");
                return;
            }

            // No route yet (still computing this round, or the request failed). User
            // reported guidance leading straight into walls and not making sense once
            // the target is past a door - a straight-line guess across different game
            // Locations is actively misleading, so say so instead of guessing, same as
            // before real pathfinding existed.
            Location playerLocation = PlayerController.GetPlayer(1).LEOIMFNKFGA;
            Location targetLocation = Utils.HJPCBBGHPDA(targetPos);
            if (playerLocation != Location.None && targetLocation != Location.None && playerLocation != targetLocation)
            {
                string areaName = LocationNames.TryGetValue(targetLocation, out var known) ? known : targetLocation.ToString();
                ScreenReader.Say($"Calculando rota até {areaName}...", interrupt: true);
                DebugLogger.LogState($"WorldNav: No route yet, target in different area ({playerLocation} vs {targetLocation})");
                return;
            }

            // User's explicit request: never speak two directions at once, even in this
            // no-route fallback ("4 pra esquerda, 3 pra baixo" - "tem que mostrar um de
            // cada vez"). Also found while re-checking this: it never divided by TileSize,
            // unlike every other distance-to-telha conversion in this file (ComputeStepCount,
            // etc.) - was speaking raw world units as if they were telhas, double the real
            // count. Fixed both at once.
            Vector3 delta = targetPos - pos;
            int dy = Mathf.RoundToInt(Mathf.Abs(delta.y) / TileSize);
            int dx = Mathf.RoundToInt(Mathf.Abs(delta.x) / TileSize);

            string fallbackMessage;
            if (dy == 0 && dx == 0)
            {
                fallbackMessage = "Você chegou";
            }
            else if (dy >= dx)
            {
                fallbackMessage = $"{dy} pra {(delta.y > 0 ? "cima" : "baixo")}";
            }
            else
            {
                fallbackMessage = $"{dx} pra {(delta.x > 0 ? "direita" : "esquerda")}";
            }

            ScreenReader.Say(fallbackMessage, interrupt: true);
            DebugLogger.LogState($"WorldNav: Guidance to \"{_selectedTarget.Value.name}\" -> {fallbackMessage} (sem rota ainda)");
        }

        private void HandleFootsteps()
        {
            var player = PlayerController.GetPlayer(1);
            if (player == null || !player.moving)
            {
                return;
            }

            Vector3 pos = player.transform.position;
            if (!_lastFootstepPosition.HasValue)
            {
                _lastFootstepPosition = pos;
                return;
            }

            if (Vector3.Distance(_lastFootstepPosition.Value, pos) < TileSize) return;

            _lastFootstepPosition = pos;

            // Properly decompiled AlmenaraGames.MultiAudioManager/AudioObject this round
            // (not guessing) to find out why the trigger always fired but nothing was ever
            // heard: confirmed AudioObject.clips was NOT empty and a valid AudioClip WAS
            // selected and assigned every time (no "doesn't have a valid Audio Clip"
            // warning ever appeared in the log) - so it's not a missing-data issue. The
            // actual audibility depends on this 3rd-party plugin's own multi-listener/
            // volume-by-distance system (MultiAudioListener, separate from Unity's built-in
            // AudioListener), which is too deep to safely unwind from outside its normal
            // init flow. No footstep sound for now - user asked to remove the reused click
            // rather than keep a sound that doesn't mean "footstep" - will wire up a
            // dedicated sound the moment a file for it is provided.
        }

        private void HandleWallBump()
        {
            var player = PlayerController.GetPlayer(1);
            HandleSingleTapWallBump(player);

            if (player == null || !player.moving)
            {
                _wallStuckTime = 0f;
                _lastWallCheckPosition = null;
                _lastBumpBlockerSpoken = null;
                CustomSounds.StopWallBumpLoop();
                CustomSounds.StopItemBumpLoop();
                return;
            }

            Vector3 pos = player.transform.position;
            if (_lastWallCheckPosition.HasValue)
            {
                float moved = Vector3.Distance(_lastWallCheckPosition.Value, pos);
                float expectedMinimal = player.speed * Time.deltaTime * 0.15f;
                _wallStuckTime = moved < expectedMinimal ? _wallStuckTime + Time.deltaTime : 0f;
            }
            _lastWallCheckPosition = pos;

            // Round 111: not blocked long enough yet - reset everything. (WallStuckSeconds is now a
            // short 0.08s so the bump SOUND is near-instant.)
            if (_wallStuckTime < WallStuckSeconds)
            {
                CustomSounds.StopWallBumpLoop();
                CustomSounds.StopItemBumpLoop();
                _lastBumpBlockerSpoken = null;
                _bumpClassified = false;
                return;
            }

            // Round 111: classify (wall vs item, + blocker name) ONCE per bump event, on the
            // transition into "stuck", instead of raycasting every frame while held against it -
            // big lag win (the result doesn't change frame to frame). The sound starts here, the
            // instant we cross the short threshold.
            if (!_bumpClassified)
            {
                _bumpClassified = true;
                _bumpMoveDir = GetHeldMovementDirection();
                _bumpIsItem = IsBlockedByNonWallItem(pos, _bumpMoveDir, out _bumpBlockerName);
                if (_bumpIsItem) { CustomSounds.StopWallBumpLoop(); CustomSounds.StartItemBumpLoop(); }
                else { CustomSounds.StopItemBumpLoop(); CustomSounds.StartWallBumpLoop(); }
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: bump classified as {(_bumpIsItem ? "item" : "wall")}{(_bumpBlockerName != null ? $" ({_bumpBlockerName})" : "")}");
            }

            // Speak the blocker name + direction once, a bit after the sound (BlockerAnnounceSeconds)
            // so quick brushes don't talk - uses the name captured on the transition above.
            if (_wallStuckTime >= BlockerAnnounceSeconds && !string.IsNullOrEmpty(_bumpBlockerName))
            {
                string dirWord = DirectionWord(_bumpMoveDir);
                string spoken = string.IsNullOrEmpty(dirWord) ? $"Bloqueado por {_bumpBlockerName}" : $"Bloqueado por {_bumpBlockerName}, {dirWord}";
                if (spoken != _lastBumpBlockerSpoken)
                {
                    _lastBumpBlockerSpoken = spoken;
                    ScreenReader.Say(spoken, interrupt: false);
                }
            }
        }

        private bool _bumpClassified;
        private bool _bumpIsItem;
        private string _bumpBlockerName;
        private Vector2 _bumpMoveDir;
        // Round 111: reusable buffer for the per-frame directional wall raycasts (RaycastNonAlloc),
        // to avoid the array allocation RaycastAll did 4x/frame.
        private static readonly RaycastHit2D[] _raycastBuffer = new RaycastHit2D[16];

        private static string DirectionWord(Vector2 dir)
        {
            if (dir == Vector2.zero) return "";
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
                return dir.x > 0 ? "à direita" : "à esquerda";
            return dir.y > 0 ? "pra cima" : "pra baixo";
        }

        private static Vector2 GetHeldMovementDirection()
        {
            Vector2 dir = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) dir += Vector2.up;
            if (Input.GetKey(KeyCode.S)) dir += Vector2.down;
            if (Input.GetKey(KeyCode.A)) dir += Vector2.left;
            if (Input.GetKey(KeyCode.D)) dir += Vector2.right;
            return dir;
        }

        private static bool IsBlockedByNonWallItem(Vector2 pos, Vector2 direction, out string blockerName)
        {
            blockerName = null;
            if (direction == Vector2.zero) return false;
            float maxDistance = TileSize * 1.2f;

            var player = PlayerController.GetPlayer(1);
            var playerCollider = player?.GetComponent<Collider2D>() ?? player?.GetComponentInChildren<Collider2D>();
            RaycastHit2D[] hits = Physics2D.RaycastAll(pos, direction.normalized, maxDistance);
            RaycastHit2D? closest = null;
            foreach (var h in hits)
            {
                if (h.collider.isTrigger) continue;
                if (playerCollider != null && h.collider.transform.root == playerCollider.transform.root) continue;
                if (!closest.HasValue || h.distance < closest.Value.distance) closest = h;
            }

            // A closed door never shows up here via Physics2D (same reason it never showed
            // up for the ambient wall sound until that got its own fix - no Collider2D at
            // the blocked threshold, it's WorldGrid walkability only).
            float? doorDist = GetClosedDoorBlockDistance(pos, direction.normalized, maxDistance);

            // User reported bumping a WALL near a door got classified as "item" - the door
            // check alone doesn't know whether a wall is actually closer and is the real
            // thing being bumped. Whichever is closer wins.
            if (closest.HasValue && (!doorDist.HasValue || closest.Value.distance <= doorDist.Value))
            {
                // Name ANY real collider blocking the player (round 105) - whether furniture
                // ("(Clone)") or static scenery like the brick pile "Grupo Ladrillos" that wedged
                // the player at the door. Walls themselves have no Collider2D here (confirmed long
                // ago), so anything we hit IS a nameable object worth announcing. The returned bool
                // still drives only the wall-vs-item SOUND (kept as the "(Clone)" signal).
                blockerName = DescribeBlockerCollider(closest.Value.collider);
                return closest.Value.collider.transform.root.name.Contains("(Clone)");
            }
            if (doorDist.HasValue) { blockerName = "porta"; return true; }
            return false;
        }

        // Round 105: human-readable name for whatever the player is wedged against, for the spoken
        // "Bloqueado por ..." announcement. Prefers the localized item name when it's a Placeable;
        // otherwise cleans the GameObject name (strips "(Clone)" and any leading "1234 - " id).
        private static string DescribeBlockerCollider(Collider2D collider)
        {
            var root = collider.transform.root;
            var placeable = root.GetComponent<Placeable>() ?? root.GetComponentInChildren<Placeable>();
            if (placeable != null && placeable.itemSetup != null && placeable.itemSetup.item != null)
            {
                string n = placeable.itemSetup.item.IABAKHPEOAF();
                if (!string.IsNullOrEmpty(n)) return n;
            }
            string name = root.name.Replace("(Clone)", "").Trim();
            int dash = name.IndexOf(" - ");
            if (dash > 0 && int.TryParse(name.Substring(0, dash).Trim(), out _)) name = name.Substring(dash + 3).Trim();
            return string.IsNullOrEmpty(name) ? "objeto" : name;
        }

        // User's explicit request: a continuous, directional sense of nearby walls (not
        // just reactive bumping while moving). EXPERIMENTAL: uses Unity's own Physics2D
        // directly instead of the game's internal walkability checks - investigated reusing
        // the game's own pathfinding "avoidWalls" check (Utils.EJPFCKFEMJF) but confirmed via
        // decompiled source it only tests position.y > 800f (an unrelated cutoff, not
        // collision), so it could not be trusted.
        //
        // First version used a single OverlapCircle point about one tile out - user reported
        // cima/baixo never triggering at all, and nothing in narrow 1-tile corridors. A
        // single distant point misses anything closer (a corridor's walls can be much closer
        // than a full tile away) and a single Raycast only reports the NEAREST hit, which in
        // practice is very likely the player's own collider (the ray starts at the player),
        // hiding any real wall behind it. Switched to RaycastAll along the whole direction
        // (catches walls at any distance up to maxDistance, near or far) and manually picks
        // the closest hit that ISN'T the player and isn't a trigger.
        private static readonly (string name, Vector2 offset)[] WallCheckDirections =
        {
            ("cima", Vector2.up), ("baixo", Vector2.down), ("esquerda", Vector2.left), ("direita", Vector2.right),
        };

        private readonly Dictionary<string, float> _wallLastBlockedTime = new Dictionary<string, float>();
        // Round 109: shortened from 0.15 so the directional wall sound disappears almost
        // immediately when the wall is no longer there (user wanted it instant). Kept just long
        // enough (~4 frames at 60fps) to bridge a single-frame raycast flicker at the edge of
        // range, now that the sound itself toggles instantly (persistent volume-toggle source).
        private const float WallSoundOffDelay = 0.06f;
        private float _lastWallDiagLogTime;

        private void HandleDirectionalWallSound()
        {
            var player = PlayerController.GetPlayer(1);
            if (player == null)
            {
                CustomSounds.StopAllDirectionalWallSounds();
                return;
            }

            Vector2 pos = player.transform.position;
            var playerCollider = player.GetComponent<Collider2D>() ?? player.GetComponentInChildren<Collider2D>();
            float maxDistance = TileSize * 1.2f;
            bool shouldLog = Main.DebugMode && Time.unscaledTime - _lastWallDiagLogTime > 1f;

            foreach (var (name, offset) in WallCheckDirections)
            {
                // Round 111: RaycastNonAlloc into a reusable buffer instead of RaycastAll - this
                // runs 4x EVERY frame, and RaycastAll allocates a fresh array each call (GC churn =
                // micro-stutter). The buffer is shared/static; 16 hits is plenty for a 1-tile ray.
                int hitCount = Physics2D.RaycastNonAlloc(pos, offset, _raycastBuffer, maxDistance);
                RaycastHit2D? closest = null;
                for (int hi = 0; hi < hitCount; hi++)
                {
                    var h = _raycastBuffer[hi];
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (playerCollider != null && h.collider.transform.root == playerCollider.transform.root) continue;
                    // User reported "cima" sounding in a corner with no wall there - log
                    // confirmed it was hitting the BED ("1130 - Cama del Jugador(Clone)"),
                    // not a wall (any solid collider counted before). Tried requiring a
                    // PhysicalSpaceWall component next - WRONG, confirmed by the next log:
                    // it excluded real walls too ("WallBack", which worked fine before,
                    // never matched it either - 172 checks, zero hits). Switched to a
                    // blocklist instead of an allowlist: furniture/decorations are runtime
                    // Instantiate()'d prefab instances, so Unity appends "(Clone)" to their
                    // name - static level geometry like walls isn't instantiated, so it never
                    // has that suffix. Confirmed against both logged examples.
                    //
                    // User then reported a closed door in a narrow corridor not triggering
                    // both sides + bottom like it used to - doors are also Clone-named (they
                    // ARE prefab instances), so this blocklist was wrongly excluding them
                    // too. Unlike loose furniture, a closed door genuinely IS a room boundary
                    // for spatial-awareness purposes (this is the AMBIENT sound, not the bump
                    // sound below, which intentionally treats doors differently per the
                    // user's own request) - excepted back in here.
                    bool isClone = h.collider.transform.root.name.Contains("(Clone)");
                    bool isDoor = h.collider.GetComponentInParent<Door>() != null;
                    if (isClone && !isDoor) continue;
                    if (!closest.HasValue || h.distance < closest.Value.distance) closest = h;
                }

                // The Door-component exception above never actually fires for a closed
                // door's blocked threshold: confirmed via log (player 0.3 units from "1125 -
                // Cellar Door" for several seconds straight, that direction's raycast still
                // came back "nada"). Re-confirmed in decompiled source - Door.PJMBLECKFLH
                // (the open/close handler) only toggles WorldGrid walkability nodes, no
                // Collider2D is enabled/disabled - so a closed door blocks movement purely
                // through the game's own grid, with no physics collider there for Physics2D
                // to ever find. Checking the door's own freeNodesOnOpen positions directly
                // instead of hoping a collider exists at the right spot.
                bool doorBlocking = !closest.HasValue && IsClosedDoorBlocking(pos, offset, maxDistance, shouldLog);
                if (closest.HasValue) _wallLastBlockedTime[name] = Time.unscaledTime;
                else if (doorBlocking) _wallLastBlockedTime[name] = Time.unscaledTime;

                // Brief grace period instead of an instant on/off, in case detection
                // flickers frame to frame near the edge of range - user reported audible
                // "pauses" in the loop that this should smooth out if that's the cause.
                bool withinGrace = _wallLastBlockedTime.TryGetValue(name, out float lastTime)
                    && Time.unscaledTime - lastTime < WallSoundOffDelay;
                CustomSounds.SetDirectionalWallSound(name, withinGrace);

                if (shouldLog)
                {
                    string hitDesc = closest.HasValue ? $"{closest.Value.collider.name} dist={closest.Value.distance:F2}"
                        : doorBlocking ? "porta fechada (grid)" : "nada";
                    DebugLogger.LogState($"WorldNav: WallCheck {name} -> {hitDesc}");
                }
            }

            if (shouldLog) _lastWallDiagLogTime = Time.unscaledTime;
        }

        // Closed doors block movement via WorldGrid walkability, not a Physics2D collider
        // (confirmed in decompiled Door.PJMBLECKFLH) - check the door's own threshold
        // positions directly instead of relying on Physics2D to happen to find something.
        // Returns the distance to the closest matching door threshold, or null if none.
        //
        // Found via log why this never fired for the doors tested: freeNodesOnOpen was
        // length 0 (not null) for both - the original "skip if null" check let those
        // through to an empty foreach that never ran. GetDoorWalkablePosition (used
        // elsewhere for routing to a door) already has the right fallback for this exact
        // case - falls back to the door's own transform position - copied that here too.
        private static float? GetClosedDoorBlockDistance(Vector2 pos, Vector2 direction, float maxDistance, bool logDiag = false)
        {
            float? best = null;
            foreach (var door in Object.FindObjectsOfType<Door>())
            {
                // Door.open itself is protected - ECMGCJGPKNO (decompiled name) is the
                // public property whose getter returns it.
                bool isOpen = door.ECMGCJGPKNO;
                int freeNodeCount = door.freeNodesOnOpen?.Length ?? 0;
                if (logDiag && Vector2.Distance(pos, door.transform.position) < 1.5f)
                {
                    DebugLogger.LogState($"WorldNav: Door diag \"{door.name}\" open={isOpen} freeNodes={freeNodeCount} doorPos={(Vector2)door.transform.position}");
                }
                if (isOpen) continue;

                Vector2[] nodePositions = freeNodeCount > 0
                    ? System.Array.ConvertAll(door.freeNodesOnOpen, o => (Vector2)door.transform.position + o)
                    : new[] { (Vector2)door.transform.position };

                foreach (var nodePos in nodePositions)
                {
                    Vector2 toNode = nodePos - pos;
                    float dist = toNode.magnitude;
                    if (logDiag) DebugLogger.LogState($"WorldNav: Door diag node nodePos={nodePos} dist={dist:F2} dot={(dist > 0.001f ? Vector2.Dot(toNode.normalized, direction) : 0f):F2}");
                    if (dist > maxDistance || dist < 0.001f) continue;
                    if (Vector2.Dot(toNode.normalized, direction) > 0.7f && (!best.HasValue || dist < best.Value)) best = dist;
                }
            }
            return best;
        }

        private static bool IsClosedDoorBlocking(Vector2 pos, Vector2 direction, float maxDistance, bool logDiag = false)
            => GetClosedDoorBlockDistance(pos, direction, maxDistance, logDiag).HasValue;

        // User's explicit request: a continuous sense of named items nearby (not just the
        // one-shot tied to the action-prompt UI text above) - items within ~6 "passos"
        // (tiles) repeat their sound every second, panned/pitched toward their direction.
        // Multiple matching items close together (e.g. baú and cama side by side) are
        // staggered instead of overlapping - explicit request, since playing both at once
        // was confusing.
        private const float ItemSoundRadius = TileSize * 3f;
        private const float ItemSoundCycleInterval = 1f;
        private const float ItemSoundStagger = 0.3f;
        private float _lastItemSoundCycleTime;
        private bool _itemSoundCycleRunning;

        private void HandleItemProximitySounds()
        {
            if (Time.unscaledTime - _lastItemSoundCycleTime < ItemSoundCycleInterval) return;
            if (_itemSoundCycleRunning) return;
            _lastItemSoundCycleTime = Time.unscaledTime;

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            var nearby = new List<(string name, Vector3 position, float distance)>();
            // Round 113: use the shared cached placeable list (refreshed every 15s) instead of a
            // fresh FindObjectsOfType every second - that per-second full-scene scan was a major
            // continuous stutter.
            foreach (var placeable in _cachedAllPlaceables)
            {
                if (placeable == null) continue;
                float distance = Vector3.Distance(playerPos, placeable.transform.position);
                if (distance > ItemSoundRadius) continue;

                string itemName = DescribePlaceable(placeable);
                if (!CustomSounds.HasItemClip(itemName)) continue;

                nearby.Add((itemName, placeable.transform.position, distance));
            }
            if (nearby.Count == 0) return;

            nearby.Sort((a, b) => a.distance.CompareTo(b.distance));
            MelonCoroutines.Start(PlayItemSoundsStaggered(nearby, playerPos));
        }

        private IEnumerator PlayItemSoundsStaggered(List<(string name, Vector3 position, float distance)> items, Vector3 playerPos)
        {
            _itemSoundCycleRunning = true;
            foreach (var item in items)
            {
                Vector3 delta = item.position - playerPos;
                float pitch = 1f;
                float pan = 0f;
                if (Mathf.Abs(delta.y) >= Mathf.Abs(delta.x))
                {
                    pitch = delta.y > 0 ? 1.3f : 0.75f;
                }
                else
                {
                    pan = delta.x > 0 ? 1f : -1f;
                }
                CustomSounds.PlayItemNearby(item.name, pitch, pan);
                yield return new WaitForSeconds(ItemSoundStagger);
            }
            _itemSoundCycleRunning = false;
        }

        // User's explicit request: count actual movement-key taps while guidance is active,
        // so BuildStepGuidanceMessage can log "announced N, actually took M taps" the moment
        // a step completes - hard numbers instead of guesses about calibration.
        private void TrackGuidanceTaps()
        {
            if (!_guidanceActive || _simplifiedSteps == null) return;
            bool anyDown = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A)
                || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D);
            if (anyDown) _tapsForCurrentStep++;
        }

        // Tracked independently of player.moving (which may never even flip true for a
        // very brief tap) - watches the raw WASD keys directly (same keys MovementAxisPatch
        // reads) so a quick single tap that bumps a wall still gets a sound, instead of only
        // sustained holding.
        private Vector2 _pendingTapDirection;

        private void HandleSingleTapWallBump(PlayerController player)
        {
            bool anyMovementKeyDown = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A)
                || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D);

            if (anyMovementKeyDown && player != null)
            {
                _pendingTapCheckTime = Time.unscaledTime + SingleTapCheckDelay;
                _pendingTapStartPosition = player.transform.position;
                _pendingTapDirection = GetHeldMovementDirection();
            }

            if (!_pendingTapCheckTime.HasValue || Time.unscaledTime < _pendingTapCheckTime.Value) return;

            if (player != null && _pendingTapStartPosition.HasValue)
            {
                float moved = Vector3.Distance(_pendingTapStartPosition.Value, player.transform.position);
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Single-tap check - moved={moved:F3} (threshold={TileSize * 0.2f:F3})");
                if (moved < TileSize * 0.2f)
                {
                    // User's explicit request: a different sound for bumping into a
                    // non-wall obstacle (closed door, furniture) - same "(Clone)" signal as
                    // the directional wall sound and the sustained bump loop.
                    bool isItem = IsBlockedByNonWallItem(player.transform.position, _pendingTapDirection, out _);
                    if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Tap bump classified as {(isItem ? "item" : "wall")}, dir={_pendingTapDirection}");
                    if (isItem)
                        CustomSounds.PlayItemBumpOnce();
                    else
                        CustomSounds.PlayWallBumpOnce();
                }
            }
            _pendingTapCheckTime = null;
            _pendingTapStartPosition = null;
        }

        private void HandleDirectionChangeSound()
        {
            Direction current = PlayerController.GetPlayerDirection(1);
            if (_lastFacingDirection == current) return;

            bool isFirstRead = _lastFacingDirection == null;
            _lastFacingDirection = current;
            if (isFirstRead) return;

            float pan = current == Direction.Left ? -1f : current == Direction.Right ? 1f : 0f;
            CustomSounds.PlayDirectionChange(pan);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Facing direction changed to {current}, pan={pan}");
        }

        private List<(string name, Vector3 position, string category)> BuildTargetList()
        {
            var list = new List<(string name, Vector3 position, string category)>();
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            Location playerLocation = PlayerController.GetPlayer(1).LEOIMFNKFGA;
            if (_rememberedEntranceDoor != null)
            {
                list.Add(("Porta da taverna", GetDoorWalkablePosition(_rememberedEntranceDoor, playerPos), "Portas"));
            }

            // User's explicit request: doors should show up just by being in the same
            // area, without needing to have been opened first (that requirement only
            // exists for telling THIS one apart as "the entrance" specifically).
            foreach (var door in Object.FindObjectsOfType<Door>())
            {
                if (door == _rememberedEntranceDoor) continue;
                if (Vector3.Distance(playerPos, door.transform.position) > NearbyDoorRadius) continue;
                list.Add((DescribeDoor(door), GetDoorWalkablePosition(door, playerPos), "Portas"));
            }

            // Round 107: area exits between locations (cellar<->tavern, etc.) are TravelZone
            // components, NOT Door components - confirmed in the log the cellar exit is
            // "TravelZone-CellarToTavern" and so never appeared in the door list. List nearby ones
            // under "Portas" too (they're passages). lookDirection/playerPosition aside, the zone's
            // own transform is where the player walks into it.
            foreach (var zone in Object.FindObjectsOfType<TravelZone>())
            {
                if (zone == null) continue;
                if (Vector3.Distance(playerPos, zone.transform.position) > NearbyDoorRadius) continue;
                list.Add((DescribeTravelZone(zone), zone.transform.position, "Portas"));
            }

            // Round 107: bed was added unconditionally, so it showed even in the cellar ("a cama
            // não deve aparecer ali na adega, é outra área"). The Location filter can't separate
            // them (the cellar shares the tavern's Location), so gate it by proximity like every
            // other item - 30 units covers the tavern building but excludes the far-off cellar.
            if (Bed.IsValid())
            {
                // Round 113: route to where the SLEEP PROMPT actually triggers (the bed's
                // sleepCollider) instead of GetPlayerBedPosition() - the user struggled to reach
                // the bed ("foi uma luta encontrar ela"), and GetPlayerBedPosition can sit on a
                // non-walkable tile making the route inconsistent. The sleepCollider centre is the
                // walkable trigger zone where "quer dormir?" appears.
                Vector3 bedTarget = (Bed.instance != null && Bed.instance.sleepCollider != null)
                    ? (Vector3)Bed.instance.sleepCollider.bounds.center
                    : Bed.GetPlayerBedPosition();
                if (Vector3.Distance(playerPos, bedTarget) <= NearbyDoorRadius)
                    list.Add(("Cama", bedTarget, "Decorativos"));
            }

            // User's explicit request: all items nearby too, same "by proximity" rule as
            // doors.
            foreach (var placeable in Object.FindObjectsOfType<Placeable>())
            {
                if (Vector3.Distance(playerPos, placeable.transform.position) > NearbyDoorRadius) continue;

                // Confirmed live: at least one Placeable ("BarManager") has no visual
                // representation at all - a manager script, not a real physical object -
                // and was showing up in the list as a confusing, meaningless entry. Skip
                // anything with nothing to actually look at.
                if (placeable.GetComponent<SpriteRenderer>() == null) continue;

                list.Add((DescribePlaceable(placeable), GetApproachPosition(placeable.gameObject, playerPos), CategorizePlaceable(placeable)));
            }

            // Round 112: the food prep table (NinjaPreparationTable) is its OWN MonoBehaviour, not
            // necessarily a Placeable, so the loop above may miss it - scan it directly and list it
            // under "Máquinas" (user: "essa mesa de menus não está aparecendo em maquinas"). Dedup
            // at the end collapses it if it was also caught as a Placeable.
            foreach (var prep in Object.FindObjectsOfType<NinjaPreparationTable>())
            {
                if (prep == null || Vector3.Distance(playerPos, prep.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Mesa de preparação", GetApproachPosition(prep.gameObject, playerPos), "Máquinas"));
            }

            // User's explicit request: floor stains from the cleaning tutorial goal
            // ("Limpe as manchas do chão") weren't in the list at all - confirmed in
            // decompiled source they're a separate component (FloorDirt: MonoBehaviour,
            // IHoverable, IProximity), not a Placeable, so the loop above never saw them.
            // Tagged "Missão" per request - everything tied to the active goal goes there;
            // for now this covers floor stains specifically (the one confirmed live), not a
            // generic goal-to-object mapping for every future quest.
            // Approach position added (was raw dirt.transform.position) - same fix already
            // applied to Placeable targets (see GetApproachPosition's barrel-in-a-wall note):
            // a target's exact center isn't guaranteed to be a walkable tile, which made
            // Home-key routing to floor stains unreliable ("rotas muito imprecisas").
            // User's explicit report: "os bancos estão numerados errados" - root cause was
            // ordering by LIVE distance-to-player, which changes every time the player moves
            // even slightly, so "Mancha 1"/"Banco 1" could silently point at a different
            // physical object between one Page Up/Down press and the next. Ordering by fixed
            // world position instead (x then y) keeps the same object at the same number
            // regardless of where the player is standing when the list gets rebuilt.
            var nearbyDirt = Object.FindObjectsOfType<FloorDirt>()
                .Where(d => Vector3.Distance(playerPos, d.transform.position) <= NearbyDoorRadius)
                .OrderBy(d => d.transform.position.x).ThenBy(d => d.transform.position.y)
                .ToList();
            for (int i = 0; i < nearbyDirt.Count; i++)
            {
                // User's explicit request: tell stains apart when several are nearby -
                // before this they were all identically named "Mancha no chão", making it
                // impossible to know which one Page Up/Down had actually selected.
                // User's explicit request: always number, even when there's only one -
                // more predictable than switching format depending on count.
                string dirtName = $"Mancha no chão {i + 1}";
                list.Add((dirtName, GetApproachPosition(nearbyDirt[i].gameObject, playerPos), "Pendentes"));
            }

            // Round 107: the cellar rats ("Remova os ratos da adega" goal) - listed under
            // "Pendentes", numbered by stable x-then-y order. Round 112: from the game's live list
            // (SceneReferences.tutorialRats) instead of FindObjectsOfType - no scan cost.
            var ratList = SceneReferences.GetSceneReferences()?.tutorialRats;
            if (ratList != null)
            {
                var nearbyRats = ratList
                    .Where(r => r != null && Vector3.Distance(playerPos, r.transform.position) <= NearbyDoorRadius)
                    .OrderBy(r => r.transform.position.x).ThenBy(r => r.transform.position.y)
                    .ToList();
                for (int i = 0; i < nearbyRats.Count; i++)
                {
                    list.Add(($"Rato {i + 1}", GetApproachPosition(nearbyRats[i], playerPos), "Pendentes"));
                }
            }

            // Same situation as floor stains: user reported benches announced fine by
            // proximity (HandleSeatAnnouncement, which scans Seat directly) but missing from
            // this Page Up/Down list - root cause confirmed by that exact mismatch: Seat
            // isn't necessarily on the same GameObject as a Placeable (the attempted
            // GetComponent<Seat>() check inside CategorizePlaceable, now removed, only ever
            // ran for objects the Placeable loop above already found), so it needs its own
            // direct loop here too, same as FloorDirt.
            GameObject heldObjectForList = SelectObject.GetPlayer(1)?.selectedGameObject;
            // Round 102: only list benches that still need action (NOT yet associated to a table).
            // Once a bench is associated (Seat.table != null), the user asked to drop it from the
            // pending list - it's done, no longer something to navigate to.
            var nearbySeats = Object.FindObjectsOfType<Seat>()
                .Where(s => s.table == null && !(s.placeable != null && s.placeable.gameObject == heldObjectForList) && Vector3.Distance(playerPos, s.transform.position) <= NearbyDoorRadius)
                .OrderBy(s => s.transform.position.x).ThenBy(s => s.transform.position.y)
                .ToList();
            for (int i = 0; i < nearbySeats.Count; i++)
            {
                // Global number (see GetSeatNumber) instead of this list's own local index -
                // user's explicit request was to be able to tell WHICH bench is which
                // consistently, and a radius-filtered local index changes meaning between
                // this list and the live proximity announcement.
                string seatName = $"Banco {GetSeatNumber(nearbySeats[i])} (sem mesa)";
                list.Add((seatName, GetApproachPosition(nearbySeats[i].gameObject, playerPos), "Pendentes"));
            }

            // User's explicit request: not just "a bench is somewhere near a table" but the
            // EXACT spot(s) a table actually wants one - confirmed in decompiled Table.cs
            // there's a real, precise answer: a private SeatingGroup[] (each with its own
            // world Transform and an "occupied" bool already tracked by the game). No public
            // getter exists, so reading it via reflection (AccessTools.Field) - this is just
            // reading existing state, not patching/changing any game behavior.
            // Round 112: use the cached seat/table arrays (refreshed by RefreshSeatSceneCache)
            // instead of two fresh FindObjectsOfType scans every time the nav list is rebuilt.
            var emptySlots = GetEmptySeatSlots(playerPos, NearbyDoorRadius, _cachedTables, _cachedSeats);
            for (int i = 0; i < emptySlots.Count; i++)
            {
                string slotName = $"Lugar pra banco {emptySlots[i].slotNumber} ({emptySlots[i].tableLabel})";
                list.Add((slotName, emptySlots[i].pos, "Pendentes"));
            }

            // User's explicit request: don't list things outside the tavern building while
            // the player hasn't left it - only show items in the SAME Location as the
            // player right now (e.g. "tentando achar a escadaria não a encontrei" - it likely
            // led to/from a different Location). Location is the coarse, building-level
            // concept (confirmed: the cellar shares the tavern's Location, since "1125 -
            // Cellar Door" never triggered the cross-area fallback message in earlier
            // testing) - the cellar door itself still shows even closed, since it's the same
            // Location and the user wants to know it's there, just blocked for now.
            list.RemoveAll(entry => playerLocation != Location.None && Utils.HJPCBBGHPDA(entry.position) != Location.None
                && Utils.HJPCBBGHPDA(entry.position) != playerLocation);

            // User reported the same physical door appearing twice (e.g. the cellar door
            // showing up both as a generic "Porta" from the Door list AND separately as
            // "Cellar Door" from the Placeable list - same GameObject pair, same position).
            // Collapse anything sharing a category and sitting within half a tile of an
            // already-kept entry, keeping the first (Door-list entries are added first, so
            // they win - their name is already reliable for these cases).
            var deduped = new List<(string name, Vector3 position, string category)>();
            foreach (var entry in list)
            {
                bool isDuplicate = deduped.Any(d => d.category == entry.category && Vector3.Distance(d.position, entry.position) < TileSize);
                if (!isDuplicate) deduped.Add(entry);
            }
            return deduped;
        }

        private static float _lastSeatSlotDiagLogTime;

        // Looked up once instead of via AccessTools.Field on every call - reflection lookups
        // aren't free, and this is now on a hot-ish path (every ~0.3s) since the lag fix.
        private static readonly System.Reflection.FieldInfo SeatingGroupsField = AccessTools.Field(typeof(Table), "seatingGroups");

        // Round 85 diagnostic - moving both the Placeable's and the Seat's own transform
        // (rounds 82/84) still didn't fix table association. Seat.GetNeighbourTable actually
        // reads from ITS OWN private "buildSquare" field (a BuildSquare component reference),
        // not transform.position directly - and that field is never reassigned anywhere in the
        // decompiled source, meaning it's a serialized/Inspector reference into the bench's
        // prefab hierarchy. Walking the actual GameObject parent chain at runtime (not more
        // guessing from decompiled text) to find out for certain whether buildSquare is a child
        // of the Placeable, the Seat, both, or neither.
        private static readonly System.Reflection.FieldInfo SeatBuildSquareField = AccessTools.Field(typeof(Seat), "buildSquare");

        private static string DescribeHierarchy(Transform t)
        {
            if (t == null) return "(null)";
            var names = new System.Collections.Generic.List<string>();
            for (var cur = t; cur != null; cur = cur.parent) names.Add(cur.name);
            names.Reverse();
            return string.Join(" > ", names);
        }

        public static void LogBuildSquareHierarchy(Seat seat, GameObject placeableGO)
        {
            if (!Main.DebugMode || seat == null) return;
            var buildSquare = SeatBuildSquareField.GetValue(seat) as Component;
            DebugLogger.LogState($"WorldNav: hierarchy diag - Placeable=\"{DescribeHierarchy(placeableGO.transform)}\" pos={placeableGO.transform.position}");
            DebugLogger.LogState($"WorldNav: hierarchy diag - Seat=\"{DescribeHierarchy(seat.transform)}\" pos={seat.transform.position}");
            DebugLogger.LogState($"WorldNav: hierarchy diag - buildSquare=\"{(buildSquare != null ? DescribeHierarchy(buildSquare.transform) : "null")}\" pos={(buildSquare != null ? buildSquare.transform.position.ToString() : "n/a")}");
        }

        // User's explicit request: announce/identify WHICH bench was grabbed and WHICH table
        // it ended up next to. Numbered GLOBALLY so "Banco 3" means the same physical bench
        // whether it's mentioned by the live proximity announcement (small radius), the Page
        // Up/Down list (large radius), or DecorationModeHandler's grab/place feedback.
        //
        // Round 70 bug, confirmed via log: the original version re-sorted ALL seats by their
        // CURRENT position on every call - fine for seats that never move, but decoration mode
        // exists specifically to MOVE them. Moving a bench changes its rank in that live sort,
        // so the very same physical bench got a different number every time it was picked up
        // (log showed object "1135 - Banco Grande(Clone)" announced as "Banco 8" twice, then
        // "Banco 4" later, with no other bench involved). Fixed: assign each seat/table a
        // number ONCE (first time anything asks about it, ordered by position AT THAT MOMENT
        // among not-yet-numbered ones) and cache it permanently - subsequent moves don't
        // reshuffle existing numbers, only newly-discovered seats/tables get appended.
        private static readonly List<Seat> _numberedSeats = new List<Seat>();
        private static readonly List<Table> _numberedTables = new List<Table>();

        public static int GetSeatNumber(Seat seat)
        {
            if (!_numberedSeats.Contains(seat))
            {
                var newlyFound = Object.FindObjectsOfType<Seat>()
                    .Where(s => !_numberedSeats.Contains(s))
                    .OrderBy(s => s.transform.position.x).ThenBy(s => s.transform.position.y);
                _numberedSeats.AddRange(newlyFound);
            }
            return _numberedSeats.IndexOf(seat) + 1;
        }

        public static int GetTableNumber(Table table)
        {
            if (!_numberedTables.Contains(table))
            {
                var newlyFound = Object.FindObjectsOfType<Table>()
                    .Where(t => !_numberedTables.Contains(t))
                    .OrderBy(t => t.transform.position.x).ThenBy(t => t.transform.position.y);
                _numberedTables.AddRange(newlyFound);
            }
            return _numberedTables.IndexOf(table) + 1;
        }

        // Mirrors GetSeatNumber/GetTableNumber above - finds the Seat component that goes
        // with a given Placeable's GameObject (they're never the same GameObject, see the
        // "public Placeable placeable" note elsewhere in this file) so DecorationModeHandler
        // can identify what it just grabbed/placed without duplicating this lookup.
        public static Seat FindSeatForPlaceable(GameObject placeableGO)
        {
            if (placeableGO == null) return null;
            foreach (var seat in Object.FindObjectsOfType<Seat>())
            {
                if (seat.placeable != null && seat.placeable.gameObject == placeableGO) return seat;
            }
            return null;
        }

        // Diagnostic only (no behavior change) - round 70's report that placing a bench
        // "exactly" where the slot announcement says still gets rejected, and placing it
        // nearby comes back "sem mesa por perto", needs real numbers to pin down rather than
        // another guess. Confirmed via decompiled Seat.GetNeighbourTable/Table.GetSeatingGroup
        // that matching depends on the seat's own facing direction (not just position) and a
        // tight tolerance (0.225 units) - logs exactly how far off the final placement was from
        // every nearby slot, and which way the seat ended up facing, so the next round's log can
        // show the real gap instead of guessing at it again.
        public static void LogSeatPlacementDiagnostics(Seat seat)
        {
            if (!Main.DebugMode || seat == null) return;
            Vector3 seatPos = seat.transform.position;
            Direction facing = seat.placeable != null ? seat.placeable.GetDirection() : Direction.Up;
            DebugLogger.LogState($"WorldNav: seat placement diag - seat at {seatPos} facing={facing} table={(seat.table != null ? seat.table.gameObject.name : "null")}");
            foreach (var table in Object.FindObjectsOfType<Table>())
            {
                if (Vector3.Distance(table.transform.position, seatPos) > TileSize * 6f) continue;
                var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
                if (groups == null) continue;
                foreach (var group in groups)
                {
                    if (group.transform == null) continue;
                    float dist = Vector3.Distance(group.transform.position, seatPos);
                    DebugLogger.LogState($"WorldNav: seat placement diag - table \"{table.gameObject.name}\" slot pos={group.transform.position} slotDir={group.direction} dist={dist:F3}");
                }
            }
        }

        // Round 87: round 86 found table=null even calling the engine's own search directly -
        // so the search itself is missing the table, not a timing issue. GetNeighbourTable's
        // exact search point is "buildSquare.GetCentrePosition() + Utils.NGFODNCHPHB(direction) *
        // 0.5f", compared against tables within a tight 0.225 radius. GetSeatTargetPosition (our
        // own placement formula, written in round 71 to avoid visually overlapping the table)
        // ALSO adds slot.position + the same kind of 0.5-unit directional offset - if that offset
        // and the engine's own search offset point the same way, they'd stack instead of
        // cancelling, landing the search point roughly a tile-width past where the table actually
        // is. Logging the literal search point vs every nearby table's position settles this with
        // a number instead of more formula-reasoning.
        public static void LogTableSearchGap(Seat seat)
        {
            if (!Main.DebugMode || seat == null) return;
            var buildSquare = SeatBuildSquareField.GetValue(seat) as Component;
            if (buildSquare == null) return;
            var getCentrePosition = AccessTools.Method(buildSquare.GetType(), "GetCentrePosition");
            Vector3 centre = (Vector3)getCentrePosition.Invoke(buildSquare, null);
            Direction facing = seat.placeable != null ? seat.placeable.GetDirection() : Direction.Up;
            Vector3 searchPos = centre + Utils.NGFODNCHPHB(facing) * 0.5f;
            DebugLogger.LogState($"WorldNav: table search gap - buildSquare centre={centre} facing={facing} searchPos={searchPos}");
            foreach (var table in Object.FindObjectsOfType<Table>())
            {
                float dist = Vector3.Distance(table.transform.position, searchPos);
                if (dist > TileSize * 6f) continue;
                DebugLogger.LogState($"WorldNav: table search gap - table \"{table.gameObject.name}\" pos={table.transform.position} distFromSearchPos={dist:F3}");
            }

            // Round 89: pivot-to-pivot distance is only a proxy - Seat.GetNeighbourTable's real
            // check is Physics2D.OverlapCircleNonAlloc against actual colliders (excluding
            // triggers), so a table with a collider larger than a point could still be found even
            // several tenths of a unit past its pivot, or could be missed even when close if its
            // collider is a trigger (explicitly skipped by that code) or on the wrong layer.
            // Running the literal same query here removes all that guesswork.
            var hits = Physics2D.OverlapCircleAll(searchPos, 0.225f, CommonReferences.GGFJGHHHEJC.objectLayers);
            DebugLogger.LogState($"WorldNav: table search gap - live OverlapCircle at {searchPos} r=0.225 found {hits.Length} collider(s)");
            foreach (var hit in hits)
            {
                var tableHit = hit.GetComponentInParent<Table>();
                DebugLogger.LogState($"WorldNav: table search gap - hit \"{hit.gameObject.name}\" isTrigger={hit.isTrigger} layer={LayerMask.LayerToName(hit.gameObject.layer)} table={(tableHit != null ? tableHit.gameObject.name : "none")}");
            }
        }

        // Round 90: generic "surface decoration" placement (paintings/plants/centerpieces etc,
        // received from a shop order - see docs/modules/inventory-and-items.md). Read
        // Placeable.PEFFMJOMPMN (called every frame from WhileSelected, same as the bench's
        // GetNeighbourTable association) in full: items with isPlaceableOnSurface == true get
        // auto-attached to whatever SurfaceSortOrder the CURSOR currently sits over
        // (CursorManager.GetCursorWorldPosition() + mouse offset, fed into Utils.CCCCIKOMAEN -
        // a Physics2D.OverlapPointAll wrapper - then filtered by SurfaceSortOrder.IsItemAllowed).
        // Same class of problem as the bench's table search: that automatic system depends on the
        // cursor truthfully tracking the held item, which round 82 proved it does NOT for
        // keyboard-driven movement. Reusing the exact same point-overlap + IsItemAllowed check,
        // just fed from the Placeable's OWN transform.position (which DecorationModeHandler does
        // keep accurate) instead of the cursor - mirrors how the bench fix took direct ownership
        // of GetNeighbourTable instead of trusting the automatic per-frame version.
        public static SurfaceSortOrder FindSurfaceAtPosition(Vector3 position, Placeable placeable)
        {
            if (placeable == null || placeable.itemSetup == null) return null;
            var hits = Utils.CCCCIKOMAEN<SurfaceSortOrder>(position);
            foreach (var surface in hits)
            {
                if (surface != null && surface.IsItemAllowed(placeable.itemSetup.item, placeable, placeable.surfaceGOInstantiated))
                {
                    return surface;
                }
            }
            return null;
        }

        // Guidance counterpart to FindNearestEmptySlot, for items that need ANY valid surface
        // (table/shelf/etc with a SurfaceSortOrder) rather than a specific seating slot. No
        // existing engine utility does this scene-wide search (confirmed - PEFFMJOMPMN only ever
        // checks whatever's directly under the cursor, never searches for a nearby one), so this
        // is new, not ported from a hidden game method.
        public static SurfaceSortOrder FindNearestValidSurface(Vector3 position, float maxDistance, Placeable placeable)
        {
            if (placeable == null || placeable.itemSetup == null) return null;
            SurfaceSortOrder best = null;
            float bestDist = maxDistance;
            foreach (var surface in Object.FindObjectsOfType<SurfaceSortOrder>())
            {
                if (surface == null) continue;
                if (!surface.IsItemAllowed(placeable.itemSetup.item, placeable, placeable.surfaceGOInstantiated)) continue;
                float dist = Vector3.Distance(position, surface.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = surface;
                }
            }
            return best;
        }

        // Round 101: candles/tablecloths/centerpieces only COUNT for "Coloque seus novos itens
        // na taverna" when they snap onto a designated SnapToPosition on a TABLE - the round-100
        // log proved the candle was attaching to a generic surface named "Surface" with
        // snapped=False every time, so it never registered. The game's own GetSnapItem picks the
        // snap via the CURSOR (round 82 proved that's unreliable for us), so instead this scans
        // the public snapToPositionArray of every surface directly, finds the nearest FREE snap
        // whose item matches, and returns its exact world position. Snapping the item onto that
        // point is what makes AddPlaceableToSurface set snappedToPosition = true.
        public static Vector3? FindNearestSnapPosition(Vector3 position, float maxDistance, Placeable placeable, out SurfaceSortOrder owningSurface)
        {
            owningSurface = null;
            if (placeable == null || placeable.itemSetup == null || placeable.itemSetup.item == null) return null;
            int itemId = placeable.itemSetup.item.JDJGFAACPFC();
            Vector3? best = null;
            float bestDist = maxDistance;
            foreach (var surface in Object.FindObjectsOfType<SurfaceSortOrder>())
            {
                if (surface == null || surface.snapToPositionArray == null) continue;
                foreach (var snap in surface.snapToPositionArray)
                {
                    if (snap == null || snap.used || snap.transform == null) continue;
                    bool matches = (snap.item != null && snap.item.JDJGFAACPFC() == itemId);
                    if (!matches && snap.items != null)
                    {
                        foreach (var alt in snap.items)
                        {
                            if (alt != null && alt.JDJGFAACPFC() == itemId) { matches = true; break; }
                        }
                    }
                    if (!matches) continue;
                    float dist = Vector3.Distance(position, snap.transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = snap.transform.position;
                        owningSurface = surface;
                    }
                }
            }
            return best;
        }

        // Debug helper: log every surface near the held item that has a free snap position for it,
        // so we can confirm whether snap-based placement (candles/tablecloths) has a real target.
        public static void LogSnapTargets(Placeable placeable, float maxDistance)
        {
            if (placeable == null || placeable.itemSetup == null || placeable.itemSetup.item == null) return;
            int itemId = placeable.itemSetup.item.JDJGFAACPFC();
            Vector3 pos = placeable.transform.position;
            int found = 0;
            foreach (var surface in Object.FindObjectsOfType<SurfaceSortOrder>())
            {
                if (surface == null || surface.snapToPositionArray == null) continue;
                for (int i = 0; i < surface.snapToPositionArray.Length; i++)
                {
                    var snap = surface.snapToPositionArray[i];
                    if (snap == null || snap.transform == null) continue;
                    bool matches = (snap.item != null && snap.item.JDJGFAACPFC() == itemId);
                    if (!matches && snap.items != null)
                        foreach (var alt in snap.items) if (alt != null && alt.JDJGFAACPFC() == itemId) { matches = true; break; }
                    if (!matches) continue;
                    found++;
                    float dist = Vector3.Distance(pos, snap.transform.position);
                    DebugLogger.LogState($"WorldNav: snap target surface=\"{surface.gameObject.name}\" snapPos={snap.transform.position} used={snap.used} dist={dist:F2}");
                }
            }
            if (found == 0) DebugLogger.LogState($"WorldNav: NO snap target found for item id {itemId} (item does not use table snap positions, or none nearby)");
        }

        // Round 97: unified "nearest valid placement position" using the game's OWN
        // IsObjectInValidLocation check, replacing the per-category replications. The round-93
        // hand-rolled wall check (4 corners of itemBase.bounds vs WorldGrid tile flags) was
        // confirmed wrong by the round-96 log: it returned a point (6.08, 910.10) the game's real
        // Deselect/IsObjectInValidLocation rejected even a full frame later. The round-96 itemSpace
        // variant already proved that temporarily moving the real object and calling the game's own
        // check works perfectly (the plant placed). Generalizing that to ALL non-seat items - the
        // game's check internally covers itemSpace, wall (itemBase) AND physicalSpace, so the point
        // returned here is GUARANTEED to be one Enter will actually accept, instead of an
        // approximation. Physics2D.SyncTransforms() forces Collider2D.bounds (read by the wall
        // path) to update from each transform write within this synchronous loop. Candidates are
        // checked nearest-first and it early-outs at the closest valid one, so the common case
        // (a valid spot nearby) is cheap; only the "no valid spot anywhere" case pays the full scan.
        // Round 104: find the nearest transform position where the painting's WALL geometry check
        // passes, by replicating the game's own Placeable.FNPBNFFEBAF EXACTLY (verified by reading
        // it - Placeable.cs:1688). The big correction this round: the validity check operates on
        // the 4 corners of itemBase.bounds, NOT on transform.position (round 103 wrongly tested the
        // transform against the wall-tile grid, which is why it kept guiding to spots that weren't
        // actually placeable). FNPBNFFEBAF requires: all 4 bounds corners are wall tiles
        // (WorldGrid.ALNFLFCLIEP) AND each has a floor below at one consistent height
        // (WorldGrid.KHJJCAGIJAP). Both are pure tile-data lookups - STABLE, no physics flicker, so
        // this can be scanned synchronously (unlike IsObjectInValidLocation, whose physicalSpace
        // sub-check reads a FixedUpdate-only trigger list). The transform->bounds offset is
        // item-specific, so it's measured at runtime from the live itemBase.bounds. Occupancy is a
        // separate flicker-free distance check against existing wall Placeables. physicalSpace and
        // the remaining IsObjectInValidLocation sub-checks are handled at confirm time by the
        // settle-retry (which spans real frames). Returns the nearest passing transform position.
        public static Vector3? FindNearestValidWallPosition(Placeable placeable, float maxDistance)
        {
            if (placeable == null || placeable.itemBase == null) return null;
            Vector3 origin = placeable.transform.position;
            Bounds b = placeable.itemBase.bounds;
            Vector3 centerOffset = b.center - origin; // collider offset relative to the transform
            Vector3 ext = b.extents;
            var wallItems = Object.FindObjectsOfType<Placeable>()
                .Where(p => p != null && p.isPlaceableOnWall && p.gameObject != placeable.gameObject)
                .ToList();
            const float step = 0.5f;
            int range = Mathf.CeilToInt(maxDistance / step);
            Vector3? best = null;
            float bestDist = float.MaxValue;
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    Vector3 c = new Vector3(origin.x + dx * step, origin.y + dy * step, origin.z);
                    float d = Vector3.Distance(origin, c);
                    if (d > maxDistance || d >= bestDist) continue;
                    if (!WallGeometryValidAt(c + centerOffset, ext)) continue;
                    if (wallItems.Any(w => Vector3.Distance(w.transform.position, c) < step)) continue;
                    best = c;
                    bestDist = d;
                }
            }
            return best;
        }

        // Exact replica of Placeable.FNPBNFFEBAF (Placeable.cs:1688) for a candidate bounds centre.
        private static bool WallGeometryValidAt(Vector3 boundsCenter, Vector3 ext)
        {
            Vector2[] corners =
            {
                new Vector2(boundsCenter.x - ext.x, boundsCenter.y + ext.y),
                new Vector2(boundsCenter.x + ext.x, boundsCenter.y + ext.y),
                new Vector2(boundsCenter.x - ext.x, boundsCenter.y - ext.y),
                new Vector2(boundsCenter.x + ext.x, boundsCenter.y - ext.y),
            };
            float height = -1000f;
            foreach (var corner in corners)
            {
                if (!WorldGrid.ALNFLFCLIEP(corner)) return false;
                if (!WorldGrid.KHJJCAGIJAP(corner, out float floorY)) return false;
                float h = (float)(int)(floorY * 2f) / 2f;
                if (height == -1000f || height == h) { height = h; continue; }
                return false;
            }
            return true;
        }

        public static Vector3? FindNearestValidPosition(Placeable placeable, float maxDistance)
        {
            if (placeable == null) return null;
            Vector3 original = placeable.transform.position;
            const float step = 0.5f;
            int range = Mathf.CeilToInt(maxDistance / step);

            var candidates = new System.Collections.Generic.List<Vector3>();
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    Vector3 c = new Vector3(original.x + dx * step, original.y + dy * step, original.z);
                    if (Vector3.Distance(original, c) <= maxDistance) candidates.Add(c);
                }
            }
            candidates.Sort((a, b) => Vector3.Distance(original, a).CompareTo(Vector3.Distance(original, b)));

            Vector3? best = null;
            foreach (var c in candidates)
            {
                placeable.transform.position = c;
                Physics2D.SyncTransforms();
                if (placeable.IsObjectInValidLocation(false)) { best = c; break; }
            }
            placeable.transform.position = original;
            Physics2D.SyncTransforms();
            if (Main.DebugMode && !best.HasValue)
            {
                DebugLogger.LogState($"WorldNav: FindNearestValidPosition - NO valid spot within {maxDistance} of {original} ({candidates.Count} candidates checked) - if this is a wall item it may need a different facing/rotation, or there's no valid wall in range");
            }
            return best;
        }

        // Round 99: the painting (wall) and tablecloth (surface) report "Posição válida" /
        // "bem aqui" but Deselect still returns false. Reading the decompiled Placeable.Deselect
        // (line 1847) showed the real gate is IsObjectInValidLocation(BIOKGEFFNAA: TRUE) - we only
        // ever checked (false) - plus DeselectAction's own canBePlaced check. canBePlaced is a
        // dead field (always true, confirmed), so the divergence has to be in the validity check
        // itself OR the object's live state (currentSurface, collider bounds) at the exact deselect
        // moment differing from when we searched. Logs every input to that decision right before
        // Deselect runs, so the next test pins the exact failing sub-check instead of more theory.
        public static void LogDeselectGate(Placeable p, string context)
        {
            if (p == null) return;
            Physics2D.SyncTransforms();
            bool validFalse = p.IsObjectInValidLocation(false);
            bool validTrue = p.IsObjectInValidLocation(true);
            bool physOk = p.physicalSpace == null || p.physicalSpace.ValidPosition();
            DebugLogger.LogState($"WorldNav: deselect gate [{context}] pos={p.transform.position} validFalse={validFalse} validTrue={validTrue} canBePlaced={p.canBePlaced} enabled={p.enabled} attachedToPlaceable={(p.attachedToPlaceable != null)} isPlaceableOnWall={p.isPlaceableOnWall} isPlaceableOnSurface={p.isPlaceableOnSurface} currentSurface={(p.currentSurface != null ? p.currentSurface.gameObject.name : "null")} isOnSurface={p.IsObjectOnASurface()} physicalSpaceOk={physOk}");
        }

        // Round 94: log proved the plant ("Planta Moribunda", hasItemSpace=True, no surface/wall)
        // never reached "Posição válida" anywhere across many grab+arrow-move attempts, even
        // though it uses the same generic itemSpace check benches do (which DOES work). Rather
        // than guess why (e.g. grid-alignment theory), replicate ItemSpace.IsItemSpaceValid's own
        // per-buildSquare checks here (both are public APIs) so the next test's log shows exactly
        // which check is failing instead of more speculation.
        public static void LogItemSpaceValidityDiagnostic(Placeable placeable)
        {
            if (placeable == null || placeable.itemSpace == null || placeable.currentSurface != null) return;
            var buildSquares = placeable.itemSpace.buildSquares;
            if (buildSquares == null) return;
            for (int i = 0; i < buildSquares.Length; i++)
            {
                var square = buildSquares[i];
                if (square == null)
                {
                    DebugLogger.LogState($"WorldNav: itemSpace diag - buildSquare {i} is null");
                    continue;
                }
                Vector3 centre = square.GetCentrePosition();
                Location location = WorldGrid.HJPCBBGHPDA(centre);
                bool locationOk = placeable.IsInValidLocation(location);
                bool squareValid = square.IsValid(placeable.itemSpace, placeable.attachedToPlaceable, true, placeable.specificRules, placeable.itemSpace.checkConstructionPositions, placeable.itemSpace.checkHerbs);
                // Round 94 follow-up: squareValid alone doesn't say WHICH of BuildSquare.IsValid's
                // several gates (zone type, ground type, wall tile, player overlap) is the real
                // rejection - replicating those specific sub-checks too (all public APIs) instead
                // of guessing from the single boolean.
                ZoneType zoneHere = WorldGrid.AGKGGAFFFGM(centre);
                GroundType groundHere = WorldGrid.NCEHFMPBBAK(centre);
                bool isWallTile = WorldGrid.ALNFLFCLIEP(centre);
                float distToPlayer = Vector3.Distance(centre, PlayerController.GetPlayerPosition(1));
                // Round 95: round 94's diagnostic ruled out location/zone/ground/wall (all fine
                // away from the wall) yet squareValid stayed False even 8-10 units from the
                // player - pointing at BuildSquare.IsValid's last gate, WorldGrid.NGDHDMAMGPI
                // (checks WorldTile.canPlaceObjects and whether blockingObjects is already
                // registered there - the real "is this tile occupied by clutter" check, separate
                // from a live Physics2D overlap). Reading the WorldTile directly (both public)
                // to log the actual blocker by name instead of just a boolean.
                bool canPlaceObjects = false;
                string blockingNames = "n/a";
                if (WorldGrid.GCGNCHFNEBJ(centre, out WorldTile tile))
                {
                    canPlaceObjects = tile.canPlaceObjects;
                    blockingNames = tile.blockingObjects == null ? "none" : string.Join(",", tile.blockingObjects.ConvertAll(go => go != null ? go.name : "null"));
                }
                DebugLogger.LogState($"WorldNav: itemSpace diag - square {i} pos={centre} location={location} locationOk={locationOk} squareValid={squareValid} zoneHere={zoneHere} zoneNeeded={placeable.zoneTypeNeeded} groundHere={groundHere} groundNeeded={placeable.groundTypeNeeded} isWallTile={isWallTile} distToPlayer={distToPlayer:F2} attachedToPlayer={placeable.attachedToPlayer} canPlaceObjects={canPlaceObjects} blockingObjects={blockingNames}");
            }
        }

        // Same stability problem and same fix as GetSeatNumber/GetTableNumber - SeatingGroup is
        // a reference type (a plain serialized class, not a struct), so its identity persists
        // across calls even though it has no transform of its own to be "the same GameObject" -
        // safe to use directly as a list key like the Seat/Table components above.
        private static readonly List<SeatingGroup> _numberedSlots = new List<SeatingGroup>();

        public static int GetSlotNumber(SeatingGroup group)
        {
            if (!_numberedSlots.Contains(group))
            {
                var allGroups = new List<SeatingGroup>();
                foreach (var table in Object.FindObjectsOfType<Table>())
                {
                    var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
                    if (groups != null) allGroups.AddRange(groups.Where(g => g != null && g.transform != null));
                }
                var newlyFound = allGroups.Where(g => !_numberedSlots.Contains(g))
                    .OrderBy(g => g.transform.position.x).ThenBy(g => g.transform.position.y);
                _numberedSlots.AddRange(newlyFound);
            }
            return _numberedSlots.IndexOf(group) + 1;
        }

        // Round 71 feature: user explicitly asked for automatic snap-to-slot + auto-rotate on
        // placement instead of needing to hit the exact mark by hand (confirmed very hard with
        // 0.5-unit cursor steps against a 0.225-unit engine tolerance that also depends on
        // facing direction - see LogSeatPlacementDiagnostics above). Called once, only when
        // Enter is pressed to confirm a Seat's placement (not a hot per-frame path), so a fresh
        // scan here is fine. maxDistance is deliberately more forgiving than the engine's own
        // 0.225 - the player only needs to walk UP TO a slot, not hit it pixel-perfect; this
        // function (and DecorationModeHandler) does the exact alignment from there.
        // Round 76: DecorationModeHandler started calling FindNearestEmptySlot every 0.3s while
        // a bench is held (for the live guidance announcement), but this method was calling
        // Object.FindObjectsOfType<Table>() AND <Seat>() directly EVERY call - given the
        // ~150-180ms per-call cost confirmed in round 74's timers, that's ~300ms+ of stall every
        // 0.3 seconds while holding something, a severe regression nobody had measured yet.
        // Static cache shared by this method and LogNearestSlotDistance below, same "identity is
        // stable, only position changes" reasoning as WorldNavigationHandler's instance-level
        // seat/table cache - just needs its own copy since this is a static method.
        private static Table[] _staticCachedTables;
        private static Seat[] _staticCachedSeats;
        private static float _staticCacheTime = -999f;
        private const float StaticSceneCacheInterval = 20f;

        private static void RefreshStaticSceneCache()
        {
            if (_staticCachedTables != null && Time.unscaledTime - _staticCacheTime < StaticSceneCacheInterval) return;
            _staticCacheTime = Time.unscaledTime;
            _staticCachedTables = Object.FindObjectsOfType<Table>();
            _staticCachedSeats = Object.FindObjectsOfType<Seat>();
        }

        public static SeatingGroup FindNearestEmptySlot(Vector3 position, float maxDistance, out Table ownerTable)
        {
            RefreshStaticSceneCache();
            ownerTable = null;
            GameObject heldNow = SelectObject.GetPlayer(1)?.selectedGameObject;
            SeatingGroup best = null;
            float bestDist = maxDistance;
            foreach (var table in _staticCachedTables)
            {
                if (table == null) continue;
                var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
                if (groups == null) continue;
                foreach (var group in groups)
                {
                    if (group.transform == null) continue;
                    float dist = Vector3.Distance(position, group.transform.position);
                    if (dist > bestDist) continue;
                    bool occupiedByRealSeat = false;
                    foreach (var seat in _staticCachedSeats)
                    {
                        if (seat == null) continue;
                        bool held = seat.placeable != null && seat.placeable.gameObject == heldNow;
                        if (held) continue;
                        if (Vector3.Distance(seat.transform.position, group.transform.position) < 0.3f)
                        {
                            occupiedByRealSeat = true;
                            break;
                        }
                    }
                    if (occupiedByRealSeat) continue;
                    best = group;
                    bestDist = dist;
                    ownerTable = table;
                }
            }
            return best;
        }

        // Round 76: DecorationModeHandler now locks onto one target slot per hold (instead of
        // re-picking "nearest" every check, which flip-flopped between two similarly-close slots
        // and never converged - confirmed in log: the announced distance oscillated between two
        // values, e.g. "9 pra direita"/"10 pra direita", dozens of times). This lets it confirm
        // the lock is still good (nobody else took the slot in the meantime) without re-running
        // the full nearest-search.
        public static bool IsSlotEmpty(SeatingGroup slot)
        {
            RefreshStaticSceneCache();
            GameObject heldNow = SelectObject.GetPlayer(1)?.selectedGameObject;
            foreach (var seat in _staticCachedSeats)
            {
                if (seat == null) continue;
                bool held = seat.placeable != null && seat.placeable.gameObject == heldNow;
                if (held) continue;
                if (Vector3.Distance(seat.transform.position, slot.transform.position) < 0.3f)
                {
                    // Round 77 diagnostic - the locked slot kept getting dropped/re-picked
                    // within under a second of being locked, with no key pressed in between.
                    // Logging exactly which seat caused IsSlotEmpty to reject it, instead of
                    // guessing further.
                    if (Main.DebugMode)
                    {
                        bool heldByGameObjectName = seat.placeable != null && heldNow != null && seat.placeable.gameObject.name == heldNow.name;
                        DebugLogger.LogState($"WorldNav: IsSlotEmpty - slot pos={slot.transform.position} rejected by seat \"{seat.gameObject.name}\" (instanceId={seat.GetInstanceID()}) seatPos={seat.transform.position} seat.placeable={(seat.placeable != null ? seat.placeable.gameObject.name + " (id=" + seat.placeable.gameObject.GetInstanceID() + ")" : "null")} heldNow={(heldNow != null ? heldNow.name + " (id=" + heldNow.GetInstanceID() + ")" : "null")} sameNameButDifferentId={heldByGameObjectName && (seat.placeable.gameObject != heldNow)}");
                    }
                    return false;
                }
            }
            return true;
        }

        // Round 73 diagnostic only - FindNearestEmptySlot above silently returns null whenever
        // nothing qualifies within maxDistance, which doesn't say HOW far the real nearest slot
        // actually was. Called only when a snap attempt fails to find anything, to get a real
        // distance number instead of guessing whether the radius is too tight.
        public static void LogNearestSlotDistance(Vector3 position)
        {
            if (!Main.DebugMode) return;
            RefreshStaticSceneCache();
            SeatingGroup nearest = null;
            Table nearestTable = null;
            float nearestDist = float.MaxValue;
            foreach (var table in _staticCachedTables)
            {
                if (table == null) continue;
                var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
                if (groups == null) continue;
                foreach (var group in groups)
                {
                    if (group.transform == null) continue;
                    float dist = Vector3.Distance(position, group.transform.position);
                    if (dist < nearestDist) { nearestDist = dist; nearest = group; nearestTable = table; }
                }
            }
            if (nearest == null)
            {
                DebugLogger.LogState("WorldNav: snap diag - no seating slot exists anywhere in the scene");
            }
            else
            {
                DebugLogger.LogState($"WorldNav: snap diag - nearest slot is {nearestDist:F2} units away (table \"{nearestTable.gameObject.name}\", slot pos={nearest.transform.position}, slotDir={nearest.direction})");
            }
        }

        // Round 72 bug fix: the snap-to-slot feature placed the bench's own centre AT
        // group.transform.position directly - every attempt then failed canBePlaced (log:
        // "confirm placement -> False snapped=True", repeated). Re-derived the right target
        // from the engine's own formulas instead of guessing again: `Seat.GetNeighbourTable`
        // looks for a table near (seat's own centre + facing direction * 0.5), and
        // `Table.GetSeatingGroup` checks that the SAME kind of point (slot tile + slot's
        // direction * 0.5) is free/walkable - in both cases the "+ direction * 0.5" step moves
        // from the TABLE side to the SEAT side. So group.transform.position is the table-edge
        // reference point, not the seat's own resting spot - the seat's centre needs to be
        // pushed OUTWARD from the table by half a tile, in the slot's own direction, to clear
        // the table's footprint instead of overlapping it.
        //
        // Round 89: that "group.transform.position is the table-edge reference point" line was
        // never actually verified against the table's own data - it was an assumption, and it's
        // the reason rounds 87/88 still measured a 0.6-0.8 unit gap after fixing the facing
        // direction. Read Table.PlaceSeatingGroup in full: the engine's OWN code computes this
        // exact target as "placeable.itemSpace.buildSquares[slot.buildSquares.x].
        // GetCentrePosition() + Utils.NGFODNCHPHB(Utils.ABNPPDOGEPM(seatDirection)) * 0.5f" - i.e.
        // it starts from one of the TABLE's own buildSquare cells (the specific cell this seating
        // group is attached to), not from the slot's transform. Using the literal same source
        // value instead of the group marker removes the guesswork the round-72 comment above was
        // built on.
        public static Vector3 GetSeatTargetPosition(SeatingGroup slot, Table ownerTable)
        {
            if (ownerTable != null && ownerTable.placeable != null && ownerTable.placeable.itemSpace != null)
            {
                var tableBuildSquares = ownerTable.placeable.itemSpace.buildSquares;
                int idx = slot.buildSquares.x;
                if (idx >= 0 && idx < tableBuildSquares.Length && tableBuildSquares[idx] != null)
                {
                    return tableBuildSquares[idx].GetCentrePosition() + Utils.NGFODNCHPHB(slot.direction) * 0.5f;
                }
            }
            return slot.transform.position + Utils.NGFODNCHPHB(slot.direction) * 0.5f;
        }

        // Shared by BuildTargetList (nav list) and HandleSeatSlotAnnouncement (proximity
        // speech) - see the "Lugar pra banco" note above for why this reads a private field.
        // Takes the scene-wide table/seat arrays as parameters instead of scanning internally
        // (lag fix - the caller now controls how often that expensive scan actually happens;
        // BuildTargetList, only called on demand by Page Up/Down, scans fresh every time,
        // while the per-frame proximity caller passes the once-a-second cache instead).
        private static List<(Vector3 pos, string tableLabel, int slotNumber)> GetEmptySeatSlots(Vector3 playerPos, float radius, Table[] allTables, Seat[] allSeats)
        {
            // Round 112: cheap early-out. If no table is within range there are no slots to
            // compute - skip the whole reflection/distance scan. The log showed this running every
            // ~1.5s at ~15ms even at the oven (far from any table, "0 slots" every time), a real
            // continuous micro-stutter.
            bool anyTableNear = false;
            if (allTables != null)
            {
                foreach (var t in allTables)
                {
                    if (t != null && Vector3.Distance(playerPos, t.transform.position) <= radius) { anyTableNear = true; break; }
                }
            }
            if (!anyTableNear) return new List<(Vector3 pos, string tableLabel, int slotNumber)>();

            // Numbered GLOBALLY across the WHOLE scene (not just the nearby/radius-filtered
            // subset) for the same reason as GetSeatNumber/GetTableNumber above - so "vaga 2"
            // means the same physical slot whether it's the live proximity announcement (small
            // radius) or the Page Up/Down list (large radius) asking.
            var allTablesOrdered = allTables
                .Where(t => t != null)
                .OrderBy(t => t.transform.position.x).ThenBy(t => t.transform.position.y)
                .ToList();
            // User's explicit request to validate, not assume: the debug log added last round
            // confirmed `occupied` NEVER flips true (checked a full play session's worth of
            // log lines, all `occupied=False`, even right after placing a bench) - and
            // `Table.PlaceSeatingGroup`/`GetSeatingGroup` (the only methods that ever write to
            // it) are confirmed to have ZERO call sites anywhere in decompiled source. This
            // flag just isn't maintained by any currently-active code path - not a timing
            // issue, not our bug. Falling back to a real, computed check instead: is there
            // already a Seat sitting close to this slot's position right now.
            GameObject heldNow = SelectObject.GetPlayer(1)?.selectedGameObject;
            var allEmptySlots = new List<(Vector3 pos, string tableLabel, SeatingGroup group)>();
            for (int t = 0; t < allTablesOrdered.Count; t++)
            {
                var groups = SeatingGroupsField.GetValue(allTablesOrdered[t]) as SeatingGroup[];
                if (groups == null) continue;
                // Same stability fix as GetSeatNumber/GetTableNumber - this table can itself be
                // moved in decoration mode, so a live re-sorted index would relabel it too.
                string tableLabel = $"mesa {GetTableNumber(allTablesOrdered[t])}";
                foreach (var group in groups)
                {
                    if (group.transform == null) continue;
                    bool occupiedByRealSeat = false;
                    foreach (var seat in allSeats)
                    {
                        if (seat == null) continue;
                        bool held = seat.placeable != null && seat.placeable.gameObject == heldNow;
                        if (held) continue;
                        if (Vector3.Distance(seat.transform.position, group.transform.position) < 0.3f) { occupiedByRealSeat = true; break; }
                    }
                    if (Main.DebugMode && Time.unscaledTime - _lastSeatSlotDiagLogTime > 1f)
                    {
                        DebugLogger.LogState($"WorldNav: seating group for \"{allTablesOrdered[t].gameObject.name}\" gameOccupiedFlag={group.occupied} realSeatNearby={occupiedByRealSeat} pos={group.transform.position}");
                    }
                    if (occupiedByRealSeat) continue;
                    allEmptySlots.Add((group.transform.position, tableLabel, group));
                }
            }
            if (Main.DebugMode && Time.unscaledTime - _lastSeatSlotDiagLogTime > 1f) _lastSeatSlotDiagLogTime = Time.unscaledTime;

            // Slot numbers come from GetSlotNumber (assigned once, stable forever) instead of a
            // recomputed index here - same instability class as GetSeatNumber/GetTableNumber:
            // the table (and therefore its slots, which are children of it) can be moved in
            // decoration mode, which would otherwise reshuffle "vaga N" for slots that never
            // moved relative to each other.
            var result = new List<(Vector3 pos, string tableLabel, int slotNumber)>();
            foreach (var slot in allEmptySlots)
            {
                if (Vector3.Distance(playerPos, slot.pos) > radius) continue;
                result.Add((slot.pos, slot.tableLabel, GetSlotNumber(slot.group)));
            }
            return result;
        }

        // Classified by real component types confirmed in decompiled source (Container.cs,
        // Crafter.cs, Placeable.canBeAddedToInventory) - not guessed from names.
        private static string CategorizePlaceable(Placeable placeable)
        {
            // Round 102: a placed candle is a working consumable - user wants it under
            // "Repositivos" (restockables) while still lit, but moved to "Pendentes" once fully
            // spent (needs replacing). Checked BEFORE the Crafter branch (the candle carries a
            // Crafter). Spent threshold is the game's own (Crafter fuel <= 1).
            if (placeable.itemSetup != null && placeable.itemSetup.item != null
                && placeable.itemSetup.item.JDJGFAACPFC() == CandleItemId)
            {
                var candleCrafter = placeable.GetComponent<Crafter>() ?? placeable.GetComponentInChildren<Crafter>();
                return (candleCrafter != null && candleCrafter.LCCABPFHCOL <= 1) ? "Pendentes" : "Repositivos";
            }
            // Round 112/113: crafting/serving stations the user wants under "Máquinas" - the drinks
            // table/dispenser, the barrels and the food prep table. Checked BEFORE Container, since
            // DrinkDispenser/BanquetBarrel ARE Containers but the user wants them as machines.
            if (IsDrinkStation(placeable) != null
                || placeable.GetComponent<NinjaPreparationTable>() != null || placeable.GetComponentInChildren<NinjaPreparationTable>() != null)
            {
                return "Máquinas";
            }
            if (placeable.GetComponent<Container>() != null) return "Containers";
            if (placeable.GetComponent<Crafter>() != null) return "Máquinas";
            string nm = placeable.gameObject.name.ToLowerInvariant();
            if (nm.Contains("bebida") || nm.Contains("preparac") || nm.Contains("preparation")) return "Máquinas";

            // User's explicit correction: a cellar door and a staircase (both Placeable,
            // not Door component - that's why they end up here instead of the dedicated
            // door list) are passages, not decoration. "Decorativos" should mean
            // window/vase/purely-cosmetic, confirmed live GameObject names: "Puerta",
            // "Cellar Door", "Escalera Arriba".
            string rawName = placeable.gameObject.name.ToLowerInvariant();
            if (rawName.Contains("puerta") || rawName.Contains("door") || rawName.Contains("escalera") || rawName.Contains("stair"))
            {
                return "Portas";
            }

            // User's explicit request: anything the active goal asked to clean should be
            // in "Missão" too, not just floor stains. Confirmed in decompiled source: a
            // table is a SEPARATE component (Table, with its own public dirt-level
            // property) alongside Placeable on the same GameObject - if it's currently
            // dirty, it's the same "Limpe a mesa" goal seen in the tutorial text.
            var table = placeable.GetComponent<Table>();
            if (table != null && table.JNHCCCBICDM != TableDirtLevel.Perfect && table.JNHCCCBICDM != TableDirtLevel.Clean)
            {
                return "Pendentes";
            }

            if (placeable.canBeAddedToInventory) return "Coletáveis";
            return "Decorativos";
        }

        // User tracked a barrel and reported it "getting very lost" - confirmed in log the
        // route NEVER succeeded (pathfinding kept failing for over a minute) because the
        // registered position was literally inside the wall/object's own footprint, not a
        // walkable tile a player could ever stand on - same root cause as the door issue,
        // just for ordinary placed objects instead. Placeable doesn't expose a dedicated
        // walkable-offset list like Door does, but most have a Collider2D marking their
        // solid footprint - this nudges the target to just outside that footprint, on the
        // side facing the player, which lands on a walkable tile in the common case.
        private static Vector3 GetApproachPosition(GameObject target, Vector3 playerPos)
        {
            var collider = target.GetComponent<Collider2D>();
            if (collider == null) return target.transform.position;

            Vector3 center = collider.bounds.center;
            Vector3 toPlayer = playerPos - center;
            toPlayer.z = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f) return target.transform.position;

            Vector3 direction = toPlayer.normalized;
            Vector3 extents = collider.bounds.extents;
            float reach = Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y;
            // User reported needing several tries at a chest before "Você chegou" fired -
            // the buffer pushed the target a half tile past the collider edge, farther than
            // where the player actually needed to stand. Shrunk to a quarter tile.
            Vector3 result = center + direction * (reach + TileSize * 0.25f);
            // User reported pathfinding consistently failing for some objects (e.g. the tap/
            // dispenser, "Torneira") that aren't behind a closed door - logging the computed
            // point to confirm (not guess) whether this heuristic is landing somewhere
            // unwalkable for wall-mounted objects specifically.
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: GetApproachPosition for \"{target.name}\" -> center={center} result={result}");
            return result;
        }

        private static Vector3 GetDoorWalkablePosition(Door door, Vector3 playerPos)
        {
            if (door.freeNodesOnOpen == null || door.freeNodesOnOpen.Length == 0) return door.transform.position;

            Vector3 basePos = door.transform.position;
            Vector3 best = basePos + (Vector3)door.freeNodesOnOpen[0];
            float bestDist = Vector3.Distance(best, playerPos);
            for (int i = 1; i < door.freeNodesOnOpen.Length; i++)
            {
                Vector3 candidate = basePos + (Vector3)door.freeNodesOnOpen[i];
                float d = Vector3.Distance(candidate, playerPos);
                if (d < bestDist)
                {
                    best = candidate;
                    bestDist = d;
                }
            }
            return best;
        }

        private static string DescribeDoor(Door door)
        {
            string name = door.gameObject.name;
            return name == "Door" ? "Porta" : name;
        }

        // Round 107: a passage between areas (cellar exit etc.). Named by where it leads when
        // that's known (locationTo), else by the cleaned GameObject name.
        private static string DescribeTravelZone(TravelZone zone)
        {
            string loc = LocationName(zone.locationTo);
            if (!string.IsNullOrEmpty(loc)) return $"Passagem para {loc}";
            string n = zone.gameObject.name.Replace("TravelZone-", "").Replace("TravelZone", "").Trim();
            return string.IsNullOrEmpty(n) ? "Passagem" : $"Passagem: {n}";
        }

        private static string LocationName(Location loc)
        {
            switch (loc)
            {
                case Location.Tavern: return "a taverna";
                case Location.City:
                case Location.CityOutside: return "a cidade";
                case Location.CityTavern: return "a taverna da cidade";
                case Location.Road: return "a estrada";
                case Location.River: return "o rio";
                case Location.Quarry: return "a pedreira";
                case Location.Farm: return "a fazenda";
                case Location.Mine: return "a mina";
                case Location.Beach: return "a praia";
                case Location.Forest: return "a floresta";
                case Location.Camp: return "o acampamento";
                default: return null;
            }
        }

        // Round 113: drink-serving stations the user wants clearly named and under "Máquinas":
        // DrinkDispenser/DrinksTable (accept all drink types) -> "Dispensador de bebidas";
        // ServiceBarrel/BanquetBarrel (only sparkling) -> "Barril". Returns null if not one.
        private static string IsDrinkStation(Placeable placeable)
        {
            if (placeable == null) return null;
            // Round 114/116: the "mesa de menu" (BarMenuManager, opens BigContainerUI) - where the
            // player adds cooked food to the tavern menu. Round 116 fix: BarMenuManager lives on a
            // DIFFERENT GameObject and references its Placeable via .placeable, so GetComponent on
            // the placeable missed it and it fell through to the drink-dispenser name. Compare
            // against BarMenuManager.instance.placeable directly (robust), plus the name fallback.
            var barMenu = BarMenuManager.instance;
            if ((barMenu != null && barMenu.placeable == placeable)
                || placeable.GetComponent<BarMenuManager>() != null || placeable.GetComponentInChildren<BarMenuManager>() != null
                || placeable.gameObject.name.Contains("BigContainer"))
                return "Mesa de menu";
            // Barrels FIRST: a ServiceBarrel CONTAINS a DrinkDispenser (confirmed: ServiceBarrel
            // .drinkDispenser), so a barrel GameObject has both - "Barril" is the more specific name.
            if (placeable.GetComponent<ServiceBarrel>() != null || placeable.GetComponentInChildren<ServiceBarrel>() != null
                || placeable.GetComponent<BanquetBarrel>() != null || placeable.GetComponentInChildren<BanquetBarrel>() != null)
                return "Barril";
            // Round 121: there are several drink dispensers - differentiate them by the drink they
            // hold (lastDrink), since sighted players tell them apart by colour. Falls back to the
            // dispenser id when empty.
            var dd = placeable.GetComponent<DrinkDispenser>() ?? placeable.GetComponentInChildren<DrinkDispenser>();
            if (dd != null)
            {
                var drink = dd.lastDrink?.LHBPOPOIFLE();
                string drinkName = drink != null ? drink.IABAKHPEOAF() : null;
                return !string.IsNullOrEmpty(drinkName) ? $"Dispensador de bebidas, {drinkName}" : $"Dispensador de bebidas {dd.drinkDispenserId}";
            }
            if (placeable.GetComponent<DrinksTable>() != null || placeable.GetComponentInChildren<DrinksTable>() != null)
                return "Dispensador de bebidas";
            return null;
        }

        private static string DescribePlaceable(Placeable placeable)
        {
            // Round 113: name the drink stations explicitly (before the itemSetup name, which is
            // either missing - "Mesa de Bebidas" had none - or the generic "Barril").
            string drinkName = IsDrinkStation(placeable);
            if (drinkName != null) return drinkName;

            // User reported confusing/cryptic names ("dispenser de bebidas", "armário
            // grande, sei lá") from the GameObject-name heuristic. Placeable has a direct
            // reference to the real Item data (Placeable.itemSetup.item) - using the item's
            // own IABAKHPEOAF() (decompiled) instead of looking up nameId directly, since
            // some items use a different "Items/item_name_<id>" key (translationByID) that
            // a direct nameId lookup misses entirely (confirmed: caused a real item - the
            // mop - to wrongly report empty/"Vazio" elsewhere). IABAKHPEOAF() already knows
            // about that and falls back to the raw asset name if no translation exists.
            string itemName = placeable.itemSetup != null && placeable.itemSetup.item != null
                ? placeable.itemSetup.item.IABAKHPEOAF()
                : null;
            if (!string.IsNullOrEmpty(itemName))
            {
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Item name from itemSetup: \"{itemName}\" (GameObject=\"{placeable.gameObject.name}\")");
                return itemName;
            }

            string name = placeable.gameObject.name;
            if (name.EndsWith("(Clone)")) name = name.Substring(0, name.Length - "(Clone)".Length).Trim();

            int dashIndex = name.IndexOf(" - ");
            if (dashIndex >= 0 && int.TryParse(name.Substring(0, dashIndex), out _))
            {
                name = name.Substring(dashIndex + 3).Trim();
            }

            // Confirmed in the log: every object without itemSetup.item is pure scenery
            // with no Item/localization data at all - its GameObject name IS the dev's
            // original asset name, in Spanish (this game's source assets are Spanish-named
            // even though displayed Item text is localized). Translating the SPECIFIC
            // Spanish names actually observed live, not guessing a general dictionary.
            if (FallbackNameTranslations.TryGetValue(name, out var translated)) name = translated;

            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Item name fallback (no itemSetup.item): \"{name}\" (GameObject=\"{placeable.gameObject.name}\")");
            return name;
        }

        private static readonly Dictionary<string, string> FallbackNameTranslations = new Dictionary<string, string>
        {
            // Confirmed wrong via log: interacting with this opens "DrinkDispenserUI"
            // (ContentBeerTap) - it's a drink dispenser tap, not a sink faucet.
            { "Grifo", "Dispenser de Bebidas" },
            { "Malteadora", "Moedor de Malte" },
            { "Trapo Colgado", "Pano Pendurado" },
            { "Grupo Ladrillos", "Grupo de Tijolos" },
            { "Cajas Apiladas", "Caixas Empilhadas" },
            { "Lateral Habitacion", "Parede Lateral do Quarto" },
            { "Escalera Arriba", "Escada" },
            { "Horno Variant", "Forno" },
            { "Mesa de Cocina Variant", "Mesa de Cozinha" },
            { "Ventana de Madera", "Janela de Madeira" },
            { "Puerta", "Porta" },
            { "Cofre Pequeño", "Cofre Pequeno" },
            { "Cama del Jugador", "Cama do Jogador" },
            { "Barril de Servicio", "Barril de Serviço" },
            { "Cellar Door", "Porta do Porão" },
        };

        private void HandleFloorDirtAnnouncement()
        {
            var manager = InputByProximityManager.GetPlayer(1);
            if (manager == null) return;

            var current = manager.GetCurrentFocusedInputElement();
            FloorDirt currentDirt = current?.mainGameObject != null ? current.mainGameObject.GetComponent<FloorDirt>() : null;

            if (currentDirt == _lastFloorDirtFocus) return;
            _lastFloorDirtFocus = currentDirt;
            if (currentDirt == null) return;

            ScreenReader.Say("Próximo: Mancha no chão: segure E pra limpar", interrupt: false);
            if (Main.DebugMode) DebugLogger.LogState("WorldNav: FloorDirt proximity announcement spoken");
        }

        // User reported the game lagging/freezing badly after these were added - root cause:
        // Throttling just the ANNOUNCEMENT logic (0.3s, last round's fix) wasn't enough on its
        // own - the user still reported lag. Root cause: it only throttled how often the
        // expensive Object.FindObjectsOfType (full scene scan) calls ran, but each cycle still
        // fired up to 3 separate scans (Seat here, Seat again + Table inside
        // GetEmptySeatSlots) - ~10/sec total, far more than the established pattern elsewhere
        // (ItemSoundCycleInterval: ONE scan per second). Splitting the concern properly now:
        // the scene scan itself is cached for a full second (matching that pattern) and shared
        // between both seat methods, while the cheap distance-check/announcement logic that
        // reads the cache can run every frame without needing its own throttle.
        private Seat[] _cachedSeats = new Seat[0];
        private Table[] _cachedTables = new Table[0];
        private float _lastSeatSceneCacheTime = -999f;

        // Round 74: found the REAL cause of "muito lento" via the round-73 timers - it was
        // never about how MANY scans ran per second, it's that a single
        // Object.FindObjectsOfType<T>() call in this scene costs ~150-180ms by itself (measured
        // live - confirmed in the PERF log even with only 8 seats/1 table as the result), almost
        // certainly because the call's cost scales with the TOTAL object count in the scene
        // (lots of decorative tiles/props), not the small number actually returned. Once per
        // second was still far too often at that per-call cost. Since seat/table IDENTITY is
        // stable (confirmed across the numbering work - decoration mode only moves them, never
        // destroys/recreates them) and their live positions already come for free from the
        // cached objects' own transforms, there's no need to re-fetch the array itself often -
        // widened drastically; this is a safety net against rare cases (new construction) more
        // than a "needs to be fresh every second" requirement.
        private const float SeatSceneCacheInterval = 30f;

        // Round 102: placed candles (id 605) for the proximity announcement below. Round 113:
        // derived from the shared _cachedAllPlaceables scan instead of its own pass.
        private Placeable[] _cachedCandles = new Placeable[0];

        // Round 113: ONE cached scan of all Placeables, shared by the candle proximity AND the
        // item-proximity sounds. HandleItemProximitySounds was doing its OWN FindObjectsOfType<
        // Placeable> EVERY second (~87ms spike per second - a major continuous stutter and the real
        // cause of "anuncios de item proximo demora muito"). Now both read this cache.
        private Placeable[] _cachedAllPlaceables = new Placeable[0];
        private float _lastAllPlaceablesTime = -999f;
        private const float AllPlaceablesInterval = 15f;

        private void RefreshSeatSceneCache()
        {
            // Round 112: rats now come from the game's own live list (SceneReferences.tutorialRats)
            // - no FindObjectsOfType<TutorialRat> scan, and death/count is instant (the list updates
            // when a rat is destroyed). See HandleRatAnnouncement.
            if (Time.unscaledTime - _lastAllPlaceablesTime >= AllPlaceablesInterval)
            {
                _lastAllPlaceablesTime = Time.unscaledTime;
                var swc = Main.DebugMode ? System.Diagnostics.Stopwatch.StartNew() : null;
                _cachedAllPlaceables = Object.FindObjectsOfType<Placeable>();
                _cachedCandles = _cachedAllPlaceables
                    .Where(p => p != null && p.itemSetup != null && p.itemSetup.item != null && p.itemSetup.item.JDJGFAACPFC() == CandleItemId)
                    .ToArray();
                if (swc != null && swc.ElapsedMilliseconds > 3) DebugLogger.LogState($"WorldNav: PERF placeable scan took {swc.ElapsedMilliseconds}ms ({_cachedAllPlaceables.Length} placeables, {_cachedCandles.Length} candles)");
            }

            if (Time.unscaledTime - _lastSeatSceneCacheTime < SeatSceneCacheInterval) return;
            _lastSeatSceneCacheTime = Time.unscaledTime;
            var sw = Main.DebugMode ? System.Diagnostics.Stopwatch.StartNew() : null;
            _cachedSeats = Object.FindObjectsOfType<Seat>();
            _cachedTables = Object.FindObjectsOfType<Table>();
            if (sw != null && sw.ElapsedMilliseconds > 3) DebugLogger.LogState($"WorldNav: PERF RefreshSeatSceneCache took {sw.ElapsedMilliseconds}ms ({_cachedSeats.Length} seats, {_cachedTables.Length} tables)");
        }

        private GameObject _lastNearRat;
        private Vector3 _lastNearRatPos;
        private int _lastRatCount = -1;
        private float _lastRatMoveAnnounceTime;
        private const float RatMoveAnnounceInterval = 0.6f;

        // Round 108/111/112: rats. Round 112 - read the game's OWN live list
        // (SceneReferences.tutorialRats), not a FindObjectsOfType scan: it's free to read and the
        // count is exact/instant (the game removes a rat from it the moment it's destroyed), which
        // fixes both the lag ("caçar os ratos está com muito lag") and makes the death announce
        // reliable. Proximity on approach, removal announce when the count drops, and which way the
        // nearest rat moved (they wander).
        private readonly System.Collections.Generic.Dictionary<Customer, CustomerState> _customerStates =
            new System.Collections.Generic.Dictionary<Customer, CustomerState>();
        private readonly System.Collections.Generic.Dictionary<Customer, bool> _customerServed =
            new System.Collections.Generic.Dictionary<Customer, bool>();
        private readonly System.Collections.Generic.HashSet<Customer> _customerOrderAnnounced =
            new System.Collections.Generic.HashSet<Customer>();
        private float _lastTavernServiceCheck;

        private static string CustomerWantWord(Customer c)
        {
            var reqItem = c.currentRequest?.LHBPOPOIFLE();
            return reqItem != null && !string.IsNullOrEmpty(reqItem.IABAKHPEOAF()) ? reqItem.IABAKHPEOAF()
                : (c.preference == CustomerPreference.Drink ? "bebida" : "comida");
        }

        // Round 118/119: announce the tavern service loop. New customer -> "Cliente chegou"; ready
        // to serve (OrderInTable) -> "Cliente quer {item}"; served (hasBeenServed) -> "Pedido
        // servido"; leaving -> "Cliente saiu satisfeito/insatisfeito" (by hasBeenServed). Serving
        // is manual (item on tray + E next to them) OR remote with the Z/X keys (HandleServeKeys).
        private bool _tavernServeHooked;
        private int _lastDirtCount = -1;
        private int _lastTrayDrinkCount = -1;
        private int _lastRowdyCount = -1;

        // Round 127: find rowdy customers by MOOD (currentMoodState == Rowdy) across the live
        // customer list, not just TavernManager.customersRowdy - more robust for V (calm) / Delete
        // (expel) and the "new rowdy" announcement.
        private static int CountRowdyCustomers()
        {
            var tm = TavernManager.GGFJGHHHEJC;
            if (tm == null || tm.customers == null) return 0;
            int n = 0;
            foreach (var c in tm.customers)
                if (c != null && (c.currentMoodState == MoodState.Rowdy || c.customerState == CustomerState.BeingANuisance)) n++;
            return n;
        }

        private static Customer FindNearestRowdyCustomer()
        {
            var tm = TavernManager.GGFJGHHHEJC;
            if (tm == null || tm.customers == null) return null;
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            Customer target = null;
            float best = float.MaxValue;
            foreach (var c in tm.customers)
            {
                if (c == null) continue;
                if (c.currentMoodState != MoodState.Rowdy && c.customerState != CustomerState.BeingANuisance) continue;
                float d = Vector3.Distance(playerPos, c.transform.position);
                if (d < best) { best = d; target = c; }
            }
            return target;
        }

        private void HandleTavernServiceAnnouncements()
        {
            // Round 120: hook the game's own serve event (fires synchronously inside every serve -
            // player E, the Z/X keys, or an employee) so "servido" is RELIABLE. The 0.5s poll on
            // hasBeenServed missed fast serves (user: served 5, only 2 announced). Subscribed once.
            if (!_tavernServeHooked)
            {
                var cr = CommonReferences.GGFJGHHHEJC;
                if (cr != null)
                {
                    cr.OnAnyCustomerServeItem += (pn, item) =>
                    {
                        string n = item?.LHBPOPOIFLE()?.IABAKHPEOAF();
                        ScreenReader.Say(string.IsNullOrEmpty(n) ? "Pedido servido" : $"{n} servido", interrupt: false);
                    };
                    _tavernServeHooked = true;
                }
            }

            // Round 124: poll a bit faster (0.25s) so orders are announced sooner.
            if (Time.unscaledTime - _lastTavernServiceCheck < 0.25f) return;
            _lastTavernServiceCheck = Time.unscaledTime;

            // Round 122: announce when a NEW floor stain appears (user: "manchas... não foram
            // anunciadas"). The proximity announcement only fires when you walk up to one.
            var crRefs = CommonReferences.GGFJGHHHEJC;
            int dirtCount = crRefs?.tavernFloorDirt != null ? crRefs.tavernFloorDirt.Count : 0;
            if (_lastDirtCount >= 0 && dirtCount > _lastDirtCount)
                ScreenReader.Say(dirtCount - _lastDirtCount == 1 ? "Mancha nova no chão" : $"{dirtCount - _lastDirtCount} manchas novas no chão", interrupt: false);
            _lastDirtCount = dirtCount;

            // Round 123: drinks must be FULLY filled before they land on the tray (currentDrinks) -
            // the user had no feedback that a cup was complete, so X kept failing with an empty tray
            // ("serve X - tray=[]"). Announce when a drink reaches the tray so they know it's ready.
            // Round 127: announce when a new customer turns rowdy so the player knows there's one to
            // calm (V) or kick out (Delete) - "não atualiza para ver proximos clientes".
            int rowdyCount = CountRowdyCustomers();
            if (_lastRowdyCount >= 0 && rowdyCount > _lastRowdyCount)
                ScreenReader.Say("Cliente ficou bravo, V acalma ou Delete expulsa", interrupt: false);
            _lastRowdyCount = rowdyCount;

            var trayDrinks = PlayerController.GetPlayer(1)?.trayHandler?.tray?.currentDrinks;
            int trayCount = trayDrinks != null ? trayDrinks.Count : 0;
            if (_lastTrayDrinkCount >= 0 && trayCount > _lastTrayDrinkCount && trayDrinks != null && trayDrinks.Count > 0)
            {
                string drink = trayDrinks[trayDrinks.Count - 1]?.LHBPOPOIFLE()?.IABAKHPEOAF();
                ScreenReader.Say(string.IsNullOrEmpty(drink) ? "Bebida pronta na bandeja, aperte X pra servir" : $"{drink} na bandeja, aperte X pra servir", interrupt: false);
            }
            _lastTrayDrinkCount = trayCount;

            var tm = TavernManager.GGFJGHHHEJC;
            var customers = tm != null ? tm.customers : null;
            if (customers == null) { if (_customerStates.Count > 0) { _customerStates.Clear(); _customerServed.Clear(); } return; }

            var seen = new System.Collections.Generic.HashSet<Customer>();
            foreach (var c in customers)
            {
                if (c == null) continue;
                seen.Add(c);
                CustomerState state = c.customerState;
                bool known = _customerStates.TryGetValue(c, out var prev);
                if (!known) ScreenReader.Say("Cliente chegou", interrupt: false);
                _customerStates[c] = state;
                // Round 124: announce the order as soon as the customer is in a serveable state AND
                // has an order, tracked per-customer (not tied to the exact state-transition frame) -
                // the user reported bar orders being "left behind". Re-arms when they leave the
                // serveable state so a second order announces again.
                bool serveable = state == CustomerState.OrderInTable || state == CustomerState.WaitingAtBar;
                if (serveable && c.currentRequest != null)
                {
                    if (_customerOrderAnnounced.Add(c))
                    {
                        string where = state == CustomerState.WaitingAtBar ? "no balcão" : "na mesa";
                        ScreenReader.Say($"Cliente {where} quer {CustomerWantWord(c)}. Z comida, X bebida", interrupt: false);
                    }
                }
                else if (!serveable) _customerOrderAnnounced.Remove(c);
                // Track hasBeenServed for the satisfied/dissatisfied announcement on leave. The
                // "servido" announcement itself is handled by the OnAnyCustomerServeItem hook above.
                _customerServed[c] = c.hasBeenServed;
            }

            // Departures: tell the player whether they left satisfied (served) or not.
            if (_customerStates.Count > seen.Count)
            {
                var gone = new System.Collections.Generic.List<Customer>();
                foreach (var kv in _customerStates) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var g in gone)
                {
                    _customerServed.TryGetValue(g, out bool served);
                    _customerStates.Remove(g);
                    _customerServed.Remove(g);
                    _customerOrderAnnounced.Remove(g);
                    ScreenReader.Say(served ? "Cliente saiu satisfeito" : "Cliente saiu insatisfeito", interrupt: false);
                }
            }
        }

        // Round 119: serve a waiting customer WITHOUT walking to them - Z serves food, X serves
        // drink ("não ficar indo e vindo"). Finds the nearest customer in OrderInTable with that
        // preference and calls the game's own ServeCustomer (no distance check of its own - it
        // serves currentRequest from the player's tray). If the item isn't on the tray it fails.
        private void HandleServeKeys()
        {
            bool z = Input.GetKeyDown(KeyCode.Z);
            bool x = Input.GetKeyDown(KeyCode.X);
            if (!z && !x) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

            var tm = TavernManager.GGFJGHHHEJC;
            if (tm == null || tm.customers == null) { ScreenReader.Say("Nenhum cliente", interrupt: true); return; }

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            Customer target = null;
            float bestDist = float.MaxValue;
            int matchCount = 0;
            foreach (var c in tm.customers)
            {
                if (c == null || c.currentRequest == null) continue;
                if (c.customerState != CustomerState.OrderInTable && c.customerState != CustomerState.WaitingAtBar) continue;
                // Round 121: classify by the ORDERED ITEM (currentRequest.JEPBBEBJEFI() = is a drink),
                // not Customer.preference - the user reported Z and X doing the same thing, so the
                // preference field wasn't reliably food-vs-drink. Z serves food orders, X drink orders.
                bool isDrink = c.currentRequest.JEPBBEBJEFI();
                if (z && isDrink) continue;   // Z = food only
                if (x && !isDrink) continue;   // X = drink only
                matchCount++;
                float d = Vector3.Distance(playerPos, c.transform.position);
                if (d < bestDist) { bestDist = d; target = c; }
            }
            if (Main.DebugMode)
            {
                if (target == null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var c in tm.customers)
                    {
                        if (c == null) continue;
                        string req = c.currentRequest?.LHBPOPOIFLE()?.IABAKHPEOAF() ?? "null";
                        string kind = c.currentRequest != null ? (c.currentRequest.JEPBBEBJEFI() ? "bebida" : "comida") : "?";
                        sb.Append($"[{c.customerState}/{kind}/{req}] ");
                    }
                    DebugLogger.LogState($"WorldNav: serve key {(z ? "Z food" : "X drink")} - NO MATCH. customers: {sb}");
                }
                else DebugLogger.LogState($"WorldNav: serve key {(z ? "Z food" : "X drink")} pressed - {matchCount} matching, target found");
            }
            if (target == null)
            {
                // Round 130: the user insists they can serve at the bar with E while Z says nothing.
                // As a definitive test (and possible fix), try the game's ServeCustomer on the nearest
                // matching-kind customer in ANY state - ServeCustomer itself decides if the state is
                // serveable. If it works, we learn which state; if not, the log shows it's truly not
                // serveable (timing), and we give the informative message.
                var trayFb = PlayerController.GetPlayer(1)?.trayHandler?.tray;
                Customer fb = null; float fbDist = float.MaxValue;
                bool coming = false, done = false;
                foreach (var c in tm.customers)
                {
                    if (c == null || c.currentRequest == null) continue;
                    if (c.currentRequest.JEPBBEBJEFI() == z) continue; // wrong kind for this key
                    float d = Vector3.Distance(playerPos, c.transform.position);
                    if (d < fbDist) { fbDist = d; fb = c; }
                    if (c.customerState == CustomerState.HeadingToBar || c.customerState == CustomerState.HeadingToSeat) coming = true;
                    else if (c.customerState == CustomerState.EatingAtTable) done = true;
                }
                if (fb != null)
                {
                    bool fbServed = fb.ServeCustomer(1, true, trayFb);
                    if (Main.DebugMode) DebugLogger.LogState($"WorldNav: serve {(z ? "Z" : "X")} FALLBACK try on state={fb.customerState} -> served={fbServed}");
                    if (fbServed) { return; } // OnAnyCustomerServeItem hook announces it
                }
                string what = z ? "comida" : "bebida";
                if (coming) ScreenReader.Say($"Cliente de {what} ainda chegando, espere ouvir no balcão", interrupt: true);
                else if (done) ScreenReader.Say($"Clientes de {what} já foram servidos", interrupt: true);
                else ScreenReader.Say($"Nenhum cliente quer {what}", interrupt: true);
                return;
            }

            var tray = PlayerController.GetPlayer(1)?.trayHandler?.tray;
            string item = CustomerWantWord(target);
            // Round 122: dump what's actually on the tray + the customer's request, to find why a
            // drink the user filled won't serve (tray.MHBHHNCFOEG removes the request instance from
            // currentDrinks - if it's a different instance / non-stackable, it won't match).
            if (Main.DebugMode)
            {
                string trayDrinks = tray?.currentDrinks != null
                    ? string.Join(", ", System.Linq.Enumerable.Select(tray.currentDrinks, d => d?.LHBPOPOIFLE()?.IABAKHPEOAF() ?? "?"))
                    : "tray-null";
                var reqI = target.currentRequest?.LHBPOPOIFLE();
                DebugLogger.LogState($"WorldNav: serve X - request=\"{(reqI != null ? reqI.IABAKHPEOAF() : "?")}\" reqStackable={(reqI != null ? reqI.canBeStacked.ToString() : "?")} tray=[{trayDrinks}] state={target.customerState}");
            }
            bool served = target.ServeCustomer(1, true, tray);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: serve key {(z ? "Z food" : "X drink")} -> served={served} item={item}");
            if (served)
            {
                // The OnAnyCustomerServeItem hook announces "{item} servido" - don't double it here.
                _customerServed[target] = true;
            }
            else
            {
                // Round 121: drinks must be on the TRAY - and filling a cup at a dispenser puts it
                // there automatically (DrinkDispenser.TakeDrink uses trayHandler.tray), so the real
                // fix is filling the RIGHT drink (each dispenser holds one type, now named). Food can
                // come from the inventory.
                bool isDrink = target.currentRequest.JEPBBEBJEFI();
                ScreenReader.Say(isDrink
                    ? $"Não consegui servir {item}. Encha {item} no dispensador certo, vai pra bandeja"
                    : $"Não consegui servir {item}. Precisa ter {item} no inventário ou bandeja", interrupt: true);
            }
        }

        // Round 123: V calms the nearest rowdy customer from anywhere ("acalmar clientes"), mirroring
        // the game's "Calm down" interact (Customer.OBGPLACHKHK(employee), here null = player). It's
        // probabilistic in-game, so it can fail - announce the outcome. KeyCode.V is unused by the game.
        private void HandleCalmKey()
        {
            if (!Input.GetKeyDown(KeyCode.V)) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

            Customer target = FindNearestRowdyCustomer();
            if (target == null) { ScreenReader.Say("Nenhum cliente encrenqueiro", interrupt: true); return; }

            // Round 130: use the game's REAL calm method - CalmCustomer(null) (what E calls, line
            // 873). The objective "Tente acalmar um cliente insatisfeito" tracks this CALL, so my
            // round-128 direct mood-set (MFOPJDFMJBN) silently broke the objective. CalmCustomer only
            // works on a Rowdy, not-yet-nuisance customer; it's probabilistic (calm -> Neutral, or
            // fail -> BecomeNuisance), and in the tutorial it intentionally makes them a nuisance so
            // you then expel them. Either way the "tried to calm" objective ticks.
            if (target.customerState == CustomerState.BeingANuisance)
            {
                ScreenReader.Say("Esse já está fazendo bagunça, use Delete pra expulsar", interrupt: true);
                return;
            }
            bool handled = target.CalmCustomer(null);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: calm key V -> CalmCustomer handled={handled}, mood={target.currentMoodState}, state={target.customerState}");
            if (!handled) { ScreenReader.Say("Não deu pra acalmar esse agora", interrupt: true); return; }
            if (target.currentMoodState == MoodState.Neutral) ScreenReader.Say("Cliente acalmado", interrupt: true);
            else ScreenReader.Say("Tentou acalmar, mas ficou bravo. Use Delete pra expulsar", interrupt: true);
        }

        // Round 125: Delete EXPELS (kicks out) the nearest rowdy customer, with the mop in hand
        // (like the game's mop-hit). Calming (V) and expelling (Delete) are the two responses - the
        // user keeps both. A customer must be BeingANuisance for MarkAsKicked() to work, so a still-
        // Rowdy one is pushed to nuisance first via FHPAMNEIJLI(true). KeyCode.Delete is only used by
        // the game inside inventory drag (MouseSlot), not in the world.
        private void HandleExpelKey()
        {
            if (!Input.GetKeyDown(KeyCode.Delete)) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;

            Item selected = null;
            try { selected = PlayerInventory.GetPlayer(1)?.actionBarInventory?.GetSelectedItem(); }
            catch (System.Exception) { }
            if (!(selected is Mop))
            {
                ScreenReader.Say("Segure o esfregão pra expulsar", interrupt: true);
                return;
            }

            Customer target = FindNearestRowdyCustomer();
            if (target == null) { ScreenReader.Say("Nenhum cliente encrenqueiro", interrupt: true); return; }

            // Round 132 fix: the mission's expel objective is tracked by CommonReferences
            // .OnCustomerIsHit (T112_CalmarCliente subscribes to it), which ONLY fires inside
            // Customer.KickOut(hitDetection) - NOT KickWithForce/MarkAsKicked. So the previous Delete
            // expelled the customer but never completed the objective. Proper path: BecomeNuisance(true)
            // (sets the nuisance flag KickOut requires + fires OnCustomerBecomeNuisance) then
            // KickOut(player's HitDetection) - which fires OnCustomerIsHit, counts "kickedCustomers",
            // and flings them out (HandleSendOut -> KickWithForce). Uses the real player HitDetection
            // (bouncer=false, playerNum=1) so HandleSendOut resolves the force origin.
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            var hd = PlayerController.GetPlayer(1)?.hitDetection;
            if (target.customerState != CustomerState.BeingANuisance) target.BecomeNuisance(true);
            if (hd != null) target.KickOut(hd);
            else target.KickWithForce(playerPos); // fallback if the player HitDetection isn't available
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: expel key Delete -> KickOut(hd={(hd != null)}), state={target.customerState}");
            ScreenReader.Say("Cliente expulso", interrupt: true);
        }

        // Round 120: Backspace cleans the nearest floor stain from anywhere, IF the mop is the
        // selected hotbar item ("uma mancha limpada para cada backspace"). Uses the game's own
        // FloorDirt.DestroyFloorDirt() (fires OnFloorDirtDestroyed, so the goal still counts), and
        // the live CommonReferences.tavernFloorDirt list (cheap). KeyCode.Backspace is unused by the
        // game.
        private void HandleMopBackspace()
        {
            if (!Input.GetKeyDown(KeyCode.Backspace)) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;

            Item selected = null;
            try { selected = PlayerInventory.GetPlayer(1)?.actionBarInventory?.GetSelectedItem(); }
            catch (System.Exception) { }
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: mop backspace - selected item = \"{(selected != null ? selected.IABAKHPEOAF() : "null")}\" isMop={(selected is Mop)}");
            if (!(selected is Mop))
            {
                ScreenReader.Say("Selecione o esfregão no uso rápido primeiro", interrupt: true);
                return;
            }

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);

            // Nearest floor stain (CommonReferences.tavernFloorDirt).
            var cr = CommonReferences.GGFJGHHHEJC;
            var dirtList = cr != null ? cr.tavernFloorDirt : null;
            FloorDirt nearestDirt = null;
            float dirtDist = float.MaxValue;
            if (dirtList != null)
                foreach (var d in dirtList)
                {
                    if (d == null) continue;
                    float dist = Vector3.Distance(playerPos, d.transform.position);
                    if (dist < dirtDist) { dirtDist = dist; nearestDirt = d; }
                }

            // Round 124: also clear dirty dishes off seats with Backspace ("limpar mesa... igual as
            // manchas"). Each Seat has a dirtyDish + Seat.CleanDirtyDish(); use the cached seat list.
            // Round 126 fix: CleanDirtyDish() only DEACTIVATES the dish GameObject - it never nulls
            // Seat.dirtyDish. So "dirtyDish != null" stayed true forever (-> "mesa limpa" infinito +
            // false positives). A seat is actually dirty only when its dish is ACTIVE in the scene.
            Seat nearestDishSeat = null;
            float dishDist = float.MaxValue;
            if (_cachedSeats != null)
                foreach (var s in _cachedSeats)
                {
                    if (s == null || s.dirtyDish == null || !s.dirtyDish.gameObject.activeSelf) continue;
                    float dist = Vector3.Distance(playerPos, s.transform.position);
                    if (dist < dishDist) { dishDist = dist; nearestDishSeat = s; }
                }

            // Round 127: BIG tables hold their dirty dishes in Table.dish[] (NOT Seat.dirtyDish) -
            // that's why the big table was never cleaned. Scan tables too and clean a single dish by
            // replicating the game's own clear (SetActive(false) + RemoveFromSurface).
            Table nearestDishTable = null;
            DirtyDish nearestTableDish = null;
            float tableDishDist = float.MaxValue;
            if (_cachedTables != null)
                foreach (var t in _cachedTables)
                {
                    if (t == null || t.dish == null) continue;
                    foreach (var dd in t.dish)
                    {
                        if (dd == null || !dd.gameObject.activeSelf) continue;
                        float dist = Vector3.Distance(playerPos, dd.transform.position);
                        if (dist < tableDishDist) { tableDishDist = dist; nearestTableDish = dd; nearestDishTable = t; }
                    }
                }

            // Round 129: the actual "dirty table" the customers complain about is the table's DIRT
            // LEVEL (Table.JNHCCCBICDM: Messy/Dirty/VeryDirty from accumulated dirtiness), NOT dishes -
            // the round-128 log proved 0 dirty dishes while the big table was visibly dirty. Clean it
            // with SetDirtiness(0).
            Table nearestDirtyTable = null;
            float dirtyTableDist = float.MaxValue;
            if (_cachedTables != null)
                foreach (var t in _cachedTables)
                {
                    if (t == null || (int)t.JNHCCCBICDM < (int)TableDirtLevel.Messy) continue;
                    float dist = Vector3.Distance(playerPos, t.transform.position);
                    if (dist < dirtyTableDist) { dirtyTableDist = dist; nearestDirtyTable = t; }
                }

            if (nearestDirt == null && nearestDishSeat == null && nearestTableDish == null && nearestDirtyTable == null)
            {
                if (Main.DebugMode)
                {
                    int seatsWithDish = 0, tableDishes = 0, dirtyTables = 0;
                    if (_cachedSeats != null) foreach (var s in _cachedSeats) if (s != null && s.dirtyDish != null && s.dirtyDish.gameObject.activeSelf) seatsWithDish++;
                    if (_cachedTables != null) foreach (var t in _cachedTables) { if (t == null) continue; if (t.dish != null) foreach (var dd in t.dish) if (dd != null && dd.gameObject.activeSelf) tableDishes++; if ((int)t.JNHCCCBICDM >= (int)TableDirtLevel.Messy) dirtyTables++; }
                    DebugLogger.LogState($"WorldNav: backspace NADA - {(_cachedSeats?.Length ?? -1)} seats ({seatsWithDish} dirty), {(_cachedTables?.Length ?? -1)} tables ({tableDishes} dishes, {dirtyTables} dirty-level), dirtList={(dirtList?.Count ?? -1)}");
                }
                ScreenReader.Say("Nada pra limpar", interrupt: true);
                return;
            }

            // Clean whichever of the four is closest.
            float minDist = Mathf.Min(Mathf.Min(dirtDist, dishDist), Mathf.Min(tableDishDist, dirtyTableDist));
            if (nearestDirtyTable != null && dirtyTableDist <= minDist)
            {
                nearestDirtyTable.SetDirtiness(0f);
                if (Main.DebugMode) DebugLogger.LogState("WorldNav: mop backspace cleaned a table dirt level");
                ScreenReader.Say("Mesa limpa", interrupt: true);
                return;
            }
            if (nearestDishSeat != null && dishDist <= minDist)
            {
                nearestDishSeat.CleanDirtyDish();
                if (Main.DebugMode) DebugLogger.LogState("WorldNav: mop backspace cleaned a seat dish");
                ScreenReader.Say("Mesa limpa", interrupt: true);
                return;
            }
            if (nearestTableDish != null && tableDishDist <= minDist)
            {
                nearestTableDish.gameObject.SetActive(false);
                nearestDishTable.placeable?.placeableSurface?.RemoveFromSurface(nearestTableDish.transform);
                if (Main.DebugMode) DebugLogger.LogState("WorldNav: mop backspace cleaned a table dish");
                ScreenReader.Say("Mesa limpa", interrupt: true);
                return;
            }

            nearestDirt.DestroyFloorDirt(); // removes itself from tavernFloorDirt
            int remaining = 0;
            if (dirtList != null) foreach (var d in dirtList) if (d != null) remaining++;
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: mop backspace cleaned a stain, {remaining} remaining");
            ScreenReader.Say(remaining <= 0 ? "Mancha limpa, todas limpas" : $"Mancha limpa, faltam {remaining}", interrupt: true);
        }

        private void HandleRatAnnouncement()
        {
            var sceneRefs = SceneReferences.GetSceneReferences();
            var rats = sceneRefs != null ? sceneRefs.tutorialRats : null;
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            GameObject nearest = null;
            float nearestDist = float.MaxValue;
            int count = 0;
            if (rats != null)
            {
                foreach (var rat in rats)
                {
                    if (rat == null) continue;
                    count++;
                    float dist = Vector3.Distance(playerPos, rat.transform.position);
                    if (dist <= ItemSoundRadius && dist < nearestDist) { nearest = rat; nearestDist = dist; }
                }
            }

            // A rat was removed (count dropped).
            if (_lastRatCount >= 0 && count < _lastRatCount)
            {
                ScreenReader.Say(count == 0 ? "Todos os ratos removidos" : $"Rato removido, faltam {count}", interrupt: false);
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: rat removed, {count} remaining");
            }
            _lastRatCount = count;

            // Entering range of a (different) rat - proximity + how to deal with it.
            if (nearest != _lastNearRat)
            {
                _lastNearRat = nearest;
                if (nearest != null)
                {
                    _lastNearRatPos = nearest.transform.position;
                    _lastRatMoveAnnounceTime = Time.unscaledTime;
                    ScreenReader.Say("Rato perto. Use o esfregão pra removê-lo", interrupt: false);
                    if (Main.DebugMode) DebugLogger.LogState($"WorldNav: rat proximity at {nearest.transform.position} dist={nearestDist:F1}");
                }
                return;
            }
            if (nearest == null) return;

            // The nearest rat moved - tell the player which way (throttled, ~1 tile of movement).
            if (Time.unscaledTime - _lastRatMoveAnnounceTime >= RatMoveAnnounceInterval)
            {
                Vector3 cur = nearest.transform.position;
                Vector3 delta = cur - _lastNearRatPos;
                if (delta.magnitude >= TileSize)
                {
                    ScreenReader.Say($"Rato foi {DirectionWord(delta)}", interrupt: false);
                    _lastNearRatPos = cur;
                    _lastRatMoveAnnounceTime = Time.unscaledTime;
                }
            }
        }

        private Placeable _lastNearCandle;

        // Round 102: user asked to be warned when passing a spent candle and told the remaining
        // amount when passing a still-lit one. The candle is a Crafter (confirmed: Placeable.
        // CreateRotatedPrefab calls component.SetFuel; Crafter.LCCABPFHCOL exposes the live fuel),
        // and the game's own "spent" threshold is fuel <= 1 (Crafter.HIEAIMBBKFL line ~238). We
        // announce spent vs lit on approach; the exact percentage needs the candle's MAX fuel,
        // which isn't reliably readable from the obfuscated source yet, so the real value is logged
        // here to build the % next round instead of guessing it.
        private void HandleCandleAnnouncement()
        {
            GameObject heldObject = SelectObject.GetPlayer(1)?.selectedGameObject;
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            Placeable nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var candle in _cachedCandles)
            {
                if (candle == null || candle.gameObject == heldObject) continue;
                float dist = Vector3.Distance(playerPos, candle.transform.position);
                if (dist <= ItemSoundRadius && dist < nearestDist) { nearest = candle; nearestDist = dist; }
            }

            if (nearest == _lastNearCandle) return;
            _lastNearCandle = nearest;
            if (nearest == null) return;

            var crafter = nearest.GetComponent<Crafter>() ?? nearest.GetComponentInChildren<Crafter>();
            int fuel = crafter != null ? crafter.LCCABPFHCOL : -1;
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: candle proximity \"{nearest.gameObject.name}\" hasCrafter={crafter != null} fuel={fuel}");

            if (crafter != null && fuel <= 1)
                ScreenReader.Say("Vela apagada, precisa repor", interrupt: false);
            else
                ScreenReader.Say("Vela acesa", interrupt: false);
        }

        private void HandleSeatAnnouncement()
        {
            // User's explicit report: a bench kept being announced as "Próximo" even right
            // after picking it up - it's still a real GameObject in the scene while held (now
            // following the cursor instead of sitting still), so the scan kept finding it.
            // Excluding whatever's currently selected/held in decoration mode.
            GameObject heldObject = SelectObject.GetPlayer(1)?.selectedGameObject;

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            Seat nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var seat in _cachedSeats)
            {
                if (seat == null) continue;
                // Bug found by reading log evidence directly: this exclusion never actually
                // worked (the announcement kept repeating right after grabbing). Root cause -
                // confirmed in Seat.cs: Seat has its own "public Placeable placeable;" field,
                // meaning Seat and its Placeable are NOT the same GameObject (same root cause
                // already found for the missing-from-nav-list bug). Comparing seat.gameObject
                // directly against selectedGameObject (which IS the Placeable's GameObject)
                // could never match - comparing through seat.placeable instead.
                if (seat.placeable != null && seat.placeable.gameObject == heldObject) continue;
                float dist = Vector3.Distance(playerPos, seat.transform.position);
                if (dist <= ItemSoundRadius && dist < nearestDist)
                {
                    nearest = seat;
                    nearestDist = dist;
                }
            }

            if (nearest == _lastNearSeat) return;
            _lastNearSeat = nearest;
            if (nearest == null) return;

            // Tells apart a seat that's actually doing its job (linked to a table, set by
            // Seat.GetNeighbourTable on placement) from one just sitting somewhere with no
            // table nearby - canBePlaced alone (no overlap) doesn't guarantee this, confirmed
            // reading Seat.cs's own table-association logic.
            string status = nearest.table != null ? "associado a uma mesa" : "sem mesa associada";
            ScreenReader.Say($"Próximo: Banco {GetSeatNumber(nearest)} ({status})", interrupt: false);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Seat proximity announcement, table={(nearest.table != null ? nearest.table.gameObject.name : "nenhuma")}");
        }

        // User's explicit request: the empty seating-group slots themselves (see
        // GetEmptySeatSlots) weren't announced when walking near one, same gap the seat
        // announcement above used to have.
        private Vector3? _lastNearSeatSlot;

        // Round 74: the round-73 timer confirmed GetEmptySeatSlots itself (not just the scene
        // scan it used to do internally) costs ~25ms per call even in steady state - this
        // method was calling it EVERY FRAME, unthrottled, unlike every other heavy check in
        // this file. ~25ms/frame is over a full frame's budget at 60fps, paid every single
        // frame for an announcement that doesn't need per-frame precision. Same throttle
        // pattern as everywhere else (item sounds, seat scene cache).
        private float _lastSeatSlotCheckTime = -999f;
        private const float SeatSlotCheckInterval = 0.3f;

        private void HandleSeatSlotAnnouncement()
        {
            if (Time.unscaledTime - _lastSeatSlotCheckTime < SeatSlotCheckInterval) return;
            _lastSeatSlotCheckTime = Time.unscaledTime;

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            var getSlotsSw = Main.DebugMode ? System.Diagnostics.Stopwatch.StartNew() : null;
            var slots = GetEmptySeatSlots(playerPos, ItemSoundRadius, _cachedTables, _cachedSeats);
            if (getSlotsSw != null && getSlotsSw.ElapsedMilliseconds > 3) DebugLogger.LogState($"WorldNav: PERF GetEmptySeatSlots took {getSlotsSw.ElapsedMilliseconds}ms ({slots.Count} slots)");

            Vector3? nearest = null;
            string nearestLabel = null;
            int nearestNumber = 0;
            float nearestDist = float.MaxValue;
            foreach (var slot in slots)
            {
                float dist = Vector3.Distance(playerPos, slot.pos);
                if (dist < nearestDist) { nearest = slot.pos; nearestLabel = slot.tableLabel; nearestNumber = slot.slotNumber; nearestDist = dist; }
            }

            bool same = nearest.HasValue && _lastNearSeatSlot.HasValue && Vector3.Distance(nearest.Value, _lastNearSeatSlot.Value) < 0.01f;
            if (same || (!nearest.HasValue && !_lastNearSeatSlot.HasValue)) return;
            _lastNearSeatSlot = nearest;
            if (!nearest.HasValue) return;

            // User's explicit request: identify WHICH slot and WHICH table, not just "a slot
            // exists somewhere nearby".
            ScreenReader.Say($"Próximo: Lugar pra banco {nearestNumber} junto da {nearestLabel}", interrupt: false);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Empty seat slot proximity announcement at {nearest.Value}");
        }

        private void HandleZoneAnnouncement()
        {
            var player = PlayerController.GetPlayer(1);
            if (player == null) return;

            Location current = player.LEOIMFNKFGA;
            if (_lastLocation == current) return;

            // User reported never hearing this - logged here UNCONDITIONALLY (even for
            // Location.None, before the skip below) so the next test confirms whether
            // LEOIMFNKFGA genuinely never left None (e.g. only set by crossing specific
            // zone-transition triggers, not just by being inside an area) instead of
            // guessing why it stayed silent.
            if (Main.DebugMode)
            {
                DebugLogger.LogState($"WorldNav: Location raw value changed {_lastLocation} -> {current}");
            }
            _lastLocation = current;

            if (current == Location.None) return;

            string name = LocationNames.TryGetValue(current, out var known) ? known : current.ToString();
            ScreenReader.Say($"Área: {name}", interrupt: false);
            DebugLogger.LogState($"WorldNav: Location changed to {current} ({name})");
        }

        private ZoneType _lastZoneType = (ZoneType)(-1);
        private float _lastZoneTypeCheckTime;

        // Round 112: user asked to be told when entering the kitchen / bedroom / dining room /
        // cellar / corridor. Those are room-level ZoneTypes (WorldGrid.AGKGGAFFFGM at the player's
        // position), distinct from the building-level Location handled above. Announced on change.
        private void HandleZoneTypeAnnouncement()
        {
            if (Time.unscaledTime - _lastZoneTypeCheckTime < 0.3f) return;
            _lastZoneTypeCheckTime = Time.unscaledTime;
            ZoneType zone = WorldGrid.AGKGGAFFFGM(PlayerController.GetPlayerPosition(1));
            if (zone == _lastZoneType) return;
            _lastZoneType = zone;
            string name = ZoneTypeName(zone);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: zone changed to {zone} ({name ?? "(silent)"})");
            if (name != null) ScreenReader.Say(name, interrupt: false);
        }

        private static string ZoneTypeName(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.DiningRoom: return "Sala de jantar";
                case ZoneType.CraftingRoom: return "Cozinha";
                case ZoneType.Cellar: return "Adega";
                case ZoneType.RentedRoom:
                case ZoneType.RoomPlayer2:
                case ZoneType.RoomPlayer3:
                case ZoneType.RoomPlayer4: return "Quarto";
                case ZoneType.WoodWorkshop: return "Oficina de madeira";
                case ZoneType.MetalWorkshop: return "Oficina de metal";
                case ZoneType.StoneWorkshop: return "Oficina de pedra";
                case ZoneType.WithoutZone: return "Corredor";
                default: return null; // None / unmapped - stay silent
            }
        }

        private void HandleSimulatedClick()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool enterDown = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (!enterDown || !(ctrl || shift)) return;

            // Confirmed live (Fireplace): some objects (IInteractable + IProximity) never go
            // through InteractObject.SetCurrentInteract/GetCurrentInteractGO() at all - the
            // on-screen "[Q] ..." prompt comes from IProximity.IsAvailableByProximity, and the
            // real mouse-click action calls IInteractable.MouseUp directly. GetCurrentInteractGO()
            // came back null for these every time, which is why the old Ctrl+Enter (UI click
            // only) silently did nothing for them. Try this path first for Ctrl+Enter (left
            // click) - it's a direct equivalent of what a real click does, not a simulation
            // through Unity's UI event system, so it works for these world objects.
            if (ctrl && TryInteractableMouseUp())
            {
                return;
            }

            var target = InteractObject.BBJCJFJEFKK(1)?.GetCurrentInteractGO();
            if (target == null)
            {
                if (Main.DebugMode) DebugLogger.LogState("Ctrl/Shift+Enter pressed but no current interact target");
                return;
            }

            var button = ctrl ? PointerEventData.InputButton.Left : PointerEventData.InputButton.Right;
            var eventData = new PointerEventData(EventSystem.current) { button = button };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerClickHandler);

            string keyName = ctrl ? "Ctrl+Enter" : "Shift+Enter";
            string clickName = ctrl ? "left" : "right";
            DebugLogger.LogInput(keyName, $"Simulated {clickName} click on \"{target.name}\"");
        }

        private static bool TryInteractableMouseUp()
        {
            var closest = FindClosestAvailableByProximity();
            if (closest == null) return false;

            bool handled = ((IInteractable)closest).MouseUp(1);
            DebugLogger.LogInput("Ctrl+Enter", $"IInteractable.MouseUp on \"{closest.gameObject.name}\" -> {handled}");
            return handled;
        }

        private static MonoBehaviour FindClosestAvailableByProximity()
        {
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            MonoBehaviour closest = null;
            float closestDist = float.MaxValue;

            foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (!(behaviour is IInteractable) || !(behaviour is IProximity proximity)) continue;

                bool available;
                try
                {
                    available = proximity.IsAvailableByProximity(1);
                }
                catch (System.Exception)
                {
                    // Confirmed live: Harvestable.IsAvailableByProximity throws a
                    // NullReferenceException for at least some instances (likely
                    // uninitialized/not-yet-grown ones), every single frame, regardless of
                    // distance to the player. That killed THIS WHOLE method silently for
                    // every caller (GetNearestInteractionName included), which is why action
                    // prompt announcements went completely silent - confirmed via the
                    // exception's stack trace landing here, repeated nonstop in the log.
                    continue;
                }
                if (!available) continue;

                float distance = Vector3.Distance(playerPos, behaviour.transform.position);
                if (distance < closestDist)
                {
                    closestDist = distance;
                    closest = behaviour;
                }
            }

            return closest;
        }

        // Round 119: the closest AVAILABLE NPC (customer / cat), if any. NPCs take priority for the
        // interaction NAME - the game can focus a nearby station (e.g. the beer tap "Grifo") while
        // the prompt actually shown is the cat's "Conversar", so the name read as the tap. Confirmed
        // in the log: near the cat, the interaction target resolved to "663 - Grifo".
        private static GameObject FindClosestAvailableNpc()
        {
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            GameObject closest = null;
            float closestDist = float.MaxValue;
            foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
            {
                // Round 131: ONLY the dialogue NPCs (cat, Mai) - they are the case where the game
                // focuses a nearby station instead of the NPC. Customers must NOT be included here:
                // doing so made a station's "Abrir" prompt get named after a nearby customer
                // ("Cliente, quer Espetinho: Abrir"). Customers already resolve correctly via the
                // focused element.
                if (!(behaviour is CatNPC) && !(behaviour is MaiNPC)) continue;
                if (!(behaviour is IProximity proximity)) continue;
                bool available;
                try { available = proximity.IsAvailableByProximity(1); }
                catch (System.Exception) { continue; }
                if (!available) continue;
                float distance = Vector3.Distance(playerPos, behaviour.transform.position);
                if (distance < closestDist) { closestDist = distance; closest = behaviour.gameObject; }
            }
            return closest;
        }

        /// <summary>
        /// Best-effort name for whatever the player is currently positioned to interact
        /// with - used by DialogueAnnouncer to prefix the spoken action prompt (e.g. "Porta:
        /// Abrir") so the player knows WHAT it is, not just the verb. Only called once per
        /// new prompt (not every frame) since the IProximity fallback scans every
        /// MonoBehaviour in the scene. Approximate when two different nearby objects each
        /// show their own prompt at once - this picks whichever object is geometrically
        /// closest overall, which may not always match the specific prompt being announced.
        /// </summary>
        public static string GetNearestInteractionName() => GetNearestInteractionTarget()?.name;

        // Round 118: a customer or the cat. A customer reads as "Cliente, quer {item}" (or food/
        // drink if the specific item isn't resolvable) so the player knows what to serve;
        // CustomerBase.currentRequest is the ordered ItemInstance, Customer.preference is Food/Drink.
        private static string DescribeNpc(GameObject go)
        {
            var customer = go.GetComponent<Customer>() ?? go.GetComponentInParent<Customer>();
            if (customer != null)
            {
                // Round 127: only say "quer {item}" when the customer is actually waiting to be
                // served (OrderInTable/WaitingAtBar) - the user pressed Z near customers the proximity
                // called "quer comida" but who were already EatingAtTable (served), so Z found nothing
                // and it felt inconsistent. Reflect the real state instead.
                bool serveable = customer.customerState == CustomerState.OrderInTable
                    || customer.customerState == CustomerState.WaitingAtBar;
                if (serveable && customer.currentRequest != null)
                {
                    var reqItem = customer.currentRequest.LHBPOPOIFLE();
                    string itemName = reqItem != null ? reqItem.IABAKHPEOAF() : null;
                    if (!string.IsNullOrEmpty(itemName)) return $"Cliente, quer {itemName}";
                    return customer.preference == CustomerPreference.Drink ? "Cliente, quer bebida" : "Cliente, quer comida";
                }
                if (customer.customerState == CustomerState.EatingAtTable) return "Cliente comendo";
                return "Cliente";
            }
            if (go.GetComponent<CatNPC>() != null || go.GetComponentInParent<CatNPC>() != null) return "Gato";
            // Round 120: the "gata" the user reported is actually Mai (MaiNPC, a DialogueNPCBase) -
            // the log showed go="MaiNPC" falling through to the nearby bar's name. Name her "Mai".
            if (go.GetComponent<MaiNPC>() != null || go.GetComponentInParent<MaiNPC>() != null) return "Mai";
            return null;
        }

        /// <summary>
        /// Same resolution as GetNearestInteractionName, but also returns the world
        /// position (needed to work out the item's direction relative to the player for the
        /// directional item-proximity sound) and uses the same clean display name as the
        /// navigation list (DescribePlaceable) when the target is a Placeable, instead of
        /// the raw GameObject name.
        /// </summary>
        public static (string name, Vector3 position)? GetNearestInteractionTarget()
        {
            // Round 117: prefer the object the prompt is ACTUALLY for (the focused proximity input
            // element's mainGameObject) over the geometrically-closest IProximity. The user hit
            // exactly this bug: near the cat (a "Conversar" prompt) it named the closest object - a
            // drinks dispenser - so the cat read as "Dispensador de bebidas", and the menu table did
            // the same. The focused element is the real prompt target.
            GameObject go = InputByProximityManager.GetPlayer(1)?.GetCurrentFocusedInputElement()?.mainGameObject;
            if (go == null) go = InteractObject.BBJCJFJEFKK(1)?.GetCurrentInteractGO();
            if (go == null)
            {
                var behaviour = FindClosestAvailableByProximity();
                if (behaviour == null) return null;
                go = behaviour.gameObject;
            }

            // Round 118: NPCs (customers, the cat) read as their raw GameObject name
            // ("HumanMaiCustomer (2)") - name them properly, and for a customer say WHAT they want
            // (the order item + food/drink) so the player knows what to serve.
            string npcName = DescribeNpc(go);
            // Round 119: NPCs win the NAME over a nearby station the game happened to focus (the cat
            // near the tap). If the focused object isn't an NPC but an available one is nearby, use it.
            if (npcName == null)
            {
                var npcGo = FindClosestAvailableNpc();
                if (npcGo != null) { go = npcGo; npcName = DescribeNpc(go); }
            }
            var placeable = npcName == null ? (go.GetComponent<Placeable>() ?? go.GetComponentInParent<Placeable>()) : null;
            string name = npcName ?? (placeable != null ? DescribePlaceable(placeable) : go.name);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: interaction target go=\"{go.name}\" npc={(npcName != null)} placeable=\"{(placeable != null ? placeable.gameObject.name : "null")}\" -> name=\"{name}\"");
            return (name, go.transform.position);
        }

        // User's explicit request: pitch/pan encoding for the item-proximity sound -
        // vertical-dominant direction (in front/cima vs behind/baixo) maps to pitch (higher/
        // lower), horizontal-dominant direction (esquerda/direita) keeps normal pitch and
        // pans instead. Same "pick the dominant axis" convention already used for the
        // step-guidance fallback message.
        public static (string name, float pitch, float pan)? GetNearestInteractionAudioInfo()
        {
            var target = GetNearestInteractionTarget();
            if (target == null) return null;

            Vector3 delta = target.Value.position - PlayerController.GetPlayerPosition(1);
            float pitch = 1f;
            float pan = 0f;
            if (Mathf.Abs(delta.y) >= Mathf.Abs(delta.x))
            {
                pitch = delta.y > 0 ? 1.3f : 0.75f;
            }
            else
            {
                pan = delta.x > 0 ? 1f : -1f;
            }
            return (target.Value.name, pitch, pan);
        }
    }
}
