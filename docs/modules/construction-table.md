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
