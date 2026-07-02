using System;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// User's explicit request: move items between an open chest, the player's inventory,
    /// and the 1-8 hotbar without a mouse. Operates on whatever SlotUI is currently selected
    /// via the existing generic Tab/arrow navigation (KeyboardUINavigator already reads its
    /// content aloud) - this only adds the missing ACTION.
    ///
    /// Ctrl+Enter on a slot in the player's inventory sends that item to the open chest
    /// (first free/stackable slot there); Ctrl+Enter on a slot in the chest sends it to the
    /// player's inventory (first free/stackable slot). Ctrl+1-8 sends the focused inventory
    /// item to that exact hotbar slot (swaps if occupied). Shift+1-8 sends that hotbar slot's
    /// item back to the inventory (first free slot).
    ///
    /// All move logic verified by reading decompiled/Container.cs, Slot.cs and Utils.cs
    /// directly (not guessed from method names) after a research agent's first pass got the
    /// chest class wrong (see docs/modules/inventory-and-items.md) and invented a
    /// CommonReferences method name that doesn't exist.
    /// </summary>
    public class InventoryTransferHandler
    {
        // CORRECTION: `BLMADJJOAKA = new Slot[8]` in ActionBarInventory.Awake (cited here
        // before as proof of "8 slots") turned out to be a local-coop mirror array for
        // player 2, not the real hotbar - confirmed live the user could select all the way
        // to "Uso rápido 10" via the game's own native scrolling with no error, so the real
        // ActionBarInventory.slots (Container's own field, sized in the prefab/editor, not
        // visible in decompiled code) holds at least 10. Key '1' maps to index 0 ...
        // key '0' maps to index 9, matching the user's own original guess ("vai do 1 ao 9",
        // even a bit short). Bounds-checked against the live array length below regardless,
        // since this still isn't independently confirmed beyond "at least 10."
        private static readonly KeyCode[] HotbarKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8,
            KeyCode.Alpha9, KeyCode.Alpha0,
        };

        // The game's own input module wipes Unity's EventSystem.currentSelectedGameObject
        // back to null every frame without a gamepad (confirmed - see KeyboardUINavigator's
        // class doc, the exact reason it tracks its own virtual cursor instead of trusting
        // Unity's). Relying on EventSystem here meant this whole handler silently never
        // found a focused slot - the real cause of the first test round doing nothing.
        // Main.cs now passes the navigator's own tracked selection in directly.
        public void Update(GameObject focusedGameObject)
        {
            _focusedGameObject = focusedGameObject;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (ctrl && Input.GetKeyDown(KeyCode.Return))
            {
                HandleContainerTransfer();
                return;
            }

            for (int i = 0; i < HotbarKeys.Length; i++)
            {
                if (!Input.GetKeyDown(HotbarKeys[i])) continue;

                if (ctrl) HandleAssignToHotbar(i);
                else if (shift) HandleReturnFromHotbar(i);
            }
        }

        private GameObject _focusedGameObject;
        private bool _hotbarSelectionHooked;
        // Round 101: user wants the selected hotbar (uso rápido) slot to announce its quantity
        // and update live as the item is consumed (10 candles -> 9 -> 8...). Selecting a slot
        // fires OnSelectionChanged, but USING an item doesn't - only the Stack drops - so we poll
        // the currently-selected slot each frame and announce the new count when it decreases.
        private int _polledHotbarIndex = -1;
        private int _polledHotbarStack;

        // User's explicit request: plain 1-8 (no modifier) is the game's own native control
        // to select/equip a hotbar item - it already works, but says nothing, so there's no
        // way to confirm what's now in hand without looking. ActionBarInventory.OnSelectionChanged
        // (confirmed public field in decompiled ActionBarInventory.cs) fires whenever the
        // game itself changes the selection, independent of any UI being open - hooking it
        // once, lazily, since the player's PlayerInventory doesn't exist yet when this
        // handler is constructed in Main.OnInitializeMelon.
        public void EnsureHotbarSelectionAnnouncer()
        {
            if (!_hotbarSelectionHooked)
            {
                var pi = PlayerInventory.GetPlayer(1);
                if (pi?.actionBarInventory == null) return;
                pi.actionBarInventory.OnSelectionChanged += OnHotbarSelectionChanged;
                _hotbarSelectionHooked = true;
            }

            PollSelectedHotbarStack();
        }

        // Round 101: announce the running count of the selected hotbar item as it's consumed.
        // Only the selected slot is polled (set in OnHotbarSelectionChanged) and only a DECREASE
        // is announced - re-stocking/assigning is already covered by other announcements, and a
        // bare rising number while shopping would be noise. Says just the number ("9", "8"...),
        // matching the user's "com 9 8 e assim por diante" while actively using items.
        private void PollSelectedHotbarStack()
        {
            if (_polledHotbarIndex < 0) return;
            var slots = PlayerInventory.GetPlayer(1)?.actionBarInventory?.slots;
            if (slots == null || _polledHotbarIndex >= slots.Length) { _polledHotbarIndex = -1; return; }

            var slot = slots[_polledHotbarIndex];
            int stack = (slot?.itemInstance != null) ? slot.Stack : 0;
            if (stack < _polledHotbarStack)
            {
                _polledHotbarStack = stack;
                ScreenReader.Say(stack > 0 ? stack.ToString() : "acabou", interrupt: true);
            }
            else if (stack > _polledHotbarStack)
            {
                // keep the baseline in sync if it grew (e.g. a stack was topped up) without
                // announcing, so a later decrease is measured from the right number.
                _polledHotbarStack = stack;
            }
        }

        private void OnHotbarSelectionChanged(int playerNum, int newIndex)
        {
            if (playerNum != 1) return;

            // Confirmed live (debug log) the real bug behind "balde não aparece no uso" /
            // "esfregão apareceu em outro lugar": ActionBarInventory.SetCurrentSlotSelected
            // fires this event BEFORE updating its own internal selected-index field - so
            // calling GetSelectedItem() from inside this callback (as before) read whatever
            // slot was selected PREVIOUSLY, not the one newIndex refers to. Indexing
            // directly with the newIndex this event handed us instead, bypassing
            // GetSelectedItem()/GetCurrentSlotSelected() entirely.
            var playerInventory = PlayerInventory.GetPlayer(1);
            var slots = playerInventory?.actionBarInventory?.slots;
            Slot selectedSlot = (slots != null && newIndex >= 0 && newIndex < slots.Length) ? slots[newIndex] : null;
            int stack = (selectedSlot?.itemInstance != null) ? selectedSlot.Stack : 0;
            string itemName = (selectedSlot?.itemInstance != null && stack > 0) ? selectedSlot.itemInstance.LHBPOPOIFLE()?.IABAKHPEOAF() : null;

            // Round 101: track the selected slot so PollSelectedHotbarStack can announce the
            // running count as the item is used up.
            _polledHotbarIndex = (slots != null && newIndex >= 0 && newIndex < slots.Length) ? newIndex : -1;
            _polledHotbarStack = stack;

            string announcement = string.IsNullOrEmpty(itemName)
                ? $"Uso rápido {newIndex + 1} vazio"
                : (stack > 1 ? $"{itemName} selecionado, {stack}" : $"{itemName} selecionado");
            ScreenReader.Say(announcement, interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: hotbar selection changed to index {newIndex} (\"{itemName}\" x{stack})");
        }

        private Slot GetFocusedSlot()
        {
            if (_focusedGameObject == null) return null;

            var slotUI = _focusedGameObject.GetComponent<SlotUI>() ?? _focusedGameObject.GetComponentInParent<SlotUI>();
            return slotUI?.IHENCGDNPBL;
        }

        // CORRECTION this same round: tried simplifying this to MainUI.GetCurrentContainer
        // (thinking it was the general "whatever container is open" lookup, since
        // GameInventoryUI's own auto-transfer handler calls it) - confirmed by reading
        // every caller of its setters (GBEIHIDIDAD/LIIGLHOFDBK) that it's ONLY ever written
        // by DrinkDispenserUI/Fireplace/OfferingStatueUI, never by ContainerUI/ItemContainer
        // - a basic chest never touches it. Reverted to checking the chest UI directly via
        // its own ALPOKDOCCGM property (confirmed correct in round 44).
        private static Container GetOpenStationContainer(int playerNum)
        {
            var big = BigContainerUI.Get(playerNum);
            if (big != null && big.IsOpen()) return big.ALPOKDOCCGM;

            var small = SmallContainerUI.Get(playerNum);
            if (small != null && small.IsOpen()) return small.ALPOKDOCCGM;

            // Round 116: it's NOT only chests that receive items from the inventory. Non-chest
            // stations (the drinks dispenser / beer tap, etc.) set MainUI.GetCurrentContainer when
            // open. Use it, but ONLY while such a station window is actually open, so a stale value
            // from a closed station is never used (the round-44 hazard the comment above warned of).
            // Round 134 fix: for the SERVICE BARREL, DrinkDispenserUI.GIMEBIPKLMM calls LIIGLHOFDBK
            // which has a game bug — both if-branches check `== 0`, so playerNum=1 never sets
            // MainUI.GetCurrentContainer. Get the DrinkDispenser directly from the UI via reflection
            // before falling back to the (potentially stale) GetCurrentContainer.
            if (IsNonChestStationOpen(playerNum))
            {
                var ddUI = DrinkDispenserUI.Get(playerNum);
                if (ddUI != null && ddUI.IsOpen())
                {
                    var field = typeof(DrinkDispenserUI).GetField("MJMNGLHDJFH",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field?.GetValue(ddUI) is Container directContainer)
                    {
                        if (Main.DebugMode) DebugLogger.LogState(
                            $"InventoryTransfer: DrinkDispenserUI open, got dispenser via reflection (type={directContainer.GetType().Name}, slots={directContainer.slots?.Length ?? 0})");
                        return directContainer;
                    }
                    if (Main.DebugMode) DebugLogger.LogState("InventoryTransfer: DrinkDispenserUI open but MJMNGLHDJFH reflection returned null");
                }
                var stationContainer = MainUI.GetCurrentContainer(playerNum);
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: NonChestStation fallback container={stationContainer?.GetType().Name ?? "null"}");
                if (stationContainer != null) return stationContainer;
            }
            return null;
        }

        private static bool IsNonChestStationOpen(int playerNum)
        {
            // DrinkDispenserUI may have windowType=EWindow.Disabled and not appear in
            // GetCurrentOpenWindows — check IsOpen() directly first.
            var ddUI = DrinkDispenserUI.Get(playerNum);
            if (ddUI != null && ddUI.IsOpen()) return true;
            var windows = MainUI.GetCurrentOpenWindows(playerNum);
            if (windows == null) return false;
            foreach (var w in windows)
            {
                if (w is DrinkDispenserUI) return true;
            }
            return false;
        }

        // The oven/malt crafting screen. FindObjectsOfType (not GetCurrentOpenWindows) to match
        // KeyboardUINavigator - GameCraftingUI isn't always registered in the open-window list.
        // Only called on a Ctrl+Enter keypress, so the scan cost is negligible.
        private static bool IsCraftingUIOpen()
        {
            var crafting = UnityEngine.Object.FindObjectsOfType<GameCraftingUI>();
            foreach (var c in crafting)
                if (c != null && c.gameObject.activeInHierarchy) return true;
            return false;
        }

        private static bool IsChestOpen(int playerNum)
        {
            var big = BigContainerUI.Get(playerNum);
            if (big != null && big.IsOpen()) return true;
            var small = SmallContainerUI.Get(playerNum);
            return small != null && small.IsOpen();
        }

        // Confirmed via live log: SlotUI.container is null for the inventory items shown
        // inside MainPanelUI's own "Inventário" tab (a different display path than a
        // chest's GameInventoryUI) - relying on it made every inventory-sourced slot read
        // as "not the player's inventory" there, causing Ctrl+Enter to wrongly announce
        // "retirado do baú" while just reshuffling the item within the same inventory, and
        // Ctrl+1-8 to always refuse with "isn't in the player's inventory". Checking the
        // Slot's own identity against the inventory's actual slots array instead - this
        // doesn't depend on which UI happened to set the SlotUI's container field.
        private static bool IsPlayerInventorySlot(Slot slot, PlayerInventory playerInventory)
        {
            return Array.IndexOf(playerInventory.inventory.slots, slot) >= 0;
        }

        private void HandleContainerTransfer()
        {
            const int playerNum = 1;

            // [54] Oven / crafting station: a Crafter is NOT a Container - ingredients aren't held
            // in station slots like a chest. The game shift-clicks them into the recipe's
            // modifier/ingredient slots (and back out) via SlotUI.DoAutomaticTransfer. So when the
            // crafting UI is open, Ctrl+Enter on the focused slot runs that transfer (same action
            // Enter does in the navigator), giving the uniform "Ctrl+Enter adiciona / remove" the
            // user expects at EVERY station - the chest-container path below would just say
            // "Nenhuma estação aberta" since there's no Container to find.
            if (_focusedGameObject != null && IsCraftingUIOpen())
            {
                var craftSlot = _focusedGameObject.GetComponent<SlotUI>() ?? _focusedGameObject.GetComponentInParent<SlotUI>();
                if (craftSlot != null)
                {
                    try { craftSlot.DoAutomaticTransfer(playerNum); }
                    catch (System.Exception ex) { if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: oven DoAutomaticTransfer threw: {ex.Message}"); }
                    if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+Enter oven auto-transfer on \"{_focusedGameObject.name}\"");
                    // Announce what's now in the modifier + whether it's ready (instead of a blind
                    // "movido"). Note: the game routes an inventory item into the FOCUSED modifier
                    // slot - the first Ctrl+Enter on an unfocused slot only focuses it, so the state
                    // may read unchanged until a second press (known game quirk, to be smoothed later).
                    string state = KeyboardUINavigator.CraftingModifierState();
                    ScreenReader.Say(string.IsNullOrEmpty(state) ? "Ingrediente movido" : state, interrupt: true);
                    return;
                }
            }

            Slot sourceSlot = GetFocusedSlot();
            if (sourceSlot == null || sourceSlot.itemInstance == null)
            {
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+Enter - no focused slot with an item (focusedGO={(_focusedGameObject == null ? "null" : _focusedGameObject.name)}, slot={(sourceSlot == null ? "null" : "set, empty")})");
                return;
            }

            var playerInventory = PlayerInventory.GetPlayer(playerNum);
            if (playerInventory == null) return;

            // Round 117: decide direction by whether the focused slot is one of the STATION's own
            // slots, NOT by checking the player-inventory array. A dispenser shows a FILTERED copy
            // of the inventory whose Slot objects aren't in playerInventory.inventory.slots, so the
            // old IsPlayerInventorySlot check returned false for a real inventory item -> wrong
            // direction ("retirou da estação" for an inventory item, item went nowhere). The
            // station container's slots ARE the real ones, so this is reliable: in station -> go to
            // inventory; otherwise (an inventory/filtered slot) -> go to the station.
            Container station = GetOpenStationContainer(playerNum);
            bool sourceIsStation = station != null && Array.IndexOf(station.slots, sourceSlot) >= 0;
            Container target = sourceIsStation ? playerInventory.inventory : station;

            if (target == null)
            {
                // Sending inventory -> station needs a station (chest, dispenser, ...) actually
                // open, same as the real game - not a bug.
                ScreenReader.Say("Nenhuma estação aberta", interrupt: true);
                if (Main.DebugMode) DebugLogger.LogState("InventoryTransfer: no open station container found for Ctrl+Enter");
                return;
            }

            // Say "baú" only for a real chest; otherwise "estação" (dispenser, menu table, etc.).
            string place = IsChestOpen(playerNum) ? "no baú" : "na estação";
            string from = IsChestOpen(playerNum) ? "do baú" : "da estação";
            string actionLabel = sourceIsStation ? $"retirado {from}" : $"colocado {place}";
            if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+Enter sourceIsStation={sourceIsStation} station={(station != null ? "set" : "null")}");

            // Round 134: DrinkDispenser.AddItemInstance goes through a container-level type
            // filter (DOOILKJLDHD) that may reject items acceptable at the individual slot
            // level. When moving INTO a DrinkDispenser, try its slots directly (slot[0]=liquid,
            // slot[1]=cups) before falling back to the full container-level AddItemInstance path.
            if (!sourceIsStation && target is DrinkDispenser dispenser)
            {
                if (TryMoveToDispenserSlot(playerNum, sourceSlot, dispenser, actionLabel))
                    return;
            }

            MoveStack(playerNum, sourceSlot, target, actionLabel);
        }

        private void HandleAssignToHotbar(int hotbarIndex)
        {
            const int playerNum = 1;
            Slot sourceSlot = GetFocusedSlot();
            if (sourceSlot == null || sourceSlot.itemInstance == null)
            {
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+{hotbarIndex + 1} - no focused slot with an item");
                return;
            }

            var playerInventory = PlayerInventory.GetPlayer(playerNum);
            if (playerInventory == null) return;

            // Only makes sense from the player's own inventory - a chest slot has no
            // business jumping straight to the hotbar. Most likely reason this fires: the
            // chest window is still focused, not the inventory one - use seta
            // direita/esquerda (KeyboardUINavigator) to switch first.
            if (!IsPlayerInventorySlot(sourceSlot, playerInventory))
            {
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+{hotbarIndex + 1} - focused slot isn't in the player's inventory");
                return;
            }

            if (hotbarIndex >= playerInventory.actionBarInventory.slots.Length) return;
            Slot hotbarSlot = playerInventory.actionBarInventory.slots[hotbarIndex];

            // Confirmed live: a hotbar slot can end up with a non-null itemInstance but
            // Stack 0 (a "ghost" reference, likely leftover from this same play session's
            // earlier, since-fixed GHCDPAJHKOI bug) - itemInstance != null alone isn't a
            // reliable "has something" check. MEODNPFJDMH (tried first) turned out to be a
            // no-op here: it removes exactly 1 unit, and Slot's own stack-change tracking
            // only clears itemInstance on an actual transition INTO 0 - since Stack was
            // already 0, decrementing further changes nothing, so itemInstance never got
            // cleared. Setting the field directly instead - safe specifically because this
            // only ever runs when Stack <= 0, i.e. there is nothing real in the slot for the
            // player to lose; a slot the player actually configured always has Stack >= 1
            // and is never touched by this branch.
            if (hotbarSlot.itemInstance != null && hotbarSlot.Stack <= 0)
            {
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+{hotbarIndex + 1} - hotbar slot {hotbarIndex} had a ghost itemInstance (Stack 0) - clearing it");
                hotbarSlot.itemInstance = null;
            }

            // Used Slot.GHCDPAJHKOI (the game's own slot-exchange helper) here before -
            // confirmed live this was the bug behind "esfregão no 1, balde no 2, mas o
            // shift trazia o item errado": GHCDPAJHKOI's special case for the hotbar's
            // singleItem slots only ever does anything when the TARGET is already EMPTY -
            // assigning to an OCCUPIED hotbar slot silently does nothing at all (no swap,
            // no error), while this handler still announced success because it read
            // hotbarSlot.itemInstance right after with no idea the call had no-opped.
            // Doing the exchange ourselves, one explicit step at a time, so every step's
            // outcome is visible and nothing is assumed.
            if (hotbarSlot.itemInstance != null)
            {
                int freed = MoveUnitsToContainer(playerNum, hotbarSlot, playerInventory.inventory);
                if (hotbarSlot.itemInstance != null)
                {
                    ScreenReader.Say("Sem espaço para tirar o item do uso rápido", interrupt: true);
                    if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+{hotbarIndex + 1} - couldn't free hotbar slot {hotbarIndex} (freed {freed}, still has \"{hotbarSlot.itemInstance.LHBPOPOIFLE()?.IABAKHPEOAF()}\")");
                    return;
                }
            }

            int moved = MoveUnitsToSlot(playerNum, sourceSlot, hotbarSlot);
            if (moved <= 0)
            {
                ScreenReader.Say("Sem espaço", interrupt: true);
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+{hotbarIndex + 1} - hotbar slot {hotbarIndex} refused \"{sourceSlot.itemInstance?.LHBPOPOIFLE()?.IABAKHPEOAF()}\"");
                return;
            }

            string itemName = hotbarSlot.itemInstance?.LHBPOPOIFLE()?.IABAKHPEOAF();
            ScreenReader.Say(string.IsNullOrEmpty(itemName) ? $"Uso rápido {hotbarIndex + 1}" : $"{itemName} no uso rápido {hotbarIndex + 1}", interrupt: true);
            // Diagnostic only (no fix yet): confirmed live the item can vanish again within
            // ~1s with zero other input in between - need to know if a LATER read still sees
            // the same Slot object (would mean something else cleared it) or a different one
            // entirely (would mean the array/Slot got replaced/rebuilt underneath us).
            if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Ctrl+{hotbarIndex + 1} - assigned \"{itemName}\" ({moved} unit(s)) to hotbar slot {hotbarIndex} [slotObj={hotbarSlot.GetHashCode()} containerObj={playerInventory.actionBarInventory.GetHashCode()} arrayObj={playerInventory.actionBarInventory.slots.GetHashCode()} stackAfter={hotbarSlot.Stack}]");
        }

        private void HandleReturnFromHotbar(int hotbarIndex)
        {
            const int playerNum = 1;
            var playerInventory = PlayerInventory.GetPlayer(playerNum);
            if (playerInventory == null) return;
            if (hotbarIndex >= playerInventory.actionBarInventory.slots.Length) return;

            Slot hotbarSlot = playerInventory.actionBarInventory.slots[hotbarIndex];
            if (hotbarSlot.itemInstance == null || hotbarSlot.Stack <= 0)
            {
                // User reported a Shift+N press that said nothing at all - this is the only
                // silent path here. Saying something every time (even "vazio") instead of
                // staying silent, so a press never looks like it just didn't register.
                // Also covers a "ghost" itemInstance (non-null but Stack 0) - confirmed live
                // this can happen, and there's nothing real there to move out either way.
                if (hotbarSlot.itemInstance != null) hotbarSlot.itemInstance = null;
                // Same diagnostic as the assign side - compare slotObj/arrayObj here against
                // the values logged at assignment to see whether this is really the same
                // Slot we wrote to moments ago.
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Shift+{hotbarIndex + 1} - found empty [slotObj={hotbarSlot.GetHashCode()} containerObj={playerInventory.actionBarInventory.GetHashCode()} arrayObj={playerInventory.actionBarInventory.slots.GetHashCode()}]");
                ScreenReader.Say($"Uso rápido {hotbarIndex + 1} vazio", interrupt: true);
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: Shift+{hotbarIndex + 1} - hotbar slot {hotbarIndex} is empty");
                return;
            }

            MoveStack(playerNum, hotbarSlot, playerInventory.inventory, $"retirado do uso rápido {hotbarIndex + 1}");
        }

        // Container.AddItemInstance (confirmed by reading Utils.CHMEHDFPGCI) only places ONE
        // unit per call - looping it ourselves (instead of using AddItemInstances, which
        // does the same loop but doesn't report how many actually fit) lets us know exactly
        // how much to remove from the source, in case the target doesn't have room for the
        // whole stack.
        private static int MoveUnitsToContainer(int playerNum, Slot sourceSlot, Container target)
        {
            var item = sourceSlot.itemInstance;
            int originalCount = sourceSlot.Stack;
            int moved = 0;

            for (int i = 0; i < originalCount; i++)
            {
                if (target.AddItemInstance(playerNum, item, false, false) == null) break;
                moved++;
            }

            if (moved > 0) sourceSlot.Stack -= moved;
            return moved;
        }

        // Same idea as MoveUnitsToContainer, but targeting one specific, already-known Slot
        // (the hotbar slot) instead of asking a Container to pick one - Slot.FEEOFAGCONJ is
        // the same single-unit "try to add" primitive Container.AddItemInstance itself calls
        // internally, just applied directly instead of via a container-wide search.
        private static int MoveUnitsToSlot(int playerNum, Slot sourceSlot, Slot targetSlot)
        {
            var item = sourceSlot.itemInstance;
            int originalCount = sourceSlot.Stack;
            int moved = 0;

            for (int i = 0; i < originalCount; i++)
            {
                if (!targetSlot.FEEOFAGCONJ(playerNum, item)) break;
                moved++;
            }

            if (moved > 0) sourceSlot.Stack -= moved;
            return moved;
        }

        // Round 134: DrinkDispenser container-level filter (CHMEHDFPGCI/DOOILKJLDHD) may
        // reject items that individual dispenser slots actually accept. Try each slot directly
        // via FEEOFAGCONJ (same per-slot check used internally by AddItemInstance), bypassing
        // the container-level filter. Slot[0]=liquid/water, slot[1]=cups.
        private static bool TryMoveToDispenserSlot(int playerNum, Slot sourceSlot, DrinkDispenser dispenser, string actionLabel)
        {
            if (dispenser.slots == null || dispenser.slots.Length == 0) return false;
            var item = sourceSlot.itemInstance;
            if (item == null) return false;
            for (int i = 0; i < dispenser.slots.Length; i++)
            {
                var targetSlot = dispenser.slots[i];
                if (targetSlot == null) continue;
                if (Main.DebugMode) DebugLogger.LogState(
                    $"InventoryTransfer: DrinkDispenser direct slot[{i}] try for \"{item.LHBPOPOIFLE()?.IABAKHPEOAF()}\" (type={item.LHBPOPOIFLE()?.GetType().Name ?? "null"})");
                int moved = MoveUnitsToSlot(playerNum, sourceSlot, targetSlot);
                if (moved > 0)
                {
                    string itemName = item.LHBPOPOIFLE()?.IABAKHPEOAF();
                    string namePart = string.IsNullOrEmpty(itemName) ? "Item" : itemName;
                    string message = moved < sourceSlot.Stack + moved
                        ? $"{namePart} {actionLabel} ({moved} unidade{(moved > 1 ? "s" : "")})"
                        : $"{namePart} {actionLabel}";
                    ScreenReader.Say(message, interrupt: true);
                    return true;
                }
                if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: DrinkDispenser slot[{i}] rejected");
            }
            return false;
        }

        private static void MoveStack(int playerNum, Slot sourceSlot, Container target, string actionLabel)
        {
            var item = sourceSlot.itemInstance;
            int originalCount = sourceSlot.Stack;
            int moved = MoveUnitsToContainer(playerNum, sourceSlot, target);

            if (moved <= 0)
            {
                ScreenReader.Say("Sem espaço", interrupt: true);
                if (Main.DebugMode)
                {
                    string itemType = item.LHBPOPOIFLE()?.GetType().Name ?? "null";
                    string containerType = target?.GetType().Name ?? "null";
                    int slotCount = target?.slots?.Length ?? 0;
                    DebugLogger.LogState($"InventoryTransfer: 0 moved of \"{item.LHBPOPOIFLE()?.IABAKHPEOAF()}\" (type={itemType}) ({actionLabel}) - target={containerType} slots={slotCount}");
                }
                return;
            }

            string itemName = item.LHBPOPOIFLE()?.IABAKHPEOAF();
            string namePart = string.IsNullOrEmpty(itemName) ? "Item" : itemName;
            string message = moved < originalCount
                ? $"{namePart} {actionLabel} ({moved} de {originalCount})"
                : $"{namePart} {actionLabel}";

            // TODO próxima rodada: tocar um som próprio aqui (via CustomSounds, não o
            // Sound.GGFJGHHHEJC do próprio jogo - já tivemos 3 tentativas sem sucesso de
            // ouvir esse sistema de áudio do jogo, ver header de CustomSounds.cs) no
            // sucesso do movimento, antes ou junto do ScreenReader.Say abaixo.
            ScreenReader.Say(message, interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"InventoryTransfer: moved {moved}/{originalCount} of \"{itemName}\" ({actionLabel})");
        }
    }
}
