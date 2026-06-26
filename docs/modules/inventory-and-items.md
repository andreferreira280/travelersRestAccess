# Módulo: Inventário, baú e uso rápido (hotbar)

> Ver convenção em `docs/modules/main-menu-and-options.md` (cabeçalho).
> Feature nova, branch `feature/inventoryAndGetItens`. Esta é a rodada
> de pesquisa (nada implementado ainda) - ver `project_status.md`.

## Pedido do usuário (2026-06-21)

1. Pegar item do baú e colocar no inventário do jogador.
2. Colocar item do inventário no baú.
3. Colocar item do inventário no uso rápido (1-9).
4. Retirar item do uso rápido, de volta pro inventário.

Navegação desejada: duas listas lado a lado quando o baú está aberto -
baú na esquerda (lista padrão ao abrir), inventário na direita, seta
direita troca de lista. Ctrl+Enter num item do baú manda ele pro
inventário (primeira posição livre); Ctrl+Enter num item do inventário
manda pro baú (primeira posição livre). Do inventário pro uso rápido:
Ctrl+1 a Ctrl+9 manda pra aquela posição específica. Do uso rápido pro
inventário: Shift+1 a Shift+9 manda de volta (primeira posição livre).

## Pesquisa no decompiled/ - arquitetura confirmada

Usei um agente de busca pra mapear o sistema (resumo abaixo), mas
**corrigi um erro real do agente antes de documentar** (ver
"Correção importante" abaixo) - reforça a regra do projeto de nunca
confiar em nome/objetivo de método sem ler o corpo de verdade.

### Hierarquia confirmada (lida diretamente, não só relatada pelo agente)

- **`Container`** (`Container.cs`) - classe base com `Slot[] slots`,
  `maxStack`, e o método central
  `AddItemInstance(int playerNum, ItemInstance item, bool playSound=false, bool sync=true)`
  - delega pra `Utils.CHMEHDFPGCI(...)`, que percorre `slots` testando
    cada um (stackável primeiro, depois vazio) - é o método certo pra
    "primeira posição livre", confirmado lendo o corpo.
- **`Inventory : Container`** e **`ActionBarInventory : Container`** -
  ambos herdam o mesmo `AddItemInstance`. `PlayerInventory` tem
  `public Inventory inventory` e `public ActionBarInventory
  actionBarInventory` (ainda não confirmei o nome do campo de
  contagem de slots da hotbar - 1 a 9 é a suposição do pedido do
  usuário, não confirmada no código ainda).
- **`Slot`** (`Slot.cs`) - tem `ItemInstance itemInstance`, `int
  Stack` (property). Métodos estáticos confirmados lendo o corpo:
  - `GHCDPAJHKOI(int playerNum, Slot from, Slot to)` - troca/exchange
    entre dois slots (usa um slot temporário internamente quando
    nenhum dos dois é vazio - confirmado lendo o corpo completo).
  - `NFBAGDKBOAD(int playerNum, Slot from, Slot to)` - move/combina
    item de `from` pra `to` (chamado internamente por `GHCDPAJHKOI`).
  - `MJLNPAEBAFF(int playerNum, Slot from, Slot to)` - merge (junta
    pilhas do mesmo item).
  - `ONIFGHNHCPP`/`FEEOFAGCONJ`/`MEONGIBAALL` - colocar item, checar
    capacidade, checar se aceita - todos confirmados existindo via
    grep, ainda não li o corpo de todos em detalhe.
- **`SlotUI`** (`SlotUI.cs`) - já usado pelo nosso próprio
  `KeyboardUINavigator.cs` (`DescribeSlotUI`) pra ler o item de um
  slot em qualquer lista genérica de UI (Tab/setas já navegam
  qualquer `Selectable`, incluindo `SlotUI`) - **ou seja, navegar e
  OUVIR o conteúdo de cada slot do baú provavelmente já funciona hoje,
  sem código novo, só falta a ação de mover.** Campo público
  confirmado: `SlotUI.IHENCGDNPBL` (tipo `Slot`, property) - nome já
  usado no nosso próprio código, não inventado pelo agente.
- **`MouseSlot.cs`** - escuta um evento global (via
  `CommonReferences.MNFMOEKMJKN()` - confirmei esse nome lendo
  `SlotUI.cs`, o agente tinha inventado um nome diferente
  `GGFJGHHHEJC` que NÃO existe no código de verdade) disparado quando
  o jogador clica/arrasta um slot com o mouse, e decide ali (shift
  pressionado? mesmo item?) se chama `MJLNPAEBAFF`, `NFBAGDKBOAD` ou
  `GHCDPAJHKOI`. Útil como referência de "qual método chamar
  quando", mas não precisamos reusar o mouse - podemos chamar os
  métodos do `Slot` diretamente a partir das nossas teclas.

### Correção importante (erro do agente de pesquisa, corrigido antes de documentar)

O agente assumiu que "o baú" é `TreasureChest.cs` - **errado,
confirmado lendo o arquivo**: `TreasureChest` não tem nenhum
`Container`/`Slot[]` - é o ponto de escavação de tesouro ÚNICO (dá os
itens uma vez e desbloqueia receitas, não é uma caixa de
armazenamento reaberta). O "Baú pequeno" que o usuário quer (já
confirmado nesta sessão como um `Placeable` comum, usado no módulo de
navegação) certamente é **`ItemContainer.cs`**
(`ItemContainer : Container, IInteractable, ISelectable, IHoverable, IProximity`)
- herda `Container` DIRETAMENTE, então já tem `slots[]` e
`AddItemInstance` prontos. Ainda não li o corpo completo de
`ItemContainer.cs` (é grande, tem uma coroutine de sincronização
online) nem confirmei como ele abre a UI (provavelmente via
`ContainerUI.cs`/`BigContainerUI.cs`/`SmallContainerUI.cs` -
encontrados no diretório, ainda não investigados).

## Implementado (2026-06-21, primeira rodada de código)

Confirmado ao vivo pelo usuário antes desta rodada: navegar os slots
de um baú aberto com Tab/setas já lê o nome de cada item - o sistema
genérico (`KeyboardUINavigator.DescribeSlotUI`) já cobre isso, sem
código novo. Faltava só a AÇÃO de mover - implementada em
`InventoryTransferHandler.cs` (novo handler, instanciado em `Main.cs`,
só roda quando alguma UI do jogo está aberta).

- **Hotbar tem 8 slots, não 9**: confirmado lendo
  `ActionBarInventory.Awake()` (`BLMADJJOAKA = new Slot[8]`) - o
  próprio usuário não tinha certeza ("acho que vai do 1 ao 9"). Tecla
  1 = índice 0 ... tecla 8 = índice 7.
- **`Container.AddItemInstance` só coloca 1 unidade por chamada**:
  confirmado lendo `Utils.CHMEHDFPGCI` até o fim - quem move uma
  pilha inteira (`AddItemInstances`, no plural) só repete essa chamada
  em loop e não informa quantos couberam de verdade. Por isso o
  handler novo faz o próprio loop (`MoveStack`), pra saber exatamente
  quantos couberam e diminuir o slot de origem só por essa quantidade
  (importante se o destino não tiver espaço pra pilha toda).
- **Container do baú aberto, sem referência fixa**: `ItemContainer`
  abre `BigContainerUI.Get(1)` ou `SmallContainerUI.Get(1)`
  (dependendo do tamanho do baú) - cada um expõe o `Container` que
  está mostrando via a propriedade pública `ALPOKDOCCGM` (confirmado
  lendo o corpo de `ContainerUI.PJDPPGMDBMC`, o método que abre a UI).
  `GetOpenChestContainer` checa `IsOpen()` dos dois e pega o que
  estiver aberto - mais seguro que adivinhar qual é qual.
- **Ctrl+Enter**: se o slot focado é do inventário do jogador, manda
  pro baú aberto (primeira posição livre); se é do baú, manda pro
  inventário. Usa `MoveStack` (loop próprio descrito acima).
- **Ctrl+1 a Ctrl+8**: do inventário pra aquele slot exato do uso
  rápido - usa `Slot.GHCDPAJHKOI` (confirmado lendo o corpo: troca os
  dois slots corretamente mesmo se o destino já tiver algo - é o
  mesmo método que o `MouseSlot` do próprio jogo usa pra
  arrastar-e-soltar itens diferentes).
- **Shift+1 a Shift+8**: daquele slot do uso rápido pro inventário
  (primeira posição livre) - usa `MoveStack` de novo.

**AINDA NÃO TESTADO AO VIVO** - build limpo, mas nada disso foi
confirmado em jogo ainda. Pontos de risco conhecidos pra essa
primeira rodada de teste:
- Não testei item não-stackável (ferramenta, equipamento) - só
  segui a lógica geral de `Slot`/`Container`.
- `ItemInstance.Equals` (usado internamente pra decidir se dois itens
  "são iguais" pra pilha) tem regras especiais pra comida com
  ingredientes diferentes - não testei esse caso.
- Não testei o que acontece se o inventário/baú estiver
  completamente cheio (espero a mensagem "Sem espaço", mas não
  confirmei ao vivo).

## Pendências futuras (não bloqueiam o teste desta rodada)

1. Ler `ItemContainer.cs` por completo (ainda não li tudo) - útil se
   aparecer algum comportamento estranho ao abrir baús de tipos
   diferentes.

## 2ª rodada (2026-06-21) - bug real achado: Ctrl+Enter não fazia NADA

**Causa raiz confirmada (não foi a lógica de mover item - essa nunca
chegou a rodar)**: `InventoryTransferHandler` lia
`EventSystem.current.currentSelectedGameObject` pra saber qual slot
estava focado - só que o próprio `KeyboardUINavigator.cs` já documenta
(no comentário da classe) que o sistema de input do jogo zera esse
valor TODO frame quando não há gamepad - é exatamente por isso que o
navegador genérico usa seu próprio "cursor virtual" em vez de confiar
nisso. Meu handler novo caiu na mesma armadilha que o próprio
navegador já tinha resolvido - sempre lia `null`, nunca achava o slot,
e como eu só logava esse caso na falha "sem baú aberto" (não nesse
caminho), ficou completamente silencioso no log.

**Corrigido**: adicionei `KeyboardUINavigator.GetCurrentSelectedGameObject()`
(expõe o item que o cursor virtual já rastreia) e troquei
`InventoryTransferHandler.Update()` pra receber esse GameObject direto
do `Main.cs`, em vez de consultar o EventSystem.

**Limitação separada, ainda não resolvida - "a lista do inventário não
aparece ao lado da do baú"**: confirmado lendo o código por que isso
acontece - `KeyboardUINavigator.GetTopWindow()` só escaneia a janela
MAIS RECENTE da pilha de janelas abertas do jogo
(`MainUI.GetCurrentOpenWindows(1).Last`), de propósito (é a mesma
defesa que evita o bug antigo de "abas misturadas" do painel
principal). Quando um baú abre, ele abre a `GameInventoryUI` por baixo
e fica como janela do topo - então hoje só dá pra navegar os slots do
BAÚ, não do inventário, enquanto o baú estiver aberto. Isso não
impede testar a direção "baú -> inventário" (não precisa focar um
slot do inventário pra isso), mas impede "inventário -> baú" e
Ctrl+1-8 (que partem de um slot do inventário). Vou investigar uma
solução (provavelmente uma tecla explícita pra alternar qual
janela o cursor escaneia, não misturar as duas listas numa escaneada
só - mistura é o que causou bug antes) numa próxima rodada, depois de
confirmar que o Ctrl+Enter em si já funciona.

## 3ª rodada (2026-06-21) - confirmado: o fix funcionou, + 2 ajustes

**Ctrl+Enter confirmado funcionando** (log: `InventoryTransfer: moved
1/1 of "Esfregão"` e `moved 2/2 of "Balde"`, ambos baú -> inventário).
Dois pontos do usuário ainda precisavam de ajuste, nenhum era bug novo:

1. **"não deu feedback"** - na verdade FALOU ("Esfregão" sozinho, sem
   sufixo "(N de M)" - confirmado no log de fala), só que um nome de
   item sozinho soa igual à navegação normal e passa despercebido como
   confirmação de uma ação. Trocado pra sempre dizer a direção:
   "{item} retirado do baú" / "{item} colocado no baú" / "{item}
   retirado do uso rápido". Deixei um comentário `TODO` exatamente no
   ponto de sucesso do `MoveStack` pra inserir um som próprio
   (`CustomSounds`, não o sistema de áudio do próprio jogo - já
   tivemos 3 tentativas sem sucesso de ouvir esse outro sistema, ver
   header de `CustomSounds.cs`) numa próxima rodada.
2. **"Ctrl+1 no esfregão não funcionou"** - não é bug, é exatamente a
   limitação já documentada acima: o esfregão tinha ido pro
   inventário, mas o cursor só conseguia focar slots do BAÚ (janela do
   topo), então `Ctrl+1` silenciosamente não achava o slot certo.

**Implementada a solução pra essa limitação** - perguntei "como o
jogador faz isso no jogo de verdade" e confirmei lendo
`GameInventoryUI.cs`/`ContainerUI.cs`/`Utils.cs` que o próprio jogo já
tem um mecanismo de "transferência automática" usado quando NÃO há
mouse disponível (`SlotUI.autoTransferEnabled`/`DoAutomaticTransfer`,
ligado por `ContainerUI`/`InventoryUI` quando um baú está aberto) -
mas ele move só 1 unidade por ativação (confirmado lendo
`Slot.MJLNPAEBAFF` e `Utils.DKHBBNHMOEB` até o fim), então não é
diretamente reaproveitável pra mover pilhas inteiras de uma vez como
o usuário quer.

O que dava pra reaproveitar diretamente: `MainUI.GetCurrentContainer(int
playerNum)` - é a MESMA chamada que `GameInventoryUI.IILKKKEDLLK` (seu
handler de auto-transferência) usa pra achar "o container aberto
agora" - troquei `GetOpenChestContainer` (que antes testava
`BigContainerUI`/`SmallContainerUI` na mão) por essa chamada única,
mais simples e mais robusta.

Pro problema de navegação em si, adicionei em
`KeyboardUINavigator.cs` uma alternância de foco: **seta
direita/esquerda**, só quando um baú E o inventário estão os dois
abertos ao mesmo tempo (senão essas teclas continuam livres pra
qualquer outra coisa, como ajuste de slider), troca qual das duas
janelas (`ContainerUI` do baú vs `GameInventoryUI`) o cursor de
navegação está escaneando (`_manualWindowOverride`, lido dentro de
`GetTopWindow()`). Isso não é a lista lado a lado "bonita" que o
usuário descreveu, mas resolve o bloqueio de verdade: agora dá pra
focar um slot do inventário enquanto o baú está aberto, então
Ctrl+1-8 e "inventário -> baú" já têm como ser testados.

**Próximo teste (com F12 ativado ANTES de entrar no jogo):**
1. Abra um baú, foque um item do baú, Ctrl+Enter - deve falar "{item}
   retirado do baú" (frase completa, não só o nome).
2. Aperte seta direita - o cursor deve passar a ler os itens do SEU
   INVENTÁRIO agora (confirme falando o nome de um item que já estava
   lá, não um item do baú).
3. Com um item do inventário focado: Ctrl+Enter (deve ir pro baú,
   "colocado no baú"), Ctrl+1 (deve ir pro uso rápido 1).
4. Seta esquerda - volta a focar os itens do baú.
5. Com o uso rápido 1 ocupado, foque ele (não dá pra focar pelo
   teclado ainda - usar mouse só pra conferir visualmente, ou usar
   Shift+1 direto) e teste Shift+1 - deve voltar pro inventário
   ("retirado do uso rápido").
6. "testei" quando terminar.

## 4ª rodada (2026-06-21) - bug real de identidade do slot + correção de engano meu

Teste real: usuário testou Ctrl+Enter/Ctrl+1-8/Shift+1-8 navegando pela
aba "Inventário" do **painel principal** (`MainPanelUI`), não por um
baú aberto. Resultado, explicado pelo log linha a linha:

1. **"control enter no inv diz retirado do baú, mas nem no baú eu
   to"**: confirmado no log (`InventoryTransfer: moved 1/1 of
   "Esfregão" (retirado do baú)`, sem nenhum baú aberto na sessão).
   Causa: a checagem usava `slotUI.container == playerInventory.inventory`,
   e o campo `SlotUI.container` vem **nulo** pros slots mostrados
   nessa aba do painel principal (diferente de quando um baú abre e
   mostra a `GameInventoryUI` por baixo, onde esse campo é
   preenchido). Resultado: o item só ficava sendo embaralhado pra
   outro slot do MESMO inventário, com uma mensagem errada. **Corrigido**:
   troquei pra checar a identidade do próprio `Slot` dentro do array
   `playerInventory.inventory.slots` (`IsPlayerInventorySlot`), que não
   depende de qual UI preencheu esse campo.
2. **"colocar no uso rápido não funciona"**: mesma causa - com
   `container` nulo, a checagem de "isso é mesmo um slot do
   inventário?" sempre falhava. Mesma correção resolve.
3. **"retirar do uso rápido diz que tá sem espaço"**: ainda sem causa
   confirmada - o slot de uso rápido 1 já tinha um item (provavelmente
   de antes desta sessão de testes, não colocado por Ctrl+1 já que
   isso sempre falhava). Adicionei log do nome do item nesse ponto de
   falha pra próxima rodada não ficar no escuro outra vez.
4. **"como eu vou colocar item no baú sem o baú estar aberto?"** -
   resposta: não dá, por design - igual ao jogo de verdade (não tem
   pra onde "arrastar" sem uma janela de baú aberta pra receber).
   Ctrl+Enter inventário->baú só funciona com um baú de fato aberto;
   agora fala "Nenhum baú aberto" em vez de ficar em silêncio nesse
   caso.

**Correção de um engano meu nesta mesma rodada (antes de ser
testado)**: tinha trocado `GetOpenChestContainer` pra usar
`MainUI.GetCurrentContainer`, achando (com base só na leitura de
`GameInventoryUI.IILKKKEDLLK`) que era a referência genérica "o
container aberto agora". Reli quem **escreve** nesse campo
(`MainUI.GBEIHIDIDAD`/`LIIGLHOFDBK`) e confirmei que só
`DrinkDispenserUI`, `Fireplace` e `OfferingStatueUI` o usam - um baú
comum (`ItemContainer`/`ContainerUI`) nunca toca nele. Revertido pra
checar `BigContainerUI`/`SmallContainerUI.IsOpen()` diretamente (a
versão da rodada 44, que estava certa). Pego antes do teste, não
chegou a ser um bug visível pro usuário.

## 5ª rodada (2026-06-21) - mensagens sem número + seta direita ainda não confirmada

Pelo log dessa rodada, linha a linha:

1. **"retirar do baú funciona e falando"** - confirmado, sem ajuste
   necessário.
2. **"esfregão no 1 e balde no 2, mas saiu trocado/confuso no
   shift"**: a causa real era outra - a mensagem de devolver
   ("retirado do uso rápido") nunca dizia QUAL número, então não tinha
   como confirmar de qual uso rápido cada coisa estava saindo. Também
   achei pelo log: quando o slot do uso rápido já estava vazio, o
   código ficava em silêncio total (nem log, nem fala) - é o motivo
   do "apertei e não falou nada" na primeira tentativa. **Corrigido**:
   agora sempre fala alguma coisa (inclusive "Uso rápido N vazio"
   quando não tem nada lá), e a mensagem de devolver já inclui o
   número ("Esfregão retirado do uso rápido 1").
3. **"tentei usar 1 e 2 sem controle/shift, não funcionou"** - isso
   não é meu código: tecla 1-8 sozinha (sem Ctrl/Shift) é o controle
   NATIVO do jogo pra selecionar/equipar aquele item do uso rápido (eu
   não capturo essa tecla sem modificador). O jogo deve estar
   selecionando sim, só que sem nenhum aviso falado - hoje não existe
   nenhum jeito de saber qual item está "na mão" sem olhar a tela.
   Isso é uma funcionalidade nova pra investigar numa rodada futura
   (anunciar qual item ficou selecionado), não um bug do que já foi
   implementado.
4. **"inventário não vai pra direita com o baú aberto"** - ainda não
   resolvido. Confirmado no log que a seta direita FOI pressionada
   várias vezes com o baú aberto, mas nenhuma tentativa de troca
   apareceu no log (nem sucesso nem falha) - sinal de que
   `HandleContainerInventorySwitch` está saindo pela checagem
   `containerWindow == null || inventoryWindow == null` sem eu
   conseguir ver o motivo exato ainda. Adicionei log detalhado nesse
   ponto (lista os tipos de TODAS as janelas abertas no momento) -
   isso vai finalmente mostrar se a `GameInventoryUI` realmente entra
   na lista de janelas abertas do jogo quando o baú abre, ou se é
   outra coisa (suspeita: o "Inventário" pode estar sendo mostrado por
   um caminho que nunca chama `GameInventoryUI.OpenUI()` de verdade -
   o mesmo motivo que deixava `SlotUI.container` nulo na aba do painel
   principal).

## 6ª rodada (2026-06-21) - 3 bugs/lacunas resolvidos pelo log

1. **Confirmada a suspeita acima**: o log mostrou
   `openWindows=[SmallContainerUI]` - a `GameInventoryUI` realmente
   NUNCA entra nessa lista quando um baú abre. Corrigido sem depender
   dela estar lá: `HandleContainerInventorySwitch` agora só exige
   achar o `ContainerUI` (o baú) na lista, e pega a `GameInventoryUI`
   direto pelo singleton (`GameInventoryUI.Get(1)`), sem checar se
   está "na lista". `GetTopWindow()` também não exige mais que o
   `_manualWindowOverride` esteja na lista - só limpa o override
   quando o baú de fato fecha.
2. **"Esfregão no 1, balde no 2, mas o shift trouxe errado"**: achei a
   causa real lendo `Slot.GHCDPAJHKOI` (o método que eu usava pra
   trocar o item do uso rápido) até o fim - seu caso especial pra
   slots `singleItem` (que os do uso rápido são) só faz alguma coisa
   quando o slot de destino já está VAZIO; se já tem algo lá, o
   método não faz nada e não avisa - mas meu código continuava
   anunciando sucesso mesmo assim (lendo o conteúdo do slot
   DEPOIS, sem saber que nada tinha mudado). Corrigido: parei de usar
   esse método e faço a troca eu mesmo, em passos explícitos (tira o
   que já estava lá primeiro, só then coloca o novo item), cada passo
   com seu próprio aviso de sucesso/falha.
3. **"Uso rápido não fala nada quando aperto"**: não tinha como, eu
   nunca tinha implementado isso. Achei o mecanismo certo do próprio
   jogo - `ActionBarInventory.OnSelectionChanged` (evento público,
   dispara toda vez que o jogo troca a seleção, mesmo sem nenhuma UI
   aberta) - e me inscrevi nele. Agora trocar de item com 1-8
   (controle nativo do jogo) anuncia "{item} selecionado".
4. **"Tentei limpar a mesa, nada aconteceu"** - ainda não investigado;
   peço pra testar de novo DEPOIS de confirmar que a seleção do uso
   rápido já está anunciando - se o item realmente nunca estava
   "na mão" (sem confirmação antes, não tinha como saber), pode ser
   que isso resolva sozinho. Se persistir, preciso de log novo
   focado especificamente nessa ação.

## 7ª rodada (2026-06-21) - 3 dos 4 itens confirmados certos, achei outro bug

Confirmado pelo log: seta direita (inventário ao lado do baú), baú <->
inventário nas duas direções, e o anúncio de seleção do uso rápido
("1 2 e afins funciona anunciar qual uso está selecionado") - todos
certos agora.

**Bug novo, achado e corrigido**: Ctrl+1/2/3 passaram a dizer "não dá
pra colocar" pra TODO slot do uso rápido, mesmo vazio. Causa
confirmada no log (`couldn't free hotbar slot 0 (freed 0, still has
"")`): os slots do uso rápido ficaram com uma referência "fantasma" -
`itemInstance` não-nulo mas com quantidade (`Stack`) zero - provável
sobra de um bug já corrigido em rodada anterior (`GHCDPAJHKOI`),
persistente porque o jogo não foi reiniciado entre as rodadas de
teste. Meu código checava só "tem item?" (`itemInstance != null`),
não "tem item DE VERDADE?" (`Stack > 0` também) - corrigido nos dois
handlers (atribuir e devolver) pra tratar isso como vazio e limpar a
referência fantasma antes de seguir.

Ainda pendente: "limpar a mesa com o esfregão" - não testado de novo
ainda porque o uso rápido estava travado nesse bug. Pedido pra
retestar.

## 8ª rodada (2026-06-21) - a "limpeza fantasma" continuava acontecendo

Usuário, com razão, ficou com cuidado de essa limpeza apagar algo que
ele tinha configurado de verdade. Confirmado pelo log que o bug
persistiu: a limpeza disparava (`hotbar slot 0 had a ghost
itemInstance (Stack 0) - clearing it`), mas o item continuava preso lá
mesmo assim (`couldn't free hotbar slot 0... still has ""`).

Causa: usei `Slot.MEODNPFJDMH()` pra limpar, mas esse método só
remove exatamente 1 unidade - como a quantidade já estava em 0, "tirar
1" não mudava nada (a lógica interna do `Slot` só reage a uma
TRANSIÇÃO pra 0, não a "já estava em 0"), então o item nunca era
removido de verdade. Corrigido pra zerar o campo direto.

**Sobre a preocupação do usuário (importante registrar)**: essa
limpeza só roda quando `Stack <= 0` - ou seja, só quando NÃO HÁ
quantidade real ali. Um item que o jogador colocou de verdade sempre
tem `Stack >= 1`, então nunca é afetado por esse código. Não há risco
de perder algo configurado de propósito.

## 9ª rodada (2026-06-21) - bug real no anúncio de seleção + correção do "8 slots"

Usuário pediu explicitamente pra eu validar pelo log sem ele
contaminar minha leitura com a própria descrição. Lido o log do
início ao fim desta vez (não só os trechos óbvios) - confirmei dois
problemas reais, nenhum inventado:

1. **"Esfregão no 1, mas ele disse que tava em outro lugar" / "balde
   não aparece no uso"** - confirmado: ao selecionar uso rápido 2
   (onde o balde tinha sido colocado), o anúncio dizia "vazio"; ao
   selecionar uso rápido 3 (vazio de verdade), o anúncio dizia
   "Esfregão" (que estava no uso 1). Causa real, lendo
   `ActionBarInventory.SetCurrentSlotSelected` até o fim: o evento
   `OnSelectionChanged` (que eu uso pra anunciar) dispara ANTES do
   jogo atualizar seu próprio índice interno de "qual slot está
   selecionado" - então quando meu código perguntava "qual item está
   selecionado agora?", a resposta vinha do slot ANTERIOR, não do
   novo. Corrigido: paro de perguntar ao jogo "qual a seleção atual"
   e uso direto o índice que o próprio evento já me entregou como
   parâmetro.
2. **"Fui até o uso 10, mas você disse que eram só 8"** - usuário
   certo, eu errei: o `Slot[8]` que eu tinha lido em
   `ActionBarInventory.Awake` (`BLMADJJOAKA`) é um array espelho usado
   só no modo coop local pro jogador 2, não é o uso rápido de verdade
   - confirmado ao vivo que dá pra selecionar até "Uso rápido 10" sem
   erro, então o array real tem pelo menos 10. Ampliei minhas teclas
   Ctrl+N/Shift+N pra cobrir 1-9 e 0 (10 no total), com checagem de
   limite contra o tamanho real do array (não assumindo mais um
   número fixo).

**Ainda não confirmado**: "shift 1 continua dizendo vazio mesmo depois
de eu ter colocado o esfregão lá" e "uso do esfregão não funciona" -
não achei a causa ainda; pode ser efeito colateral do bug acima (a
navegação nativa pelo uso rápido, que o usuário fez bastante entre
colocar e tentar retirar, passa pelo MESMO código que tinha o bug) ou
outra coisa. Pedido reteste limpo: colocar e retirar em sequência,
sem navegar pelos outros slots no meio, pra isolar.

## 10ª rodada (2026-06-21) - mesmo no teste limpo, o item desaparece

Usuário fez o teste isolado pedido (rodada 9). Confirmado pelo log:
não era efeito de navegação - Ctrl+1 atribui esfregão ao uso 1 (meu
próprio log confirma `assigned "Esfregão" ... to hotbar slot 0`), e
**menos de 1.3s depois, sem nenhuma outra tecla no meio**, Shift+1 já
lê o mesmo slot como vazio. Isso não é o bug já corrigido
(referência fantasma) nem o bug do anúncio (índice lido antes da
hora) - é um terceiro problema, ainda sem causa confirmada.

Hipótese ainda não testada: o array `ActionBarInventory.slots` (ou os
próprios objetos `Slot` dentro dele) pode estar sendo recriado/
reconstruído por algum refresh da UI (a aba "Inventário" do painel
principal já demonstrou comportamento estranho antes - `SlotUI.container`
nulo, `GameInventoryUI` fora da lista de janelas) - se isso acontecer,
eu escreveria no objeto `Slot` antigo e a leitura seguinte pegaria
um objeto novo e vazio, sem que nada tenha "apagado" nada de verdade.

Em vez de tentar mais um conserto às cegas, adicionei log de
diagnóstico que identifica o objeto exato (`slotObj`/`containerObj`/
`arrayObj`, via hash code) tanto na atribuição quanto na leitura -
isso vai mostrar se é o MESMO slot (significa que algo realmente
limpou) ou um slot DIFERENTE (significa que o array foi reconstruído
por baixo). Pedido reteste: Ctrl+1 seguido imediatamente de Shift+1,
de novo, só isso.

**Sobre "uso do esfregão não funciona"**: como o item nunca permanece
no uso rápido por mais de 1-2 segundos, é bem provável que essa falha
seja CONSEQUÊNCIA do mesmo bug (não dá pra usar uma ferramenta que já
não está mais lá quando você tenta), não um quarto problema separado
- só vou investigar isso à parte depois de resolver o desaparecimento.

## 11ª rodada (2026-06-21) - causa raiz achada: não era nem meu código

O log de diagnóstico (hash code do objeto `Slot`) confirmou: era
literalmente o MESMO objeto `Slot` tanto na hora de atribuir quanto na
hora de ler de volta vazio - descartando a hipótese de array
reconstruído. Isso apontava pra algo realmente limpando o slot.

Achei lendo `ActionBarUI.cs` (classe base da UI do uso rápido, não
mexida até agora): seu próprio `Update()` reage a QUALQUER tecla
("GetAnyButtonDown()") e processa as ações nativas "ActionBar1" até
"ActionBar10" - que SÃO as teclas 1 a 0 puras, sem se importar se
Ctrl ou Shift estão pressionados também. Cada uma chama
`SwapSlotsInput`, que procura por um `SlotUI` embaixo do CURSOR DO
MOUSE (não tem nada a ver com teclado) e troca o conteúdo dele com o
uso rápido correspondente.

Ou seja: minha tecla Ctrl+1 SEMPRE também disparava essa troca nativa
baseada na posição do mouse, no MESMO frame - o jogo trocava o uso
rápido 1 com o que estivesse embaixo do cursor (provavelmente vazio,
ou outra coisa qualquer, dependendo de onde o mouse ficou parado),
desfazendo o que eu tinha acabado de fazer. Isso explica o
"desaparece sem eu apertar mais nada" - na verdade ERA a mesma tecla,
duas reações ao mesmo tempo.

**Corrigido com um patch Harmony novo** (`HotbarSwapPatch.cs`, mesmo
padrão de `SpaceClosePatch.cs`): bloqueia essa troca nativa
especificamente quando Ctrl ou Shift estão pressionados (jogo com
mouse normal nunca usa essas teclas junto - é seguro). Seleção pura
"1-8/9/0" sem modificador continua livre, sem mudança.

**Sobre "uso do esfregão não funciona"**: ainda não confirmado se
resolve com isso (a hipótese era que o item nunca ficava parado o
suficiente pra usar) - pedido reteste depois desse patch. As teclas
testadas (Q, F, E) e o clique do mouse não são confirmadas como a
tecla certa de "usar" - o jogo usa Rewired (remapeável), não dá pra
confirmar pelo código só; se persistir depois do reteste, preciso
investigar a tecla certa separadamente.

## 12ª rodada (2026-06-21) - patch do uso rápido confirmado certo + achada a tecla de "Limpar"

Usuário confirmou: "agora ele seta direito" - o `HotbarSwapPatch` da
rodada anterior resolveu o desaparecimento. Sobre "limpar" - ainda
incerto se funcionou, e perguntou se precisa estar virado pro lado
certo ou mexer o mouse.

**Achado, sem precisar perguntar nada**: a dica visual do próprio jogo
(`"[E] Limpar"`, capturada por `DialogueAnnouncer.ScanAndAnnounceText`)
sempre teve a tecla certa - só que era removida (`ActionPromptPattern`)
antes de anunciar, sobrando só "Limpar" sem dizer qual tecla. Corrigido
em `DialogueAnnouncer.cs` (`ActionPromptKeyPattern` nova) pra incluir
"(tecla X)" no anúncio - ex: "Próximo: Mesa grande: Limpar (tecla E)".
Resolve pra QUALQUER interação cuja tecla não seja óbvia, não só essa.

Como é o mesmo mecanismo de proximidade já usado pra portas/baús
(que já funcionam sem precisar mirar com mouse), a pergunta sobre
"virado pro lado certo"/"mexer o mouse" provavelmente não se aplica -
mas só dá pra confirmar com o teste real, agora com a tecla certa
sabida de antemão.

Sinal pra saber se "Limpar" funcionou de verdade: o anúncio "Próximo:
Mesa grande: Limpar" deve PARAR de aparecer quando voltar pro mesmo
lugar (a mesa limpa não oferece mais essa ação).

## 13ª rodada (2026-06-21) - mancha e mesa funcionam de jeitos DIFERENTES (achado lendo o código, não é bug)

Usuário confirmou tecla E certa, conseguiu limpar 1 mancha, mas não a
mesa, e notou que manchas não são anunciadas ao passar perto.

**Correção de um erro meu nesta mesma rodada**: a primeira leitura
(`Mop.cs` isolado) me fez concluir que mancha limpa "automaticamente,
sem apertar nada" - errado. O código que realmente decide isso é
`UseObject.Update()`, que eu não tinha lido ainda, e separa dois
caminhos pela TAG do objeto focado por proximidade:
- **Tag `"FloorDirt"` (mancha no chão)**: precisa SEGURAR a tecla
  ligada à ação `"Interact"` (a mesma de portas/baús) enquanto o jogo
  considera a mancha como foco - não é automático, e não é só um
  toque, é segurar.
- **Qualquer outro alvo, inclusive `Table` (mesa)**: passa por um
  caminho TOTALMENTE diferente, ligado à ação `"Use"` - NÃO
  `"Interact"`. Isso explica exatamente o relato do usuário: apertar E
  "como faço com as portas" não faz nada na mesa, porque a mesa não
  escuta essa ação, escuta outra.

Não consigo confirmar pelo código só qual tecla física está amarrada
a `"Use"` (Rewired é remapeável, dados de binding não ficam no
C# decompilado). Dois caminhos pra descobrir:
1. Usuário pode ir em Opções > Atribuir Teclas (já navegável, ver
   `main-menu-and-options.md`) e procurar a entrada "Usar"/"Use" pra
   ouvir a tecla de verdade.
2. Tentar segurar o BOTÃO ESQUERDO DO MOUSE perto da mesa com o
   esfregão selecionado, como teste rápido (padrão comum nesse
   gênero de jogo pra ação "Usar", mas não confirmado pelo código).

Sobre manchas não serem anunciadas ao passar perto: como a ação real
é "segurar Interact", o jogo só mostra a dica visual "[E] ..." quando
o foco de proximidade já está na mancha - se isso não está disparando
de forma confiável, é uma questão de DETECÇÃO de proximidade da
mancha (não investigado ainda), separada da mesa.

## 14ª rodada (2026-06-21) - log de diagnóstico adicionado (sem fixar nada ainda)

Usuário relatou: "Atribuir Teclas" não lê nada útil (bate com a
pendência já conhecida em `main-menu-and-options.md` sobre
`KeybindElementKeyboard(Clone)` sem nome); zero manchas anunciadas
desta vez; o personagem as vezes vira sozinho ao apertar E mas nada
limpa; pediu pra investigar a fundo com logs em vez de mais teoria.

Lendo `Table.MouseHold` direto (o método real disparado pela ação
"Use" - não confirmei isso por teoria, é o método que implementa
`IInteractable.MouseHold`), achei mais detalhe: com o esfregão
selecionado, exige `PlayerInputs.GHKOCEOEKGK` (tempo segurando "Use")
>= 0.3s ANTES de fazer qualquer coisa - ou seja, segurar por MENOS de
0.3s não conta (ao contrário do que eu tinha dito errado na rodada
12, sobre "segurar bloqueia" - é o oposto: precisa segurar pelo MENOS
esse tempo). Depois disso, ainda checa se existe uma posição livre
pra limpar (`IsAnyPositionToCleanAvailable`) e MOVE o personagem até
lá (`GoToPosition`) antes de acumular progresso de limpeza
(`doWork.AddWorkDone`).

Em vez de continuar só lendo código, adicionei `CleaningDebugPatch.cs`
(novo patch Harmony, só log, não muda nada do jogo) que captura em
tempo real, com F12 ativado:
- Cada chamada de `Table.MouseHold` (tempo de "Use" segurado, sujeira
  atual, progresso de trabalho, resultado).
- Cada chamada de `FloorDirt.Clean` (progresso de trabalho,
  resultado).
- Quando uma mancha é destruída de verdade (`DestroyFloorDirt`) ou
  uma mesa chega a sujeira 0 (`SetDirtiness`) - confirma quando
  "sair da categoria de itens" deveria acontecer.
- O foco de proximidade atual (`InputByProximityManager`), uma linha
  só quando MUDA - mostra se o jogo alguma vez focou numa mancha ou
  mesa enquanto o usuário andava perto.

Build limpo. Pedido reteste com F12 ativado ANTES de entrar no jogo,
andando perto de manchas e tentando segurar a tecla de "Usar" na
mesa - vou ler o log eu mesmo depois, igual sempre fiz.

## 15ª rodada (2026-06-21) - log lido: mancha precisa segurar SEM INTERROMPER; mesa nunca recebeu o sinal certo nenhuma vez

Usuário relatou "consegui limpar uma coisa só, mas não sei o que foi"
e "o resto nada funcionou, com a tecla E, mas acho que teve uma
posição específica em relação à minha posição e à posição da coisa".
Li o log direto (`CleaningDebugPatch`) em vez de pedir mais
descrição.

**A única limpeza com sucesso foi uma MANCHA NO CHÃO, não a mesa**
(`FloorDirt DESTROYED (cleaned) "FloorDirt"` às 21:38:10, depois de
`workDone` subir 0.0 → 0.4 → 0.9 → 1.3 → 1.7 → 2.2 → 2.6 → 3.0/3.0
sem nenhum reset no meio). Em TODAS as outras tentativas de mancha, o
`workDone` reseta pra 0.0 antes de chegar a 3.0 (ex: 0.0→0.4→0.9→1.3→
1.7→ **reset pra 0.0** de novo) - ou seja, não é posição, é precisar
segurar E SEM SOLTAR (nem por um instante) por tempo suficiente
(~3-4s reais) até completar - qualquer interrupção (soltar a tecla,
sair do alcance) zera o progresso e tem que começar de novo.

**Sobre a mesa: `Table.MouseHold` (o método real por trás da ação
"Usar") apareceu ZERO vezes no log inteiro**, mesmo com o foco de
proximidade pousando em "Mesa Grande" várias vezes durante o teste.
Conferido tecla por tecla nesses momentos: o usuário sempre apertava
E (nunca o suficiente perto da mesa antes do foco mudar pra outra
coisa) ou teclas de movimento - nunca um clique/hold de mouse
sustentado enquanto o foco realmente estava na mesa. Duas vezes
apareceu `KeyDown: Mouse0` no log, mas em ambas o foco no momento era
numa mancha, não na mesa.

**Conclusão**: a dica "(tecla E)" que anuncio pra mesa está
ENGANOSA - o jogo mostra esse texto, mas o mecanismo real
(`Table.MouseHold`) nunca foi disparado por E nenhuma vez em todo o
teste. Ainda não sei a tecla/botão certo de "Usar" - só confirmei
que NÃO é E e que mouse clique simples (sem segurar) também não
chegou a ser testado de fato na mesa.

Próximo teste: perto da mesa, esperar o anúncio "Próximo: Mesa
grande: Limpar", e em seguida SEGURAR o botão ESQUERDO DO MOUSE sem
soltar por uns 4-5 segundos, parado, sem se mover. Vou procurar
`Table.MouseHold` no log depois pra confirmar se disparou ou não -
sinal inequívoco, independente do que aparecer na tela.

## 16ª rodada (2026-06-21) - mesa CONFIRMADA limpa segurando o mouse; manchas movidas pra investigação de navegação

Log do reteste confirmado: **`Table.MouseHold` disparou e a mesa foi
limpa de verdade** (`useHoldTime` subindo 0.00→10.00s,
`dirtiness` caindo 2000→0, `result=True` no final) - segurando o
botão esquerdo do mouse parado funcionou. Mesa resolvida.

Manchas continuaram sem foco nenhuma vez nesse log (zero
`proximity focus -> FloorDirt` da sessão toda) - usuário relatou
"continua sem anunciar manchas", "rotas muito imprecisas", e pediu
um anúncio próprio mesmo sem o jogo mostrar nada, "igual a mesa".
Como isso é sobre achar/navegar até a mancha (não sobre a mecânica de
limpar, já resolvida), o trabalho foi feito em
`WorldNavigationHandler.cs` - ver `world-object-navigation.md`,
"34ª rodada": anúncio próprio ao focar numa mancha, posição de
aproximação (`GetApproachPosition`) em vez do centro exato pra rota
do Home, numeração quando há mais de uma por perto.

## 17ª rodada (2026-06-21) - som de "objetivo concluído" + anúncio da missão atualizada

Usuário confirmou manchas anunciadas e mesa limpa funcionando. Dois
pedidos novos: "quando limpei a mesa, a missão foi atualizada, mas
ele não anunciou nada" e "não tem som para quando a mancha do chão é
limpa, coloca aí mesmo que o jogo não coloque".

Achado lendo `NewTutorialManager.cs`: existe
`ObjectiveCompleted(int, bool)`, que marca o objetivo como concluído
(ícone de check) e toca `PlayObjectivesCompletedSound()` - só que
esse som usa `MultiAudioManager`, o MESMO sistema de áudio do jogo já
confirmado nesta sessão como não confiável pra nós (igual o som de
passos, que nunca funcionou). Então mesmo quando a "missão atualiza",
o som pode estar tocando só silenciosamente. O TEXTO do objetivo
(`objectives[i].textMesh`) não muda quando só o ícone de check vira -
por isso o `DialogueAnnouncer` (que só re-anuncia texto que MUDOU)
nunca pegava isso.

**Corrigido**: novo patch Harmony em `TutorialTracePatch.cs`
(`ObjectiveCompletedPostfix`) que lê o texto do objetivo concluído e
anuncia "Objetivo concluído: {texto}" + toca nosso próprio som (não
o do jogo). Mesmo clipe (`limpou.wav`, novo) reaproveitado em
`CleaningDebugPatch.FloorDirtDestroyedPostfix` pra tocar quando uma
mancha no chão é limpa - usuário confirmou que um som curto (~2s) já
serve, mesmo reaproveitado pros dois casos.

**Pendente do usuário**: precisa colocar um arquivo `limpou.wav` na
raiz do projeto (mesmo esquema dos outros sons - `parede.wav`,
`itens.wav`, etc.) pra esses dois sons funcionarem.

Build limpo.

## 18ª rodada (2026-06-21) - bug real achado: limpou.wav nunca seria copiado mesmo se o usuário colocasse o arquivo

Usuário reportou "mancha limpada ou limpando ainda tá sem som". Antes
de pedir pra ele conferir de novo, conferi eu mesmo se o arquivo
existia - não existia ainda (esperado, pendente dele). MAS achei um
bug real meu enquanto conferia: o `.csproj` tem uma lista FIXA de
nomes de arquivo `.wav` que são copiados pra pasta `Mods` do jogo
depois do build - eu adicionei `limpou.wav` no código
(`CustomSounds.cs`) na rodada passada, mas esqueci de adicionar esse
mesmo nome na lista do `.csproj`. Resultado: mesmo que o usuário
colocasse o arquivo certinho, ele NUNCA seria copiado pra onde o jogo
de fato carrega os sons - o som nunca tocaria, sem nenhum erro
visível. Corrigido (`limpou.wav` adicionado à lista, com
`ContinueOnError="WarnAndContinue"` pra não quebrar o build enquanto
o arquivo ainda não existe - confirmado com build de teste, dá só um
aviso, não erro).

Usuário confirmou que adicionou o `limpou.wav` de verdade (build
re-testado, arquivo confirmado copiado pra pasta `Mods` do jogo desta
vez) e pediu volume 100% pro som dessa rodada especificamente, não os
60% padrão usados por todos os outros sons. Adicionado um multiplicador
(`1/Volume`) só pra essa chamada (`CustomSounds.PlayObjectiveCompleted`),
sem mudar o volume base dos outros sons.

## 19ª rodada (2026-06-22) - pesquisa: como e onde "itens recebidos" são posicionados

Usuário pediu pra investigar a fundo como/onde "itens recebidos" são
posicionados, deixando claro que a suposição dele ("são posicionados a
partir do inventário") era só um palpite, não algo confirmado. Usei um
agente de busca pra mapear o terreno rapidamente, mas **corrigi várias
afirmações erradas/incompletas dele lendo o código de verdade antes de
documentar** - mesma regra de sempre: nome de método e resumo de
agente não provam comportamento, o corpo do método sim.

### Resposta direta: depende de QUAL loja, não existe uma regra única

**A suposição do usuário estava parcialmente certa - mas condicional,
não universal.** Achei a peça central em `ShopsManager.cs` (método
`CJJGKCKAFCG`, linhas 87-119): quando um pedido de loja (`ShopOrder` -
`ShopOrder.cs`, um struct simples com `playerNum`, `items`,
`deliveryHour`, `shop`) atinge sua hora de entrega
(`orders[num].deliveryHour <= WorldTime...hour`, checado a cada
segundo via `WorldTime.OnTickTime1Second`), cada item do pedido segue
um de dois caminhos, decidido por um campo fixo da loja:

```
if (shop.sendToDeliveryChest)
    // vai pro Baú de Entregas (DeliveryChest) - um baú ÚNICO, FIXO
    // num lugar específico do mapa, é um Container normal com slots.
else
    // vai DIRETO pro PlayerInventory do jogador que fez o pedido
    // (PlayerInventory.OGKNJNINGMH(...).AINJENENGFG(...))
```

`Shop.sendToDeliveryChest` (`Shop.cs:17`) é um `bool` público num
`ScriptableObject` - ou seja, é uma configuração FIXA por loja, feita
pelos desenvolvedores no editor do jogo, não algo que o jogador
escolhe por pedido. Cada loja do jogo (mercado geral, açougue, etc.)
já vem decidida: OU os pedidos dela sempre vão pro baú de entregas, OU
sempre vão direto pro inventário do jogador.

**Conclusão pra responder o usuário**: não existe uma resposta única -
pra ALGUMAS lojas, sim, os itens vão direto pro inventário (suposição
dele confirmada NESSE caso); pra OUTRAS, vão pro baú de entregas (um
objeto físico no mapa, posição fixa). Pra saber qual caminho uma loja
específica usa, precisaria olhar o asset `Shop` daquela loja
específica no editor do jogo (não dá pra saber só pelo código).

### O Baú de Entregas (`DeliveryChest.cs`, 805 linhas)

- Singleton (`static DeliveryChest GGFJGHHHEJC`, definido em `Awake()`
  - linha 275-279) - um único baú fixo no mapa, herda de
  `ItemContainer` (tem `Slot[] slots` normal, igual qualquer baú).
- Dezenas de métodos quase-duplicados (nomes ofuscados como
  `DJDMGBKKAMK`, `BPBDECHBLBO`, `CANCHBINJNE`, etc.) seguem o mesmo
  padrão: tentam achar uma vaga livre/empilhável nos slots do baú
  (mesma lógica de "primeira posição livre" já documentada acima pra
  `Container`); se NENHUMA vaga livre existe (baú cheio), o item
  "vaza" pro mundo como item largado (`DroppedItem`), na posição
  `GGFJGHHHEJC.transform.position + Vector3.down` (ou seja, embaixo
  do próprio baú) - confirmado em várias dessas funções (ex: linha
  188, 219, 379).
- **Achado um segundo uso do baú, sem relação com pedidos de loja**:
  `DroppedItem.cs`, método `PFLBPMIEKGF` (linha 676-698) - esse nome
  ofuscado se repete em DEZENAS de outras classes não relacionadas
  (`Camera2D`, `WorldTime`, vários NPCs) com a MESMA assinatura
  (`private void`, sem parâmetros) - confirma que é o método especial
  `OnDestroy()` do Unity (chamado automaticamente quando o objeto é
  destruído), não algo que nosso código ou o jogo chama diretamente.
  Quando um item largado no mundo está sendo destruído E o modo de
  construção da taverna está aberto E o item está numa "zona de
  entrega" (`Utils.EJPFCKFEMJF`, `Utils.cs:545-548` - simplesmente
  `posição.y > 800`, uma faixa específica do mapa), o item é salvo no
  baú de entregas em vez de simplesmente desaparecer. Não é o
  mecanismo PRINCIPAL de "como itens chegam" - é uma rede de segurança
  pra não perder itens de entrega que acabem caindo nessa zona.
- **Corrigido um engano do agente de busca**: ele citou duas chamadas
  em `GameManager.cs` (linhas ~480-489 e ~920-932) como sendo
  "depósito de construção" e "carregamento de save" - **errado,
  conferido lendo o contexto completo**: as duas são sobre o Jogador 2
  entrando/saindo do modo cooperativo local (`GiveChestPlayer2()` /
  `OnPlayer2Joined()` na primeira; consolidar itens do Jogador 2 de
  volta pro Jogador 1 na segunda) - o baú de entregas aparece ali só
  como destino de SOBRA (itens que não cabem na transferência), não
  como parte de "como pedidos chegam".

### Item largado manualmente vs. item pego do chão (`Pickupable.cs`, 206 linhas, lido por completo)

Mecanismo SEPARADO do baú de entregas - é sobre o jogador andar até um
item já largado no mundo e interagir (tecla/botão de interação, várias
variantes pra mouse/teclado/gamepad, todas confirmadas lendo o corpo):
sempre vai direto pro `PlayerInventory` do jogador que pegou
(`PlayerInventory.GetPlayer(...).AddItem(...)` / `.OJDGOADOCMG(...)`,
ex: linha 87, 113, 169) - nunca pro baú de entregas. Faz sentido: é
"pegar do chão", diferente de "receber um pedido de loja".

### Posição/física de um item largado (`DroppedItem.cs`)

- Posição inicial vem de um parâmetro (`SpawnDroppedItem`, linha 700) -
  sempre um pouco deslocada pra baixo da posição passada
  (`PHCBNGOILFJ`, linhas 739-755: `posição -= Vector3.up * 0.227~0.40`,
  o valor exato varia se foi um jogador específico que largou ou o
  sistema).
- **Corrigi uma afirmação errada do agente**: ele disse "sem
  aleatoriedade nem espalhamento" - **falso, conferido em
  `DroppedItemFollowPlayer.cs`, método `InitialForce` (linhas
  242-260)**: quando o item é largado pelo SISTEMA (não por um jogador
  específico), recebe um pequeno empurrão numa direção ALEATÓRIA
  (`UnityEngine.Random.Range(-1f,1f)` nos eixos X/Y, normalizado,
  multiplicado por `maxSpeed * 0.4f`) antes de assentar - ou seja, há
  sim um pequeno "espalhamento" físico, não é uma posição 100% fixa.

## Arquivos relevantes (decompiled/)

- `Container.cs`, `Slot.cs`, `ItemInstance.cs` - núcleo confirmado.
- `ItemContainer.cs` - o "baú" de verdade (ainda não lido por completo).
- `SlotUI.cs` - UI de slot individual, já usada por
  `KeyboardUINavigator.cs`.
- `ContainerUI.cs`, `BigContainerUI.cs`, `SmallContainerUI.cs` -
  candidatos a UI do baú, ainda não investigados.
- `ActionBarInventory.cs`, `PlayerInventory.cs` - hotbar e inventário
  do jogador.
- `MouseSlot.cs` - referência de qual método chamar pra cada ação
  (mover, trocar, juntar).
- `TreasureChest.cs` - **NÃO é o baú** (ponto de escavação único) -
  registrado aqui só pra não repetir essa confusão numa rodada futura.
- `GameInventoryUI.cs` (`IILKKKEDLLK`), `Utils.cs` (`DKHBBNHMOEB`,
  `BMPHEAFDFPI`), `MainUI.cs` (`GetCurrentContainer`) - mecanismo real
  de "transferência automática" do próprio jogo (1 unidade por
  ativação) - confirmado, mas não reaproveitado diretamente (granularidade
  diferente do que o usuário pediu); só `GetCurrentContainer` foi
  reaproveitado.
- `ShopsManager.cs` (`CJJGKCKAFCG`) + `ShopOrder.cs` + `Shop.cs`
  (`sendToDeliveryChest`) - decide se um pedido de loja vai pro baú de
  entregas ou direto pro inventário do jogador (19ª rodada).
- `DeliveryChest.cs` - baú de entregas (singleton, `Container` normal).
- `Pickupable.cs` - pegar item largado do mundo, sempre vai pro
  `PlayerInventory` (mecanismo separado do baú de entregas).
- `DroppedItem.cs` + `DroppedItemFollowPlayer.cs` (`InitialForce`) -
  posição/física de item largado no mundo, inclusive o pequeno
  espalhamento aleatório ao ser largado pelo sistema.

## Quantidade nos slots (rodadas 100-101)

- `KeyboardUINavigator.DescribeSlotUI` lê `Slot.Stack` (campo público) e
  anuncia "nome, N" quando N>1 (ex. "Vela, 10"); item único só o nome.
- Uso rápido (`InventoryTransferHandler`): `OnHotbarSelectionChanged`
  fala "nome, N" ao selecionar; `PollSelectedHotbarStack` (por frame, via
  `EnsureHotbarSelectionAnnouncer`) acompanha o slot selecionado e anuncia
  a contagem ao DIMINUIR ("9", "8"... "acabou"). Só anuncia queda, não
  aumento (reabastecer já tem outros anúncios).

## Troca de lista em estações (rodada 115)

`KeyboardUINavigator.HandleContainerInventorySwitch` (seta direita/esquerda)
troca o foco entre os slots da estação e o `GameInventoryUI` do jogador.
Antes só reconhecia `ContainerUI` (baú, BigContainerUI/mesa de menu). A
torneira de bebidas é `DrinkDispenserUI : UIWindow` (NÃO ContainerUI) e era
pulada, embora abra o GameInventoryUI junto. Agora `IsStationWindow(w) =>
w is ContainerUI || w is DrinkDispenserUI` cobre os dois. Ao trocar, fala
"Inventário"/"Estação" e "Inventário vazio, nada pra adicionar" quando o
inventário não tem item válido (`CountInventoryItems`).
