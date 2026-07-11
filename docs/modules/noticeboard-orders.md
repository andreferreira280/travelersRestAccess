# Módulo: Quadro de Pedidos (Notice Board Orders)

Handling: **`KeyboardUINavigator.cs`** (navegação + descrição + ativação por Enter,
~linha 2650). NÃO criar handler separado — houve um NoticeBoardHandler duplicado
que causou anúncio dobrado e foi REMOVIDO.

## Problema (rodadas ~221-223)
Usuário: "mesmo tendo itens compatíveis no inventário, não permite entregar."
Causa raiz: no jogo base a entrega é drag-drop no slot do pedido (só mouse). O
`KeyboardUINavigator` já navegava/descrevia os pedidos e aceitava, mas para pedido
ATUAL mandava fazer um "Right + Ctrl+Enter" em dois passos (confuso). E uma
tentativa de auto-preencher varria só a hotbar → item no inventário principal dava
"não encontrei".

## O que é a CraftingInventory (armazém de produção) — resposta ao usuário
É um agregado VIRTUAL, não uma sala. `CraftingInventory.DDOMGCBEFOK` =
todos os baús/barris/dispensers colocados na taverna + o inventário do próprio
jogador. As operações varrem o inventário do jogador PRIMEIRO, depois os
contêineres. Ou seja: conta o seu inventário + os baús em qualquer área da taverna.

## Como funciona (investigado)
Ver memória `reference-order-delivery`. Resumo:
- `OrderQuestUI.Get(1)`, `currentOrderQuestElements[]` (aceitos),
  `availableOrderQuestElements[]` (disponíveis).
- `OrderQuestElementUI`: `.num`, `.currentQuestElement`, `.AINAHCLIAFF`
  (CraftItemTypeQuest: `.requiredAmount`, `.reward.reputationPoints`,
  `.INKJOLLEBGI()`=Item[]), `.slotUI.IHENCGDNPBL` (Slot do pedido), `.button`.
- `TransferItemsFromSlot(1, origem, quest, slotUI)`: checa só o TIPO e SOMA no slot
  do pedido (não remove da origem). `Slot.BEEDBHJANGN` acumula (retorna sobra).
- `AutomaticFillQuest(1, quest, slotUI)`: preenche o slot a partir da CraftingInventory.
- `TryToCompleteOrder(1, num, item, stack)`: checa tipo + quantidade
  (`RequiredItemQuest.FJDFAEDIAFJ`), remove da CraftingInventory (inclui inventário
  do jogador) e paga.

## Fix aplicado (rodada 223)
No Enter do `KeyboardUINavigator` para pedido ATUAL: preenche o slot até a
`requiredAmount` — 1º `AutomaticFillQuest` (contêineres), 2º varrendo
`PlayerInventory.GetPlayer(1).GetAllSlots()` (inventário principal + hotbar) com
`TransferItemsFromSlot` acumulando — e então `TryToCompleteOrder`. Mensagens:
"Pedido entregue: N de X", "Faltam itens: pede N, só reuni M", ou "Não encontrei X".

## Aberto
- Testar com item no inventário principal e/ou em baús da taverna.
- Se `singleItem` no slot do pedido impedir acumular de múltiplos slots, avaliar.
- "Cofre Entregas / Caixa de Correspondência" (DeliveryChest) é OUTRO objeto.
