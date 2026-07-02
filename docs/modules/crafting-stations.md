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

## PESQUISA: navegar grids do forno (modificador + quantidade) — rodada 134S

Pedido do usuário: poder alterar a QUANTIDADE e os INGREDIENTES (modificadores)
de uma receita pelo teclado. Afeta TODAS as receitas. Bloqueia o tutorial do
bife grelhado (precisa colocar carne bovina no modificador e/ou reduzir o lote
de 20 pra 4).

### Raiz do problema (confirmado por log + decompiled)
- O KeyboardUINavigator só varre `Selectable` (KeyboardUINavigator.cs ~linha 762:
  `root.GetComponentsInChildren<Selectable>()`). Os itens dos GRIDS do forno são
  `SlotUI` (UIBehaviour), NÃO `Selectable` -> nunca entram na lista navegável.
- Confirmado no log: no painel de modificadores os únicos itens navegáveis são
  Output, Name (InputField), Mod1, AcceptButton, CancelButton. A grade de carnes
  NÃO aparece.
- "Mod1" e "Output" SÃO navegáveis (têm Selectable), mas dar clique neles
  (pointerDown+Up+click, já implementado no Activate) NÃO resolve: a seleção do
  modificador vem de clicar uma carne na GRADE, e a quantidade muda por outro
  mecanismo.

### Arquitetura (decompiled)
- `ModifierUI : Container` — slots privados `outputSlotUI`, `input1UI/2/3` (SlotUI),
  `modiferRequirementsArray` (ModiferRequirement[]). Instâncias: `ModifierUI.instances[3]`.
- Escolha de item de modificador passa por `ChooseSlotUI` / `ChooseItemUI` — ambos
  fazem `obj.OnOptionSlotClicked += IMAOMELGPHH` (ChooseItemUI.cs:412, ChooseSlotUI.cs:1194).
  Ou seja: clicar um SlotUIRecipe de opção dispara `OnOptionSlotClicked(int, Slot, int)`.
- `SlotUIRecipe : SlotUI` tem `public Action<int, Slot, int> OnOptionSlotClicked`.
- `SlotUI : UIBehaviour, ISubmitHandler, IPointerDownHandler, IPointerUpHandler, ...`
  (NÃO IPointerClickHandler). Clique real = pointerDown + pointerUp.
- Quantidade: `Recipe.output` (ItemAmount) e `craftingList[i].output.amount`;
  no slot é `IHENCGDNPBL.Stack` / método `OCJOJKJPDNO(amount)` (GameCraftingUI.cs
  116,273). Setter por input do usuário (scroll/drag/botão) ainda NÃO confirmado.
- `Recipe.ingredientsNeeded[]` (RecipeIngredient: item, amount, mod).
- Contagem no inventário: `CraftingInventory.KCCBHHEGEHG(1, item)` (já usado no
  leitor de ingredientes do KeyboardUINavigator.DescribeRecipeIngredients).

### Plano de implementação (próxima passada focada)
1. NavItem: adicionar `public GameObject SlotObject;` (Anchor fica null nesses).
2. No scan, QUANDO o forno/modificador está aberto, achar os SlotUI dos grids
   (ModifierUI.input slots + a grade de opções/ChooseSlotUI) e adicioná-los como
   NavItem (SlotObject). Escopo restrito ao crafting pra não afetar outros menus.
3. Guardar TODAS as ~8 referências a `.Anchor` contra null (GetCurrentSelectedGameObject
   linha 152; preservação 229/389/394/396/398; Activate 462; AnnounceCurrent 1337).
4. AnnounceCurrent(SlotObject): ler nome do item + Stack do slot.
5. Activate(SlotObject): pointerDown+Up+click (já temos o helper).
6. Quantidade: descobrir o setter (testar se pointerDown+Up no Output muda; se não,
   procurar IScrollHandler/botões +/-; pior caso, modo "Enter -> digitar número"
   chamando o setter de Stack/amount).
7. RISCO: mudança no núcleo do navegador (usado por todos os menus). Fazer aditivo,
   escopado ao crafting, com guardas de null, e build/teste cuidadoso.

### Pendentes relacionados
- [185] ler "Vender: X" (valor de venda) no forno.
- [186] digitar quantidade direto.
- [59] ESC não fecha o forno enquanto a fala da Mai está aberta.
