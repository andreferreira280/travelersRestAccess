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

## Stopping point - 2026-06-20, round 11 (most recent)

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
- (Add one file per feature/module here as new ones start, e.g.
  `docs/modules/tutorial.md`.)

## Original Setup Next Steps (completed, kept for history)

1. Read `docs/ACCESSIBILITY_MODDING_GUIDE.md` completely
2. Run codebase analysis (Phase 1 in `docs/setup-guide.md`): namespaces, singletons, input system, UI system, game mechanics, tutorial
3. Document findings in `docs/game-api.md`
4. Create feature plan (Phase 1.5)
5. Set up basic C# project (Phase 2) - first goal: mod loads and announces "Mod loaded" via Tolk/NVDA
