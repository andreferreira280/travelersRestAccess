using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Keyboard accessibility for Decoration Mode (B key) - relocating an already-placed
    /// object (table, bench, etc.) without a mouse. The game's whole placement flow is
    /// mouse-driven (hover an item, left-click to grab, drag, left-click again to drop on a
    /// valid spot, highlighted in red/green for sighted players) - user's explicit request
    /// for a full keyboard-controlled virtual cursor instead of just announcing valid/invalid
    /// while still requiring the mouse to move things.
    ///
    /// Built entirely on PUBLIC game APIs, no Harmony patches needed: CursorManager already
    /// exposes a world-space cursor position used internally for GAMEPAD D-pad nudging during
    /// placement (confirmed in decompiled Placeable.ALFOFLNNPMJ - the exact "+-0.5 per press"
    /// pattern this mirrors, just gated on IsGamepadActive there, which a keyboard player never
    /// satisfies) - moving it ourselves via CursorManager.SetCursorPositionFromWorld makes the
    /// game's own per-frame Placeable.WhileSelected -> SetPosition logic follow it on its own,
    /// the same way it already follows the mouse or a gamepad.
    ///
    /// SCOPE update (round 91): originally written assuming "pulling a brand new item from
    /// inventory into decoration mode" was a separate, untouched entry point. Live log evidence
    /// proved otherwise - selecting a new decoration on the hotbar and using it (native key,
    /// confirmed "F" by the user) fires "Modo de decoração ativado" + "Item pego" through this
    /// exact same SelectObject.selectedGameObject path, same as our own Enter-grab. Everything
    /// below already reacts correctly regardless of which path set selectedGameObject.
    /// </summary>
    public class DecorationModeHandler
    {
        private const float TileSize = 0.5f;

        private bool _wasActive;
        private GameObject _lastSelectedGameObject;
        private bool? _lastCanBePlaced;
        private Seat _pendingSeatCheck;
        private int? _pendingSeatCheckSlotNumber;
        private GameObject _pendingSnapDeselect;
        private SeatingGroup _pendingSnapSlot;
        // Round 100: wall/surface confirm that returned False on the first frame is retried over
        // the next few frames instead of giving up. Root cause (confirmed in the log + decompiled
        // PhysicalSpaceWall.ValidPosition): physicalSpace validity reads a "colliders" list
        // populated by OnTriggerEnter/Exit2D callbacks, which only fire during the physics step
        // (FixedUpdate), NOT from a same-frame transform write + Physics2D.SyncTransforms(). Right
        // after teleporting the item via arrows, that list still holds stale overlaps, so the
        // item reads invalid even at a genuinely valid spot - matching "announced valid but Enter
        // failed", and why walking the item there (gradual, physics settles) worked. Keeping the
        // item pinned still for a few frames lets the trigger list clear, then Deselect succeeds.
        private GameObject _pendingSettleDeselect;
        private Vector3 _pendingSettlePos;
        private SurfaceSortOrder _pendingSettleSurface;
        private string _pendingSettleLabel;
        private int _pendingSettleFramesLeft;
        private const int SettleRetryFrames = 30;
        private Vector2Int? _lastGuidanceTileOffset;
        private float _lastGuidanceCheckTime = -999f;
        private bool _wasdReminderGivenThisHold;
        private SeatingGroup _lockedTargetSlot;
        // Round 89: GetSeatTargetPosition now needs the slot's owner table too (to read the
        // table's own buildSquare centre, the engine's real reference point) - SeatingGroup has
        // no back-reference to its Table, so this has to be tracked alongside the lock.
        private Table _lockedTargetTable;
        private Vector3? _heldIntendedPosition;
        // Round 90: same lock-onto-one-target pattern as _lockedTargetSlot, for generic
        // decorations (paintings/plants/centerpieces) that need a surface instead of a seat.
        private SurfaceSortOrder _lockedTargetSurface;
        // Round 101: when the held item snaps to a designated TABLE spot, guidance leads to that
        // exact snap point (not the surface centre) so the player lands where it actually counts.
        private Vector3? _lockedTargetSnapPos;
        private Vector2Int? _lastSurfaceGuidanceTileOffset;
        // Round 103: directional guidance to a free wall tile is back (round 102 removed it, which
        // left the blind player with no way to find the wall - confirmed in the log they never
        // reached one). Now targets a stable free wall tile (geometry, not the flaky physics
        // check), plus the round-102 "livre aqui" announcement for the final confirmation.
        private bool? _lastWallFree;
        private Vector3? _lockedTargetWallPoint;
        private Vector2Int? _lastWallGuidanceTileOffset;
        // Round 91: arrow-key movement only ever moved once per physical key-down event
        // (GetKeyDown fires exactly once per press, ignoring how long it's held) - user reported
        // it "won't go very fast" needing dozens of taps to cross a room. Hold-to-repeat timer,
        // same UX as a text caret: a single tap still moves exactly one tile (no minimum hold),
        // holding repeats after a short delay.
        private float _nextArrowRepeatTime;
        private const float ArrowRepeatInitialDelay = 0.25f;
        private const float ArrowRepeatInterval = 0.08f;
        // Round 91: accessible replacement for the native "Estilo" (T key) skin/style cycling -
        // user asked for a navigable list (arrows + Enter) instead of blind repeated cycling.
        private bool _stylePickerActive;
        private Placeable _stylePickerPlaceable;
        private int _stylePickerIndex;
        private int _stylePickerOriginalIndex;
        private int _stylePickerCount;

        public void Update()
        {
            // Seat.table is assigned by Seat.GetNeighbourTable, which the game itself runs
            // a frame late in some cases (confirmed in decompiled source: a "NextFrame"
            // coroutine variant exists) - checking it the same frame Deselect() succeeds
            // could read a stale null. Delaying the announcement by one Update() call avoids
            // reporting "sem mesa por perto" wrongly just from timing.
            if (_pendingSeatCheck != null)
            {
                var seat = _pendingSeatCheck;
                int? attemptedSlot = _pendingSeatCheckSlotNumber;
                _pendingSeatCheck = null;
                _pendingSeatCheckSlotNumber = null;
                // User's explicit request: confirm WHICH bench and, if associated, WHICH
                // vaga/mesa - not just a generic yes/no, and clearer verbs ("pego"/"solto").
                int seatNumber = WorldNavigationHandler.GetSeatNumber(seat);
                string msg;
                if (seat.table != null)
                {
                    msg = attemptedSlot.HasValue
                        ? $"Banco {seatNumber} posicionado na vaga {attemptedSlot.Value}"
                        : $"Banco {seatNumber} solto, associado à mesa {WorldNavigationHandler.GetTableNumber(seat.table)}";
                }
                else
                {
                    msg = attemptedSlot.HasValue
                        ? $"Banco {seatNumber} solto perto da vaga {attemptedSlot.Value}, mas não associou à mesa"
                        : $"Banco {seatNumber} solto, mas sem mesa por perto";
                }
                ScreenReader.Say(msg, interrupt: true);
                WorldNavigationHandler.LogSeatPlacementDiagnostics(seat);
            }

            // Round 71 feature: user asked to not need pixel-perfect manual alignment onto a
            // table slot - HandleConfirmPlacement below, when a free slot is close enough,
            // snaps the cursor onto it and forces the bench's facing to match BEFORE
            // confirming, instead of confirming wherever the player happened to be holding it.
            // Deferred one frame on purpose: Placeable.WhileSelected (the engine's own code that
            // actually moves the transform to follow the cursor) runs once per frame on its
            // own - calling Deselect() in the SAME frame we move the cursor would still read
            // the OLD position, same class of timing bug as the seat.table check above.
            if (_pendingSnapDeselect != null)
            {
                GameObject beingPlaced = _pendingSnapDeselect;
                SeatingGroup slot = _pendingSnapSlot;
                _pendingSnapDeselect = null;
                _pendingSnapSlot = null;
                if (Main.DebugMode)
                {
                    var placeableBeforeDeselect = beingPlaced.GetComponent<Placeable>();
                    DebugLogger.LogState($"DecorationMode: snap - position 1 frame later={beingPlaced.transform.position} canBePlaced={(placeableBeforeDeselect != null ? placeableBeforeDeselect.canBePlaced.ToString() : "?")}");
                }
                // Round 86: found the real gate by reading Seat.GetNeighbourTable/
                // GetNeighbourTableAround/CODJEMEJFGF in full. table association only ever runs
                // automatically through Placeable.WhileSelected -> Seat's WhileSelectedCallback,
                // which stops firing the instant the object is no longer selected - so whichever
                // one of our frame vs the engine's own Update() runs Deselect() first determines
                // whether the LAST GetNeighbourTable() call ever saw our final position at all.
                // Calling it explicitly here removes that race entirely instead of hoping the
                // implicit per-frame timing lines up.
                Seat seatBeforeDeselect = WorldNavigationHandler.FindSeatForPlaceable(beingPlaced);
                if (seatBeforeDeselect != null)
                {
                    seatBeforeDeselect.GetNeighbourTableAround();
                    seatBeforeDeselect.GetNeighbourTable();
                    if (Main.DebugMode)
                    {
                        var p = beingPlaced.GetComponent<Placeable>();
                        Table foundTable = seatBeforeDeselect.table;
                        DebugLogger.LogState($"DecorationMode: explicit GetNeighbourTable - direction={(p != null ? p.GetDirection().ToString() : "?")} table={(foundTable != null ? foundTable.gameObject.name : "null")} leftRightTuck={(foundTable != null ? foundTable.leftRightTuck.ToString("F2") : "n/a")} upDownTuck={(foundTable != null ? foundTable.upDownTuck.ToString("F2") : "n/a")}");
                    }
                    WorldNavigationHandler.LogTableSearchGap(seatBeforeDeselect);
                }
                var pendingSelectObj = SelectObject.GetPlayer(1);
                bool placedAfterSnap = pendingSelectObj != null && pendingSelectObj.Deselect();
                HandlePlacementResult(placedAfterSnap, beingPlaced, slot);
                return;
            }

            // Round 100: settle-retry for wall/surface items (see field comment). Keep the item
            // pinned at the validated spot and re-attempt Deselect each frame until the physics
            // trigger list clears and it becomes genuinely valid, or the budget runs out.
            if (_pendingSettleDeselect != null)
            {
                GameObject beingPlaced = _pendingSettleDeselect;
                var settleSelectObj = SelectObject.GetPlayer(1);
                var placeable = beingPlaced.GetComponent<Placeable>();
                // Abort if the item stopped being the held selection (player grabbed something
                // else / left decoration mode) - nothing to retry.
                if (settleSelectObj == null || settleSelectObj.selectedGameObject != beingPlaced || placeable == null)
                {
                    ClearPendingSettle();
                    return;
                }
                // Re-pin to the validated position so the game's own WhileSelected can't drift it
                // while we wait for physics to settle.
                CursorManager.SetCursorPositionFromWorld(1, _pendingSettlePos);
                placeable.SetMouseOffset(Vector3.zero);
                if (_pendingSettleSurface != null && placeable.currentSurface == null)
                    placeable.AddPlaceableToSurface(_pendingSettleSurface, true);
                placeable.SetPosition(1, placeable.attachedToPlayer, placeable.snapToGrid, true);
                if (!placeable.snappedToPosition) beingPlaced.transform.position = _pendingSettlePos;
                Physics2D.SyncTransforms();

                bool placed = settleSelectObj.Deselect();
                _pendingSettleFramesLeft--;
                if (placed)
                {
                    if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: {_pendingSettleLabel} settle deselect -> True (frames left {_pendingSettleFramesLeft}) finalPos={beingPlaced.transform.position}");
                    GameObject placedObj = beingPlaced;
                    ClearPendingSettle();
                    HandlePlacementResult(true, placedObj, null);
                }
                else if (_pendingSettleFramesLeft <= 0)
                {
                    if (Main.DebugMode)
                    {
                        DebugLogger.LogState($"DecorationMode: {_pendingSettleLabel} settle gave up");
                        WorldNavigationHandler.LogDeselectGate(placeable, _pendingSettleLabel);
                    }
                    GameObject failedObj = beingPlaced;
                    ClearPendingSettle();
                    HandlePlacementResult(false, failedObj, null);
                }
                return;
            }

            var decoMode = DecorationMode.GetPlayer(1);
            bool active = decoMode != null && decoMode.DMBFKFLDDLH;

            if (active != _wasActive)
            {
                _wasActive = active;
                ScreenReader.Say(active ? "Modo de decoração ativado" : "Modo de decoração desativado", interrupt: true);
                if (!active)
                {
                    _lastSelectedGameObject = null;
                    _lastCanBePlaced = null;
                }
            }
            if (!active) return;

            var selectObj = SelectObject.GetPlayer(1);
            if (selectObj == null) return;

            if (selectObj.selectedGameObject == null)
            {
                if (_lastSelectedGameObject != null && Main.DebugMode)
                {
                    DebugLogger.LogState("DecorationMode: selectedGameObject went back to null (placed or released by some means)");
                }
                _lastSelectedGameObject = null;
                _lastCanBePlaced = null;
                _heldIntendedPosition = null;
                HandleGrab(selectObj);
                return;
            }

            if (selectObj.selectedGameObject != _lastSelectedGameObject)
            {
                _lastSelectedGameObject = selectObj.selectedGameObject;
                _lastCanBePlaced = null;
                // User's explicit request: say WHICH bench was grabbed, not just "an item" -
                // same global numbering as the proximity announcement/nav list
                // (WorldNavigationHandler.GetSeatNumber) so the number means the same thing.
                var grabbedSeat = WorldNavigationHandler.FindSeatForPlaceable(selectObj.selectedGameObject);
                string holdMsg = grabbedSeat != null
                    ? $"Banco {WorldNavigationHandler.GetSeatNumber(grabbedSeat)} pego. Use as setas pra mover, Enter pra soltar."
                    : "Item pego. Use as setas pra mover, Enter pra soltar.";
                ScreenReader.Say(holdMsg, interrupt: true);
                _lastGuidanceTileOffset = null;
                _wasdReminderGivenThisHold = false;
                _lockedTargetSlot = null;
                _lockedTargetTable = null;
                _lockedTargetSurface = null;
                _lockedTargetSnapPos = null;
                _lastSurfaceGuidanceTileOffset = null;
                _lastWallFree = null;
                _lockedTargetWallPoint = null;
                _lastWallGuidanceTileOffset = null;
                _lockedTargetItemSpacePosition = null;
                _lastItemSpaceGuidanceTileOffset = null;
                // Round 97: THE fix for the user's "cursor and my position aren't being compared"
                // report. Items grabbed via the native "F" hotbar key set selectedGameObject
                // directly (never through our Enter-grab/HandleGrab), so _heldIntendedPosition was
                // left null - HandleCursorMovement then returned early every frame, leaving the
                // item frozen at wherever the game spawned it (near the wall/cursor, unrelated to
                // the player) while the guidance compared that frozen point to the target. Now
                // every grab path initializes our own position tracking here from the object's
                // real spawn position, so arrows actually move it (same virtual-cursor model the
                // bench uses) and the guidance reflects the item the player is actually driving.
                if (!_heldIntendedPosition.HasValue)
                {
                    _heldIntendedPosition = selectObj.selectedGameObject.transform.position;
                    CursorManager.SetCursorPositionFromWorld(1, _heldIntendedPosition.Value);
                    var initPlaceable = selectObj.selectedGameObject.GetComponent<Placeable>();
                    if (initPlaceable != null) initPlaceable.SetMouseOffset(Vector3.zero);
                }
                // User's explicit request to validate this for real: this fires from
                // reading selectedGameObject directly, regardless of HOW it got set (our own
                // Enter-grab below, OR a native key like the "T"/"R" the user found already
                // grabs/places on its own) - confirms whether a native shortcut is really
                // doing the same thing our code does.
                if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: selectedGameObject changed to \"{selectObj.selectedGameObject.name}\" (instanceId={selectObj.selectedGameObject.GetInstanceID()}) seatNumber={(grabbedSeat != null ? WorldNavigationHandler.GetSeatNumber(grabbedSeat).ToString() : "n/a")}");
                // Round 91: moved here from HandleGrab - log proved a hotbar item used directly
                // (the user's "F") never goes through our own Enter-grab at all, so a diagnostic
                // placed only there never fired for "Cuadro Raido"/"Centro de Mesa"/"Planta
                // Moribunda". This block reacts to selectedGameObject changing regardless of
                // which path set it, so it actually captures every case.
                if (Main.DebugMode && grabbedSeat == null)
                {
                    var grabbedPlaceable = selectObj.selectedGameObject.GetComponent<Placeable>();
                    if (grabbedPlaceable != null)
                    {
                        DebugLogger.LogState($"DecorationMode: grabbed item category - placeableAnywhere={grabbedPlaceable.placeableAnywhere} isPlaceableOnSurface={grabbedPlaceable.isPlaceableOnSurface} isPlaceableOnWall={grabbedPlaceable.isPlaceableOnWall} onlyInAllowedSurfaces={grabbedPlaceable.onlyInAllowedSurfaces} hasItemSpace={grabbedPlaceable.itemSpace != null} hasItemBase={grabbedPlaceable.itemBase != null} currentSurface={(grabbedPlaceable.currentSurface != null ? grabbedPlaceable.currentSurface.gameObject.name : "null")} multipleSkins={grabbedPlaceable.multipleSkins} skinsCount={(grabbedPlaceable.skinsGameObjects.Length != 0 ? grabbedPlaceable.skinsGameObjects.Length : grabbedPlaceable.skins.Length)}");
                        // Round 101: for surface items, log which tables expose a free snap spot for
                        // this exact item - confirms whether the candle/tablecloth has a real snap
                        // target (and where) vs the generic "Surface" it was landing on (snapped=False).
                        if (grabbedPlaceable.isPlaceableOnSurface)
                            WorldNavigationHandler.LogSnapTargets(grabbedPlaceable, 30f);
                    }
                }
            }

            if (_stylePickerActive)
            {
                HandleStylePicker();
                return;
            }

            HandleCursorMovement(selectObj);
            HandleValidPositionFeedback(selectObj);
            HandleSeatSlotGuidance(selectObj);
            HandleSurfaceGuidance(selectObj);
            HandleWallGuidance(selectObj);
            HandleItemSpaceGuidance(selectObj);
            HandleWasdReminder(selectObj);
            HandleStyleTrigger(selectObj);
            HandleConfirmPlacement(selectObj);
        }

        // Round 75: found the real reason the guidance announcement (above) went quiet for
        // 14+ seconds in the user's test - the raw key log showed ONLY W/A/S/D presses the
        // entire time the bench was held, not a single arrow key. WASD moves the PLAYER (which
        // drags the camera, and with it whatever world point the held bench is currently
        // anchored to - explains why the announced distance still drifted around without ever
        // resolving), but only the arrow keys actually move the held bench itself (see
        // HandleCursorMovement). One-time reminder the first time this mix-up is detected per
        // hold, instead of repeating it every frame.
        // Round 97: applies to ALL items again - round 94 restricted it to benches because
        // non-seat items tracked WASD walking back then, but that model was reverted this round
        // (all items now move only via arrows), so WASD is once more "the wrong keys" for every
        // held item, and the reminder is correct universally.
        private void HandleWasdReminder(SelectObject selectObj)
        {
            if (_wasdReminderGivenThisHold) return;

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                _wasdReminderGivenThisHold = true; // already using the right keys
                return;
            }

            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A)
                || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D))
            {
                _wasdReminderGivenThisHold = true;
                // Round 92: was hardcoded "o banco" - this method fires for ANY held item now
                // (confirmed in log: a user holding the plant heard "Pra mover o banco..." and,
                // reasonably, found that confusing). Wording generalized.
                ScreenReader.Say("Pra mover o item, use as setas do teclado, não W A S D", interrupt: true);
            }
        }

        // Round 74: found the real reason "soltou fora" kept happening even after the snap
        // logic itself was fixed - the player had NO feedback on how close the held bench
        // actually was to a slot while nudging it with the arrow keys (the existing "Lugar pra
        // banco" announcement is based on the PLAYER's position, which doesn't move while
        // standing still moving just the cursor). Announces distance/direction in tiles toward
        // the nearest empty slot, in the same "X pra direita, Y pra baixo" phrasing already used
        // for the door/bed navigation guidance elsewhere, updating only when the rounded tile
        // offset actually changes.
        //
        // Round 76 fix: re-picking "nearest empty slot" on every check flip-flopped between two
        // similarly-close slots as the bench moved (confirmed in log: distance oscillated
        // "9 pra direita"/"10 pra direita" back and forth, never converging - "me levando a
        // lugar nenhum"). Locking onto ONE target slot per hold (re-picked only if it becomes
        // unavailable) so the guidance always points at a fixed, consistent destination.
        private const float SeatSlotGuidanceCheckInterval = 0.3f;
        private const float SeatSlotGuidanceSearchRadius = 30f;
        // Round 97: tightened from 15 to 6 - FindNearestValidPosition now calls the game's own
        // IsObjectInValidLocation (with a Physics2D.SyncTransforms) per candidate, more expensive
        // than the old pure-tile scan, so a smaller box keeps the worst case (no valid spot found,
        // full scan re-run every 0.3s while the item is far from any wall) bounded. The painting's
        // wall sits ~2.5 units from where it spawns, well within 6.
        private const float WallGuidanceSearchRadius = 6f;

        private void HandleSeatSlotGuidance(SelectObject selectObj)
        {
            GameObject beingPlaced = selectObj.selectedGameObject;
            Seat seat = WorldNavigationHandler.FindSeatForPlaceable(beingPlaced);
            if (seat == null) { _lastGuidanceTileOffset = null; _lockedTargetSlot = null; _lockedTargetTable = null; return; }

            if (Time.unscaledTime - _lastGuidanceCheckTime < SeatSlotGuidanceCheckInterval) return;
            _lastGuidanceCheckTime = Time.unscaledTime;

            if (_lockedTargetSlot == null || !WorldNavigationHandler.IsSlotEmpty(_lockedTargetSlot))
            {
                // Round 77 diagnostic - the lock kept getting dropped seconds after being set,
                // with no key pressed in between (confirmed via the raw key log). Logging the
                // transition itself, paired with WorldNavigationHandler.IsSlotEmpty's own log of
                // WHY it rejected the old slot, to find the real cause instead of guessing again.
                if (Main.DebugMode && _lockedTargetSlot != null) DebugLogger.LogState($"DecorationMode: locked slot dropped (was pos={_lockedTargetSlot.transform.position}), re-picking");
                _lockedTargetSlot = WorldNavigationHandler.FindNearestEmptySlot(beingPlaced.transform.position, SeatSlotGuidanceSearchRadius, out _lockedTargetTable);
                if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: locked slot -> {(_lockedTargetSlot != null ? _lockedTargetSlot.transform.position.ToString() : "null")}");
            }
            var slot = _lockedTargetSlot;
            if (slot == null) { _lastGuidanceTileOffset = null; return; }

            Vector3 target = WorldNavigationHandler.GetSeatTargetPosition(slot, _lockedTargetTable);
            Vector3 cur = beingPlaced.transform.position;
            var offset = new Vector2Int(
                Mathf.RoundToInt((target.x - cur.x) / TileSize),
                Mathf.RoundToInt((target.y - cur.y) / TileSize));

            // Round 81 diagnostic - SetMouseOffset(zero) (round 79) did NOT fix it (user: "mesmo
            // erro"). Re-read Placeable.SetPosition in the decompiled source: it only applies
            // the new position unconditionally when "currentSurface" is null - if currentSurface
            // is NOT null, it gates the update behind IsNewPosOnSurface (which itself requires
            // isPlaceableOnSurface to be true, false for normal floor furniture - meaning if
            // currentSurface somehow got set on this bench, EVERY position update while held
            // would be silently rejected, leaving transform.position stuck). Logging every public
            // field that feeds that exact decision, plus the cursor's own live position (to also
            // rule out our SetCursorPositionFromWorld call itself not sticking), instead of
            // hypothesizing a fifth time.
            // Round 82: surface theory (round 81) is fully ruled out now - currentSurface/
            // surfaceCollider/isPlaceableOnSurface/isOnSurface were null/false on every single
            // logged check last round, never the cause. Trimmed that out; logging
            // _heldIntendedPosition alongside cur now to confirm the direct-transform-write
            // (this round's fix) actually keeps them equal, instead of guessing again.
            if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: guidance calc cur={cur} target={target} heldIntended={_heldIntendedPosition} slotDir={slot.direction} rawOffset=({(target.x - cur.x) / TileSize:F3},{(target.y - cur.y) / TileSize:F3})");

            if (_lastGuidanceTileOffset.HasValue && _lastGuidanceTileOffset.Value == offset) return;
            _lastGuidanceTileOffset = offset;

            if (offset.x == 0 && offset.y == 0)
            {
                ScreenReader.Say("Vaga bem aqui, pode soltar", interrupt: true);
                return;
            }

            var parts = new System.Collections.Generic.List<string>();
            if (offset.x != 0) parts.Add($"{Mathf.Abs(offset.x)} pra {(offset.x > 0 ? "direita" : "esquerda")}");
            if (offset.y != 0) parts.Add($"{Mathf.Abs(offset.y)} pra {(offset.y > 0 ? "cima" : "baixo")}");
            ScreenReader.Say($"Vaga: {string.Join(", ", parts)}", interrupt: true);
        }

        // Round 90: same guidance pattern as HandleSeatSlotGuidance, generalized for decorations
        // that need ANY valid surface (isPlaceableOnSurface) instead of a specific seating slot -
        // paintings/plants/centerpieces etc received from shop orders (see
        // docs/modules/inventory-and-items.md). Only runs for items WITHOUT a Seat (benches keep
        // using the slot-specific guidance above) and WITH isPlaceableOnSurface set, so it stays
        // out of the way of every other item type (including ones placeableAnywhere/on a wall,
        // which don't need this kind of guidance at all).
        private void HandleSurfaceGuidance(SelectObject selectObj)
        {
            GameObject beingPlaced = selectObj.selectedGameObject;
            var placeable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
            if (placeable == null || !placeable.isPlaceableOnSurface)
            {
                _lastSurfaceGuidanceTileOffset = null;
                _lockedTargetSurface = null;
                _lockedTargetSnapPos = null;
                return;
            }

            if (Time.unscaledTime - _lastGuidanceCheckTime < SeatSlotGuidanceCheckInterval) return;
            _lastGuidanceCheckTime = Time.unscaledTime;

            if (placeable.currentSurface != null)
            {
                // Already successfully attached (HandleCursorMovement does this live) - nothing
                // left to guide towards.
                if (_lastSurfaceGuidanceTileOffset.HasValue && _lastSurfaceGuidanceTileOffset.Value == Vector2Int.zero) return;
                _lastSurfaceGuidanceTileOffset = Vector2Int.zero;
                ScreenReader.Say("Superfície bem aqui, pode soltar", interrupt: true);
                return;
            }

            // Round 101: prefer a designated table snap point (where the item actually counts for
            // the mission) over a generic surface centre.
            if (!_lockedTargetSnapPos.HasValue && _lockedTargetSurface == null)
            {
                _lockedTargetSnapPos = WorldNavigationHandler.FindNearestSnapPosition(beingPlaced.transform.position, SeatSlotGuidanceSearchRadius, placeable, out _lockedTargetSurface);
                if (!_lockedTargetSnapPos.HasValue)
                    _lockedTargetSurface = WorldNavigationHandler.FindNearestValidSurface(beingPlaced.transform.position, SeatSlotGuidanceSearchRadius, placeable);
            }
            bool snapTarget = _lockedTargetSnapPos.HasValue;
            if (!snapTarget && _lockedTargetSurface == null) { _lastSurfaceGuidanceTileOffset = null; return; }

            Vector3 target = snapTarget ? _lockedTargetSnapPos.Value : _lockedTargetSurface.transform.position;
            Vector3 cur = beingPlaced.transform.position;
            var offset = new Vector2Int(
                Mathf.RoundToInt((target.x - cur.x) / TileSize),
                Mathf.RoundToInt((target.y - cur.y) / TileSize));

            if (_lastSurfaceGuidanceTileOffset.HasValue && _lastSurfaceGuidanceTileOffset.Value == offset) return;
            _lastSurfaceGuidanceTileOffset = offset;

            string here = snapTarget ? "Lugar na mesa bem aqui, pode soltar" : "Superfície bem aqui, pode soltar";
            string lead = snapTarget ? "Lugar na mesa" : "Superfície";
            var parts = new System.Collections.Generic.List<string>();
            if (offset.x != 0) parts.Add($"{Mathf.Abs(offset.x)} pra {(offset.x > 0 ? "direita" : "esquerda")}");
            if (offset.y != 0) parts.Add($"{Mathf.Abs(offset.y)} pra {(offset.y > 0 ? "cima" : "baixo")}");
            ScreenReader.Say(parts.Count > 0 ? $"{lead}: {string.Join(", ", parts)}" : here, interrupt: true);
        }

        // Round 92: same idea as HandleSurfaceGuidance, for items that need a WALL
        // (isPlaceableOnWall) instead of a surface - confirmed necessary by log: the painting
        // ("Cuadro Raido") never found a valid spot across 8 grab attempts. Walls are pure
        // Tilemap-cell data here (no GameObject to search for, confirmed via research), so
        // WorldNavigationHandler.FindNearestWallPoint scans grid points with WorldGrid.
        // ALNFLFCLIEP instead of iterating a scene object list.
        private void HandleWallGuidance(SelectObject selectObj)
        {
            GameObject beingPlaced = selectObj.selectedGameObject;
            var placeable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
            if (placeable == null || !placeable.isPlaceableOnWall || placeable.isPlaceableOnSurface)
            {
                _lastWallFree = null;
                _lockedTargetWallPoint = null;
                _lastWallGuidanceTileOffset = null;
                return;
            }

            if (Time.unscaledTime - _lastGuidanceCheckTime < SeatSlotGuidanceCheckInterval) return;
            _lastGuidanceCheckTime = Time.unscaledTime;

            // Round 103: if the painting is already at a spot the game accepts, that's the signal to
            // drop - announce it (IsObjectInValidLocation is the same gate Deselect uses).
            bool free = placeable.IsObjectInValidLocation(false);
            if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: wall free check cur={beingPlaced.transform.position} free={free} lockedWall={(_lockedTargetWallPoint.HasValue ? _lockedTargetWallPoint.Value.ToString() : "none")}");
            if (free)
            {
                _lockedTargetWallPoint = null; // arrived; drop the directional lock
                if (_lastWallFree.HasValue && _lastWallFree.Value) return;
                _lastWallFree = true;
                _lastWallGuidanceTileOffset = null;
                ScreenReader.Say("Parede livre aqui, pode soltar", interrupt: true);
                return;
            }
            _lastWallFree = false;

            // Not at a valid spot - guide the player toward the nearest FREE wall tile. Round 102's
            // mistake was removing this guidance: the log showed the painting stuck at y~906 (in the
            // room) while valid walls are at y~910, with no cue to move up. Uses the stable
            // bounds-geometry scan (FindNearestValidWallPosition) so it points at a real,
            // unoccupied, placeable wall spot.
            if (!_lockedTargetWallPoint.HasValue)
            {
                _lockedTargetWallPoint = WorldNavigationHandler.FindNearestValidWallPosition(placeable, WallGuidanceSearchRadius);
                if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: wall lock -> {(_lockedTargetWallPoint.HasValue ? _lockedTargetWallPoint.Value.ToString() : "none")}");
            }
            if (!_lockedTargetWallPoint.HasValue)
            {
                if (_lastWallGuidanceTileOffset.HasValue) return;
                _lastWallGuidanceTileOffset = Vector2Int.zero;
                ScreenReader.Say("Nenhuma parede livre por perto, ande mais pra perto de uma parede", interrupt: true);
                return;
            }

            Vector3 target = _lockedTargetWallPoint.Value;
            Vector3 cur = beingPlaced.transform.position;
            var wallOffset = new Vector2Int(
                Mathf.RoundToInt((target.x - cur.x) / TileSize),
                Mathf.RoundToInt((target.y - cur.y) / TileSize));
            if (_lastWallGuidanceTileOffset.HasValue && _lastWallGuidanceTileOffset.Value == wallOffset) return;
            _lastWallGuidanceTileOffset = wallOffset;

            var wallParts = new System.Collections.Generic.List<string>();
            if (wallOffset.x != 0) wallParts.Add($"{Mathf.Abs(wallOffset.x)} pra {(wallOffset.x > 0 ? "direita" : "esquerda")}");
            if (wallOffset.y != 0) wallParts.Add($"{Mathf.Abs(wallOffset.y)} pra {(wallOffset.y > 0 ? "cima" : "baixo")}");
            ScreenReader.Say(wallParts.Count > 0 ? $"Parede livre: {string.Join(", ", wallParts)}" : "Parede livre bem aqui, pode soltar", interrupt: true);
        }

        // Round 96: user confirmed the plant works now but asked for the same "acertividade"
        // (precision/reliability) the bench and wall/surface already have via real directional
        // guidance + snap-on-confirm - this category previously had neither (round 94 only ever
        // added a silent debug-log diagnostic, no spoken guidance at all). Same lock-onto-one-
        // target pattern as the wall/surface guidance above, using the new
        // FindNearestValidItemSpacePosition search. Kept under a smaller radius than the wall/
        // surface searches since each candidate here is more expensive (temporarily moves the
        // real object per candidate, not a pure function).
        private const float ItemSpaceGuidanceSearchRadius = 6f;
        private Vector3? _lockedTargetItemSpacePosition;
        private Vector2Int? _lastItemSpaceGuidanceTileOffset;
        private float _lastItemSpaceDiagTime = -999f;

        private void HandleItemSpaceGuidance(SelectObject selectObj)
        {
            GameObject beingPlaced = selectObj.selectedGameObject;
            var placeable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
            if (placeable == null || placeable.itemSpace == null || placeable.isPlaceableOnSurface || placeable.isPlaceableOnWall
                || WorldNavigationHandler.FindSeatForPlaceable(beingPlaced) != null)
            {
                _lockedTargetItemSpacePosition = null;
                _lastItemSpaceGuidanceTileOffset = null;
                return;
            }

            if (Time.unscaledTime - _lastGuidanceCheckTime < SeatSlotGuidanceCheckInterval) return;
            _lastGuidanceCheckTime = Time.unscaledTime;

            if (placeable.IsObjectInValidLocation(false))
            {
                if (_lastItemSpaceGuidanceTileOffset.HasValue && _lastItemSpaceGuidanceTileOffset.Value == Vector2Int.zero) return;
                _lastItemSpaceGuidanceTileOffset = Vector2Int.zero;
                ScreenReader.Say("Lugar livre bem aqui, pode soltar", interrupt: true);
                return;
            }

            if (!_lockedTargetItemSpacePosition.HasValue)
            {
                _lockedTargetItemSpacePosition = WorldNavigationHandler.FindNearestValidPosition(placeable, ItemSpaceGuidanceSearchRadius);
            }
            if (!_lockedTargetItemSpacePosition.HasValue)
            {
                _lastItemSpaceGuidanceTileOffset = null;
                // No valid spot found within range at all - keep the diagnostic as a fallback so
                // a future log can still show the real blocker (e.g. clutter/occupancy) instead
                // of going silent.
                if (Main.DebugMode && Time.unscaledTime - _lastItemSpaceDiagTime >= SeatSlotGuidanceCheckInterval)
                {
                    _lastItemSpaceDiagTime = Time.unscaledTime;
                    WorldNavigationHandler.LogItemSpaceValidityDiagnostic(placeable);
                }
                return;
            }

            Vector3 target = _lockedTargetItemSpacePosition.Value;
            Vector3 cur = beingPlaced.transform.position;
            var offset = new Vector2Int(
                Mathf.RoundToInt((target.x - cur.x) / TileSize),
                Mathf.RoundToInt((target.y - cur.y) / TileSize));

            if (_lastItemSpaceGuidanceTileOffset.HasValue && _lastItemSpaceGuidanceTileOffset.Value == offset) return;
            _lastItemSpaceGuidanceTileOffset = offset;

            var parts = new System.Collections.Generic.List<string>();
            if (offset.x != 0) parts.Add($"{Mathf.Abs(offset.x)} pra {(offset.x > 0 ? "direita" : "esquerda")}");
            if (offset.y != 0) parts.Add($"{Mathf.Abs(offset.y)} pra {(offset.y > 0 ? "cima" : "baixo")}");
            ScreenReader.Say(parts.Count > 0 ? $"Lugar livre: {string.Join(", ", parts)}" : "Lugar livre bem aqui, pode soltar", interrupt: true);
        }

        // Round 91: accessible replacement for the native "Estilo" (T key, Placeable.NextSkin)
        // skin/style cycling - that native action just silently advances an index with no
        // announcement, useless for a screen-reader user. Offers a real list (arrows + Enter)
        // instead. Only handles the simple skins/skinsGameObjects array case (confirmed via
        // research: Placeable.NextSkin branches to a more complex "skinVariationGropus" path for
        // some items, which toggles several skins on/off in combination rather than picking one
        // of N - out of scope here until we confirm any of our items actually use it).
        private void HandleStyleTrigger(SelectObject selectObj)
        {
            if (!Input.GetKeyDown(KeyCode.T)) return;
            GameObject beingPlaced = selectObj.selectedGameObject;
            var placeable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
            if (placeable == null || !placeable.multipleSkins || !placeable.canCycleSkin) return;
            if (placeable.skinVariationGropus.Count > 0) return;

            int count = placeable.skinsGameObjects.Length != 0 ? placeable.skinsGameObjects.Length : placeable.skins.Length;
            if (count <= 1) return;

            _stylePickerPlaceable = placeable;
            _stylePickerCount = count;
            // The native key press that opened this also reaches Placeable's own input handling
            // in the same frame and may have already advanced the index by one - reading it AFTER
            // is deliberate, so the list opens on whatever the live index actually is right now.
            _stylePickerOriginalIndex = placeable.GetSkinIndex();
            _stylePickerIndex = _stylePickerOriginalIndex;
            _stylePickerActive = true;
            ScreenReader.Say($"Lista de estilos. Estilo {_stylePickerIndex + 1} de {_stylePickerCount}. Setas pra navegar, Enter pra escolher, Esc pra cancelar.", interrupt: true);
        }

        private void HandleStylePicker()
        {
            var placeable = _stylePickerPlaceable;
            if (placeable == null) { _stylePickerActive = false; return; }

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                _stylePickerIndex = (_stylePickerIndex + 1) % _stylePickerCount;
                ScreenReader.Say($"Estilo {_stylePickerIndex + 1} de {_stylePickerCount}", interrupt: true);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _stylePickerIndex = (_stylePickerIndex - 1 + _stylePickerCount) % _stylePickerCount;
                ScreenReader.Say($"Estilo {_stylePickerIndex + 1} de {_stylePickerCount}", interrupt: true);
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                placeable.ChangeSkin(_stylePickerIndex);
                ScreenReader.Say("Estilo aplicado", interrupt: true);
                _stylePickerActive = false;
                _stylePickerPlaceable = null;
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                placeable.ChangeSkin(_stylePickerOriginalIndex);
                ScreenReader.Say("Estilo cancelado", interrupt: true);
                _stylePickerActive = false;
                _stylePickerPlaceable = null;
            }
        }

        // Searches near the PLAYER, not the mouse cursor - found via user report: the
        // proximity announcement (player-position-based) said a bench was right there, but
        // Enter (originally cursor-position-based) never found it. Root cause: the game's
        // mouse cursor has no reason to be anywhere near the player for someone who never
        // moves a physical mouse - it could be parked anywhere left over from wherever it
        // last was (screen center, a menu, etc.), completely disconnected from the avatar's
        // world position. Searching by player position instead removes that disconnect
        // entirely for the grab step.
        private const float GrabRadius = TileSize * 3f;

        private void HandleGrab(SelectObject selectObj)
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;

            Vector3 playerPos = PlayerController.GetPlayerPosition(1);
            Collider2D[] hits = Physics2D.OverlapCircleAll(playerPos, GrabRadius);
            var candidates = new System.Collections.Generic.List<Placeable>();
            foreach (var hit in hits)
            {
                var placeable = hit.GetComponent<Placeable>() ?? hit.GetComponentInParent<Placeable>();
                if (placeable == null || candidates.Contains(placeable)) continue;
                candidates.Add(placeable);
            }
            candidates.Sort((a, b) => Vector3.Distance(playerPos, a.transform.position).CompareTo(Vector3.Distance(playerPos, b.transform.position)));

            if (candidates.Count == 0)
            {
                ScreenReader.Say("Nada pra pegar aqui perto de você", interrupt: true);
                return;
            }

            // User's explicit report: heard "Banco" announced nearby, tried to grab, but the
            // CLOSEST Placeable turned out to be something unrelated and unselectable (a
            // "Grifo"/torneira, confirmed by log) - so the grab failed even though the actual
            // bench was right there too, just a bit further away. Trying every candidate in
            // distance order instead of giving up after the single closest one.
            Placeable grabbed = null;
            foreach (var candidate in candidates)
            {
                if (selectObj.SelectPlaceable(candidate)) { grabbed = candidate; break; }
            }

            if (grabbed != null)
            {
                // Cursor starts wherever it was last left (mouse or a previous keyboard
                // move) - snapping it to the object just grabbed gives arrow-key movement a
                // sensible, predictable starting point instead of jumping from some
                // unrelated leftover position.
                CursorManager.SetCursorPositionFromWorld(1, grabbed.transform.position);
                // Round 79: found the real reason arrow moves only ever produced a one-frame
                // blip before snapping back (confirmed in log: cur stayed fixed at one position
                // for many checks, flickered to the adjacent tile for exactly one check, then
                // reverted - with no key pressed in between). Placeable.GetNewPosition computes
                // finalPos = cursor position + a private "mouse offset" field, which the game
                // recalibrates to whatever keeps the bench's CURRENT position unchanged whenever
                // it thinks the real mouse is active - effectively cancelling our cursor moves
                // out one cycle later. SetMouseOffset (public) lets us force that offset to zero,
                // so finalPos becomes exactly the cursor position with nothing fighting it.
                grabbed.SetMouseOffset(Vector3.zero);
                // Round 81: that alone didn't fix it (still reverted). Re-read Placeable.
                // SetPosition - it only applies the freshly computed position unconditionally
                // when "currentSurface" is null; otherwise it gates the update behind
                // IsNewPosOnSurface, which requires isPlaceableOnSurface (false for ordinary
                // floor furniture) - meaning if currentSurface is set on this bench for any
                // reason, EVERY position update while held would be silently rejected, exactly
                // matching "stuck at one position". RemoveFromSurface is the game's own method
                // for "this item is no longer associated with a surface" (used elsewhere when
                // picking items up) - calling it here is a no-op if currentSurface was already
                // null, or the actual fix if it wasn't.
                grabbed.RemoveFromSurface(false);
                // Round 82: neither of the above fully fixed it. The round-81 diagnostic ruled
                // out the surface theory completely (currentSurface/surfaceCollider/
                // isPlaceableOnSurface/isOnSurface were null/false on EVERY single check) - and
                // revealed something more fundamental: CursorManager.GetCursorWorldPosition()
                // itself returned a value completely unrelated to the bench's actual
                // transform.position (different numbers entirely, not just lagging by one
                // frame), and BOTH independently flickered between two values 0.5 apart with no
                // key pressed. The cursor/offset system this whole feature was built on is not
                // a reliable source of truth here, for reasons that resisted three rounds of
                // reading the decompiled source. Giving up on cooperating with it: we now own
                // the held object's intended position directly in our own field, and force it
                // onto the transform every frame ourselves (see HandleCursorMovement) instead of
                // asking the engine's own WhileSelected/SetPosition to do it.
                _heldIntendedPosition = grabbed.transform.position;
                // Round 85: round 84's fix (moving seat.transform too) still didn't work.
                // Logging the real GameObject hierarchy now instead of guessing a sixth time -
                // see WorldNavigationHandler.LogBuildSquareHierarchy for why.
                Seat grabbedSeatForDiag = WorldNavigationHandler.FindSeatForPlaceable(grabbed.gameObject);
                if (grabbedSeatForDiag != null) WorldNavigationHandler.LogBuildSquareHierarchy(grabbedSeatForDiag, grabbed.gameObject);
                // Round 91: the "grabbed item category" diagnostic moved to Update()'s
                // selectedGameObject-changed block (fires for ANY entry point, not just this
                // one) - see the comment there for why.
            }
            else
            {
                ScreenReader.Say("Não consegui pegar nada aqui - tentei tudo perto de você", interrupt: true);
            }
            // Round 72 note: "name" alone (e.g. "1135 - Banco Grande(Clone)") is the prefab's
            // catalog id, shared by EVERY clone of that furniture - it does NOT prove "same
            // physical bench" across two log lines. Logging GetInstanceID() too so a future log
            // can tell apart "grabbed the same bench again" from "grabbed a different bench that
            // just looks/sounds identical" - relevant to the user's "saw Banco 8 in two places"
            // report, which this method alone can't disprove or confirm yet.
            if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: grab attempt near player {playerPos}, {candidates.Count} candidate(s) -> {(grabbed != null ? $"{grabbed.gameObject.name} (instanceId={grabbed.GetInstanceID()})" : "nenhum")}");
        }

        // Round 82: stopped going through CursorManager/GetCursorWorldPosition as the source of
        // truth for where the held bench is - the round-81 diagnostic showed it reporting a
        // value with no relationship to the bench's actual transform.position, both flickering
        // independently. We track the intended position ourselves (_heldIntendedPosition, set at
        // grab time) and force it onto the transform directly, every frame (not just on a key
        // press), so nothing the engine does on other frames can revert it.
        private void HandleCursorMovement(SelectObject selectObj)
        {
            if (!_heldIntendedPosition.HasValue) return;
            GameObject beingPlaced = selectObj.selectedGameObject;
            if (beingPlaced == null) return;

            // Round 97: ALL items now use the same pure virtual-cursor model the bench has always
            // used (arrows move it, fully decoupled from walking). Round 94-96's "non-seat items
            // track the player walking" experiment was reverted - the log + user feedback proved
            // it caused the exact "the cursor and my position aren't being compared" confusion:
            // the item floated wherever the game spawned it while the guidance compared THAT to
            // the target, unrelated to where the player stood. One consistent model (the one the
            // user already understands from the bench) is both more accurate and less confusing.
            Vector3 pos = _heldIntendedPosition.Value;
            // Round 91: GetKeyDown only fires once per physical press, no matter how long the
            // key stays down - user reported movement "won't go very fast" since every tile
            // needed a fresh release+press. Hold-to-repeat: a single tap still moves exactly one
            // tile (isFreshPress), holding past ArrowRepeatInitialDelay repeats every
            // ArrowRepeatInterval - same UX as a text caret/cursor held down.
            KeyCode? heldDirection = null;
            bool isFreshPress = false;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { heldDirection = KeyCode.LeftArrow; isFreshPress = true; }
            else if (Input.GetKeyDown(KeyCode.RightArrow)) { heldDirection = KeyCode.RightArrow; isFreshPress = true; }
            else if (Input.GetKeyDown(KeyCode.UpArrow)) { heldDirection = KeyCode.UpArrow; isFreshPress = true; }
            else if (Input.GetKeyDown(KeyCode.DownArrow)) { heldDirection = KeyCode.DownArrow; isFreshPress = true; }
            else if (Input.GetKey(KeyCode.LeftArrow)) heldDirection = KeyCode.LeftArrow;
            else if (Input.GetKey(KeyCode.RightArrow)) heldDirection = KeyCode.RightArrow;
            else if (Input.GetKey(KeyCode.UpArrow)) heldDirection = KeyCode.UpArrow;
            else if (Input.GetKey(KeyCode.DownArrow)) heldDirection = KeyCode.DownArrow;

            bool shouldMove = heldDirection.HasValue && (isFreshPress || Time.unscaledTime >= _nextArrowRepeatTime);
            if (shouldMove)
            {
                _nextArrowRepeatTime = Time.unscaledTime + (isFreshPress ? ArrowRepeatInitialDelay : ArrowRepeatInterval);
                Vector3 step = Vector3.zero;
                switch (heldDirection.Value)
                {
                    case KeyCode.LeftArrow: step = new Vector3(-TileSize, 0); break;
                    case KeyCode.RightArrow: step = new Vector3(TileSize, 0); break;
                    case KeyCode.UpArrow: step = new Vector3(0, TileSize); break;
                    case KeyCode.DownArrow: step = new Vector3(0, -TileSize); break;
                }
                pos += step;
            }
            _heldIntendedPosition = pos;

            CursorManager.SetCursorPositionFromWorld(1, pos);
            var placeable = beingPlaced.GetComponent<Placeable>();
            if (placeable != null)
            {
                placeable.SetMouseOffset(Vector3.zero);
                placeable.SetPosition(1, placeable.attachedToPlayer, placeable.snapToGrid, true);
            }
            beingPlaced.transform.position = pos;
            // Round 84: round 83's SetPosition call didn't fully fix it either (still "não
            // associou", second bench still computed the same target). Confirmed via the
            // diagnostic that Seat's own GameObject is genuinely independent and never follows
            // the Placeable - and re-reading Seat.GetNeighbourTable confirmed it searches from
            // Seat's OWN private "buildSquare" field, not anything on the Placeable. Unity only
            // auto-propagates position through real parent-child transform hierarchy - since
            // Seat isn't a child of the Placeable, nothing we do to the Placeable's transform
            // was ever going to move it. Moving it explicitly here closes the gap directly.
            Seat heldSeat = WorldNavigationHandler.FindSeatForPlaceable(beingPlaced);
            if (heldSeat != null) heldSeat.transform.position = pos;

            // Round 90: generic decorations that need a surface (paintings/plants/centerpieces -
            // isPlaceableOnSurface == true) normally auto-attach via Placeable.PEFFMJOMPMN, called
            // every frame from WhileSelected - but that method finds the surface using the
            // CURSOR's position (CursorManager.GetCursorWorldPosition() + mouse offset), the exact
            // same source already proven unreliable for keyboard movement (round 82). Taking
            // direct ownership here too, same pattern as the bench/seat fix: find the surface at
            // OUR known-good position instead, and call the engine's own
            // Add/RemoveFromPlaceableSurface so everything else downstream (sorting, snap-to-spot
            // visuals) still works exactly like the native flow.
            if (placeable != null && placeable.isPlaceableOnSurface)
            {
                // Round 102: when guidance has locked a free TABLE snap point and the item is close
                // to it, pull the item exactly ONTO that point and attach there - this makes it
                // actually snap (snappedToPosition=true) AND marks the spot used, so the NEXT candle
                // targets a different free spot instead of piling onto the same place (the user's
                // "todas no mesmo lugar"). Reuses the guidance lock (_lockedTargetSnapPos) so there's
                // no expensive per-frame scene scan here; FindSurfaceAtPosition is a cheap point
                // overlap.
                if (_lockedTargetSnapPos.HasValue && Vector3.Distance(pos, _lockedTargetSnapPos.Value) <= TileSize * 1.5f)
                {
                    Vector3 sp = _lockedTargetSnapPos.Value;
                    var snapSurface = WorldNavigationHandler.FindSurfaceAtPosition(sp, placeable);
                    beingPlaced.transform.position = sp;
                    CursorManager.SetCursorPositionFromWorld(1, sp);
                    Physics2D.SyncTransforms();
                    if (snapSurface != null && snapSurface != placeable.currentSurface)
                        placeable.AddPlaceableToSurface(snapSurface, true);
                    if (placeable.currentSurface != null) placeable.currentSurface.UpdatePosition(placeable);
                    _heldIntendedPosition = beingPlaced.transform.position;
                }
                else if (!_lockedTargetSnapPos.HasValue)
                {
                    // No snap target for this item (a genuine non-snap surface item, e.g. a painting
                    // on a plain shelf) - keep the generic live attach (round 90).
                    var surfaceHere = WorldNavigationHandler.FindSurfaceAtPosition(pos, placeable);
                    if (surfaceHere != null && surfaceHere != placeable.currentSurface)
                        placeable.AddPlaceableToSurface(surfaceHere, true);
                    else if (surfaceHere == null && placeable.currentSurface != null)
                        placeable.RemoveFromSurface(true);
                    if (placeable.currentSurface != null)
                    {
                        placeable.currentSurface.UpdatePosition(placeable);
                        if (!placeable.snappedToPosition) beingPlaced.transform.position = pos;
                    }
                }
                else if (placeable.currentSurface != null && !placeable.snappedToPosition)
                {
                    // A snap target exists but we're not near it yet, and the item got stuck loose
                    // on a generic surface - detach so it doesn't place loose (snapped=False).
                    placeable.RemoveFromSurface(true);
                    beingPlaced.transform.position = pos;
                }
                if (Main.DebugMode)
                    DebugLogger.LogState($"DecorationMode: surface live - snapped={placeable.snappedToPosition} surface=\"{(placeable.currentSurface != null ? placeable.currentSurface.gameObject.name : "null")}\" lockedSnap={(_lockedTargetSnapPos.HasValue ? _lockedTargetSnapPos.Value.ToString() : "none")} pos={beingPlaced.transform.position}");
            }
        }

        private void HandleValidPositionFeedback(SelectObject selectObj)
        {
            var placeable = selectObj.selectedGameObject != null ? selectObj.selectedGameObject.GetComponent<Placeable>() : null;
            if (placeable == null) return;

            // Round 90: grepped the ENTIRE decompiled/ tree for "canBePlaced = " while
            // researching generic decoration placement - it is NEVER reassigned anywhere outside
            // its own declaration (always true). This announcement has been reading a dead field
            // this whole time. Deselect/DeselectAction's real gate is the PUBLIC
            // IsObjectInValidLocation(bool) - calling that directly instead. Harmless for benches
            // (itemSpace path, same answer in practice) and the only way to get a correct answer
            // at all for non-seat decorations that go through the itemBase/surface/wall path.
            bool canBePlaced = placeable.IsObjectInValidLocation(false);
            if (_lastCanBePlaced.HasValue && _lastCanBePlaced.Value == canBePlaced) return;
            _lastCanBePlaced = canBePlaced;

            ScreenReader.Say(canBePlaced ? "Posição válida" : "Posição inválida", interrupt: true);
        }

        // User's explicit request: stop requiring pixel-perfect manual alignment onto a table
        // slot (confirmed via LogSeatPlacementDiagnostics that the engine's own tolerance is a
        // tight 0.225 units AND depends on facing direction) - more forgiving than that, the
        // player only needs to get the bench reasonably close to a free slot before pressing
        // Enter. Widened from 3 to 5 tiles after round 73's "soltou fora" report - 3 tiles
        // turned out to miss real attempts (not confirmed exactly how close they were, since
        // this round's log never reached the snap branch at all - see the diagnostic added
        // below for next time).
        private const float SnapToSlotRadius = TileSize * 5f;

        private void HandleConfirmPlacement(SelectObject selectObj)
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;

            GameObject beingPlaced = selectObj.selectedGameObject;
            Seat seat = WorldNavigationHandler.FindSeatForPlaceable(beingPlaced);

            if (seat != null)
            {
                // Round 76: use the SAME slot the guidance announcement just locked onto
                // (re-searching fresh here could disagree with what the player was just told,
                // especially right after the oscillation fix above) - only fall back to a fresh
                // search if guidance never ran/locked for some reason.
                SeatingGroup slot;
                Table ownerTable;
                if (_lockedTargetSlot != null && WorldNavigationHandler.IsSlotEmpty(_lockedTargetSlot))
                {
                    slot = _lockedTargetSlot;
                    ownerTable = _lockedTargetTable;
                }
                else
                {
                    slot = WorldNavigationHandler.FindNearestEmptySlot(beingPlaced.transform.position, SnapToSlotRadius, out ownerTable);
                }
                if (slot != null && Vector3.Distance(beingPlaced.transform.position, WorldNavigationHandler.GetSeatTargetPosition(slot, ownerTable)) > SnapToSlotRadius)
                {
                    slot = null;
                }
                if (slot == null && Main.DebugMode)
                {
                    // Round 73: every confirm attempt this round had snapped=False - never
                    // even reached the snap logic. Logging the nearest slot's real distance
                    // (regardless of radius) so next round's log shows whether the radius is
                    // still too tight or the player genuinely wasn't near any table.
                    WorldNavigationHandler.LogNearestSlotDistance(beingPlaced.transform.position);
                }
                if (slot != null)
                {
                    // Snap the bench exactly onto the slot and face it the direction the slot
                    // expects (SetDirection - the same public API the native R/rotate key
                    // calls) instead of confirming whatever position/facing the player happened
                    // to be holding. Deferred to next frame - see the _pendingSnapDeselect
                    // handling at the top of Update(). Target position is offset from the
                    // slot's own marker (see GetSeatTargetPosition) - using the marker directly
                    // overlapped the table every time (round 71's bug).
                    Vector3 targetPos = WorldNavigationHandler.GetSeatTargetPosition(slot, ownerTable);
                    CursorManager.SetCursorPositionFromWorld(1, targetPos);
                    var placeable = beingPlaced.GetComponent<Placeable>();
                    if (placeable != null)
                    {
                        // Round 88: round 87's exact-search-point diagnostic measured the search
                        // landing 1.77 units away from the real table - and the direction of that
                        // gap gave away the actual bug. slot.direction is "which side of the table
                        // this slot is on" (confirmed: a Left slot sat to the table's LEFT in
                        // world coords) - used directly as the bench's OWN facing, that makes the
                        // bench face Left too, i.e. AWAY from the table on its right, not toward
                        // it. Seat.GetNeighbourTable searches 0.5 units in whatever direction the
                        // Placeable is currently facing, so facing away from the table guarantees
                        // it searches past empty space instead. Facing the opposite of the slot's
                        // side - toward the table - is what makes that search land on it.
                        placeable.SetDirection(Utils.ABNPPDOGEPM(slot.direction), false);
                        placeable.SetMouseOffset(Vector3.zero);
                        placeable.SetPosition(1, placeable.attachedToPlayer, placeable.snapToGrid, true);
                    }
                    beingPlaced.transform.position = targetPos;
                    // Round 84: same fix as HandleCursorMovement - Seat is its own independent
                    // GameObject (confirmed via the diagnostic), never moved by anything done to
                    // the Placeable's transform. GetNeighbourTable (which runs right after
                    // Deselect, via _pendingSeatCheck) searches from Seat's own position - has to
                    // be moved here directly or it'll keep searching from the wrong spot.
                    seat.transform.position = targetPos;
                    _heldIntendedPosition = targetPos;
                    _pendingSnapDeselect = beingPlaced;
                    _pendingSnapSlot = slot;
                    if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: snap attempt - target={targetPos} slotPos={slot.transform.position} slotDir={slot.direction} beforePos={beingPlaced.transform.position}");
                    WorldNavigationHandler.LogBuildSquareHierarchy(seat, beingPlaced);
                    return;
                }
            }

            // Round 94: user explicitly asked to make surface decorations (centerpiece, etc.) as
            // forgiving as the bench - confirmed via log the centerpiece DID eventually work, but
            // took repeated re-grabs and pixel-perfect nudging to land exactly on the surface's
            // own reference point, feeling "muito inconstante" compared to the bench's snap-on-
            // Enter. Same pattern as the seat snap above: if a valid surface is within radius,
            // snap straight onto it (AddPlaceableToSurface, same public API HandleCursorMovement
            // already calls live) instead of requiring the player to manually nudge onto the
            // exact spot before Enter does anything.
            var surfacePlaceable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
            if (surfacePlaceable != null && surfacePlaceable.isPlaceableOnSurface)
            {
                // Round 101: prefer a designated TABLE snap spot (SnapToPosition) over a generic
                // surface. The round-100 log showed the candle attaching to a plain "Surface" with
                // snapped=False, so it never counted for the mission. Snapping it exactly onto a
                // free snap point makes AddPlaceableToSurface set snappedToPosition = true.
                // Round 106: user asked to make the candle as forgiving as the painting - FORCE it
                // onto the nearest valid snap point on Enter even if it's far ("ao soltar mesmo q
                // não esteja perto arrastar para la e posicionar"). Widened from SnapToSlotRadius
                // (2.5u) to the whole-room guidance radius so distance is no longer a barrier; the
                // candle drags to the nearest free table spot the mission accepts.
                SurfaceSortOrder snapSurface;
                Vector3? snapPos = WorldNavigationHandler.FindNearestSnapPosition(beingPlaced.transform.position, SeatSlotGuidanceSearchRadius, surfacePlaceable, out snapSurface);
                if (snapPos.HasValue)
                {
                    if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: candle force-snap - cur={beingPlaced.transform.position} target={snapPos.Value}");
                    SnapAndConfirm(selectObj, beingPlaced, surfacePlaceable, snapPos.Value, snapSurface, "surfaceSnap");
                    return;
                }

                // Fall back to any valid surface (items that genuinely don't use snap spots).
                var surface = surfacePlaceable.currentSurface != null
                    ? surfacePlaceable.currentSurface
                    : _lockedTargetSurface;
                if (surface == null || !surface.IsItemAllowed(surfacePlaceable.itemSetup.item, surfacePlaceable, surfacePlaceable.surfaceGOInstantiated))
                {
                    surface = WorldNavigationHandler.FindNearestValidSurface(beingPlaced.transform.position, SnapToSlotRadius, surfacePlaceable);
                }
                if (surface != null && Vector3.Distance(beingPlaced.transform.position, surface.transform.position) <= SnapToSlotRadius)
                {
                    SnapAndConfirm(selectObj, beingPlaced, surfacePlaceable, surface.transform.position, surface, "surface");
                    return;
                }
            }

            // Round 97: merged the round-95 wall snap and round-96 itemSpace snap into one block -
            // both find the nearest position the game's own IsObjectInValidLocation accepts (via
            // FindNearestValidPosition) and snap onto it. Covers the wall case (painting) and the
            // generic floor-space case (plant), with the same forgiving snap-on-confirm the bench
            // has.
            var genericPlaceable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
            if (genericPlaceable != null && genericPlaceable.currentSurface == null
                && !genericPlaceable.isPlaceableOnSurface
                && (genericPlaceable.isPlaceableOnWall || genericPlaceable.itemSpace != null)
                && WorldNavigationHandler.FindSeatForPlaceable(beingPlaced) == null)
            {
                if (genericPlaceable.isPlaceableOnWall)
                {
                    // Round 104: user explicitly asked to FORCE the painting onto the nearest valid
                    // wall spot to where they are ("sete forçado se for necessário em um lugar válido
                    // o mais próximo q eu estiver, desde q seja na parede"). On Enter, find the
                    // nearest geometrically-valid wall position (FindNearestValidWallPosition, the
                    // exact FNPBNFFEBAF replica) and snap onto it; the settle-retry confirms it with
                    // the game's own check across real frames. Falls back to the current spot only if
                    // no valid wall is in range at all.
                    Vector3 wallTarget = beingPlaced.transform.position;
                    if (!genericPlaceable.IsObjectInValidLocation(false))
                    {
                        Vector3? validWall = WorldNavigationHandler.FindNearestValidWallPosition(genericPlaceable, WallGuidanceSearchRadius);
                        if (validWall.HasValue) wallTarget = validWall.Value;
                    }
                    if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: wall confirm - cur={beingPlaced.transform.position} target={wallTarget}");
                    SnapAndConfirm(selectObj, beingPlaced, genericPlaceable, wallTarget, null, "wall");
                    return;
                }

                // itemSpace (plant): keep the forgiving nearest-valid search - it works for this case.
                Vector3? validSpot = _lockedTargetItemSpacePosition;
                if (!validSpot.HasValue || Vector3.Distance(beingPlaced.transform.position, validSpot.Value) > ItemSpaceGuidanceSearchRadius)
                {
                    validSpot = WorldNavigationHandler.FindNearestValidPosition(genericPlaceable, ItemSpaceGuidanceSearchRadius);
                }
                if (validSpot.HasValue && Vector3.Distance(beingPlaced.transform.position, validSpot.Value) <= SnapToSlotRadius)
                {
                    SnapAndConfirm(selectObj, beingPlaced, genericPlaceable, validSpot.Value, null, "itemSpace");
                    return;
                }
            }

            bool placed = selectObj.Deselect();
            HandlePlacementResult(placed, beingPlaced, null);
        }

        // Round 99: snap onto the validated target and Deselect in ONE frame. This replaces the
        // round-96 deferred-by-one-frame approach: the deferred frame let the game's own
        // Placeable.WhileSelected run in between and disturb the object (detach the surface, or
        // move the wall item via the cursor pipeline we bypass), which is exactly why the painting
        // and tablecloth reported "Posição válida" yet Deselect returned false. The original reason
        // for deferring (Collider2D.bounds lagging a frame behind a same-frame transform write) is
        // now handled by Physics2D.SyncTransforms() instead, so a single frame is both correct and
        // free of that interference. LogDeselectGate dumps every input to Placeable.Deselect's real
        // gate (IsObjectInValidLocation true/false, canBePlaced, currentSurface, physicalSpace)
        // right before the call, so if it still fails the log names the exact failing sub-check.
        private void SnapAndConfirm(SelectObject selectObj, GameObject beingPlaced, Placeable placeable, Vector3 targetPos, SurfaceSortOrder surface, string label)
        {
            CursorManager.SetCursorPositionFromWorld(1, targetPos);
            placeable.SetMouseOffset(Vector3.zero);
            if (surface != null) placeable.AddPlaceableToSurface(surface, true);
            placeable.SetPosition(1, placeable.attachedToPlayer, placeable.snapToGrid, true);
            beingPlaced.transform.position = targetPos;
            Physics2D.SyncTransforms();
            _heldIntendedPosition = targetPos;
            if (Main.DebugMode)
            {
                DebugLogger.LogState($"DecorationMode: {label} snap+confirm - target={targetPos} surface={(surface != null ? surface.gameObject.name : "none")}");
                WorldNavigationHandler.LogDeselectGate(placeable, label);
            }
            bool placed = selectObj.Deselect();
            if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: {label} immediate deselect -> {placed} finalPos={beingPlaced.transform.position}");
            if (placed)
            {
                HandlePlacementResult(true, beingPlaced, null);
                return;
            }
            // Round 100: don't give up - the physical-space trigger list is likely just stale from
            // the same-frame teleport (see the _pendingSettleDeselect field comment). Arm a
            // few-frame settle retry that keeps the item pinned here until physics catches up.
            _pendingSettleDeselect = beingPlaced;
            _pendingSettlePos = targetPos;
            _pendingSettleSurface = surface;
            _pendingSettleLabel = label;
            _pendingSettleFramesLeft = SettleRetryFrames;
        }

        private void ClearPendingSettle()
        {
            _pendingSettleDeselect = null;
            _pendingSettleSurface = null;
            _pendingSettleLabel = null;
            _pendingSettleFramesLeft = 0;
        }

        private void HandlePlacementResult(bool placed, GameObject beingPlaced, SeatingGroup snappedSlot)
        {
            if (placed)
            {
                // User's explicit concern: a position can be free of overlap
                // (canBePlaced) without actually being USEFUL - a bench only counts for
                // the "assentos disponíveis" goal if it ends up next to a table
                // (Seat.table, set automatically on placement via Seat.GetNeighbourTable -
                // confirmed reading Seat.cs). Give that more specific feedback for seats
                // instead of just "Item solto" - deferred a frame, see _pendingSeatCheck.
                //
                // Bug found via log (before the snap-to-slot feature existed): this ALWAYS came
                // back null - confirmed cause is the same one found elsewhere: Seat has its own
                // "public Placeable placeable;" field, so it's not on the same GameObject as
                // beingPlaced. GetComponent<Seat>() could never find it. Searching all seats by
                // their .placeable reference instead (shared helper, same one used elsewhere).
                Seat seat = WorldNavigationHandler.FindSeatForPlaceable(beingPlaced);
                if (seat != null)
                {
                    _pendingSeatCheck = seat;
                    _pendingSeatCheckSlotNumber = snappedSlot != null ? WorldNavigationHandler.GetSlotNumber(snappedSlot) : (int?)null;
                }
                else
                {
                    // Round 90: same "tell the player whether it actually achieved the thing it
                    // needed, not just whether it dropped without overlapping" concern as the
                    // bench's Seat.table check above, generalized to surface decorations.
                    // Round 101: items that snap to a designated SnapToPosition (candles,
                    // tablecloths, centerpieces - the "Coloque seus novos itens na taverna"
                    // objective) only COUNT when they land on that real table spot, not just
                    // loose on any surface. snappedToPosition (set in AddPlaceableToSurface)
                    // distinguishes the two, so the blind player knows whether it actually
                    // registered.
                    var placeable = beingPlaced != null ? beingPlaced.GetComponent<Placeable>() : null;
                    // Round 108: say the item's NAME when placing it (user request), and drop the
                    // "mas não num ponto de mesa" wording - now that Enter force-snaps to the
                    // nearest valid table spot (round 106), reporting "not a table point" is both
                    // confusing and rarely true.
                    string itemName = placeable != null && placeable.itemSetup != null && placeable.itemSetup.item != null
                        ? placeable.itemSetup.item.IABAKHPEOAF()
                        : null;
                    if (string.IsNullOrEmpty(itemName)) itemName = "Item";
                    string msg;
                    if (placeable != null && placeable.snappedToPosition)
                        msg = $"{itemName} encaixado na mesa";
                    else if (placeable != null && placeable.currentSurface != null)
                        msg = $"{itemName} colocado na superfície";
                    else
                        msg = $"{itemName} colocado";
                    ScreenReader.Say(msg, interrupt: true);
                }
                _lastSelectedGameObject = null;
                _lastCanBePlaced = null;
            }
            else
            {
                ScreenReader.Say("Não posso soltar aqui", interrupt: true);
            }
            if (Main.DebugMode) DebugLogger.LogState($"DecorationMode: confirm placement -> {placed} snapped={snappedSlot != null}");
        }
    }
}
