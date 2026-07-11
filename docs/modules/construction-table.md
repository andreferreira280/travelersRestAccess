# Módulo: Mesa de Construção (Construction Table)

Arquivo: `ConstructionTableHandler.cs`

## Objetivo
Tornar acessível a mesa de construção (edição de tavern: pisos, paredes, portas,
zonas, decoração) e, principalmente, permitir que um jogador cego atravesse o
**tutorial de construção** guiado (muito visual) e construa salas de verdade.

## Estado atual (rodada 220, 2026-07-11)

### O que já funciona
- Abertura/fechamento anunciados; navegação de abas ([ e ]), andares, slots (setas
  esquerda/direita), cursor (WASD) com anúncio de coluna/linha/piso-vazio.
- `L` = caixa delimitadora da área destacada (verde); `J`/`K` = coluna/linha atual.
- **Shift+K** (`AutoPlaceAll`) = coloca TODOS os tiles verdes (AddFloorDisponible)
  como piso real. Debita materiais. RETORNA bool (sucesso) agora.
- **F8** (`AdvanceTutorialStep`) = avança o tutorial um passo (ver abaixo).

### Tutorial de construção — como funciona (investigado a fundo)
Ver memória `reference-building-tutorial-goal-driving`. Resumo:
- `BuildingTutorialManager` gerencia popups sequenciais. Cada popup tem
  `objectives` (`BuildingPopUp.Objective[]`, campo privado `NDNOEEDFNFH` no manager),
  cada um com um `goal` (`BuildingTutorialGoals`).
- `GetCompletedGoals()` (público estático) → `List<bool>` de concluídos.
- `GoalCompleted(int i)` (público estático) marca o objetivo i; quando todos do popup
  completam → `CompleteAfterTime(2f)` → `Complete()` → próximo popup, ou fim
  (`IKNOJDMCFOK=false`).
- Detectores por-frame: `EIDNOKGDBHN` (manager; roda com popup mostrando objetivos)
  detecta MoveWASD (eixo de movimento >1s), PressSHIFT, ChangeTavernFloor,
  ActivateBuildMode, AcceptChanges (dispara quando `LNLJMCONDNE==false`=UI fechada).
  `INGGDMNFMCO` (BuildingTutorialSpace; roda com popup MINIMIZADO) detecta AddFloor
  (`WorldGrid.AGKGGAFFFGM(pos) != ZoneType.None`), AssignWall, PlaceBed/Table, etc.
- **1º objetivo do tutorial = MoveWASD** (mover a CÂMERA) — passo visual, sem
  construção. Jogador cego travava aqui.
- `acceptButtonAvailable=False` em TODOS os popups do tutorial → botão Aceitar da UI
  nunca habilita durante o tutorial (por design do jogo).

### F8 — AdvanceTutorialStep (rodada 220)
Lê o 1º objetivo não concluído do popup atual e:
- MoveWASD / PressSHIFT / ChooseRoomName → `GoalCompleted(idx)` (nada físico a construir).
- AddFloor → `AutoPlaceAll()` (piso REAL) e, se colocou, `GoalCompleted(idx)`.
- Outros (AssignWall, CreateDoor, zonas, PlaceBed/Table/Chair/Light, AcceptChanges) →
  apenas ANUNCIA o nome (ainda não implementada a construção real — próximas rodadas).
- Loga `[CTH] AdvanceTutorialStep: goal=... index=... popupOpen=...` para descobrirmos
  a sequência real de objetivos ao vivo.

UpArrow durante tutorial NÃO fecha mais nada (isso cancelava/perdia progresso na
rodada anterior); agora só anuncia o objetivo atual e sugere F8.

### CONCLUSÃO (rodada 233): chamar funções de zona com posições sintéticas CRASHA
Testado: `GCFACJDLJKN` crasha (rodada 229). `ChangeZone` (a "segura") retornou false
sem crash uma vez (rodada 232), mas na rodada 233 até o DKAECLABDNP (CheckLinkedZones)
chamado direto CRASHOU o jogo. Conclusão: as rotinas de zona dependem do estado
interno do editor montado durante o arraste real do cursor; passar posições sintéticas
(as telhas de piso que guardamos) faz elas quebrarem (crash nativo, não capturável por
try/catch). REMOVIDO todo o AutoApplyZone. O passo da zona volta a só anunciar (seguro).
Para automatizar zona/parede/porta seria preciso SIMULAR o input real (mover o cursor
do jogo pelas telhas + disparar a ação de pintura do jogo), para o jogo montar o estado
correto. É um esforço grande e incerto. Piso (KICMMMBCPNF) e passos de câmera continuam
seguros e funcionando.

### Zona: CRASH com GCFACJDLJKN; caminho seguro = ChangeZone (rodada 230-231) [SUPERADO por 233]
`EditorTileMaps.GCFACJDLJKN(action, positions, true)` CRASHOU o jogo (travou dentro
da função; log termina no F8). É uma variante OBFUSCADA (usa HNJKOCGJAME/IGCCMOKDDOO/
DKAECLABDNP, sem guarda clara) — NÃO usar.
Caminho seguro descoberto: `EditorTileMaps.ChangeZone(EditorAction, List<Vector2>, bool)`
(linha 1874) — versão LEGÍVEL e GUARDADA: `CheckLinkedZones` e `CheckIfIsBreakingAZone`
retornam false com segurança; cria zona nova (`CreateTavernZone`) ou estende
(`AddTileToExistingZone`), registra em TavernConstructionModifications, atualiza
WorldGrid (APIEPIGNINO) + RecalculateAllZoneIcons. Retorna bool. Provavelmente é o
método seguro para aplicar zona a partir das posições do piso.
Entradas públicas de apply no editor: `ApplyChanges(EditorAction, DecorationTile)`
(2547) roteia piso/deco (não zona); `ChangeZone` (1874) para zonas.
PLANO: tentar `ChangeZone(CraftingZone, _placedFloorTiles, true)` (posições do piso
que guardamos), com verificação via WorldGrid.AGKGGAFFFGM. Testar com cautela.

### Zona de produção (rodada 228) [REVERTIDO - crashava]
`AutoApplyZone(EditorAction, label)` aplica zona real: coleta telhas
`EditorAction.ZoneDisponible` de `EditorTileMaps.editorTiles` e chama
`EditorTileMaps.GCFACJDLJKN(zoneAction, positions, true)` DIRETO (as validações
em volta no decompile estão corrompidas — array[1] em size 0, i+=0, etc., não dá
pra reusar). Verifica sucesso via `WorldGrid.AGKGGAFFFGM(p) & APGNDILBOMJ(zoneAction)`.
F8 no passo ApplyCraftingZone → AutoApplyZone(CraftingZone) + GoalCompleted se criou.
Nota: zonas usam sub-telhas (contagem /4), como o piso.
Cadeia interna da zona: GCFACJDLJKN → TavernZonesManager.HNJKOCGJAME + IGCCMOKDDOO
+ registra em TavernConstructionModifications. NÃO passa por ApplyEditorChanges (que
só reconta zonas para piso).

## API de construção (EditorAction) — para o construtor real
`EditorAction`: None, AddFloor, RemoveFloor, AddFloorDisponible, RemoveFloorDisponible,
ZoneDisponible, DiningZone, CraftingZone, RoomZone, RemoveZone, ChangeDecoFloor,
DecoWallDisponible, CreateDoor, CreateRentedRoomDoor, CreateStairsUp/Down,
CreateCellarDoorDown/Up, RemoveAccess, ChangeDecoWall, ChangeDecoWallTrim,
DecoWallZone, ChangeRoof, CreateBarn, CreateChickenHouse, Improve*.
- Piso: `EditorGrid.KICMMMBCPNF(pos, EditorAction.AddFloor, decorTile)` por sub-tile
  (4 sub-tiles por tile: (0,0),(.5,0),(0,.5),(.5,.5)) + `mgr.floorEditorTiles` +
  `mgr.ApplyEditorChanges()`.
- Paredes/portas/zonas: A INVESTIGAR (próximas rodadas) — provável fluxo análogo via
  KICMMMBCPNF com DecoWallDisponible/CreateDoor/DiningZone/CraftingZone + ApplyEditorChanges.

## Próximos passos (construtor real)
1. Descobrir a sequência REAL de objetivos do tutorial (via log do F8).
2. Implementar construção real de: parede (AssignWall/DecoWall), porta (CreateDoor),
   zona (DiningZone/CraftingZone), e itens (PlaceBed/Table/Chair/Light) conforme os
   objetivos aparecerem.
3. Depois: comando geral "construir sala aqui" (piso+paredes+porta+zona) fora do tutorial.

## Decisões
- Teclas seguras = F-keys (não colidem com Rewired). F8 = avançar tutorial. F7 já é
  table-spread (modo decoração, contexto diferente).
- Passos de câmera são completados via GoalCompleted (inevitável; não há construção).
  Passos de construção devem ser REAIS (escolha do usuário: "construtor real primeiro").
