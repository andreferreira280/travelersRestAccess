using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly string[] CategoryOrder = { "Portas", "Missão", "Containers", "Máquinas", "Coletáveis", "Decorativos" };

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
        private const float WallStuckSeconds = 0.6f;
        private Vector3? _lastWallCheckPosition;
        private float _wallStuckTime;

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
                HandleZoneAnnouncement();
                HandleHomeKey();
                HandleGuidanceUpdate();
                TrackGuidanceTaps();
                HandleFootsteps();
                HandleWallBump();
                HandleDirectionChangeSound();
                HandleDirectionalWallSound();
                HandleItemProximitySounds();
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

            foreach (var door in Object.FindObjectsOfType<Door>())
            {
                float distance = Vector3.Distance(playerPos, door.transform.position);
                DebugLogger.LogState($"WorldNav: Door \"{door.gameObject.name}\" pos={door.transform.position} dist={distance:F1}");
            }
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

            if (_wallStuckTime < WallStuckSeconds)
            {
                CustomSounds.StopWallBumpLoop();
                CustomSounds.StopItemBumpLoop();
                return;
            }

            // User's explicit request: a different sound for getting stuck against
            // something that isn't a wall (closed door, furniture) - reuses the same
            // "(Clone)" signal already confirmed for the directional wall sound (static
            // level geometry isn't runtime-instantiated, furniture/doors are).
            bool stuckOnItem = IsBlockedByNonWallItem(pos, GetHeldMovementDirection());
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Sustained bump classified as {(stuckOnItem ? "item" : "wall")}");
            if (stuckOnItem)
            {
                CustomSounds.StopWallBumpLoop();
                CustomSounds.StartItemBumpLoop();
            }
            else
            {
                CustomSounds.StopItemBumpLoop();
                CustomSounds.StartWallBumpLoop();
            }
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

        private static bool IsBlockedByNonWallItem(Vector2 pos, Vector2 direction)
        {
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
                return closest.Value.collider.transform.root.name.Contains("(Clone)");
            }
            return doorDist.HasValue;
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
        private const float WallSoundOffDelay = 0.15f;
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
                RaycastHit2D[] hits = Physics2D.RaycastAll(pos, offset, maxDistance);
                RaycastHit2D? closest = null;
                foreach (var h in hits)
                {
                    if (h.collider.isTrigger) continue;
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
            foreach (var placeable in Object.FindObjectsOfType<Placeable>())
            {
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
                    bool isItem = IsBlockedByNonWallItem(player.transform.position, _pendingTapDirection);
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

            if (Bed.IsValid())
            {
                list.Add(("Cama", Bed.GetPlayerBedPosition(), "Decorativos"));
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

            // User's explicit request: floor stains from the cleaning tutorial goal
            // ("Limpe as manchas do chão") weren't in the list at all - confirmed in
            // decompiled source they're a separate component (FloorDirt: MonoBehaviour,
            // IHoverable, IProximity), not a Placeable, so the loop above never saw them.
            // Tagged "Missão" per request - everything tied to the active goal goes there;
            // for now this covers floor stains specifically (the one confirmed live), not a
            // generic goal-to-object mapping for every future quest.
            foreach (var dirt in Object.FindObjectsOfType<FloorDirt>())
            {
                if (Vector3.Distance(playerPos, dirt.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Mancha no chão", dirt.transform.position, "Missão"));
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

        // Classified by real component types confirmed in decompiled source (Container.cs,
        // Crafter.cs, Placeable.canBeAddedToInventory) - not guessed from names.
        private static string CategorizePlaceable(Placeable placeable)
        {
            if (placeable.GetComponent<Container>() != null) return "Containers";
            if (placeable.GetComponent<Crafter>() != null) return "Máquinas";

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
                return "Missão";
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

        private static string DescribePlaceable(Placeable placeable)
        {
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

        /// <summary>
        /// Same resolution as GetNearestInteractionName, but also returns the world
        /// position (needed to work out the item's direction relative to the player for the
        /// directional item-proximity sound) and uses the same clean display name as the
        /// navigation list (DescribePlaceable) when the target is a Placeable, instead of
        /// the raw GameObject name.
        /// </summary>
        public static (string name, Vector3 position)? GetNearestInteractionTarget()
        {
            GameObject go = InteractObject.BBJCJFJEFKK(1)?.GetCurrentInteractGO();
            if (go == null)
            {
                var behaviour = FindClosestAvailableByProximity();
                if (behaviour == null) return null;
                go = behaviour.gameObject;
            }

            var placeable = go.GetComponent<Placeable>() ?? go.GetComponentInParent<Placeable>();
            string name = placeable != null ? DescribePlaceable(placeable) : go.name;
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
