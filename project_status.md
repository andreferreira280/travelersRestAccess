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
- **Active feature:** New Game setup - this turned out to have two parts:
  1. Intro story dialogue (general-purpose narrative/NPC text reader,
     not just the intro) - see `docs/modules/dialogue-system.md`.
     **Working and tested** as of 2026-06-18: text is read correctly
     (story + ambient NPC barks distinguished), Space advances dialogue,
     Up re-reads last story line, Down re-reads last ambient line.
  2. Character Creator screen - see `docs/modules/new-game-setup.md`.
     Generic navigation works (33 items found with no custom code), body
     part rows collapse into one item each. A whole round of fixes was
     just implemented (text field editing, gender selected-state, color
     names, color picker Left/Right convention, cursor returning to the
     right spot after closing the color picker) - **NOT YET TESTED**,
     this is the very next thing to verify next session.
- Decided with the user: rich environment descriptions (e.g. "a wood
  tavern lit by candles...") will be hand-written for the fixed intro
  content only, not auto-generated via AI vision (out of scope for now).

## Stopping point - 2026-06-18 (end of session)

User is done testing for today. Build is clean (0 errors/warnings) as of
the last change. Everything below is implemented but **UNTESTED** - this
is exactly where to resume next session (no need to re-explain anything,
just continue iterating from a fresh "testei" round):

1. Character Creator text fields (player name, tavern name) - Enter now
   activates real Unity text editing; never tested with actual typing.
2. Color picker: Left/Right now navigates between swatches (Up/Down
   everywhere else) - untested.
3. Cursor should now return to the exact item (e.g. a specific
   ColorButton) you pressed Enter on before a popup opened, instead of
   resetting to the first item - untested, this is a second attempt
   (first attempt/hypothesis, a 0.15s close-debounce, did NOT fix it).
4. Color names (red/blue/etc.) - fixed to read the game's real
   material-based color data instead of the button's own Image (which
   is always white) - untested, and even when working it's only an
   approximation (nearest match in a small fixed palette).
5. Gender (Male/Female) "selected"/"not selected" announcement via
   reflection on private fields - untested.
6. "Random"/"ButtonLeft" buttons now announce something after activating
   - untested.
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
- (Add one file per feature/module here as new ones start, e.g.
  `docs/modules/tutorial.md`, `docs/modules/inventory.md`.)

## Original Setup Next Steps (completed, kept for history)

1. Read `docs/ACCESSIBILITY_MODDING_GUIDE.md` completely
2. Run codebase analysis (Phase 1 in `docs/setup-guide.md`): namespaces, singletons, input system, UI system, game mechanics, tutorial
3. Document findings in `docs/game-api.md`
4. Create feature plan (Phase 1.5)
5. Set up basic C# project (Phase 2) - first goal: mod loads and announces "Mod loaded" via Tolk/NVDA
