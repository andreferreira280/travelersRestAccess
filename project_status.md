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
- **Active feature:** New Game setup (Character Creator + whatever screens
  appear between "Novo" and gameplay actually starting) - see
  `docs/modules/new-game-setup.md`. Just started, mostly unexplored.
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
- `docs/modules/new-game-setup.md` - Character Creator and first
  new-game screens. In progress.
- (Add one file per feature/module here as new ones start, e.g.
  `docs/modules/tutorial.md`, `docs/modules/inventory.md`.)

## Original Setup Next Steps (completed, kept for history)

1. Read `docs/ACCESSIBILITY_MODDING_GUIDE.md` completely
2. Run codebase analysis (Phase 1 in `docs/setup-guide.md`): namespaces, singletons, input system, UI system, game mechanics, tutorial
3. Document findings in `docs/game-api.md`
4. Create feature plan (Phase 1.5)
5. Set up basic C# project (Phase 2) - first goal: mod loads and announces "Mod loaded" via Tolk/NVDA
