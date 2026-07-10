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
        private static readonly string[] CategoryOrder = { "Servir", "Portas", "Comerciantes", "NPCs", "Animais", "Pendentes", "Repositivos", "Containers", "Máquinas", "Cultivo", "Materiais", "Coletáveis", "Decorativos" };

        // The town/region merchants and what each sells (from the wiki, provided by the user). Used to
        // put them in their own "Comerciantes" category (out of "NPCs") with a description, and to let
        // the player locate whoever is in the current scene (city ones in the city, Holly/Bob outside).
        private static readonly System.Collections.Generic.Dictionary<string, string> MerchantWares =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Amos", "taverna: equipamentos de cozinha e bebida, móveis, fermento, ingredientes e projetos" },
            { "Persa", "loja de animais: gatos e animais de estimação" },
            { "Woody", "carpinteiro: madeira, móveis de madeira, baús, máquinas de carpintaria e projetos" },
            { "Petra", "ferreiro interno: projetos, bancadas e máquinas de metal e equipamentos de ferraria" },
            { "Hallmund", "ferreiro externo: ferramentas, melhorias de ferramentas e equipamentos de mineração" },
            { "Chuck", "açougueiro: carne de boi, porco, frango e miúdos" },
            { "Kujaku", "peixaria: peixes, frutos do mar e ingredientes de pesca" },
            { "Lia", "vegetais, frutas e produtos agrícolas colhidos" },
            { "Rhia", "sementes sazonais e especiais" },
            { "Agatha", "decoração: móveis, iluminação, tapetes, quadros, janelas e conforto" },
            { "Holly", "fazenda: animais, ração, leite e ovos. Fecha sábado e domingo" },
            { "Bob", "golem: lenha, mudas de árvores, ovos e recursos naturais. Reabastece terça e sexta" },
        };

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
        // Merchants use a wider radius so the whole current area's merchants are findable, without
        // pulling in cross-map ones (which had broken routes). Scale is large: cross-area gaps are
        // 500-680 units (door distances in the log), intra-area spread is far smaller, so 300 covers
        // a full local area (all city merchants together, or Holly/Bob at the farm) while excluding
        // the next area over. Fixes "tem alguns da cidade q aparecem fora... rotas completamente
        // loucas".
        private const float MerchantRadius = 300f;
        // In the city the user wants EVERY door/passage listed from anywhere in the city, not just
        // the nearby ones ("quando eu entrar na cidade, quero todas as passagens e portas sempre
        // disponiveis em qualquer parte da cidade"). Intra-area spread is < 300 and the next area
        // over is 500-1000+ units away, so a 600-unit radius covers the whole city and its inner
        // buildings' entrances (Ferraria/Serraria) without pulling in other maps.
        private const float CityWideDoorRadius = 600f;

        // Only the OPEN, walkable city itself gets the city-wide door radius. The city's inner
        // buildings (Blacksmith/Sawmill/PetShop/Bathhouse/CityTavern) are separate interior rooms:
        // from inside them the 600-unit radius reached far, unrelated TravelZones (camp/beach/pirate
        // cave) that don't even show in the open city (user: "no ferreiro aparecem varias saidas pra
        // outros lugares... essas saidas nem dentro da cidade foram mostradas"). Inside an interior we
        // fall back to the normal nearby radius, so only that room's own exit shows.
        private static bool IsCityLocation(Location loc)
        {
            return loc == Location.City || loc == Location.CityOutside;
        }

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
        // User (round 236): after reaching a tracked resource (ore, etc.), the NEXT target must not
        // be the same one - and a rock you just broke shouldn't linger in the guide. Records where
        // you last arrived; the nav list hides any target on that spot until you walk away from it.
        private Vector3? _recentlyReachedPos;
        private const float ReachedSkipRadius = 0.75f;
        private const float ReachedClearDist = 1.5f;
        private bool _isRetryAttempt;

        // Multi-area routing: when the final target is in a different Location, we first
        // route to the TravelZone exit, then re-route to the real target once the player
        // crosses into the new area. _finalTarget stores the real destination during that
        // intermediate leg; _lastGuidancePlayerLocation tracks location changes mid-route.
        private (string name, Vector3 position)? _finalTarget;
        private Location _lastGuidancePlayerLocation;

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
        // Round 179: user wants the "Bloqueado por X" callout near-INSTANT ("demora muito, quero
        // algo mais instantâneo"). Dropped from 0.2s to 0.1s - just above the 0.08s sound threshold
        // so it still skips the very quickest brushes but speaks almost immediately.
        private const float BlockerAnnounceSeconds = 0.1f;
        private Vector3? _lastWallCheckPosition;
        private float _wallStuckTime;

        // User's explicit request (rodada 134k): if the player bumps a wall ~6 times in a row
        // while being guided, force an immediate re-route to get them unstuck efficiently.
        private int _consecutiveBumps;
        private float _lastBumpTime;
        private const int BumpsBeforeReroute = 6;
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

            // Applies/lifts the mute on its transition (see CustomSounds) - must run every frame,
            // even with a UI open, so sounds reliably resume after dialogue/menu. UiOpen extends the
            // mute to ALL menus/stations (project rule: our world sounds are silenced in any UI).
            CustomSounds.UiOpen = anyUiOpen;
            CustomSounds.UpdateConversationMute();

            HandleTutorialHelpKey();
            HandleTavernOpenClose();
            HandleQuickSave();
            HandleInfoKey();
            HandleReputationKey();
            HandleMoneyGainAnnouncement();
            EnsureObjectiveHook();

            // While ANY area is still building its terrain (initial load OR an area transition),
            // pause the mod's heavy per-frame world scanning. The destination area's terrain
            // coroutine must complete for the game to release the input/movement blockers
            // (TitleScreen.allTerrainUpdated at load; TilemapScene.updatingTerrain for the
            // TravelZone fade-in). This is a safe no-op for gameplay (input is disabled during
            // that window anyway) and stops us hammering the world grid / running dozens of
            // FindObjects sweeps against a half-built area. Doubles as the bisect for whether the
            // per-frame scan load is what stalls the terrain build.
            if (!anyUiOpen && !AnyTerrainUpdating())
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
                HandleNearbyResourceAnnouncement();
                HandleArableZoneAnnouncement();
                HandleToolAimAnnouncement();
                HandleWellProximitySound();
                HandlePostBoxProximitySound();
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

            // Runs regardless of anyUiOpen: the Post Box is a UI, so its letter content must
            // be read while that UI is open (user: "abri uma carta e só leu Voltar").
            HandlePostboxAnnouncement();

            // Event-message overlay (EventTextUI): the game's generic pop-up for quest/lore/event
            // text - e.g. the cave "Ler" message ("InkeepersCave_Message") and other event messages
            // that were never read (user: "apareceu algum dialogo q não leu"; "eventos de música têm
            // mensagens não lidas"). Read it whenever a new message shows. Runs regardless of anyUiOpen.
            HandleEventTextAnnouncement();

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

            // Round 74: confirmed via the round-73 timers that FindAll<Door>()
            // alone costs ~100-113ms in this scene (same root cause as RefreshSeatSceneCache
            // above - the call's cost scales with total scene object count, not the 6 doors it
            // actually returns). This whole block is debug-only diagnostic printing, doors never
            // change mid-session, so there's no reason to pay that cost every second - cached
            // with the same long interval as the seat/table cache instead.
            if (Time.unscaledTime - _lastDoorCacheTime > SeatSceneCacheInterval || _cachedDoors == null)
            {
                _lastDoorCacheTime = Time.unscaledTime;
                var doorScanSw = System.Diagnostics.Stopwatch.StartNew();
                _cachedDoors = FindAll<Door>();
                if (doorScanSw.ElapsedMilliseconds > 3) DebugLogger.LogState($"WorldNav: PERF Door FindObjectsOfType took {doorScanSw.ElapsedMilliseconds}ms ({_cachedDoors.Length} doors)");
            }
            foreach (var door in _cachedDoors)
            {
                // Null-check: confirmed in the city log (NRE every second) that a cached door
                // can be destroyed during an area transition before the cache refreshes,
                // leaving a Unity fake-null whose .transform throws.
                if (door == null) continue;
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

            string s = ObjectiveSummary();
            ScreenReader.Say(string.IsNullOrEmpty(s) ? "Nenhum objetivo ativo agora" : $"Objetivo: {s}", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogInput("Tab", $"Objective readout: {s}");
        }

        // Objective text + per-objective DONE/pending status. The game's objectives array
        // (NewTutorialManager.objectives = TextImageUI[]) shows the description + count ("0/5"); the
        // completion state is the private List<bool> completedObjectives (read via reflection). So a
        // multi-part goal reads e.g. "Derrube 5 árvores 2/5. Minere 5 carvão (feito)".
        private static System.Reflection.FieldInfo _completedObjField;
        private string ObjectiveSummary()
        {
            var texts = new List<string>();

            // Tutorial objectives (NewTutorialManager) - the early-game "faça X" steps.
            var tm = NewTutorialManager.instance;
            if (tm != null && tm.objectives != null)
            {
                List<bool> completed = null;
                try
                {
                    if (_completedObjField == null)
                        _completedObjField = typeof(NewTutorialManager).GetField("completedObjectives",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    completed = _completedObjField?.GetValue(tm) as List<bool>;
                }
                catch { }
                for (int i = 0; i < tm.objectives.Length; i++)
                {
                    var obj = tm.objectives[i];
                    if (obj == null || obj.gameObject == null || !obj.gameObject.activeInHierarchy || obj.textMesh == null) continue;
                    string t = UITextExtractor.GetReadableText(obj.textMesh);
                    if (string.IsNullOrEmpty(t)) continue;
                    bool done = completed != null && i < completed.Count && completed[i];
                    string entry = done ? $"{t.Trim()} (feito)" : t.Trim();
                    if (!texts.Contains(entry)) texts.Add(entry);
                }
            }

            // Round 238: also read the MISSION/quest objective panel (MissionsManager.objectivesPanel)
            // - the on-screen tracker for a selected notice-board mission. The Tab readout used to
            // only see tutorial objectives, so once the tutorial was done it said "nenhum objetivo"
            // even with a mission showing on screen (user report). Reading the live panel text matches
            // exactly what's displayed, including which mission is currently focused.
            try
            {
                var mm = MissionsManager.instance;
                if (mm != null && mm.objectivesPanel != null && mm.objectivesPanel.activeInHierarchy)
                {
                    foreach (var tmp in mm.objectivesPanel.GetComponentsInChildren<TMPro.TMP_Text>(false))
                    {
                        if (tmp == null) continue;
                        string s = UITextExtractor.GetReadableText(tmp);
                        if (string.IsNullOrEmpty(s)) s = tmp.text;
                        s = s?.Trim();
                        if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                        if (!texts.Contains(s)) texts.Add(s);
                    }
                }
            }
            catch { }

            return texts.Count == 0 ? null : string.Join(". ", texts);
        }

        // Announce objective progress automatically when it changes (user: "se pediu 5 e eu fizer um,
        // dizer restam 4"). NewTutorialManager.ObjectivesUpdated fires on every change; announce the
        // new summary, deduped so it doesn't repeat the same text.
        private bool _objHooked;
        private string _lastObjSummary;
        private void EnsureObjectiveHook()
        {
            if (_objHooked) return;
            var tm = NewTutorialManager.instance;
            if (tm == null) return;
            tm.ObjectivesUpdated += () => AnnounceObjectiveIfChanged();
            _objHooked = true;
        }

        // Announce the objective only if its text changed (progress advanced). Called both from the
        // game's ObjectivesUpdated event AND from OnActionDone (the event alone didn't fire reliably -
        // user: "não diz conforme vou completando").
        private void AnnounceObjectiveIfChanged()
        {
            try
            {
                string s = ObjectiveSummary();
                if (!string.IsNullOrEmpty(s) && s != _lastObjSummary)
                {
                    _lastObjSummary = s;
                    ScreenReader.Say($"Objetivo: {s}", interrupt: false);
                }
            }
            catch { }
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
                _finalTarget = null;
                _fineMode = false;
                _lastGuidancePlayerLocation = PlayerController.GetPlayer(1)?.LEOIMFNKFGA ?? Location.None;
                _isInitialPathRequest = true;
                ScreenReader.Say("Calculando rota...", interrupt: true);
                RequestPathToTarget(PlayerController.GetPlayerPosition(1), _selectedTarget.Value.position);
            }
            else
            {
                _selectedTarget = null;
                _finalTarget = null;
                _fineMode = false;
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
            if (_isInitialPathRequest) return;

            Vector3 pos = PlayerController.GetPlayerPosition(1);

            // Multi-area routing: detect when the player crosses into a new area.
            // When that happens, discard the intermediate TravelZone target and re-route
            // to the real final destination from the new position.
            Location currentLoc = PlayerController.GetPlayer(1)?.LEOIMFNKFGA ?? Location.None;
            if (_finalTarget.HasValue && currentLoc != Location.None && currentLoc != _lastGuidancePlayerLocation)
            {
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Player crossed into {currentLoc} — re-routing to final target \"{_finalTarget.Value.name}\"");
                _lastGuidancePlayerLocation = currentLoc;
                _selectedTarget = _finalTarget;
                _finalTarget = null;
                _fineMode = false;
                _lastGuidancePosition = null;
                _currentPath = null;
                _simplifiedSteps = null;
                _currentStepIndex = 0;
                _isInitialPathRequest = true;
                _pathRequestPending = false;
                RequestPathToTarget(pos, _selectedTarget.Value.position);
                return;
            }

            // No further destination chained: if we just crossed into a NEW area and the
            // target is in a DIFFERENT area than where we now are (i.e. it's the passage we
            // just walked through, now behind us in the old area), we've arrived. End the
            // guide instead of computing a nonsensical delta across two coordinate spaces -
            // confirmed bug: the instant the player entered the tavern it said "1833 pra
            // baixo" (player at tavern y=903, target at road y=-12).
            if (currentLoc != Location.None && currentLoc != _lastGuidancePlayerLocation)
            {
                Location targetLoc = Utils.HJPCBBGHPDA(_selectedTarget.Value.position);
                if (targetLoc != Location.None && targetLoc != currentLoc)
                {
                    if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Crossed into {currentLoc}, target \"{_selectedTarget.Value.name}\" is in {targetLoc} (behind us) — arrived, ending guide");
                    _lastGuidancePlayerLocation = currentLoc;
                    ScreenReader.Say("Você chegou", interrupt: true);
                    _guidanceActive = false;
                    _selectedTarget = null;
                    _finalTarget = null;
                _fineMode = false;
                    _currentPath = null;
                    _simplifiedSteps = null;
                    return;
                }
            }
            _lastGuidancePlayerLocation = currentLoc;

            if (_lastGuidancePosition.HasValue && Vector3.Distance(_lastGuidancePosition.Value, pos) < TileSize) return;
            _lastGuidancePosition = pos;

            bool offTrack = IsOffTrack(pos);
            if (offTrack || Time.unscaledTime - _lastPathRequestTime > PathRequestCooldown)
            {
                RequestPathToTarget(pos, _selectedTarget.Value.position);
            }

            AnnounceDirectionToSelectedTarget();
        }

        // Off-track = the player has strayed far from the ACTUAL path (nearest waypoint), so a
        // recompute is warranted. Measured against the raw waypoints (not a collapsed straight line),
        // so simply walking a curve/go-around no longer counts as "off track" - that false positive
        // was what forced a recompute every tile and made the route oscillate.
        private bool IsOffTrack(Vector3 pos)
        {
            if (_currentPath == null || _currentPath.Length == 0) return false;
            float best = float.MaxValue;
            for (int i = 0; i < _currentPath.Length; i++)
            {
                float d = Vector2.Distance(pos, _currentPath[i]);
                if (d < best) best = d;
            }
            return best > TileSize * 4f;   // ~2 units off the path
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
                // Snap the START to the nearest free PathNode. When the player stands ON a passage
                // trigger / door cell (which the pathfinder marks non-free even though you can stand
                // there), A* can't even begin and returns "no route" - confirmed in the city log:
                // standing right on "TravelZone-CityToPetShop" (goal 0.5 units away, itself a valid
                // free node) still failed every frame ("Sem rota, tente ir pra direita"). Snapping to
                // a walkable neighbour is a no-op when `from` is already free (the normal case).
                Vector3 snappedFrom = SnapToWalkableTile(from, from, null, SafeLoc(from));
                _lastPathRequestStart = Utils.MJEACANINDN(snappedFrom);
                var info = new PathRequestInfo
                {
                    startPos = _lastPathRequestStart,
                    goalPos = Utils.MJEACANINDN(to),
                    pathEnd = to,
                    canWalkDiagonal = true,
                    avoidWalls = true,
                    avoidObjects = true,
                    // The A* grid is 0.25 units, so even a ~14-unit in-area route (quarry ->
                    // bathhouse passage) fans out over thousands of cells around obstacles - 3500 was
                    // exhausted before reaching the goal, so those passages ALWAYS returned "no route"
                    // and the fallback walked the player into walls (user: "da mina para as fontes só
                    // me colocou em paredes"). RequestPath runs on a BACKGROUND THREAD (confirmed:
                    // pathRequestQueue + worker thread), so a bigger budget only delays the callback,
                    // it never freezes the game. A* STOPS as soon as it reaches the goal, so a bigger
                    // cap does NOT slow down easy routes (they succeed early) - it only lets harder
                    // routes explore enough to succeed. Confirmed live: the quarry->mine passage (~27
                    // units, cluttered) still exhausted 25000 from some player positions and failed,
                    // even though the route exists (it succeeds from other spots). The mine entrance
                    // sits past a narrow gap, so from a position where the straight-line heuristic
                    // points at a wall, A* explores a huge area before finding the gap. 60000 covers
                    // those hard positions. RequestPath is on a background thread, and A* stops the
                    // instant it reaches the goal, so this only costs more on genuinely hard/failed
                    // searches - never a freeze, and easy routes stay fast.
                    maxNodes = 60000,
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

                // Nudge retry: the last tile of a closed door is sometimes blocked; backing
                // off one tile toward the player usually lands on a walkable spot.
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

                // Multi-area fallback: A* failed (node budget or physical disconnect).
                // If the target is in another area, route to the intermediate TravelZone
                // exit first — a short in-area route A* can always find.
                Location playerLoc = PlayerController.GetPlayer(1)?.LEOIMFNKFGA ?? Location.None;
                Location targetLoc = Utils.HJPCBBGHPDA(_lastRequestTo);
                if (playerLoc != Location.None && playerLoc != targetLoc && !_finalTarget.HasValue)
                {
                    var passage = FindPassageToward(playerLoc, targetLoc, _lastRequestTo);
                    if (passage.HasValue && Vector3.Distance(passage.Value.position, _lastRequestFrom) > TileSize)
                    {
                        if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Cross-area fallback — routing to passage \"{passage.Value.name}\" at {passage.Value.position}");
                        _finalTarget = _selectedTarget;
                        _lastGuidancePlayerLocation = playerLoc;
                        _selectedTarget = ($"Saída → {passage.Value.name}", passage.Value.position);
                        _isInitialPathRequest = wasInitial;
                        RequestPathToTarget(_lastRequestFrom, passage.Value.position);
                        if (wasInitial) ScreenReader.Say($"Vá para a saída em direção a {passage.Value.name}", interrupt: true);
                        return;
                    }
                }

                _lastPathFailed = true;
                if (wasInitial) ScreenReader.Say("Não encontrei uma rota até lá. Pode estar bloqueado.", interrupt: true);
                return;
            }

            _lastPathFailed = false;
            _currentPath = path;
            _simplifiedSteps = SimplifyPath(_lastPathRequestStart, path);
            _currentStepIndex = 0;
            _pathProgressIndex = 0;
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Pathfinding succeeded, {path.Length} waypoints ({_simplifiedSteps.Count} etapas)");

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
        // A* path waypoints are on a 0.25-unit grid, so a route running mostly straight
        // produces tiny perpendicular "jogs" (e.g. a single 0.25-tile step up in the middle
        // of a long leftward leg). Those became phantom "1 pra cima/baixo" steps the player's
        // ~0.56 movement step could never land on, so they bounced. Intermediate steps
        // shorter than this are dropped as noise (the final destination step is always kept).
        private const float MinStepLength = TileSize; // 0.5 = one tile

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

            // Drop tiny intermediate jog steps (A*-grid noise). Measure each step's length
            // along its own axis from the last KEPT point; skip short non-final ones. This
            // kills the phantom-step bounce WITHOUT cutting real corners (so the step-advance
            // threshold can stay tight and not steer the player into walls early).
            var filtered = new List<(string direction, Vector3 endPosition)>();
            Vector3 segStart = start;
            for (int i = 0; i < steps.Count; i++)
            {
                bool isLast = i == steps.Count - 1;
                float axisLen = (steps[i].direction == "cima" || steps[i].direction == "baixo")
                    ? Mathf.Abs(steps[i].endPosition.y - segStart.y)
                    : Mathf.Abs(steps[i].endPosition.x - segStart.x);
                if (isLast || axisLen >= MinStepLength)
                {
                    filtered.Add(steps[i]);
                    segStart = steps[i].endPosition;
                }
            }

            // Merge any steps that ended up adjacent in the same direction after dropping
            // the jogs between them (e.g. "esquerda 5" + "esquerda 13" -> "esquerda 18").
            var merged = new List<(string direction, Vector3 endPosition)>();
            foreach (var s in filtered)
            {
                if (merged.Count > 0 && merged[merged.Count - 1].direction == s.direction)
                    merged[merged.Count - 1] = (s.direction, s.endPosition);
                else
                    merged.Add(s);
            }
            return merged;
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

        // Lowered 1.4->1.2 tiles (0.7->0.6). 0.7 cleared the bounce but cut CORNERS - the
        // player turned ~1.4 tiles early and walked into walls (user: "dou de cara com as
        // paredes"). The phantom-step bounce is now killed at the source by dropping tiny
        // jog steps in SimplifyPath (see MinStepLength), so this only needs to cover the
        // player's own ~0.56 overshoot at a real waypoint - 0.6 does that without cutting
        // corners as aggressively.
        private const float StepAdvanceThreshold = TileSize * 1.2f;

        // Final-destination arrival radius. Confirmed in log (real bug, not a guess): the
        // player's per-tap movement step (~0.56 units) is LARGER than the old per-axis
        // arrival window (TileSize*0.5 = 0.25), so for any destination sitting off the
        // player's movement grid they overshoot it every single step and oscillate
        // "1 pra cima"/"1 pra baixo" forever, never rounding to 0 - stuck 46s at a tavern
        // passage. A ~1-tile 2D radius means "close enough to step onto / interact", which
        // is exactly right for a door/passage trigger and still precise for small objects.
        // Bumped 1.2->1.8->2.0 (0.6->0.9->1.0) per user "se estava uma telha coloque duas".
        // A full two tiles of arrival slack: the per-tap step (~0.56) can't overshoot a
        // 1.0-radius circle, so the "Você chegou" fires cleanly without the back-and-forth.
        private const float FinalArrivalRadius = TileSize * 2.0f;

        // FINE mode for the last few tiles (user: "nas últimas telhas, refine pra acertar a telha
        // exata/lateral em qualquer área"). Coarse guidance stays while far (avoids oscillation);
        // near the target we snap both to tile centers and guide axis-by-axis in whole tiles.
        // Hysteresis (enter < 1.25, exit > 1.75) stops fine/coarse flapping at the boundary.
        private bool _fineMode;
        private const float FineEnterDistance = TileSize * 2.5f;   // 1.25
        private const float FineExitDistance = TileSize * 3.5f;    // 1.75

        // Turn-by-turn guidance now FOLLOWS the raw A* waypoints (_currentPath) instead of
        // collapsing them into a few straight "etapas". The old collapse cut corners: 27-72 winding
        // waypoints became 2-6 straight lines that sliced through the very walls A* had routed
        // around, so the player was told "esquerda" straight into a wall (user: "só becos, bate na
        // parede"). It also made IsOffTrack fire every tile (the player was never on the straight
        // line), forcing a recompute every ~0.4s that produced a different path each time - the
        // "sobe, depois desce sem nada no caminho" oscillation. Progress marks how far along the raw
        // path the player has reached; guidance aims at the next TURN along it.
        private int _pathProgressIndex;
        private const float GuidanceLookAhead = TileSize * 1.5f;   // aim this far ahead on the path
        // True when the last A* attempt (incl. retries/cross-area) gave up with no route, so the
        // straight-line fallback is only a rough compass (may point at walls) - announced as "sem
        // rota" so the user knows it's blocked/unreachable, not a confident route (user: "não sei se
        // está bloqueado ou é só parede").
        private bool _lastPathFailed;

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
            if (_currentPath == null || _currentPath.Length == 0) return null;

            // Advance our progress marker to the nearest waypoint ahead (monotonic).
            AdvancePathProgress(pos);

            Vector3 targetPos = _selectedTarget?.position ?? (Vector3)_currentPath[_currentPath.Length - 1];
            float distToTarget = Vector2.Distance(pos, targetPos);

            // FINE mode for the last couple of tiles: snap both to tile centres and guide in whole
            // tiles (robust when the target tile itself is occupied - a door/anvil - and absorbs the
            // ~0.56 per-tap overshoot so "Você chegou" fires cleanly). Hysteresis avoids flapping.
            if (!_fineMode && distToTarget <= FineEnterDistance) _fineMode = true;
            else if (_fineMode && distToTarget > FineExitDistance) _fineMode = false;
            if (_fineMode)
            {
                Vector3 pC = WorldGrid.LOJBKLKMINM(pos);
                Vector3 tC = WorldGrid.LOJBKLKMINM(targetPos);
                int cx = Mathf.RoundToInt((tC.x - pC.x) / TileSize);
                int cy = Mathf.RoundToInt((tC.y - pC.y) / TileSize);
                if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy)) <= 1)
                {
                    _guidanceActive = false;
                    _fineMode = false;
                    MarkReached(targetPos);
                    return "Você chegou";
                }
                return $"{Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy))} pra {Direction8FromOffsets(cx, cy)}";
            }
            if (distToTarget <= FinalArrivalRadius) { _guidanceActive = false; MarkReached(targetPos); return "Você chegou"; }

            // Turn-by-turn: aim at the first waypoint a short hop ahead (skip ones we're standing on),
            // take the 8-way heading toward it, then EXTEND the leg while the path keeps that same
            // heading. The leg end is the next TURN. This follows the real winding path exactly, so it
            // never points through a wall the way the old straight-line "etapas" did.
            Vector3 aim = targetPos;
            for (int i = _pathProgressIndex; i < _currentPath.Length; i++)
            {
                aim = _currentPath[i];
                if (Vector2.Distance(pos, _currentPath[i]) >= GuidanceLookAhead) break;
            }
            string heading = Direction8(aim - pos);

            Vector3 turnPoint = aim;
            for (int i = _pathProgressIndex; i < _currentPath.Length; i++)
            {
                if (Vector2.Distance(pos, _currentPath[i]) < GuidanceLookAhead) continue;
                if (Direction8((Vector3)_currentPath[i] - pos) != heading) break;
                turnPoint = _currentPath[i];
            }

            int count = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(pos, turnPoint) / TileSize));
            _lastSpokenCountForStep = count;
            return $"{count} pra {heading}";
        }

        // Advance the progress marker to the nearest waypoint ahead (monotonic - never rewinds, so a
        // small overshoot at a corner doesn't snap the route backwards). Forward window only: cheap,
        // and it won't latch onto a later part of a route that loops back near the start.
        private void AdvancePathProgress(Vector3 pos)
        {
            if (_currentPath == null || _currentPath.Length == 0) return;
            float best = float.MaxValue;
            int bestIdx = _pathProgressIndex;
            int end = Mathf.Min(_currentPath.Length, _pathProgressIndex + 60);
            for (int i = _pathProgressIndex; i < end; i++)
            {
                float d = Vector2.Distance(pos, _currentPath[i]);
                if (d < best) { best = d; bestIdx = i; }
            }
            _pathProgressIndex = bestIdx;
        }

        // 8-way direction word from a world delta. Diagonal ("cima e esquerda", etc.) when neither
        // axis dominates the other by more than 2x - so a diagonal staircase in the A* path reads as
        // one steady diagonal instruction instead of flip-flopping between two cardinals.
        private static string Direction8(Vector3 d)
        {
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y);
            if (ax < 0.0001f && ay < 0.0001f) return "cima";
            bool up = d.y > 0f, right = d.x > 0f;
            bool diagonal = ax > 0.0001f && ay > 0.0001f && ax <= ay * 2f && ay <= ax * 2f;
            if (diagonal) return $"{(up ? "cima" : "baixo")} e {(right ? "direita" : "esquerda")}";
            return ay >= ax ? (up ? "cima" : "baixo") : (right ? "direita" : "esquerda");
        }

        private static string Direction8FromOffsets(int cx, int cy)
        {
            int ax = Mathf.Abs(cx), ay = Mathf.Abs(cy);
            bool up = cy > 0, right = cx > 0;
            bool diagonal = ax > 0 && ay > 0 && ax <= ay * 2 && ay <= ax * 2;
            if (diagonal) return $"{(up ? "cima" : "baixo")} e {(right ? "direita" : "esquerda")}";
            return ay >= ax ? (up ? "cima" : "baixo") : (right ? "direita" : "esquerda");
        }

        private void AnnounceDirectionToSelectedTarget()
        {
            Vector3 pos = PlayerController.GetPlayerPosition(1);
            Vector3 targetPos = _selectedTarget.Value.position;

            if (_currentPath != null && _currentPath.Length > 0)
            {
                string message = BuildStepGuidanceMessage(pos);
                if (string.IsNullOrEmpty(message)) return;
                ScreenReader.Say(message, interrupt: true);
                if (Main.DebugMode)
                    DebugLogger.LogState($"WorldNav: Guidance to \"{_selectedTarget.Value.name}\" -> {message} (progresso {_pathProgressIndex}/{_currentPath.Length}) pos={pos}");
                return;
            }

            // No A* route yet. If we're doing cross-area routing and already chaining to
            // an intermediate passage, the selected target IS the passage — straight-line
            // fallback to it (same area, short distance) is safe and useful.
            // If NOT chaining yet and the target is in another Location, give a directional
            // bearing to the target so the player has some sense of where to go while A*
            // computes or while we wait for the cross-area fallback to kick in.
            Vector3 delta = targetPos - pos;
            int dy = Mathf.RoundToInt(Mathf.Abs(delta.y) / TileSize);
            int dx = Mathf.RoundToInt(Mathf.Abs(delta.x) / TileSize);

            string fallbackMessage;
            if (dy == 0 && dx == 0)
            {
                fallbackMessage = "Você chegou";
            }
            else if (_lastPathFailed)
            {
                // A* definitively failed - the bearing may point straight at a wall. Give only a
                // rough compass and say so, so the user relies on the wall sounds instead of trusting
                // a precise count into a wall (user: "só me colocou em paredes").
                fallbackMessage = $"Sem rota, tente ir pra {Direction8(delta)}";
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
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Guidance to \"{_selectedTarget.Value.name}\" -> {fallbackMessage} (sem rota, failed={_lastPathFailed})");
        }

        // Returns the TravelZone (exit from `playerLoc`) whose world position is closest
        // to `targetPos` — used when A* fails cross-area to find a safe intermediate waypoint
        // that IS reachable inside the player's current area.
        // When `targetLoc` is a known Location, a BFS picks the correct first hop in the
        // area graph; when it's Location.None / unmapped, the nearest exit by world distance
        // is used as a heuristic (the map is geographically coherent, so it works).
        private (string name, Vector3 position)? FindPassageToward(Location playerLoc, Location targetLoc, Vector3 targetPos)
        {
            try
            {
                var tzm = TravelZonesManager.GGFJGHHHEJC;
                if (tzm == null || !tzm.allTravelZones.ContainsKey(playerLoc)) return null;

                var exits = tzm.allTravelZones[playerLoc];
                if (exits == null || exits.Count == 0) return null;

                // Route to the passage square on the PLAYER's side, snapped to a walkable tile in the
                // player's own (loaded) area - NOT the raw TravelZone.position, which can be the square
                // on the DESTINATION side (unloaded area). That was why city->road etc. came back
                // "Sem rota" / only estimated: the fallback goal (e.g. "Estrada" at 6,903) sat in the
                // Road area, unreachable from the city. Same fix as direct passage targets.
                Vector3 pp = PlayerController.GetPlayerPosition(1);

                // BFS to find the next hop toward targetLoc (when it's a known Location).
                if (targetLoc != Location.None && tzm.allTravelZones.ContainsKey(targetLoc))
                {
                    Location nextHop = FindNextHop(playerLoc, targetLoc);
                    if (nextHop != Location.None && exits.ContainsKey(nextHop))
                    {
                        var tz = exits[nextHop];
                        string name = LocationNames.TryGetValue(nextHop, out var n) ? n : nextHop.ToString();
                        return (name, GetTravelZoneApproach(tz, pp));
                    }
                }

                // Fallback: pick the exit whose world position is closest to the target.
                (string name, Vector3 position)? best = null;
                float bestDist = float.MaxValue;
                foreach (var kv in exits)
                {
                    if (kv.Value == null) continue;
                    float dist = Vector3.Distance(kv.Value.position, targetPos);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        string n = LocationNames.TryGetValue(kv.Key, out var ln) ? ln : "saída";
                        best = (n, GetTravelZoneApproach(kv.Value, pp));
                    }
                }
                return best;
            }
            catch (System.Exception ex)
            {
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: FindPassageToward threw: {ex.Message}");
                return null;
            }
        }

        // BFS over the area graph to find the first Location to hop to when going from
        // `from` toward `to`.
        private static Location FindNextHop(Location from, Location to)
        {
            try
            {
                var tzm = TravelZonesManager.GGFJGHHHEJC;
                if (tzm == null) return Location.None;

                var queue = new Queue<Location>();
                var visited = new HashSet<Location> { from };
                var parent = new Dictionary<Location, Location>();

                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    Location current = queue.Dequeue();
                    if (current == to)
                    {
                        // Trace back to find the first hop from `from`
                        Location step = to;
                        while (parent.ContainsKey(step) && parent[step] != from)
                            step = parent[step];
                        return step;
                    }
                    if (!tzm.allTravelZones.ContainsKey(current)) continue;
                    foreach (var kv in tzm.allTravelZones[current])
                    {
                        if (!visited.Contains(kv.Key))
                        {
                            visited.Add(kv.Key);
                            parent[kv.Key] = current;
                            queue.Enqueue(kv.Key);
                        }
                    }
                }
                return Location.None;
            }
            catch
            {
                return Location.None;
            }
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

            // [73] One footstep per tile of movement, using the GAME's own terrain-correct step
            // clips (Sound.stepsDirt/Grass/Wood/Stone), played through our 2D AudioSource (the
            // game's MultiAudioManager path is silent for the mod). This gives a step sound on
            // EVERY move regardless of the game's own 0.5s footstep cooldown.
            var clip = PickFootstepClip(player, pos);
            if (clip != null) CustomSounds.PlayGameClip(clip, FootstepVolume);
        }

        private const float FootstepVolume = 0.55f;

        private static AudioClip PickFootstepClip(PlayerController player, Vector3 pos)
        {
            try
            {
                var sound = Sound.GGFJGHHHEJC;
                if (sound == null) return null;
                AudioClip[] arr = sound.stepsDirt;
                if (WorldGrid.GCGNCHFNEBJ(pos, out var tile))
                {
                    if (tile.groundType.HasFlag(GroundType.Floor))
                        arr = tile.materialType == MaterialType.Wood ? sound.stepsWood
                            : tile.materialType == MaterialType.Stone ? sound.stepsStone
                            : sound.stepsDirt;
                    else if (tile.groundType.HasFlag(GroundType.Stone)) arr = sound.stepsStone;
                    else if (tile.groundType.HasFlag(GroundType.Grass)) arr = sound.stepsGrass;
                    else arr = sound.stepsDirt;
                }
                if (arr == null || arr.Length == 0) arr = sound.stepsDirt;
                if (arr == null || arr.Length == 0) return null;
                return arr[UnityEngine.Random.Range(0, arr.Length)];
            }
            catch { return null; }
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

                // Count consecutive bumps (reset if it's been a while since the last one -
                // those are separate incidents, not a stuck streak). After enough in a row,
                // force a fresh route so the guide actively pulls the player off the wall.
                if (Time.unscaledTime - _lastBumpTime > 5f) _consecutiveBumps = 0;
                _lastBumpTime = Time.unscaledTime;
                _consecutiveBumps++;
                if (_guidanceActive && _selectedTarget != null && _consecutiveBumps >= BumpsBeforeReroute)
                {
                    _consecutiveBumps = 0;
                    if (Main.DebugMode) DebugLogger.LogState("WorldNav: stuck on wall repeatedly - forcing reroute");
                    ScreenReader.Say("Recalculando rota", interrupt: true);
                    RequestPathToTarget(pos, _selectedTarget.Value.position);
                }
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
                    // interrupt: true so it's spoken immediately instead of queuing behind a tile/
                    // resource announcement (user: "demora muito, quero algo mais instantâneo").
                    ScreenReader.Say(spoken, interrupt: true);
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
        // Round 234 LAG FIX: this method ran a full-scene FindAll<Door>() (~50ms in the big scenes)
        // on EVERY call, and the directional wall/door detector (IsClosedDoorBlocking) calls it
        // multiple times per frame while walking - a huge continuous stutter. Doors aren't
        // created/destroyed mid-area, so the LIST is safe to cache (each door's live open/closed
        // state is still read fresh per iteration below).
        private static Door[] _doorListCache;
        private static float _doorListCacheTime = -999f;
        private static Door[] DoorsCached()
        {
            if (_doorListCache == null || Time.unscaledTime - _doorListCacheTime > 15f)
            {
                _doorListCacheTime = Time.unscaledTime;
                _doorListCache = FindAll<Door>();
            }
            return _doorListCache;
        }

        private static float? GetClosedDoorBlockDistance(Vector2 pos, Vector2 direction, float maxDistance, bool logDiag = false)
        {
            float? best = null;
            foreach (var door in DoorsCached())
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

        // (Standing-tile terrain announcement removed at the user's request - replaced by the
        // direction-aware front-tile announcer below, which always speaks the tile AHEAD.)

        // Interactable ground only - returns null for tiles with no farming/gathering use, so the
        // per-step announcement stays silent on them. Overlays first (accurate current state),
        // then the tile's groundType.
        private static string UsefulGroundName(Vector3 pos)
        {
            try
            {
                if (WorldGrid.MMIIIKBJKBA<FertileSoil>(pos) != null) return "Terra arada";
                if (WorldGrid.MMIIIKBJKBA<HoleInGround>(pos) != null) return "Buraco";
                if (WorldGrid.GCGNCHFNEBJ(pos, out var tile))
                {
                    if (tile.hasSnow) return null;
                    var gt = tile.groundType;
                    if (gt.HasFlag(GroundType.TilledEarth)) return "Terra arada";
                    if (gt.HasFlag(GroundType.Grass)) return "Grama";
                    if (gt.HasFlag(GroundType.Ground)) return "Terra";
                }
            }
            catch { }
            return null;
        }

        // ===== Mira de ferramentas (foice/enxada/pá/picareta/machado/regador/semente) =====
        // O jogo mira a ferramenta na telha à frente, na DIREÇÃO QUE O JOGADOR ENCARA
        // (characterAnimation.FCGBJEIIMBC - a mesma que as ferramentas usam), não no mouse.
        // Para um jogador cego isso é invisível: ele não sabe pra onde está virado nem se há
        // um alvo compatível à frente. Este handler (1) anuncia o alvo útil à frente conforme a
        // ferramenta na mão, ao virar/andar; e (2) ao apertar F sem alvo válido, avisa "Nada à
        // frente" (o jogo já toca o som do golpe no vazio).
        private enum ToolKind { None, Sickle, Hoe, Spade, Seed, Watering, Pick, Axe }
        private static readonly float FrontObjectRadius = TileSize * 2.2f;
        private Vector2Int _lastFrontTile = new Vector2Int(int.MinValue, int.MinValue);
        private string _lastFrontContent = null;
        private const float FrontScanInterval = 0.05f;  // proactive front-tile scan throttle (the
        private float _lastFrontScanTime;               // per-frame cost is 3 cached-list sweeps)
        // 1-sample debounce: tapping WASD turns AND steps the character, so a transient intermediate
        // tile flashes before the final one - announcing both was the "anuncia duas vezes ao virar"
        // report. Only announce a (tile,content) that persists across two scans; the transient is
        // skipped. A walked-onto tile stays put for several scans, so it still announces (once).
        private Vector2Int _pendingFrontTile = new Vector2Int(int.MinValue, int.MinValue);
        private string _pendingFrontContent = null;

        private static ToolKind ClassifyHeldTool()
        {
            var item = PlayerInventory.GetPlayer(1)?.actionBarInventory?.GetSelectedItem();
            if (item == null) return ToolKind.None;
            if (item is Sickle) return ToolKind.Sickle;
            if (item is Hoe) return ToolKind.Hoe;
            if (item is Spade) return ToolKind.Spade;
            if (item is Seed) return ToolKind.Seed;
            if (item is WateringCan) return ToolKind.Watering;
            if (item is Pick) return ToolKind.Pick;
            if (item is Ax) return ToolKind.Axe;
            return ToolKind.None;
        }

        // The tile one step ahead in the faced direction - the tile the tools act on (the valid
        // "blue square"). MUST match ToolCursorAimPatch exactly: snap the player to their tile
        // CENTER first (WorldGrid.LOJBKLKMINM), THEN step one 0.5-cell ahead, so the announced tile
        // is the same cell the tool targets (the grid is quantized to 0.5 and uses Mathf.Floor).
        private static Vector3 FrontTilePosition() => ChosenToolTile();

        // The tile the held tool will actually act on. LOG PROOF (round 146): the game's diggable
        // "blue squares" are a CLUSTER around the player (own cell + some neighbours), NOT the tile
        // the player faces - so aiming blindly at the facing tile missed. Instead we pick a tile the
        // GAME marks diggable RIGHT NOW: the facing tile if it's blue, else the blue tile nearest to
        // the facing direction (own cell + 8 neighbours). Cached per frame (called from the hot
        // GetCursorWorldPosition postfix). Falls back to the facing tile when nothing is diggable
        // (no tool / not on the farm) so the plain terrain announcement still works.
        // True while ANY loaded area is mid terrain-build (initial world load or an area
        // transition). Read-only. Used to pause the mod's heavy per-frame world scanning during
        // that window - the game keeps the player's input/movement blockers on until the build
        // finishes, so there is nothing useful to scan for, and hammering a half-built grid is
        // both wasteful and a suspected contributor to the transition stall.
        public static bool AnyTerrainUpdating()
        {
            try
            {
                var mgr = TravelZonesManager.GGFJGHHHEJC;
                if (mgr == null || mgr.allTilemapScenes == null) return false;
                foreach (var kv in mgr.allTilemapScenes)
                    if (kv.Value != null && kv.Value.updatingTerrain) return true;
            }
            catch { }
            return false;
        }

        private static int _chosenFrame = -1;
        private static Vector3 _chosenTile;
        public static Vector3 ChosenToolTile()
        {
            if (Time.frameCount == _chosenFrame) return _chosenTile;
            _chosenFrame = Time.frameCount;

            Vector3 center = WorldGrid.LOJBKLKMINM(PlayerController.GetPlayerPosition(1));
            Vector3 facingTile = center + Utils.NGFODNCHPHB(PlayerController.GetPlayer(1).characterAnimation.FCGBJEIIMBC) * TileSize;
            _chosenTile = facingTile;
            try
            {
                ToolKind kind = ClassifyHeldTool();
                if (!IsGroundTool(kind)) return _chosenTile;

                // Facing tile first (respect the player's intent when it's actionable).
                if (ToolCanActAt(kind, facingTile)) { _chosenTile = facingTile; return _chosenTile; }

                // Else the actionable tile nearest to where they're facing (own cell + 8 neighbours).
                float bestD = float.MaxValue; Vector3 best = facingTile; bool found = false;
                if (ToolCanActAt(kind, center)) { best = center; bestD = Vector3.Distance(facingTile, center); found = true; }
                foreach (var m in WorldGrid.allNeighbours)
                {
                    Vector3 c = center + m.position;
                    if (!ToolCanActAt(kind, c)) continue;
                    float d = Vector3.Distance(facingTile, c);
                    if (d < bestD) { bestD = d; best = c; found = true; }
                }
                _chosenTile = found ? best : facingTile;
            }
            catch { }
            return _chosenTile;
        }

        // Cardinal direction from the player's cell to a target tile ("à frente" when it's the
        // facing tile), for announcing where the chosen diggable tile is.
        private static string DirectionToTile(Vector3 target)
        {
            try
            {
                Vector3 center = WorldGrid.LOJBKLKMINM(PlayerController.GetPlayerPosition(1));
                Vector3 d = target - center;
                if (d.sqrMagnitude < 0.01f) return "aqui";
                if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return d.x > 0 ? "à direita" : "à esquerda";
                return d.y > 0 ? "acima" : "abaixo";
            }
            catch { return null; }
        }

        // The compatible target for the held tool on the given tile, or null if none.
        private string FrontTargetForTool(ToolKind kind, Vector3 frontPos)
        {
            switch (kind)
            {
                case ToolKind.Sickle:
                    // The sickle cuts grass-clump / weed Harvestable OBJECTS only - it never changes
                    // the ground's GroundType (so it is NOT how you "remove grass" - that's the spade).
                    return NearestHarvestableName(frontPos);
                case ToolKind.Hoe:
                {
                    // The hoe tills BARE GROUND -> tilled earth. It does NOT work on grass (grass
                    // must be removed with the spade first), only on Ground. An empty tilled bed can
                    // also be cleared back.
                    if (GroundHasFlag(frontPos, GroundType.Ground) && !GroundHasFlag(frontPos, GroundType.Grass))
                        return "terra";
                    var fsH = WorldGrid.MMIIIKBJKBA<FertileSoil>(frontPos);
                    if (fsH != null && fsH.plantedCropSetter == null) return "terra arada";
                    return null;
                }
                case ToolKind.Spade:
                {
                    // The spade changes the tile: Grass -> Ground (this is "remove the grass"),
                    // Ground -> Grass, Stone -> gives a stone. It also digs trees/stumps/rocks.
                    if (GroundHasFlag(frontPos, GroundType.Grass)) return "grama";
                    if (GroundHasFlag(frontPos, GroundType.Stone)) return "pedra";
                    if (GroundHasFlag(frontPos, GroundType.Ground)) return "terra";
                    return null;
                }
                case ToolKind.Seed:
                {
                    var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(frontPos);
                    return (fs != null && fs.plantedCropSetter == null) ? "terra arada" : null;
                }
                case ToolKind.Watering:
                {
                    var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(frontPos);
                    return (fs != null && fs.plantedCropSetter != null) ? "planta" : null;
                }
                case ToolKind.Pick:
                case ToolKind.Axe:
                    return FocusedChopMineTarget(kind);
            }
            return null;
        }

        // A dry, empty tilled tile at/ahead - a seed can't plant here until it's watered.
        private static bool FrontIsDryTilled(Vector3 p, out string hint)
        {
            hint = null;
            try
            {
                var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(p);
                if (fs == null || fs.plantedCropSetter != null || fs.daysUntilDry > 1) return false;
                string dir = DirectionToTile(p);
                hint = string.IsNullOrEmpty(dir) ? "terra arada seca, regue antes de plantar"
                    : $"{dir}: terra arada seca, regue antes";
                return true;
            }
            catch { return false; }
        }

        // Pick/Axe act on the FOCUSED proximity object (a Rock/Tree the player is near+facing), not
        // a tile - the tool auto-walks to it. So "can act" = the focused object is a rock (pick, not
        // axRequired) or a tree/axe-rock (axe). Fixes "diz nada para minerar mas tem".
        private static string FocusedChopMineTarget(ToolKind kind)
        {
            try
            {
                var go = InputByProximityManager.GetPlayer(1)?.GetCurrentFocusedInputElement()?.mainGameObject;
                if (go == null) return null;
                var rock = go.GetComponent<Rock>() ?? go.GetComponentInParent<Rock>();
                var tree = go.GetComponent<Tree>() ?? go.GetComponentInParent<Tree>();
                if (kind == ToolKind.Pick && rock != null)
                {
                    // Blocked-mining feedback (user: "pedras que não deixa minerar... anuncie o motivo").
                    // A pick can't break an axe-required rock, and higher-tier ore needs a better pick.
                    if (rock.axRequired) return "essa pedra precisa de machado";
                    string nm = DroppedName(rock.droppedItems != null && rock.droppedItems.Length > 0 ? rock.droppedItems[0].item : null) ?? "pedra";
                    if (rock.toolLevelRequired > 1) nm += $", precisa de picareta nível {rock.toolLevelRequired}";
                    return nm;
                }
                if (kind == ToolKind.Axe && (tree != null || (rock != null && rock.axRequired)))
                    return "árvore";
            }
            catch { }
            return null;
        }

        // F2: announce player money + current time + season + location on one key (user request).
        // Works anywhere (called outside the no-UI gate). KeyCode.F2 isn't used by the game.
        private void HandleInfoKey()
        {
            if (!Input.GetKeyDown(KeyCode.F2)) return;
            var parts = new System.Collections.Generic.List<string>();
            try
            {
                if (Money.IsValid())
                {
                    int g = Money.GetGold(), s = Money.GetSilver(), c = Money.GetCopper();
                    var m = new System.Collections.Generic.List<string>();
                    if (g > 0) m.Add($"{g} ouro");
                    if (s > 0) m.Add($"{s} prata");
                    if (c > 0 || m.Count == 0) m.Add($"{c} cobre");
                    parts.Add("Dinheiro: " + string.Join(", ", m));
                }
            }
            catch { }
            try
            {
                var d = WorldTime.NOAOJJLNHJJ;
                parts.Add($"{d.hour}:{d.min:00}");
                parts.Add(SeasonPt(d.season));
            }
            catch { }
            try { parts.Add(LocationPt(PlayerController.GetPlayer(1).LEOIMFNKFGA)); }
            catch { }
            if (parts.Count > 0) ScreenReader.Announce(string.Join(". ", parts));
        }

        // User wants money GAINS spoken with the coin type ("mais 42 cobre", "mais 1 ouro") instead
        // of the bare HUD number that we now filter. Poll the total (Money.ToCopper) and, on an
        // increase, announce the delta split into gold/silver/copper (100 copper = 1 silver, 100
        // silver = 1 gold). Spending only re-baselines (no announce).
        private int _lastMoneyCopper = -1;
        private void HandleMoneyGainAnnouncement()
        {
            try
            {
                if (!Money.IsValid()) return;
                int total = Money.ToCopper();
                if (_lastMoneyCopper < 0) { _lastMoneyCopper = total; return; }
                if (total <= _lastMoneyCopper) { _lastMoneyCopper = total; return; }
                int delta = total - _lastMoneyCopper;
                _lastMoneyCopper = total;
                int d = delta;
                int g = d / 10000; d %= 10000;
                int s = d / 100; int c = d % 100;
                var parts = new System.Collections.Generic.List<string>();
                if (g > 0) parts.Add($"{g} ouro");
                if (s > 0) parts.Add($"{s} prata");
                if (c > 0) parts.Add($"{c} cobre");
                if (parts.Count > 0) ScreenReader.Say($"Mais {string.Join(", ", parts)}", interrupt: false);
            }
            catch { }
        }

        // F4: current tavern reputation (user request). Milestone = the reputation LEVEL shown in
        // TavernManagerUI; plus any unspent skill points. F4 isn't used by the game.
        private void HandleReputationKey()
        {
            if (!Input.GetKeyDown(KeyCode.F4)) return;
            var parts = new System.Collections.Generic.List<string>();
            int level = 0;
            try { level = TavernReputation.GetMilestone(); parts.Add($"Reputação: nível {level}"); } catch { }
            // How much reputation is left to the next level (user request): current exp vs the
            // current milestone's repMax.
            try
            {
                int cur = TavernReputation.GetReputationExp();
                int max = ReputationDBAccessor.GetReputation(level).repMax;
                if (max > 0) parts.Add($"{cur} de {max}, faltam {Mathf.Max(0, max - cur)} para o próximo nível");
            }
            catch { }
            try
            {
                int sp = TavernReputation.GetRemainingSkillPoints();
                if (sp > 0) parts.Add($"{sp} ponto{(sp > 1 ? "s" : "")} de habilidade disponível{(sp > 1 ? "eis" : "")}");
            }
            catch { }
            // Recipe fragments the player currently has (user: "no f4 deveria dizer tb quantos
            // fragmentos tenho"). RecipesManager.recipeFragments is the live available count.
            try { parts.Add($"{RecipesManager.recipeFragments} fragmentos de receita"); } catch { }
            // Comfort of the zone the player is standing in - the same value the visual comfort bar
            // shows (TavernZone.comfort for the player's current zoneIndex; user: "f4 tb informe em
            // quanto está o conforto da taverna").
            try
            {
                var p = PlayerController.GetPlayer(1);
                var tzm = TavernZonesManager.GGFJGHHHEJC;
                if (p != null && tzm != null)
                {
                    var zone = tzm.GetTavernZone(p.zoneIndex);
                    if (zone != null) parts.Add($"Conforto: {zone.comfort}");
                }
            }
            catch { }
            ScreenReader.Announce(parts.Count > 0 ? string.Join(". ", parts) : "Reputação indisponível");
        }

        private string _lastEventText;
        private float _lastEventTextCheck;
        private void HandleEventTextAnnouncement()
        {
            if (Time.unscaledTime - _lastEventTextCheck < 0.2f) return;
            _lastEventTextCheck = Time.unscaledTime;
            try
            {
                EventTextUI open = null;
                foreach (var e in FindAll<EventTextUI>())
                {
                    if (e == null || e.eventText == null || !e.eventText.gameObject.activeInHierarchy) continue;
                    open = e; break;
                }
                if (open == null) { _lastEventText = null; return; }
                string txt = open.eventText.text;
                if (string.IsNullOrWhiteSpace(txt)) return;
                if (txt == _lastEventText) return;   // dedup: only announce a NEW message
                _lastEventText = txt;
                ScreenReader.Announce(txt);
                if (Main.DebugMode) DebugLogger.LogState($"EventText announced: \"{txt}\"");
            }
            catch { }
        }

        private static string SeasonPt(Season s)
        {
            switch (s)
            {
                case Season.Spring: return "Primavera";
                case Season.Summer: return "Verão";
                case Season.Autumn: return "Outono";
                case Season.Winter: return "Inverno";
                default: return s.ToString();
            }
        }

        private static string LocationPt(Location l)
        {
            switch (l)
            {
                case Location.Tavern: return "Taverna";
                case Location.Road: return "Estrada";
                case Location.River: return "Rio";
                case Location.Camp: return "Acampamento";
                case Location.Quarry: return "Pedreira";
                case Location.Farm: return "Fazenda";
                case Location.BarnInterior: return "Interior do celeiro";
                case Location.FarmShop: return "Loja da fazenda";
                case Location.CityOutside: return "Fora da cidade";
                case Location.Mine: return "Mina";
                case Location.Beach: return "Praia";
                case Location.City: return "Cidade";
                case Location.Sawmill: return "Serraria";
                case Location.Blacksmith: return "Ferreiro";
                case Location.Forest: return "Floresta";
                default: return l.ToString();
            }
        }

        private static string ToolSoundKey(ToolKind k)
        {
            switch (k)
            {
                case ToolKind.Spade: return "cavar";
                case ToolKind.Hoe: return "arar";
                case ToolKind.Seed: return "plantar";
                case ToolKind.Watering: return "regar";
                case ToolKind.Sickle: return "foice";
                case ToolKind.Pick: return "picareta";
                case ToolKind.Axe: return "machado";
                default: return null;
            }
        }

        private static bool IsGroundTool(ToolKind k) =>
            k == ToolKind.Spade || k == ToolKind.Hoe || k == ToolKind.Seed || k == ToolKind.Watering;

        // Whether the held tool can ACTUALLY act on this tile. NOTE: the game's "blue square"
        // (GetBlueSquareAtPosition) is a LOOSER pre-filter than the real gate - it skips the hoe's
        // 8-neighbour zone sweep - so it gave false "dá pra arar" on tiles that then wouldn't till.
        // We replicate each tool's real validity (from HoeInstance.NBFBPMNMBJG:382 /
        // SpadeInstance.NBFBPMNMBJG:78 / Seed / WateringCan) precisely instead.
        private static bool ToolCanActAt(ToolKind kind, Vector3 p)
        {
            try
            {
                // Necessary condition: the game must mark the tile diggable (a "blue square") - that's
                // what its action gate (HBEBAFHEMAP) checks first. Then refine with the tool's real
                // validity (the blue set is looser than the hoe's neighbour rule / the seed's watered
                // requirement, so it alone gives false positives).
                // The HOE: call the GAME'S OWN per-cell predicate (public static, this is the exact
                // blue/red decision the game draws - HoeInstance.cs:447/498). Its blue squares don't
                // show up in our GetBlueSquareAtPosition query (deadlock), but GOCEDDNOMPN answers
                // "will the hoe till here" definitively, including the weeds/neighbour checks our
                // hand-rolled CanHoe was missing.
                if (kind == ToolKind.Hoe) return HoeInstance.GOCEDDNOMPN(p);

                var gc = PlayerController.GetPlayer(1)?.gridController;
                if (gc == null || gc.GetBlueSquareAtPosition(p) == null) return false;
                switch (kind)
                {
                    case ToolKind.Spade: return CanSpade(p);
                    case ToolKind.Seed: return CanPlant(p);
                    case ToolKind.Watering: return CanWater(p);
                }
            }
            catch { }
            return false;
        }

        // Diagnostic: dump a tile's real state + the GAME'S blue square there, so we can see where
        // the game actually accepts a dig/till vs where we aim.
        private static void DumpFarmTile(string label, Vector3 p)
        {
            string gt = "?";
            try { if (WorldGrid.GCGNCHFNEBJ(p, out var t)) gt = t.groundType.ToString(); } catch { }
            bool farm = false, wz = false, blue = false, placeable = false, allNb = false;
            try { farm = WorldGrid.LKBLKCFOEPA(p); } catch { }
            try { wz = WorldGrid.AGKGGAFFFGM(p) == ZoneType.WithoutZone; } catch { }
            try { placeable = WorldGrid.GJHHDIJOILG(p); } catch { }
            try { allNb = AllNeighboursWithoutZone(p); } catch { }
            try { blue = PlayerController.GetPlayer(1).gridController.GetBlueSquareAtPosition(p) != null; } catch { }
            DebugLogger.LogState($"  FarmTile {label} pos={p} ground={gt} farmable={farm} withoutZone={wz} allNbWZ={allNb} placeable={placeable} gameBlueSquare={blue}");
        }

        private static bool OnRoad()
        { try { return PlayerController.GetPlayer(1).LEOIMFNKFGA == Location.Road; } catch { return true; } }
        private static bool IsToolBusy()
        { try { return PlayerController.GetPlayer(1).NILLCIMMKJE; } catch { return false; } }
        private static bool TileHasSnow(Vector3 p)
        { try { return WorldGrid.GCGNCHFNEBJ(p, out var t) && t.hasSnow; } catch { return false; } }
        private static bool IsWithoutZone(Vector3 p)
        { try { return WorldGrid.AGKGGAFFFGM(p) == ZoneType.WithoutZone; } catch { return false; } }
        private static bool AllNeighboursWithoutZone(Vector3 p)
        {
            foreach (var m in WorldGrid.allNeighbours)
                if (WorldGrid.AGKGGAFFFGM(p + m.position) != ZoneType.WithoutZone) return false;
            return true;
        }

        // Spade: turns Grass->Ground / Ground->Grass / Stone->item on a farmable, zone-less tile.
        // Less strict than the hoe (no 8-neighbour sweep) - that's why it "cavou muito mais".
        private static bool CanSpade(Vector3 p)
        {
            if (!OnRoad() || TileHasSnow(p) || WorldGrid.GJHHDIJOILG(p)) return false;
            bool grd = GroundHasFlag(p, GroundType.Grass) || GroundHasFlag(p, GroundType.Stone)
                || (GroundHasFlag(p, GroundType.Ground) && !GroundHasFlag(p, GroundType.TilledEarth));
            if (!grd) return false;
            return WorldGrid.LKBLKCFOEPA(p) && IsWithoutZone(p);
        }

        // Hoe: tills bare Ground -> TilledEarth. Requires the tile AND all 8 neighbours to be
        // WithoutZone (HoeInstance.cs:405-419) - the reason only a few interior farm tiles till.
        private static bool CanHoe(Vector3 p)
        {
            if (!OnRoad() || TileHasSnow(p) || WorldGrid.GJHHDIJOILG(p)) return false;
            if (!WorldGrid.LKBLKCFOEPA(p) || !IsWithoutZone(p) || !AllNeighboursWithoutZone(p)) return false;
            if (GroundHasFlag(p, GroundType.Ground) && !GroundHasFlag(p, GroundType.TilledEarth)) return true;
            var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(p);   // empty tilled bed can be re-cleared
            return fs != null && fs.plantedCropSetter == null;
        }

        // Seed: plant on an EMPTY tilled soil that is WATERED (daysUntilDry > 1, Seed.cs:74).
        private static bool CanPlant(Vector3 p)
        {
            var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(p);
            return fs != null && fs.plantedCropSetter == null && fs.daysUntilDry > 1;
        }

        // Watering can: waters a tilled tile; same zone gates as the hoe.
        private static bool CanWater(Vector3 p)
        {
            if (!OnRoad() || TileHasSnow(p) || WorldGrid.GJHHDIJOILG(p)) return false;
            if (!WorldGrid.LKBLKCFOEPA(p) || !IsWithoutZone(p) || !AllNeighboursWithoutZone(p)) return false;
            return GroundHasFlag(p, GroundType.TilledEarth);
        }

        private const float ArableScanRadius = 6f;   // world units scanned around the player

        // The 0.5-grid arable scan is ~1600 cells + the hoe predicate - too heavy to run on EVERY
        // Page Up/Down (user: "grande lag na troca das categorias"). Cache it: rescan only when a
        // farming action changed a tile (_arableDirty, set by the OnActionDone hook), when the player
        // walked a good distance, or after a few seconds. Tile positions are absolute, so reusing the
        // cache while paging (standing still) is correct.
        private float _arableScanTime = -999f;
        private Vector3 _arableScanCenter;
        private bool _arableDirty = true;
        private readonly List<(string name, Vector3 position)> _arableCache = new List<(string name, Vector3 position)>();

        private void ScanArableTiles(Vector3 playerPos, List<(string name, Vector3 position, string category)> list)
        {
            try
            {
                bool stale = _arableDirty || Vector3.Distance(playerPos, _arableScanCenter) > 4f;
                if (stale)
                {
                    float t0 = Main.DebugMode ? Time.realtimeSinceStartup : 0f;
                    int farmCells = 0;
                    _arableCache.Clear();
                    Vector3 c = WorldGrid.LOJBKLKMINM(playerPos);
                    for (float dx = -ArableScanRadius; dx <= ArableScanRadius + 0.01f; dx += TileSize)
                        for (float dy = -ArableScanRadius; dy <= ArableScanRadius + 0.01f; dy += TileSize)
                        {
                            Vector3 p = new Vector3(c.x + dx, c.y + dy, 0f);
                            if (!WorldGrid.GCGNCHFNEBJ(p, out var tile) || !tile.farmable) continue;
                            farmCells++;
                            var gt = tile.groundType;
                            if (gt.HasFlag(GroundType.TilledEarth)) continue;
                            bool arar = false;
                            try { arar = HoeInstance.GOCEDDNOMPN(p); } catch { }
                            if (arar) { _arableCache.Add(("Pra arar", p)); continue; }
                            if (gt.HasFlag(GroundType.Grass) && CanSpade(p)) _arableCache.Add(("Pra cavar", p));
                        }
                    _arableScanTime = Time.unscaledTime;
                    _arableScanCenter = playerPos;
                    _arableDirty = false;
                    if (Main.DebugMode) DebugLogger.LogState($"ScanArableTiles: {(Time.realtimeSinceStartup - t0) * 1000f:F1}ms, farmCells={farmCells}, results={_arableCache.Count}");
                }
                foreach (var (name, p) in _arableCache) list.Add((name, p, "Cultivo"));
            }
            catch { }
        }

        // Describe the arable zone's composition when the player ENTERS the farm (tile.farmable
        // goes false->true), so a screen-reader player gets the lay of the land while walking
        // (user: "ao andar ter uma descrição da zona que compõe as áreas aráveis").
        private bool _wasInFarm;
        private bool _farmHooked;
        private void HandleArableZoneAnnouncement()
        {
            EnsureFarmHooks();
            try
            {
                Vector3 pos = PlayerController.GetPlayerPosition(1);
                bool inFarm = WorldGrid.GCGNCHFNEBJ(pos, out var tile) && tile.farmable;
                if (inFarm == _wasInFarm) return;
                _wasInFarm = inFarm;
                if (inFarm) AnnounceZoneSummary();   // announce on entering the farm
            }
            catch { }
        }

        // Re-announce the composition after a farming action (till/dig/plant/harvest/water) - user:
        // "quando eu interagir e usar um espaço naquela zona, falar novamente quantos tem de cada".
        // Also marks the arable cache dirty so the category counts refresh.
        private void EnsureFarmHooks()
        {
            if (_farmHooked) return;
            try
            {
                var cr = CommonReferences.GGFJGHHHEJC;
                if (cr == null) return;
                cr.OnActionDone += (playerNum, action) =>
                {
                    _arableDirty = true;
                    _targetDirty = true;   // force the nav list to rebuild with fresh counts
                    // A farm action can add/remove a FertileSoil/Harvestable (plant, harvest, chop) -
                    // force the 15s scene cache to re-scan next frame so the walking resource
                    // announcement sees the just-planted crop / freed tile without a 15s delay.
                    _lastAllPlaceablesTime = -999f;
                    if (Main.DebugMode) DebugLogger.LogState($"OnActionDone: action={action} inFarm={_wasInFarm}");
                    try { if (_wasInFarm) AnnounceZoneSummary(); } catch { }
                    AnnounceObjectiveIfChanged();   // "restam 4" as you complete objective steps
                };
                _farmHooked = true;
                if (Main.DebugMode) DebugLogger.LogState("Farm hooks: subscribed to OnActionDone");
            }
            catch { }
        }

        private void AnnounceZoneSummary()
        {
            try
            {
                Vector3 pos = PlayerController.GetPlayerPosition(1);
                int cavar = 0, arar = 0;
                Vector3 c = WorldGrid.LOJBKLKMINM(pos);
                for (float dx = -ArableScanRadius; dx <= ArableScanRadius + 0.01f; dx += TileSize)
                    for (float dy = -ArableScanRadius; dy <= ArableScanRadius + 0.01f; dy += TileSize)
                    {
                        Vector3 p = new Vector3(c.x + dx, c.y + dy, 0f);
                        if (!WorldGrid.GCGNCHFNEBJ(p, out var t) || !t.farmable) continue;
                        if (t.groundType.HasFlag(GroundType.TilledEarth)) continue;
                        bool a = false; try { a = HoeInstance.GOCEDDNOMPN(p); } catch { }
                        if (a) arar++;
                        else if (t.groundType.HasFlag(GroundType.Grass) && CanSpade(p)) cavar++;
                    }
                int molhada = 0, seca = 0, plMolhada = 0, pronta = 0, sede = 0, morta = 0;
                foreach (var fs in FindAll<FertileSoil>())
                {
                    if (fs == null) continue;
                    if (fs.plantedCropSetter != null)
                    {
                        switch (CropStateLabel(fs.plantedCropSetter, fs.daysUntilDry))
                        {
                            case "Planta morta": morta++; break;
                            case "Planta pronta pra colher": pronta++; break;
                            case "Planta com sede": sede++; break;
                            default: plMolhada++; break;   // "Planta molhada"
                        }
                    }
                    else if (fs.daysUntilDry > 1) molhada++;
                    else seca++;
                }
                var parts = new System.Collections.Generic.List<string>();
                if (cavar > 0) parts.Add($"{cavar} pra cavar");
                if (arar > 0) parts.Add($"{arar} pra arar");
                if (molhada > 0) parts.Add($"{molhada} terra molhada");
                if (seca > 0) parts.Add($"{seca} terra seca");
                if (plMolhada > 0) parts.Add($"{plMolhada} planta{(plMolhada > 1 ? "s" : "")} molhada{(plMolhada > 1 ? "s" : "")}");
                if (sede > 0) parts.Add($"{sede} planta{(sede > 1 ? "s" : "")} com sede");
                if (pronta > 0) parts.Add($"{pronta} pronta{(pronta > 1 ? "s" : "")} pra colher");
                if (morta > 0) parts.Add($"{morta} morta{(morta > 1 ? "s" : "")}");
                ScreenReader.Say(parts.Count > 0 ? $"Roça: {string.Join(", ", parts)}" : "Roça", interrupt: false);
            }
            catch { }
        }

        // Name a farm tile: the planted CROP's name when it has one (so a planted tile reads
        // "trigo, dá pra regar" not "terra arada"), else the ground state. Crop name via
        // CropSetter -> Crop (reflection, obfuscated names, safe fallback "planta").
        private static System.Reflection.PropertyInfo _cropProp;
        private static System.Reflection.MethodInfo _cropNameM;
        // Localized crop name from a CropSetter (obfuscated: FJJCOJGJCLF property -> Crop, LOMLPPEKPJB
        // -> localized name), or null. Reflection so no compile dependency on the obfuscated names.
        private static string CropName(CropSetter cs)
        {
            try
            {
                if (cs == null) return null;
                if (_cropProp == null) _cropProp = cs.GetType().GetProperty("FJJCOJGJCLF");
                var crop = _cropProp?.GetValue(cs) as Crop;
                if (crop == null) return null;
                // The crop's OWN name falls back to the raw Spanish asset name ("83 - Hojas de Té
                // Rojo") when its nameId has no PT translation. The HARVESTED ITEM's name IS localized
                // (PT), so prefer it (user: "quero as plantas em ptbr").
                try
                {
                    if (crop.harvestedItems != null && crop.harvestedItems.Length > 0 && crop.harvestedItems[0].item != null)
                    {
                        string itemName = crop.harvestedItems[0].item.IABAKHPEOAF();
                        if (!string.IsNullOrEmpty(itemName) && !itemName.Contains(" - ")) return itemName;
                    }
                }
                catch { }
                string cn = crop.LOMLPPEKPJB();
                if (string.IsNullOrEmpty(cn)) return null;
                // Strip a leading "83 - " asset-id prefix if we fell back to the raw name.
                int dash = cn.IndexOf(" - ");
                if (dash >= 0 && int.TryParse(cn.Substring(0, dash), out _)) cn = cn.Substring(dash + 3).Trim();
                return cn;
            }
            catch { return null; }
        }

        private static string CropOrGroundName(Vector3 pos)
        {
            try
            {
                var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(pos);
                if (fs != null && fs.plantedCropSetter != null)
                    return CropName(fs.plantedCropSetter) ?? "planta";
            }
            catch { }
            return UsefulGroundName(pos);
        }

        // A planted crop is ready to harvest when it's fully grown AND its harvestable is armed
        // (Harvestable.isHarvestable). Harvesting = the Interact key on it ("[E] Harvest").
        private static bool CropIsReady(CropSetter cs)
        {
            try { return cs != null && cs.growable != null && cs.growable.grown && cs.harvestable != null && cs.harvestable.isHarvestable; }
            catch { return false; }
        }

        private static bool CropIsDead(CropSetter cs)
        {
            try { return cs != null && cs.growable != null && cs.growable.isDead; }
            catch { return false; }
        }

        // A ready crop is harvested by HAND (the Interact key / Ctrl+Enter -> MouseUp) only when its
        // Harvestable.canInteract is true. When it's false the interact path is refused
        // (Harvestable.IsAvailableByProximity returns false on !canInteract) and the crop must be cut
        // with the SICKLE (foice) instead - this is why wheat "precisa ser com a foice".
        private static bool CropHandHarvest(CropSetter cs)
        {
            try { return cs != null && cs.harvestable != null && cs.harvestable.canInteract; }
            catch { return true; }
        }

        // The "Cultivo" state label for a PLANTED crop (null if the soil has no crop). Priority:
        // dead > ready-to-harvest > thirsty (dry soil, still growing) > normal growing. daysUntilDry
        // comes from the crop's FertileSoil (==0 means the soil dried out and the crop needs water).
        private static string CropStateLabel(CropSetter cs, int daysUntilDry)
        {
            if (cs == null) return null;
            if (CropIsDead(cs)) return "Planta morta";
            if (CropIsReady(cs)) return "Planta pronta pra colher";
            // A growing crop is either watered (soil still damp) or thirsty (soil dried out). User
            // wants these split: "planta molhada" vs "planta com sede".
            return daysUntilDry > 0 ? "Planta molhada" : "Planta com sede";
        }

        private static bool GroundHasFlag(Vector3 pos, GroundType flag)
        {
            try { return WorldGrid.GCGNCHFNEBJ(pos, out var tile) && tile.groundType.HasFlag(flag); }
            catch { return false; }
        }

        private string NearestHarvestableName(Vector3 frontPos)
        {
            float best = FrontObjectRadius; string name = null;
            foreach (var h in _cachedHarvestables)
            {
                if (h == null) continue;
                float d = Vector3.Distance(frontPos, h.gameObject.transform.position);
                if (d >= best) continue;
                best = d;
                name = (h.harvestedItems != null && h.harvestedItems.Length > 0 && h.harvestedItems[0].item != null)
                    ? ItemDisplayName(h.harvestedItems[0].item) : CleanSceneObjectName(h.gameObject.name);
            }
            return name;
        }

        private string NearestMiscHarvestName(Vector3 frontPos)
        {
            float best = FrontObjectRadius; string name = null;
            foreach (var m in _cachedMiscHarvests)
            {
                if (m == null) continue;
                float d = Vector3.Distance(frontPos, m.gameObject.transform.position);
                if (d >= best) continue;
                best = d;
                name = m.harvestedItems.item != null ? ItemDisplayName(m.harvestedItems.item) : CleanSceneObjectName(m.gameObject.name);
            }
            return name;
        }

        private string NearestTreeName(Vector3 frontPos)
        {
            float best = FrontObjectRadius; string name = null;
            foreach (var t in _cachedTrees)
            {
                if (t == null) continue;
                float d = Vector3.Distance(frontPos, t.gameObject.transform.position);
                if (d >= best) continue;
                best = d;
                var pl = t.GetComponent<Placeable>();
                name = pl != null ? DescribePlaceable(pl) : CleanSceneObjectName(t.gameObject.name);
            }
            return name;
        }

        private static string ToolVerb(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.Sickle: return "cortar";
                case ToolKind.Hoe: return "arar";
                case ToolKind.Spade: return "cavar";
                case ToolKind.Seed: return "plantar";
                case ToolKind.Watering: return "regar";
                case ToolKind.Pick: return "minerar";
                case ToolKind.Axe: return "cortar";
                default: return "usar";
            }
        }

        private void HandleToolAimAnnouncement()
        {
            try
            {
                // The tools act on the tile ONE STEP AHEAD in the facing direction (confirmed in
                // code: the spade/hoe accept a target only if it's a valid "blue square", and the
                // blue square is the front tile - SpadeInstance.cs:354/363, ToolInstance.cs:1362).
                // So we describe and validate the FRONT tile (the tile the tool will act on).
                Vector3 frontPos = FrontTilePosition();
                var frontTile = new Vector2Int(Mathf.RoundToInt(frontPos.x / TileSize), Mathf.RoundToInt(frontPos.y / TileSize));

                // [F] No-target feedback (tool-specific): pressing F with a tool but no valid target
                // on the front tile -> warn + a reliable "empty" cue (Sickle handled separately).
                // KeyCode.F is the user's use key.
                if (Input.GetKeyDown(KeyCode.F) && !IsToolBusy())
                {
                    // Skip while a swing is in progress (NILLCIMMKJE) - the game ignores a mid-swing
                    // press, so a "Nada aqui" then would be a false negative.
                    ToolKind kind = ClassifyHeldTool();
                    if (kind != ToolKind.None)
                    {
                        // Ground tools: replicate the tool's REAL validity (zone/neighbours/farmable/
                        // ground) - the user hit "diz grama mas não deixa cavar". Object tools
                        // (sickle/pick/axe): keep the nearest-object test.
                        bool canAct = IsGroundTool(kind)
                            ? ToolCanActAt(kind, frontPos)
                            : !string.IsNullOrEmpty(FrontTargetForTool(kind, frontPos));
                        if (!canAct)
                        {
                            if (kind != ToolKind.Sickle) CustomSounds.PlayItemBumpOnce();
                            ScreenReader.Say($"Nada aqui para {ToolVerb(kind)}", interrupt: true);
                        }
                        else
                        {
                            // Distinct audio cue per tool so a blind player can tell which tool acted
                            // (user: "cada ferramenta não tem seu som"). Silent until the user adds the
                            // .wav (see sons.txt).
                            CustomSounds.PlayToolSound(ToolSoundKey(kind));
                        }
                        if (Main.DebugMode)
                        {
                            Vector3 pp = PlayerController.GetPlayerPosition(1);
                            var dir = PlayerController.GetPlayer(1).characterAnimation.FCGBJEIIMBC;
                            DebugLogger.LogState($"ToolAim: F kind={kind} dir={dir} playerPos={pp} frontPos={frontPos} frontTile={frontTile} canAct={canAct} busy={IsToolBusy()}");
                            // Rich dump: where does the GAME actually place diggable "blue squares"?
                            Vector3 center = WorldGrid.LOJBKLKMINM(pp);
                            DumpFarmTile("frontPos", frontPos);
                            DumpFarmTile("playerCell", center);
                            DumpFarmTile("up", center + Vector3.up * 0.5f);
                            DumpFarmTile("down", center + Vector3.down * 0.5f);
                            DumpFarmTile("left", center + Vector3.left * 0.5f);
                            DumpFarmTile("right", center + Vector3.right * 0.5f);
                        }
                    }
                }

                // Proactive: ALWAYS announce the interactable content of the FRONT tile (the one the
                // tool acts on), independent of the tool in hand (the player may forget which tool is
                // selected). Re-announce on EVERY change of the tile OR its content - so turning to a
                // new tile and changing a tile in place (spade/hoe) both re-speak.
                // Throttled (the scan sweeps 3 cached lists) - still well under one tile per tick.
                if (Time.unscaledTime - _lastFrontScanTime < FrontScanInterval) return;
                _lastFrontScanTime = Time.unscaledTime;

                // ONE clean announcement (user: "diga um único tipo de telha, não grama+terra arada
                // junto; só onde dá pra cavar/arar"):
                // - With a farming tool: announce ONLY the actionable tile the tool will act on, as
                //   "{direção}: {chão}, dá pra {verbo}". Silent when nothing near is actionable, so
                //   walking only speaks where you can work.
                // - Without a farming tool: just the single useful ground of the tile ahead.
                // No crop/object chaining (that was announcing planted seeds you weren't standing on).
                ToolKind held = ClassifyHeldTool();
                string content;
                if (IsGroundTool(held))
                {
                    if (ToolCanActAt(held, frontPos))
                    {
                        string g = CropOrGroundName(frontPos);
                        string gl = string.IsNullOrEmpty(g) ? "telha" : g.ToLowerInvariant();
                        string dir = DirectionToTile(frontPos);
                        content = string.IsNullOrEmpty(dir)
                            ? $"{gl}, dá pra {ToolVerb(held)}"
                            : $"{dir}: {gl}, dá pra {ToolVerb(held)}";
                    }
                    else if (held == ToolKind.Seed && FrontIsDryTilled(frontPos, out string dseca))
                    {
                        // Guide the planting flow: a seed can only plant on WATERED tilled soil, so a
                        // dry tilled tile reads silent otherwise (confusing). Tell them to water first.
                        content = dseca;
                    }
                    else content = null;
                }
                else
                {
                    // No farming tool in hand -> stay SILENT (user: "só anunciar onde dá pra cavar/
                    // arar"). Finding tilled/plantable spots without a tool is what the "Cultivo"
                    // target category is for.
                    content = null;
                }

                // 1-sample debounce: require the (tile,content) to persist across two scans before
                // announcing, so the transient tile during a turn+step isn't spoken (one announce
                // per turn/step, not two).
                if (frontTile != _pendingFrontTile || content != _pendingFrontContent)
                {
                    _pendingFrontTile = frontTile;
                    _pendingFrontContent = content;
                    return;
                }

                if (frontTile == _lastFrontTile && content == _lastFrontContent) return;
                _lastFrontTile = frontTile;
                _lastFrontContent = content;
                if (!string.IsNullOrEmpty(content))
                {
                    // interrupt so a new tile replaces the previous line instead of queueing behind
                    // it (user: "fala um lugar e outro ao mesmo tempo") - the latest tile is what
                    // matters while walking/working.
                    ScreenReader.Say(content, interrupt: true);
                    if (Main.DebugMode) DebugLogger.LogState($"ToolAim: front={frontTile} content=\"{content}\"");
                }
            }
            catch { }
        }

        // The interactable content of the tile ahead, chained (a tile can be several things at once
        // - GroundType is a [Flags] enum, plus FertileSoil/crop overlays and objects on top). Tool-
        // independent. Returns null when nothing interactable is there.
        private string DescribeFrontTile(Vector3 frontPos)
        {
            var parts = new System.Collections.Generic.List<string>();
            // Only announce the GROUND on USEFUL (farmable) tiles - the plot where you can dig/till/
            // plant. On non-farmable ground (paths, random grass, floors) stay silent (user: "somente
            // telhas úteis: terra que dá pra cavar/plantar, ou grama que dá pra foiçar/cavar").
            string ground = UsefulGroundName(frontPos);
            if (!string.IsNullOrEmpty(ground) && WorldGrid.LKBLKCFOEPA(frontPos)) parts.Add(ground.ToLowerInvariant());
            try
            {
                var fs = WorldGrid.MMIIIKBJKBA<FertileSoil>(frontPos);
                if (fs != null && fs.plantedCropSetter != null) parts.Add("planta");
            }
            catch { }
            string obj = NearestFrontObjectName(frontPos);
            if (!string.IsNullOrEmpty(obj)) parts.Add(obj);
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // Nearest interactable object (tree / rock-mineral / harvestable) to the tile ahead.
        private string NearestFrontObjectName(Vector3 frontPos)
        {
            float best = FrontObjectRadius; string name = null;
            foreach (var t in _cachedTrees)
            {
                if (t == null) continue;
                float d = Vector3.Distance(frontPos, t.gameObject.transform.position);
                if (d >= best) continue;
                best = d;
                var pl = t.GetComponent<Placeable>();
                name = pl != null ? DescribePlaceable(pl) : CleanSceneObjectName(t.gameObject.name);
            }
            foreach (var m in _cachedMiscHarvests)
            {
                if (m == null) continue;
                float d = Vector3.Distance(frontPos, m.gameObject.transform.position);
                if (d >= best) continue;
                best = d;
                name = m.harvestedItems.item != null ? ItemDisplayName(m.harvestedItems.item) : CleanSceneObjectName(m.gameObject.name);
            }
            foreach (var h in _cachedHarvestables)
            {
                if (h == null) continue;
                float d = Vector3.Distance(frontPos, h.gameObject.transform.position);
                if (d >= best) continue;
                best = d;
                name = (h.harvestedItems != null && h.harvestedItems.Length > 0 && h.harvestedItems[0].item != null)
                    ? ItemDisplayName(h.harvestedItems[0].item) : CleanSceneObjectName(h.gameObject.name);
            }
            return name;
        }

        // [56-61] The farm-tile states have NO game-provided name (confirmed via research) - the
        // game only localizes the verbs (Plantar/Cavar/Regar). So when a farming action prompt
        // shows, we name the GROUND ourselves from the tile state, instead of letting whatever
        // Placeable the proximity system focused (e.g. a foundry placed on the plot) supply the
        // name -> that was the "Fundição: Plantar" bug.
        public static string GroundStateName(Vector3 pos)
        {
            try
            {
                if (WorldGrid.MMIIIKBJKBA<FertileSoil>(pos) != null) return "Terra arada";
                if (WorldGrid.MMIIIKBJKBA<HoleInGround>(pos) != null) return "Pequeno buraco";
                if (WorldGrid.GCGNCHFNEBJ(pos, out var tile)) return TerrainName(tile);
            }
            catch { }
            return null;
        }

        private static string TerrainName(WorldTile tile)
        {
            if (tile.hasSnow) return "Neve";
            var gt = tile.groundType;
            if (gt.HasFlag(GroundType.TilledEarth)) return "Terra arada";
            if (gt.HasFlag(GroundType.Grass)) return "Grama";
            if (gt.HasFlag(GroundType.Sand)) return "Areia";
            if (gt.HasFlag(GroundType.Stone)) return "Pedra";
            if (gt.HasFlag(GroundType.Floor))
                return tile.materialType == MaterialType.Wood ? "Piso de madeira"
                    : tile.materialType == MaterialType.Stone ? "Piso de pedra" : "Piso";
            if (gt.HasFlag(GroundType.Ground)) return "Terra";
            return null;
        }

        // [55] Announce the resource/tree/animal that is ON the player's CURRENT tile - one per
        // step, only the one matching the tile you're standing on (not a radius sweep). Gated on
        // tile change so it's cheap (no per-frame scanning) and has no lag.
        private void HandleNearbyResourceAnnouncement()
        {
            // User (round 158): every resource must be read when walking, WITH OR WITHOUT a tool
            // ("tudo deve falar independente da ferramenta"). So this no longer bails out for a
            // held tool. The ONE thing that must stay quiet while a ground tool is in hand is plain
            // weeds/grass (Herb harvestables) - that was the old "erva alta em cima de grama, dá pra
            // cavar" noise; crops/trees/rocks are useful and always announce.
            bool groundTool = IsGroundTool(ClassifyHeldTool());

            Vector3 pos = PlayerController.GetPlayerPosition(1);
            var tile = new Vector2Int(Mathf.RoundToInt(pos.x / TileSize), Mathf.RoundToInt(pos.y / TileSize));
            if (tile == _lastResourceTile) return;
            _lastResourceTile = tile;

            // Only consider things essentially on this tile (within ~one tile).
            float bestDist = TileSize * 1.2f;
            string bestName = null;
            void Consider(GameObject go, string name)
            {
                if (go == null || string.IsNullOrEmpty(name)) return;
                float d = Vector3.Distance(pos, go.transform.position);
                if (d < bestDist) { bestDist = d; bestName = name; }
            }

            foreach (var h in _cachedHarvestables)
            {
                if (h == null) continue;
                // Planted crops are owned by the FertileSoil loop below (tile-exact + full state).
                // Skipping them here avoids the loose ~1-tile radius announcing a crop on a
                // NEIGHBOURING tile (user: "não fala na telha q pisei, parece q fala ao redor").
                if (h.cropSetter != null) continue;
                // Plain weed/grass (Herb harvestable): the noisy "erva alta" - skip it while a
                // ground tool is in hand so it doesn't stack on the tool's own ground callout.
                if (h.herb != null && groundTool) continue;
                string nm = (h.harvestedItems != null && h.harvestedItems.Length > 0 && h.harvestedItems[0].item != null)
                    ? ItemDisplayName(h.harvestedItems[0].item) : CleanSceneObjectName(h.gameObject.name);
                Consider(h.gameObject, nm);
            }
            // Planted crops: a FertileSoil with plantedCropSetter (has NO Harvestable while growing -
            // that appears only when ripe - so this is the only source that catches growing crops).
            // TILE-EXACT: announce ONLY the crop on the tile the player is standing on, snapping both
            // to tile centres - the shared ~1-tile radius above was announcing neighbours ("ao redor").
            // State + how to harvest: dead / ready (hand vs sickle) / thirsty / growing.
            // Get the crop on the player's EXACT tile straight from the game's grid lookup (more
            // reliable than snapping a cached FertileSoil's transform - the old snap missed crops
            // whose sprite pivot is offset, so walking on them announced nothing).
            try
            {
                var fsHere = WorldGrid.MMIIIKBJKBA<FertileSoil>(pos);
                if (fsHere != null && fsHere.plantedCropSetter != null)
                {
                    var cs = fsHere.plantedCropSetter;
                    string cn = CropName(cs) ?? "planta";
                    string nm;
                    switch (CropStateLabel(cs, fsHere.daysUntilDry))
                    {
                        case "Planta morta": nm = $"{cn} morto"; break;
                        case "Planta pronta pra colher":
                            nm = CropHandHarvest(cs) ? $"{cn} pronto, Control Enter pra colher"
                                                     : $"{cn} pronto, colha com a foice"; break;
                        case "Planta com sede": nm = $"{cn} com sede, precisa de água"; break;
                        default: nm = cn; break;
                    }
                    Consider(fsHere.gameObject, nm);
                }
            }
            catch { }
            foreach (var m in _cachedMiscHarvests)
            {
                if (m == null) continue;
                string nm = m.harvestedItems.item != null ? ItemDisplayName(m.harvestedItems.item) : CleanSceneObjectName(m.gameObject.name);
                Consider(m.gameObject, nm);
            }
            foreach (var t in _cachedTrees)
            {
                if (t == null) continue;
                bool chopped = false; try { chopped = t.HasBeenChopped(); } catch { }
                if (chopped) continue;
                string nm = DroppedName(t.droppedItems != null && t.droppedItems.Length > 0 ? t.droppedItems[0].item : null)
                    ?? CleanSceneObjectName(t.gameObject.name);
                Consider(t.gameObject, string.IsNullOrEmpty(nm) ? "Árvore" : $"Árvore, {nm}");
            }
            // Rocks/ore incl. coal (Rock objects, NOT Harvestable) - user wants every resource read
            // when walking, tool-free (tutorial "minere 5 carvão"). Named by what they drop.
            foreach (var rock in _cachedRocks)
            {
                if (rock == null) continue;
                string nm = DroppedName(rock.droppedItems != null && rock.droppedItems.Length > 0 ? rock.droppedItems[0].item : null)
                    ?? CleanSceneObjectName(rock.gameObject.name);
                Consider(rock.gameObject, string.IsNullOrEmpty(nm) ? "Pedra" : nm);
            }
            foreach (var a in _cachedAnimals)
            {
                if (a == null) continue;
                Consider(a.gameObject, DescribeNpc(a));
            }

            if (bestName != null)
            {
                ScreenReader.Say(bestName, interrupt: false);
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Tile resource announced \"{bestName}\" dist={bestDist:F1}");
            }
        }

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

        // The full target list is built from ~10 FindObjectsOfType full-scene scans, so rebuilding it
        // on EVERY Page Up/Down was a big lag (user: "categorias muito lag"). Cache it and rebuild
        // ONLY when the world could have changed: a farming/chop action (_targetDirty via OnActionDone)
        // or the player walked far enough that nearby objects differ. A time window fails for slow
        // reading (each press >window -> rebuild), so cache by POSITION+dirty instead - navigating
        // while standing still is then always instant.
        private bool _targetDirty = true;
        private Vector3 _targetCacheCenter = new Vector3(99999f, 99999f, 0f);
        private List<(string name, Vector3 position, string category)> _targetCache;
        private List<(string name, Vector3 position, string category)> BuildTargetList()
        {
            Vector3 pp = PlayerController.GetPlayerPosition(1);
            // Once the player walks away from the spot they just reached, stop hiding it (the
            // resource may still be there, or a new one spawned) - and force a rebuild so it returns.
            if (_recentlyReachedPos.HasValue && Vector3.Distance(pp, _recentlyReachedPos.Value) > ReachedClearDist)
            {
                _recentlyReachedPos = null;
                _targetDirty = true;
            }
            if (_targetCache != null && !_targetDirty && Vector3.Distance(pp, _targetCacheCenter) < 3f)
                return _targetCache;
            float t0 = Main.DebugMode ? Time.realtimeSinceStartup : 0f;
            _targetCache = BuildTargetListUncached();
            _targetCacheCenter = pp;
            _targetDirty = false;
            if (Main.DebugMode) DebugLogger.LogState($"BuildTargetList rebuilt in {(Time.realtimeSinceStartup - t0) * 1000f:F1}ms, {_targetCache.Count} items");
            return _targetCache;
        }

        // Called when the guide reports arrival. Hides that spot from the nav list until the player
        // walks away (ReachedClearDist), so the NEXT tracked resource is a different one and a
        // just-broken rock doesn't keep being offered.
        private void MarkReached(Vector3 pos)
        {
            _recentlyReachedPos = pos;
            _targetDirty = true;
        }

        // True if a world position is on the spot the player just reached (and hasn't left yet).
        private bool IsRecentlyReached(Vector3 pos)
            => _recentlyReachedPos.HasValue && Vector3.Distance(pos, _recentlyReachedPos.Value) < ReachedSkipRadius;

        // FindObjectsByType(None) skips the InstanceID sort FindObjectsOfType does - much cheaper for
        // the one-off scans below (order doesn't matter here; the list is sorted by distance later).
        private static T[] FindAll<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsSortMode.None);

        private List<(string name, Vector3 position, string category)> BuildTargetListUncached()
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
            // In the city, list every door/passage across the whole city (not just nearby ones).
            float doorRadius = IsCityLocation(playerLocation) ? CityWideDoorRadius : NearbyDoorRadius;
            foreach (var door in (_cachedDoors ?? FindAll<Door>()))
            {
                if (door == null || door == _rememberedEntranceDoor) continue;
                if (Vector3.Distance(playerPos, door.transform.position) > doorRadius) continue;
                list.Add((DescribeDoor(door), GetDoorWalkablePosition(door, playerPos), "Portas"));
            }

            // Round 107: area exits between locations (cellar<->tavern, etc.) are TravelZone
            // components, NOT Door components - confirmed in the log the cellar exit is
            // "TravelZone-CellarToTavern" and so never appeared in the door list. List nearby ones
            // under "Portas" too (they're passages). lookDirection/playerPosition aside, the zone's
            // own transform is where the player walks into it.
            foreach (var zone in FindAll<TravelZone>())
            {
                if (zone == null) continue;
                if (Vector3.Distance(playerPos, zone.transform.position) > doorRadius) continue;
                // Route to a WALKABLE approach point, not the zone's raw center. Confirmed in
                // the city log: "Passagem para a taverna da cidade" got "sem rota ainda" every
                // frame (A* never reached the goal) and the straight-line fallback walked the
                // player into a wall - the trigger's center sits inside non-walkable geometry.
                // GetApproachPosition nudges the goal to just outside the zone's collider on
                // the player's side, which A* can actually reach.
                list.Add((DescribeTravelZone(zone), GetTravelZoneApproach(zone, playerPos), "Portas"));
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

            // User's explicit request (rodada 134f): NPCs (townspeople, the cat, etc.) as
            // navigable targets under their own "NPCs" category, named as the game names them
            // ("os npc estão todos sem nome, quero os nomeado de acordo com o q o jogo diz").
            // They all share the NPC base: DialogueNPCBase for dialogue characters (which
            // carries actorName/characterName), CatNPC for the cat (named by GameObject name).
            foreach (var npc in FindAll<NPC>())
            {
                if (npc == null) continue;
                // Skip ambient/utility "NPCs" (door openers, buzzing flies, movers) that aren't
                // real characters - same ones DialogueAnnouncer filters out of narration.
                string rawNpc = npc.gameObject.name;
                if (rawNpc.Contains("Door") || rawNpc.Contains("Buzz") || rawNpc.Contains("Mudanza")) continue;
                string npcName = DescribeNpc(npc);
                if (string.IsNullOrEmpty(npcName)) continue;
                // Woody (Serraria) and Petra (Ferraria) live INSIDE their buildings. Only track them
                // when the player is actually in that building - outside, their route was "buga"
                // (user: "só deve ser rastreado na categoria se estiver dentro da serraria/forja").
                if (npcName == "Woody" && playerLocation != Location.Sawmill) continue;
                if (npcName == "Petra" && playerLocation != Location.Blacksmith) continue;
                // Amos (city-tavern trader) and Persa (petshop trader) likewise live INSIDE their
                // building - only track them there, or from other city spots they showed with the
                // wrong/looping routes (Amos was appearing inside the blacksmith).
                if (npcName == "Amos" && playerLocation != Location.CityTavern) continue;
                if (npcName == "Persa" && playerLocation != Location.PetShop) continue;
                bool isMerchant = MerchantWares.TryGetValue(npcName, out var wares);
                // Merchants get a WIDER radius than normal NPCs so all merchants of the current AREA
                // show together (city merchants in the city, Holly/Bob near the farm) - but NOT the
                // whole map, which listed cross-area merchants with broken long routes (user: "tem
                // alguns da cidade q aparecem fora... rotas completamente loucas").
                float radius = isMerchant ? MerchantRadius : NearbyDoorRadius;
                if (Vector3.Distance(playerPos, npc.transform.position) > radius) continue;
                // Merchants go in their OWN "Comerciantes" category (user request) with what they sell.
                if (isMerchant)
                {
                    list.Add(($"{npcName}, {wares}", GetApproachPosition(npc.gameObject, playerPos), "Comerciantes"));
                }
                else
                {
                    list.Add((npcName, GetApproachPosition(npc.gameObject, playerPos), "NPCs"));
                }
            }

            // User's explicit request: all items nearby too, same "by proximity" rule as
            // doors.
            foreach (var placeable in (_cachedAllPlaceables ?? FindAll<Placeable>()))
            {
                if (placeable == null || Vector3.Distance(playerPos, placeable.transform.position) > NearbyDoorRadius) continue;

                // Confirmed in the test log (real bug, not a guess): the player's bed is ALSO
                // a Placeable, so this loop added it a SECOND time as "Cama do jogador" (its
                // localized name) pointing at GetApproachPosition - the bed's EDGE, not the
                // sleepCollider center where the "Quer dormir?" prompt actually fires. The
                // user picked that wrong entry, so the route led beside the bed and the sleep
                // prompt never triggered. Skip the bed here; the dedicated "Cama" entry above
                // already lists it with the correct sleepCollider target.
                if (Bed.instance != null && placeable == Bed.instance.placeable) continue;

                // Confirmed live: at least one Placeable ("BarManager") has no visual
                // representation at all - a manager script, not a real physical object -
                // and was showing up in the list as a confusing, meaningless entry. Skip
                // anything with nothing to actually look at.
                if (placeable.GetComponent<SpriteRenderer>() == null) continue;

                string pName = DescribePlaceable(placeable);
                string broken = CrafterBrokenSuffix(placeable);
                if (!string.IsNullOrEmpty(broken)) pName += broken;
                list.Add((pName, GetApproachPosition(placeable.gameObject, playerPos), CategorizePlaceable(placeable)));
            }

            // Round 112: the food prep table (NinjaPreparationTable) is its OWN MonoBehaviour, not
            // necessarily a Placeable, so the loop above may miss it - scan it directly and list it
            // under "Máquinas" (user: "essa mesa de menus não está aparecendo em maquinas"). Dedup
            // at the end collapses it if it was also caught as a Placeable.
            foreach (var prep in FindAll<NinjaPreparationTable>())
            {
                if (prep == null || Vector3.Distance(playerPos, prep.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Mesa de preparação", GetApproachPosition(prep.gameObject, playerPos), "Máquinas"));
            }

            // Well: IInteractable/IHoverable/IProximity, not a Placeable - scan separately.
            // User: "o poço deve aparecer em categoria de máquinas".
            foreach (var well in (_cachedWells ?? FindAll<Well>()))
            {
                if (well == null || Vector3.Distance(playerPos, well.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Poço", GetApproachPosition(well.gameObject, playerPos), "Máquinas"));
            }

            // Water source ("a fonte de onde tira a água que enche a garrafa"): BottleTrigger is an
            // IInteractable whose OnHover shows "Collect water" and MouseUp does ActionDone.FillWaterBottle
            // (empty bottle -> full bottle). User wants it findable under "Materiais".
            foreach (var bottle in FindAll<BottleTrigger>())
            {
                if (bottle == null || Vector3.Distance(playerPos, bottle.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Fonte de água", GetApproachPosition(bottle.gameObject, playerPos), "Materiais"));
            }

            // Mine/cave torch puzzle: the torches the player must find + light (TorchInteractable,
            // IInteractable) didn't show in any category (user: "na caverna preciso encontrar uma
            // tocha q nem aparece na lista"). Scan them so they're navigable like the well.
            foreach (var torch in FindAll<TorchInteractable>())
            {
                if (torch == null) continue;
                list.Add(("Tocha", GetApproachPosition(torch.gameObject, playerPos), "Máquinas"));
            }
            // The innkeeper cave quest object ("Acenda a tocha de Rygar") is an InnkeeperCaveManager
            // (IInteractable), NOT a TorchInteractable, so it didn't show up (user: "a tocha que
            // encontrei não era mostrada... deve estar em item de missão"). List it under Pendentes.
            foreach (var cave in FindAll<InnkeeperCaveManager>())
            {
                if (cave == null) continue;
                list.Add(("Tocha de Rygar", GetApproachPosition(cave.gameObject, playerPos), "Pendentes"));
            }
            // Hot-bath event ("Derramar água" / PourWater): a GameEvent + IInteractable the user must
            // act on (fill the bath) - belongs under Pendentes like the cave torch (user: "isso deve
            // aparecer na categoria pendente").
            foreach (var bath in FindAll<HotBathEvent>())
            {
                // User: "banho quente fica aparecendo fora da pedreira e com a missão concluída".
                // Only list it while the event is actually live (GameEvent.isActive && !isDone) AND
                // the player is near it - it had no distance or state filter, so it showed always.
                if (bath == null || !bath.isActive || bath.isDone) continue;
                if (Vector3.Distance(playerPos, bath.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Banho quente", GetApproachPosition(bath.gameObject, playerPos), "Pendentes"));
            }

            // Interactable forms (castle "Preencher" forms) - user wants the ones still to fill in
            // "Pendentes". ICNNAEDJNLH is true once a form is made/filled, so skip those.
            foreach (var form in FindAll<InteractableForm>())
            {
                if (form == null || Vector3.Distance(playerPos, form.transform.position) > NearbyDoorRadius) continue;
                bool made = false;
                try { made = form.ICNNAEDJNLH; } catch { }
                if (made) continue;
                list.Add(("Formulário para preencher", GetApproachPosition(form.gameObject, playerPos), "Pendentes"));
            }

            // Serving drinks (banquet competition AND the tavern): customers waiting with an order
            // (CustomerBase.currentRequest = the drink ItemInstance) go under "Servir", so the blind
            // player can find each one and hear what to bring. Round 246: first piece of the
            // drink-serving system (user: "inicie... o mesmo sistema pra servir drinks na taverna").
            AddServingCustomers(list, playerPos);
            // Mailbox (user: "caixa de correio não está em maquinas"). PostBox is IInteractable, not a
            // Placeable in the nav's usual scan, so add it here like the well.
            foreach (var pbx in FindAll<PostBox>())
            {
                if (pbx == null || Vector3.Distance(playerPos, pbx.transform.position) > NearbyDoorRadius) continue;
                list.Add(("Caixa de correio", GetApproachPosition(pbx.gameObject, playerPos), "Máquinas"));
            }

            // Harvestable resources (trees, herbs, crops) and MiscellaneousHarvest (stones, minerals,
            // misc pickups) - user: "arvores deve aparecer em categoria materiais, assim como pedras,
            // ou qualquer outro minerio".
            foreach (var harv in (_cachedHarvestables ?? FindAll<Harvestable>()))
            {
                if (harv == null || Vector3.Distance(playerPos, harv.transform.position) > NearbyDoorRadius) continue;
                if (IsRecentlyReached(harv.transform.position)) continue;
                // Planted crops (trigo, chá...) are Harvestables under a CropSetter - they belong in
                // "Cultivo" as "Planta"/crop name, NOT in "Materiais" (user: "não quero plantas que eu
                // plantei em materiais"). Skip them here.
                if (harv.GetComponentInParent<CropSetter>() != null) continue;
                string harvName = null;
                if (harv.harvestedItems != null && harv.harvestedItems.Length > 0 && harv.harvestedItems[0].item != null)
                    harvName = ItemDisplayName(harv.harvestedItems[0].item);
                // Last resort: the harvestable's own cleaned GameObject name (e.g. "2001 - Roble"
                // -> "Roble"/"Carvalho") instead of the generic "Recurso".
                if (string.IsNullOrEmpty(harvName)) harvName = CleanSceneObjectName(harv.gameObject.name);
                if (string.IsNullOrEmpty(harvName)) harvName = "Recurso";
                list.Add((harvName, GetApproachPosition(harv.gameObject, playerPos), "Materiais"));
            }
            foreach (var misc in (_cachedMiscHarvests ?? FindAll<MiscellaneousHarvest>()))
            {
                if (misc == null || Vector3.Distance(playerPos, misc.transform.position) > NearbyDoorRadius) continue;
                if (IsRecentlyReached(misc.transform.position)) continue;
                string miscName = null;
                if (misc.harvestedItems.item != null)
                    miscName = ItemDisplayName(misc.harvestedItems.item);
                if (string.IsNullOrEmpty(miscName)) miscName = CleanSceneObjectName(misc.gameObject.name);
                if (string.IsNullOrEmpty(miscName)) miscName = "Recurso";
                list.Add((miscName, GetApproachPosition(misc.gameObject, playerPos), "Materiais"));
            }

            // Trees (axe) and rocks/ore incl. coal (pickaxe) - user: "carvões e outros minerais não
            // aparecem". These are Tree/Rock objects (NOT Harvestable/MiscellaneousHarvest), so the
            // loops above missed them. Approach + face + F chops/mines them (the tool auto-walks).
            foreach (var tree in (_cachedTrees ?? FindAll<Tree>()))
            {
                if (tree == null || Vector3.Distance(playerPos, tree.transform.position) > NearbyDoorRadius) continue;
                if (IsRecentlyReached(tree.transform.position)) continue;
                // Skip a tree already felled (user: "peguei uma árvore e ele ainda aponta pra ela").
                bool chopped = false; try { chopped = tree.HasBeenChopped(); } catch { }
                if (chopped) continue;
                string nm = DroppedName(tree.droppedItems != null && tree.droppedItems.Length > 0 ? tree.droppedItems[0].item : null)
                    ?? CleanSceneObjectName(tree.gameObject.name);
                if (string.IsNullOrEmpty(nm)) nm = "Árvore";
                list.Add(($"Árvore, {nm}", GetApproachPosition(tree.gameObject, playerPos), "Materiais"));
            }
            // Round 235: reuse the shared cache (refreshed on area change / mining action / 60s)
            // instead of a fresh full-scene scan on every Page Up/Down - the nav-list rebuild was
            // doing several of these ~42ms scans per keypress.
            foreach (var rock in _cachedRocks)
            {
                if (rock == null || Vector3.Distance(playerPos, rock.transform.position) > NearbyDoorRadius) continue;
                if (IsRecentlyReached(rock.transform.position)) continue;   // just mined it - don't re-offer
                // Name a rock by what it drops (coal/stone/ore) so "Carvão" shows for the coal step.
                string nm = DroppedName(rock.droppedItems != null && rock.droppedItems.Length > 0 ? rock.droppedItems[0].item : null)
                    ?? CleanSceneObjectName(rock.gameObject.name);
                if (string.IsNullOrEmpty(nm)) nm = "Pedra";
                // User's request: know the pickaxe LEVEL a rock needs before walking to it, so you
                // don't reach one your tool can't break. Rock.toolLevelRequired is the tier.
                int lvl = 1;
                try { lvl = rock.toolLevelRequired; } catch { }
                if (lvl > 1) nm += $", nível {lvl}";
                list.Add((nm, GetApproachPosition(rock.gameObject, playerPos), "Materiais"));
            }

            // "Animais" (user: "animais não têm sua categoria, devem ter"): chickens/cows/etc.
            foreach (var animal in _cachedAnimals)
            {
                if (animal == null || Vector3.Distance(playerPos, animal.transform.position) > NearbyDoorRadius) continue;
                string an = CleanSceneObjectName(animal.gameObject.name);
                if (string.IsNullOrEmpty(an)) an = "Animal";
                list.Add((an, GetApproachPosition(animal.gameObject, playerPos), "Animais"));
            }

            // "Cultivo": tilled/planted tiles the player wants to find (plant/water/harvest). Each
            // tilled tile is a FertileSoil MonoBehaviour placed when you hoe the ground. Empty bed
            // (plantedCropSetter == null) -> "Terra arada"; with a crop -> "Planta". The per-type
            // collapse below turns these into one entry per kind with a count, guiding to the nearest
            // and then the next nearest. Rebuilt fresh each time the list is opened, so it updates as
            // tiles are tilled/planted/cleared.
            // No distance filter here (unlike Materiais): the player wants to find ALL their farm
            // tiles, even the far ones (user: "plantei 9, só mostram 7" - the far/planted ones were
            // dropped). FertileSoil only exists where they tilled, so listing all is fine. State is
            // spelled out so a screen-reader player knows what to do: dry soil needs watering before
            // planting; watered soil is ready to plant.
            foreach (var fs in _cachedFertileSoils)   // round 235: cache reuse (see rock loop above)
            {
                if (fs == null) continue;
                string fsName;
                if (fs.plantedCropSetter != null)
                    fsName = CropStateLabel(fs.plantedCropSetter, fs.daysUntilDry);   // morta/pronta/com sede/planta
                else fsName = fs.daysUntilDry > 1 ? "Terra arada molhada" : "Terra arada seca";
                list.Add((fsName, GetApproachPosition(fs.gameObject, playerPos), "Cultivo"));
            }

            // RAW arable tiles (no object exists for them - scan the 0.5 grid). "Pra arar" = a tile
            // the hoe would till right now (the game's own predicate, incl. the miolo/neighbour +
            // weed rules); "Pra cavar" = farmable grass to spade into bare ground first. The per-type
            // collapse turns these into one guided entry each with a count.
            ScanArableTiles(playerPos, list);

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
            var nearbyDirt = FindAll<FloorDirt>()
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
            var nearbySeats = _cachedSeats   // round 235: cache reuse (see rock loop above)
                .Where(s => s != null && s.table == null && !(s.placeable != null && s.placeable.gameObject == heldObjectForList) && Vector3.Distance(playerPos, s.transform.position) <= NearbyDoorRadius)
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
                // "Cultivo" crops are exempt: two crops on adjacent tiles can share an approach
                // position and would be wrongly merged, undercounting plantas (zone said 9, category
                // 8). Each crop is a distinct harvestable; the grouping step below collapses them
                // into one "(N perto)" entry anyway, so keeping them all here just fixes the count.
                bool isDuplicate = entry.category != "Cultivo"
                    && deduped.Any(d => d.category == entry.category && Vector3.Distance(d.position, entry.position) < TileSize);
                if (!isDuplicate) deduped.Add(entry);
            }

            // User [79][80][81]: resources/collectibles are too many to navigate one by one.
            // Collapse each TYPE (same name) in Materiais/Coletáveis into a SINGLE entry placed
            // at the NEAREST instance, with a count ("Carvalho (5 perto)"). Tracking then routes
            // to the nearest; after you collect it, the next rebuild picks the next nearest.
            var grouped = new List<(string name, Vector3 position, string category)>();
            var resourceGroups = new Dictionary<string, (Vector3 pos, float dist, int count)>();
            var groupOrder = new List<string>();
            foreach (var entry in deduped)
            {
                if (entry.category == "Materiais" || entry.category == "Coletáveis" || entry.category == "Cultivo")
                {
                    string key = entry.category + "|" + entry.name;
                    float d = Vector3.Distance(playerPos, entry.position);
                    if (!resourceGroups.TryGetValue(key, out var g)) { resourceGroups[key] = (entry.position, d, 1); groupOrder.Add(key); }
                    else resourceGroups[key] = (d < g.dist ? entry.position : g.pos, Mathf.Min(d, g.dist), g.count + 1);
                }
                else grouped.Add(entry);
            }
            foreach (var key in groupOrder)
            {
                var g = resourceGroups[key];
                int sep = key.IndexOf('|');
                string cat = key.Substring(0, sep);
                string nm = key.Substring(sep + 1);
                string label = g.count > 1 ? $"{nm} ({g.count} perto)" : nm;
                grouped.Add((label, g.pos, cat));
            }

            // User: unique objects (aging barrels, chests, forms - anything NOT collapsed as a
            // resource) can share the same name; give each a stable ID so it's individually
            // trackable ("Barril de envelhecimento 1, 2, 3..."). Number by position so the same
            // physical object keeps the same number across rebuilds.
            var nameCounts = new Dictionary<string, int>();
            foreach (var e in grouped) { nameCounts.TryGetValue(e.name, out int c); nameCounts[e.name] = c + 1; }
            for (int i = 0; i < grouped.Count; i++)
            {
                var e = grouped[i];
                if (nameCounts.TryGetValue(e.name, out int cnt) && cnt > 1)
                {
                    int idx = 1;
                    foreach (var other in grouped)
                        if (other.name == e.name && (other.position.x < e.position.x
                            || (other.position.x == e.position.x && other.position.y < e.position.y)))
                            idx++;
                    grouped[i] = ($"{e.name} {idx}", e.position, e.category);
                }
            }
            return grouped;
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
                var newlyFound = FindAll<Seat>()
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
                var newlyFound = FindAll<Table>()
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
            foreach (var seat in FindAll<Seat>())
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
            foreach (var table in FindAll<Table>())
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
            foreach (var table in FindAll<Table>())
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
            foreach (var surface in FindAll<SurfaceSortOrder>())
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
            foreach (var surface in FindAll<SurfaceSortOrder>())
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
            foreach (var surface in FindAll<SurfaceSortOrder>())
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
            var wallItems = FindAll<Placeable>()
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

        // For a bench/seat: the computed seat-slot position is often REFUSED by the game at Deselect
        // even with canBePlaced=True - the real gate is IsObjectInValidLocation(TRUE) (round 99), and
        // it comes back False on some slots (confirmed: 2nd bench, both table sides). Search a SMALL
        // grid around the slot target for the nearest spot where the game actually accepts it, so the
        // bench still lands at the table's seat but on a tile the game allows. Only checks
        // IsObjectInValidLocation(true) (tile-based, safe to test synchronously) - canBePlaced is
        // physics/frame-delayed and is validated later by the settle-retry.
        public static Vector3? FindNearbyValidPlacement(Placeable placeable, Vector3 aroundPos, float maxDistance, Seat associateSeat = null)
        {
            if (placeable == null) return null;
            Vector3 original = placeable.transform.position;
            Vector3 seatOriginal = associateSeat != null ? associateSeat.transform.position : Vector3.zero;
            const float step = 0.25f;
            int range = Mathf.CeilToInt(maxDistance / step);
            var candidates = new System.Collections.Generic.List<Vector3>();
            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                {
                    Vector3 c = new Vector3(aroundPos.x + dx * step, aroundPos.y + dy * step, original.z);
                    if (Vector3.Distance(aroundPos, c) <= maxDistance) candidates.Add(c);
                }
            candidates.Sort((a, b) => Vector3.Distance(aroundPos, a).CompareTo(Vector3.Distance(aroundPos, b)));
            // Two passes: first prefer a tile that is BOTH game-valid AND associates the seat with a
            // table (so the bench counts for the "assentos" objective - user: "colocou mas não
            // associou"); if none associates, fall back to the nearest merely-valid tile so it at
            // least places.
            Vector3? bestBoth = null, bestValid = null;
            foreach (var c in candidates)
            {
                placeable.transform.position = c;
                if (associateSeat != null) associateSeat.transform.position = c;
                Physics2D.SyncTransforms();
                if (!placeable.IsObjectInValidLocation(true)) continue;
                if (!bestValid.HasValue) bestValid = c;
                if (associateSeat != null)
                {
                    try { associateSeat.GetNeighbourTable(); } catch { }
                    if (associateSeat.table != null) { bestBoth = c; break; }
                }
                else { bestBoth = c; break; }
            }
            placeable.transform.position = original;
            if (associateSeat != null) associateSeat.transform.position = seatOriginal;
            Physics2D.SyncTransforms();
            Vector3? best = bestBoth ?? bestValid;
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: FindNearbyValidPlacement around {aroundPos} -> {(best.HasValue ? best.Value.ToString() : "none")} (both={(bestBoth.HasValue ? "y" : "n")} validOnly={(bestValid.HasValue ? "y" : "n")}, {candidates.Count} candidates)");
            return best;
        }

        // Like FindNearbyValidPlacement but returns ONLY a position that both is game-valid AND
        // makes the seat associate with a table (never the valid-but-unassociating fallback). Used by
        // the Alt+M auto-arranger to decide whether a bench can actually be seated here at all - if
        // this returns null, moving the bench would just scatter it (game-valid but table=null, which
        // is exactly what the crowded big-bench case produced). Returns null = "won't seat here".
        public static Vector3? FindAssociatingPlacement(Placeable placeable, Vector3 aroundPos, float maxDistance, Seat associateSeat)
        {
            if (placeable == null || associateSeat == null) return null;
            Vector3 original = placeable.transform.position;
            Vector3 seatOriginal = associateSeat.transform.position;
            const float step = 0.25f;
            int range = Mathf.CeilToInt(maxDistance / step);
            var candidates = new System.Collections.Generic.List<Vector3>();
            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                {
                    Vector3 c = new Vector3(aroundPos.x + dx * step, aroundPos.y + dy * step, original.z);
                    if (Vector3.Distance(aroundPos, c) <= maxDistance) candidates.Add(c);
                }
            candidates.Sort((a, b) => Vector3.Distance(aroundPos, a).CompareTo(Vector3.Distance(aroundPos, b)));
            Vector3? best = null;
            foreach (var c in candidates)
            {
                placeable.transform.position = c;
                associateSeat.transform.position = c;
                Physics2D.SyncTransforms();
                if (!placeable.IsObjectInValidLocation(true)) continue;
                try { associateSeat.GetNeighbourTable(); } catch { }
                if (associateSeat.table != null) { best = c; break; }
            }
            placeable.transform.position = original;
            associateSeat.transform.position = seatOriginal;
            Physics2D.SyncTransforms();
            return best;
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
                foreach (var table in FindAll<Table>())
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
        // FindAll<Table>() AND <Seat>() directly EVERY call - given the
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
            _staticCachedTables = FindAll<Table>();
            _staticCachedSeats = FindAll<Seat>();
        }

        // All empty seat slots across every table, nearest-first to `position`. Same emptiness test
        // as FindNearestEmptySlot (no non-held Seat within 0.3u). Used by the Alt+M auto-arranger to
        // try EVERY free slot for a bench (not just the closest), so a big bench that can't fit the
        // nearest/crowded slot still gets a chance on a less crowded side/table.
        public static System.Collections.Generic.List<(SeatingGroup slot, Table table)> GetAllEmptySlots(Vector3 position, float maxDistance)
        {
            RefreshStaticSceneCache();
            GameObject heldNow = SelectObject.GetPlayer(1)?.selectedGameObject;
            var result = new System.Collections.Generic.List<(SeatingGroup slot, Table table, float dist)>();
            foreach (var table in _staticCachedTables)
            {
                if (table == null) continue;
                var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
                if (groups == null) continue;
                foreach (var group in groups)
                {
                    if (group == null || group.transform == null) continue;
                    float dist = Vector3.Distance(position, group.transform.position);
                    if (dist > maxDistance) continue;
                    bool occupied = false;
                    foreach (var seat in _staticCachedSeats)
                    {
                        if (seat == null) continue;
                        if (seat.placeable != null && seat.placeable.gameObject == heldNow) continue;
                        if (Vector3.Distance(seat.transform.position, group.transform.position) < 0.3f) { occupied = true; break; }
                    }
                    if (!occupied) result.Add((group, table, dist));
                }
            }
            result.Sort((a, b) => a.dist.CompareTo(b.dist));
            var slots = new System.Collections.Generic.List<(SeatingGroup slot, Table table)>();
            foreach (var r in result) slots.Add((r.slot, r.table));
            return slots;
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

        // Round 225: table-placement guard. The user (blind) can't see which side of a table is
        // against a wall, so a table placed too close leaves seat slots with no valid floor - a
        // bench then refuses to place/associate there ("none valid"). Counts, for a just-placed
        // Table, how many of its seat slots land on a real floor tile vs. off-floor (wall/void).
        // Uses the game's own tile-validity check (WorldGrid.LKBLKCFOEPA, the same one that gates
        // the front-tile ground announcement) at each slot's computed bench position - no bench
        // Placeable is needed, so it works at table-placement time. Returns (total, blocked).
        public static (int total, int blocked) CountBlockedSeatSlots(Table table)
        {
            if (table == null) return (0, 0);
            var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
            if (groups == null) return (0, 0);
            int total = 0, blocked = 0;
            foreach (var group in groups)
            {
                if (group == null || group.transform == null) continue;
                total++;
                Vector3 seatPos = GetSeatTargetPosition(group, table);
                bool floorOk = false;
                try { floorOk = WorldGrid.LKBLKCFOEPA(seatPos); } catch { }
                if (!floorOk) blocked++;
            }
            return (total, blocked);
        }

        // Like CountBlockedSeatSlots but for a POPULATED table: a slot with a bench already on it reads
        // "no floor" from LKBLKCFOEPA (the bench occupies the tile), so CountBlockedSeatSlots wrongly
        // counts every OCCUPIED slot as "blocked". This separates the three real states so the arrange
        // report is honest: occupied (has a bench), freeUsable (empty, floor OK), freeBlocked (empty,
        // no floor - the only real "blocked by wall" case the user cares about). Occupancy = a non-held
        // Seat within 0.3u of the slot marker (same test the rest of the arranger uses).
        public static (int total, int occupied, int freeUsable, int freeBlocked) CountSlotStates(Table table, Seat[] seats)
        {
            if (table == null) return (0, 0, 0, 0);
            var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
            if (groups == null) return (0, 0, 0, 0);
            GameObject heldNow = SelectObject.GetPlayer(1)?.selectedGameObject;
            int total = 0, occupied = 0, freeUsable = 0, freeBlocked = 0;
            foreach (var group in groups)
            {
                if (group == null || group.transform == null) continue;
                total++;
                bool occ = false;
                if (seats != null)
                {
                    foreach (var s in seats)
                    {
                        if (s == null || s.transform == null) continue;
                        if (s.placeable != null && s.placeable.gameObject == heldNow) continue;
                        if (Vector3.Distance(s.transform.position, group.transform.position) < 0.3f) { occ = true; break; }
                    }
                }
                if (occ) { occupied++; continue; }
                Vector3 seatPos = GetSeatTargetPosition(group, table);
                bool floorOk = false;
                try { floorOk = WorldGrid.LKBLKCFOEPA(seatPos); } catch { }
                if (floorOk) freeUsable++; else freeBlocked++;
            }
            return (total, occupied, freeUsable, freeBlocked);
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

            // Mine/quarry fix (confirmed in the PERF log: every route to "Minério de ferro (N perto)"
            // came back "no route", only trivially succeeding at 1 waypoint when the goal got nudged
            // onto the player): the ore sits in a cluster of OTHER rocks, so the naive "just outside
            // the collider toward the player" point lands on another unwalkable rock. If the point
            // isn't a walkable tile, search the tiles around the target (nearest ring first) for the
            // walkable one closest to the player - that's a tile A* can actually reach.
            if (!IsWalkableApproach(result, collider))
            {
                Vector3? better = FindWalkableApproach(center, collider, playerPos, reach);
                if (better.HasValue) result = better.Value;
            }

            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: GetApproachPosition for \"{target.name}\" -> center={center} result={result}");
            return result;
        }

        // A tile is a usable approach iff it's a node the pathfinder can actually stand on. This is
        // the EXACT test the game A* uses (WorldGrid.DLFFCGLGDLL reads PathNode.isFree from
        // PathNodesManager.pathNodes, keyed by the grid-snapped position; a position that isn't a key,
        // or whose node isn't free, is treated as blocked). Round 231-232 wrongly used
        // WorldGrid.LKBLKCFOEPA + a Physics2D overlap, which disagreed with the pathfinder (so the
        // mine passage's non-free trigger tile "looked" walkable and the snap never moved) AND the
        // overlap call was the lag the user reported. This match is a cheap dictionary lookup.
        private static bool IsWalkableApproach(Vector3 pos, Collider2D target)
        {
            try
            {
                Vector2 key = Utils.MJEACANINDN(pos);
                return PathNodesManager.pathNodes.TryGetValue(key, out var node) && node.isFree;
            }
            catch { return false; }
        }

        // Snap an arbitrary point (e.g. a TravelZone passage square, whose trigger centre often sits
        // on non-walkable geometry) to the nearest tile the pathfinder can actually stand on, biased
        // toward the player. Returns the input unchanged if it's already walkable or nothing better
        // is found. This is why the mine passage failed ("no route") while the hot-springs passage
        // (whose centre happens to be walkable) worked.
        private static Location SafeLoc(Vector3 p)
        {
            try { return Utils.HJPCBBGHPDA(p); } catch { return Location.None; }
        }

        // requireLoc: when set, the result tile must resolve to that Location. This is the real fix
        // for the mine passage: the game's A* only explores nodes whose Location == the goal's, so a
        // passage square that resolves to the DESTINATION area (Mine) is unreachable from the player's
        // area (Quarry) - "no route" no matter how walkable it looks. Snapping to a walkable tile in
        // the PLAYER's Location gives A* a goal it can actually reach; stepping onto it then triggers
        // the transition. (The hot-springs passage worked by luck - its near square resolved to the
        // player's side.)
        private static Vector3 SnapToWalkableTile(Vector3 pos, Vector3 playerPos, Collider2D exclude, Location requireLoc = Location.None)
        {
            bool Ok(Vector3 p) => IsWalkableApproach(p, exclude) && (requireLoc == Location.None || SafeLoc(p) == requireLoc);
            if (Ok(pos)) return pos;
            Vector3[] dirs =
            {
                new Vector3(1, 0), new Vector3(-1, 0), new Vector3(0, 1), new Vector3(0, -1),
                new Vector3(1, 1).normalized, new Vector3(-1, 1).normalized,
                new Vector3(1, -1).normalized, new Vector3(-1, -1).normalized,
            };
            for (int ring = 1; ring <= 8; ring++)
            {
                Vector3? best = null;
                float bestDist = float.MaxValue;
                foreach (var d in dirs)
                {
                    Vector3 cand = pos + d * (TileSize * ring);
                    if (!Ok(cand)) continue;
                    float dist = Vector3.Distance(cand, playerPos);
                    if (dist < bestDist) { bestDist = dist; best = cand; }
                }
                if (best.HasValue) return best.Value;
            }
            return pos;
        }

        private static Vector3? FindWalkableApproach(Vector3 center, Collider2D target, Vector3 playerPos, float reach)
        {
            Vector3[] dirs =
            {
                new Vector3(1, 0), new Vector3(-1, 0), new Vector3(0, 1), new Vector3(0, -1),
                new Vector3(1, 1).normalized, new Vector3(-1, 1).normalized,
                new Vector3(1, -1).normalized, new Vector3(-1, -1).normalized,
            };
            Vector3? best = null;
            float bestDist = float.MaxValue;
            for (int ring = 0; ring < 4; ring++)
            {
                foreach (var d in dirs)
                {
                    Vector3 cand = center + d * (reach + TileSize * (0.5f + ring));
                    if (!IsWalkableApproach(cand, target)) continue;
                    float dist = Vector3.Distance(cand, playerPos);
                    if (dist < bestDist) { bestDist = dist; best = cand; }
                }
                if (best.HasValue) return best; // nearest ring with any walkable tile wins
            }
            return best;
        }

        // A passage (TravelZone) has TWO trigger squares - one on THIS area's side (`position`) and
        // one on the DESTINATION area's side (`position2`). Route to the square on the PLAYER'S side
        // (whichever is in the player's current Location, else the nearest): it's a walkable tile in
        // the currently-loaded area, so the game A* can actually reach it.
        //
        // Confirmed live (quarry->mine): routing to GetPositionOnPathRequest()/GetApproachPosition
        // sent the goal ~20 units away toward the mine side (an unloaded area). The game A* rejects
        // any node whose Location != goalLocation (PathRequestManager line ~2690), and it can't reach
        // a tile in an unloaded area, so EVERY quarry passage came back "no route" and the straight-
        // line fallback walked the player into walls (user: "só becos... não contorna paredes").
        private static Vector3 GetTravelZoneApproach(TravelZone zone, Vector3 playerPos)
        {
            try
            {
                Location playerLoc = PlayerController.GetPlayer(1)?.LEOIMFNKFGA ?? Location.None;
                Vector3 p1 = zone.position;
                Vector3 p2 = zone.position2;
                bool has1 = p1 != Vector3.zero;
                bool has2 = p2 != Vector3.zero;

                var zoneCollider = zone.GetComponent<Collider2D>();
                // Force the route target onto the PLAYER's side (see SnapToWalkableTile) - the passage
                // squares can both resolve to the destination area, which A* can't reach from here.
                Location need = playerLoc;

                // Prefer the square whose Location matches where the player currently is.
                if (playerLoc != Location.None)
                {
                    bool p1Here = has1 && Utils.HJPCBBGHPDA(p1) == playerLoc;
                    bool p2Here = has2 && Utils.HJPCBBGHPDA(p2) == playerLoc;
                    if (p1Here && !p2Here) { var w = SnapToWalkableTile(p1, playerPos, zoneCollider, need); LogZoneApproach(zone, w, "loc-match p1"); return w; }
                    if (p2Here && !p1Here) { var w = SnapToWalkableTile(p2, playerPos, zoneCollider, need); LogZoneApproach(zone, w, "loc-match p2"); return w; }
                }

                // Otherwise the nearest square (the player stands on their own side, so the near one).
                if (has1 && has2)
                {
                    Vector3 near = Vector3.Distance(playerPos, p1) <= Vector3.Distance(playerPos, p2) ? p1 : p2;
                    var w = SnapToWalkableTile(near, playerPos, zoneCollider, need);
                    LogZoneApproach(zone, w, "nearest");
                    return w;
                }
                if (has1) { var w = SnapToWalkableTile(p1, playerPos, zoneCollider, need); LogZoneApproach(zone, w, "only p1"); return w; }
                if (has2) { var w = SnapToWalkableTile(p2, playerPos, zoneCollider, need); LogZoneApproach(zone, w, "only p2"); return w; }
            }
            catch (System.Exception ex) { if (Main.DebugMode) DebugLogger.LogState($"WorldNav: GetTravelZoneApproach threw: {ex.Message}"); }
            return GetApproachPosition(zone.gameObject, playerPos);
        }

        private static void LogZoneApproach(TravelZone zone, Vector3 chosen, string why)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"WorldNav: ZoneApproach \"{zone.gameObject.name}\" -> {chosen} ({why}); p1={zone.position} p2={zone.position2} center={zone.transform.position}");
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
                case Location.FarmShop: return "a loja da fazenda";
                case Location.Mine: return "a mina";
                case Location.QuarryCave: return "a caverna da pedreira";
                case Location.InnkeepersCave: return "a caverna";
                case Location.Beach: return "a praia";
                case Location.Forest: return "a floresta";
                case Location.Camp: return "o acampamento";
                case Location.Bathhouse:
                case Location.BathhouseInterior: return "as fontes termais";
                case Location.Port: return "o porto";
                case Location.Sawmill: return "a serraria";
                case Location.Blacksmith: return "o ferreiro";
                case Location.PetShop: return "o petshop";
                case Location.ButcherHouse: return "o açougue";
                case Location.BarnInterior: return "o celeiro";
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

        // Names an NPC the way the game does. DialogueNPCBase carries the actor/character
        // name; the cat and any plain NPC fall back to a cleaned GameObject name.
        private static string DescribeNpc(NPC npc)
        {
            if (npc is DialogueNPCBase dlg)
            {
                if (!string.IsNullOrWhiteSpace(dlg.actorName)) return dlg.actorName.Trim();
                if (dlg.characterName != CharacterName.None) return dlg.characterName.ToString();
                // The dialogue's conversation title is often the character's name for merchants whose
                // actorName isn't pre-set (user: "merchants com nomes genericos").
                if (!string.IsNullOrWhiteSpace(dlg.conversationTitle))
                {
                    string ct = dlg.conversationTitle.Trim();
                    // conversation titles look like "Rhia_Standard" / "Woody/Intro" - take the part
                    // before the first separator as the name.
                    int sep = ct.IndexOfAny(new[] { '_', '/', '.' });
                    if (sep > 0) ct = ct.Substring(0, sep).Trim();
                    if (!string.IsNullOrWhiteSpace(ct) && !ct.Equals("None", System.StringComparison.OrdinalIgnoreCase)) return ct;
                }
            }
            // Generic city-NPC prefab names -> readable Portuguese (real names weren't set on these).
            string rawName = npc.gameObject.name;
            var mM = System.Text.RegularExpressions.Regex.Match(rawName, @"^Merchant\s*(\d+)$");
            if (mM.Success) return $"Comerciante {mM.Groups[1].Value}";
            var mC = System.Text.RegularExpressions.Regex.Match(rawName, @"^CityCustomer\s*(\d+)$");
            if (mC.Success) return $"Cliente da cidade {int.Parse(mC.Groups[1].Value)}";
            // Fallback: the GameObject name is usually the character's own name (e.g. "Bob",
            // "BobNPC", "Cat") - clean it up the same way scenery names are cleaned.
            return CleanSceneObjectName(npc.gameObject.name);
        }

        // Shared name cleanup for raw GameObject names (scenery + NPC fallbacks): strips the
        // engine's "(Clone)", " Variant", trailing " (N)" duplicate-index, a leading "NN - "
        // numeric prefix and a trailing "NPC", collapses the "Destructible ..." scenery family
        // into one readable label, then applies the Spanish->Portuguese translation table.
        private static string CleanSceneObjectName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string name = raw;

            // Whole "Destructible Outside 4 Variant (6)" family -> one readable name.
            if (name.StartsWith("Destructible", System.StringComparison.OrdinalIgnoreCase))
                return "Objeto destrutível";

            // Underscores are word separators in asset names ("City_Plaque" -> "City Plaque").
            name = name.Replace('_', ' ').Trim();

            if (name.EndsWith("(Clone)")) name = name.Substring(0, name.Length - "(Clone)".Length).Trim();

            // Strip a leading "1125 - " style numeric prefix.
            int dashIndex = name.IndexOf(" - ");
            if (dashIndex >= 0 && int.TryParse(name.Substring(0, dashIndex), out _))
                name = name.Substring(dashIndex + 3).Trim();

            // Strip a trailing " (3)" duplicate index and a " Variant" suffix.
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)\s*$", "").Trim();
            if (name.EndsWith(" Variant")) name = name.Substring(0, name.Length - " Variant".Length).Trim();

            // Strip a trailing "NPC" so prefab names like "BobNPC" read as "Bob".
            if (name.EndsWith("NPC")) name = name.Substring(0, name.Length - "NPC".Length).Trim();

            if (FallbackNameTranslations.TryGetValue(name, out var translated)) name = translated;
            return name;
        }

        // Best display name for an Item (rodada 134Y - based on deep code research). Uses the
        // game's localized name; when that's MISSING (the method returns the raw asset name,
        // i.e. == item.name), falls back to a Portuguese CATEGORY word from the item's type
        // (Tool subclass / Food.ingredientType / Item.category) instead of "Recurso" or a raw
        // Spanish code. This means every item reads as at least a correct category.
        // If this placeable is a BROKEN machine (Crafter), a suffix telling the player it needs
        // repair + the cost, so the tutorial "conserte a serraria" is discoverable. Broken = the
        // machine's item id maps to a `Crafter.*Repaired` static flag that is still false
        // (Crafter.cs:2674 AIIELPDNAPP, flags at :212). Repair itself is the normal Interact on it
        // (opens a Yes/No "quer consertar?" dialogue).
        private static string CrafterBrokenSuffix(Placeable placeable)
        {
            try
            {
                if (placeable.GetComponent<Crafter>() == null) return null;
                int id = placeable.itemSetup.item.JDJGFAACPFC();
                if (Main.DebugMode) DebugLogger.LogState($"CrafterBroken check: id={id} name=\"{placeable.gameObject.name}\" sawmillRepaired={Crafter.sawmillRepaired}");
                bool broken;
                string cost;
                switch (id)
                {
                    case 703: broken = !Crafter.sawmillRepaired; cost = "10 madeira"; break;
                    case 704: broken = !Crafter.smelterRepaired; cost = "5 tábuas e 5 pedra"; break;
                    case 706: broken = !Crafter.stoneWorkstationRepaired; cost = "4 pregos e 5 pedra"; break;
                    case 723: broken = !Crafter.stumpRepaired; cost = "5 madeira"; break;
                    case 728: broken = !Crafter.blacksmithsTableRepaired; cost = "4 barras de ferro e 5 pedra"; break;
                    default: return null;
                }
                return broken ? $", quebrada, conserte interagindo ({cost})" : null;
            }
            catch { return null; }
        }

        // Localized name of the item a tree/rock drops, or null. Labels a coal rock "Carvão", etc.
        private static string DroppedName(Item item)
        {
            try { return item != null ? ItemDisplayName(item) : null; }
            catch { return null; }
        }

        public static string ItemDisplayName(Item item)
        {
            if (item == null) return null;
            string raw = null;
            try { raw = item.IABAKHPEOAF(); } catch { }
            bool translationMissing = string.IsNullOrEmpty(raw) || raw == item.name;
            string cleaned = CleanSceneObjectName(raw);

            // Real translation, or our scenery table improved it (e.g. Roble->Carvalho): use it.
            if (!translationMissing && !string.IsNullOrEmpty(cleaned)) return cleaned;
            if (!string.IsNullOrEmpty(cleaned) && FallbackNameTranslations.ContainsValue(cleaned)) return cleaned;

            string cat = CategoryWord(item);
            if (!string.IsNullOrEmpty(cat)) return cat;
            return !string.IsNullOrEmpty(cleaned) ? cleaned : "Item";
        }

        private static string CategoryWord(Item item)
        {
            try
            {
                if (item is Tool)
                {
                    if (item is Pick) return "Picareta";
                    if (item is Mop) return "Esfregão";
                    if (item is Ax) return "Machado";
                    if (item is Sickle) return "Foice";
                    if (item is Rod) return "Vara de pesca";
                    if (item is Hoe) return "Enxada";
                    if (item is Spade) return "Pá";
                    if (item is WateringCan) return "Regador";
                    return "Ferramenta";
                }
                if (item is Food food && food.ingredientType != IngredientType.None)
                {
                    switch (food.ingredientType)
                    {
                        case IngredientType.Meat: return "Carne";
                        case IngredientType.Veg: return "Vegetal";
                        case IngredientType.Fruit: return "Fruta";
                        case IngredientType.Herb: return "Erva";
                        case IngredientType.Mushroom: return "Cogumelo";
                        case IngredientType.Grain: return "Cereal";
                        case IngredientType.Flour: return "Farinha";
                        case IngredientType.Berries: return "Frutos silvestres";
                        case IngredientType.Nuts: return "Frutos secos";
                        case IngredientType.Legumes:
                        case IngredientType.Bean: return "Legume";
                        case IngredientType.Cheese: return "Queijo";
                        case IngredientType.Honey: return "Mel";
                        case IngredientType.Hop: return "Lúpulo";
                        case IngredientType.WhiteFish:
                        case IngredientType.BlueFish: return "Peixe";
                        case IngredientType.Shellfish: return "Marisco";
                        case IngredientType.Seed: return "Semente";
                    }
                }
                switch (item.category)
                {
                    case Category.Nature: return "Recurso natural";
                    case Category.Farming: return "Item de cultivo";
                    case Category.Food: return "Comida";
                    case Category.Brewing: return "Bebida";
                    case Category.Tools: return "Ferramenta";
                    case Category.Furniture: return "Mobília";
                    case Category.Decorations: return "Decoração";
                    case Category.Lighting: return "Iluminação";
                    case Category.Cosmetic: return "Cosmético";
                }
            }
            catch { }
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
                ? ItemDisplayName(placeable.itemSetup.item)
                : null;
            if (!string.IsNullOrEmpty(itemName))
            {
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Item name from itemSetup: \"{itemName}\" (GameObject=\"{placeable.gameObject.name}\")");
                return itemName;
            }

            // Confirmed in the log: every object without itemSetup.item is pure scenery
            // with no Item/localization data at all - its GameObject name IS the dev's
            // original asset name, in Spanish/English (e.g. "Felpudo Exterior", "Cherry Old",
            // "Destructible Outside 4 Variant (6)"). CleanSceneObjectName strips the engine
            // cruft and applies the Spanish->Portuguese table; user reported these as the
            // "placas e objetos com nomes estranhos".
            string name = CleanSceneObjectName(placeable.gameObject.name);

            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Item name fallback (no itemSetup.item): \"{name}\" (GameObject=\"{placeable.gameObject.name}\")");
            return name;
        }

        // Round 226: public name resolver for the decoration pick-up menu. Prefers the stable
        // seat/table numbering the rest of the mod already uses ("Banco 9", "Mesa 2") so the
        // spoken list matches the placement announcements; falls back to the item/scenery name.
        public static string DescribeDecorationName(Placeable placeable)
        {
            if (placeable == null) return "objeto";
            var seat = FindSeatForPlaceable(placeable.gameObject);
            if (seat != null) return $"Banco {GetSeatNumber(seat)}";
            var table = placeable.GetComponent<Table>();
            if (table != null) return $"Mesa {GetTableNumber(table)}";
            return DescribePlaceable(placeable);
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
            // Rodada 134f: untranslated scenery names confirmed in the test log.
            { "Felpudo Exterior", "Capacho" },
            { "Cherry Old", "Cerejeira" },
            { "Taberna", "Taverna" },
            // Rodada 134h: names confirmed in the city interaction-prompt log.
            { "City Plaque", "Placa da cidade" },
            { "Post Box", "Caixa de correio" },
            // Rodada 134W: resource/collectible raw asset names (Spanish) confirmed in log.
            { "Roble", "Carvalho" },
            { "Castaño", "Castanheiro" },
        };

        // Post Box letter reading (user: the mail menu read only the dates and "Voltar", never
        // the letter itself). PostboxUI shows the opened letter in private fields letterSubject
        // and letterTextBig (the body) - read via reflection. Announced once per letter change.
        private static readonly System.Reflection.FieldInfo PostboxSubjectField =
            typeof(PostboxUI).GetField("letterSubject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo PostboxBodyField =
            typeof(PostboxUI).GetField("letterTextBig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private string _lastPostboxLetter;

        // Set by the navigator when the user presses Enter on a letter (OpenLetter). The body is
        // read ONLY then - not automatically when the postbox menu opens (user: "não quero q saia
        // lendo quando menu aberto, e sim com as setas"). Navigation reads the subject per row.
        public static bool LetterReadArmed;
        private void HandlePostboxAnnouncement()
        {
            PostboxUI pb;
            try { pb = PostboxUI.ODLPIANFFFJ(1); }
            catch { return; }
            if (pb == null || !pb.IsOpen() || PostboxBodyField == null)
            {
                _lastPostboxLetter = null;
                LetterReadArmed = false;
                return;
            }

            if (!LetterReadArmed) return;   // only after Enter on a letter, never auto on menu open

            var bodyLabel = PostboxBodyField.GetValue(pb) as TMPro.TMP_Text;
            string body = bodyLabel != null ? UITextExtractor.GetReadableText(bodyLabel) : null;
            if (string.IsNullOrEmpty(body)) return;   // still armed: wait for OpenLetter to populate it

            LetterReadArmed = false;
            _lastPostboxLetter = body;
            var subjectLabel = PostboxSubjectField?.GetValue(pb) as TMPro.TMP_Text;
            string subject = subjectLabel != null ? UITextExtractor.GetReadableText(subjectLabel) : null;
            string msg = string.IsNullOrEmpty(subject) ? body : $"{subject}. {body}";
            ScreenReader.Say(msg, interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: Postbox letter read, subject=\"{subject}\"");
        }

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
        // FindAll<T>() call in this scene costs ~150-180ms by itself (measured
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

        // Well (MonoBehaviour, not Placeable) proximity sound — refreshed on the same
        // AllPlaceablesInterval cadence as the main placeable cache.
        private Well[] _cachedWells = new Well[0];
        private const float WellSoundRadius = 5f;
        private float _lastWellSoundTime = -999f;
        private const float WellSoundInterval = 3f;

        // Round 113: ONE cached scan of all Placeables, shared by the candle proximity AND the
        // item-proximity sounds. HandleItemProximitySounds was doing its OWN FindObjectsOfType<
        // Placeable> EVERY second (~87ms spike per second - a major continuous stutter and the real
        // cause of "anuncios de item proximo demora muito"). Now both read this cache.
        private Placeable[] _cachedAllPlaceables = new Placeable[0];
        private float _lastAllPlaceablesTime = -999f;
        // Round 234: was 15s. The cached objects (rocks, trees, harvestables, placeables...) are
        // STATIC - they only change when one is created/destroyed (mined, chopped, planted, placed)
        // or when the player changes area. Both are already handled by events: OnActionDone forces a
        // re-scan (EnsureFarmHooks), and _lastScanLocation below forces one on area change. So the
        // blind periodic re-scan is almost pure wasted cost (each stage is a ~42ms full-scene
        // FindObjectsByType - the lag the user feels). Widened to 60s as a rare safety net only.
        private const float AllPlaceablesInterval = 60f;
        // Force a fresh scan the moment the player enters a new area (its objects are different), so
        // the long interval above never leaves stale/empty resource data after a transition.
        private Location _lastScanLocation = Location.None;

        // Ambient proximity announcement [82] (user chose option "a": announce each new
        // resource/tree/animal as you pass). Cached scans + a "already announced" set with
        // hysteresis (forget when you walk away, so re-approaching re-announces).
        private Harvestable[] _cachedHarvestables = new Harvestable[0];
        private MiscellaneousHarvest[] _cachedMiscHarvests = new MiscellaneousHarvest[0];
        private Tree[] _cachedTrees = new Tree[0];
        private AnimalNPC[] _cachedAnimals = new AnimalNPC[0];
        private Rock[] _cachedRocks = new Rock[0];
        private FertileSoil[] _cachedFertileSoils = new FertileSoil[0];
        private Vector2Int _lastResourceTile = new Vector2Int(int.MinValue, int.MinValue);

        private int _sceneScanStage = -1; // -1 = idle; >=0 = the scan to run THIS frame

        // Drink-serving: list every customer currently waiting with an order, both the banquet
        // competition (BanquetOrdersManager.tableOrders) and the ordinary tavern (Bar.waitingAtBar),
        // under "Servir" with the drink they want (CustomerBase.currentRequest).
        private static string DrinkRequestName(ItemInstance inst)
        {
            try { var it = inst != null ? inst.LHBPOPOIFLE() : null; return it != null ? it.IABAKHPEOAF() : "bebida"; }
            catch { return "bebida"; }
        }

        private void AddServingCustomers(List<(string name, Vector3 position, string category)> list, Vector3 playerPos)
        {
            try
            {
                var bom = BanquetOrdersManager.instance;
                if (bom != null && bom.tableOrders != null)
                {
                    foreach (var cust in bom.tableOrders)
                    {
                        if (cust == null || cust.currentRequest == null) continue;
                        list.Add(($"Cliente quer {DrinkRequestName(cust.currentRequest)}", GetApproachPosition(cust.gameObject, playerPos), "Servir"));
                    }
                }
            }
            catch { }
            try
            {
                var bar = Bar.instance;
                if (bar != null && bar.waitingAtBar != null)
                {
                    foreach (var npc in bar.waitingAtBar)
                    {
                        if (npc == null || npc.customer == null || npc.customer.currentRequest == null) continue;
                        list.Add(($"Cliente quer {DrinkRequestName(npc.customer.currentRequest)}", GetApproachPosition(npc.gameObject, playerPos), "Servir"));
                    }
                }
            }
            catch { }
        }

        // True in the mine/quarry areas, where tavern furniture (seats/tables/wells/candles) and
        // trees/animals/farm soil don't exist - used to skip those full-scene scans (lag fix).
        private static bool IsMiningArea()
        {
            var p = PlayerController.GetPlayer(1);
            if (p == null) return false;
            var loc = p.LEOIMFNKFGA;
            return loc == Location.Mine || loc == Location.Quarry || loc == Location.QuarryCave;
        }

        private void RefreshSeatSceneCache()
        {
            // Each full-scene FindObjectsByType is tens of ms in this game's big scenes; doing ALL of
            // them in one frame was a ~580ms freeze every 15s (confirmed in the PERF log) - the source
            // of "andando muito devagar" and "anúncios voltaram a demorar". Now we run ONE scan per
            // frame (staged), so the cost is spread out and no single frame hitches. The cached arrays
            // are never null (init to empty), so consumers reading a not-yet-refreshed stage just see
            // last cycle's data (or empty on cold start) - safe, no NRE. Rats use the game's own live
            // list (SceneReferences.tutorialRats), so they're not scanned here.
            // Area change: force a fresh scan cycle now (new area = different objects).
            if (_sceneScanStage < 0)
            {
                var loc = PlayerController.GetPlayer(1)?.LEOIMFNKFGA ?? Location.None;
                if (loc != _lastScanLocation) { _lastScanLocation = loc; _lastAllPlaceablesTime = -999f; }
            }
            if (_sceneScanStage < 0 && Time.unscaledTime - _lastAllPlaceablesTime >= AllPlaceablesInterval)
            {
                _lastAllPlaceablesTime = Time.unscaledTime;
                _sceneScanStage = 0;
            }
            if (_sceneScanStage < 0) return;

            // Lag fix (user: "quando entra na pedreira fica bem lento" - PERF log showed each
            // full-scene FindObjectsByType ~42ms in the mine/quarry). Tavern furniture (candles,
            // wells, seats, tables) simply doesn't exist in the mining areas, so those scans are
            // pure wasted cost there - skip them and let the (already empty) caches stand.
            bool mining = IsMiningArea();

            var sw = Main.DebugMode ? System.Diagnostics.Stopwatch.StartNew() : null;
            switch (_sceneScanStage)
            {
                case 0: _cachedAllPlaceables = FindAll<Placeable>(); break;
                case 1:
                    if (mining) { _cachedCandles = new Placeable[0]; break; }
                    _cachedCandles = _cachedAllPlaceables
                        .Where(p => p != null && p.itemSetup != null && p.itemSetup.item != null && p.itemSetup.item.JDJGFAACPFC() == CandleItemId)
                        .ToArray();
                    break;
                case 2: if (mining) { _cachedWells = new Well[0]; break; } _cachedWells = FindAll<Well>(); break;
                case 3: _cachedHarvestables = FindAll<Harvestable>(); break;
                case 4: _cachedMiscHarvests = FindAll<MiscellaneousHarvest>(); break;
                case 5: if (mining) { _cachedTrees = new Tree[0]; break; } _cachedTrees = FindAll<Tree>(); break;
                case 6: if (mining) { _cachedAnimals = new AnimalNPC[0]; break; } _cachedAnimals = FindAll<AnimalNPC>(); break;
                case 7: _cachedRocks = FindAll<Rock>(); break;
                case 8: if (mining) { _cachedFertileSoils = new FertileSoil[0]; break; } _cachedFertileSoils = FindAll<FertileSoil>(); break;
                case 9: if (mining) { _cachedSeats = new Seat[0]; break; } _cachedSeats = FindAll<Seat>(); break;
                case 10: if (mining) { _cachedTables = new Table[0]; break; } _cachedTables = FindAll<Table>(); break;
            }
            if (sw != null && sw.ElapsedMilliseconds > 3) DebugLogger.LogState($"WorldNav: PERF scene scan stage {_sceneScanStage} took {sw.ElapsedMilliseconds}ms");
            _sceneScanStage++;
            if (_sceneScanStage > 10) _sceneScanStage = -1;
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
        private bool _tavernOpenInitialized;
        private bool _lastTavernOpen;

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
                    // User request: say HOW MUCH satisfaction the customer left with. The concrete
                    // measure is reputationGain (+ when served well, - when not); "mais/menos" so the
                    // screen reader speaks the sign clearly instead of a "+"/"-" symbol. Base the
                    // satisfied/dissatisfied word on the rep SIGN when known (was contradicting itself:
                    // "saiu satisfeito, reputação menos 12"); fall back to hasBeenServed otherwise.
                    int rep = 0; try { rep = g != null ? g.reputationGain : 0; } catch { }
                    bool happy = rep != 0 ? rep > 0 : served;
                    string leave = happy ? "Cliente saiu satisfeito" : "Cliente saiu insatisfeito";
                    if (rep != 0) leave += $", reputação {(rep > 0 ? "mais" : "menos")} {Mathf.Abs(rep)}";
                    ScreenReader.Say(leave, interrupt: false);
                }
            }
        }

        private void HandleQuickSave()
        {
            if (!Input.GetKeyDown(KeyCode.S)) return;
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
            var saveUI = SaveUI.instance;
            if (saveUI == null) return;
            saveUI.AutoSave();
            ScreenReader.Say("Jogo salvo", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState("QuickSave: AutoSave called");
        }

        private void HandleTavernOpenClose()
        {
            var tm = TavernManager.GGFJGHHHEJC;
            if (tm == null) return;
            bool open = tm.LKOJBFMGMAE;
            if (!_tavernOpenInitialized)
            {
                _tavernOpenInitialized = true;
                _lastTavernOpen = open;
                return;
            }
            if (open == _lastTavernOpen) return;
            _lastTavernOpen = open;
            ScreenReader.Say(open ? "Taverna aberta" : "Taverna fechada", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"TavernOpenClose: tavern is now {(open ? "open" : "closed")}");
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

        // P key: read the tutorial help popup text (the "Ajuda" button on the GoalsPanel,
        // activated by Rewired "Objective" action). The game only shows the popup once per phase
        // opening (guarded by `open` flag), so the screen reader never hears it. Two behaviors:
        // (1) When the popup becomes visible, read its text automatically.
        // (2) P re-reads the text; if the popup was open and blocking movement, also closes it.
        private bool _tutorialPopupWasActive;
        private bool _pendingReadTutorialPopup;

        private void HandleTutorialHelpKey()
        {
            var inst = NewTutorialManager.instance;
            if (inst == null || inst.mainPopup == null) return;
            bool popupActive = inst.mainPopup.activeSelf;

            // Auto-read when popup appears.
            if (popupActive && !_tutorialPopupWasActive)
            {
                string text = inst.popupText != null ? inst.popupText.text : null;
                if (!string.IsNullOrEmpty(text)) ScreenReader.Say(text, interrupt: false);
                if (Main.DebugMode) DebugLogger.LogState($"WorldNav: tutorial popup appeared, text=\"{text?.Substring(0, System.Math.Min(60, text?.Length ?? 0))}\"");
            }
            _tutorialPopupWasActive = popupActive;

            // P: re-read (and close if blocking movement).
            if (Input.GetKeyDown(KeyCode.P))
            {
                if (popupActive)
                {
                    // Use GetReadableText (strips <b>/<color=...> rich-text tags) - reading
                    // popupText.text raw made NVDA speak the literal tags (user: "tagueamentos
                    // estranhos"). Same fix applied to the next-frame read below.
                    string text = inst.popupText != null ? UITextExtractor.GetReadableText(inst.popupText) : null;
                    if (!string.IsNullOrEmpty(text)) ScreenReader.Say(text, interrupt: true);
                    NewTutorialManager.ClosePopUp();
                    if (Main.DebugMode) DebugLogger.LogState("WorldNav: P key - re-read and close tutorial popup");
                }
                else if (inst.currentTutorialPhase != null)
                {
                    // Try to open the popup; read on the next frame after text is set.
                    NewTutorialManager.ShowPopUp();
                    _pendingReadTutorialPopup = true;
                    if (Main.DebugMode) DebugLogger.LogState("WorldNav: P key - called ShowPopUp, will read next frame");
                }
                else
                {
                    ScreenReader.Say("Sem ajuda para esta fase", interrupt: true);
                }
            }

            if (_pendingReadTutorialPopup && popupActive)
            {
                string text = inst.popupText != null ? UITextExtractor.GetReadableText(inst.popupText) : null;
                if (!string.IsNullOrEmpty(text)) ScreenReader.Say(text, interrupt: true);
                _pendingReadTutorialPopup = false;
            }
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

        // User request: the post box plays a locating sound every 1s while you're near, panned
        // left/right and pitched higher (above) / lower (below) so you can home in on it - same
        // directional scheme as the item-proximity sounds. Needs "correio.wav" in the sounds folder.
        private float _lastPostBoxSoundTime;
        private const float PostBoxSoundInterval = 1f;
        private const float PostBoxSoundRadius = 7f;
        private void HandlePostBoxProximitySound()
        {
            if (Time.unscaledTime - _lastPostBoxSoundTime < PostBoxSoundInterval) return;
            var boxes = FindAll<PostBox>();
            if (boxes == null || boxes.Length == 0) return;
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            PostBox nearest = null; float best = float.MaxValue;
            foreach (var b in boxes)
            {
                if (b == null) continue;
                float d = Vector3.Distance(playerPos, b.transform.position);
                if (d < best) { best = d; nearest = b; }
            }
            if (nearest == null || best > PostBoxSoundRadius) return;
            _lastPostBoxSoundTime = Time.unscaledTime;
            Vector3 delta = nearest.transform.position - playerPos;
            float pitch = 1f, pan = 0f;
            if (Mathf.Abs(delta.y) >= Mathf.Abs(delta.x)) pitch = delta.y > 0 ? 1.3f : 0.75f;   // above/below
            else pan = delta.x > 0 ? 1f : -1f;                                                   // right/left
            CustomSounds.PlayZoneSoundDirectional("correio", pan, pitch);
        }

        private void HandleWellProximitySound()
        {
            if (Time.unscaledTime - _lastWellSoundTime < WellSoundInterval) return;
            if (_cachedWells == null || _cachedWells.Length == 0) return;
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            foreach (var well in _cachedWells)
            {
                if (well == null) continue;
                if (Vector3.Distance(playerPos, well.transform.position) <= WellSoundRadius)
                {
                    _lastWellSoundTime = Time.unscaledTime;
                    CustomSounds.PlayZoneSound("poço");
                    return;
                }
            }
        }

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

            // Distinguish outdoor pátio from indoor corridors: both are WithoutZone, but
            // the outside area has Location != Tavern (typically Location.None).
            var player = PlayerController.GetPlayer(1);
            Location playerLoc = player != null ? player.LEOIMFNKFGA : Location.None;
            string name = ZoneTypeName(zone, playerLoc);
            if (Main.DebugMode) DebugLogger.LogState($"WorldNav: zone changed to {zone} (loc={playerLoc}, name={name ?? "(silent)"})");

            // Zone-transition sounds: each workshop zone + outdoor transition gets a distinct sound.
            switch (zone)
            {
                case ZoneType.WoodWorkshop: CustomSounds.PlayZoneSound("madeira"); break;
                case ZoneType.MetalWorkshop: CustomSounds.PlayZoneSound("metal"); break;
                case ZoneType.StoneWorkshop: CustomSounds.PlayZoneSound("pedra"); break;
                case ZoneType.WithoutZone:
                    if (playerLoc != Location.Tavern) CustomSounds.PlayZoneSound("porta"); break;
            }

            if (name != null) ScreenReader.Say(name, interrupt: false);
        }

        private static string ZoneTypeName(ZoneType zone, Location playerLoc = Location.None)
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
                case ZoneType.WithoutZone:
                    return playerLoc == Location.Tavern ? "Corredor" : "Pátio da taverna";
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

        // REVERTED the 2-unit interaction distance cap: it broke Ctrl+Enter on large stations whose
        // transform centre sits >2 units from where the player stands adjacent (user: "não deixa eu
        // interagir... volte ao normal"). Back to no cap here. (The "opens from far" report will be
        // re-addressed with a proximity check that doesn't false-negative on big stations.)
        private static bool TryInteractableMouseUp()
        {
            var closest = FindClosestAvailableByProximity();
            if (closest == null) return false;

            bool handled = ((IInteractable)closest).MouseUp(1);
            DebugLogger.LogInput("Ctrl+Enter", $"IInteractable.MouseUp on \"{closest.gameObject.name}\" -> {handled}");
            return handled;
        }

        private static MonoBehaviour FindClosestAvailableByProximity(float maxDistance = float.MaxValue)
        {
            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            MonoBehaviour closest = null;
            float closestDist = float.MaxValue;

            foreach (var behaviour in FindAll<MonoBehaviour>())
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
                if (distance > maxDistance) continue;
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
            foreach (var behaviour in FindAll<MonoBehaviour>())
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
            // Rodada 134h: route ANY remaining NPC through the shared namer so townspeople
            // (RhiaNPC etc.) read as "Rhia" instead of the raw "RhiaNPC". Covers MaiNPC too
            // (characterName=Mai), so the old per-character special cases aren't needed.
            var npc = go.GetComponent<NPC>() ?? go.GetComponentInParent<NPC>();
            if (npc != null) return DescribeNpc(npc);
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
            // near the tap). If the focused object isn't an NPC but an available one is nearby AND
            // closer to the player, use the NPC. Round 133: guard with distance comparison - the
            // reverse bug ("dispenser shows cat name") happens when the player is AT the dispenser
            // and the cat is further away but still IsAvailableByProximity. The NPC should only
            // win if it's actually closer than the focused object.
            if (npcName == null)
            {
                var npcGo = FindClosestAvailableNpc();
                if (npcGo != null)
                {
                    Vector3 pPos = PlayerController.GetPlayerPosition(1);
                    float npcDist = Vector3.Distance(pPos, npcGo.transform.position);
                    float focDist = Vector3.Distance(pPos, go.transform.position);
                    if (npcDist < focDist) { go = npcGo; npcName = DescribeNpc(go); }
                }
            }
            // Trees/rocks (coal veins, ore, logs) are Placeables that DescribePlaceable can't name,
            // so they read "Decoração" (user: "carvão aparecia como decoração; madeira idem"). Name
            // them by what they drop + the action, e.g. "Carvão, minerar" / "Carvalho, cortar".
            if (npcName == null)
            {
                var rock = go.GetComponent<Rock>() ?? go.GetComponentInParent<Rock>();
                if (rock != null)
                {
                    string rn = DroppedName(rock.droppedItems != null && rock.droppedItems.Length > 0 ? rock.droppedItems[0].item : null) ?? "Pedra";
                    // Surface WHY a rock can't be mined (user: "pedras que não deixa minerar, anuncie
                    // o motivo"): a pick can't break an axe-required rock, and high-tier ore needs a
                    // better pick (Rock.toolLevelRequired).
                    string act = rock.axRequired ? "precisa de machado"
                        : rock.toolLevelRequired > 1 ? $"minerar, precisa de picareta nível {rock.toolLevelRequired}"
                        : "minerar";
                    return ($"{rn}, {act}", go.transform.position);
                }
                var tr = go.GetComponent<Tree>() ?? go.GetComponentInParent<Tree>();
                if (tr != null)
                {
                    string tn = DroppedName(tr.droppedItems != null && tr.droppedItems.Length > 0 ? tr.droppedItems[0].item : null) ?? "Árvore";
                    // Hardwoods need a better axe (Tree.toolLevelRequired) - surface it (user: "madeira
                    // nobre não cortou, não sei se tem aviso").
                    string act = tr.toolLevelRequired > 1 ? $"cortar, precisa de machado nível {tr.toolLevelRequired}" : "cortar";
                    return ($"{tn}, {act}", go.transform.position);
                }
                // A planted crop (Harvestable with a cropSetter) - name it by the crop + "colher"
                // when it's ready, so "trigo, colher" instead of a raw name / "Decoração".
                var harv = go.GetComponent<Harvestable>() ?? go.GetComponentInParent<Harvestable>();
                if (harv != null && harv.cropSetter != null)
                {
                    string cn = CropName(harv.cropSetter) ?? "Planta";
                    string label = cn;
                    if (CropIsDead(harv.cropSetter)) label = $"{cn} morto";
                    else if (CropIsReady(harv.cropSetter))
                        label = CropHandHarvest(harv.cropSetter) ? $"{cn}, Control Enter pra colher" : $"{cn}, colha com a foice";
                    return (label, go.transform.position);
                }
            }

            var placeable = npcName == null ? (go.GetComponent<Placeable>() ?? go.GetComponentInParent<Placeable>()) : null;
            // Clean the raw-GameObject-name fallback too (e.g. "City_Plaque" -> "Placa da
            // cidade", "Post Box" -> "Caixa de correio") - same cleanup the nav list uses.
            string name = npcName ?? (placeable != null ? DescribePlaceable(placeable) : CleanSceneObjectName(go.name));
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
