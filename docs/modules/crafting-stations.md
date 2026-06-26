# Módulo: Estações de crafting (forno e mesa de preparação)

> Notas de continuidade. Investigação iniciada na rodada 109 (usuário tentou
> cozinhar no tutorial). Ainda NÃO há acessibilidade dedicada implementada -
> isto é o mapeamento pra construir a feature.

## O que o jogo diz (diálogo do tutorial, lido no log)

"Aquelas duas mesas na sua frente? A da ESQUERDA, com as facas, é a MESA DE
PREPARAÇÃO DE ALIMENTOS, e a da DIREITA com a frigideira é o FORNO."
"Alguns pratos exigem ingredientes complexos que você precisa preparar
primeiro na mesa de preparação... mas vendo o que você fez com aqueles
ratos, não vou te deixar sozinho com uma faca, então vamos tentar algo mais
simples primeiro..." -> o tutorial DIRECIONA pro forno primeiro; a mesa de
preparação vem depois.

## Forno (Forno / "672 - Horno Variant")

- Abre a UI **`GameCraftingUI`** (confirmado no log: "Aberto: GameCraftingUI").
- Hierarquia dos Selectables (o que nossa navegação Tab/setas encontra):
  - **Categorias (topo)**: `ContentCraftingUI/RecipePages/` -> All, Starters,
    Vegetables, Meat, Fish, PastaRice, Broths, Desserts. (São essas que o
    usuário ouviu.)
  - **Receita selecionada (Top Panel/RecipeElementUI)**: "Output" (resultado),
    "New SlotUI Recipe" x5 (os slots de INGREDIENTES que a receita exige),
    "Favorite".
  - **Lista de receitas (Bottom Panel/Scroll View/Viewport/ListContent)**:
    "New SlotUI Recipe Selectable Element(Clone)" - cada um é UMA RECEITA
    disponível. No teste só havia 1 (tutorial). É AQUI que estão as receitas
    pra escolher, não no topo.
  - **`FuelButton`**: botão de adicionar combustível.
  - BackButton, Search Box (InputField), Clear.
- Por que "os slots embaixo pareciam vazios": provavelmente eram os slots de
  INGREDIENTE da receita (mostram o que falta), e/ou a lista de receitas tinha
  só 1 item. Os slots de ingrediente não são inventário - `DescribeSlotUI`
  pode lê-los como "Vazio". Precisa de um leitor dedicado (nome da receita,
  ingredientes necessários x possuídos, combustível).

## Mesa de preparação ("NinjaPreparationTable")

- Classe `NinjaPreparationTable : IInteractable, IHoverable, IProximity,
  ISelectable`. É interagível (tem `ActionType.Interact`). NÃO está quebrada.
- F/E "não fizeram nada" no teste -> provável causa: o tutorial ainda não
  liberou (manda fazer o forno primeiro), OU exige estar de frente / a tecla
  de interação certa. Tem campos `Bento`, `AddFoodToBento`, `timeToPrepare` -
  você monta um "bento" de ingredientes preparados.

## Implementado (rodada 110) - leitura de receitas e ingredientes

Em `KeyboardUINavigator` (descritores adicionados antes do bloco genérico de
`SlotUI`, já que `SlotUIRecipe : SlotUI`):
- **`DescribeRecipeElement(RecipeElementUI)`**: ao navegar uma entrada de
  receita (na lista de baixo), fala "Receita: {nome do prato}" (lê
  `recipeName` por reflexão; fallback = item do `outputSlot`). Resolve o
  "não vi onde apareceu a receita" - agora dá pra achar pelo nome.
- **`DescribeRecipeSlot(SlotUIRecipe)`**: nos slots de ingrediente, fala
  "{ingrediente}, precisa {N}, você tem {M}" (M via
  `PlayerInventory.NumberOfItems(itemId)`). Atende "informe o que pede e
  quanto tem; se não tiver, nome e 0".
- Reflexão: `RecipeElementUI.recipeName` e `.inputSlots` (privados).
- Diagnósticos DebugMode: "DescribeRecipeElement"/"DescribeRecipeSlot".
- NOTA: assumido que a entrada de receita da lista tem `RecipeElementUI`
  direto no Selectable e os slots de ingrediente têm `SlotUIRecipe` (e NÃO
  RecipeElementUI). Se o próximo log mostrar diferente, ajustar a checagem.

## Correção rodada 112 - elemento da lista é RecipeSlot

O diagnóstico da rodada 111 provou: o elemento "New SlotUI Recipe Selectable
Element(Clone)" NÃO tem RecipeElementUI nem SlotUIRecipe. Ele é um
**`RecipeSlot`** (GameCraftingUI monta a lista de `craftingElementPrefab`, um
RecipeSlot; `recipeSlots = List<RecipeSlot>`). `RecipeSlot.recipe` (Recipe,
público) tem `IABAKHPEOAF()` (nome), `ingredientsNeeded` (RecipeIngredient[]:
item, amount) e `output` (ItemAmount).

`KeyboardUINavigator.DescribeRecipeListEntry(RecipeSlot)` agora fala
"Receita: {nome}. Dá pra fazer / Faltam ingredientes. Precisa: {ing} {amount},
tem {owned}, ...". Isso resolve #5 (nome) E #7 (ver o que precisa antes do
Enter). Os slots de ingrediente (SlotUIRecipe) seguem em DescribeRecipeSlot.

Categorias: `DrinksTable` (Mesa de Bebidas) e `NinjaPreparationTable` (mesa de
preparação) -> "Máquinas" (CategorizePlaceable + scan próprio da prep table).

## Próximos passos (feature de acessibilidade de crafting - a fazer)

1. Leitor dedicado do `GameCraftingUI`: anunciar nome da receita ao navegar a
   lista (Bottom Panel), ingredientes necessários vs possuídos, e o estado do
   combustível (FuelButton).
2. Atalho pra pular direto pra lista de receitas (Bottom Panel) em vez de
   passar por todos os Selectables do topo.
3. Mesa de preparação: confirmar a tecla/condição de interação e o fluxo do
   Bento.
4. Combustível: o `FuelButton` abre o fluxo de adicionar lenha - mapear.

## Observação de performance (rodada 109)

`GetEmptySeatSlots` apareceu no log custando 15-20ms a cada ~1.3s ("PERF
GetEmptySeatSlots took 15ms") perto do forno - fonte real de micro-travada.
Vale revisar (talvez throttle maior ou cache) quando mexer em lag.
