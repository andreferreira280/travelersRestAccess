# Módulo: Serviço da taverna (clientes, pedidos, atender)

> Iniciado na rodada 118. Investigação do loop de atender clientes depois que
> a taverna abre, com a parte inicial de acessibilidade implementada.

## Como o jogo funciona (lido no decompilado)

- **Cliente** = `Customer : CustomerBase, IInteractable, IProximity` (GameObject
  "HumanMaiCustomer..."). Campos públicos úteis:
  - `customerState` (`CustomerState`: Spawning, HeadingToBar, OrderInTable,
    EatingAtTable, Leaving, Despawning, Inactive, BeingANuisance...).
  - `currentRequest` (`ItemInstance`) - o item pedido. `preference`
    (`CustomerPreference`: Food/Drink).
  - `hasBeenServed` (bool), `tableOrder` (`CustomerOrder`).
- **Prompt "Serve" (E)** aparece quando `customerState == OrderInTable`
  (Customer.cs:576). Atender é MANUAL: o item pedido tem que estar na sua
  BANDEJA (`PlayerController.trayHandler.tray`) e você aperta E perto do
  cliente (`ServeCustomer(..., tray)`).
- **TavernManager** (`TavernManager.GGFJGHHHEJC`): `customers` (List<Customer>),
  `LKOJBFMGMAE` (=> aberta), eventos `OnCustomerEnterTavern`,
  `OnCustomerLeaveTavern`, `OnTavernOpen(int)`.
- A "gata" é `CatNPC : NPC` (prompt "Conversar").

## Implementado (rodada 118)

- **Nome do cliente/gato no prompt** (`WorldNavigationHandler.DescribeNpc`,
  usado por `GetNearestInteractionTarget`): cliente -> "Cliente, quer {item}"
  (ou comida/bebida); gato -> "Gato". Antes saía o nome cru
  ("HumanMaiCustomer (2)") ou, pior, o nome do objeto mais próximo (um
  dispenser) - corrigido na rodada 117 usando o elemento de interação FOCADO.
- **Anunciador de serviço** (`HandleTavernServiceAnnouncements`, poll 0.5s da
  lista `customers`): cliente novo -> "Cliente chegou"; cliente vira
  OrderInTable -> "Cliente quer {item}, sirva com a bandeja"; cliente sai ->
  "Cliente foi embora".
- Precisou referenciar as DLLs do Photon no csproj (Customer/CatNPC herdam de
  `MonoBehaviourPunCallbacks`).

## Implementado (rodada 119)

- **Servir remoto Z/X** (`HandleServeKeys`): Z serve o cliente OrderInTable
  mais próximo com `preference==Food`, X com `==Drink`, chamando
  `Customer.ServeCustomer(1, true, tray)` (sem checagem de distância própria;
  serve `currentRequest` da bandeja). Falha clara se o item não está na
  bandeja. Z/X livres no jogo.
- **Servido / satisfeito**: rastreia `hasBeenServed` -> "Pedido servido" na
  transição (cobre o E nativo; Z/X marca pra não duplicar); ao sair, "Cliente
  saiu satisfeito/insatisfeito".
- **Nome do gato** corrigido: perto da gata o jogo focava a torneira
  ("663 - Grifo"); `GetNearestInteractionTarget` agora prioriza NPC disponível
  (`FindClosestAvailableNpc`) sobre a estação focada.

## Implementado (rodada 120)

- **Z/X** agora servem cliente em OrderInTable OU **WaitingAtBar** (no bar) -
  antes só OrderInTable, por isso "não funcionava".
- **"servido" confiável**: assina `CommonReferences.GGFJGHHHEJC.
  OnAnyCustomerServeItem` (dispara em todo serviço) em vez do poll que perdia
  serviços rápidos.
- **Mai**: `MaiNPC -> "Mai"` em DescribeNpc (a "gata" era a Mai; o jogo focava
  o bar do lado).
- **Backspace limpa mancha** (`HandleMopBackspace`): com o esfregão selecionado
  no uso rápido, limpa a mancha mais próxima (`FloorDirt.DestroyFloorDirt`,
  conta pra missão via OnFloorDirtDestroyed), lista `CommonReferences.
  tavernFloorDirt`.

## Mecânica de servir (lida em NABCJBPDMJI, rodada 121)

- **Cliente na MESA (OrderInTable)**: item TEM que estar na BANDEJA
  (`tray.MHBHHNCFOEG(item)`).
- **Cliente no BAR (WaitingAtBar)**: COMIDA (`!item.JEPBBEBJEFI()`) pode vir do
  inventário OU do BarMenuInventory; BEBIDA (`JEPBBEBJEFI()=true`) precisa da
  BANDEJA.
- Encher um copo num dispensador põe a bebida DIRETO na bandeja
  (`DrinkDispenser.TakeDrink(..., trayHandler.tray, ...)`). O blocker do usuário
  era encher a bebida ERRADA - cada dispensador tem um tipo (cor).
- Z/X classificam por `currentRequest.JEPBBEBJEFI()` (não `preference`).
- Dispensadores nomeados por `DrinkDispenser.lastDrink` -> "Dispensador de
  bebidas, {bebida}".

## Implementado (rodadas 122-123)

- **Pedidos do BALCÃO** (WaitingAtBar) anunciados, não só mesa: "Cliente no
  balcão quer {item}".
- **Manchas novas**: poll de `CommonReferences.tavernFloorDirt.Count` -> "Mancha
  nova no chão".
- **Bebida na bandeja**: a bebida só cai em `tray.currentDrinks` quando o copo é
  enchido por COMPLETO (DoWork). Poll do count -> "{bebida} na bandeja, aperte X
  pra servir" (era o motivo do X falhar com tray=[]).
- **Tecla V** acalma o `customersRowdy` mais próximo via `Customer.OBGPLACHKHK
  (null)` (probabilístico).
- Backspace: a checagem `GetSelectedItem() is Mop` confirmada funcionando no log.

## Implementado (rodada 124)

- **V acalma de verdade**: `OBGPLACHKHK(null)` é probabilístico e nunca acalma
  pro jogador (log: sempre calmed=False); trocado por
  `Customer.MFOPJDFMJBN(MoodState.Neutral)` (humor direto pra Neutro).
- **Backspace limpa louça suja** também: `Seat.dirtyDish` + `Seat.CleanDirtyDish()`
  (lista `_cachedSeats`), além das manchas - limpa a mais próxima.
- **Pedidos**: anúncio por-cliente (`_customerOrderAnnounced`), dispara assim que
  o cliente fica serveável com pedido; poll 0.25s.

## Implementado (rodada 125)

- **Delete expulsa encrenqueiro** (`HandleExpelKey`, com esfregão): kicka o
  `customersRowdy` mais próximo via `Customer.MarkAsKicked()` (exige
  BeingANuisance -> força com `FHPAMNEIJLI(true)` se ainda Rowdy). "Cliente
  expulso".
- V (acalmar) e Delete (expulsar) coexistem; o jogador escolhe.

Controles taverna: V acalma / Delete expulsa / Z comida / X bebida / Backspace
limpa mancha ou louça.

## Implementado / investigado (rodada 126)

- **Louça suja**: `Seat.CleanDirtyDish()` só `SetActive(false)` no prato, nunca
  zera `Seat.dirtyDish`. Detecção corrigida pra `dirtyDish.gameObject.activeSelf`
  (antes: "mesa limpa" infinito + falso positivo + limpava prato fantasma em vez
  da mesa grande). Vazio -> "Nada pra limpar".
- **Acalmar x missão**: `TavernServiceManager.kickedCustomers` +
  `AddKickedCustomer` existem; NÃO há contador de acalmados. Missão de rowdy = 
  EXPULSAR (Delete), não acalmar.

## Implementado (rodada 127)

- **Mesa grande**: louça em `Table.dish[]` (não `Seat.dirtyDish`); Backspace
  varre `_cachedTables` e limpa o prato ativo mais próximo (SetActive(false) +
  `placeableSurface.RemoveFromSurface`). Limpa o mais próximo entre mancha /
  louça de cadeira / louça de mesa.
- **DescribeNpc ciente do estado**: "quer {item}" só em OrderInTable/WaitingAtBar;
  "Cliente comendo" em EatingAtTable; senão "Cliente". (Z parecia inconsistente
  porque a proximidade dizia "quer comida" pra quem já estava comendo.)
- **Rowdy por humor**: V/Delete usam `FindNearestRowdyCustomer` (currentMoodState
  == Rowdy ou BeingANuisance). Alerta "Cliente ficou bravo" quando surge.

## Implementado (rodada 128) - validação a fundo

- **Expulsar (Delete) corrigido**: era `FHPAMNEIJLI` (DESLIGA hitDetection).
  Caminho real do mop-hit = `BecomeNuisance(true)` + `KickWithForce(playerPos)`
  (chama MarkAsKicked + progride o tutorial, `GetCurrentPhaseID() < 168`).
  Conta pra missão. A missão de rowdy é EXPULSAR (a fase 112 transforma
  CalmCustomer em BecomeNuisance).
- **Acalmar (V)**: `CalmCustomer(null)` (o que o E chama) só funciona em Rowdy
  não-nuisance. V agora só acalma Rowdy; nuisance -> "use Delete".
- **Z** validado correto: clientes em HeadingToBar quando Z foi apertado
  (timing). Aperte ao ouvir o pedido.

## Implementado (rodada 129)

- **Mesa grande**: a sujeira é o NÍVEL DE SUJEIRA (`Table.JNHCCCBICDM` =
  TableDirtLevel Messy/Dirty/VeryDirty), não louça. Backspace limpa via
  `Table.SetDirtiness(0f)`. Pega o mais próximo entre 4: mancha / louça de
  cadeira / louça de mesa / nível de sujeira.
- **Z sem bug** (validado): COMIDA é servida no BAR (`WaitingAtBar`); 0
  `OrderInTable/comida` no log. HeadingToBar/HeadingToSeat não dá pra servir
  (nem E). Mensagem do Z melhorada (chegando / já servido / nenhum).

## Implementado (rodada 130)

- **V acalmar** voltou a usar `Customer.CalmCustomer(null)` (o método real do E,
  linha 873) - o objetivo "Tente acalmar" rastreia essa chamada; o MFOPJDFMJBN
  direto da rodada 128 quebrava o objetivo. Probabilístico; a tentativa conta.
- **Z/X fallback**: se não acha no estado serviível, tenta `ServeCustomer` no
  cliente do tipo certo mais próximo (qualquer estado) e loga
  `serve Z FALLBACK try on state=... -> served=...`.
- Objetivo da missão = "tente acalmar" (V) + "expulsar com esfregão" (Delete).

## Implementado (rodada 132) - objetivo de expulsar

- Lendo `T112_CalmarCliente` (fase 112): objetivos rastreados por
  `OnCustomerBecomeNuisance` (acalmar) e `OnCustomerIsHit` (expulsar).
  `OnCustomerIsHit` só dispara em `Customer.KickOut(HitDetection)` (não em
  KickWithForce). Delete agora: `BecomeNuisance(true)` + `KickOut(
  PlayerController.GetPlayer(1).hitDetection)` -> completa o objetivo + conta
  kickedCustomers + expulsa.
- Pendente: tecla P abre uma "ajuda" que lê 1x - fazer ler+fechar.

## A fazer (próximas rodadas) - "investigue tudo, não quero nada sem anunciar"

- Alertas/eventos/missões que surgem durante o serviço (manchas novas no chão,
  clientes rowdy/BeingANuisance, last orders via `OnTavernLastOrders`,
  reputação) - anunciar quando APARECEM, não só por proximidade.
- Quantos clientes cabem / estatística ao abrir (assentos disponíveis ->
  TavernSeatingManager).
- Comidas/bebidas servidas por funcionários (Barworker/Employee) - se há
  auto-serviço, anunciar.
- Confirmar/ajustar o nome do gato se a resolução ainda pegar o dispenser
  (diagnóstico "interaction target ... npc=" no log).
