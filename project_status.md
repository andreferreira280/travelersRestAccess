# Project Status - TravellersRestAccess

## Setup Info

- **Game:** Travellers Rest (Early Access)
- **Developer:** Louqou
- **Game directory:** `C:\Users\andre\Downloads\Travellers Rest (Early Access)\Travellers Rest\Windows`
- **Engine:** Unity 2022.3.62f2
- **Architecture:** 64-bit
- **Runtime Type:** net35 → TargetFramework `net472`
- **User experience level:** Little/None programming experience
- **Multilingual:** Yes, auto-detect game language

## Environment Checklist

- [x] MelonLoader installed
- [x] MelonLoader log read (Game Name: `TravellersRest`, Developer: `Louqou`)
- [x] Tolk DLLs present (`Tolk.dll`, `nvdaControllerClient64.dll`)
- [x] .NET SDK installed (10.0.301)
- [x] Decompiler installed (`ilspycmd` 10.1.0.8386)
- [x] `Assembly-CSharp.dll` decompiled to `decompiled/` (1976 files)
- [x] Codebase analysis (Phase 1 of setup-guide.md) - see `docs/game-api.md`
- [x] Feature plan created - see `docs/feature-plan.md`
- [x] Basic mod framework (Phase 2) - builds successfully, DLL auto-copied to game's Mods folder
- [x] Verified in-game: "TravellersRestAccess loaded" announced via NVDA

## Current Focus

- **Active feature (started 2026-06-20):** object/world navigation
  (find and reach doors/objects/characters) - branch
  `feature/worldNavigation`, see `docs/modules/world-object-navigation.md`
  for the staged plan. Stage 1 (coordinate feasibility, debug-log only)
  implemented, not yet tested live.
- **Done (merged to main):** Main Menu + Options (feature-plan.md item 2
  and the "Settings/Options" rough feature) - see
  `docs/modules/main-menu-and-options.md`. A few items still pending
  validation there (screen-effect toggles, Keybind tab) but the feature is
  stable enough to build on.
- **Done (stable, building on top of it now):** New Game setup - two
  parts:
  1. Intro story dialogue (general-purpose narrative/NPC text reader,
     not just the intro) - see `docs/modules/dialogue-system.md`.
     Working and tested: text read correctly (story + ambient NPC
     barks distinguished), Space advances dialogue, Up/Down re-read.
  2. Character Creator screen - see `docs/modules/new-game-setup.md`.
     All confirmed working as of 2026-06-19: generic navigation, text
     field editing/confirm-feedback, gender, color-picker-navigation,
     cursor-return-after-popup, loading-screen announcement, and the
     Space-closes-screen bug (5 attempts, root cause was the screen's
     own `Update()` override, fixed via Harmony patch).
     **Known, paused limitation:** color names are unreliable (user's
     explicit choice to pause investigating further) - picking colors
     itself still works fine, only the spoken name can be wrong.
- **Active feature (started 2026-06-19):** moving past character
  creation into actual gameplay. Two rounds of testing so far - see
  `docs/modules/main-panel-tabs.md` (Inventory/Quests/Recipes/Skills/
  Encyclopedia) and `docs/modules/core-gameplay-navigation.md` (world
  navigation research: zones/interactables/map).
  - Pause menu icon-only buttons (Discord, Unstuck) had no label -
    fixed.
  - **MainPanelUI tab navigation, round 2 fix:** round 1's fix
    (scanning whichever tab content was `activeInHierarchy`) turned
    out wrong - confirmed live ALL tabs stay active simultaneously
    (non-selected ones are just moved off-screen), so Inventory was
    actually navigating a scrambled mix of Inventory+Quests+Recipes+
    Skills+Collections items at once. Re-fixed by reading the game's
    own private "selected tab index" field via reflection instead.
    This should also fix the Quests/Recipes/Skills navigation reports
    from the same round, since they're the same underlying tab strip.
  - **New this round: arrow keys were also moving the character**
    while Inventory/Quests/Recipes/Skills was open. Confirmed in
    decompiled source this is intentional game design (only
    `PauseMenuUI` blocks movement, not `MainPanelUI`) - arrows and
    WASD feed the same movement axis, so our menu navigation and
    character movement were both firing off the same keypress. Asked
    the user how to resolve it; they chose to keep WASD as movement
    always and have arrows be exclusively ours. Fixed via a new
    Harmony patch (`MovementAxisPatch.cs`) that recomputes the
    movement axes from WASD only while one of our nav screens is open.
  - **Encyclopedia category list "bagunçada":** confirmed cause (all
    13 section buttons share one generic unlabeled name), found the
    real per-section name exists as a localization key, but the
    hierarchy order doesn't reliably map to the data array order
    (confirmed live) - added diagnostic logging instead of guessing a
    fix blind. Not yet implemented.
  - World navigation research (zones/interactables/map) continues -
    the action-prompt diagnostic logging (added last round) captured
    its first real sample this round ("[E] Arrumar a Cama"),
    confirming the format - still not wired into an actual announcement
    yet, next round's work.
- Decided with the user: rich environment descriptions (e.g. "a wood
  tavern lit by candles...") will be hand-written for the fixed intro
  content only, not auto-generated via AI vision (out of scope for now).

## Stopping point - 2026-06-25, round 132 (most recent)

ROOT CAUSE of the expel objective not completing - read `T112_CalmarCliente`
(tutorial phase 112). It subscribes two objective handlers:
`CommonReferences.OnCustomerBecomeNuisance` (-> CALM objective) and
`CommonReferences.OnCustomerIsHit` (-> KICK objective). `OnCustomerIsHit` fires
ONLY inside `Customer.KickOut(HitDetection)` (Customer.cs:624), not in
KickWithForce/MarkAsKicked - so the old Delete expelled but never completed the
objective.
- **Delete fixed**: now `BecomeNuisance(true)` (sets the nuisance flag KickOut
  needs + fires OnCustomerBecomeNuisance) then `KickOut(PlayerController
  .GetPlayer(1).hitDetection)` - fires OnCustomerIsHit, counts kickedCustomers,
  flings via HandleSendOut->KickWithForce. Falls back to KickWithForce if the
  player HitDetection is null.
- V (CalmCustomer -> BecomeNuisance) already completes the calm objective.
- Told the user they don't need to send logs (I read Latest.log directly; they
  just need F12 on).
- Pending: P key opens a "help" message that reads once - make it read+close
  (repeatable); needs finding that UI.

Build clean.

## Stopping point - 2026-06-25, round 131

- **NPC-overriding-station name regression FIXED** (#1): the round-119
  `FindClosestAvailableNpc` included Customer, so a station's "Abrir" prompt got
  named after a nearby customer ("Cliente, quer Espetinho: Abrir"). Restricted
  it to CatNPC/MaiNPC only (the dialogue NPCs that genuinely need the override);
  Customers resolve correctly via the focused element already.
- **Mission**: V (CalmCustomer) + Delete (KickWithForce) both count now, but the
  mission stays open. Likely the tutorial wants ONE customer: try-calm (V ->
  BecomeNuisance in phase 112) then expel (Delete) the SAME one. Couldn't read
  the objective text (Mission.cs has no readable text fields; localized/obfusc).
- Open questions put to the user: which key opens the "help" message (reads
  once, wants repeat+close); what G/N/M/U/P/J do (game uses an input map, not
  raw KeyCode); what the objective panel shows when stuck.
- Z bar fallback (round 130) still awaiting a test log
  (`serve Z FALLBACK try on state=...`).

Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 130

User revealed the objective text: "Tente acalmar um cliente insatisfeito" +
"Use o esfregão para expulsar um cliente" - so the mission needs BOTH a calm
ATTEMPT and a kick.
- **V calm fixed for the objective**: reverted from the round-128 direct
  `MFOPJDFMJBN(Neutral)` (which bypassed the game and silently broke the
  objective) to `Customer.CalmCustomer(null)` - the real method E calls (line
  873), which the "tente acalmar" objective tracks. It's probabilistic
  (Neutral, or BecomeNuisance on fail / in the tutorial); either way the attempt
  counts. V announces "Cliente acalmado" or "Tentou acalmar, mas ficou bravo,
  use Delete".
- **Delete** already uses the counting kick (round 128).
- **Z bar discrepancy** still unexplained (log shows only EatingAtTable at the
  Z moment). Added a FALLBACK: when no serveable match, Z/X try
  `ServeCustomer(1,true,tray)` on the nearest matching-kind customer in ANY
  state and log `serve Z FALLBACK try on state=... -> served=...` - definitive
  data next round (and serves if a state I missed is serveable).

Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 129

- **Big table dirt SOLVED**: the round-128 diagnostic proved 0 dirty dishes
  while the big table was visibly dirty. The dirt is the table's DIRT LEVEL
  (`Table.JNHCCCBICDM` = TableDirtLevel Messy/Dirty/VeryDirty from accumulated
  `dirtiness`), a separate system. HandleMopBackspace now scans `_cachedTables`
  for `JNHCCCBICDM >= Messy` and cleans via `Table.SetDirtiness(0f)`. Picks the
  closest of 4: floor stain / seat dish / table dish / table dirt-level.
- **Z validated (no bug)**: the log proved FOOD orders happen at the BAR
  (`WaitingAtBar`) - 0 `OrderInTable/comida` in the entire session. The customers
  Z "didn't serve" were HeadingToBar/HeadingToSeat (not serveable - E can't
  either). Z serves WaitingAtBar food fine (served=True) and the bar order is
  announced. Improved the no-match message to explain (coming / already served /
  none).
- **Mission**: round-128 Delete (BecomeNuisance + KickWithForce) is the
  counting kick; calm (V) doesn't count - confirmed it's a KICK mission.

Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 128

Deep validation (user pushed - rightly):
- **Expel/mission fixed**: Delete was using `FHPAMNEIJLI(true)` which DISABLES
  hitDetection - the wrong nuisance path. The real mop-hit is `BecomeNuisance(true)`
  (enables hitDetection, fires OnCustomerBecomeNuisance) + `KickWithForce(playerPos)`
  (calls MarkAsKicked AND advances the tutorial, `GetCurrentPhaseID() < 168`).
  Delete now uses that. The rowdy mission is to KICK (tutorial phase 112 turns
  CalmCustomer into BecomeNuisance on purpose), so V won't complete it - Delete will.
- **Calm**: `CalmCustomer(null)` (what E calls, line 873) only works on Rowdy &
  not-nuisance; a BeingANuisance customer can't be calmed. V now only calms Rowdy
  ones and tells the user to use Delete for nuisances.
- **Z "didn't serve" validated**: the NO-MATCH log shows those customers were
  `HeadingToBar` (still walking to order) - Z was correct; it's timing. Guidance:
  press Z/X when the order is announced.
- **Big table**: scan still found nothing ("Nada pra limpar"); added a diagnostic
  logging seat/table counts + dirty counts to locate the big-table dirt next log
  (may be a different system than Table.dish[]).

Build clean.

## Stopping point - 2026-06-25, round 127

- **Big table dishes** (#1): big tables hold dirty dishes in `Table.dish[]`
  (DirtyDish[]), NOT `Seat.dirtyDish` - that's why they never cleaned.
  HandleMopBackspace now also scans `_cachedTables`, cleans the nearest active
  table dish by replicating the game's clear (`dish.SetActive(false)` +
  `table.placeable.placeableSurface.RemoveFromSurface(dish.transform)`). Cleans
  the closest of floor stain / seat dish / table dish.
- **Z "inconsistent"** (#4): log showed the food customers were `EatingAtTable`
  (already served) while the OrderInTable ones wanted drinks - Z was correct.
  Root annoyance: DescribeNpc said "quer comida" regardless of state. Now it
  says "quer {item}" only for OrderInTable/WaitingAtBar, "Cliente comendo" for
  EatingAtTable, else "Cliente".
- **Rowdy** (#2/#3): V (calm) and Delete (expel) now scan the live customer
  list by `currentMoodState == Rowdy` (or BeingANuisance) via
  FindNearestRowdyCustomer, not just `customersRowdy`. Added a "Cliente ficou
  bravo, V acalma ou Delete expulsa" alert when the rowdy count grows.

Deferred: customer patience timer, bedroom "Corredor".
Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 126

- **Dirty-dish cleaning fixed** (#1/#3/#7): `Seat.CleanDirtyDish()` only
  `SetActive(false)`s the dish - it never nulls `Seat.dirtyDish`. So the old
  `dirtyDish != null` check was always true (-> "mesa limpa" forever + false
  positives + cleaning ghost dishes instead of the real/far one, e.g. the big
  table). Now requires `dirtyDish.gameObject.activeSelf`. Both-null -> "Nada pra
  limpar".
- **Calm vs mission** (#4): investigated - `TavernServiceManager` has
  `kickedCustomers` + `AddKickedCustomer`, but NO calmed counter. So the rowdy
  mission is to KICK OUT (Delete -> MarkAsKicked -> AddKickedCustomer counts);
  calming (V) is an alternative that doesn't count. Told the user to use Delete
  for the mission.
- **Z "didn't serve 3 skewers"** (#2): log shows those Z presses had
  matching=0 (the food customers weren't OrderInTable/WaitingAtBar at that
  instant; user then served via E). Enhanced the no-match diagnostic to dump
  every customer's state/kind/request to confirm next round.

Deferred: customer patience timer, bedroom "Corredor".
Build clean.

## Stopping point - 2026-06-25, round 125

- **Delete expels rowdy customers** (`HandleExpelKey`): with the mop selected,
  kicks out the nearest `customersRowdy` via the game's `Customer.MarkAsKicked()`
  (requires BeingANuisance, so a still-Rowdy one is pushed there first with
  `FHPAMNEIJLI(true)`). "Cliente expulso". KeyCode.Delete only used by the game
  in inventory drag (MouseSlot), not in the world; handler is in the !anyUiOpen
  block.
- Clarified for the user: V (calm, via MFOPJDFMJBN -> Neutral, reliable) and
  Delete (expel) are both kept - they choose per situation. The game's
  probabilistic player-calm basically never works (a bouncer job), which is why
  round 124 bypassed it.

Current tavern controls: V calm / Delete expel / Z food / X drink / Backspace
clean stain or dirty dish.

Deferred: customer patience timer, bedroom "Corredor".
Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 124

- **V calm now reliable** (#3/#4): log showed `OBGPLACHKHK(null)` always
  returns False (the game's player-calm is probabilistic and basically never
  works for the player - even its E did nothing). HandleCalmKey now sets the
  mood straight to Neutral via `Customer.MFOPJDFMJBN(MoodState.Neutral)`
  (-> UpdateMoodState, removes from customersRowdy).
- **Backspace clears dirty dishes too** (#2): cleans the nearest of a floor
  stain (tavernFloorDirt) OR a dirty seat dish (`Seat.dirtyDish` !=null ->
  `Seat.CleanDirtyDish()`, from `_cachedSeats`). "Mancha limpa" / "Mesa limpa".
- **Orders announced sooner & not missed** (#5): order announcement is now
  per-customer (`_customerOrderAnnounced` set) - fires as soon as the customer
  is OrderInTable/WaitingAtBar with a currentRequest, re-arms when they leave
  the serveable state. Poll 0.5s -> 0.25s.
- **Z food** (#1): log confirms Z serves valid food customers (served=True);
  the unservable ones wanted drinks (X) or were already eating. Announcer now
  aligned with the Z/X serveable states.

Deferred: customer patience timer, bedroom "Corredor".
Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 123

Log nailed the drink mystery: `serve X - request="Copo de água" tray=[]` - the
tray was EMPTY. A cup must be FULLY filled (a DoWork hold) before it lands in
`tray.currentDrinks`; the user had no feedback the fill completed.

- **Drink-on-tray feedback** (#2): HandleTavernServiceAnnouncements now tracks
  `trayHandler.tray.currentDrinks.Count` -> "{drink} na bandeja, aperte X pra
  servir" when it grows. This unblocks X (fill fully, hear it, press X). The
  "Z didn't serve bar customers" (#1) was them wanting drinks (water) - X's job.
- **V calms rowdy** (#3, `HandleCalmKey`): nearest `TavernManager.customersRowdy`
  -> `Customer.OBGPLACHKHK(null)` (the game's "Calm down", probabilistic).
  KeyCode.V unused by game.
- **Backspace mop check** (#4): the log shows the check WORKS (Vela isMop=False
  did NOT clean; Esfregão isMop=True did). Likely the mop stays in the active
  hotbar slot. Asked user for an exact repro; kept the `GetSelectedItem() is Mop`
  check + its diagnostic.

Deferred: customer patience timer, bedroom "Corredor".
Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-25, round 122

Read the actual log (user was right to push). Findings:
- **Z (food) works** (served=True). **X (drink) finds the customer but
  ServeCustomer returns False** - the drink isn't on the tray when X fires.
  Read Tray.MHBHHNCFOEG = `currentDrinks.Remove(request)`, and ItemInstance.
  Equals (line 397) compares by TYPE for real items (the `MLBOMGHINCA(item,
  null)` guard is a null check), so a right-TYPE drink on the tray WOULD match.
  Conclusion: the filled drink isn't reaching `tray.currentDrinks`. Added a
  diagnostic dumping tray.currentDrinks + the request + canBeStacked + state
  ("serve X - request=... tray=[...]") to pin where the drink goes next round
  (user suspects a "mesa de bebidas").

Concrete fixes:
- **Bar orders announced** (#3): HandleTavernServiceAnnouncements now announces
  the order on WaitingAtBar too (not only OrderInTable) - "Cliente no balcão
  quer {item}".
- **New stain alert** (#5): tracks CommonReferences.tavernFloorDirt count ->
  "Mancha nova no chão" when it grows.
- **Backspace** (#6): added a diagnostic logging the selected hotbar item +
  isMop, to see why the mop check misbehaves.

Deferred to next log: the X drink/tray root cause, the Backspace mop check.
Build clean.

## Stopping point - 2026-06-25, round 121

Serving deep-dive (read NABCJBPDMJI, the serve validation):
- **Z/X did the same thing**: classified food/drink by `Customer.preference`
  (unreliable). Now by the ordered item: `currentRequest.JEPBBEBJEFI()` (is a
  drink). Z = food only, X = drink only.
- **3 dispensers all "Dispensador de bebidas"**: differentiated by their drink
  via `DrinkDispenser.lastDrink` -> "Dispensador de bebidas, {drink}" (fallback
  to drinkDispenserId).
- **The tray**: confirmed in code that filling a cup at a dispenser puts the
  drink straight on the player's tray (`DrinkDispenser.TakeDrink` uses
  `trayHandler.tray`). NABCJBPDMJI: table customers need the item on the tray;
  bar customers can take FOOD (`!JEPBBEBJEFI()`) from the inventory but DRINKS
  still need the tray. So the user's blocker was filling the WRONG drink - the
  dispenser differentiation fixes that. Improved the X failure message to guide
  filling the right drink.

Deferred: customer patience timer, bedroom "Corredor", and verifying Z/X serve
bar customers live (diagnostic "serve key ... matching=" in the log).

Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-24, round 120

- **Z/X "didn't work"**: only matched `OrderInTable`; customers waiting at the
  bar are `WaitingAtBar` (the game's ServeCustomer accepts both). HandleServeKeys
  now matches both + logs "serve key ... matching/waiting=...".
- **"servido" inconsistent (2 of 5)**: replaced the 0.5s hasBeenServed poll with
  a subscription to `CommonReferences.GGFJGHHHEJC.OnAnyCustomerServeItem`
  (Action<int,ItemInstance>, fires synchronously inside every serve - E, Z/X,
  employee). Announces "{item} servido" reliably. Removed the poll's served
  announcement and Z/X's own (avoid double); poll still tracks hasBeenServed for
  the satisfied/dissatisfied-on-leave line.
- **"gata" was Mai**: log showed go="MaiNPC" falling to the nearby bar's name.
  DescribeNpc now: MaiNPC -> "Mai" (CatNPC -> "Gato", Customer -> "Cliente...").
- **Backspace cleans nearest stain** (new, `HandleMopBackspace`): if the mop is
  the selected hotbar item (`actionBarInventory.GetSelectedItem() is Mop`),
  Backspace calls `FloorDirt.DestroyFloorDirt()` on the nearest dirt from
  `CommonReferences.tavernFloorDirt` -> "Mancha limpa, faltam N". KeyCode.Backspace
  unused by the game.

Deferred: customer patience timer (#4), bedroom "Corredor".
Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-24, round 119

Tavern service round 2:

- **Z/X remote serving** (`HandleServeKeys`): Z serves the nearest
  OrderInTable customer with `preference==Food`, X with `==Drink`, by calling
  the game's `Customer.ServeCustomer(1, true, tray)` (no distance check of its
  own; serves currentRequest from the player's tray). Fails clearly if the
  item isn't on the tray. KeyCode.Z/X confirmed unused by the game.
- **Served / satisfied announcements** (HandleTavernServiceAnnouncements):
  tracks `hasBeenServed` per customer -> "Pedido servido" on the transition
  (covers E-serve too; Z/X marks it to avoid a double), and on departure
  "Cliente saiu satisfeito/insatisfeito" by the last hasBeenServed.
- **Cat naming fixed** (#1): the log showed near the cat the interaction
  resolved to "663 - Grifo" (the beer tap) - the game focuses a nearby
  station. `GetNearestInteractionTarget` now prefers an available NPC
  (`FindClosestAvailableNpc`: closest in-range Customer/CatNPC) over the
  focused station for the name.

Deferred: customer patience TIMER (#4 - announce at start + 25% left; the
patience calc is heavily obfuscated, needs more digging) and the bedroom
"Corredor" zone.

Build clean. See `docs/modules/tavern-service.md`.

## Stopping point - 2026-06-24, round 118

Stations + menu-table name confirmed working. Focus: the tavern service loop
(user: "investigue tudo, não quero eventos/orders sem anunciar").

- **Customer/cat naming** (`DescribeNpc`, used by GetNearestInteractionTarget):
  a `Customer` reads "Cliente, quer {item}" (currentRequest item, else food/
  drink from `preference`); `CatNPC` -> "Gato". Was reading the raw GameObject
  name ("HumanMaiCustomer (2)").
- **Tavern service announcer** (`HandleTavernServiceAnnouncements`, polls
  `TavernManager.GGFJGHHHEJC.customers` every 0.5s): "Cliente chegou" on a new
  customer, "Cliente quer {item}, sirva com a bandeja" when one reaches
  CustomerState.OrderInTable, "Cliente foi embora" on departure.
- Serving is MANUAL: ordered item on the player's tray + E (Serve) next to the
  customer (Customer.ServeCustomer uses trayHandler.tray).
- **csproj**: added PhotonUnityNetworking/PhotonRealtime/Photon3Unity3D
  references - Customer/CatNPC inherit MonoBehaviourPunCallbacks.

See `docs/modules/tavern-service.md`. Deferred / next: new floor-stain &
rowdy-customer & last-orders alerts on appearance, customer-capacity stat,
employee auto-serving, the cat possibly still resolving to a nearby dispenser
(diagnostic in log), and the bedroom "Corredor" zone.

Build clean.

## Stopping point - 2026-06-24, round 117

Fixed confusions from round 116:

1. **Cat / menu table named "Dispensador de bebidas"** (#1/#2): root cause -
   `GetNearestInteractionTarget` named the geometrically CLOSEST IProximity
   (a nearby dispenser), not the object the prompt was actually for. Now uses
   `InputByProximityManager.GetPlayer(1).GetCurrentFocusedInputElement()
   .mainGameObject` first (the real focused interaction), falling back to
   GetCurrentInteractGO / closest.
2. **Ctrl+Enter wrong direction at the dispenser** (#4): the dispenser shows a
   FILTERED COPY of the inventory whose Slots aren't in
   playerInventory.inventory.slots, so `IsPlayerInventorySlot` returned false
   for a real inventory item -> "retirou da estação" + item lost. Now decides
   direction by whether the focused slot is in the STATION container's slots
   (`Array.IndexOf(station.slots, sourceSlot)`): in station -> inventory,
   else -> station. Works for any station/container.
3. **Station not default focus** (#2/#5, "só na 2ª vez"): reset
   `_manualWindowOverride` on ANY station open/close (not just open), so each
   session starts on the station (the top window; GameInventoryUI isn't in the
   open-window list).
4. Barrel (#6): same code paths, covered.

Diagnostics added: "interaction target ..." and "Ctrl+Enter sourceIsStation=".
Build clean. Deferred: bedroom announced "Corredor".

## Stopping point - 2026-06-24, round 116

Three station-related bugs:

1. **Ctrl+Enter transfer only worked for chests** (#3/#4): `GetOpenChestContainer`
   only checked BigContainerUI/SmallContainerUI. Renamed to
   `GetOpenStationContainer` - falls back to `MainUI.GetCurrentContainer`
   (set by DrinkDispenser/Fireplace/etc.) when a non-chest station UI is open
   (`IsNonChestStationOpen` checks GetCurrentOpenWindows for DrinkDispenserUI),
   guarded so a stale value isn't used. Announcement says "estação" not "baú"
   for non-chest stations (IsChestOpen); "Nenhuma estação aberta" when none.
2. **Menu table named "Dispensador de bebidas"** (#1): `BarMenuManager` is on a
   SEPARATE GameObject and references its `.placeable`, so GetComponent on the
   placeable missed it and it fell to the drink name. Now `IsDrinkStation`
   compares `BarMenuManager.instance.placeable == placeable` -> "Mesa de menu".
   Diagnostic added to GetNearestInteractionTarget.
3. **Sometimes opened focused on the inventory, not the station** (#2): the
   navigator now resets `_manualWindowOverride` when a NEW station window opens
   (`GetOpenStationWindow` / `_lastStationWindow`), so the default focus is the
   station.

Build clean. Deferred: bedroom announced "Corredor" (needs room geometry).

## Stopping point - 2026-06-24, round 115

Fixed the right-side inventory not working at the drinks dispenser / menu
table. Root cause from the log:
- The list-switch (`HandleContainerInventorySwitch`, right/left arrow swaps
  the station's slots <-> the player's GameInventoryUI) only recognised
  `ContainerUI`. `DrinkDispenserUI : UIWindow` is NOT a ContainerUI, so it
  was skipped ("switch skipped - no chest open, openWindows=[DrinkDispenserUI]")
  even though DrinkDispenserUI.OpenUI opens GameInventoryUI alongside it.
- Added `IsStationWindow(w) => w is ContainerUI || w is DrinkDispenserUI`,
  used in both the switch and `GetTopWindow`'s override-persistence check.
- The menu table (BigContainerUI : ContainerUI) already worked - the log
  showed it reading "Espetinho de rato, 20 (1 of 52)".
- On switch, now announces "Inventário" / "Estação", and "Inventário vazio,
  nada pra adicionar" when the inventory side has no items (CountInventoryItems).

Notes (game behavior, not bugs): the beer tap is a single-slot tap (1 slot
+ pull button), not a multi-slot chest; the service barrel that "won't open"
is tutorial-gated (`NewTutorialManager.ServiceBarrelBlocked`, DrinkDispenser
line 622).

Deferred: bedroom announced as "Corredor" (#1) - the game's ZoneType marks
that spot WithoutZone; matching the visual room needs room geometry.

Build clean.

## Stopping point - 2026-06-24, round 114

- **Menu table ("mesa de menu" / "BigContainer")**: `IsDrinkStation` now
  returns "Mesa de menu" for a Placeable with `BarMenuManager` (or GameObject
  name "BigContainer", opens BigContainerUI) -> named + "Máquinas".
- **Drink station naming reordered**: barrels (`ServiceBarrel`/`BanquetBarrel`)
  checked BEFORE dispenser, because a ServiceBarrel CONTAINS a DrinkDispenser
  (`ServiceBarrel.drinkDispenser`) - so a barrel GameObject has both; "Barril"
  is the more specific name. Standalone DrinkDispenser/DrinksTable ->
  "Dispensador de bebidas".
- **#3 (which accepts all vs only sparkling)**: investigated - the only clear
  distinguisher is `DrinkDispenser.isBeerTap`; the all-vs-sparkling filter and
  whether espumante=beer are NOT readable in the obfuscated code (likely
  per-instance data). Asked the user for the in-game names to map precisely.

Deferred:
- Bedroom announced as "Corredor" (#1): the game's own ZoneType marks that
  spot WithoutZone (its "RoomPlayerN" bedroom zone is only the tiles at the
  bed). Matching the visual room needs the room geometry - a separate task; a
  bed-radius heuristic was rejected (the boundary is ~1 tile, too tight).
- Dispenser/menu-table right-side inventory silent (#4/#5): needs the two-panel
  UI hierarchy from a log with the screen open - asked the user to send it
  (will also add "vazio/nada pra adicionar" when no valid items).

Build clean.

## Stopping point - 2026-06-24, round 113

Found ANOTHER major lag source + several fixes:

- **Item-proximity lag (the big one)**: `HandleItemProximitySounds` did its
  OWN `Object.FindObjectsOfType<Placeable>()` EVERY second (~87ms spike per
  second = constant stutter, and the real "anuncios de item proximo demora
  muito"). Added a shared `_cachedAllPlaceables` (refreshed every 15s) used
  by BOTH item-proximity and the candle filter; removed the separate candle
  scan. This is probably also why area announcements felt like "only on
  load" (lost/delayed in the constant stutter).
- **C key after un-marking (#3)**: turning the guide off (Home) now clears
  `_selectedTarget`, so the C coordinate readout stops announcing the old
  target.
- **Bed route (#12/#13)**: route to the bed's `sleepCollider.bounds.center`
  (where the "quer dormir?" prompt triggers, a walkable spot) instead of
  `GetPlayerBedPosition()` which could be a non-walkable tile - the user
  struggled to reach it.
- **Drink stations (#8-#11)**: `DrinkDispenser`/`DrinksTable` -> "Dispensador
  de bebidas"; `ServiceBarrel`/`BanquetBarrel` -> "Barril"; all under
  "Máquinas" (`IsDrinkStation` helper, checked before the Container branch
  since DrinkDispenser IS a Container).

Zones (#1): confirmed WORKING in the round-112 log (Corredor/Quarto/Cozinha/
Adega/Sala de jantar all fired) - the "only on load" was likely the
per-second stutter swallowing them.

Deferred (need more info / bigger): the "mesa de menu" rename (#5, identify
the object), the right-side inventory going silent at the dispenser/menu
table (#6/#7, needs the two-panel UI hierarchy from a log), and general
route precision (#14).

Build clean.

## Stopping point - 2026-06-24, round 112

Big focus: LAG (the log showed RefreshSeatSceneCache **348ms**, GetEmptySeatSlots
15-18ms every 1.5s, Door scan 83ms) - which was also making the sounds feel
delayed.

**Lag fixes (WorldNavigationHandler):**
- Rats: read the game's live `SceneReferences.tutorialRats` list instead of
  FindObjectsOfType<TutorialRat> - free, and rat death/count is now exact/
  instant. Fixes "caçar os ratos está com muito lag" + reliable death announce.
- Candles: the FindObjectsOfType<Placeable> scan (the dominant cost) moved OFF
  the seat refresh onto its own 60s cadence.
- Seats/tables refresh 20s -> 30s.
- `GetEmptySeatSlots`: cheap early-out when no table is within range (was
  burning ~15ms/1.5s at the oven). BuildTargetList now uses the cached
  seat/table arrays instead of two fresh FindObjectsOfType.

**Recipe reading + inspect (#5/#7):** the recipe LIST entry is a `RecipeSlot`
(not RecipeElementUI/SlotUIRecipe - confirmed by the round-111 diagnostic).
`DescribeRecipeListEntry` reads "Receita: {name}. Dá pra fazer / Faltam
ingredientes. Precisa: {ing amount, tem owned}..." from `RecipeSlot.recipe`
(`ingredientsNeeded`, `IABAKHPEOAF()`), so the player sees requirements before
Enter crafts it.

**Categories (#9/#10):** drinks table (`DrinksTable`) + food prep table
(`NinjaPreparationTable`) -> "Máquinas" (component check + name fallback in
CategorizePlaceable; plus a dedicated NinjaPreparationTable scan in
BuildTargetList since it isn't a Placeable).

**Area announcements (#8):** `HandleZoneTypeAnnouncement` announces room-level
`ZoneType` changes (WorldGrid.AGKGGAFFFGM): Cozinha (CraftingRoom), Sala de
jantar (DiningRoom), Adega (Cellar), Quarto (RentedRoom/RoomPlayerN), Corredor
(WithoutZone), workshops.

Sounds (#1/#2): logic already optimized (109/111) + RaycastNonAlloc; the
remaining delay was attributed to the general lag - asked user to re-test.

Build clean.

## Stopping point - 2026-06-24, round 111

Four items:

1. **Bump sounds still slow + lag**: lowered `WallStuckSeconds` 0.25->0.08
   (sound fires near-instant), and restructured `HandleWallBump` to
   classify (wall vs item, blocker name) ONCE on the transition into stuck
   instead of raycasting every frame while held (big lag win). Also
   switched the per-frame directional-wall raycasts from `RaycastAll` to
   `RaycastNonAlloc` (reusable `_raycastBuffer`) to kill the 4x/frame array
   allocation.
2. **Rat death announced**: `HandleRatAnnouncement` now tracks the non-null
   rat count (a killed rat's cached ref goes null) and says "Rato removido,
   faltam N" / "Todos os ratos removidos".
3. **Rat movement direction**: announces which way the NEAREST rat moved
   ("Rato foi à direita...") - throttled 0.6s, ~1 tile of movement.
4. **Recipe reading fix**: the recipe list entry's RecipeElementUI/SlotUIRecipe
   is on a CHILD of the navigable Button (it read its raw GameObject name) -
   added `GetComponentInChildren` fallback (self/children only, not parent,
   so ingredient slots don't grab the detail panel's RecipeElementUI). Added
   "CraftingNav:" diagnostic. The recipe-list-at-bottom / empty-slots layout
   is the game's own UI order (can't reorder) - explained to the user.

Build clean. Known lag still open: `GetEmptySeatSlots` ~15-20ms periodically
(noted round 109) - separate focused pass.

## Stopping point - 2026-06-24, round 110

User asked to make the oven usable: they couldn't find the available recipe
(pressed Enter on Starters/Meat categories but the recipe never surfaced),
and wanted ingredient slots to announce the required ingredient + how many
they own (name + 0 if none).

Built oven (GameCraftingUI) reading in `KeyboardUINavigator` (describers
added before the generic SlotUI block, since SlotUIRecipe : SlotUI):
- `DescribeRecipeElement(RecipeElementUI)`: a recipe entry reads "Receita:
  {dish name}" (recipeName via reflection; fallback = outputSlot item) -
  recipes live in the bottom scroll list, so now they're findable by name.
- `DescribeRecipeSlot(SlotUIRecipe)`: ingredient slots read "{ingredient},
  precisa {needed}, você tem {owned}" (owned via
  PlayerInventory.NumberOfItems(itemId)).
- Reflection fields RecipeNameField/RecipeInputSlotsField; DebugMode logs
  to confirm the component layout next test (assumed: list entry has
  RecipeElementUI directly, ingredient slots have SlotUIRecipe only).

Build clean. See `docs/modules/crafting-stations.md`.

## Stopping point - 2026-06-24, round 109

Tab + rats confirmed working. Two areas this round:

**B) Wall/bump sounds made near-instant (done).** User: the directional
wall sound and the bump sounds had audible delay to start AND to stop,
wanted them instant "like seeing", without lag. Root cause: CustomSounds
CREATED a GameObject+AudioSource and Play()'d it on each start, DESTROYED
it on each stop - Play() on a fresh source has start-up latency and the
churn adds delay. Fix: persistent sources per direction/loop, created once
and kept playing at volume 0; activating just toggles the volume (instant,
no churn). Also shortened `WallSoundOffDelay` 0.15->0.06 (stop grace) and
`WallStuckSeconds` 0.6->0.25 (bump-sound start). Idle cost is a few silent
looping sources - trivial.

**A) Crafting investigation (forno + mesa de preparação) - mapped, not yet
built.** User couldn't use the food-prep table (F/E did nothing); the oven
opened `GameCraftingUI` with recipe categories read but the slots looked
empty. Findings (now in `docs/modules/crafting-stations.md`):
- Oven opens `GameCraftingUI`. Categories are `RecipePages` (top). The
  actual RECIPES are in the Bottom Panel scroll list ("New SlotUI Recipe
  Selectable Element(Clone)"); the top slots are the selected recipe's
  INGREDIENTS (so they read "empty"). There's a `FuelButton` for fuel. Only
  1 recipe available (tutorial).
- The prep table is `NinjaPreparationTable` (IInteractable/IProximity) - not
  broken; the tutorial dialogue explicitly defers it ("vamos tentar algo
  mais simples primeiro" = do the oven first), and it uses a Bento system.
- Full crafting-UI accessibility (announce recipe names/ingredients/fuel,
  jump to the recipe list) is a sizeable dedicated feature - proposed to the
  user as the next focus.
- Noted a real lag source near the oven: `GetEmptySeatSlots` ~15-20ms every
  ~1.3s.

Build clean. See `docs/modules/crafting-stations.md` and
`docs/modules/world-object-navigation.md`.

## Stopping point - 2026-06-24, round 108

Six requests:

1. **Item name on placement**: `HandlePlacementResult` now reads
   `placeable.itemSetup.item.IABAKHPEOAF()` - "{name} encaixado na mesa" /
   "{name} colocado".
2. **Dropped the "mas não num ponto de mesa" wording** - misleading now
   that Enter force-snaps (round 106).
3. **Tab repeats the current objective**: `HandleObjectiveKey` reads
   `NewTutorialManager.instance.objectives[i].textMesh` live (only active
   ones) so progress counts ("2 ratos") are current. KeyCode.Tab unused in
   the game; runs only when no UI is open.
4. **Rats move**: confirmed - TutorialRat has wander coroutines. That's why
   routes to them are stale (the target position is captured at selection,
   not live-tracked). Informed the user; live-tracking deferred.
5. **Rat proximity announcement**: new `HandleRatAnnouncement` (cached
   `_cachedRats = FindObjectsOfType<TutorialRat>()`, positions read live) -
   "Rato perto. Use o esfregão pra removê-lo" when the nearest rat changes.
6. **Rat interaction**: the mop (per the mission dialogue "levaria o
   esfregão...") - folded into the proximity line. No discrete per-rat key
   exists (TutorialRat isn't IInteractable); exact mop gesture/key not dug
   out yet (flagged to user).

Build clean. See `docs/modules/world-object-navigation.md`.

## Stopping point - 2026-06-24, round 107

User confirmed candle works and they passed the door (C-key alignment).
Four requests, all done in WorldNavigationHandler:

1. **Faster blocker speech**: the spoken "Bloqueado por ..." now fires at
   `BlockerAnnounceSeconds` (0.2s) instead of waiting for the bump-sound's
   `WallStuckSeconds` (0.6s). HandleWallBump split into an early
   announce-threshold and the longer sound-threshold.

2. **Cellar exit in "Portas"**: the cellar→tavern exit is a `TravelZone`
   ("TravelZone-CellarToTavern"), NOT a Door (confirmed in log:
   "WallCheck baixo -> TravelZone-CellarToTavern dist=0.16"). BuildTargetList
   now lists nearby `TravelZone`s under "Portas" via `DescribeTravelZone`
   (named by `locationTo` -> "Passagem para a taverna", with a
   `LocationName` PT map).

3. **Rats in "Pendentes"**: the cellar rats ("Remova os ratos da adega"
   goal) are `TutorialRat` components - listed as "Rato N" (stable x/y
   order), category "Pendentes".

4. **Bed only when near**: the bed was added unconditionally and showed in
   the cellar (which shares the tavern Location, so the Location filter
   can't separate them). Now gated by NearbyDoorRadius (30u) - the cellar
   is ~105u from the bed, so it drops there.

Build clean. See `docs/modules/world-object-navigation.md`.

## Stopping point - 2026-06-24, round 106

User confirmed the blocker announcement works. Three requests:

1. **C key for coordinates** (`HandleCoordinateKey` in WorldNavigationHandler):
   press C to hear "Você está em X, Y"; if a nav target is selected
   (`_selectedTarget`), also "Alvo NOME em X, Y". Rounded to whole tiles.
   Guarded against Ctrl/Shift. KeyCode.C confirmed unused in decompiled.

2. **Candle force-snap like the painting**: the candle confirm now finds
   the nearest valid table SnapToPosition within the whole-room radius
   (SeatSlotGuidanceSearchRadius, was SnapToSlotRadius ~2.5u) and FORCE-
   snaps onto it on Enter even from far ("ao soltar mesmo q não esteja
   perto arrastar para la"). Settle-retry confirms. Addresses the
   remaining candle inconsistency.

3. **Door question answered (from the log, no code change)**: NOT a
   mission gate - it's alignment + the physical brick pile. Player was at
   x=12.39 while the door passage is at x=12 (slightly left) with "Grupo
   Ladrillos" to the right; left/up/down were clear. The new C key helps
   the player compare their x to the door's and align. (Door routing
   still falls back to door.transform.position because freeNodesOnOpen is
   empty - a known imprecision, not fixed this round.)

Milestone holding: painting + candle placement both work. Build clean.
See `docs/modules/world-object-navigation.md` and
`docs/modules/decoration-mode.md`.

## Stopping point - 2026-06-24, round 105

User: **painting and candle both WORK now** (round 104's wall fix + round
102's candle snap fix confirmed live - a big milestone after ~15 rounds on
decoration placement). New problem: couldn't pass through the (open)
tavern door, "something is holding me", asked if furniture was blocking.

Diagnosed from the log: player at (12.39, 909.31), door "Door" at
(12.0, 909.48) dist 0.4, open. "WallCheck direita -> Collider dist=0.22"
and "Sustained bump classified as item" - a real collider 0.22 to the
player's right. Identified it: **"Grupo Ladrillos"** (a tutorial brick
pile / construction object, GameObject "99999 - Tutorial Object") at
(12.50, 908.50), right at the door. Left/up/down were all clear - the
player just needed to align left to the door (x=12) and pass, but had no
way to know what was wedging them.

Fix (a real usability gap, not just this door): the sustained-bump handler
now SPEAKS the blocker's name + direction once ("Bloqueado por Grupo
Ladrillos à direita") instead of only playing the item-bump sound.
`IsBlockedByNonWallItem` now returns the blocker name for ANY real
collider hit (furniture "(Clone)" OR static scenery like the bricks - walls
have no Collider2D here, so anything hit is nameable); `DescribeBlockerCollider`
prefers the localized item name (Placeable) and otherwise cleans the
GameObject name. The wall-vs-item SOUND classification is unchanged.

Build clean. See `docs/modules/world-object-navigation.md`.

## Stopping point - 2026-06-23, round 104

User: painting STILL doesn't work; authorized subagents, asked to go deep
and IMPLEMENT a working solution that FORCE-places the painting at the
nearest valid wall spot to where they are.

Ran a general-purpose subagent to read the wall-validity mechanics in
decompiled Placeable.cs, then verified the key methods myself
(FNPBNFFEBAF:1688, HHAEKEAPKOE:1594, WorldGrid.KHJJCAGIJAP:1330). THE
ROOT ERROR of all prior wall rounds, found: wall validity operates on the
4 CORNERS of `itemBase.bounds`, NOT on `transform.position`. Round 103's
scan tested the transform against the wall-tile grid - the wrong thing -
so it guided to spots the game never accepts. FNPBNFFEBAF requires: all 4
bounds corners are wall tiles (WorldGrid.ALNFLFCLIEP) AND each has a floor
below at one consistent height (WorldGrid.KHJJCAGIJAP). Both are pure
tile-data lookups - STABLE, no physics flicker (the flicker is only in the
physicalSpace sub-check, which can't be scanned synchronously).

Fix: new `WorldNavigationHandler.FindNearestValidWallPosition` replicates
FNPBNFFEBAF EXACTLY on candidate bounds-corner positions (transform->bounds
offset measured at runtime from the live itemBase.bounds), plus a
flicker-free occupancy check vs existing wall Placeables. The wall confirm
now FORCE-snaps the painting onto the nearest such position on Enter (user's
explicit ask), and the settle-retry confirms it with the game's own check
across real frames. Guidance points at the same position. Diagnostics
"wall lock"/"wall confirm"/deselect gate kept to pin any remaining gap
(e.g. FCGPPPPDFMB column-sweep or zone check, not replicated - relying on
the confirm-time real check for those).

Candle placement (round 102) still untested - user blocked on painting.
Build clean. See `docs/modules/decoration-mode.md`, "40ª rodada".

## Stopping point - 2026-06-23, round 103

User tested round 102: candle proximity worked ("falou sim acesa"); but
the painting "não funcionou de jeito nenhum" across several walls, and
they were blocked on it (couldn't reach the candles).

Root cause in the log: round 102's removal of wall guidance made it
WORSE. The painting sat at y~906 (inside the room) while valid walls are
at y~910 (where it placed successfully back in rounds 99/100), and with
no directional cue the blind player swept arrows uselessly - every "wall
free check" logged free=False, and the one confirm showed
physicalSpaceOk=True but validTrue=False (i.e. the failing sub-check was
the WALL geometry, not physics - the painting simply was never on a
wall).

Fix (round 103): restored directional wall guidance, but now targeting a
stable FREE wall tile. New `WorldNavigationHandler.FindNearestFreeWallTile`
scans with the tile-geometry check `WorldGrid.ALNFLFCLIEP` (the tile's own
.wall flag - flicker-free, unlike IsObjectInValidLocation's physicalSpace
sub-check that broke FindNearestValidPosition for walls) plus a distance
check against existing wall Placeables for occupancy. `HandleWallGuidance`
now says "Parede livre: X pra cima/..." leading to it, "Parede livre aqui,
pode soltar" when the spot is actually valid, or "Nenhuma parede livre por
perto" if none. The wall confirm snaps onto the locked free wall tile when
the current spot isn't valid (forgiving, + settle-retry). Diagnostics
"wall lock"/"wall free check ... lockedWall=" added to confirm next round
whether the wall-tile target needs a small offset to reach a valid spot.

Candle placement (round 102's snap fix) still untested - user was blocked
on the painting. Build clean. See `docs/modules/decoration-mode.md`,
"39ª rodada".

## Stopping point - 2026-06-23, round 102

User tested round 101 and gave a broad batch (chose "tudo de uma vez"):

1. **Wall (painting) simplified** per explicit request ("andar até uma
   parede, só informe se aquela parede está livre" - the old guidance
   sent them to occupied walls). Dropped the FindNearestValidPosition
   coordinate guidance; `HandleWallGuidance` now just announces whether
   the painting's CURRENT spot is a free wall ("Parede livre aqui" /
   "Aqui não dá, mova pra uma parede livre"), and the wall confirm places
   it right where it is (settle-retry absorbs the physics flicker).

2. **Candle snap refinement**: round 101 snapped only sometimes ("a
   última das cinco deu certo, as outras no mesmo lugar"). Root cause: the
   live attach stuck the candle loose on a generic surface, so the snap
   spot was never marked used and the next candle targeted the same place.
   `HandleCursorMovement` now, when a free table snap is locked by guidance
   and the item is close, pulls the candle exactly onto it and attaches
   there (snaps + marks used), and refuses to place loose on a generic
   surface when a real snap target exists. Reuses the guidance lock (no
   per-frame scene scan).

3. **Categories** (WorldNavigationHandler): "Missão" renamed to
   "Pendentes"; new "Repositivos" category; placed candle (id 605) →
   Repositivos while lit, → Pendentes when spent; associated benches
   (Seat.table != null) dropped from the list.

4. **Candle proximity**: candle is a Crafter (Placeable.SetFuel /
   Crafter.LCCABPFHCOL). On approach announces "Vela acesa" or "Vela
   apagada, precisa repor" (game's own spent threshold fuel <= 1). The
   EXACT % is deferred - the candle's max fuel isn't reliably readable
   from the obfuscated source, so `HandleCandleAnnouncement` logs the real
   fuel value ("candle proximity ... fuel=N") to build the % next round
   instead of guessing.

Candle item id confirmed = 605 (ItemDatabaseAccessor.GetItem(605),
"605 - Vela(Clone)"). Build clean. See `docs/modules/decoration-mode.md`
"38ª rodada" and `docs/modules/world-object-navigation.md`.

## Stopping point - 2026-06-23, round 101

User tested round 100. Three points: painting placed only on the 2nd-3rd
try (still needed walking); 10 candles placed but "all in the same spot"
and the mission did NOT complete; and asked the hotbar (uso rápido) to
announce quantity and update as items are used (9, 8...).

Root cause of the candle, found in the round-100 log: the "surface
attach" diagnostic showed the candle attaching to a generic surface named
"Surface" with **snapped=False every time**. Surface items that count for
the "Coloque seus novos itens na taverna" objective (candle, tablecloth,
centerpiece) only register when they snap onto a designated
`SnapToPosition` on a TABLE - round 100's "preserve the snap" fix didn't
help because the snap never happened (the game picks the snap via the
cursor, decorrelated from our arrow movement since round 82).

The mission is "Coloque seus novos itens na taverna" (place your new
items), NOT "10 candles" - the 10 is just the stack size. Two items can't
share one snap spot (`!canBeRepeated`), so they go on different
spots/tables.

Fix: new `WorldNavigationHandler.FindNearestSnapPosition` scans the
public `snapToPositionArray` of every `SurfaceSortOrder` directly (no
cursor), finds the nearest FREE matching snap, returns its exact world
position. `HandleConfirmPlacement` now snaps surface items onto that
point ("surfaceSnap"), so `AddPlaceableToSurface` sets snappedToPosition
= true; `HandleSurfaceGuidance` leads to it ("Lugar na mesa: ...");
`HandlePlacementResult` distinguishes "Item encaixado na mesa" vs "Item
solto na superfície, mas não num ponto de mesa". New `LogSnapTargets`
diagnostic lists every table with a free snap for the item at grab time.
Fallback to the old generic-surface behavior if the item uses no snaps.

Hotbar quantity (in `InventoryTransferHandler.cs`):
`OnHotbarSelectionChanged` announces "name, N" when N>1, and a new
per-frame `PollSelectedHotbarStack` announces the running count as the
selected item is consumed ("9", "8"... "acabou").

Painting (wall): round 100's settle-retry helped (it places now, vs not
at all) but still took 2-3 tries; the "settle gave up" + deselect-gate
diagnostics will show why next round. No new wall change this round.

Build clean. See `docs/modules/decoration-mode.md`, "37ª rodada".

## Stopping point - 2026-06-23, round 100

User tested round 99 and gave 4 points: plant OK; forro placed but
unsure it landed ON a table; quadro (painting) only placed "by accident"
when physically near the wall (with arrows it announced the right spot
but Enter wouldn't place); new item VELA (candle) asked for a table,
used 10 but the mission did NOT complete; and asked the inventory to
announce QUANTITY when a slot holds more than one of an item.

Read the new log + decompiled source and found TWO root causes:

1. **"announces valid via arrows but Enter fails" (painting + the
   surface flicker)**: `PhysicalSpaceWall.ValidPosition()` iterates a
   `colliders` list populated by `OnTriggerEnter2D/Exit2D` - which only
   fire on the physics step (FixedUpdate), NOT from a same-frame
   `transform.position =` + `Physics2D.SyncTransforms()`. So right after
   teleporting the item via arrows, that list still holds stale overlaps
   → reads invalid at a genuinely valid spot. Confirmed in the log: SAME
   position (10.96, 910.23) was invalid via arrows (player far) and
   valid walking there; `physicalSpaceOk` flickered True/False at a
   fixed surface position. Round 99's "all in one frame" approach is
   actually wrong for the wall/surface path for this exact reason. Fix:
   `SnapAndConfirm` no longer gives up on a failed first Deselect - it
   arms a settle-retry (`_pendingSettleDeselect`, up to 30 frames) that
   keeps the item pinned at the validated spot and retries Deselect each
   frame until the trigger list clears.

2. **candle/tablecloth don't count for the mission (snap being
   undone)**: `SurfaceSortOrder.UpdatePosition` moves the item exactly
   onto a designated `SnapToPosition` on the TABLE (tablecloth/candle
   snap spots, built from `tablecloths.tableCovers`). Our code then
   re-asserted the raw cursor pos right after, yanking the item off the
   snap → mission never counts it. Fix: only re-assert raw pos when
   `placeable.snappedToPosition == false`; when it snapped to a
   designated spot, leave it there. New diagnostic logs the surface name
   + snapped flag at each attach.

Also (point 4): `KeyboardUINavigator.DescribeSlotUI` now reads
`Slot.Stack` and announces "name, N" when a slot holds more than one
(e.g. "Vela, 10"); single items unchanged.

Build clean. Concrete evidence-backed fixes + diagnostics - requesting a
test. See `docs/modules/decoration-mode.md`, "36ª rodada".

## Stopping point - 2026-06-23, round 99

User tested round 98 (new log). Plant now places AND moves well
(unified arrow model confirmed good). But painting (wall) and forro
(tablecloth/surface) still won't place "even though it says the
position is correct." User authorized subagents and asked to
understand these placement mechanics deeply, to move faster on related
future tasks.

Read `Placeable.Deselect` in full and found why "valid" != "placeable":
- The log showed plant `Deselect -> True`, but wall/surface
  `Deselect -> False` at the SAME position they reported valid.
- `Deselect` (line 1847) gates on `IsObjectInValidLocation(TRUE)` (we
  checked `false`) and `DeselectAction` whose `canBePlaced` gate is a
  dead field (always true - confirmed). Neither was the real cause.
- REAL cause: round 96's one-frame-deferred Deselect. On that extra
  frame the game's own `Placeable.WhileSelected` runs and disturbs the
  object - detaches the surface / shifts the wall item via the cursor
  pipeline we bypass (round 82) - so by the deferred Deselect it's no
  longer valid. The plant survived because its `itemSpace` path reads
  `transform.position` directly, not `currentSurface`/collider bounds.

Fix: `DecorationModeHandler.SnapAndConfirm` now does snap +
`Physics2D.SyncTransforms()` + `Deselect` all in ONE frame. The
original reason for deferring (Collider2D.bounds lagging a frame) is
handled by SyncTransforms instead, so one frame is correct AND gives
the game no window to interfere. Added `WorldNavigationHandler.
LogDeselectGate` logging every input to Deselect's real gate right
before the call, so if it still fails the log names the exact failing
sub-check.

Deep-dive documented for the user's "understand the mechanics" ask:
new "Mecânica de confirmação de posicionamento (Deselect) - REFERÊNCIA"
section at the top of `docs/modules/decoration-mode.md` (full Deselect
path, the dead canBePlaced field, the itemSpace-vs-itemBase/bounds
distinction, the SyncTransforms requirement, why not to defer).

Build clean. Concrete fix + diagnostic safety net - requesting a test.
See `docs/modules/decoration-mode.md`, "35ª rodada".

## Stopping point - 2026-06-23, round 98

User tested round 97 (new log) and gave a pivotal diagnostic clue.
Results: plant placed successfully again. Painting still broken ("said
it would place, then the coordinates jumped/grew again", says "1 to
the right" but can't go right). And THE key insight: "when I open the
item with F it says like 5 to the right, but I'm at a much greater
distance than that - it's as if the cursor you're looking at and my
position aren't being compared." Plus general confusion at having two
models (bench: B+arrows; items: F+walk) and a request for accuracy.

The log confirmed both root causes exactly:
1. **Coordinate mismatch**: items grabbed via the native "F" hotbar key
   set selectedGameObject directly, never going through our HandleGrab,
   so `_heldIntendedPosition` was left null. HandleCursorMovement then
   returned early every frame, leaving the item FROZEN at its spawn
   point (e.g. (3.58, 909.60), near the wall) while the player stood
   elsewhere (e.g. (6.43, 906.68)). Guidance compared the frozen item
   to the target = meaningless relative to the player. Exactly the
   user's "cursor vs my position" complaint.
2. **Wall placement fails**: round 93's hand-rolled IsValidWallPosition
   (4 corners of itemBase.bounds vs tile flags) returned a point the
   game's real IsObjectInValidLocation rejects (confirmed: snap target
   (6.08, 910.10), deferred deselect returned False a full frame later
   - not a timing issue, a genuinely-wrong target).

Two-part fix, both addressing the user's core asks (accuracy +
consistency):
- **Unified validity search**: new `FindNearestValidPosition` uses the
  game's OWN `IsObjectInValidLocation` (move object + Physics2D.
  SyncTransforms + check, nearest-first early-out), replacing the wall
  replication AND the round-96 itemSpace search. Guaranteed to agree
  with what Enter accepts. Logs a clear "NO valid spot - may need a
  different facing/rotation" if nothing's found, so if the painting
  needs rotation we'll know next round.
- **Unified movement model**: reverted the round-94/95/96 "non-seat
  items track the player walking" experiment. ALL items now use the
  bench's pure arrow-driven virtual cursor (decoupled from walking),
  initialized at grab time for EVERY entry path (the F-grab init was
  the missing piece). This removes the coordinate mismatch entirely
  (no player-vs-item comparison - arrows drive the item, guidance is
  exact) and gives one consistent model the user already understands.
  Removed `_heldOffsetFromPlayer`; WASD reminder again applies to all
  items; wall+itemSpace snap blocks merged into one.

NOTE: this reverses the user's round-95 ask for walking - but that ask
was based on arrows appearing broken (which was really the validity
bug). Flagged to the user explicitly; reversible if they disagree.

Build clean. Concrete evidence-backed fixes - requesting a test. See
`docs/modules/decoration-mode.md`, "34ª rodada".

## Stopping point - 2026-06-23, round 97

User clarified the testing-cadence feedback from round 96: only ask
for a new test when there's a concrete fix ready to verify, or a real
need for another round of logs - not the stricter "wait until
everything in the batch works" read from round 95 (saved/updated in
memory). Test results for round 96's fixes: plant placed successfully
this time (though user wants the same precision/reliability - "
acertividade" - the bench has). Painting: "completamente louco" - said
it was about to place, then the coordinates jumped away again.

Read the new log and found the real bug in round 96's own wall-snap
fix: `HandleConfirmPlacement` set the snapped position and called
`Deselect()` in the SAME frame - confirmed via the log that the
authoritative check (which reads `itemBase.bounds`, a Collider2D)
sees STALE bounds that Unity hasn't updated yet from a same-frame
transform write (the literal same position, checked again one frame
later, passed - "Posição válida"/"Parede bem aqui" both appeared
right after the failed confirm). Compounding it: since Deselect()
failed, the item stayed held, and the very next frame's
`HandleCursorMovement` (the round-95 walk-tracking model) recomputed
position from `playerPos + the OLD offset`, wiping out the snap
entirely - explains the user's "disse que ia colocar, mas aumentou as
coordenadas de novo" exactly (logged jump from (5.75, 910.21) to
(15.61, 907.00), the player's own position).

Fixed both: the wall/surface snap-on-confirm now defers `Deselect()`
by one frame (mirroring the bench's existing `_pendingSnapDeselect`
pattern, generalized to `_pendingGenericSnapDeselect`) so physics
bounds have a frame to catch up, and updates `_heldOffsetFromPlayer`
to match the snapped position so a not-yet-confirmed hold doesn't get
reverted on the next frame either way.

Also extended the same precision the bench/wall/surface already have
to the plant's category (generic floor-space items, no Seat/surface/
wall): added real spoken directional guidance ("Lugar livre: X pra
direita...") and snap-on-confirm, using a new
`FindNearestValidItemSpacePosition` search (no pure-function form
exists for this check, unlike walls - it temporarily moves the real
object per candidate and restores it, all within one method call, so
no visible flicker).

Build clean. These are concrete, well-evidenced fixes (not blind
guesses) - requesting a test now per the user's clarified preference.
See `docs/modules/decoration-mode.md`, "33ª rodada".

## Stopping point - 2026-06-23, round 96

User tested round 95's walk-tracking change (new log). Confirmed the
walk-tracking itself works well ("agora atualiza conforme eu ando,
muito bom"). Two remaining problems, with an explicit "resolva isso em
definitivo" (fix this for good) for both:
- Painting: still couldn't place it. Reported being already pressed
  against the wall while guidance still said "go right."
- Plant: still no guidance/updates at all.

Read the new log's `wall guidance calc cur=...` lines (added last
round specifically for this) and got a definitive answer: walking
pins the player against the wall's own collision ~0.27 units short of
the actual valid wall point (measured directly: cur=(5.75,910.21) vs
target=(6.02,910.10) at the moment Enter failed) - a real structural
gap, since the valid mount point is essentially at/behind the wall
surface while the player's collider stops at its near face. The held
item COULD close that gap via one arrow press (its position isn't
blocked by collision), but the user reasonably assumed "already at
the wall" meant done and never tried arrows at that exact moment.
Fixed with the same pattern already proven for the bench/centerpiece:
`HandleConfirmPlacement` now auto-snaps onto the nearest valid wall
point on Enter (within the same generous radius) instead of requiring
exact manual alignment - this should be the definitive fix, following
the identical successful pattern, but not yet verified live.

Plant: the extended diagnostic (zone/ground/wall) ruled those out -
confirmed `groundHere=Floor`, `zoneHere=DiningRoom`, off any wall tile,
8-10 units from the player, and `squareValid` was STILL false. Traced
`BuildSquare.IsValid`'s remaining gate to `WorldGrid.NGDHDMAMGPI`
(checks `WorldTile.canPlaceObjects` and whether `blockingObjects` is
already registered there - real tile-occupancy data, not a live
Physics2D check). Extended the diagnostic to read the WorldTile
directly and log the actual blocking object(s) by name. Genuinely
can't say yet whether this is a code bug or environmental clutter -
this exact tavern corner has been the test site for 90+ rounds across
this whole feature (benches, tables, dropped items, paintings all
tested there repeatedly), so leftover registered clutter is a real
possibility worth checking before assuming a logic bug.

Build clean. Both fixes/diagnostics need one more test to close out -
not requesting it unprompted (user's bundling preference) but it's
the only way to confirm the wall snap holds and learn the plant's
exact blocker. See `docs/modules/decoration-mode.md`, "32ª rodada".

## Stopping point - 2026-06-23, round 95

User tested round 94's fixes (new log, this time from today). Results:
centerpiece - confirmed working (auto-snap-on-confirm fix held up).
Painting - walked as close as possible ("1 pra direita" remaining),
but couldn't close that last gap and never placed it. Plant - received
no guidance at all. Explicit feedback: (1) only ask for a new test
once everything in this batch is testable and working similarly - not
the case yet (saved as a feedback memory, see CLAUDE.md/memory);
(2) for the bench they navigate the virtual cursor with arrows, but for
the others they have to walk - if that's really how it works, the
guidance should update as they walk.

Read the raw key log for the painting hold: confirmed the wall
guidance offset ("Parede: 1 pra direita") never changed once across a
27-second hold, despite at least one confirmed RightArrow press - but
no diagnostic existed to show the held item's actual position during
that check (unlike the seat-slot guidance, which already logs cur/
target). Added that logging (`DecorationMode: wall guidance calc
cur=... target=...`) - genuinely unresolved until the next test.

Extended the plant's itemSpace diagnostic (round 94) with the specific
sub-checks `BuildSquare.IsValid` runs (zone type, ground type, wall
tile, distance to player) - round 94's diagnostic only confirmed
`squareValid=False` without saying which check inside it fails.

Architecture change, explicitly per the user's description of how they
actually play: non-seat held items (painting/plant/centerpiece) now
track the player's live position plus an arrow-adjustable offset
(zero at grab), instead of being fully decoupled from movement like
the bench (round 82's design, kept unchanged for seats). Walking was
previously a no-op for held non-seat items - only a fresh re-grab via
"F" (which spawns at the player's current position) ever made walking
seem to help. Also rescoped the "use arrows, not WASD" reminder to
benches only, since WASD is now the intended primary movement for
everything else.

Build clean. This round's architecture change has NOT been verified
live yet - not requesting an isolated retest per the user's bundling
request; holding it together with the still-open painting/plant
diagnostics for one combined test whenever the user is ready. See
`docs/modules/decoration-mode.md`, "31ª rodada".

## Stopping point - 2026-06-22, round 94

User tested round 93's wall guidance and reported back: painting still
doesn't work; centerpiece works but is "too inconsistent" to feel like
the bench; plant never found a valid spot anywhere, including with
arrow keys (confirmed via raw key log this time, ruling out the
WASD-mixup theory from round 93).

Found the project in a BROKEN BUILD state when picking this up - a
prior session had already read `Placeable.FNPBNFFEBAF` in full and
written `WorldNavigationHandler.IsValidWallPosition` (the real 4-corner
+ consistent-height wall check, replacing round 93's single-point
approximation) plus an updated `FindNearestWallPoint` signature, but
never updated `DecorationModeHandler.cs`'s call sites to match - a
missing-argument compile error. Finished that wire-up:
`HandleWallGuidance` now calls `IsValidWallPosition` both for "Parede
bem aqui" and to drive the search, so that announcement only fires
when Enter will actually succeed - matches the log's evidence exactly
(it said "bem aqui" twice, Enter said "Não posso soltar aqui" both
times).

Centerpiece/surface inconsistency: added auto-snap-on-confirm in
`HandleConfirmPlacement`, mirroring the bench's snap-to-slot pattern -
Enter near a valid surface (within the same 2.5-tile radius) now
attaches directly via `Placeable.AddPlaceableToSurface` instead of
requiring pixel-perfect manual alignment first.

Plant: same generic floor-space check benches use
(`ItemSpace.IsItemSpaceValid`), which works for benches but never
validated for the plant at any tested position. Have an unconfirmed
theory (spawn position from "F" may not be grid-aligned, and 0.5-tile
steps would never correct that) but didn't apply it blind - added
`WorldNavigationHandler.LogItemSpaceValidityDiagnostic` (replicates the
engine's own per-buildSquare checks, both public APIs) so the next
test's log shows the real rejection reason.

Build clean. See `docs/modules/decoration-mode.md`, "30ª rodada".

## Stopping point - 2026-06-22, round 93

User tested round 92: style picker worked for the plant. Dropping the
plant and the painting failed, no guidance on where to put them. The
centerpiece apparently gave guidance but needed walking with the
player (not arrows), unsure if it landed correctly. Asked to dig
deeper into painting + plant placement.

With the diagnostic now actually capturing data (round 92's relocation
fix), the log gave real per-item categories:
- **Painting ("Cuadro Raido")**: `isPlaceableOnWall=True`. Across 8
  grab attempts it NEVER reached a valid position once - confirms it
  genuinely needs wall guidance, which didn't exist yet.
- **Centerpiece**: `isPlaceableOnSurface=True` - DID work; full
  successful sequence found in the log ("Posição inválida" ->
  "Superfície: 5 pra direita, 1 pra baixo" -> "Posição válida" ->
  "Superfície bem aqui, pode soltar" -> "Item solto na superfície").
  Re-grabbing after walking (each fresh grab spawns at the player's
  current position) is what got it close enough.
- **Plant**: `hasItemSpace=True`, no surface/wall - uses the same
  generic "area is clear" check seats/benches use. The log showed the
  user only ever pressed WASD while holding it (moved the PLAYER, not
  the held item) before switching to test the style picker - never
  actually tried arrow keys on it. Likely doesn't need special
  guidance, just needs the user to actually use arrows on it.
- **Bug found**: the WASD-misuse reminder ("Pra mover o banco, use as
  setas...") fires for ANY held item but hardcoded the word "banco" -
  confirmed in the log firing while the plant was held. Fixed to say
  "o item".

Implemented wall guidance: read `Placeable.FNPBNFFEBAF`/`WorldGrid.
ALNFLFCLIEP`/`KHJJCAGIJAP` (all public/static, verified via grep) -
the real check requires all 4 corners of the item's collider bounds
to sit on wall tiles at a consistent height. Walls have no GameObject
to search (pure Tilemap-cell data, confirmed via earlier research), so
added `WorldNavigationHandler.FindNearestWallPoint` - scans a grid of
candidate points with `WorldGrid.ALNFLFCLIEP` instead of
`FindObjectsOfType`. New `HandleWallGuidance` (mirrors
`HandleSurfaceGuidance`): "Parede: X pra direita/..." / "Parede bem
aqui, pode soltar". This is an approximation (tests one point, not the
real 4-corner check) - `IsObjectInValidLocation`'s "Posição válida"
remains the authoritative signal for whether Enter actually works.

Build clean. See `docs/modules/decoration-mode.md`, "29ª rodada".

## Stopping point - 2026-06-22, round 92

User tested round 91 (surface guidance): benches still work ("conseegui
os bancos, continuam funcionando"). New feedback on the 3 new items:

1. **Critical entry-point gap found**: the painting/plant/centerpiece
   are placed for the first time via a native hotbar key (user calls it
   "F") that auto-enters Decoration Mode and grabs a fresh instance -
   log confirms ("Modo de decoração ativado" -> "Item pego" ->
   "selectedGameObject changed to ...") this goes through the EXACT
   same `SelectObject.selectedGameObject` path our code already
   reacts to - so guidance logic itself wasn't broken, but our
   diagnostic (`grabbed item category`) never fired because it only
   lived inside `HandleGrab` (our own Enter-grab), which this path
   never calls. Moved that diagnostic into Update()'s
   `selectedGameObject changed` block instead, which fires regardless
   of entry point. Also confirmed in the log that surface guidance DID
   already fire correctly for the centerpiece ("Superfície: 9 pra
   direita, 1 pra cima") - the gap was purely diagnostic-visibility,
   not the guidance itself.
2. **Arrow-key movement too slow**: `GetKeyDown` only fires once per
   physical press, requiring a fresh release+press per tile. Not a
   debug/logging-overhead issue (user explicitly asked not to touch it
   if so) - genuinely an input-handling gap. Added hold-to-repeat in
   `HandleCursorMovement` (initial 0.25s delay then 0.08s repeat) -
   single tap still moves exactly one tile.
3. **Plant's "Estilo" (T key) needs to be a real list**: researched
   `Placeable.NextSkin`/`ChangeSkin(int)`/`GetSkinIndex()`/`skins`/
   `skinsGameObjects`/`multipleSkins` (all public, verified via grep) -
   native behavior is silent index cycling, no UI. Built
   `HandleStyleTrigger`/`HandleStylePicker`: T opens an accessible list
   ("Estilo N de M"), arrows navigate, Enter commits
   (`placeable.ChangeSkin(index)`), Esc reverts. Deliberately scoped to
   the simple skins/skinsGameObjects-array case only - `Placeable` also
   has a `skinVariationGropus` path (toggles several skins in
   combination, not a single pick-one-of-N) that's out of scope until
   confirmed any of our items actually use it.
4. **Wall guidance still deferred**: confirmed via research that wall
   data is pure Tilemap-cell data (`WorldGrid.ALNFLFCLIEP`), not
   GameObjects - no `FindObjectsOfType`-style registry exists, so a
   "nearest wall" search would need to scan tilemap cells in a grid,
   not iterate a scene object list like `FindNearestValidSurface` does.
   Still waiting on the diagnostic (now actually capturing data for
   these items) to confirm whether the painting truly needs
   `isPlaceableOnWall` before building that.

Build clean. See `docs/modules/decoration-mode.md`, "28ª rodada".

## Stopping point - 2026-06-22, round 91

Still `feature/inventoryAndGetItens`, not yet committed. User asked to
extend the bench's guidance+auto-rotate+precise-snap experience to
generic decorative items received last test (log-confirmed: "Pintura
desgastada", "Planta morta"/"Planta Moribunda", "Centro de mesa
surrado" - none have a `Seat`, grabbing them only logs generic "Item
pego"). Authorized subagent use; wants it tested next iteration
alongside a bench regression check.

Spawned an Explore agent, then personally verified (and corrected) its
findings before implementing - same rule as always. Two real findings:

1. The "Posição válida/inválida" announcement has been reading
   `Placeable.canBePlaced`, which a full-tree grep confirms is NEVER
   reassigned anywhere (always `true`) - a dead field. The real gate
   `Deselect()` uses is the public `IsObjectInValidLocation(bool)`.
   Switched `HandleValidPositionFeedback` to call that directly -
   should be a no-op for benches (same underlying check) but is the
   only way to get correct live feedback for non-seat items at all.

2. The agent claimed surface-attachment (for items needing
   `isPlaceableOnSurface`) only happens on save/load or tavern
   randomization, never live. Wrong - verified myself that
   `Placeable.PEFFMJOMPMN` (called every frame from `WhileSelected`,
   the same method that drives the bench's table search) DOES
   auto-attach live, but by reading `CursorManager.GetCursorWorldPosition()`
   - the exact cursor source already proven decorrelated from keyboard
   movement in round 82.

Implemented, mirroring the bench fix's "take direct ownership instead
of trusting the automatic cursor-driven system" pattern:
- `WorldNavigationHandler.FindSurfaceAtPosition`/`FindNearestValidSurface`
  (new, analogous to `FindNearestEmptySlot`) using `Utils.CCCCIKOMAEN`
  + `SurfaceSortOrder.IsItemAllowed` fed from the item's real position.
- `DecorationModeHandler.HandleCursorMovement` now drives
  `AddPlaceableToSurface`/`RemoveFromSurface` itself for
  `isPlaceableOnSurface` items, every frame, instead of relying on the
  cursor-driven automatic version.
- New `HandleSurfaceGuidance` (mirrors `HandleSeatSlotGuidance`):
  "Superfície: X pra direita/esquerda/cima/baixo" / "Superfície bem
  aqui, pode soltar".
- Added a `Main.DebugMode` diagnostic logging all 4 placement-category
  flags (`placeableAnywhere`/`isPlaceableOnSurface`/`isPlaceableOnWall`/
  `onlyInAllowedSurfaces`) at grab time for non-seat items - these are
  per-prefab Inspector values, not statically determinable from
  decompiled source (confirmed) - next test's log will reveal exactly
  what each of the 3 items actually needs.

Scope NOT covered this round: wall-mounted placement guidance
(`isPlaceableOnWall`) - no equivalent "auto-attach" or "nearest wall"
system was found in the engine for that case, and there's no
confirmation yet that any of the 3 items actually needs it. Falls back
to the existing generic move+Enter flow with the now-correct validity
feedback; will revisit if next test's diagnostic shows it's needed.

Build clean. See `docs/modules/decoration-mode.md`, "27ª rodada".

## Stopping point - 2026-06-22, round 90
Decoration-mode bench/table association (rounds 71-89) is RESOLVED -
user confirmed "sucesso funcionou" after round 89's fix.

User pivoted to a new research request: how/where "received items"
get positioned, explicitly flagging their "starts from the inventory"
idea as an unverified guess. Spawned an Explore agent for a first
pass, then personally verified (and corrected several inaccuracies in)
its findings by reading the actual decompiled method bodies before
documenting anything - same project rule as always, agent
summaries/method names are not proof.

Answer (now in `docs/modules/inventory-and-items.md`, "19ª rodada"):
no single rule - `ShopsManager.cs`'s `CJJGKCKAFCG` processes each
`ShopOrder` at its `deliveryHour` and checks `Shop.sendToDeliveryChest`
(a fixed per-shop ScriptableObject flag) - some shops deliver to the
singleton `DeliveryChest` (a fixed world container), others go
straight to `PlayerInventory`. So the user's guess is correct for SOME
shops, wrong for others - it depends on which shop. Also mapped:
`Pickupable.cs` (manual ground pickup, always -> PlayerInventory,
unrelated to DeliveryChest), `DroppedItem.cs`/`DroppedItemFollowPlayer.cs`
(spawn position + a real randomized scatter impulse the research
agent had wrongly claimed didn't exist), and a `DeliveryChest`
"salvage on destroy" safety net for delivery-zone (`y > 800`) dropped
items that the agent had mischaracterized as "construction cleanup".

No code changed this round - pure research/documentation, per the
user's request. See `docs/modules/inventory-and-items.md`, "19ª
rodada" for full citations.

## Stopping point - 2026-06-22, round 89
round 88's direction fix didn't fully resolve it ("ainda continua o
mesmo problema") and asked to focus exclusively on debugging this,
doing all necessary code checks/verifications/measurements,
authorizing subagent use if needed.

Confirmed via the round-87 diagnostic that the direction fix DID help
(gap dropped 1.771 -> 0.797) but wasn't enough on its own. Read
`Table.PlaceSeatingGroup` in full (the engine's own code for seating a
Seat into a SeatingGroup) and found the real remaining bug:
`WorldNavigationHandler.GetSeatTargetPosition` had been computing the
bench's target from `slot.transform.position` since round 72, on an
unverified assumption that this equaled "the table-edge reference
point". `PlaceSeatingGroup` actually computes from one of the TABLE's
own `itemSpace.buildSquares` cells (`buildSquares[slot.buildSquares.x]
.GetCentrePosition()`), not from the slot's own transform at all.

Fixed: `GetSeatTargetPosition` now takes the owner `Table` and uses
that table buildSquare cell instead, matching the engine's own formula
exactly. Threading the owner table through required adding a
`_lockedTargetTable` field next to `_lockedTargetSlot` in
`DecorationModeHandler` (SeatingGroup has no back-reference to its
Table) and updating all 3 call sites
(`HandleSeatSlotGuidance`/`HandleConfirmPlacement` x2).

Also added `WorldNavigationHandler.LogTableSearchGap`'s second half:
runs the literal same `Physics2D.OverlapCircleAll` query
`Seat.GetNeighbourTable` does and logs every actual hit (collider
name/isTrigger/layer) - a safety net in case there's still a gap or a
trigger/layer-mask issue unrelated to plain distance.

Build clean. See `docs/modules/decoration-mode.md`, "25ª rodada".

## Stopping point - 2026-06-22, round 88
diagnostic returned a concrete number - search point landed 1.771
units from the real table, much larger than the "two 0.5-unit offsets
stacking" theory would predict - which prompted a closer look instead
of trusting that theory.

Real root cause found: `SeatingGroup.direction` indicates which SIDE
of the table the slot sits on (confirmed numerically - a "Left" slot
sat to the table's world-space left). That same value was being
passed straight into `Placeable.SetDirection` as the bench's own
facing - so the bench ended up facing the same way the slot pointed,
i.e. facing AWAY from the table (further left), not toward it.
`Seat.GetNeighbourTable` searches exactly 0.5 units in whatever
direction the Placeable currently faces - facing away from the table
guarantees that search lands in empty space.

Fixed: `HandleConfirmPlacement` now calls
`placeable.SetDirection(Utils.ABNPPDOGEPM(slot.direction), false)` -
the opposite of the slot's side, so the bench faces toward the table.
`GetSeatTargetPosition` (the position formula) was left untouched -
it was never the problem, only the facing direction was. This is the
first fix in this whole sequence backed by a directly measured
number rather than elimination/inference - decent confidence this is
the actual fix.

Build clean. See `docs/modules/decoration-mode.md`, "24ª rodada".

## Stopping point - 2026-06-22, round 87
reported "erro ainda o mesmo" after round 86's explicit
`GetNeighbourTable`/`GetNeighbourTableAround` call - but the new
diagnostic from that call proved decisive: `table=null` even when
WE call the engine's own search method directly, ruling out the
timing-race theory entirely. The search itself isn't finding the
table, full stop.

Cross-referencing with the already-existing `LogSeatPlacementDiagnostics`
output: the placed bench's seat ended up 0.613 units from its target
slot - well past the engine's own 0.225 search tolerance. Suspected
(not yet confirmed numerically) cause: `WorldNavigationHandler.
GetSeatTargetPosition` (round 71, designed to avoid visually
overlapping the table) adds a 0.5-unit offset toward the slot's
direction, and `Seat.GetNeighbourTable`'s own search ALSO adds a
0.5-unit offset in the same kind of direction from the bench's side -
if both point the same way, they'd stack instead of cancel, pushing
the search point roughly a tile-width past the actual table.

Added `WorldNavigationHandler.LogTableSearchGap` - logs the exact
search point the engine's own formula computes
(`buildSquare.GetCentrePosition() + Utils.NGFODNCHPHB(direction) *
0.5f`) against every nearby table's real position, to confirm the
exact gap numerically before changing the placement formula (per the
user's standing no-guessing rule).

Build clean. See `docs/modules/decoration-mode.md`, "23ª rodada".

## Stopping point - 2026-06-22, round 86
authorized switching to Opus ("sim pode trocar") but the bug persisted
regardless - clarified that Claude has no tool to switch its own
active model mid-session; only the user can do this via `/model`.

Round 85's hierarchy diagnostic log paid off: confirmed `Seat` and
`buildSquare` ARE genuine Unity children of the bench's own
GameObject (so they already auto-follow when the bench moves - the
small position offset between them is just that build-square cell's
position within the bench's own footprint, not a desync). This rules
out the "disconnected GameObject" theory from rounds 84/85 entirely -
position was never the actual problem.

Read `Seat.GetNeighbourTable`/`GetNeighbourTableAround`/`CODJEMEJFGF`
in full (not fragments) and found the real root cause: table
association only gets recalculated automatically while the bench is
actively held (once per frame, via a callback the engine fires from
`Placeable.WhileSelected`). The instant Deselect() runs, that
recalculation stops - and there was a frame-ordering race between our
mod's code and the engine's own Update() over whether the final
position ever got one more recalculation pass before the object
stopped being "selected".

Fixed by removing the race entirely: explicitly call
`Seat.GetNeighbourTableAround()` and `Seat.GetNeighbourTable()`
ourselves, directly, right before triggering Deselect() - no longer
dependent on implicit per-frame timing. Added a diagnostic log
showing the found table (if any) plus `leftRightTuck`/`upDownTuck`,
in case some specific table still structurally rejects a given seat
direction.

Build clean. See `docs/modules/decoration-mode.md`, "22ª rodada".

## Stopping point - 2026-06-22, round 85
round 84's fix (also moving `seat.transform.position`) still didn't
resolve table-association/second-bench-stuck: "testei, mesmo
problema".

Found another layer of indirection rather than guessing again:
`Seat.GetNeighbourTable` actually reads from a private `buildSquare`
field (a `BuildSquare` component reference) that's never reassigned
anywhere in the decompiled source - meaning it's a fixed,
editor-serialized reference, not something recomputed from either the
Placeable's or the Seat's live transform. Rather than attempt a 4th
guess at which transform to move, added
`WorldNavigationHandler.LogBuildSquareHierarchy` (uses
`AccessTools.Field(typeof(Seat), "buildSquare")`) that logs the actual
GameObject parent-chain and position for the Placeable, the Seat, and
the Seat's `buildSquare`, called at grab time and again at the snap
attempt - this will show definitively where the disconnect is instead
of more speculation.

Given this is now the same root issue across many consecutive rounds,
asked the user (per their own standing instruction) whether to
authorize switching to Opus for this specific investigation.

Build clean. See `docs/modules/decoration-mode.md`, "21ª rodada".

## Stopping point - 2026-06-22, round 84
round 83's `Placeable.SetPosition` fix didn't change anything:
"continua igual, as setas andam, mas não associa, e nem deixou colocar
o segundo".

Also this round: configured Windows notification-sound hooks
(`Notification` + `Stop`) in `~/.claude/settings.json` per user
request, and clarified that switching the active model mid-session
isn't something Claude can do via a tool - only the user can run
`/model`; Claude can flag when it seems warranted but can't execute
the switch itself.

Root cause (this time confirmed structurally, not just by elimination):
re-read `Seat.GetNeighbourTable` - it searches from Seat's own private
`buildSquare` field, not anything reachable from the Placeable.
`BuildSquare` components ARE genuine Unity children of their owning
GameObject (confirmed in `BuildSquare.cs` - everything reads
`base.transform.position`), so they auto-follow when a real parent
moves - but `Seat` and `Placeable` are NOT parent/child, just two
GameObjects holding cross-references to each other (established many
rounds ago). Nothing applied to the Placeable's transform was ever
going to move the Seat - this explains why every round-79/82/83
attempt (offset zeroing, direct transform write, SetPosition) failed
to fix table-association and occupancy, despite each one genuinely
fixing the symptom it targeted (drag oscillation). Fixed: both
`HandleCursorMovement` and the snap-to-slot path now also directly
set `seat.transform.position` to match the bench's new position,
since `WorldNavigationHandler.FindSeatForPlaceable` already resolves
the Seat for a held Placeable. Removed the now-explained diagnostic
that was logging unrelated cart seats ("UpperSeat"/"MiddleSeat" etc.)
as noise.

Build clean. See `docs/modules/decoration-mode.md`, "20ª rodada".

## Stopping point - 2026-06-22, round 83
confirmed the round-82 architectural fix solved the arrow-key
oscillation ("agora funcionou andar com o cursor com setas"), but
found two new symptoms: the bench placed but said "não foi associado"
(not associated with the table), and a second bench couldn't be placed
at all - correctly suspecting it was computing the same target slot as
the first bench.

Root cause: `Seat.GetNeighbourTable` (decompiled) doesn't use
`transform.position` at all - it reads `buildSquare.GetCentrePosition()`,
a separate world-grid registration that only updates through
`Placeable.SetPosition`'s internal pipeline (PixelSnap/snapToGrid.Snap/
AddItemBaseToWorldTiles). Round 82's direct `transform.position` write
moved the bench visually but never touched that grid registration, so
the game's own table-association and occupancy logic kept reading the
stale old location - explaining both symptoms at once. Fixed: both
`HandleCursorMovement` and the snap-to-slot path in
`HandleConfirmPlacement` now also call `placeable.SetPosition(1,
placeable.attachedToPlayer, placeable.snapToGrid, true)` (same call
signature as the native gamepad D-pad nudge) right after syncing
cursor+offset, so the grid registration updates too. Also added a
diagnostic to `FindNearestEmptySlot` that flags if a Seat's own
`transform.position` ever disagrees with its Placeable's, in case this
isn't the complete fix.

Build clean. See `docs/modules/decoration-mode.md`, "19ª rodada".

## Stopping point - 2026-06-22, round 82
round 81's full diagnostic: "feito, tudo igual".

The surface-gating theory is now conclusively ruled out -
`currentSurface`/`surfaceCollider`/`isPlaceableOnSurface`/
`isOnSurface` were null/false on every single logged line. The actual
finding: `CursorManager.GetCursorWorldPosition()` and the bench's real
`transform.position` were completely unrelated numbers (not just
lagging), and both independently flickered between two values 0.5
apart with no key pressed - the cursor system this whole feature was
built on (mirroring the native gamepad D-pad nudge pattern in
`Placeable.ALFOFLNNPMJ`) isn't behaving reliably here, for reasons
that resisted 3 rounds of decompiled-source reading.

Architectural change rather than another point fix: stopped routing
movement through `CursorManager` as the source of truth.
`DecorationModeHandler` now owns the held object's intended position
directly (`_heldIntendedPosition`, seeded at grab time) and forces it
onto `transform.position` every single frame (not just on key press),
in both `HandleCursorMovement` and the snap-to-slot path in
`HandleConfirmPlacement`. `CursorManager.SetCursorPositionFromWorld`
and `SetMouseOffset(zero)` calls were kept alongside (harmless,
possibly still relevant to the engine's own canBePlaced/highlight
checks) but no longer relied upon for the actual position.

Build clean. See `docs/modules/decoration-mode.md`, "18ª rodada".

## Stopping point - 2026-06-22, round 81
round 80's `SetMouseOffset(zero)` fix did NOT resolve the oscillation:
"mesmo erro, por favor valide a fundo isso, estamos girando em
círculo" - position kept alternating between two fixed values (e.g.
3.75/4.25) regardless of repeated arrow presses.

Read `Placeable.SetPosition` more carefully: it only applies the
freshly computed position unconditionally when `currentSurface` is
null; otherwise it gates the update behind `IsNewPosOnSurface`, which
requires `isPlaceableOnSurface` (false for ordinary floor furniture)
- meaning if `currentSurface` is set on this bench for any reason,
every position update while held gets silently rejected, matching the
observed "stuck between two values" exactly. Fixed (and instrumented):
`HandleGrab` now calls `Placeable.RemoveFromSurface(false)` right
after grabbing (the game's own "no longer on a surface" method, a
no-op if `currentSurface` was already null). Also expanded the
existing guidance-calc debug log to print `currentSurface`,
`surfaceCollider`, `isPlaceableOnSurface`, `IsObjectOnASurface()`, and
the cursor's own live position every check - so if this still isn't
the full fix, the next log gives a definitive answer instead of
another guess.

Build clean. See `docs/modules/decoration-mode.md`, "17ª rodada".

## Stopping point - 2026-06-22, round 80
"mesmo comportamento" on the round-79 diagnostic, and asked whether
there's a hot-reload option to avoid restarting the game each test -
there isn't (MelonLoader loads the DLL and applies Harmony patches
once at startup; confirmed no built-in reload mechanism, told the user
to keep restarting for now).

Real root cause found by reading the decompiled `Placeable` class
directly instead of guessing further: `Placeable.GetNewPosition()`
computes `finalPos = CursorManager.GetCursorWorldPosition() +
HOFIBNPEDAA` (a private "mouse offset" field). The game recalibrates
that offset whenever it thinks the real mouse is in use, in a way that
exactly cancels out our `SetCursorPositionFromWorld` calls one cycle
later - explains the one-frame blip-then-revert pattern seen in the
round-79 log (cur snapped from (6.25,906.98) to (6.75,906.98) for
exactly one check after an arrow press, then back, with no key in
between). Fixed via the public `Placeable.SetMouseOffset(Vector3)`
API: `DecorationModeHandler` now zeroes this offset right after
grabbing, and again on every arrow-key move (defensive), so
`finalPos` becomes exactly the cursor position with nothing fighting
it. This is the first fix targeting the actual root cause in this
area rather than a downstream symptom - needs a real test to confirm.

Build clean. See `docs/modules/decoration-mode.md`, "16ª rodada".

## Stopping point - 2026-06-22, round 79
round-78 diagnostic ("testado, valide").

Confirmed: `_lockedTargetSlot` is NOT breaking - the "locked slot
dropped" log never fired, same slot (11.85, 905.83) the whole session.
So the slot lock (round 76/77) is working correctly; the oscillation
is happening somewhere else. Some arrow presses correctly decreased the
announced count by 1 (real movement working), but the count also
increased again on its own between checks with zero keys pressed - so
either the bench's own live position (`beingPlaced.transform.position`)
is drifting without input, or something in the offset math itself is
unstable. Added a raw diagnostic log (`DecorationMode: guidance calc
cur=... target=...`) printed on every 0.3s check, so the next test
shows the literal world position driving the calculation instead of
inferring it.

Build clean. See `docs/modules/decoration-mode.md`, "15ª rodada".

## Stopping point - 2026-06-22, round 78
round 77's slot lock fix: "continua ainda sem diminuir".

Confirmed via the raw key log that the lock IS breaking on its own -
"Vaga: 5 pra direita, 5 pra cima" became "Vaga: 10 pra direita, 2 pra
baixo" 0.85s later with zero keys pressed in between. Not the
WASD/arrow mix-up (round 76) and not float-rounding jitter near a
tile boundary (considered, but the jump is too large/structured for
that) - `_lockedTargetSlot` is being dropped because `IsSlotEmpty`
reports it newly "occupied" when nothing in the scene should have
changed. Root cause not yet found. Added surgical diagnostics instead
of guessing again: `WorldNavigationHandler.IsSlotEmpty` now logs
exactly which seat (by instance ID + position) caused a rejection, and
`DecorationModeHandler.HandleSeatSlotGuidance` logs the lock
transition itself. Needs one more F12 test (just holding a bench
still, no need to fully place it) to get the real answer.

Build clean. See `docs/modules/decoration-mode.md`, "14ª rodada".

## Stopping point - 2026-06-22, round 77
with arrow keys this time (confirmed via raw key log): "só diminuiu
andando, mas mesmo assim ainda está tudo confuso me levando a lugar
nenhum".

Root cause confirmed in log: the "Vaga: X pra ..." distance oscillated
between two values (e.g. 9/10, then 5/6) dozens of times, never
converging - `HandleSeatSlotGuidance` re-picked "nearest empty slot"
on every 0.3s check, and the table's 6 slots (2 columns) meant the
"nearest" slot flip-flopped between two columns as the bench moved
near the midpoint, so the announced target kept changing instead of
staying fixed. Fixed: `DecorationModeHandler` now locks onto one
target slot per hold (`_lockedTargetSlot`), re-picking only if that
slot becomes occupied; `HandleConfirmPlacement` snaps to that same
locked slot instead of a fresh search, so what's announced matches
where Enter actually places it.

Also found (via code reading, not yet reported by the user) a second
real perf bug: `WorldNavigationHandler.FindNearestEmptySlot` called
`Object.FindObjectsOfType<Table>()`/`<Seat>()` directly on every call -
and it's now called 3x/sec while holding a bench (for the guidance
above), at the ~150-180ms-per-call cost confirmed in round 74. Added a
static cache (`_staticCachedTables`/`_staticCachedSeats`, 20s refresh,
same reasoning as the instance-level caches) shared by
`FindNearestEmptySlot`, the new `IsSlotEmpty`, and `LogNearestSlotDistance`.

Build clean. See `docs/modules/decoration-mode.md`, "13ª rodada".

## Stopping point - 2026-06-22, round 76
the new "Vaga: ..." guidance (round 75) took too long to update and
eventually went silent ("se perdeu... não falou mais nada").

Root cause confirmed via the raw key-press log (captures every key,
not just ones the mod listens for): only W/A/S/D were pressed the
entire time the bench was held - not a single arrow key. WASD moves
the player (and the camera, which drags whatever world point the held
bench is anchored to - explaining why the distance still drifted a bit
without arrows), but only the arrow keys actually move the held bench
(`HandleCursorMovement`). Added `DecorationModeHandler.
HandleWasdReminder`: the first time W/A/S/D is pressed while holding a
seat without having pressed an arrow key yet, says "Pra mover o banco,
use as setas do teclado, não W A S D" (once per hold).

Build clean. See `docs/modules/decoration-mode.md`, "12ª rodada".

## Stopping point - 2026-06-22, round 75
round 74's PERF timers: "lentidão melhorou, resto nada" (lag mentioned
as ongoing concern; bench snap still didn't engage).

Root cause of the lag, confirmed via the timers: a single
`Object.FindObjectsOfType<T>()` call costs ~150-180ms in this scene by
itself (cost scales with total scene object count, not the small
result), and it was running 1x/sec in 3 places, plus
`GetEmptySeatSlots` cost ~25ms every single frame unthrottled. Fixed:
widened the seat/table cache refresh to 20s (identity is stable, only
position changes, which comes free from the cached objects), cached
the door scan the same way instead of rescanning every second, and
throttled `GetEmptySeatSlots` to run every 0.3s instead of every frame.

Root cause of "soltou fora", confirmed via the new distance diagnostic:
real distances at confirm time were 2.6-5.1 units - the bench was never
actually near a slot, because while moving the cursor with arrows
(holding a bench), there was no feedback telling the player how far
the cursor was from the nearest slot (the existing "Lugar pra banco"
announcement only reacts to the player's own position, which doesn't
move during cursor nudging). Added `DecorationModeHandler.
HandleSeatSlotGuidance`: while holding a seat, announces "Vaga: X pra
direita/esquerda, Y pra cima/baixo" (tiles) whenever that offset
changes, and "Vaga bem aqui, pode soltar" at zero offset - same
phrasing convention as the door/bed step-guidance system.

Build clean. See `docs/modules/decoration-mode.md`, "11ª rodada".

## Stopping point - 2026-06-22, round 74
round 73's snap-position fix with "Banco 8": still placed away from
the slot, and reported the game running very slowly.

Log showed `confirm placement -> False snapped=False` both times - the
snap logic never even triggered (no slot considered close enough
within the 3-tile radius). Widened `SnapToSlotRadius` to 5 tiles and
added `WorldNavigationHandler.LogNearestSlotDistance` (fires whenever
the snap doesn't trigger) so the next log shows the exact real distance
instead of guessing whether 3 tiles was just barely too tight.

Lag: measured actual frame-to-frame gaps in the log directly instead of
guessing - found ~1-second freezes recurring through almost the entire
session, not just during decoration mode. Timing matches three
existing once-per-second routines (`RefreshSeatSceneCache`, the
once-per-second door scan, and `GetEmptySeatSlots`'s per-frame cost,
the latter grown slightly this feature). Added `Stopwatch`-based timing
(debug-mode only) to all three, logging "PERF ..." whenever one takes
over 3ms, instead of guessing which one (or combination) is the real
cause.

Build clean. See `docs/modules/decoration-mode.md`, "10ª rodada".

## Stopping point - 2026-06-22, round 73
round 72's new auto-snap feature, fresh log read (F12 was on).

Real bug found and fixed: log showed `confirm placement -> False
snapped=True` repeated 10+ times - the auto-snap feature WAS running,
but always landed the bench in an invalid (overlapping) spot. Re-derived
the right target position from the engine's own
`Seat.GetNeighbourTable`/`Table.GetSeatingGroup` formulas: the
`SeatingGroup.transform.position` we were reading is the table-edge
reference point, not the seat's own resting spot - the seat needs to be
pushed half a tile further out, in the slot's own direction, to clear
the table's footprint. Fixed in
`WorldNavigationHandler.GetSeatTargetPosition`. Not yet confirmed live
(first test of the corrected version still pending).

Also realized a flaw in my own prior investigation of the "Banco 8/4
inconsistent numbering" report: a GameObject's `.name` in the log
(e.g. "1135 - Banco Grande(Clone)") is the shared item/prefab id, not a
unique identifier - matching names across log lines never actually
proved "same physical bench." There may simply be multiple "Banco
Grande" benches in the tavern, each correctly and stably numbered, and
the player picking up a different one each time without realizing it.
Added real Unity instance-ID logging to grab/hold events to settle this
with hard proof next round instead of more guessing.

Build clean. See `docs/modules/decoration-mode.md`, "9ª rodada".

## Stopping point - 2026-06-22, round 72

Still `feature/inventoryAndGetItens`, not yet committed. No new log
this round (same session/file as round 71) - user asked for new
features and reported a possible numbering issue based on that same
play session.

Implemented: pressing Enter to place a bench now auto-snaps it exactly
onto a free table slot (within 3 tiles) and auto-rotates it to face the
direction that slot requires (`Placeable.SetDirection`, the same public
API the native R key uses) before confirming - removes the need to hit
the exact mark or know which way to face by hand. Not yet tested live
(first round this exists). Slot numbers ("vaga N") got the same
stable/permanent-assignment fix as bench/table numbers. Messages
reworded per request: "Banco N pego" / "Banco N posicionado na vaga V"
/ "Banco N solto, associado à mesa M" or "...mas sem mesa por perto".

User reported seeing "Banco 8" (and separately "Banco 4") in two places
at once after moving a bench across the room - re-read the round 71 log
in full (no new log was generated) and found no contradiction: the
moved bench was always "Banco 8", a second never-moved bench was always
"Banco 4". Did not guess a fix - likely explanation to confirm next
round: the proximity announcement radius is only 1.5 units, so a bench
moved across a small room might still be the SAME bench triggering the
announcement from far away, not a genuine duplicate. Asked for a
focused F12 retest of exactly this scenario.

Build clean. See `docs/modules/decoration-mode.md`, "8ª rodada".

## Stopping point - 2026-06-22, round 71

Still `feature/inventoryAndGetItens`, not yet committed. User tested
round 70's fixes, log read in full (F12 was on).

Numbering fix: **confirmed working** via log - the same physical bench
("1135 - Banco Grande(Clone)") was grabbed twice and announced "Banco
8" both times; a second, never-grabbed bench appeared 4 separate times
always as "Banco 4". No further action needed there.

"Nada para interagir aqui" (added last round): **removed**, not fixed.
User reported it fired even with something there, including right when
opening a door. Log proved the underlying signal
(`InteractObject.GetCurrentInteractGO()`) is unreliable for this: zero
"CurrentInteract CHANGED" lines in the whole session (never went
non-null even once) despite the player standing 0.3 units from a door
that opened minutes later - this field tracks some interactables (beds,
confirmed earlier) but not doors. Removed the announcement rather than
guess at a better signal without proof one exists.

The new placement diagnostic (added last round) already produced a
useful first data point: the bench's second drop landed ~1.55-3.1 units
from any table slot, facing "Right" while nearby slots want "Left" -
supports the facing-direction theory, but isn't yet a close-enough
attempt to isolate whether distance or direction is the deciding
factor. Asked for one more placement attempt, as close and as
correctly-facing as the player can manage, to get a real near-miss
sample.

Build clean. See `docs/modules/decoration-mode.md`, "7ª rodada".

## Stopping point - 2026-06-22, round 70

Still `feature/inventoryAndGetItens`, not yet committed. User tested
round 69's lag fix and benches/tables, log read in full (F12 was on).

Real, confirmed bug fixed: `GetSeatNumber`/`GetTableNumber` re-sorted
ALL seats/tables by CURRENT position on every call - fine for objects
that never move, but decoration mode exists to move them. The same
physical bench (one GameObject, confirmed in the log) got announced as
"Banco 8" twice and "Banco 4" once, purely because moving it changed
its rank in that live sort. Fixed by assigning each seat/table a
number ONCE (first time anything asks, ordered by position at that
moment among the not-yet-numbered ones) and keeping it fixed afterward
- `GetEmptySeatSlots`' "mesa N" label had the same bug (the table was
also moved repeatedly this session) and got the same fix.

User also asked whether a placed bench "goes back on its own" if not
associated with a table - confirmed no code does that; this was very
likely the numbering bug's illusion (same physical bench, different
announced number, looking like a different/reverted bench).

Placement precision ("não deixa soltar exatamente onde anuncia"; "perto,
mas sem mesa por perto") was NOT fixed blind this round - confirmed via
decompiled source that bench-table association depends on the bench's
FACING direction, not just position, with a tight 0.225-unit tolerance,
and that most furniture auto-snaps to the grid on drop (so cursor drift
likely isn't the cause). Added `WorldNavigationHandler.
LogSeatPlacementDiagnostics` (debug-mode only, fires on every bench
placement) logging the bench's real final position + facing direction
and its exact distance to every nearby table slot - next round's log
will show the real numbers instead of more guessing.

Implemented (re-requested by user, previously flagged but not built):
pressing E with nothing to interact with now announces "Nada para
interagir aqui" instead of silently doing nothing.

Build clean. See `docs/modules/decoration-mode.md`, "6ª rodada".

## Stopping point - 2026-06-22, round 69

Still `feature/inventoryAndGetItens`, not yet committed. User tested
round 68's lag fix - improved but still present. Found the rest of the
cause: the 0.3s throttle only limited how OFTEN the expensive
`FindObjectsOfType` scans ran, but each cycle still did up to 3
separate scans (~10/sec total). Split concerns properly: scene scans
(Seat/Table) now cached once per second in shared fields
(`_cachedSeats`/`_cachedTables`, refreshed by `RefreshSeatSceneCache`),
the cheap per-frame distance-check/announcement logic reads the cache
instead of re-scanning.

Also fixed, all from real log evidence:
- Grab failing on a second bench because the closest Placeable was an
  unrelated fixed fixture ("Grifo"/torneira) that can't be selected -
  `HandleGrab` now tries every candidate in distance order instead of
  giving up after the closest one.
- No bench/table/slot numbers in grab/place feedback - added global
  numbering helpers (`WorldNavigationHandler.GetSeatNumber`/
  `GetTableNumber`), used consistently everywhere (proximity
  announcement, nav list, decoration mode grab/place messages) so the
  same number always means the same physical object.
- Re-investigated "bench still announced after pickup" with fresh log
  - confirmed this was NOT the held-exclusion bug anymore; it was the
  same bench, legitimately re-announced after being placed away from
  any table (correct behavior).
- Investigated user's "Conversar (E) does nothing" report - log
  confirmed they walked away before pressing E (proximity focus had
  already become "none"); not a decoration-mode bug, but flagged the
  gap that nothing tells the player E had no target. Not fixed yet -
  separate system, asked user if they want it addressed.

Build clean. See `docs/modules/decoration-mode.md`, "5ª rodada".

## Stopping point - 2026-06-22, round 68

Still `feature/inventoryAndGetItens`, not yet committed. User couldn't
even test round 67 - game was lagging/freezing badly. Found and fixed
a real performance regression introduced by round 67's own additions:
`HandleSeatAnnouncement` and `HandleSeatSlotAnnouncement` (called
every frame, unconditionally) each did one or more full-scene
`Object.FindObjectsOfType<...>()` scans, and `GetEmptySeatSlots` did
an uncached `AccessTools.Field` reflection lookup per table per call -
all of this stacking on top of every frame with no throttle, unlike
the existing item/dirt scans which already cycle on a timer. Fixed by
giving both seat methods the same 0.3s throttle pattern already used
elsewhere (`ItemSoundCycleInterval`), and caching the `seatingGroups`
FieldInfo as a static readonly field instead of looking it up on every
call. Build clean. Needs a retest to confirm performance is back to
normal before re-attempting the seat-slot test from round 67.

## Stopping point - 2026-06-22, round 67

Still `feature/inventoryAndGetItens`, not yet committed. F12 was
actually on this time - first round in this thread with real log
evidence instead of guessing from descriptions.

Log disproved the user's "T grabs, R places" theory: T had zero
observable effect; the game's own on-screen hint confirmed Q=Pegar,
R=Rotacionar (rotate, not place); the actual grab/place both lined up
with our own Enter key. Found one root-cause bug explaining three
separate symptoms at once: `Seat` has its own `public Placeable
placeable;` field, meaning Seat is never on the same GameObject as the
bench's Placeable (`selectedGameObject`) - so `seat.gameObject ==
heldObject` and `beingPlaced.GetComponent<Seat>()` could never match.
Fixed in three places (WorldNavigationHandler's proximity exclusion +
nav-list exclusion, DecorationModeHandler's seat-specific placement
message) by comparing through `seat.placeable.gameObject` instead.

Also confirmed via log (144x `occupied=False`, zero exceptions) AND
via decompiled source (the only two methods that ever write
`SeatingGroup.occupied` - `PlaceSeatingGroup`/`GetSeatingGroup` - have
no call sites anywhere) that this flag is simply dead/unmaintained by
the game in this version, not a timing bug on our end. Replaced with a
real computed check: is an actual Seat already sitting within ~0.3
units of that slot's position.

Numbering changed to always include a number, even with only one item
(was previously "Banco" without a number when count==1, "Banco 1"
when 2+) - simpler, predictable, per explicit request.

Build clean. See `docs/modules/decoration-mode.md`, "4ª rodada", for
full detail and the next targeted test (drop a bench specifically
onto a "Lugar pra banco" slot, not just anywhere valid).

## Stopping point - 2026-06-22, round 66

Still `feature/inventoryAndGetItens`, not yet committed. User
discovered T/R already grab/place natively in decoration mode
(probably the default Select/Rotate-equivalent action keys) - our own
Enter-based grab/place left in place since it doesn't conflict
(only acts when nothing/something specific is selected), likely just
redundant now.

Two more reports addressed:
- Bench kept being announced as "Próximo: Banco" even right after
  being picked up - it's still a real GameObject while held (just
  following the cursor), so proximity scans kept finding it. Fixed by
  excluding `SelectObject.GetPlayer(1).selectedGameObject` from both
  the proximity announcement and the Page Up/Down nav list scan.
- A used-up seat slot ("Lugar pra banco") possibly still showing as
  available - not fixed blindly; added a throttled (1/sec) debug log
  in `GetEmptySeatSlots` printing every nearby `SeatingGroup.occupied`
  state, to confirm with real data next test whether the game's own
  `occupied` flag actually updates after placing via T/R (vs our own
  Enter), instead of guessing.

Also added unconditional debug logging (gated by DebugMode only, not
by which key triggered it) whenever `selectedGameObject` changes to
non-null or back to null - validates whether T/R genuinely select/
deselect through the same field our code reads, per the user's
explicit request to "validar e debugar se isso está acontecendo de
verdade" instead of assuming.

Build clean. See `docs/modules/decoration-mode.md`, "3ª rodada".

## Stopping point - 2026-06-22, round 65

Still `feature/inventoryAndGetItens`, not yet committed. User
confirmed grabbing a bench now works (round 64's player-position fix
for `HandleGrab`). Reported 4 new points without F12 enabled during
that test (so this round's fixes are code-review-based, not
log-confirmed - flagged to the user to enable F12 next time):

1. **Bench numbering unstable** - root cause: both the floor-dirt and
   seat lists in `BuildTargetList` ordered by LIVE distance-to-player,
   which changes every time the list rebuilds (every Page Up/Down
   press) as the player moves - "Banco 1" could silently point at a
   different physical bench between presses. Fixed: ordered by fixed
   world position (x then y) instead, for both lists.
2. **No exact "where to put the bench" points** - confirmed via
   `Table.cs` there's a real, precise signal: a private
   `SeatingGroup[]` (`seatingGroups`), each with its own world
   Transform and an `occupied` bool already tracked by the game. Read
   via reflection (`AccessTools.Field`, read-only, no game behavior
   changed). Empty slots now appear in the "Missão" nav list as
   "Lugar pra banco (mesa)" and get their own proximity announcement
   ("Próximo: Lugar pra banco junto da mesa"), mirroring how stains/
   benches already work.
3. **Cleaning sound still silent** - user had since added `limpou.wav`
   for real; rebuilding now shows it copies cleanly (no more warning).
   Asked for 100% volume specifically for this sound (not the shared
   60% baseline) - added a `1/Volume` multiplier just for
   `CustomSounds.PlayObjectiveCompleted()`.

Build clean. None of round 65's fixes confirmed via log yet (F12 was
off during the test that produced this feedback) - asked the user to
enable it before the next round. See `world-object-navigation.md`
("35ª rodada") and `inventory-and-items.md`.

## Stopping point - 2026-06-21, round 64

Still `feature/inventoryAndGetItens`, not yet committed. User
corrected round 63's wrong conclusion: benches already exist in the
world (nothing about construction in the actual mission text) - the
real bug was elsewhere. Three real fixes this round:

1. **`limpou.wav` would never have worked even with the file in
   place** - found while checking whether the user had added it yet
   (they hadn't, expected) - but also found the `.csproj`'s
   `CopyToMods` target has a hardcoded wav filename list that I forgot
   to update when adding `limpou.wav` to `CustomSounds.cs` last round.
   Fixed (added to the list, `ContinueOnError="WarnAndContinue"` so a
   still-missing file warns instead of failing the build).
2. **Decoration-mode grab fixed**: `HandleGrab` searched at the MOUSE
   CURSOR's world position, but the proximity announcement
   (`HandleSeatAnnouncement`) searches near the PLAYER - for someone
   who never moves a physical mouse, the cursor has no reason to be
   anywhere near the avatar, fully explaining "anuncia o banco mas
   Enter não pega". Now searches by player position
   (`Physics2D.OverlapCircleAll`) and snaps the cursor to the grabbed
   object's position on success, so arrow-key movement starts
   somewhere sensible.
3. **Benches missing from the "Missão" nav list**: the attempted fix
   last round checked `placeable.GetComponent<Seat>()` only on objects
   already found via the `Placeable` loop - confirmed wrong by the
   exact same mismatch (proximity announcement, which scans `Seat`
   directly, found them; the nav list didn't), meaning `Seat` isn't
   necessarily on the same GameObject as the bench's `Placeable`.
   Fixed by giving `Seat` its own scan loop in `BuildTargetList`, same
   pattern as `FloorDirt`.

Known, undecided gap: a clean table doesn't show in "Missão" even if
it still needs more seats nearby - no intrinsic signal found for "this
table needs seating" the way Table's dirt level signals "needs
cleaning". Left as a documented limitation, not guessed at.

Build clean (`dotnet build` succeeded, one harmless warning for the
still-missing `limpou.wav`). See `docs/modules/decoration-mode.md` and
`docs/modules/inventory-and-items.md` ("18ª rodada").

## Stopping point - 2026-06-21, round 63

Still `feature/inventoryAndGetItens`, not yet committed. User tested
round 62's decoration mode: B toggled and announced correctly, but
Enter found no bench to grab and none showed in the Missão nav list.
Also asked whether "valid position" (no overlap) could differ from
"position the mission actually wants" - good catch, confirmed yes.

Root cause of the missing bench: `DecorationMode.ToggleDecorationMode()`
only enables rearranging objects ALREADY in the world - no
purchase/construction panel opens from B. Getting a first bench needs
a separate system (`TavernConstructionUI`, `ConstructionManager.
currentInstantiatedGO`, tied to the "BuildMode" action) that's never
been investigated - already an open TODO in `game-api.md` line 277.
Not a bug in what was built; the user simply hasn't constructed a
bench yet, so there's nothing for decoration mode to find.

Confirmed via `Seat.cs`: `canBePlaced` (no overlap) and "actually
useful" are genuinely different - a seat only counts toward the
"assentos disponíveis" goal once `Seat.table` gets set, which only
happens if it lands adjacent to a table (`Seat.GetNeighbourTable`,
sometimes a frame delayed). Updated `DecorationModeHandler` to check
this after placing a seat specifically (deferred one frame via
`_pendingSeatCheck` to avoid reading a stale null) - announces "Banco
colocado, associado a uma mesa" or "...mas sem mesa por perto" instead
of generic "Item colocado". Also added `HandleSeatAnnouncement` to
`WorldNavigationHandler.cs` (distance-based, not proximity-system-based
like FloorDirt/Table - Seat is a plain MonoBehaviour, no IProximity).

Confirmed for the user (not a fix, just an answer): the nav list
showing "Mancha no chão 1" through "4" means 4 real, currently-uncleaned
stains are nearby right now - reflects live world state.

Build clean. Real next step, if the user wants to continue this
thread: investigate Construction Mode (BuildMode action,
TavernConstructionUI) - without a bench actually existing, the rest of
decoration mode's bench-specific behavior can't be tested at all. See
`docs/modules/decoration-mode.md`.

## Stopping point - 2026-06-21, round 62

User chose "cursor virtual completo" for decoration-mode keyboard
accessibility (asked via AskUserQuestion after round 61 flagged this
as a sizable new feature needing scoping). Implemented as a new
module - see `docs/modules/decoration-mode.md` for full detail.

Key finding that made this tractable without any Harmony patching:
`CursorManager` already exposes a public world-space cursor position
(`GetCursorWorldPosition`/`SetCursorPositionFromWorld`, static) that
the game's OWN gamepad D-pad support already nudges by 0.5 per press
during placement (confirmed in decompiled `Placeable.ALFOFLNNPMJ`,
gated on `PlayerInputs.IsGamepadActive` - never true for a keyboard
player). `Placeable.WhileSelected()` follows whatever the cursor's
current position is every frame regardless of how it got there, so
calling the same public API from our own keyboard handler is enough -
no patch needed.

New `DecorationModeHandler.cs`: announces decoration mode on/off,
Enter grabs whatever's under the cursor (`Physics2D.OverlapPointAll`)
when nothing is held, arrow keys nudge the cursor by 0.5 when holding
something, announces "Posição válida"/"inválida" on change (reading
`Placeable.canBePlaced` - the same value driving the sighted red/
highlight tint), Enter again calls `SelectObject.Deselect()` to
confirm (safe by the game's own design - it no-ops if the spot isn't
actually valid).

Also added `Seat` to the "Missão" category in
`WorldNavigationHandler.CategorizePlaceable` per the user's request
("banco precisa aparecer em itens de missão").

Scope boundary documented, not built: pulling a brand-new item from a
purchase/construction menu into decoration mode for the first time is
a separate, uninvestigated entry point.

Build clean. None of this has been tested live yet - asked for a
retest (see decoration-mode.md's test steps): toggle B, grab an
existing bench/table, move with arrows, listen for valid/invalid,
confirm with Enter.

## Stopping point - 2026-06-21, round 61

Still `feature/inventoryAndGetItens`, not yet committed. User
confirmed round 60's fixes (stains announced, table cleaning works)
and asked for two more things: an announcement when cleaning the
table updates the tutorial objective checklist ("missão atualizada,
mas não anunciou nada"), and a sound for floor stains being cleaned
("não tem som... coloca aí mesmo que o jogo não coloque").

Found `NewTutorialManager.ObjectiveCompleted(int, bool)` - marks an
objective's checkmark and calls `PlayObjectivesCompletedSound()`,
which goes through `MultiAudioManager` - the same audio system already
confirmed unreliable/silent for us this project (footsteps never
worked through it either). The objective's text label doesn't change
when only the checkmark icon toggles, so `DialogueAnnouncer`'s
changed-text scan never caught it.

Added a Harmony postfix on `ObjectiveCompleted` (in
`TutorialTracePatch.cs`) that announces "Objetivo concluído: {texto}"
and plays our own sound instead of the game's unreliable one. New
shared clip `limpou.wav` (added to `CustomSounds.cs`,
`PlayObjectiveCompleted()`) also wired into
`CleaningDebugPatch.FloorDirtDestroyedPostfix` for the floor-stain
case - same clip reused for both, per user's own suggestion that a
short ~2s sound is fine either way.

**User needs to drop a `limpou.wav` file in the project root** (same
pattern as the other custom sounds) before either of these is
audible.

User also flagged a NEW feature area: decoration mode (B key) -
placing a bench/seat via mouse-only drag (cursor over item, left-click
to grab, drag to a valid spot, click again to place), asked me to
investigate keyboard support and a way to announce "only valid
positions". Confirmed in decompiled `Placeable.cs` that validity IS a
real, readable signal (`public bool canBePlaced`, toggled by overlap
checks, same value driving the sighted red/highlight tint) - so
audio feedback for valid/invalid IS feasible. Did not start building
this - full keyboard-driven decoration mode (virtual cursor or
grid-snap movement, grab/release, valid-position feedback) is a
sizable new subsystem, not a small fix, and "bancos precisam aparecer
em itens de missão" is ambiguous for an unplaced item (the Page
Up/Down nav list only scans objects already in the world). Asked the
user to scope this before building it blind.

Build clean (`dotnet build` succeeded).

## Stopping point - 2026-06-21, round 60

Still `feature/inventoryAndGetItens`, not yet committed. Read the
retest log - **table cleaning confirmed working**: `Table.MouseHold`
fired continuously, `useHoldTime` climbed to 10s, `dirtiness` dropped
2000->0, ended `result=True`. Holding the left mouse button standing
still near the table is the real, working interaction. Table cleaning
mechanic considered resolved.

Floor stains: zero proximity-focus events on any FloorDirt in this
whole log session. User reported still no announcement, very
imprecise routing, very inconsistent at finding stains, and explicitly
asked for a custom announcement "like the table" even without an
official game prompt. This is a navigation/findability problem, not a
cleaning-mechanic problem (the holding-E mechanic itself was already
confirmed working in round 59) - addressed in `WorldNavigationHandler.cs`
instead (see `world-object-navigation.md`, round 34):
- `HandleFloorDirtAnnouncement()` (new) - speaks "Próximo: Mancha no
  chão: segure E pra limpar" whenever the game's own proximity
  manager focuses a FloorDirt, regardless of whether the game shows
  its own on-screen text for it (confirmed it usually doesn't).
- `BuildTargetList()` - floor dirt entries now use `GetApproachPosition`
  (same walkable-offset fix already applied to Placeables) instead of
  the dirt's exact center, for more reliable Home-key routing.
- Multiple nearby stains are now numbered ("Mancha no chão 1", "2",
  ...) instead of all sharing one indistinguishable name in the
  Page Up/Down list.

Build clean. Asked for a retest: walk near a stain (listen for the
new announcement), cycle Page Up/Down with 2+ stains nearby, and try
Home-key guidance to one.

## Stopping point - 2026-06-21, round 59

Still `feature/inventoryAndGetItens`, not yet committed. Read the
round-58 `CleaningDebugPatch` log directly (file
`26-6-21_21-32-17.log`) instead of asking the user to describe more.

Confirmed: the user's one successful clean was a FLOOR STAIN, not the
table (`FloorDirt DESTROYED` at 21:38:10, after `workDone` climbed
0.0->3.0 with zero resets). Every OTHER stain attempt reset to 0.0
partway through - not a position issue as the user guessed, but
holding E without ANY interruption for ~3-4s straight; releasing even
briefly (or losing proximity focus) zeroes accumulated progress.

`Table.MouseHold` (the real method behind the "Use" action) appeared
ZERO times in the entire log, despite proximity focus landing on the
table multiple times - cross-checked raw keydowns at every such
window: user only ever pressed E or movement keys near the table,
never a sustained mouse hold while actually focused on it (2 stray
`Mouse0` keydowns in the log both occurred while focus was on a
FloorDirt, not the table). This means the "(tecla E)" announcement
hint added in round 55 is actively misleading for the table case -
flagged in the module doc, not yet fixed (don't know the real
key/button yet, just confirmed E isn't it).

Asked for one more targeted retest: hold left mouse button
continuously (~4-5s, standing still) right after the "Mesa grande:
Limpar" prompt is heard. Will check for `Table.MouseHold` log lines
specifically next round - presence/absence is unambiguous ground
truth regardless of what's perceived on screen.

No code changed this round (log-reading only). See
`docs/modules/inventory-and-items.md`, "15ª rodada".

## Stopping point - 2026-06-21, round 58

Still `feature/inventoryAndGetItens`, not yet committed. User
reported: "Atribuir Teclas" reads nothing useful (known pending issue
in `main-menu-and-options.md`), zero floor-dirt announcements this
round, table still not cleanable, character sometimes turns on E with
no clean happening, and explicitly asked for deep investigation with
debug logging instead of more theorizing.

Read `Table.MouseHold` directly (confirmed this IS the method behind
the "Use" action, via `IInteractable`) - refined round-57's finding:
with the mop selected, requires holding "Use" for >= 0.3s
(`PlayerInputs.GHKOCEOEKGK`) before anything happens (round 12's
claim that holding blocks it was backwards - it's a MINIMUM hold, not
a maximum), then checks for a free clean position
(`IsAnyPositionToCleanAvailable`) and walks the character there
(`GoToPosition`) before work starts accumulating.

Added `CleaningDebugPatch.cs` (new Harmony patch, log-only, no
behavior change) - registered in `Main.cs` alongside the other
patches. Captures, gated behind F12 like everything else:
- Every `Table.MouseHold` call (use-hold time, dirtiness, work
  progress, result).
- Every `FloorDirt.Clean` call (work progress, result).
- `FloorDirt.DestroyFloorDirt` / `Table.SetDirtiness` reaching 0 -
  confirms the moment something is actually cleaned (ties to the
  user's "tem que sair da categoria de itens" requirement).
- Current proximity focus (`InputByProximityManager`), logged once
  per change only - will show whether the game ever focuses on a
  FloorDirt/Table while walking nearby.

Build clean (`dotnet build` succeeded). Asked for a retest with F12
on; will read the MelonLoader log directly next round, same workflow
as always - no more theorizing without log evidence.

## Stopping point - 2026-06-21, round 57

Still `feature/inventoryAndGetItens`, not yet committed. User pushed
back on the round-56 "floor dirt cleans automatically" claim and
reported E does nothing on the table even pressed the same way as
doors. Correctly so - that claim was wrong, made from reading
`Mop.cs` in isolation without checking what actually calls into it.

Read `UseObject.Update()` (the real input dispatcher, not read
before) and found it branches on the focused object's Unity tag:
- Tag `"FloorDirt"`: requires HOLDING the `"Interact"` action (the
  same one doors/chests use) while proximity-focused on the stain -
  not automatic, not a tap.
- Anything else, including `Table`: goes through a completely
  different action, `"Use"` - not `"Interact"`/E at all. This is
  exactly why pressing E "like the doors" does nothing on the table -
  the table doesn't listen to that action.

Can't confirm the physical key bound to `"Use"` from decompiled C#
alone (Rewired bindings are remappable, stored as data, not in
source). Two next steps suggested to the user: check Options >
Atribuir Teclas (already navigable per
`main-menu-and-options.md`) for the "Usar"/"Use" entry, or try
holding the left mouse button near the table as a quick guess.

Build clean (no code changed this round). See
`docs/modules/inventory-and-items.md`, "13ª rodada" (now corrected in
place rather than re-numbered, since round 56's claim was wrong, not
superseded).

## Stopping point - 2026-06-21, round 55

Still `feature/inventoryAndGetItens`, not yet committed. User
confirmed the round-54 `HotbarSwapPatch` fixed the vanishing-item bug
("agora ele seta direito"). Unsure whether "Limpar" (cleaning the
table with the mop) worked, and asked whether facing direction or
mouse movement might matter.

Found the real blocker without needing to ask: the game's own
on-screen action hint (`"[E] Limpar"`, captured by
`DialogueAnnouncer.ScanAndAnnounceText`) always had the correct key -
it was being stripped (`ActionPromptPattern`) before announcing,
leaving just "Limpar" with no way to know which key to press (the
user had been guessing: Q, F, E, Ctrl+Enter, mouse click). Added
`ActionPromptKeyPattern` to capture the bracketed letter and append
"(tecla X)" to every action-prompt announcement - fixes this for any
interaction whose key isn't already known, not just cleaning. Since
this is the same proximity-based mechanism already used for doors/
chests (no mouse aiming needed there either), facing direction/mouse
movement probably aren't factors - but only a live retest with the
now-known key can confirm.

Build clean. See `docs/modules/inventory-and-items.md` ("12ª rodada")
and `docs/modules/core-gameplay-navigation.md` for the action-prompt
key fix specifically.

## Stopping point - 2026-06-21, round 54

Still `feature/inventoryAndGetItens`, not yet committed. Root cause
of the round-53 "item vanishes within 1s, zero other input" bug
found - and it wasn't in our own code at all. The hash-code diagnostic
log confirmed the same literal `Slot` C# object was read both times,
ruling out an array rebuild. Found in `ActionBarUI.Update()` (never
touched by this mod before): it reacts to `GetAnyButtonDown()` and
processes native "ActionBar1".."ActionBar10" actions (the bare 1-0
keys) completely independent of Ctrl/Shift state, calling
`SwapSlotsInput` - which raycasts for whatever `SlotUI` is under the
**mouse cursor** and swaps it into that hotbar slot via
`Slot.GHCDPAJHKOI`. Every Ctrl+1/Shift+1 press was also triggering
this native, mouse-position-based swap on the same frame, undoing or
corrupting our own assignment depending on wherever the mouse cursor
happened to be resting - explaining the "no other key pressed" symptom
(it WAS the same keypress, two independent reactions).

Fixed with a new Harmony patch, `HotbarSwapPatch.cs` (same pattern as
`SpaceClosePatch.cs`): prefix-blocks `ActionBarUI.SwapSlotsInput`
whenever Ctrl or Shift is held. Mouse-driven play never holds those
modifiers while aiming at a slot, so this is safe and narrowly scoped;
plain 1-8/9/0 selection (a different code path,
`ActionBarInventory.SetCurrentSlotSelected`) is untouched.

Not yet confirmed: whether "uso do esfregão não funciona" (tried Q,
F, E, Ctrl+Enter, mouse click - none worked) resolves now that the
item should actually stay in the hotbar. The game's input goes through
Rewired (remappable), so the "Use" action's actual bound key can't be
confirmed from source alone - if this persists after retesting, the
actual key needs separate investigation.

Build clean. See `docs/modules/inventory-and-items.md`, "11ª rodada".

## Stopping point - 2026-06-21, round 53

Still `feature/inventoryAndGetItens`, not yet committed. User did the
isolated retest asked for in round 52 - confirmed via log this is a
THIRD, distinct bug, not a side effect of the previous two: Ctrl+1
assigns the mop to hotbar slot 0 (my own debug log confirms
`assigned "Esfregão" ... to hotbar slot 0`), and under 1.3s later,
with zero other input in between, Shift+1 reads the same slot as
empty. Not the ghost-itemInstance bug (already fixed, would log
differently) and not the stale-selection-read bug (unrelated code
path - this is direct slot access, not GetSelectedItem()).

Didn't attempt another blind fix - added hash-code-based diagnostic
logging (`slotObj`/`containerObj`/`arrayObj`) at both the assign
success point and the return-empty point, to determine whether a
later read sees the literal same `Slot` C# object (would mean
something genuinely cleared it) or a different one (would mean the
array/Slot got rebuilt underneath us - plausible given
`MainPanelUI`'s Inventory tab has already shown other odd behavior:
null `SlotUI.container`, `GameInventoryUI` missing from the open-window
list).

Also noted: "uso do esfregão não funciona" (using the mop) is very
likely a CONSEQUENCE of this same vanishing-item bug, not a 4th
separate issue - a tool that doesn't stay equipped for more than ~1-2s
can't be used. Deferred investigating it separately until the
vanishing-item bug itself is resolved.

Build clean. See `docs/modules/inventory-and-items.md`, "10ª rodada".

## Stopping point - 2026-06-21, round 52

Still `feature/inventoryAndGetItens`, not yet committed. User
deliberately withheld their own read of the symptoms and asked for an
independent log-only validation. Read the full round's log start to
finish; confirmed two real, distinct bugs:

1. **Selection announcement read the wrong slot**: `ActionBarInventory.SetCurrentSlotSelected`
   fires `OnSelectionChanged` BEFORE updating its own internal
   selected-index field - `OnHotbarSelectionChanged` was calling
   `GetSelectedItem()` (which reads that field), so it always reported
   the PREVIOUS slot's content, not the new one. Explains both "balde
   não aparece no uso 2" (announced empty) and "esfregão apareceu no
   uso 3" (announced the old slot's mop). Fixed: index
   `actionBarInventory.slots` directly with the `newIndex` parameter
   the event already provides, never call `GetSelectedItem()`/`GetCurrentSlotSelected()`
   from inside this callback.
2. **Hotbar slot count corrected**: the "8 slots" claim (from
   `ActionBarInventory.Awake`'s `BLMADJJOAKA = new Slot[8]`) was about
   the wrong array - that one's a local-coop mirror for player 2, not
   the real hotbar. Confirmed live the user could select up to "Uso
   rápido 10" with no error. Expanded `HotbarKeys` to 1-9 and 0 (10
   total), with a runtime bounds check against the actual array length
   instead of assuming a fixed count again.

Not yet confirmed: "Shift+1 still says empty after assigning the mop
there" and "using the mop still doesn't work" - root cause not pinned
down; possibly a side effect of scrolling through hotbar selection
(which ran through the same buggy code) between assigning and
retrieving. Asked for a clean, isolated retest (assign then
immediately retrieve, no scrolling in between) to separate this from
bug #1 above.

Build clean. See `docs/modules/inventory-and-items.md`, "9ª rodada".

## Stopping point - 2026-06-21, round 51

Still `feature/inventoryAndGetItens`, not yet committed. The round-50
"ghost itemInstance" fix didn't actually work - log confirmed the
clear attempt ran (`had a ghost itemInstance - clearing it`) but the
item stayed stuck regardless (`couldn't free... still has ""`).
Cause: used `Slot.MEODNPFJDMH()` to clear it, which only removes
exactly 1 unit - since Stack was already 0, decrementing further
changed nothing (Slot's own logic only clears itemInstance on an
actual transition INTO 0, not "was already 0"). Fixed by setting
`hotbarSlot.itemInstance = null` directly instead.

User raised a valid safety concern about this cleanup possibly
wiping a legitimately-configured hotbar slot - confirmed and noted
explicitly: the clear only ever runs when `Stack <= 0`, so a slot the
player actually set up (always `Stack >= 1`) is never touched by it.

Build clean. See `docs/modules/inventory-and-items.md`, "8ª rodada".

## Stopping point - 2026-06-21, round 50

Still `feature/inventoryAndGetItens`, not yet committed. Round-49
fixes confirmed working: seta direita switch, chest<->inventory both
directions, hotbar-selection announcement. New bug found and fixed:
Ctrl+1-8 started saying "Sem espaço" for every hotbar slot, even
empty ones. Root cause confirmed via log
(`couldn't free hotbar slot 0 (freed 0, still has "")`): hotbar slots
held a "ghost" itemInstance (non-null, but Stack 0) - leftover from
an earlier round's now-fixed GHCDPAJHKOI bug, persisting because the
game was never restarted across rounds. `itemInstance != null` alone
isn't a reliable "occupied" check - fixed both
`HandleAssignToHotbar`/`HandleReturnFromHotbar` to also check
`Stack > 0`, clearing the ghost reference outright when found.

Still pending: "limpar a mesa com o esfregão" not retested yet (hotbar
was stuck on the ghost-state bug all of last round).

Build clean. See `docs/modules/inventory-and-items.md`, "7ª rodada".

## Stopping point - 2026-06-21, round 49

Still `feature/inventoryAndGetItens`, not yet committed. Three things
resolved this round, all confirmed via live log before fixing (not
guessed):

1. **Seta direita switch confirmed broken, now fixed**: log showed
   `openWindows=[SmallContainerUI]` while a chest was open -
   `GameInventoryUI` never appears in `MainUI.GetCurrentOpenWindows`
   at all. `KeyboardUINavigator.HandleContainerInventorySwitch` no
   longer requires finding it there - reaches it directly via
   `GameInventoryUI.Get(1)` once a `ContainerUI` (the chest) is found
   in the list. `GetTopWindow()` similarly stopped requiring
   `_manualWindowOverride` to be list-member; only clears it once the
   chest itself closes.
2. **Hotbar assign mismatch ("esfregão no 1, balde no 2, shift
   trouxe o item errado") root-caused**: `Slot.GHCDPAJHKOI`'s
   special case for `singleItem` slots (the hotbar's) only acts when
   the target is already empty - assigning to an occupied hotbar slot
   silently no-ops, but `HandleAssignToHotbar` still announced success
   reading the slot afterward. Replaced with explicit, self-controlled
   steps (free the slot first if occupied, then place the new item),
   each step logging its own success/failure.
3. **New feature, not a bug**: hotbar item *selection* (plain 1-8, a
   native game control this mod never touched) had zero screenreader
   feedback - confirmed `ActionBarInventory.OnSelectionChanged` (public
   event, fires regardless of UI state) and hooked it; selecting a
   hotbar item now announces "{item} selecionado". Hooked
   unconditionally in `Main.UpdateHandlers` (not gated by `anyUiOpen`,
   since this happens out in the world).

Still open: "tentei limpar a mesa com o esfregão, nada aconteceu" -
not yet investigated; asked the user to retry after confirming hotbar
selection announces correctly first.

Build clean. See `docs/modules/inventory-and-items.md`, "6ª rodada".

## Stopping point - 2026-06-21, round 48

Still `feature/inventoryAndGetItens`, not yet committed. Chest ->
inventory confirmed working cleanly. Hotbar assign/return confirmed
working too, but two real gaps found via log:

- "Esfregão no 1, Balde no 2" then Shift+1 said nothing once, then
  pulled the wrong item: root cause was the return message never
  stated which hotbar number it came from, and an empty hotbar slot
  was a silent no-op (no speech, no log) - the "said nothing" the
  user heard. Fixed both: return message now includes the number
  ("Esfregão retirado do uso rápido 1"), and an empty slot now says
  "Uso rápido N vazio" instead of staying silent.
- Plain "1"-"8" (no modifier) not doing anything noticeable: that's
  the game's own native hotbar-select control, not something this mod
  handles - it likely IS selecting the item, just with zero
  screenreader feedback today. Flagged as a future feature
  (announce the currently-equipped item), not a bug in what's already
  built.
- **Still unresolved**: "seta direita doesn't reach the inventory list
  with a chest open." Confirmed via raw key log that RightArrow was
  pressed several times while a chest was open, but
  `HandleContainerInventorySwitch` never logged a switch attempt at
  all (success or failure) - it's exiting via
  `containerWindow == null || inventoryWindow == null` silently.
  Added detailed logging there (lists every open window's type) -
  next test run will show directly whether `GameInventoryUI` is even
  in `MainUI.GetCurrentOpenWindows` when a chest is open, or whether
  (suspected, by analogy to the `SlotUI.container` bug) the inventory
  display next to a chest doesn't actually go through
  `GameInventoryUI.OpenUI()` at all.

Build clean. See `docs/modules/inventory-and-items.md`, "5ª rodada".

## Stopping point - 2026-06-21, round 47

Still `feature/inventoryAndGetItens`, not yet committed. User tested
via `MainPanelUI`'s own "Inventário" tab (no chest open) - found a
real bug: `SlotUI.container` is null for slots shown there, so the
"is this the player's inventory" check always failed, causing
Ctrl+Enter to wrongly say "retirado do baú" while just reshuffling
the item within the same inventory, and Ctrl+1-8 to always refuse.
Fixed by checking the Slot's own identity against
`playerInventory.inventory.slots` instead of the unreliable
`container` field (`IsPlayerInventorySlot` in
`InventoryTransferHandler.cs`). Also added a "Nenhum baú aberto"
message for inventory->chest when no chest is open (expected, matches
the real game - you can't drag onto nothing).

**Also caught and reverted my own mistake from the same round, before
it was tested**: had simplified `GetOpenChestContainer` to
`MainUI.GetCurrentContainer`, wrongly inferring from
`GameInventoryUI.IILKKKEDLLK` that it was the generic "any open
container" lookup. Confirmed by reading every caller of its setters
that it's only ever used by DrinkDispenserUI/Fireplace/OfferingStatueUI,
never by a basic chest - reverted to the round-44
BigContainerUI/SmallContainerUI.IsOpen() check, which was correct.

Build clean. See `docs/modules/inventory-and-items.md`, "4ª rodada"
entry. Still unexplained: hotbar slot 1 already had an item before
any Ctrl+1 succeeded (likely pre-existing from earlier play), and
Shift+1 on it said "Sem espaço" despite empty inventory slots - added
item-name logging to that failure path for next round.

## Stopping point - 2026-06-21, round 46

Still `feature/inventoryAndGetItens`, not yet committed. The round-45
EventSystem fix worked - log confirmed `InventoryTransfer: moved 1/1
of "Esfregão"` and `moved 2/2 of "Balde"` (chest -> inventory). User's
two follow-up points, neither a new bug:

- **"sem feedback"**: it DID speak ("Esfregão" alone, confirmed in the
  speech log), but a bare item name is indistinguishable from normal
  list navigation. Fixed by always stating the direction: "{item}
  retirado do baú" / "colocado no baú" / "retirado do uso rápido".
  Left a `TODO` comment at `MoveStack`'s success point for a custom
  sound next round (via `CustomSounds`, not the game's own audio
  system - 3 prior failed attempts at that, see `CustomSounds.cs`
  header).
- **"Ctrl+1 no esfregão não funcionou"**: expected - the already-
  documented limitation (KeyboardUINavigator only scans the topmost
  window, the chest, never the inventory). Implemented the fix: added
  seta direita/esquerda window-switching in `KeyboardUINavigator.cs`
  (`_manualWindowOverride`, only active when a ContainerUI and a
  GameInventoryUI are both open) so the cursor can now reach inventory
  slots while a chest is open. Also simplified `GetOpenChestContainer`
  to use `MainUI.GetCurrentContainer(playerNum)` (the same lookup the
  game's own auto-transfer code uses) instead of probing
  Big/SmallContainerUI by hand.
- Investigated the game's own built-in "auto transfer" mechanic
  (`SlotUI.autoTransferEnabled`/`DoAutomaticTransfer`,
  `Utils.DKHBBNHMOEB`, `Slot.MJLNPAEBAFF`) to answer the user's "do
  it the way the game does it" ask - confirmed it only moves 1 unit
  per activation, so not directly reusable for moving whole stacks at
  once; only `GetCurrentContainer` was reusable as-is.

Build clean. See `docs/modules/inventory-and-items.md`, "3ª rodada"
entry, for full detail and the next test checklist (6 steps).

## Stopping point - 2026-06-21, round 45

Still `feature/inventoryAndGetItens`, not yet committed. User
reported Ctrl+Enter did nothing (mop/bucket stayed in the chest) and
the inventory list never showed up alongside the chest's.

- **Ctrl+Enter doing nothing - real bug found and fixed**: the new
  `InventoryTransferHandler` read `EventSystem.current.currentSelectedGameObject`
  to find the focused slot - but `KeyboardUINavigator.cs`'s own class
  doc already documents that the game's input module wipes that back
  to null every frame without a gamepad (the exact reason the
  navigator tracks its own virtual cursor instead). Same trap, same
  fix: added `KeyboardUINavigator.GetCurrentSelectedGameObject()` and
  changed the handler to take the GameObject as a parameter from
  `Main.cs` instead of querying EventSystem itself.
- **"Inventory list doesn't appear next to the chest's" - separate,
  unsolved limitation, root cause confirmed**: `KeyboardUINavigator.GetTopWindow()`
  deliberately only scans the most-recently-opened window
  (`MainUI.GetCurrentOpenWindows(1).Last`) - the same defense that
  fixed the old "mixed tabs" bug in the main panel. A chest opens
  `GameInventoryUI` underneath itself and ends up on top, so only the
  chest's slots are reachable right now, not the inventory's. Doesn't
  block testing "chest -> inventory" (no inventory slot needs focus
  for that), but does block "inventory -> chest" and Ctrl+1-8. Flagged
  for a follow-up round (likely an explicit key to switch which window
  the cursor scans, not merging the scan - merging is what caused the
  original bug this defense exists for).

Build clean. See `docs/modules/inventory-and-items.md`, "2ª rodada"
entry, for full detail.

## Stopping point - 2026-06-21, round 44

Still `feature/inventoryAndGetItens`, not yet committed. User
confirmed (live, F12) that navigating a chest's slots with Tab/arrows
already speaks each item's name - the generic SlotUI handling already
covers it, no new code needed there.

Implemented the first version of the 4 transfer actions in new
`InventoryTransferHandler.cs` (instantiated in `Main.cs`, runs only
when a UI is open):
- Ctrl+Enter: moves the focused slot's stack between the player's
  inventory and whichever chest UI is currently open (first free/
  stackable slot on the receiving side).
- Ctrl+1..8 / Shift+1..8: inventory <-> that exact hotbar slot.

Two more things confirmed by reading decompiled source directly
before writing this code (not trusting the earlier research agent's
paraphrase):
- Hotbar is 8 slots, not 9 (`ActionBarInventory.Awake`: `new Slot[8]`)
  - the user themselves wasn't sure. Key 1 = index 0 ... key 8 = index 7.
- `Container.AddItemInstance` only places ONE unit per call (confirmed
  reading `Utils.CHMEHDFPGCI` to the end) - `AddItemInstances` (plural)
  just loops it without reporting how many actually fit, so the new
  handler does its own loop (`MoveStack`) to track exactly how many
  units moved and shrink the source slot by that amount, not assume
  the whole stack went through.

Build clean. NOT yet tested live. See
`docs/modules/inventory-and-items.md` for full details and known risk
areas for the next test round (non-stackable items, aged-food
equality edge cases, full-container behavior, none confirmed yet).

## Stopping point - 2026-06-21, round 43

User confirmed round 42's fixes work ("agora está melhor"). Committed
all of `feature/worldNavigation`'s work (one commit), pushed, merged
into `main` (fast-forward, no conflicts), pushed `main`. Created and
pushed new branch `feature/inventoryAndGetItens` for the next feature:
letting a keyboard-only player move items between a chest, their
inventory, and the 1-9 hotbar.

This round was research only (project convention: research/plan
before implementing a new feature). Spawned an Explore agent to map
the inventory/container/item code in `decompiled/`; cross-checked its
key claims myself before trusting them (per the project's standing
rule) and caught a real mistake: the agent assumed "the baú" is
`TreasureChest.cs` - confirmed by reading the file that's wrong, it's
a one-time treasure dig spot with no `Container`/`Slot[]` at all. The
real chest class is `ItemContainer : Container` (confirmed it inherits
`Container` directly). Also caught the agent inventing a method name
(`CommonReferences.GGFJGHHHEJC`) that doesn't exist - the real one,
confirmed by reading `SlotUI.cs` (already used in our own
`KeyboardUINavigator.cs`), is `CommonReferences.MNFMOEKMJKN()`.

Confirmed directly (not just agent-reported): `Container.AddItemInstance`
finds the first free/stackable slot (delegates to `Utils.CHMEHDFPGCI`);
`Slot.GHCDPAJHKOI`/`NFBAGDKBOAD`/`MJLNPAEBAFF` are the real swap/move/
merge methods; `SlotUI.IHENCGDNPBL` (already relied on by
`KeyboardUINavigator.DescribeSlotUI`) is the slot's `Slot` reference -
meaning hearing a chest's slot contents via Tab/arrows likely already
works today, with no new code, since `SlotUI` handling is generic, not
tied to the main panel inventory tab specifically.

Full findings and the still-open questions (ItemContainer's UI-opening
mechanism, which of ContainerUI/BigContainerUI/SmallContainerUI is the
chest's actual UI, ActionBarInventory's real slot count) are in the
new `docs/modules/inventory-and-items.md`. Nothing implemented yet -
next round: read `ItemContainer.cs` and the container UI classes in
full, then live-test (F12) opening a real chest to confirm slot
reading already works before wiring up the 4 transfer key bindings.

## Stopping point - 2026-06-21, round 42

- **Real cause found via the diagnostic log added last round**:
  `freeNodesOnOpen` was length 0 (not null) for both doors tested -
  the old `== null` guard let it through into an empty `foreach` that
  could never find anything, for any door in this configuration.
  `GetDoorWalkablePosition` (already used for routing to a door)
  already had the right fallback for this - falls back to the door's
  own transform position when there are no free nodes. Copied that
  fallback into the new `GetClosedDoorBlockDistance` (refactored from
  the old bool-returning `IsClosedDoorBlocking`, which now wraps it).
- **"Bumped a wall near a door, got the item sound" - real bug,
  fixed**: the door check didn't compare against how close the wall
  actually was - if a door's threshold was merely in range, it won
  even when a much closer wall was the real obstacle. `IsBlockedByNonWallItem`
  now computes both the physics-wall distance and the door distance
  and picks whichever is actually closer.
- **Item sound radius**: 4 -> 3 tiles.
- **Per-item volume**: cama +25%, mesa -40%, via a new
  `_itemVolumeMultipliers` dict in `CustomSounds.PlayItemNearby` (on
  top of the shared 60% base volume, untouched for everything else).

Build clean. See `docs/modules/world-object-navigation.md` 33rd round
entry.

## Stopping point - 2026-06-21, round 41
  `DebugLogger.LogState` (and siblings) start with
  `if (!Main.DebugMode) return;` - they don't log unconditionally,
  contrary to what several past rounds' comments assumed. Round 28's
  "sound loading is broken" conclusion was wrong - the log just had no
  lines because F12 came on a few seconds after "Game ready" already
  fired and loading already ran. Saved to memory
  (`feedback_debuglogger_gated_by_debugmode`) so future debugging
  checks debug-mode-on timing before trusting an absent log line.
- **Main hall door still not triggering all 3 directions**: round
  40's freeNodesOnOpen fix didn't fully resolve this specific door.
  Root cause not yet confirmed - added detailed diagnostic logging
  (node position, distance, dot-product alignment per direction) to
  `IsClosedDoorBlocking` instead of guessing again blindly.
- **Item sound radius**: 6 -> 4 tiles (user reported hearing hall
  tables from the bedroom).
- **Real bug found in item-bump sound**: `IsBlockedByNonWallItem` only
  used Physics2D, so a closed door (no collider, same as the ambient
  sound's earlier bug) could never classify as "item" - bumping a
  closed door fell through to the wall-bump sound by default,
  contradicting the user's explicit request that doors count as
  "item." Fixed by reusing `IsClosedDoorBlocking` here too. Added
  diagnostic logging to both the tap and sustained bump paths in case
  something else is still wrong.

Build clean. See `docs/modules/world-object-navigation.md` 32nd round
entry.

## Stopping point - 2026-06-21, round 40

- **Closed-door ambient sound, real fix this time**: round 39's
  Door-component exception never actually worked - confirmed via log
  (standing 0.3 units from a closed door for several seconds, that
  direction's raycast stayed "nada" throughout). Re-checked decompiled
  Door.PJMBLECKFLH (the open/close handler) - it only toggles
  WorldGrid walkability nodes, no Collider2D is touched at all. A
  closed door has no physics collider at its blocked threshold for
  Physics2D to ever find, so the raycast approach could never have
  worked here regardless of filtering logic. Added
  `IsClosedDoorBlocking()` - checks the door's own `freeNodesOnOpen`
  positions directly (the same data already used for path/approach
  positions) instead of relying on physics.
- **Corridor "only the side I'm facing" report**: investigated but
  not confirmed - the log segment available showed one side
  consistently close and the other consistently empty for several
  seconds, but no proof that spot was actually a 1-tile corridor
  (could just be open space on that side). Left as open question,
  asked the user to report a specific no-door location if it recurs.
  Door.open itself is `protected` (compile error caught this) - used
  the public property `Door.ECMGCJGPKNO` (decompiled name) instead.
- **Item proximity sound redesigned into a continuous loop**: was a
  one-shot tied to the action-prompt UI text. Now: every 1s, any
  Placeable within 6 tiles with a matching named clip
  (`CustomSounds.HasItemClip`) plays its sound (pitch for vertical
  direction, pan for horizontal), staggered 0.3s apart per cycle via a
  coroutine when multiple matches are close together (explicit
  request - simultaneous playback was confusing). Old action-prompt
  one-shot left untouched, this is additive.

Build clean. See `docs/modules/world-object-navigation.md` 31st round
entry.

## Stopping point - 2026-06-21, round 39

- **Door-in-corridor regression fixed**: round 38's "(Clone)" blocklist
  (added to stop furniture triggering the ambient wall sound) also
  excluded doors, since doors are Clone-named too - closed doors
  stopped triggering the directional wall sound even though they
  block movement exactly like a wall. Added an exception: a hit with
  a `Door` component still counts as a wall for this specific ambient
  sound, even if Clone-named. Plain furniture stays excluded.
- **Per-item proximity sound**: user provided `baú.wav`/`cama.wav`/
  `mesa.wav`/`torneira.wav`. `CustomSounds.PlayItemNearby` now looks
  up a matching clip by item name (substring match) instead of always
  playing the generic `itens.wav`, and encodes direction via pitch
  (vertical-dominant: higher = ahead/cima, lower = behind/baixo) or
  pan (horizontal-dominant: normal pitch, panned left/right) - added
  `WorldNavigationHandler.GetNearestInteractionTarget()`/
  `GetNearestInteractionAudioInfo()` to get the interactable's
  position (previous `GetNearestInteractionName()` only had the name).
- **Distinct "bumping into a non-wall item" sound**: user provided
  `batendo em item.wav`. Added `IsBlockedByNonWallItem()` (same
  "(Clone)" signal as the ambient sound, but WITHOUT the door
  exception - per the user's explicit request, doors count as "item"
  here, not wall) - both the single-tap and sustained-stuck bump
  handlers now pick wall-bump vs item-bump sound based on what's
  actually blocking the attempted movement direction.

Build clean, all new files (including accented "baú.wav" and spaced
"batendo em item.wav") confirmed copying to Mods. See
`docs/modules/world-object-navigation.md` 30th round entry.

## Stopping point - 2026-06-21, round 38
diagnostic logs added last round) that loading itself is fine, 7
clips load every session including this one.

Found the real cause: log showed 172 directional wall checks this
session, zero hits - even near "WallBack", which used to fire
correctly. Round 27's fix (require a `PhysicalSpaceWall` component to
count as a wall, meant to stop the bed from triggering it) excluded
real walls too, not just furniture - a wrong assumption, caught by the
same project habit of checking the log before trusting a fix.
Switched to the opposite approach: blocklist instead of allowlist -
exclude any hit whose root name contains "(Clone)" (Unity's suffix for
runtime-`Instantiate()`'d objects, which furniture/decorations are;
static level geometry like walls isn't instantiated, so it never has
this suffix - confirmed against both the bed and "WallBack" in the
log).

Build clean. See `docs/modules/world-object-navigation.md` 29th round
entry.

## Stopping point - 2026-06-21, round 37
this round.

Investigated via log BEFORE changing anything (project rule). Found:
this session's log has zero "CustomSounds:" lines at all - not even
"loaded parede.wav", which logged in every prior session, including
ones from before the volume change. This means sound loading itself
stopped happening - the 60% volume value isn't the cause (that
wouldn't go silent, just quieter). Root cause not yet known. Added
unconditional diagnostic logs at `CustomSounds.EnsureLoaded` and the
start of the `LoadAll` coroutine, plus a try/catch around the call in
`Main.cs`, to pin down exactly where the chain breaks next session -
deliberately did NOT touch the volume value or guess at a fix without
evidence, per the user's own request for caution with small audio
tweaks plus the standing project rule (confirm via log before
concluding a cause).

Build clean. See `docs/modules/world-object-navigation.md` 28th round
entry.

## Stopping point - 2026-06-21, round 36

- **Directional wall sound false positive fixed**: log showed "cima"
  was hitting the bed (`1130 - Cama del Jugador(Clone)`, dist=0.28),
  not a wall - the raycast counted any solid collider, including
  furniture. Found a dedicated `PhysicalSpaceWall` component the game
  itself uses (for camera-occlusion wall fading) and now only count
  hits that have it.
- **"Sem rota ainda" fallback fixed to single-axis**: was joining both
  axes into one sentence ("4 pra esquerda, 3 pra baixo") - now shows
  only the larger axis, matching the real step system's one-at-a-time
  format. Also found (while fixing this) it never divided by TileSize
  - was speaking double the real telha count. Fixed both.
- **`maxNodes` raised 1500 -> 2500**: log proved the 1500 cut (made a
  few rounds ago for speed) was now causing genuine "no route"
  failures for longer distances - "Bar" failed repeatedly while far
  away (even with the new retry) and only succeeded once the player
  walked close. Not a blocked path, a search budget that was too
  small.
- **Wall sound volume -> 60%** and **4 distinct wall clips** (user
  provided `cima.wav`/`direita.wav` to go with existing
  `baixo.wav`/`esquerda.wav`, replacing the old shared file) per
  direct request.

Build clean. See `docs/modules/world-object-navigation.md` 27th round
entry.

## Stopping point - 2026-06-21, round 35
added a pathfinding retry so blocked destinations (closed doors) still
get step-by-step guidance instead of falling back to the old
straight-line message.

- **4 distinct wall clips**: `cima.wav`/`direita.wav` are new (user
  provided this round); `baixo.wav`/`esquerda.wav` already existed.
  Replaced the old shared "cima direita e esquerda.wav" file entirely
  - `CustomSounds` now has one clip field per direction, `.csproj`
  copy list updated to match.
- **Pathfinding retry for blocked destinations**: user insisted
  guidance should always come in steps, even toward something
  currently blocked (e.g. a closed door, whose threshold tile the
  game only marks walkable while open - confirmed several rounds
  ago). `OnPathComputed` now retries once on failure, aiming at a
  point nudged one tile back toward the player along the same line -
  since usually only the very last tile is what's blocked, this
  should land on an already-walkable node and give real step
  guidance up to (not through) the obstruction. Falls back to the
  honest "Não encontrei uma rota até lá." only if the retry also
  fails.

Build clean. See `docs/modules/world-object-navigation.md` 26th round
entry.

## Stopping point - 2026-06-21, round 34
attempt, and added a small grace period for audible pauses.

- **Wall sound detection rewritten**: the single fixed-distance
  OverlapCircle point missed walls closer than that distance (a
  narrow 1-tile corridor's walls sit much closer than a full tile),
  and a plain `Raycast` only reports the nearest hit - very likely the
  player's OWN collider, since the ray starts at the player's
  position, hiding any real wall beyond it (plausible explanation for
  up/down never firing, depending on the player collider's shape).
  Switched to `Physics2D.RaycastAll` along the whole direction up to
  ~1.2 tiles, picking the closest hit that isn't the player and isn't
  a trigger - covers both very-close (corridor) and one-tile-away
  (open room) walls.
- **Grace period added**: `WallSoundOffDelay` (0.15s) - a direction
  keeps playing for a brief window after its last positive detection,
  to smooth over single-frame detection flicker (a plausible cause of
  the reported audible "pauses"). If pauses persist after this, it's
  more likely the WAV file itself has a small silence baked into its
  loop point - not something fixable in code, would need a re-trimmed
  file.
- Confirmed (no new code) that "non-door objects not getting step
  guidance" is the same already-explained limitation from last round
  (only happens when pathfinding genuinely fails for that specific
  destination) - asked the user to name a specific object next time if
  they're sure it has a free path.

Build clean. See `docs/modules/world-object-navigation.md` 25th round
entry.

## Stopping point - 2026-06-21, round 33
the directional wall-proximity sound (experimental, needs live
testing), and added a diagnostic for the one case that looked like a
real bug ("Torneira" failing pathfinding without being behind a door).

- **"Escadaria"/closed targets confirmed not a list bug**: log shows
  it DOES appear in the list and gets guidance attempts - pathfinding
  itself just consistently returns no route, matching the user's own
  theory (still locked/blocked, same mechanism as a closed door's
  threshold tile not being walkable yet).
- **"Two directions instead of steps" explained**: only happens when
  `PathRequestManager` genuinely returns no route - confirmed in log
  this is 1:1 with destinations that have no walkable path at all
  right now. Not fixable without a route existing; can't fabricate
  steps for a path that doesn't exist.
- **`GetApproachPosition` diagnostic logging added**: to find out if
  "Torneira" (not door-gated) failing pathfinding is the same kind of
  bug as the earlier barrel issue (approach point landing somewhere
  unwalkable) - logs the computed point for every Placeable target.
- **Directional wall sound - phase 1 implemented (experimental)**:
  using the user-provided `baixo.wav` (down) and `cima direita e
  esquerda.wav` (up/left/right, panned/centered) - checks all 4
  cardinal directions every frame via `Physics2D.OverlapCircle`
  (Unity's own physics, not a guessed game-internal function -
  confirmed `Utils.EJPFCKFEMJF`, the pathfinding `avoidWalls` check,
  only tests `position.y > 800f` and would have been another wrong
  guess) and loops the matching clip per direction, multiple at once
  (e.g. a corner). New `CustomSounds.SetDirectionalWallSound`/
  `StopAllDirectionalWallSounds`. Genuinely untested against the
  game's real collider setup - flagged for live testing before
  trusting it. Phase 2 (door proximity loop) not started yet.

Build clean. See `docs/modules/world-object-navigation.md` 24th round
entry.

## Stopping point - 2026-06-21, round 32
outside the tavern building no longer show until the player leaves
it), and analyzed (not yet implemented - no sound files provided yet)
a directional wall/door proximity sound feature.

- **Location filter added** to `BuildTargetList()`: items whose
  `Utils.HJPCBBGHPDA` Location differs from the player's current one
  are removed from the list. Confirmed the cellar shares the tavern's
  Location (never triggered the existing cross-area fallback message
  in past logs), so closed-but-reachable items like the cellar door
  still show, while genuinely outside-the-building items don't until
  the player actually leaves. Does NOT pathfind-check every item
  (too expensive to run per-candidate) - a truly "no path exists at
  all" filter is still not implemented.
- **Sound design analysis (no code yet)**: user wants a directional
  wall-proximity loop (pan left/right, pitch up/down) plus a door
  proximity loop, to give an "echolocation"-like sense of room shape -
  gaps in the wall sound double as corridor/opening signals, so no
  separate corridor-detection system is needed (matches the user's
  own observation). Investigated `Utils.EJPFCKFEMJF` (used by
  pathfinding's `avoidWalls`) as a possible "is this a wall" check -
  ruled out, it only checks `position.y > 800f`, unrelated to
  collision (same class of wrong-guess as last round's IProximity
  mistake). Recommended approach instead: Unity's own
  `Physics2D.Raycast` in the 4 cardinal directions, independent of
  guessing the right internal game function - same principle as the
  already-working reactive wall-bump sound (measures real movement,
  not an internal flag). Waiting on the user to provide the actual
  sound files before implementing.

Build clean. See `docs/modules/world-object-navigation.md` 23rd round
entry.

## Stopping point - 2026-06-21, round 31
and loosened the step-advance threshold back, since the tighter value
caused a worse regression (permanent oscillation on a short chest leg).

- **IProximity revert**: user reported "Você chegou" firing instantly
  for the door, confirmed in log (~1.7s after "Calculando rota...",
  nowhere near the target). Read `Placeable.IsAvailableByProximity`
  and `Door.IsAvailableByProximity` in decompiled source - despite the
  name, neither checks player distance at all (pickup eligibility,
  decoration-mode state, rental-zone occupancy instead). Fully
  reverted to the geometric distance-based arrival check; removed the
  now-unused `GameObject source` field from the target tuple and
  `_selectedTarget` (was only added for this).
- **Step-advance threshold loosened back**: the chest leg from last
  round's log was still stuck oscillating "1 pra direita"/"1 pra
  esquerda" for over a minute - real distance was hovering between
  0.17 and 0.49, frequently above the 0.15 threshold tightened 2
  rounds ago. Reverted to 0.25 (matches the rounding boundary used for
  the spoken count itself).
- Asked the user whether to invest in the bigger checkpoint/fixed-
  coordinate redesign now, given the back-and-forth on short-leg
  precision - question was declined/interrupted; holding off, no
  redesign started.

Build clean. See `docs/modules/world-object-navigation.md` 22nd round
entry.

## Stopping point - 2026-06-21, round 30
event (per user suggestion), added a per-step tap-vs-announced-count
log, fixed a real "0 pra baixo" edge case, and shortened the wall-tap
sound again.

- **Re-audit**: re-read the WorldGrid step size, Door.freeNodesOnOpen,
  and axis-count logic - all still check out. The actual weak point is
  `GetApproachPosition` (last round's geometric guess for
  non-door objects) - an approximation, unlike the door's exact data.
- **"Chegou" now event-based, not distance-based**: confirmed nearly
  every interactable type in this game (`Placeable`, `Door`,
  `FloorDirt`) implements `IProximity.IsAvailableByProximity(int)` -
  the same authoritative signal the game itself uses to decide whether
  to show the "[E]/[Q] ..." prompt. `BuildTargetList()`'s tuple grew a
  `GameObject source` field (and `_selectedTarget` correspondingly) so
  `BuildStepGuidanceMessage` can ask the target directly "can you be
  interacted with right now?" before falling back to geometry. Should
  fix the door-works-chest-doesn't inconsistency, since the chest never
  had door-quality exact position data to lean on.
- **Tap-vs-count diagnostic log added**: per user's explicit request,
  every time a step completes, logs "número pedido=X, toques de
  movimento usados=Y" - lets calibration be checked from hard numbers.
- **"0 pra baixo" fixed**: the stricter `StepAdvanceThreshold` from
  last round left a gap where raw distance already rounds the spoken
  count to 0 but hadn't crossed the (smaller) advance threshold yet -
  confirmed plausible and matches the report exactly. Spoken count now
  floors at 1, never 0.
- **Wall-tap sound**: 0.2s -> 0.26s per request.
- Still holding off on a checkpoint/fixed-coordinate redesign - testing
  whether the proximity-event fix resolves the chest-specific
  inconsistency first.

Build clean. See `docs/modules/world-object-navigation.md` 21st round
entry.

## Stopping point - 2026-06-21, round 29
arrival), reduced the A* search budget to speed up route calculation,
and shortened the wall-tap sound again.

- **Step advanced too early - fixed**: user reported moving just a
  little and already getting redirected. Cause: the advance-to-next-
  step check reused the already-ROUNDED spoken count (`<= 0`), so
  anything within a quarter tile counted as "done" early. Decoupled:
  added `ComputeAxisDistance` (raw, unrounded) and a tighter
  `StepAdvanceThreshold` (0.15, vs. the up-to-0.25 slack rounding
  allowed) used only for deciding when to switch steps - the spoken
  number still rounds normally.
- **Chest arrival needed multiple tries - addressed**: `GetApproachPosition`
  (added last round) pushed the target a half tile past the collider
  edge - farther out than where the player actually stops to interact.
  Reduced the buffer to a quarter tile.
- **Route calculation speed**: confirmed in decompiled `PathRequestManager`
  the 1+ second wait isn't queue overhead (background loop polls every
  1ms) - it's genuine A* search time, bounded by `maxNodes`. Halved
  from 3000 to 1500. Trade-off: faster for normal in-tavern distances,
  but a long/complex enough route could now fail where it didn't
  before - flagged for the user to watch for in this round's testing.
- **Wall-tap sound**: 0.3s -> 0.2s per request.

Build clean. See `docs/modules/world-object-navigation.md` 20th round
entry.

## Stopping point - 2026-06-21, round 28
distance kept growing" (stale per-chunk direction label, never
recomputed from current position), generalized the door
walkable-position fix to ordinary objects (barrel was unreachable -
registered position was inside its own collider), removed the useless
"Continuando..." message, and shortened the wall-tap sound again.

- **Tile count confirmed correct (not a bug)**: used the exact
  player/endpoint positions added to the log last round to hand-verify
  several "X pra baixo/esquerda" lines - all matched 0.5 world units =
  1 tile exactly. The user's "conte direito as telhas" complaint
  turned out to be a symptom of the direction bug below, not the count
  itself.
- **Stale direction bug found and fixed**: `SimplifyPath` assigns each
  chunk's direction ONCE, from the path's original traversal. If the
  player overshoots a chunk's end point (ends up on the far side), the
  distance correctly grows as they keep moving the "wrong" way, but
  the spoken direction stayed frozen at the original label - so it
  kept saying "direita" while the real fix was to go back ("esquerda")
  - confirmed exactly in log (pos.x growing past end.x while still
  announcing "direita"). Added `GetLiveDirection()`, which recomputes
  the spoken direction from the player's current position every time,
  never trusting the precomputed label.
- **Generic objects with unreachable target position - fixed**: log
  showed a barrel target NEVER found a route (over a minute of
  "Pathfinding returned no route") - registered position was inside
  the object's own solid footprint, same class of bug as last round's
  door fix but for ordinary `Placeable`s. `GetApproachPosition()`
  nudges the target to just outside the object's `Collider2D` bounds,
  toward the player, instead of using the raw transform position.
  Needed a new `UnityEngine.Physics2DModule` reference in the .csproj
  for `Collider2D`.
- **"Continuando..." removed**: user said it told them nothing
  actionable. Steps now advance immediately once their own axis is
  satisfied (using the same axis-only measure as the spoken count),
  and the final step now resolves whichever axis (or both) still has
  distance left instead of stalling.
- **Wall-tap sound shortened again**: 0.5s -> 0.3s per request.
- Declined (for now) the suggestion to redesign navigation around
  fixed checkpoints/area coordinates - the two bugs found this round
  plausibly explain most of the reported inconsistency; recommended
  testing this fix first before a bigger rework.

Build clean. See `docs/modules/world-object-navigation.md` 19th round
entry.

## Stopping point - 2026-06-21, round 27
wall-tap sound duration, made guidance auto-disable on arrival, and
confirmed (not guessed) that closed doors legitimately block
pathfinding by design - not a bug.

- **"Two routes" bug - confirmed and fixed via log**: log showed the
  exact timing - "Calculando rota..." spoken, then 8ms later "9 pra
  baixo, 2 pra esquerda" spoken over it. `HandleGuidanceUpdate` runs
  every frame once guidance is active, and its first call always
  passes the movement-delta check (no prior position to compare yet),
  landing on the old straight-line fallback message BEFORE the real
  route (~1.2s round-trip) came back. Fixed: that update now stays
  silent while `_isInitialPathRequest` is true; `OnPathComputed`
  already announces the real first step once ready.
- **Chest "Vazio" - confirmed via log, fixed for real this time**:
  the diagnostic added two rounds ago proved the item WAS found
  (`item=itemMop`) but `LocalisationSystem.Get(item.nameId)` came back
  empty anyway. Root cause in decompiled `Item.cs`: some items use
  `translationByID`, which looks up a different key
  (`"Items/item_name_" + id`), not `nameId` directly - `Item` itself
  has a public method (`IABAKHPEOAF()`) that already knows this and
  falls back to the raw asset name if no translation exists at all.
  Swapped both lookup sites (`DescribeSlotUI` in
  `KeyboardUINavigator.cs`, `DescribePlaceable`'s itemSetup lookup in
  `WorldNavigationHandler.cs`) to call that instead of looking up
  nameId directly.
- **Wall-tap sound shortened**: user reported it playing "too much"
  when testing rapid taps against a wall - each tap played the full
  1s minimum, overlapping. Halved to 0.5s.
- **Guidance auto-disables on arrival**: saying "Você chegou" now also
  turns guidance off, instead of needing a separate Home press.
- **Closed doors blocking pathfinding - confirmed by design, not a
  bug**: found in decompiled `Door.cs` that a door's walkable
  threshold tile(s) (`freeNodesOnOpen`) only become walkable in the
  grid while the door is OPEN - closed doors genuinely block the A*
  search, same rule the game's own NPCs follow. Matches the user's own
  suspicion about the cellar door. "Não encontrei uma rota até lá." is
  confirmed (via log) to already speak in this case - never silent.
- **Tile-count calibration still open**: user couldn't confirm whether
  announced counts are double, half, or something else vs. real
  distance. Added exact player/endpoint world positions to the
  per-announcement debug log so the next round's log can be measured
  directly instead of guessed.

Build clean. See `docs/modules/world-object-navigation.md` 18th round
entry.

## Stopping point - 2026-06-21, round 26
distance, fixed doors using their real walkable tile instead of the
bare transform position, and added a fast off-track reroute.

- **Single-tap wall sound - real bug found**: the old design only
  evaluated a tap on a frame where NO movement key was held - rapid
  tapping almost never produces such a frame, so that check almost
  never ran. What the user actually heard was the unrelated sustained
  loop, accumulating stuck-time across the gaps between taps until it
  crossed 0.6s (hence "needs 6+ taps"). Fixed: each key-down now
  schedules its own independent check 0.15s later, regardless of
  hold/release state.
- **Wall sound minimum duration**: `PlayWallBumpOnce` now loops the
  clip and cuts it at a fixed 1s minimum, since the wav itself is
  shorter than what the user wants to hear.
- **Guidance activation flow fixed**: confirmed the bug - turning
  guidance on spoke the OLD straight-line fallback message first
  (premature, before the real route existed), then the real route a
  moment later. Now says "Calculando rota..." immediately and "Rota
  calculada. [first step]" once the route actually arrives - and only
  on activation, not on every background refresh while walking.
- **Step counts now axis-only**: each step's remaining count measures
  only the axis its direction refers to (e.g. a "cima" step only
  looks at vertical distance), instead of straight-line distance to
  its endpoint - sideways drift no longer skews an unrelated count.
- **Doors use their real walkable tile**: found `Door.freeNodesOnOpen`
  (public `Vector2[]`) in decompiled source - per-instance offsets
  marking the door's actual walkable threshold tile(s), separate from
  the door's bare `transform.position` (which isn't necessarily
  walkable at all). `GetDoorWalkablePosition()` now uses whichever
  offset is closest to the player. Likely the real cause of "says
  still up while standing on the door."
- **Faster off-track correction**: `HandleGuidanceUpdate` now also
  forces an immediate reroute (bypassing the 6s cooldown) once
  perpendicular drift from the current step's line gets too large,
  instead of waiting for the next periodic refresh.

Build clean. See `docs/modules/world-object-navigation.md` 17th round
entry.

## Stopping point - 2026-06-21, round 25
(fixes the jittery/increasing numbers), added diagnostics for the two
unconfirmed issues (chest "Vazio", single-tap wall sound), and answered
the camera/rotation question directly from decompiled source.

- **Bark filter bug found**: round 23 removed the `continue` that
  muted ambient barks (to fully open the filter hunting for the cat's
  line); round 24 only narrowed the name list back down without
  restoring that `continue` - so narrowing the list did nothing
  (confirmed in log: BuzzNPC/DoorNPC kept getting announced). Fixed -
  the skip is back, scoped to "BuzzNPC"/"DoorNPC"/"Mudanza" only.
- **Guidance redesigned into step-chunks**: confirmed in log that
  round 24's grid-snap fix DID work (`path=True`, "Pathfinding
  succeeded" appearing regularly), but announced numbers jumped
  around/increased - caused by re-requesting a fresh route every 2s
  (resetting progress each time) combined with a very granular raw
  path (one point per 0.25 units). `SimplifyPath()` now collapses the
  raw `Vector2[]` into cardinal-direction chunks; `AnnounceFullPlan()`
  speaks the whole plan once when a route arrives ("Rota: 4 pra cima,
  3 pra direita, 2 pra baixo"), then `AnnounceDirectionToSelectedTarget()`
  just counts down within the current chunk. Re-request cooldown raised
  from 2s to 6s (safety-net refresh only, not constant).
- **Diagnostics added** (no behavior change) for two issues we lack
  hard evidence for: `DescribeSlotUI` now logs the slot/itemInstance/
  item lookup chain (to confirm whether a "Vazio" chest is real or a
  bug), and `HandleSingleTapWallBump` now logs held-duration/moved-
  distance vs threshold (to confirm whether single-tap detection or
  key choice - WASD vs arrows - is the actual issue).
- **Camera/rotation question answered**: confirmed via the `Direction`
  enum used throughout the game's movement/animation system - fixed
  top-down view, character only ever faces 4 cardinal directions, no
  free mouse-rotation exists. No "align with north" need.

Build clean. See `docs/modules/world-object-navigation.md` 16th round
entry.

## Stopping point - 2026-06-21, round 24
text, single-tap wall sound, and narrowed the bark filter back down.

- **Pathfinding root cause found**: confirmed via log that round 23's
  integration NEVER succeeded a single request (`path=False` always,
  even 3-tile distances) - silently falling back the whole time.
  Cause: the game's A* works with positions snapped to a 0.25-unit
  grid (`Utils.MJEACANINDN`); passing raw player/target floats meant
  the algorithm's goal-equality check never matched, so it always
  exhausted its search. Fixed by snapping both start/goal through that
  same function before requesting. Needs a real test to confirm.
- **Reverted**: chest contents on Page Up/Down (user prefers the
  game's own container UI, which already opens as a list correctly).
- **Fixed the REAL container UI instead**: confirmed the actual bug -
  `KeyboardUINavigator` was reading slot buttons' generic GameObject
  name ("New SlotUI Inventory") instead of the contained item, with
  one slot reading nothing. Added a `SlotUI`-aware label reader
  (`SlotUI.IHENCGDNPBL` exposes the underlying `Slot`/`ItemInstance`
  publicly) - reads the real item name or "Vazio".
- **Wall-bump single-tap**: a quick tap into a wall produced no sound
  (only sustained holding did). Added independent raw-WASD-key
  down/release tracking so a short tap with near-zero displacement
  also gets a one-shot sound; raised the sustained-hold confirmation
  delay by ~100ms (0.5s -> 0.6s) per request.
- **Bark filter narrowed back down**: opening the filter fully last
  round didn't surface the cat's line either (confirmed in log - the
  "Mudanza" family appeared once unfiltered, the cat never did, in
  either state) - re-filtered only "BuzzNPC"/"DoorNPC"/"Mudanza"
  specifically, leaving all other NPC barks audible. Cat dialogue
  remains unexplained - not Bark UI, not a dialogue response, not
  scanned UI/world text either. Asked the user for the exact trigger
  (interacting? walking by? a specific story beat?) to narrow the
  search instead of guessing further blindly.

Build clean. See `docs/modules/world-object-navigation.md` 15th round
entry.

## Stopping point - 2026-06-21, round 23
quest tagging, and ambient-bark filter temporarily disabled.

- **Real pathfinding wired up**: `PathRequestManager.RequestPath`
  (the game's own threaded A*, already used for NPCs) is now called
  from `HandleHomeKey`/`HandleGuidanceUpdate`, wrapped in try/catch
  (confirmed via decompiled source the only likely failure - the
  internal static instance not being ready - throws synchronously on
  our calling thread, not the background one, so it's safely
  catchable). Confirmed the callback fires on the main thread via
  `PathRequestManager.Update()` draining its own result queue - safe
  to touch our state there. Guidance now walks the returned waypoint
  list instead of a straight-line delta; falls back to the prior
  "different area, can't tell you a number" honesty message only
  when no path is available yet.
- **Duplicate "door" entries fixed**: confirmed same physical door
  was appearing twice (once via `Door`, once via `Placeable` with a
  "Cellar Door"/"Puerta" name) at near-identical coordinates. Dedup
  pass added to `BuildTargetList()`: same category + within half a
  tile of an already-kept entry is dropped.
- **Container contents now announced**: confirmed `Container.slots`/
  `Slot.itemInstance` are public in decompiled source - selecting a
  Container-category target now reads its name AND current contents
  (or "vazio").
  Helps the user actually find which container has a quest item.
- **Dirty tables tagged "Missão" too**: confirmed via decompiled
  `Table.cs` (separate component from `Placeable`, with a public dirt
  level property) - not just floor stains anymore.
- **Ambient NPC bark filter disabled (temporarily, by request)**: the
  cat's missing final line never showed up even in last round's
  filtered-but-logged diagnostic - user asked to remove the filter
  entirely to actually hear everything. Re-enabled the "Conversa ao
  redor:" prefixed announcement path that already existed but was
  short-circuited. Expect noisier ambient chatter again until this is
  revisited.
- **Explained, not a bug**: the unexpected ambient dialogue lines
  (BuzzNPC/DoorNPC under a "Mudanza" object) are a scripted
  moving-family event somewhere in the loaded scene, not visible to
  the player - our scanner reads the whole scene's text, not just
  what's near the character.

Build clean. See `docs/modules/world-object-navigation.md` 14th round
entry.

## Stopping point - 2026-06-20, round 22

- **New "Missão" category**: floor stains (`FloorDirt` - confirmed a
  different component type from `Placeable`, that's why they were
  missing) now show up there. Only floor dirt covered so far, not a
  generic "whatever the active quest needs" mapping for future quests.
- **"Grifo" mistranslation fixed**: confirmed via log
  ("DrinkDispenserUI"/"ContentBeerTap") it's a drink dispenser tap,
  not a sink faucet - changed to "Dispenser de Bebidas".
- **Direction ordering**: now says the axis with the LARGER distance
  first (e.g. "3 baixo, 1 direita"), per explicit request - order
  only, doesn't add real wall-avoidance (separate larger ask, still
  not implemented).
- **Cat dialogue / table message**: checked this session's log,
  found zero trace of the cat's line (filtered or announced) - likely
  wasn't re-triggered in this test, not a filter regression. Confirmed
  the table's "[Q] Limpar" prompt WAS read - if the user means a
  different message (e.g. after finishing cleaning), need to know
  exactly when it appears to investigate further.

Build clean. See `docs/modules/world-object-navigation.md` 13th round
entry.

## Stopping point - 2026-06-20, round 21

- **"Decorativos" category fix**: doors/stairs without a `Door`
  component (`Puerta`, `Cellar Door`, `Escalera Arriba` - Placeable,
  not Door, which is why they landed there) reclassified into
  "Portas". "Decorativos" now means purely cosmetic (window, vase)
  per user's correction.
- **Table-cleaning message not read**: text scan was
  `TextMeshProUGUI`-only (UI text). Widened to `TMP_Text` (common
  base, also covers world-space floating text) in both
  `DialogueAnnouncer` and `UITextExtractor`.
- **Cat's last line not read - diagnosed, not yet fixed**: likely the
  same "ambient NPC bark" filter (added per user request to silence
  noisy background chatter) also catching this one-off, narratively
  meaningful reaction. Added debug-mode logging of filtered bark text
  so next round can confirm before changing the filter's behavior.
- **"Messy coordinates" / guidance into walls**: root cause confirmed
  structural - Home-key guidance is a straight-line XY delta, no
  pathfinding, so it's meaningless once the target is in a different
  game `Location` (not geometrically continuous with the player's
  current area). Investigated the game's own `PathRequestManager`
  (threaded A*, queue+callback API) - technically viable but a
  separate, bigger, riskier feature (project rule: never wire up a
  game system without confirming safety first). Implemented a safer
  partial fix instead: detect cross-area targets via
  `Utils.HJPCBBGHPDA(Vector2)` and say so explicitly instead of
  giving a misleading delta. Real obstacle-avoiding pathfinding within
  the same area is still not implemented - flagged as a future, larger
  ask.

Build clean. See `docs/modules/world-object-navigation.md` 12th round
entry.

## Stopping point - 2026-06-20, round 20

- **Category list now defaults to "Portas"** instead of opening with
  everything lumped together (confirmed bug from round 19's feature).
- **Item names**: confirmed via log that every Placeable without
  itemSetup.item is pure scenery with no localization data - its name
  IS the original Spanish asset name. Added a small translation table
  for the specific Spanish strings actually observed in the log (not
  a guessed generic dictionary) - Grifo, Malteadora, Trapo Colgado,
  Grupo Ladrillos, Cajas Apiladas, Lateral Habitacion, Escalera
  Arriba, Horno Variant, Mesa de Cocina Variant, Ventana de Madera,
  Puerta, Cofre Pequeño, Cama del Jugador, Barril de Servicio, Cellar
  Door. The "Cacto" mislabeled torch (round 19) is unrelated bad game
  data, not a fallback-translation case - still unfixed.
- **Footstep cadence clarified**: the per-half-tile trigger was
  already correct since the first version - the cadence was never the
  problem, the native audio routing was (see round 19). Still no
  footstep sound until a dedicated `.wav` is provided.
- **New: direction-change sound**. User noticed the character turns
  to face a direction before moving - confirmed via
  `PlayerController.GetPlayerDirection(1)` (real-time `Direction`
  enum: Up/Down/Left/Right/Diagonal). Added `stand.wav` (loaded same
  way as parede.wav/itens.wav) played on every facing change, panned
  via `AudioSource.panStereo`: -1 for Left, +1 for Right, 0 for
  Up/Down/Diagonal.

Build clean, all 3 .wav files confirmed copied to Mods folder. See
`docs/modules/world-object-navigation.md` 11th round entry.

## Stopping point - 2026-06-20, round 19

- **Footstep root cause properly confirmed (decompiled, not guessed)**:
  used `ilspycmd` to decompile `AlmenaraGames.MultiAudioManager`/
  `AudioObject` from `Assembly-CSharp-firstpass.dll`. Confirmed the
  AudioClip selection always succeeds (no "doesn't have a valid Audio
  Clip" warning ever logged) - the silence comes from this 3rd-party
  plugin's own multi-listener/distance-volume system, not missing data.
  Per user request, removed the reused click sound for footsteps
  entirely (no replacement) - needs a dedicated `.wav` from the user if
  they want a footstep sound.
- **Wall-bump sound**: switched from discrete retriggers (0.6s cooldown)
  to a continuous loop that starts when the player gets stuck and stops
  the instant they're not. Stuck-confirmation delay set to 0.5s per
  request.
- **Filtered ghost list entry**: a `Placeable` with no `SpriteRenderer`
  ("BarManager", a manager script, not a physical object) was appearing
  in the navigation list - now skipped.
- **Found (not fixed)**: a wall torch resolves to the localized name
  "Cacto" via its Item's nameId - looks like bad source data in the
  game itself, not a bug in our lookup code.
- **Found for later**: `Container.cs` has `Slot[] slots` with real
  `Item` references inside - technically enables a future "peek inside
  a specific chest" feature (relevant to the user's "esfregão" ask),
  not implemented this round.
- **Category grouping implemented**: `Ctrl+Page Up/Down` cycles category
  (announces "Categoria: X (N)"), `Page Up/Down` now navigates within
  the current category. Categories: Portas, Containers, Máquinas,
  Coletáveis, Decorativos - classified by real component types
  (`Container`, `Crafter`, `Placeable.canBeAddedToInventory`).

Build clean. See `docs/modules/world-object-navigation.md` 10th round
entry.

## Stopping point - 2026-06-20, round 18

Still `feature/worldNavigation`, not yet committed. Gave up on the
game's native footstep audio system after a 3rd confirmed dead end,
switched to the user's own sound files, improved item-name
diagnostics, and clarified scope on the "find item inside chest" ask.

- **Footstep native audio abandoned**: 3 separate fixes (cooldown
  reduction, disabling native timer, replicating per-zone override
  list) all confirmed the trigger fired correctly but never produced
  audible sound through `MultiAudioManager`/`Footsteps`. Root cause
  still unknown and not worth a 4th blind attempt - footsteps now use
  `UISound.PlayNavigate()` instead, a clip already proven reliable
  elsewhere in this mod.
- **New: `CustomSounds.cs`** - loads user-provided `parede.wav` /
  `itens.wav` (dropped in the project root) via
  `UnityWebRequestMultimedia`, plays them through a throwaway
  `AudioSource` (spatialBlend 0) independent of the game's own audio
  systems. `.csproj` updated: new `UnityWebRequestModule`/
  `UnityWebRequestAudioModule` references, and the post-build copy
  step now also copies both `.wav` files to Mods folder. Wall-bump
  and item-proximity sounds now use these instead of `UISound`.
- **Item names still cryptic for some objects** - added diagnostic
  logging distinguishing "got it from itemSetup.item" vs "fell back
  to GameObject name parsing", to confirm next round whether
  decorative Placeables (furniture, etc.) just don't have an
  associated `Item` at all, rather than guessing a 3rd naming
  strategy blind.
- **Clarified, not fixed**: finding which specific chest contains a
  quest item ("o esfregão") needs a different feature (peeking
  container contents) - improving the chest's OWN name doesn't help
  with that, told user explicitly instead of attempting a guess.

Build clean (verified the .wav files land in Mods folder after
build). See `docs/modules/world-object-navigation.md` 9th round entry.

## Stopping point - 2026-06-20, round 17

Still `feature/worldNavigation`, not yet committed. Resolved the
footstep mystery (3 rounds pending), improved item names, and shipped
a first wall-bump sound attempt.

- **Footstep root cause found**: log confirmed the distance trigger
  was firing correctly every tile - the issue was always the SOUND
  selection, not the trigger. `FootstepObjectSound.cs` revealed the
  game registers custom per-zone clips into a private list
  (`Footsteps.PMPPEAHDDAB`) and only falls back to the generic
  `stepsWood`/`stepsDirt` fields when that list is empty - which are
  apparently unset placeholders in this game (all real footstep audio
  comes from zone overrides). Fixed by checking that list first via
  reflection.
- **Item names improved**: `Placeable.itemSetup.item.nameId` is a
  localization key (same pattern as Encyclopedia titles) - now used
  via `LocalisationSystem.Get()` instead of parsing the GameObject
  name, which produced cryptic results for some items.
- **Wall-bump sound implemented** (first attempt): no existing game
  signal for "blocked movement" found, so it compares real
  frame-to-frame displacement against the expected minimum for
  `PlayerController.speed` while `moving` is true - reuses
  `UISound.PlayBoundary()`. Empirical thresholds, flagged to user as
  needing live tuning.

Build clean. See `docs/modules/world-object-navigation.md` 8th round
entry.

## Stopping point - 2026-06-20, round 16

Still `feature/worldNavigation`, not yet committed. User clarified one
"bug" from last round wasn't real, and reported a regression caused
by last round's actual fix, plus a separate genuine filter bug.

- **Reverted the Door exclusion from `FindClosestAvailableByProximity`**
  (added round 15) - user confirmed the door toggling was just them
  testing repeatedly, not a real bug. Excluding Door had a real side
  effect: it caused the door's spoken name to fall back to whatever
  OTHER nearby Placeable was picked instead (user reported the
  entrance door saying "mesa de bebidas" and the cellar door saying
  "barril").
  - net effect: 2 of the 4 fixes from round 15/16 cancel out (added
    then removed the Door exclusion) - worth remembering as "Door
    must stay a candidate in that scan" if this comes up again.
- **Ambient bark filter was too broad**: confirmed via log the
  player's OWN bark lines ("O barril está vazio.") share the same
  "Bark UI" UI prefab as ambient NPC chatter, distinguished only by
  path root ("Player/..." vs an NPC name). Fixed with
  `IsAmbientNpcBark()` - only filters non-Player Bark UI paths now.
- Noted but not actioned: user unsure which listed Placeable items
  are meaningfully interactive - no filtering criteria defined yet,
  flagged as a possible next step if it proves too noisy.
- Footstep silence: still not diagnosed (diagnostic logging from
  round 15 not yet confirmed by a test).

Build clean. See `docs/modules/world-object-navigation.md` 7th round
entry.

## Stopping point - 2026-06-20, round 15

Still `feature/worldNavigation`, not yet committed. User confirmed the
crash fix + Stage 3 worked well, then reported 2 NEW bugs (found via
log, not guessed) plus 3 more feature asks.

- **Ctrl+Enter was toggling doors open/closed on its own** - `Door`
  also implements `IInteractable`/`IProximity` like the fireplace, so
  last round's fallback caught it too; `Door.MouseUp` just flips
  open/closed. Excluded `Door` from that fallback - doors already
  work fine via their native key.
- **New crash found**: `PlayerController.GetPlayerPosition` NREs
  during brief same-scene transitions where `GetPlayer(1)` is
  temporarily null (not covered by `Main.CheckGameReady()`). Added a
  guard at the top of `WorldNavigationHandler.Update()`.
- **Footstep sound still not playing** - confirmed via log that
  `Footsteps.instance` was never non-null when checked, all session
  (zero success log lines). Root cause not yet found - added
  diagnostic logging instead of guessing a 3rd fix blind.
- **Shipped**: sound on action-prompt arrival (`UISound.PlayNavigate`),
  ambient "Conversa ao redor" announcements disabled (user found it
  too noisy around Arthur's family), `Placeable` items added to the
  Page Up/Down list (same 30-unit proximity rule as doors, names
  reused from the game's own GameObject naming - not yet confirmed
  if this makes the list too long/noisy).
- Noted: tutorial gating blocks leaving the tavern right now - that's
  the game itself, not a bug, just means zone-change testing is
  blocked until that part of the tutorial is cleared.
- Wall-bump sound: still not started (now 2nd round deferred).

Build clean. See `docs/modules/world-object-navigation.md` 6th round
entry for full detail and the next test list.

## Stopping point - 2026-06-20, round 14

Still `feature/worldNavigation`, not yet committed. User reported a
silent regression (item announcements stopped completely) plus 4 new
asks. Found and fixed a real crash, implemented Stage 3, deferred 1
item explicitly.

- **Critical bug fixed**: `Harvestable.IsAvailableByProximity` threw
  a `NullReferenceException` every frame inside
  `GetNearestInteractionName()` (added last round), silently killing
  `DialogueAnnouncer.ScanAndAnnounceText()` entirely - confirmed via
  2305 repeated exceptions in the log, all with the same stack trace.
  Fixed with try/catch around the proximity check.
- **Area announcement**: never fired in the user's test. Added
  unconditional debug logging of the raw `Location` value on every
  change (not just non-None) to confirm next round whether it's a
  real bug or the player just never crossed a zone-transition trigger.
- **Footsteps re-done**: time-based cooldown reduction wasn't good
  enough per user feedback (wants exactly 1 sound per tile while
  walking). Disabled the native timer (huge cooldown via reflection)
  and added our own distance-based trigger (0.5 units = 1 tile,
  confirmed in `WorldGrid.allNeighbours`), replicating the game's own
  terrain-to-clip logic in simplified form. Required referencing
  `Assembly-CSharp-firstpass.dll` (where `AudioObject`/
  `MultiAudioManager` live) in the `.csproj`.
- **Stage 3 shipped**: Home key toggles continuous direction+distance
  guidance to the Page Up/Down-selected target, updating every tile
  walked ("10 pra cima, 8 pra esquerda" style, world-space deltas).
- **Deferred to next round**: wall-bump sound (detect blocked
  movement) - new ask, not started, too much already in this round to
  add safely.

Build clean (had to add the new assembly reference). Not yet tested
live - next round needs the crash fix confirmed first (it's the
highest-impact one), then Stage 3 and footsteps. See
`docs/modules/world-object-navigation.md` 5th round entry.

## Stopping point - 2026-06-20, round 13

Still `feature/worldNavigation`, not yet committed. User reported the
new Stage 2 list felt too restrictive and raised 3 new orientation
asks together. Triaged: implemented the well-understood ones, clearly
deferred the bigger one rather than guess.

- **Doors should appear by proximity, not just after being opened**:
  `BuildTargetList()` now also includes any `Door` within 30 units of
  the player (the remembered "entrance" door is still tracked
  separately, since that's the only one that needs disambiguating
  from others by NAME, not just presence).
- **Items + characters + categories**: NOT implemented this round -
  confirmed `Placeable` (items) and `DialogueNPCBase`/`NPC`
  (characters) exist as the likely enumerable types, but giving them
  proper categories needs its own investigation pass (next round).
- **Area name on zone change**: implemented - polls
  `PlayerController.LEOIMFNKFGA` (a `Location` enum), announces
  "Área: <nome>" via a hardcoded PT-BR translation table
  (`LocationNames`) on change.
- **Footstep cadence**: confirmed via decompiled `Footsteps.cs` this
  is the game's OWN system (not ours), time-based (0.5s cooldown
  while moving) rather than tile-based. Real tile size confirmed in
  `WorldGrid.allNeighbours` = 0.5 units. Reduced the cooldown via
  reflection to 0.2s (no public setter) instead of adding a second,
  competing sound source. Empirical adjustment - flagged to the user
  as something to re-tune after testing, not a guaranteed-exact fix.

Build clean. Not yet tested live. See `docs/modules/world-object-navigation.md`
(4th round entry) for full detail.

## Stopping point - 2026-06-20, round 12

Feature branch `feature/worldNavigation` (off `main`), not yet
committed. Big round - moved from Stage 1 (feasibility) into Stage 2
(first real player-facing control) plus 2 unrelated bugs found via
live log during testing:

- **Stage 1 confirmed both targets**: door via `GetCurrentInteractGO()`
  (captured live, dist~0), bed via `Bed.GetPlayerBedPosition()`
  (static call, no proximity needed).
- **Bed's "Dormir?" popup was never announced** - found via decompiled
  `Bed.OnTriggerEnter2D`: it's a pure proximity trigger opening a
  `YesNoDialogueUI`, whose question text was never read. Fixed in
  `KeyboardUINavigator.AnnounceYesNoQuestionIfChanged` - confirmed
  working by user.
- **Fireplace-type objects never worked with Q nor with the
  Ctrl+Enter fallback** - root-caused via decompiled `Fireplace.cs`:
  it implements `IInteractable`/`IProximity`, bypassing
  `InteractObject.GetCurrentInteractGO()` entirely (confirmed null in
  log every time). Fixed: Ctrl+Enter now also tries calling
  `IInteractable.MouseUp` directly on the closest
  `IProximity.IsAvailableByProximity` object. "Combustível" (add fuel)
  still unresolved - looks like inventory drag-and-drop, different
  problem, deferred.
- **Action-prompt announcement (added last round) was ping-ponging
  forever** - root cause in log: 2 simultaneous prompts (fireplace's
  "Abrir" + "Combustível") thrashed a single-value tracker. Fixed with
  a `HashSet` of currently-visible prompts (announce only new ones).
  Also added the object's name to the announcement per user request
  (`WorldNavigationHandler.GetNearestInteractionName()`).
- **Stage 2 shipped**: Page Up/Down cycles between the 2 known targets
  and speaks the name. Door has no stable static reference (its
  GameObject name is reused by every door in the game) - solved by
  remembering whichever Door the player actually opens via
  `GetCurrentInteractGO()`, once per session.

Build clean. Not yet tested live - next round needs to confirm Stage 2
cycling, the fireplace Ctrl+Enter fix, and the dedup fix, all together.
See `docs/modules/world-object-navigation.md` (3rd round entry) and
`docs/modules/core-gameplay-navigation.md` for details.

## Stopping point - 2026-06-20, round 11

Found the real root cause of the recurring "Space produces no sound
advancing dialogue" report (rounds 10/11 fixes didn't actually fix
it). Grepped ALL historical logs (not just the latest) for the
success log line `"Advance dialogue"` - zero matches, ever, in the
mod's entire history, even though dialogue visibly advances one line
per Space press every time. Root cause: `FindActiveContinueButton()`
required `button.interactable == true`, which this particular button
apparently never satisfies (in either dialogue UI variant) - the game
advances dialogue itself, natively, not through this Button's
onClick. Fixed: detection now only checks `activeInHierarchy`, and no
longer calls `.onClick.Invoke()` (that risked double-advancing,
racing the game's own handler) - it only gates the confirm sound. See
`docs/modules/dialogue-system.md` v10. Not yet tested live - this is
the 3rd attempt at this specific issue, so confirm carefully next
round.

## Stopping point - 2026-06-20, round 10

Confirmed live by the user: Encyclopedia "Voltar" position fix,
subsection reorder fix, and re-read-on-reactivate all work correctly.
The last open item ("Space produces no sound advancing dialogue") is
**closed - not a bug**: read the user's `Latest.log` and found 215
consecutive "no active Continue Button" log lines, all during one
specific story line ("...mas uma voz atrás de você te faz parar.") -
the next line appears on its own ~20s later with no key pressed at
all, confirming the game itself is gating advancement on a scripted
trigger (an NPC arriving), not on player input. See
`docs/modules/dialogue-system.md` v8. No code change.
User also suggested consulting game manuals/wikis proactively for
features, not just reacting to live-test reports. Asked whether to
formalize this in `docs/feature-plan.md` or keep it informal - chose
to keep it as a general principle, no doc/workflow change.

## Stopping point - 2026-06-20, round 7

Build is clean (0 errors/warnings). Processed the live-test feedback
from round 6 (`novo_pedido.txt`). Full detail in
`docs/modules/main-panel-tabs.md`. Summary:
- **Nav sound + boundary sound were indistinguishable:** confirmed by
  the user ("tocam muito juntos") - root cause was playing both in the
  exact same frame, which blends them into one sound. Fixed with
  `UISound.PlayBoundaryDelayed()` (~0.15s gap via
  `MelonCoroutines.Start`), replacing the simultaneous `PlayBoundary()`
  call in both `KeyboardUINavigator.Move()` and
  `DialogueAnnouncer.HandleResponseInput()`.
- **Distinct sound for "chose a dialogue response" vs. "just advanced
  to the next line"**, per explicit request - added
  `UISound.PlayChoiceConfirm()` (same clip, pitched up) for the
  response-choice case only.
- **"Sounds don't play at all" report (main menu, going back to
  play):** root cause not yet confirmed - added debug-gated diagnostic
  logging in `UISound` instead of guessing (logs when `Sound.GGFJGHHHEJC`
  is null or the game's own `blockSound` is active). Needs the next
  live test with F12 on to pin down.
- **Main quest in the Quests tab fixed for real:** the previous fix
  (round 5/6) announced it as a side-channel instead of a real list
  item, and the user explicitly asked to find/interact with it in the
  menu instead. Found a real `public Button` field on `MainQuestItemUI`
  in decompiled source (the earlier "no Button at all" note was
  checking the wrong GameObject) - it's now a normal navigable item;
  Enter toggles its focus, same as clicking it with mouse/gamepad.
- **Encyclopedia topic content (e.g. "Controles Básicos") wasn't being
  read at all:** confirmed cause - it's plain display text with no
  Button, and the global dialogue scanner intentionally skips entirely
  while any UI is open (to avoid double-reading menus). Fixed with the
  same pattern used for the quest fix before: read the two private
  `TextMeshProUGUI` fields via reflection, announce on change.
- **Encyclopedia "Voltar" not reachable by arrows/Enter:** investigated
  and confirmed there's no real clickable "back" button in the game at
  all (just an Esc-key hint icon) - implementing this for real would
  need a synthetic mod-only UI element. Asked the user; chose to defer,
  since Esc already closes the screen today.
- All of the above (except the still-open sound mystery) are
  implemented but **not yet tested live** - next round should re-run
  the same checklist plus specifically: Encyclopedia topic content,
  Quests tab main mission, and the two sound adjustments.

## Stopping point - 2026-06-20, round 9

Build clean. User tested round 8 live and sent corrections again - this
time read the user's actual `Latest.log` directly (F12 was on) instead
of guessing, per the new efficiency rule. Findings:
- Round 8's "Voltar" fix was wrong (matched nothing, since the real
  button isn't under "TabsListContent" at all) AND had an unintended
  side effect (the same overly-broad filter could grab an unrelated
  subsection button instead). Re-fixed with the REAL path from the
  log: it's the already-known "VersatileButton" pattern
  (`.../VersatileButton/Button`).
- That same log also explained a second bug the user reported in
  passing ("Controles Básicos" subsection unreachable): Unity's own
  duplicate-naming leaves the FIRST clone unsuffixed ("Subsection")
  and only numbers the rest - confirmed it really is subsection #1,
  just sorted to the very end by Y. Fixed with an explicit reorder by
  the "(N)" suffix.
- Added: re-activating an already-read subsection now re-announces its
  content (previously silently did nothing the 2nd time).
- "Sounds don't play in the menu" (open since round 7): user confirmed
  this round it DOES play now - closed, no fix needed.
- Still open: Space-to-advance-dialogue sound. Checked the log - the
  code path never even ran this session (zero matching log lines), so
  still can't confirm root cause. Added one more debug log line for
  the "Space pressed but no Continue Button found" case specifically.
- User asked for an efficiency-focused workflow going forward (see
  `CLAUDE.md`) - this round applied it by reading the user's own log
  file directly instead of more back-and-forth guessing.

## Stopping point - 2026-06-20, round 8

Build clean. User tested round 7 live and corrected one of round 7's
own conclusions: the Encyclopedia DOES have a real clickable "Voltar"
button (confirmed - user pressed Enter on it, it closed the screen) -
round 7's claim of "no real button" was wrong (it isn't named
"ButtonComponents" like the 14 sections, and its Button isn't
referenced by any field in decompiled C#, so it was missed). It was
already in the nav list, just landing in the wrong position (between
section 11 and 12). Fixed: any non-"ButtonComponents" `Selectable`
sharing the same `TabsListContent` region is now treated as the back
button, labeled "Voltar", and moved to the end of the list. Confirmed
working this round. Also confirmed working: nav/boundary sound
separation, and the quest item now showing up in the Quests list.
Still open: Space-to-advance-dialogue sound not audible (Enter and
response-choice sounds both work) - not yet root-caused, same
diagnostic logging as the menu-sound mystery should help next test.
User also asked to work more token-efficiently going forward - see
`CLAUDE.md` "Efficiency / Collaboration Style" section.

## Stopping point - 2026-06-19, round 6

Build is clean (0 errors/warnings). Quick-turnaround round, mostly
refining round 5's new features based on immediate feedback. Full
detail in `docs/modules/main-panel-tabs.md`. Summary:
- **Navigation sound layering fixed**: was playing the move sound OR
  the boundary sound (one replacing the other) - user clarified they
  want the move sound ALWAYS, with the boundary sound layered on top
  only at a boundary/single-item case, not replacing it. Fixed in both
  `KeyboardUINavigator.Move()` and the dialogue response navigation.
- **New: confirm sounds for Space and Enter** - same navigate sound
  now also plays when Space advances dialogue/confirms a response
  choice, and when Enter activates a menu item - user wasn't sure
  their keypresses were registering.
- **Found and fixed a real bug while investigating the Encyclopedia
  feedback** ("a lista está começando do item 11/12"): confirmed the
  section-name fix itself worked, but cursor-preservation state
  (`_rememberedAnchors`, meant for returning from a nested popup like
  ColorPicker -> CharacterCreator) was leaking across fully separate
  open/close sessions for singleton windows like Encyclopedia, since
  it's keyed by UIWindow instance and those instances persist for the
  whole play session. Now cleared whenever everything closes.

## Stopping point - 2026-06-19, round 5

Build is clean (0 errors/warnings). Branch renamed/forked to
`feature/navigation` per the user's explicit request (menu/navigation
fixes only here; walking/movement sounds to be a separate feature,
created later). Full detail in `docs/modules/main-panel-tabs.md`.
Summary:
- **Encyclopedia sections now have real names** - the earlier
  diagnostic-log plan (round 4) never actually fired, root cause found
  (same `justOpened` timing bug class as a previous round) - implemented
  the real fix directly instead: match section buttons to their real
  localized title by POSITION in the list (not by parsing "SectionN",
  confirmed unreliable).
- **Main quest now announced in the Quests tab** - confirmed it's a
  pure display component with no Button at all, so it could never have
  shown up in arrow-key navigation. Announced separately instead of
  navigated.
- **New: navigation sound feature**, per explicit user request (sound
  on every list move, a different one at a boundary/single-item case,
  for all lists). Found and reused the game's own UI click sounds
  (`Sound.uiClickPos`/`uiClickNeg`) instead of adding new audio assets -
  see `docs/game-api.md` §7 and `UISound.cs`. Wired into both
  `KeyboardUINavigator.Move()` and dialogue response navigation.
- Still unsolved: the recurring unlabeled "VersatileButton" (icon is
  identical across all its uses, no new lead yet).
- Wall-bump sound and the big object-navigation/pathfinding feature
  are intentionally OUT of scope for this branch per the user - will
  get their own feature when started.

## Stopping point - 2026-06-19, round 4

Build is clean (0 errors/warnings). Fourth round, same day. Full detail
in `docs/modules/dialogue-system.md` (v7) and
`docs/modules/core-gameplay-navigation.md`. Summary:
- **Dialogue response fix from round 3 had a gap:** confirmed live
  every response screen so far has exactly 1 option (never 2+), but
  the code only let Up/Down do anything when there were 2+ options -
  with 1 option they did nothing at all, which read as "arrows broken"
  to the user. Fixed (Up/Down now always at least re-announce the
  current response). Also made the "did the response set change"
  check more robust (sibling-index + set comparison instead of
  position-ordered sequence) for whenever a real 2+-option case shows
  up.
- Added a debug-only diagnostic for `MainPanelUI`'s recurring
  unlabeled "VersatileButton" (confirmed live in Quests: "Button (8 of
  9)", user unsure if it's a new-quest marker) - logs its icon sprite
  name for next round, since it's not in decompiled source (pure
  scene/prefab name) and its meaning differs per panel, so no safe
  label to assign yet.
- **Big new feature scoped, not implemented:** the user wants to
  choose a nearby item/entrance/object/character from a list and get
  spoken directional guidance to walk there (shortest walkable path,
  avoiding walls) - concrete example given, can't find the path to the
  bed. Did feasibility research: the game already has its own A*
  pathfinding (`PathRequestManager.RequestPath`), so we don't need to
  build pathfinding from scratch - confirmed viable, but it's a
  multi-round project (target list, position lookup, path request,
  route-to-speech translation, recalculation while walking). Smaller
  related request also logged: a sound when the character is stuck
  against a wall - not yet researched.

## Stopping point - 2026-06-19, round 3

Build is clean (0 errors/warnings). Third round, same day. Full detail
in `docs/modules/dialogue-system.md` (v6) and
`docs/modules/main-panel-tabs.md`. Summary:
- **Critical bug fixed: dialogue CHOICES (not just linear continue)
  were unusable** - confirmed live the user got stuck 3+ minutes on a
  conversation with a real response option ("Eu não sou um
  saqueador..."), since our code only knew about the linear "Continue
  Button", not the separate "Response Menu Panel" response buttons.
  `DialogueAnnouncer` now detects these, lets Up/Down pick between
  options (when there's more than one) and Space confirm. This was
  very likely blocking real story progress - top thing to verify next.
- **Fixed the "lost objective message" bug** from round 2 (confirmed
  cause this time): `DialogueAnnouncer` was gated on
  `KeyboardUINavigator.ItemCount`, which lags a few frames behind a
  window actually opening - during that gap it was reading the panel's
  own placeholder text (e.g. "Sem receitas ainda") as if it were
  dialogue, overwriting the real last message. Now gated on
  `MainUI.IsAnyUIOpen(1)` directly instead (no lag).
- **User asked for arrows to never move the character, even outside
  menus** (only W/A/S/D should walk, ever) - `MovementAxisPatch`
  (added last round, was conditional) is now permanently on.
- **Two new gaps confirmed, not yet fixed** (no existing signal to
  hook, needs dedicated research first): picking up an item (key Q)
  gives a sound but no announcement at all (no text appears on screen
  for our scanner to catch - need to find the real "item added" game
  event instead); entering the tavern gives no announcement either
  (same underlying gap as the still-unimplemented zone-change
  proposal in `core-gameplay-navigation.md` - now confirmed as the
  top priority for the next implementation round, not just research).

## Stopping point - 2026-06-19, round 2

Build is clean (0 errors/warnings). Second round of gameplay testing,
right after round 1 below. Full detail in `docs/modules/main-panel-tabs.md`
and `docs/modules/core-gameplay-navigation.md`. Summary:
- Round 1's "fix" for Inventory navigation was wrong (confirmed live);
  re-fixed properly via reflection on the game's own selected-tab-index
  field - should also fix the Quests/Recipes/Skills navigation reports
  from this same round (same underlying tab strip, same bug).
- New bug found + fixed: arrow keys were also moving the character
  while those screens were open (confirmed intentional game design -
  only PauseMenuUI blocks movement). User chose to keep WASD as
  movement always; new Harmony patch (`MovementAxisPatch.cs`) makes
  arrows exclusively ours while a nav screen is open.
- Encyclopedia's 13 sections all sound identical - cause confirmed,
  fix deferred (the obvious approach risks mislabeling sections;
  added diagnostic logging instead, see module doc for why).
- World navigation research got its first real data point (the
  action-prompt diagnostic logging from round 1 captured a real
  sample) - still not wired into a live announcement, next round.
- Two items flagged by the user but NOT actionable yet (no log
  evidence to confirm/reproduce): the "Up arrow stopped re-reading the
  objective text after opening a menu" report, and a large future
  feature request (audio-guided navigation to nearby objects + a list
  of visible/nearby items the player can choose to auto-walk to) -
  both need more specific input from the next test round before any
  code changes; see `novo_pedido.txt`.
- **Top priority for next session:** test all of the above live, and
  if confirmed working, finally implement the action-prompt
  announcement (the only piece of `core-gameplay-navigation.md`'s
  proposal still not built).

## Stopping point - 2026-06-19, round 1

Build is clean (0 errors/warnings). The Space-bug saga from earlier
this session ended resolved (5th fix attempt, a Harmony patch on
`CharacterCreatorUI.AcceptButton()` - see `new-game-setup.md` v10) and
text-field editing, which a leftover piece of an earlier disproven fix
attempt had broken, was fixed right after (confirmed by user this round
- both are done, no longer pending).

This round's session moved into actual gameplay for the first time.
Confirmed/fixed (all **untested** until next round):
- Pause menu: 2 icon-only buttons (Discord, "Unstuck"/get-unstuck) had
  no spoken label (generic GameObject name "Button") - labeled by
  parent icon name now.
- Inventory (and Map/Encyclopedia/etc., same `MainPanelUI` tab strip):
  arrow-key navigation did nothing inside the slots grid - root cause,
  only the tab strip itself was being scanned for items, never the
  selected tab's own content panel (same structural gap `OptionsMenuUI`
  needed special handling for, just not yet applied here). Fixed by
  scanning whichever `MainPanelUI.panelsUI[].content` is currently
  active in the hierarchy.
- Validated (no code change needed): the loading tip's mention of
  "difficulty level" in Options is just a single on/off toggle for
  challenge events, not a real difficulty system - already handled by
  existing generic toggle code.

**New, not-yet-implemented territory started:** per the user's request
("comece a analisar... não sei pra onde ir"), began investigating world
navigation (zones/interactables/map) - see new
`docs/modules/core-gameplay-navigation.md`. Found `PlayerController
.OnZoneChanged` and confirmed the existing "[E] ..." action-prompt text
(today filtered as noise) is the game's own "something interactable is
nearby" signal - added debug logging for it, but deliberately did NOT
wire up a live announcement yet (need one round of real log evidence
first - this is brand new ground, not a regression fix, and the
project's established discipline is to confirm before hooking into a
new game singleton/event, to avoid crashes). **Next session: read what
the action-prompt logging captured, then implement.**

Also added per the user's request for better debug tooling:
`DebugLogger.LogRawKeyDowns()` (every key-down, debug-gated) and
`TutorialTracePatch.cs` (logs `NewTutorialManager.ShowPopUp` calls).

**Known, paused limitation (user's explicit choice):** Character
Creator color names are unreliable - picking colors works, the spoken
name can be wrong. Root cause confirmed too obfuscated to fix without
much deeper reverse-engineering. Not being worked on unless asked.
- **Branch:** `feature/newGame` (tracks `origin/feature/newGame`), branched
  from `main` after `feature/mainMenu` was merged in (fast-forward, no
  conflicts).
- **Live test loop:** see `novo_pedido.txt` at project root for the latest
  round of test feedback and next steps - rewritten after every test round.
- Commit only when the user explicitly says a version is ready to commit.
  Branch workflow: finish a feature -> merge to `main` -> branch the next
  feature from `main`, to keep `main` current and avoid long-lived
  diverging branches.

## Module Docs

- `docs/modules/main-menu-and-options.md` - Title Screen, "Jogar"/SaveUI,
  Options (all 4 tabs). Done/stable.
- `docs/modules/new-game-setup.md` - Character Creator screen. In progress.
- `docs/modules/dialogue-system.md` - Generic narrative/dialogue text
  announcer (used by the intro story, and reusable for NPC dialogue
  later). In progress.
- `docs/modules/main-panel-tabs.md` - Inventory/Quests/Recipes/Skills/
  Encyclopedia (MainPanelUI tab navigation, movement-key conflict).
  In progress.
- `docs/modules/core-gameplay-navigation.md` - World navigation
  research: zones, interactables, map. Research only, not implemented.
- `docs/modules/world-object-navigation.md` - Navigate to
  doors/objects/characters (Page Up/Down list, Home for direction, End
  to auto-walk). Active feature, branch `feature/worldNavigation`,
  Stage 1 (coordinate feasibility) in progress.
- (Add one file per feature/module here as new ones start, e.g.
  `docs/modules/tutorial.md`.)

## Original Setup Next Steps (completed, kept for history)

1. Read `docs/ACCESSIBILITY_MODDING_GUIDE.md` completely
2. Run codebase analysis (Phase 1 in `docs/setup-guide.md`): namespaces, singletons, input system, UI system, game mechanics, tutorial
3. Document findings in `docs/game-api.md`
4. Create feature plan (Phase 1.5)
5. Set up basic C# project (Phase 2) - first goal: mod loads and announces "Mod loaded" via Tolk/NVDA
