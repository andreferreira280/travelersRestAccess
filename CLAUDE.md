# TravellersRestAccess

## User

- Blind, uses screen reader (NVDA), communicates in Portuguese
- Programming experience: Little/None - explain concepts with context
- User provides direction, Claude Code writes code independently and explains
- For uncertainties: Ask briefly, then act
- Screen reader-friendly output: NO tables with `|`, use lists instead

## Project Start

For greetings ("Hello", "New project", "Let's go"):
Read `docs/setup-guide.md` and conduct the setup interview. (Already completed for this project - see `project_status.md`.)

## Environment

- **OS:** Windows (Bash/Git Bash)
- **Game:** Travellers Rest (Early Access), by Louqou
- **Game directory:** `C:\Users\andre\Downloads\Travellers Rest (Early Access)\Travellers Rest\Windows`
- **Architecture:** 64-bit
- **Engine:** Unity 2022.3.62f2
- **Runtime Type:** net35 → TargetFramework `net472`
- **MelonGame attribute:** `[assembly: MelonGame("Louqou", "TravellersRest")]`
- **Multilingual:** Yes - auto-detect game language (see `docs/localization-guide.md`)

## Coding Rules

- **Mod name:** TravellersRestAccess
- **Handler classes:** `[Feature]Handler`
- **Private fields:** `_camelCase`
- **Logs/Comments:** English
- **Build:** `dotnet build TravellersRestAccess.csproj`

## Coding Principles

- **Playability, not simplification** - Make game playable as sighted players play it; only suggest cheats when unavoidable
- **Modular** - Separate input handling, UI extraction, announcements, game state
- **Maintainable** - Consistent patterns, easily extensible
- **Efficient** - Cache objects, avoid unnecessary processing
- **Robust** - Use utility classes, handle edge cases, announce state changes
- **Respect game controls** - Never override game keys, handle rapid key presses

Patterns: `docs/ACCESSIBILITY_MODDING_GUIDE.md`

## Before Implementation

**ALWAYS:**
1. Search `decompiled/` for actual class/method names - NEVER guess
2. Check `docs/game-api.md` for keys, methods, patterns
3. Use only "Safe Keys for Mod" (see game-api.md → "Game Key Bindings")

## References

- `docs/setup-guide.md` - Project setup interview
- `docs/ACCESSIBILITY_MODDING_GUIDE.md` - Code patterns and architecture
- `docs/localization-guide.md` - Text and announcement localization
- `docs/menu-accessibility-checklist.md` - Menu implementation checklist
- `docs/game-api.md` - Keys, methods, documented patterns
- `docs/feature-plan.md` - Feature catalog and priority order
- `project_status.md` - Current setup/progress status
- `templates/` - Code templates
- `scripts/` - PowerShell helper scripts
