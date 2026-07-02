# TravellersRestAccess

## User

- Blind, uses screen reader (NVDA), communicates in Portuguese
- Programming experience: Little/None - explain concepts with context
- User provides direction, Claude Code writes code independently and explains
- For uncertainties: Ask briefly, then act
- Screen reader-friendly output: NO tables with `|`, use lists instead

## Efficiency / Collaboration Style (user request, 2026-06-20)

- Don't propose global restructuring, broad refactors, or organization
  changes unless explicitly asked - keep using the project structure
  exactly as it is.
- Reuse context already established in the session/docs before
  re-reading files; when more context is needed, ask for the minimal
  delta/diff/snippet, not the whole file.
- Work in small, incremental steps; aim for the smallest change that
  satisfies the request.
- Treat prior decisions as still valid unless the user says otherwise;
  don't re-explain or re-diagnose things already settled.
- At the start of a new session, do a compact strategic read of current
  state (project_status.md + the relevant module doc) before diving in,
  rather than re-exploring the whole codebase.
- In `novo_pedido.txt`, in addition to the existing
  done/investigating/next-test sections, briefly state what was
  understood from the user's latest message and what was/will be done
  about each point, so they can confirm the read was correct.

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

## Padrões do Projeto (regras reutilizáveis - sempre aplicar)

Definidos com o usuário. Quando surgir algo parecido, REUSE estas regras em vez
de reinventar - elas já resolvem a maior parte dos problemas recorrentes.

### Estações e contêineres (baú, dispensador, barril, forno, malte, etc.)
- Inventário do jogador SEMPRE no lado DIREITO: seta direita vai pro inventário,
  seta esquerda volta pra estação.
- Ctrl+Enter ADICIONA (inventário -> estação) e REMOVE (estação -> inventário).
- Aceitar/Cancelar (e Combustível/Voltar) ficam SEMPRE no menu da ESTAÇÃO
  (lado esquerdo), NUNCA no lado do inventário.
- Estações de RECEITA (forno/malte): o lado do inventário (direita) só aparece
  quando uma receita está aberta (ModifierUI ativo). Antes disso, só o menu da
  estação (senão o inventário aparece vazio).

### Anúncios de mundo (telhas e alvos de ferramenta)
- SEMPRE anunciar a telha UMA À FRENTE, na direção que o jogador encara
  (independente da ferramenta na mão - o jogador pode esquecer o que está
  segurando).
- Anunciar a CADA mudança da telha à frente OU do seu conteúdo (a cada passo,
  sempre - nunca só uma vez).
- Só anunciar coisas interativas/úteis (grama, terra arada, buraco, terra,
  planta, árvore, pedra/minério...); silêncio no resto.
- Sem prefixo ("À frente:" não) - falar direto o nome. Uma telha pode ter mais
  de uma coisa (GroundType é [Flags] + overlays + objetos) -> encadear com
  vírgula.
- NÃO anunciar a telha onde o jogador pisa (só a da frente).
- Ferramentas miram na telha à frente pela direção que o jogador encara (WASD),
  não no mouse. Ao usar (F) sem alvo válido: avisar + som de golpe no vazio.

### Sons do mod
- NOSSOS sons de mundo (parede, batida/bump, proximidade, zona, etc.) devem ser
  MUTADOS sempre que QUALQUER UI estiver aberta (diálogo, menu, estação) - eles
  são feedback de andar pelo mundo e atrapalham menus. Mute central:
  `CustomSounds.MuteActive` (= diálogo do jogador OU `UiOpen`). Ao adicionar um
  som novo, passá-lo por esse mesmo gate.

### Fluxo de trabalho
- Quando o usuário der uma permissão (yes/no), NÃO perguntar de novo: seguir até
  terminar a rodada.
- SEMPRE checar estes Padrões do Projeto ao fazer qualquer mudança/feature nova
  (estações, anúncios, sons) - reusar as regras existentes em vez de reinventar.

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
- `docs/modules/` - Per-feature continuity notes (architecture, status, decisions, open questions). Read the relevant one before touching that feature's code; add/update one per feature.
- `project_status.md` - Current setup/progress status, points to the active module doc
- `novo_pedido.txt` - Latest test-round feedback loop with the user (rewritten every round)
- `templates/` - Code templates
- `scripts/` - PowerShell helper scripts

## Git Workflow

- Repo already initialized with remote `origin` (GitHub: andreferreira280/travelersRestAccess), branch `feature/mainMenu`.
- Only commit when the user explicitly says a version is ready (e.g. "temos uma versão commitável"). Never commit proactively.
- Confirm with the user before pushing to the remote.
