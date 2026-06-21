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

## Stopping point - 2026-06-21, round 42 (most recent)

Still `feature/worldNavigation`, not yet committed. The door-blocking
detection finally has a confirmed root cause (not another guess).

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
