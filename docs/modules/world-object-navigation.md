# Módulo: Navegação até objetos/entradas/personagens (mundo aberto)

> Ver convenção em `docs/modules/main-menu-and-options.md` (cabeçalho).
> Pedido pelo usuário em 2026-06-20, depois de mergear a feature
> anterior (menus/diálogo) - ver `docs/modules/core-gameplay-navigation.md`
> pra pesquisa inicial relacionada (zonas, prompt de ação, pathfinding
> do próprio jogo).

## Visão final (pedida pelo usuário, várias etapas)

Escolher um item/entrada/objeto/personagem numa lista por categoria
(Page Up/Down dentro da categoria, Ctrl+Page Up/Down troca de
categoria). Depois de escolher um alvo, 3 opções (futuro):

1. Ouvir a direção sob demanda (tecla Home: "10 pra frente, 8 pra
   esquerda", com distância total, recalculando a cada passo).
2. Ser guiado automaticamente até lá (tecla End).
3. Andar ouvindo um som vindo da direção do alvo (guia sonoro).

Por enquanto, a opção de anunciar a cada passo (em vez de só quando
pedido) fica FIXA como "sempre anuncia" - sem toggle ainda.

Dois alvos concretos pra validar com: a porta de entrada da taverna, e
a cama (que o gato pede pra "encontrar" na primeira missão).

## Etapas (acordadas com o usuário, cada uma testável sozinha)

1. **Fundação (ativo agora)**: confirmar que conseguimos ler a posição
   real do jogador e dos alvos, e calcular distância ao vivo. Sem
   tecla nova, sem som - só log de debug.
2. **Lista básica**: Page Up/Down entre só os 2 alvos conhecidos
   (porta, cama), anunciando o nome - sem categoria ainda.
3. **Tecla Home**: anuncia direção + distância até o alvo escolhido,
   atualizando a cada passo (fixo "sempre anuncia").
4. **Categorias** (Ctrl+Page Up/Down): só depois de termos alvos de
   mais de 1 tipo.
5. **Tecla End** (andar até lá sozinho): usa o pathfinding que o
   próprio jogo já tem (`PathRequestManager.RequestPath`, viabilidade
   já confirmada em `core-gameplay-navigation.md`).
6. **As 3 opções finais**: feature completa.

## Etapa 1 - Fundação (em andamento)

**Objetivo:** confirmar, com dados reais (não suposição), que dá pra
pegar a posição do jogador e dos dois alvos, e calcular distância.

**Implementado:**
- `WorldNavigationHandler.cs` (novo, debug-only, sem nenhuma tecla
  nova) - a cada ~1s, loga (só com F12 ativado):
  - Posição do jogador: `PlayerController.GetPlayerPosition(1)`
    (confirmado no código decompilado, é um helper estático já pronto
    - não precisamos nem de `GetPlayer(1).transform.position`).
  - Posição + distância de toda instância de `Door` na cena (achado em
    `decompiled/Door.cs` - é `AccessElement`/`IInteractable`, tem
    `transform.position` normal). Deve incluir a porta da taverna,
    mas ainda não confirmado QUAL `Door` é ela especificamente -
    próximo teste deve revelar pelo nome/posição.
  - Posição + distância do que o PRÓPRIO JOGO considera "em alcance de
    interação agora" (`InteractObject.BBJCJFJEFKK(1)?.GetCurrentInteractGO()`,
    o mesmo mecanismo já usado pro prompt de ação "[E] ..." - ver
    `core-gameplay-navigation.md`). Essa é a forma mais simples de
    confirmar a posição da cama: basta o usuário andar até perto dela
    no jogo (sem precisar eu caçar qual `Placeable`/`CatBed` é a
    certa no código - essa investigação (`HeadToBed.cs`) é sobre o
    GATO indo pra cama DELE, não a cama do jogador, e foi descartada
    como caminho errado).

**Resultado do 1º teste (2026-06-20):** porta CONFIRMADA - capturada
como `CurrentInteract "Door"` no momento exato de abrir, distância
~0.3 (quase zero, correto). A entrada principal da taverna não é um
`Door` genérico (só apareceu 1 instância na lista de `Door`, e era a
da adega - "1125 - Cellar Door"), então a entrada deve ser uma
subclasse diferente (`JapaneseDoor`/outra) - sem problema, já que
`GetCurrentInteractGO()` não depende do tipo, achou ela mesmo assim.
Cama: o prompt "Arrumar a Cama" apareceu no log do usuário, mas o
check de `CurrentInteract` só rodava 1x/segundo e não bateu no mesmo
frame - nada foi capturado. Corrigido: agora esse check específico
roda TODO frame (sem throttle), só loga quando o valor MUDA.

**2ª rodada (2026-06-20) - achados além da Etapa 1:**
- Usuário relatou não ter NENHUM retorno de voz ao chegar perto de
  algo ("andando sem direção"). Virou prioridade: ver
  `core-gameplay-navigation.md` - prompt de ação agora é anunciado
  ("Próximo: ...") em vez de filtrado.
- Investigado por que a cama "só ativa do lado certo" e por que só o
  clique do mouse funcionou: a cama NÃO usa o sistema de interação
  por tecla (E/Q) - `Bed.OnTriggerEnter2D` (decompilado) abre por
  PROXIMIDADE (colisor 2D trigger, lado específico = onde o colisor
  realmente está) um popup Sim/Não ("Dormir?") via
  `YesNoDialogueUI`. Esse popup nunca foi anunciado (a pergunta é só
  texto, não lida pelo nosso scanner) - por isso o usuário não sabia
  que um popup tinha aparecido e usou o mouse para confirmar. Corrigido
  em `KeyboardUINavigator.cs` (`AnnounceYesNoQuestionIfChanged`) - a
  pergunta agora é falada quando aparece; os botões Sim/Não já devem
  ser navegáveis pelo esquema genérico (setas + Enter), sem código
  extra.
- Para o caso geral de "a tecla mostrada na tela (ex: [Q]) não
  funciona, só o clique do mouse" - usuário sugeriu um atalho
  keyboard-only de fallback. Implementado em
  `WorldNavigationHandler.HandleSimulatedClick()`: **Ctrl+Enter**
  simula um clique ESQUERDO, **Shift+Enter** simula um clique
  DIREITO, sempre no alvo que `GetCurrentInteractGO()` considera "em
  alcance" agora (mesmo conceito já usado no resto da Etapa 1). Só
  ativo fora de menus (não conflita com Enter normal do
  `KeyboardUINavigator` dentro de UI).

**3ª rodada (2026-06-20):**
- Confirmado: a pergunta "Dormir?" funcionou (anunciada e respondida).
  Confirmado também: a tecla Q continua não funcionando em algo
  (provavelmente uma lareira, ver abaixo) e só o clique do mouse
  ativava - **investigado e corrigido**, não só registrado.
- Causa raiz achada no `Fireplace.cs` decompilado: esse tipo de objeto
  implementa `IInteractable`/`IProximity`, mas NUNCA passa por
  `InteractObject.SetCurrentInteract`/`GetCurrentInteractGO()` (esse
  método sempre voltava null pra ele, confirmado no log -
  `"Ctrl/Shift+Enter pressed but no current interact target"`
  repetido bem na hora que o usuário tentava). O prompt na tela vem de
  `IProximity.IsAvailableByProximity`, e o clique real chama
  `IInteractable.MouseUp` direto - sem passar pelo sistema de seleção
  usado pela porta. Corrigido: Ctrl+Enter agora tenta esse caminho
  primeiro (`TryInteractableMouseUp` em `WorldNavigationHandler.cs`),
  achando o `IInteractable`+`IProximity` mais próximo disponível e
  chamando `MouseUp` nele direto - cobre a lareira e qualquer outro
  objeto no mesmo padrão. "Combustível" (adicionar combustível) parece
  ser um fluxo de arrastar item do inventário, não um clique simples -
  não resolvido ainda, é um problema diferente (acessibilidade de
  drag-and-drop), fora do escopo desta feature por agora.
- Bug achado e corrigido: o anúncio "Próximo: ..." ficava repetindo
  sem parar quando DOIS prompts apareciam ao mesmo tempo (confirmado
  no log: lareira mostra "Abrir" E "Combustível" juntos) - o
  rastreamento usava um único valor "última frase anunciada", então
  cada frame alternava entre as duas e re-anunciava ambas pra sempre.
  Trocado para um conjunto (`HashSet`) com as frases ATUALMENTE
  visíveis: anuncia só as que são novas, mantém quietas as que já
  estavam lá, e libera de novo qualquer uma que desapareça.
- Adicionado o nome do objeto ao anúncio (pedido do usuário - "Abrir"
  sozinho não diz se é porta, baú, etc.): `Próximo: Porta: Abrir`, por
  exemplo. Usa `WorldNavigationHandler.GetNearestInteractionName()` -
  esforço-melhor-possível (aproximado quando 2 objetos diferentes
  mostram prompt ao mesmo tempo - usa o mais próximo no geral).

**Etapa 2 implementada nesta rodada** (lista básica Page Up/Down,
porta + cama, anunciando o nome - ver plano abaixo): a porta de
entrada não tem referência estática confiável (nome de GameObject
"Door" é reaproveitado por toda porta do jogo, incluindo a da adega) -
a única forma confirmada de identificá-la é guardar a referência na
primeira vez que o jogador realmente a abre via
`GetCurrentInteractGO()` (isso já rodava desde a Etapa 1). A cama não
precisa desse truque - `Bed.GetPlayerBedPosition()` é uma chamada
estática direta, disponível assim que a cama existe na cena.
**Page Up/Down** alterna entre os alvos conhecidos e fala o nome
("Porta da taverna" / "Cama"). Sem categoria, sem distância ainda
(Etapas 3+).

**4ª rodada (2026-06-20) - pedidos de orientação geral:**
- "Portas/itens/personagens devem estar na lista se eu estiver na
  mesma área, mesmo sem já ter usado, cada um na sua categoria":
  implementado PARCIALMENTE - portas agora entram na lista por
  PROXIMIDADE (raio de 30 unidades do jogador), não só depois de
  abertas (a porta da entrada continua tendo tratamento especial
  porque é a única que precisamos DIFERENCIAR de outras portas
  parecidas - ver `NearbyDoorRadius`/`BuildTargetList` em
  `WorldNavigationHandler.cs`). Não existe uma "tag de zona" por
  objeto pra comparar com a localização do jogador - distância é o
  substituto prático (confirmado: objetos de áreas diferentes ficam a
  1000+ unidades, os da mesma área a poucas unidades). Itens e
  personagens (categorias) ainda NÃO implementados - precisa de
  investigação própria (`Placeable` pra itens, `DialogueNPCBase`/`NPC`
  pra personagens, ambos confirmados existir no jogo) - próximo passo
  combinado com a Etapa 4 (categorias) do plano original.
- "Não sei onde estou depois que saio da cama, fala o nome da área
  quando mudar?": implementado - `PlayerController.LEOIMFNKFGA` (a
  localização atual do jogador, ex. `Location.Tavern`) é monitorada;
  ao mudar, anuncia "Área: <nome em português>" via uma tabela de
  tradução fixa (`LocationNames` em `WorldNavigationHandler.cs`).
- "Som de passo não toca a cada passo, ajuste": investigado no
  decompilado (`Footsteps.cs`) - é um sistema do PRÓPRIO JOGO, não
  nosso, baseado em TEMPO (toca no máximo 1x a cada 0.5s de
  movimento), não em distância/tile real. Tamanho real do tile
  confirmado em `WorldGrid.allNeighbours` = 0.5 unidades. Em vez de
  criar um som paralelo (risco de tocar 2 sons ao mesmo tempo, ficando
  pior), reduzi o cooldown do sistema do jogo via reflection (não tem
  campo público) de 0.5s pra 0.2s - deve tocar bem mais seguido agora,
  usando o som certo do terreno (o do próprio jogo). Ajuste empírico
  (não dá pra calcular o valor "perfeito" sem saber a velocidade real
  configurada no jogo) - testar e ajustar de novo se ainda não estiver
  bom.

**5ª rodada (2026-06-20) - bug crítico achado + Etapa 3:**
- **REGRESSÃO CRÍTICA achada e corrigida**: anúncios de "Próximo: ..."
  pararam de funcionar completamente. Causa raiz no log: uma exceção
  (`NullReferenceException` em `Harvestable.IsAvailableByProximity`)
  era lançada a cada frame dentro de
  `WorldNavigationHandler.GetNearestInteractionName()` (chamado pela
  rodada anterior pra incluir o nome do objeto no anúncio) - matava
  silenciosamente o `DialogueAnnouncer.ScanAndAnnounceText()` inteiro,
  então NADA era anunciado, sempre. `Harvestable` (plantas/colheita)
  aparentemente lança essa exceção quando não está num estado
  específico - corrigido com try/catch ao redor da chamada, ignorando
  só o objeto problemático em vez de travar tudo.
- **Aviso de área nunca apareceu**: ainda não confirmado se é porque o
  usuário não cruzou nenhuma transição de zona real, ou outro motivo -
  adicionado log incondicional (modo debug) do valor bruto de
  `LEOIMFNKFGA` a cada mudança, mesmo quando é `None`, pra confirmar
  no próximo teste sem chutar.
- **Som de passo, 2ª tentativa**: reduzir o cooldown não foi
  suficiente (usuário confirmou - quer 1 som por telha real ao
  ANDAR, tempo é aceitável só ao CORRER). Trocado de "reduzir o
  cooldown" pra "desativar o timer nativo (valor enorme) e disparar
  nosso próprio som por DISTÂNCIA real" (0.5 unidades = 1 telha,
  confirmado em `WorldGrid`), replicando a lógica de
  som-por-terreno do próprio jogo (`WorldGrid.GCGNCHFNEBJ` +
  `WorldTile.groundType/materialType/hasSnow` + `player.inWater`) de
  forma simplificada. Precisou referenciar
  `Assembly-CSharp-firstpass.dll` no `.csproj` (onde
  `AudioObject`/`MultiAudioManager` realmente vivem, fora do
  Assembly-CSharp principal).
- **Etapa 3 implementada** ("preciso de coordenadas/navegação até as
  coisas, é o que quero agora"): tecla **Home** liga/desliga um modo
  de guia contínuo pro alvo selecionado no Page Up/Down (guardado
  separado do índice da lista, que pode mudar de tamanho conforme
  portas entram/saem do raio de 30 unidades). Enquanto ativo, a cada
  telha andada (mesma base de distância dos passos) anuncia algo como
  "10 pra cima, 8 pra esquerda" (delta em X/Y do mundo, não relativo
  à direção que o personagem está olhando). "Você chegou" quando a
  distância chega a zero.
- **Som de parede (bater/ficar presa)**: pedido recebido, NÃO
  implementado ainda - fica pra próxima rodada (essa rodada já tinha
  um bug crítico + 2 features grandes).

**6ª rodada (2026-06-20) - confirmações + 2 bugs novos achados no log:**
- Confirmado pelo usuário: anúncio com nome voltou a funcionar
  (crash corrigido), e Home (direção/distância) funcionando bem.
- **Bug novo achado no log**: `Ctrl+Enter` estava abrindo/fechando
  portas sozinho ("Ctrl+Enter -> IInteractable.MouseUp on \"Door\" ->
  True" repetido) - `Door` também implementa
  `IInteractable`/`IProximity` (igual a lareira), então o fallback
  novo da rodada anterior também capturava portas, e
  `Door.MouseUp` simplesmente alterna aberto/fechado. Portas já
  funcionam bem pela tecla nativa - esse fallback é só pra quem NÃO
  funciona (lareira) - corrigido excluindo `Door` desse caminho.
- **Crash novo achado no log**: `PlayerController.GetPlayerPosition`
  lança `NullReferenceException` em janelas curtas onde o jogador
  fica temporariamente null (transição dentro da mesma cena, não
  troca de cena completa) - `Main.cs`'s `CheckGameReady()` não cobre
  esse caso. Adicionado guard no topo do `Update()`.
- **Som de passo ainda não tocava**: o log confirmou que
  `Footsteps.instance` nunca foi setado quando checado (nenhum log de
  sucesso em toda a sessão) - causa exata ainda não confirmada
  (não chutei mais uma vez). Adicionado log de diagnóstico
  específico (`Footsteps.instance is null`, etc.) pra ver exatamente
  onde trava no próximo teste.
- **Som ao chegar perto de algo**: adicionado (`UISound.PlayNavigate()`
  junto com a fala "Próximo: ...").
- **Conversa ambiente desativada**: "Conversa ao redor: ..." (NPCs
  tipo a família do Arthur) não é mais anunciada - filtrado na raiz
  (path "Bark UI"), não é uma regra específica pro Arthur, é a
  funcionalidade toda.
- **Itens adicionados à lista**: `Placeable` dentro do mesmo raio de
  30 unidades das portas, usando o nome já legível que o próprio jogo
  dá ao objeto (ex. "Mesa Grande", "Cofre Pequeño") - sem inventar
  nome. ATENÇÃO: ainda não testado se isso deixa a lista grande
  demais numa taverna cheia de móveis/decoração - avisar se ficar
  ruim de navegar.
- **Tutorial bloqueando saída da taverna**: confirmado que é o
  PRÓPRIO JOGO (tutorial story-gated), não um bug nosso - por isso
  ainda não foi possível testar troca de área de verdade.
- **Som de parede**: ainda não implementado (continua pendente).

**7ª rodada (2026-06-20) - 2 correções (uma delas desfaz a anterior):**
- **"Ctrl+Enter abrindo/fechando porta" NÃO era bug** - usuário
  confirmou que era ele mesmo testando várias vezes de propósito.
  A exclusão de `Door` adicionada na rodada anterior foi REVERTIDA
  (`FindClosestAvailableByProximity` voltou a incluir portas) porque
  ela tinha um efeito colateral real: quando `GetCurrentInteractGO()`
  não pegava a porta no momento exato, o fallback (sem a porta como
  candidata) escolhia outro objeto próximo qualquer - exatamente a
  causa da porta "dizendo" o nome de uma mesa/barril perto dela.
- **Filtro de "Conversa ao redor" estava cortando as falas do
  PRÓPRIO personagem também** - confirmado no log: as falas do
  jogador ("O barril está vazio.", "Eu não sei como usar isto.") usam
  o mesmo componente de UI (`Bark UI`) que as falas ambiente de NPCs,
  só que com caminho começando em `"Player/"` em vez do nome de um
  NPC. Corrigido (`IsAmbientNpcBark`): só filtra quando NÃO é
  `"Player/..."` - falas do personagem continuam sendo anunciadas.
- **"Vários itens que não sei se são interagíveis"**: observação
  registrada, sem mudança ainda - a lista mostra ONDE as coisas
  estão; o que dá pra fazer com cada uma só se sabe chegando perto e
  ouvindo o "Próximo: ...". Se ficar confuso/poluído, próximo passo
  seria filtrar por algum critério de "relevância" (ainda não
  definido) em vez de listar todo `Placeable` no raio.

**Próximo teste (com F12 ativado):**
1. Perto da porta (entrada e adega) - volta a dizer o nome certo (não
   mais "mesa"/"barril")?
2. Perto do Arthur/família - "Conversa ao redor" continua sumida, MAS
   suas próprias falas (personagem) continuam sendo anunciadas?
3. Passos: ainda sem som por telha? (still pending diagnóstico)
4. "testei" quando terminar.

**8ª rodada (2026-06-20) - som de passo (causa real achada), nomes de
item, som de parede:**
- **Som de passo - causa real confirmada no log**: o gatilho por
  distância JÁ estava disparando certinho a cada telha
  ("WorldNav: Footstep played, clip=set" repetido no ritmo certo) -
  o problema nunca foi o disparo, era o SOM em si. Achei no
  decompilado (`FootstepObjectSound.cs`): o jogo usa volumes de
  trigger pra registrar um som customizado por zona (piso de madeira
  da taverna, etc.) numa lista interna por jogador
  (`Footsteps.PMPPEAHDDAB`), e SÓ usa os campos genéricos
  (`stepsWood`, `stepsDirt`...) se essa lista estiver vazia. Minha
  versão simplificada só usava os genéricos - que aparentemente estão
  vazios/sem clipe de verdade neste jogo (todo o som real vem das
  zonas customizadas). Corrigido: agora verifica essa lista primeiro
  via reflection, só cai pro genérico se não achar nada.
- **Nomes de item melhorados**: achei que `Placeable.itemSetup.item`
  dá acesso ao item de verdade, e `Item.nameId` é uma chave de
  localização (mesmo padrão usado nos títulos da Enciclopédia) -
  trocado de "tentar limpar o nome do GameObject" pra usar
  `LocalisationSystem.Get(item.nameId)`, o nome real e já traduzido
  que o jogo usa.
- **Som de parede - analisado e implementado** (1ª tentativa,
  valores empíricos): não achei nenhum sinal já pronto no jogo pra
  "movimento bloqueado" - comparei o quanto o jogador REALMENTE andou
  no frame contra o mínimo esperado pra velocidade dele
  (`PlayerController.speed`) enquanto `moving` está true; se ficar
  bem abaixo disso por ~0.25s seguidos, toca o som de "limite"
  (`UISound.PlayBoundary`, reaproveitado). Cooldown de 0.6s entre
  toques. Pode precisar de ajuste fino depois de testar.

**Próximo teste (com F12 ativado):**
1. Ande pela taverna - os passos tocam som de verdade agora (não só o
   "clip=set" no log)?
2. Use Page Up/Down perto de itens variados - os nomes ficaram mais
   claros (em português, vindo do próprio jogo)?
3. Encoste numa parede e fique tentando andar contra ela - toca um
   som? Ficou bom o tempo de resposta, ou muito rápido/devagar?
4. "testei" quando terminar.

**9ª rodada (2026-06-20) - desisti do sistema nativo de som de passo,
sons próprios do usuário, e o caso do "esfregão":**
- **Som de passo continuava mudo mesmo com a 3ª tentativa**
  (verificar lista de override por zona) - confirmado no log que o
  disparo SEMPRE funcionou certo, mas nunca produziu som audível por
  nenhum caminho do sistema nativo (`MultiAudioManager`/`Footsteps`).
  Não vale mais tentar uma 4ª variação às cegas - abandonado esse
  sistema pros passos. Agora usa `UISound.PlayNavigate()` (o mesmo
  clique já comprovado funcionar em todo o resto do mod).
- **Usuário colocou `parede.wav` e `itens.wav` na pasta raiz do
  projeto** - criado `CustomSounds.cs`: carrega esses 2 arquivos via
  `UnityWebRequestMultimedia` (precisou referenciar
  `UnityEngine.UnityWebRequestModule`/`...AudioModule` no `.csproj`)
  e toca via `AudioSource` própria (`spatialBlend = 0`, sem depender
  de nenhum sistema de áudio do jogo) - os mesmos sistemas do jogo
  que falharam pros passos. `.csproj` agora copia os 2 `.wav` pra
  pasta Mods junto com a DLL. Som de parede e som de "Próximo: ..."
  agora usam esses arquivos.
- **Nomes de item ainda estranhos**: adicionado log de diagnóstico
  (`WorldNav: Item name from itemSetup` vs `...fallback`) pra
  confirmar no próximo teste se é porque `itemSetup.item` é null
  pra esses objetos (provavelmente decoração/mobília sem item de
  inventário associado) em vez de tentar outro fallback sem saber a
  causa.
- **"Não sei qual item da lista é o baú com o esfregão"**: esclarecido
  - mesmo com nome perfeito do CONTAINER (ex. "Cofre Pequeño"), o que
  está DENTRO dele (o esfregão) não é informação que dá pra saber só
  olhando o `Placeable`/`Item` do próprio baú - precisaria de uma
  feature diferente (espiar conteúdo de containers), ainda não
  existe. Por enquanto, melhorar o nome do baú em si é o que dá pra
  fazer; achar QUAL baú tem QUAL item ainda exige abrir e ouvir o
  conteúdo um por um.

**10ª rodada (2026-06-20) - investigação real (decompilada) do som de
passo, loop no som de parede, filtro de itens-fantasma, e categorias:**
- **Som de passo, causa real confirmada (não mais suposição)**:
  decompilei `AlmenaraGames.MultiAudioManager`/`AudioObject` de verdade
  (`Assembly-CSharp-firstpass.dll`, via `ilspycmd`). Confirmado: o clipe
  de áudio NÃO estava vazio/nulo - sempre era selecionado certinho
  (`Debug.LogWarning` de "doesn't have a valid Audio Clip" nunca apareceu
  no log). O sistema realmente tenta tocar o som; o motivo de não sair
  áudio está no sistema de listener/volume por distância PRÓPRIO desse
  plugin de terceiros (`MultiAudioListener`, diferente do `AudioListener`
  nativo da Unity), que não dá pra controlar de fora do fluxo normal de
  inicialização do jogo sem mais investigação profunda. Por pedido do
  usuário, removido o som de clique do passo (sem reposição por
  enquanto) - **precisa de um arquivo `.wav` próprio pra passo, igual
  fez com parede/itens, se quiser som aí**.
- **Som de parede**: trocado de "som único toda vez que bate" pra um
  loop contínuo que começa quando o jogador fica preso e só para quando
  ele se solta (em vez de retocar a cada 0,6s). Tempo de confirmação de
  "preso" ajustado pra 0,5s conforme pedido.
- **Item "fantasma" na lista**: confirmado no log um `Placeable` sem
  nenhuma representação visual (`BarManager`, um script de controle, não
  um objeto físico) aparecendo na lista de navegação - filtrado agora
  (exige `SpriteRenderer` no objeto).
- **Nome errado encontrado**: uma antocha de parede ("622 - Antorcha de
  Pared Variant") está resolvendo pro nome "Cacto" via
  `LocalisationSystem.Get(item.nameId)` - parece ser um dado errado no
  próprio jogo (o `Item` ligado a esse objeto aponta pro nome errado),
  não um bug do nosso código. Não é algo que dá pra corrigir sem
  reescrever a tabela de tradução do jogo.
- **Achado útil pro pedido do "esfregão" (não implementado ainda)**:
  `Container.cs` (componente em todo baú/container) tem `Slot[] slots`,
  e cada `Slot` tem `itemInstance` com referência ao `Item` real dentro
  - ou seja, é TECNICAMENTE possível no futuro "espiar" o conteúdo de um
  baú específico sem abri-lo, e anunciar nomes do que tem dentro. Feature
  separada, ainda não pedida formalmente pra implementar.
- **Categorias implementadas**: `Ctrl+Page Up`/`Ctrl+Page Down` agora
  trocam de categoria (anuncia "Categoria: X (N)"); `Page Up`/`Page
  Down` navegam dentro da categoria atual. Categorias usadas: Portas,
  Containers, Máquinas, Coletáveis, Decorativos - classificadas pelos
  componentes reais do jogo (`Container`, `Crafter`,
  `Placeable.canBeAddedToInventory`), não chutadas pelo nome.

**11ª rodada (2026-06-20) - categoria padrão, tradução de nomes
confirmados no log, e som de virar/mudar direção:**
- **Categoria padrão "Portas"**: confirmado que a lista abria tudo
  junto ao carregar o jogo - `_currentCategory` agora começa em
  "Portas" em vez de vazio.
- **Nomes de item, evidência real do log**: confirmei (lendo o log,
  não chutando) que TODOS os objetos sem `itemSetup.item` são cenário
  puro sem nenhum dado de localização - o nome que aparece É o nome
  original do asset, em ESPANHOL (os assets desse jogo têm nome em
  espanhol mesmo, só o texto exibido de Item é traduzido). Adicionei
  tradução pras strings específicas que realmente apareceram no log
  (não um dicionário genérico chutado): Grifo->Torneira,
  Malteadora->Moedor de Malte, Trapo Colgado->Pano Pendurado, Grupo
  Ladrillos->Grupo de Tijolos, Cajas Apiladas->Caixas Empilhadas,
  Lateral Habitacion->Parede Lateral do Quarto, Escalera
  Arriba->Escada, Horno Variant->Forno, Mesa de Cocina
  Variant->Mesa de Cozinha, Ventana de Madera->Janela de Madeira,
  Puerta->Porta, Cofre Pequeño->Cofre Pequeno, Cama del
  Jugador->Cama do Jogador, Barril de Servicio->Barril de Serviço,
  Cellar Door->Porta do Porão. O caso "Cacto" (torcha com nome errado
  no próprio jogo) continua sem solução - não é nome de fallback, vem
  de dados reais (errados) do jogo.
- **Som de passo**: o gatilho já era por meia telha desde a primeira
  versão (`TileSize = 0.5f`) - a cadência nunca foi o problema, era o
  som não saindo pelo sistema do jogo (ver 10ª rodada). Sem `.wav`
  próprio ainda pra isso, continua mudo.
- **Novo: som de virar (stand.wav)**: usuário notou que o personagem
  vira pra direção antes de andar - confirmado em
  `PlayerController.GetPlayerDirection(1)` (enum `Direction`: Up,
  Down, Left, Right, Diagonal), que reflete a direção visual real,
  independente do movimento. Agora toca `stand.wav` a cada troca de
  direção, com `AudioSource.panStereo`: -1 (esquerda) pra Left, +1
  (direita) pra Right, 0 (centro) pra Up/Down/Diagonal.

**Próximo teste (com F12 ativado):**
1. Categoria já abre em "Portas" ao entrar no jogo/área?
2. Os nomes de item traduzidos ficaram melhores? Ainda tem algum
   estranho que eu não peguei?
3. Mude de direção (vire pra esquerda, direita, cima, baixo) - toca
   stand.wav com o lado certo (esquerda/direita) e centro pra
   cima/baixo?
4. "testei" quando terminar.

**12ª rodada (2026-06-20) - bugs reportados após o teste real:**
- **"Decorativos" tinha porta/escada**: usuário corrigiu o critério -
  "decorativos" devia ser só janela/vaso/enfeite puro, não passagens.
  `Puerta`, `Cellar Door` e `Escalera Arriba` (sem componente `Door`,
  por isso caíam em `Placeable`) agora vão pra categoria "Portas"
  (detectado pelo nome confirmado no log: "puerta"/"door"/"escalera"/
  "stair").
- **Mensagem ao limpar a mesa grande não lida**: nosso scanner de
  texto só olhava `TextMeshProUGUI` (texto de UI). Ampliado pra
  `TMP_Text` (a classe-base comum, cobre também texto 3D flutuante no
  mundo, tipo um popup de "Limpo!") - se a mensagem for desse tipo,
  agora é capturada.
- **Última fala do gato não lida (investigação, ainda sem certeza)**:
  suspeita forte é que essa fala usa o mesmo balão de fala ("Bark UI")
  de NPCs ambiente, que filtramos hoje por pedido seu (achou "Conversa
  ao redor" chato). Adicionei um log (modo debug) que mostra o texto
  filtrado mesmo sem anunciar - próximo teste confirma se é isso
  mesmo antes de eu decidir como separar "fala ambiente aleatória" de
  "reação direta e única de um NPC" (são casos diferentes, mas com a
  mesma estrutura de tela).
- **Coordenadas/direção "bagunçadas", não atravessa porta**: a causa
  é estrutural - a direção sempre foi uma linha reta (distância em X
  e Y), sem desviar de parede nem saber atravessar porta. Isso quebra
  totalmente quando o alvo está numa ÁREA diferente da sua (ex:
  Taverna vs Estrada), porque essas coordenadas não são uma coisa
  geometricamente contínua. Investiguei o sistema de caminho de
  verdade do próprio jogo (`PathRequestManager`, A* numa thread
  separada, fila de pedidos com callback) - é viável tecnicamente, mas
  é grande e arriscado de ligar sem testar em etapas (regra do
  projeto: nunca acessar sistema de jogo sem confirmar primeiro, risco
  de travar o jogo). NÃO implementei isso ainda. O que fiz agora,
  mais seguro: detectar quando o alvo está numa área diferente da sua
  e avisar isso explicitamente ("Esse alvo está em outra área (X). A
  direção não é confiável daqui - passe por uma porta primeiro.") em
  vez de dar um número errado. Pathfinding de verdade (desviar de
  parede DENTRO da mesma área) continua sendo um pedido futuro maior.

**Próximo teste (com F12 ativado):**
1. Em "Decorativos" agora só aparecem coisas tipo janela/vaso/enfeite
   (sem porta/escada)?
2. Clique na mesa grande para limpar - a mensagem aparece sendo lida
   agora?
3. Vá até um alvo em outra área (ex: selecionado dentro da taverna,
   mas você foi pra rua) - ouve o aviso novo de "está em outra área"
   em vez de uma direção sem sentido?
4. Fale com o gato até a última fala - ainda não é lida (esperado por
   enquanto, é só diagnóstico essa rodada)?
5. "testei" quando terminar.

**13ª rodada (2026-06-20) - sem evidência nova do gato/mesa no log,
mancha do chão, dispenser de bebidas, e ordem de direção:**
- **Gato e mesa, sem novidade no log desta rodada**: conferi o log
  desta sessão de teste e não achei NENHUM rastro da fala do gato
  (nem filtrada, nem lida) - sugere que talvez não tenha sido
  reproduzida nesse teste específico, não que o filtro tenha agido de
  novo. Sobre a mesa: confirmei no log que o aviso "[Q] Limpar"
  apareceu e FOI lido ("Próximo: ... Limpar") - se a mensagem que você
  quer dizer é outra (depois de terminar de limpar, por exemplo),
  preciso saber mais sobre quando ela aparece pra caçar certo.
- **Manchas do chão**: confirmado no código - são um tipo de objeto
  diferente (`FloorDirt`, não é `Placeable`), por isso nunca apareciam
  na lista. Adicionadas numa categoria nova, "Missão" (entre Portas e
  Containers no Ctrl+Page Up/Down). Por enquanto só mancha de chão
  está nessa categoria - "tudo que for da missão ativa" de forma
  genérica (pra qualquer missão futura) é mais trabalho, ainda não
  feito.
- **"Grifo" errado**: confirmado no log (abre "DrinkDispenserUI" /
  "ContentBeerTap") - corrigido pra "Dispenser de Bebidas".
- **Ordem da direção (cofre)**: implementado como pedido - agora fala
  primeiro o eixo com a distância MAIOR (ex: "3 baixo, 1 direita" se
  for mais longe na vertical). Isso não resolve desviar de parede de
  verdade (combinado na rodada passada, é projeto futuro maior) - só
  muda a ORDEM de qual número vem primeiro na fala.

**Próximo teste (com F12 ativado):**
1. Ctrl+Page Up/Down até achar a categoria "Missão" perto de uma
   mancha no chão - aparece "Mancha no chão" lá?
2. Perto do dispenser de bebidas - fala "Dispenser de Bebidas" agora?
3. No cofre (ou outro alvo na MESMA área), o número maior (cima/baixo
   ou esquerda/direita) vem primeiro na fala agora?
4. Mesa: me diga em que MOMENTO exato a mensagem que não é lida
   aparece (logo ao chegar perto? só depois de limpar? outra hora?).
5. Gato: tente de novo a conversa até o fim, se possível.
6. "testei" quando terminar.

**14ª rodada (2026-06-20) - pathfinding de verdade (autorizado pelo
usuário, com risco aceito), porta duplicada, conteúdo do cofre, mesa
suja na categoria Missão, e filtro de fala ambiente desligado:**
- **Pathfinding real implementado**: liguei o sistema de caminho do
  PRÓPRIO jogo (`PathRequestManager.RequestPath`, A* numa thread
  separada já usada pelo jogo pros NPCs, evita parede e objeto de
  verdade). Pedi confirmação antes (risco de travar o jogo, regra do
  projeto) e você autorizou tentar. Protegido com try/catch em volta
  da chamada (se o sistema interno do jogo não estiver pronto, isso
  gera um erro detectável na hora, não uma trava). Quando o caminho
  vem com sucesso, a fala usa a posição do PRÓXIMO PONTO da rota (não
  mais a posição final em linha reta) - deve resolver o "manda pra
  parede" na maioria dos casos, incluindo atravessar porta entre
  áreas diferentes (o próprio sistema do jogo sabe fazer isso, não
  preciso mais do aviso de "está em outra área" na maioria das vezes
  - esse aviso agora só aparece se a rota ainda não chegou ou falhou).
- **Porta duplicada (cofre/porão na mesma coordenada)**: confirmado
  que era o mesmo objeto físico aparecendo 2x (um pelo componente
  `Door`, outro pelo nome "Cellar Door"/"Puerta" via `Placeable`).
  Agora a lista remove duplicado quando 2 entradas da MESMA categoria
  estão a menos de meia telha uma da outra.
- **Conteúdo do cofre**: implementado - ao selecionar um Container
  (cofre, baú, etc.) com Page Up/Down, agora fala o nome E o que tem
  dentro (achei em `Container.slots`/`Slot.itemInstance` no código
  decompilado, são públicos).
- **Mesa suja agora entra em "Missão"**: achei que mesa tem um
  componente separado (`Table`, com nível de sujeira público) além do
  `Placeable` - se estiver suja (não "Perfect"/"Clean"), agora entra
  na categoria "Missão" também, não só mancha de chão.
- **Filtro de fala ambiente desligado temporariamente**: por pedido
  seu, voltei a anunciar falas de NPC ambiente (com o prefixo
  "Conversa ao redor:", separado da fala da história) - precisamos
  ouvir TUDO pra achar a fala do gato, já que mesmo o log de
  diagnóstico da rodada passada não mostrou nada dela. Espera-se mais
  barulho de novo por enquanto - aviso antes de continuar incomodando.
- **De onde vêm as falas que você não esperava**: confirmado no log -
  são de um evento de "mudança de casa" (família se mudando, peças
  "BuzzNPC"/"DoorNPC" sob o objeto "Mudanza") rolando em algum lugar
  da cena, não necessariamente perto de você. Nosso scanner lê texto
  de toda a cena carregada, não só do que está perto do seu
  personagem - por isso aparecem mensagens de gente que você não vê.

**Próximo teste (com F12 ativado):**
1. Ative o guia (Home) até um alvo conhecido por trás de uma porta -
   a direção agora muda conforme você anda (seguindo a rota) em vez
   de só apontar reto pra parede?
2. As 2 entradas duplicadas da mesma porta (cofre/porão) ainda
   aparecem 2x, ou já é só uma?
3. Selecione um cofre com Page Up/Down - fala o que tem dentro dele?
4. Mesa suja aparece também na categoria "Missão"?
5. Vai ouvir bastante "Conversa ao redor" de novo - confirme se a
   fala do gato aparece nessa enxurrada (ou ainda nada).
6. "testei" quando terminar.

**15ª rodada (2026-06-21) - causa real do pathfinding sempre falhar,
reverte fala do conteúdo do cofre, corrige UI real do container, som
de parede em toque único, e refiltra só a família da mudança:**
- **ACHADO O BUG REAL DO PATHFINDING**: confirmado no log que TODA
  rota, mesmo a 3 telhas de distância, voltava como "sem rota"
  (`path=False` sempre) - a rodada passada não estava funcionando de
  verdade, só caindo no aviso antigo de "calculando rota...". Causa:
  o algoritmo do jogo trabalha com posições arredondadas pra uma
  grade de 0,25 unidade (`Utils.MJEACANINDN`), e a posição real do
  jogador quase nunca cai exatamente nessa grade - a checagem de
  "chegou no alvo" dentro do algoritmo nunca dava bate-certo, então
  sempre esgotava a busca e desistia. Corrigido: agora arredondo a
  posição do jogador e do alvo pra essa mesma grade antes de pedir a
  rota (mesma função que o jogo usa internamente). Precisa de teste
  real pra confirmar que resolveu de vez.
- **Revertido: fala do conteúdo do cofre no Page Up/Down** - você
  pediu pra tirar (prefere a UI real do jogo, que já abre certo como
  lista).
- **UI real do cofre corrigida**: o bug real estava na navegação por
  teclado da UI do jogo - slots liam o nome genérico do prefab ("New
  SlotUI Inventory") em vez do item, e um slot não lia nada.
  Encontrei no código decompilado que `SlotUI` expõe publicamente o
  item de dentro - agora a navegação lê o nome do item real (ou
  "Vazio" pra slot sem nada, em vez de silêncio).
- **Som de parede em toque único**: agora um toque rápido (não
  segurado) que não te move nada também toca o som, sem precisar
  segurar. Mantido o tempo de confirmação maior (0,6s, +100ms) só pra
  quando fica segurando continuamente.
- **Família da mudança refiltrada (só eles)**: a fala do gato
  continuou sem aparecer mesmo com TODO filtro de ambiente desligado
  (confirmado no log - nenhum rastro, nem da família, nem do gato,
  antes de desligar; com o filtro desligado, a família apareceu mas o
  gato não). Como abrir tudo não ajudou a achar o gato, voltei a
  filtrar só "BuzzNPC"/"DoorNPC"/"Mudanza" especificamente - outras
  falas de NPC continuam audíveis. A fala do gato ainda é um mistério
  - não é Bark UI de ninguém, não é resposta de diálogo, não é texto
  de UI nem texto flutuante no mundo (já ampliamos a busca pra isso
  também). Pode ser um sistema totalmente diferente que ainda não
  identifiquei - se puder me dizer EXATAMENTE como aciona essa fala
  (interagindo com o gato? andando perto? num momento de história
  específico?), ajuda a procurar no lugar certo.

**Próximo teste (com F12 ativado):**
1. Ative o guia (Home) até um alvo atrás de uma parede/porta de
   verdade - a rota funciona agora (segue um caminho real, vira nos
   lugares certos) em vez de cair no "calculando rota..." pra sempre?
2. Abra um cofre pela UI normal do jogo (não pelo Page Up/Down) -
   os slots agora leem o nome do item certo (ou "Vazio")?
3. Dê um toque ÚNICO e rápido contra uma parede - toca o som agora?
4. Segure contra a parede - ainda toca (com um pouquinho mais de
   demora pra começar)?
5. Gato: me diga exatamente como você aciona essa fala dele.
6. "testei" quando terminar.

**16ª rodada (2026-06-21) - bug do filtro encontrado, rota
redesenhada em etapas (não mais por waypoint cru), diagnósticos pro
cofre e pro toque de parede, resposta sobre câmera/rotação:**
- **Filtro da família "Mudanza" - bug real encontrado**: confirmado
  no log que a família continuou sendo anunciada mesmo depois de eu
  "refiltrar". Causa: na 14ª rodada eu REMOVI o `continue` que
  silenciava (pra abrir tudo e caçar a fala do gato), e na 15ª eu só
  estreitei a LISTA de nomes filtrados, mas esqueci de devolver o
  `continue` - a lista filtrada existia mas não tinha efeito nenhum.
  Corrigido agora: o `continue` está de volta, só pra essa família.
- **Rota redesenhada (prioridade reforçada por você)**: confirmado no
  log que a rota de fato passou a funcionar (`path=True`,
  "Pathfinding succeeded"), mas os números ficavam pulando/
  aumentando - causa: eu pedia uma rota nova a cada 2s, o que
  reiniciava o progresso o tempo todo, e cada ponto da rota bruta é
  só 0,25 unidade (meia telha) do anterior, gerando muitos avisos
  quase idênticos. Reescrevi: agora a rota crua é agrupada em ETAPAS
  por direção (ex: "4 pra cima, 3 pra direita, 2 pra baixo"), anuncio
  o PLANO COMPLETO uma vez quando a rota chega, e depois só vou
  contando pra baixo dentro da etapa atual, trocando de etapa quando
  ela termina. Pedido de rota nova ficou bem menos frequente (6s, só
  pra corrigir desvio, não mais o tempo todo).
- **Cofre "Vazio"**: não consigo confirmar sem log se é um cofre
  realmente vazio ou um bug na nossa leitura - adicionei log de
  diagnóstico (modo debug) mostrando exatamente o que achei no slot,
  pra eu confirmar com certeza no próximo teste.
- **Toque único na parede ainda sem som**: adicionei log de
  diagnóstico também - preciso saber se você testou com WASD (as
  teclas de movimento de verdade) ou com as setas do teclado. As
  SETAS não movem o personagem (foi pedido seu há várias rodadas,
  pra ficarem livres pra navegação de menu/diálogo) - se testou com
  seta, não tem som de parede porque não houve tentativa de
  movimento nenhuma, não é bug.
- **Pergunta sobre câmera/rotação - respondida**: o jogo é de visão
  aérea fixa (de cima), o personagem só olha pra uma de 4 direções
  (cima/baixo/esquerda/direita - confirmado no enum `Direction`
  usado em todo o sistema de movimento/animação do jogo). NÃO existe
  rotação livre por mouse - então não precisa de "alinhar com o
  norte", o personagem já está sempre numa dessas 4 direções fixas,
  e o som de virar (stand.wav) já cobre exatamente essa troca.

**Próximo teste (com F12 ativado):**
1. Família da mudança (Buzz/DoorNPC) - voltou a ficar quieta?
2. Ative o guia (Home) - ouve o "Rota: X pra cima, Y pra direita..."
   completo uma vez, e depois só a contagem da etapa atual diminuindo
   (sem ficar pulando pra cima e pra baixo)?
3. Toque único de PAREDE (com WASD, não seta) - toca som agora?
4. Cofre: abra um que você lembra ter item dentro - ainda diz
   "Vazio"? Se sim, me diga qual cofre é (posição/contexto) pra eu
   cruzar com o log.
5. "testei" quando terminar.

**17ª rodada (2026-06-21) - bug real do toque único de parede
encontrado, som de parede com duração mínima, fluxo do guia
reescrito, contagem por eixo, porta com telha exata, correção mais
rápida ao se afastar:**
- **Família da mudança - confirmado funcionando** (você relatou que
  parou de falar).
- **Toque único de parede - bug real encontrado**: a lógica antiga só
  avaliava o toque no momento em que NENHUMA tecla de movimento
  estivesse pressionada - mas ao apertar rápido várias vezes, quase
  nunca existe um frame totalmente "sem tecla" entre um toque e o
  próximo, então essa avaliação quase nunca rodava. O que tocava de
  fato era o som de segurar contínuo, acumulando o tempo parado nos
  intervalos entre toques até passar de 0,6s (por isso "só com 6
  toques ou mais"). Corrigido: agora cada toque agenda sua própria
  checagem 0,15s depois, independente de soltar ou não a tecla.
- **Duração do som de toque único**: agora dura pelo menos ~1
  segundo (toca em loop e corta nesse tempo), mesmo que o arquivo
  `parede.wav` em si seja mais curto.
- **Fluxo de ativação do guia reescrito**: confirmado o bug - ao
  apertar Home, a mensagem errada (linha reta antiga) tocava antes da
  rota real chegar. Agora: "Calculando rota..." imediatamente, depois
  "Rota calculada. [primeira etapa]" quando a rota chega (só na
  ativação - atualizações de rota em segundo plano enquanto anda não
  repetem esse aviso, só a contagem normal continua).
- **Contagem por eixo**: a contagem de cada etapa agora mede só o
  eixo daquela direção (ex: numa etapa "pra cima", só a distância
  vertical conta) em vez da distância em linha reta até o ponto -
  isso deve resolver o "fica todo bugado quando me afasto", já que
  andar de lado não fazia sentido influenciar a contagem de uma
  etapa vertical.
- **Porta - telha exata**: achei no código decompilado que a porta
  tem uma lista própria (`freeNodesOnOpen`) com a(s) posição(ões)
  exatas da telha onde se anda pra atravessar - bem diferente da
  posição "crua" do objeto Porta que eu estava usando antes (provável
  causa do "diz que está pra cima mesmo estando na porta"). Portas
  agora usam essa posição real quando disponível.
- **Correção mais rápida ao se afastar**: além da atualização
  periódica (a cada 6s), agora também recalcula a rota IMEDIATAMENTE
  se perceber que você se afastou bastante da linha da etapa atual,
  em vez de esperar o próximo ciclo de 6s.

**Próximo teste (com F12 ativado):**
1. Toque único de parede com WASD - toca de primeira agora, e dura
   mais que antes?
2. Ative o guia (Home) - ouve "Calculando rota...", depois "Rota
   calculada. [etapa]", sem nenhuma mensagem errada no meio?
3. Ainda na porta (ou bem perto) - diz "Você chegou" corretamente
   agora, sem dizer "pra cima" estando nela?
4. Se afastar de propósito do caminho - a contagem continua fazendo
   sentido (sem parecer bugada) e corrige rápido?
5. Cofre "Vazio": se ainda achar um cofre com item que diz "Vazio",
   me diga qual/onde é.
6. "testei" quando terminar.

**18ª rodada (2026-06-21) - bug real das "duas rotas" encontrado e
corrigido com log, cofre "Vazio" resolvido (era de verdade o
esfregão), som de toque reduzido, guia desliga ao chegar, porta
fechada bloqueando rota explicado:**
- **"Duas rotas" - confirmado e corrigido com o log**: o log mostrou
  exatamente o bug, com horário: "Calculando rota..." falado, e 8ms
  depois "9 pra baixo, 2 pra esquerda" falado por cima (cortando o
  primeiro) - a tela de atualização por passo roda todo frame
  enquanto o guia está ativo, e a primeira chamada dela passava direto
  (não tinha posição anterior pra comparar ainda) e caía no aviso
  antigo de linha reta, ANTES da rota de verdade chegar (que demorou
  ~1,2s). Corrigido: essa atualização agora fica quieta enquanto
  espera a primeira rota chegar.
- **Cofre "Vazio" - resolvido, e era bug nosso mesmo**: o log de
  diagnóstico confirmou - o item ERA encontrado certinho
  ("item=itemMop"), mas a tradução do nome vinha vazia, caindo no
  "Vazio" por engano. Achei a causa no código decompilado: alguns
  itens (como o esfregão) usam uma chave de tradução diferente
  ("Items/item_name_<id>"), não o nome direto - e o próprio `Item` do
  jogo já tem um método público que sabe lidar com isso e nunca
  retorna vazio para um item real. Troquei pra usar esse método em
  vez da busca direta, no cofre e na lista de objetos próximos.
- **Som de toque único reduzido**: você relatou tocar demais ao
  testar toques repetidos contra a parede - cada toque tocava o som
  inteiro de 1s, ficando muito sobreposto. Reduzido pra 0,5s.
- **Guia desliga ao chegar**: ao dizer "Você chegou", agora desativa
  o guia automaticamente, sem precisar apertar Home de novo pra
  desligar.
- **Porta fechada bloqueando rota - confirmado no código, não é
  bug**: achei no decompilado que a telha de passagem da porta só
  fica "andável" pra rota enquanto ela está ABERTA - fechada, ela
  literalmente bloqueia a busca de rota (mesma regra que vale pros
  NPCs do próprio jogo). Bate com sua suspeita sobre a adega. Quando
  isso acontece, "Não encontrei uma rota até lá." já é falado (não
  fica mudo) - confirmei no log que essa frase realmente toca nesses
  casos.
- **Contagem de telhas ainda sob investigação**: você não conseguiu
  confirmar se está dizendo o dobro ou a metade do que deveria.
  Adicionei a posição exata do jogador e do ponto de destino da etapa
  no log de cada anúncio, pra eu calcular a distância real percorrida
  entre dois anúncios da rodada que vem e confirmar com número, sem
  chute.

**Próximo teste (com F12 ativado):**
1. Ative o guia até um alvo na MESMA área (sem porta fechada no
   meio) - só "Calculando rota...", depois "Rota calculada. [etapa]",
   sem mensagem dupla?
2. Toque único de parede repetido várias vezes - ficou mais leve
   agora (sem som empilhando tanto)?
3. Chegue ao destino - o guia desliga só com o "Você chegou", sem
   precisar apertar Home?
4. Abra o cofre/baú ao lado da cama de novo - o esfregão aparece com
   o nome certo agora?
5. "testei" quando terminar (a contagem de telhas eu vou confirmar
   olhando o log desta rodada, não precisa testar isso de novo
   especificamente).

**19ª rodada (2026-06-21) - contagem de telhas confirmada correta no
log, bug real da direção que "trava" encontrado, alvos genéricos
dentro de parede corrigidos, "Continuando" removido, som reduzido pra
300ms:**
- **Contagem de telhas - CONFIRMADA correta, não era bug**: usei a
  posição exata que já estava sendo logada (pedido da rodada
  passada) pra calcular a conta na mão em várias linhas do log -
  bate certinho com 0,5 unidade = 1 telha em todos os casos que
  conferi. O número em si nunca esteve errado.
- **O bug real era a DIREÇÃO travada, não a contagem**: confirmado no
  log - quando você passa do ponto da etapa (ex: precisava ir até
  x=14.75 mas continuou andando pra direita até x=16.54), a distância
  cresce de verdade (então o número subir está certo), mas eu
  continuava dizendo "direita" porque a direção de cada etapa ficava
  fixa desde quando a rota foi calculada, sem nunca reavaliar pra
  onde ir a partir de onde você está AGORA. Por isso "pediu direita
  mas a distância só aumentou" - você tinha de fato passado do ponto
  e devia voltar (esquerda), mas eu não percebia isso. Corrigido: a
  direção falada agora é sempre recalculada a partir da sua posição
  atual.
- **Alvos genéricos (barril, etc.) com ponto dentro da parede -
  corrigido**: confirmado no log que o barril nunca conseguia rota (
  "sem rota ainda" por mais de um minuto seguido) - mesma causa do
  problema da porta, mas pra objetos comuns: a posição exata do
  objeto não é necessariamente uma telha andável. Portas já tinham
  conserto próprio; agora objetos comuns (Placeable) também usam um
  ponto ajustado - bem na borda do objeto, do lado de onde você está,
  em vez do centro dele (que pode estar dentro da parede/objeto).
  Ainda precisa de teste real pra confirmar que resolve de fato.
- **"Continuando" removido**: você reclamou que não dizia nada útil.
  Agora, assim que o eixo da etapa atual termina, já troca pra
  próxima etapa (ou pro outro eixo, na etapa final) em vez de ficar
  parado nessa mensagem.
- **Som de toque único reduzido pra 0,3s** (era 0,5s).
- Sobre "às vezes sinto que andei mais de uma telha por toque": pode
  ser inércia/aceleração do próprio personagem variando a distância
  de um toque pro outro - não é algo que dependa da nossa contagem
  (que confirmei correta acima).
- Sobre colocar checkpoints/coordenadas fixas por área: por enquanto
  os dois bugs encontrados (direção travada + ponto dentro da parede)
  explicam boa parte da inconstância relatada - prefiro testar se
  isso já resolve antes de partir pra uma reformulação maior, que
  seria bem mais trabalho.

**Próximo teste (com F12 ativado):**
1. Repita o teste de ir até a "Porta" andando de propósito pra além
   do ponto - a direção agora corrige (diz pra voltar) em vez de
   continuar dizendo a mesma direção com número crescendo?
2. Rastreie o barril (ou outro objeto comum) - consegue rota agora,
   sem ficar "sem rota" pra sempre?
3. Ao longo da rota, "Continuando..." ainda aparece, ou já troca de
   etapa/eixo direto?
4. Toque único de parede - tamanho do som bom agora?
5. "testei" quando terminar.

**20ª rodada (2026-06-21) - troca de etapa mais precisa, ponto de
chegada do baú mais perto, busca de rota mais rápida, som a 200ms:**
- **Troca de etapa cedo demais - corrigido**: você relatou andar só
  um pouco e já mudar de direção. Causa: eu usava o número JÁ
  arredondado pra decidir quando trocar de etapa, então bastava
  chegar a um quarto de telha de distância (qualquer coisa que
  arredonda pra "0") pra trocar antes da hora. Agora a troca usa a
  distância real (não arredondada) com uma margem mais apertada,
  independente do número falado.
- **Ponto de chegada do baú mais perto**: você relatou precisar de
  várias tentativas pra ouvir "Você chegou" num baú. O ponto de
  aproximação (calculado a partir do colisor do objeto, desde a
  rodada passada) tinha uma margem de meia telha além da borda -
  reduzida pra um quarto de telha, mais perto de onde você realmente
  para pra interagir.
- **Busca de rota mais rápida (em teste)**: confirmei no código que a
  demora de mais de 1 segundo não é fila/espera - é o tempo real de
  busca do próprio algoritmo do jogo. Reduzi o limite de "nós" que ele
  pode explorar (3000 → 1500) - deve calcular mais rápido pra
  distâncias normais dentro da taverna, mas existe a chance de uma
  rota bem mais longa/complicada falhar onde antes funcionava. Preciso
  que você teste bastante essa rodada pra eu confirmar se isso não
  trouxe nenhum "sem rota" novo.
- **Som de toque único a 200ms** (era 300ms).

**Próximo teste (com F12 ativado):**
1. Ande só um pouco (não muito) em direção a um alvo - a etapa troca
   na hora certa agora, nem antes nem muito depois?
2. Vá até o mesmo baú de antes - "Você chegou" dispara mais rápido,
   sem precisar de várias tentativas?
3. As rotas calcularam mais rápido? Apareceu algum "Não encontrei uma
   rota até lá." em lugar onde antes encontrava?
4. Som de toque - tamanho bom agora?
5. "testei" quando terminar.

**21ª rodada (2026-06-21) - revalidação, mudança de abordagem pra
"chegou" (evento em vez de distância), log de toques vs. número
pedido, conserto do "0 pra baixo", som a 260ms:**
- **Revalidação feita**: reconferi as descobertas anteriores (passo
  da grade do WorldGrid, freeNodesOnOpen da porta, contagem por eixo)
  relendo o código de novo - seguem batendo. O ponto realmente fraco
  era o "ponto de aproximação" calculado por nós pra objetos comuns
  (baú, barril) - uma estimativa geométrica nossa, não um dado exato
  do jogo como a porta tem.
- **"Chegou" agora usa um EVENTO do jogo em vez de distância/
  arredondamento** (sugestão sua): achei no código decompilado que
  praticamente todo objeto interagível (porta, baú, mancha, etc.)
  implementa `IProximity.IsAvailableByProximity` - é o mesmo sinal que
  o próprio jogo usa pra decidir se mostra "[E]/[Q] ..." na tela.
  Antes de checar distância, agora pergunto direto pro objeto "já dá
  pra interagir comigo?" - se ele disser que sim, "Você chegou" na
  hora, sem depender da nossa estimativa geométrica de onde "deveria"
  ser o ponto de chegada. Isso deve resolver a inconstância do baú
  (a porta já funcionava bem porque tem dado próprio exato -
  freeNodesOnOpen -, o baú não tinha equivalente).
- **Log de toques vs. número pedido** (seu pedido): agora cada vez
  que uma etapa termina, registro no log "número pedido=X, toques de
  movimento usados=Y" - posso conferir com números reais se a
  contagem está calibrada certa, sem chute.
- **"0 pra baixo" corrigido**: confirmado que era possível mesmo - a
  margem mais apertada da rodada passada criou uma faixa onde a
  distância já arredondava pra "0" mas ainda não passava da etapa.
  Nunca mais fala "0", o mínimo agora é "1".
- **Som de toque único a 260ms** (era 200ms).
- Sobre checkpoints/coordenadas fixas: ainda não fiz - quero ver se a
  mudança pro evento de proximidade (que ataca direto a causa do
  "baú inconsistente") já resolve antes de partir pra uma reformulação
  maior.

**Próximo teste (com F12 ativado):**
1. Vá até o baú/barril de antes - "Você chegou" dispara direto agora
   (sem precisar ficar tentando achar o ponto certo)?
2. Ainda ouve algum "0 pra [direção]"?
3. Ande pouco a pouco em direção a um alvo - a troca de etapa parece
   mais alinhada com o quanto você realmente andou?
4. Som de toque - tamanho bom agora (260ms)?
5. "testei" quando terminar.

**22ª rodada (2026-06-21) - revertido o "evento" da rodada passada
(estava errado), ajuste de volta na margem de troca de etapa:**
- **"Chegou" instantâneo na porta - bug real, raiz confirmada e
  REVERTIDA**: você relatou que ao tentar achar a porta, ele já dizia
  "chegou" sem você nem saber do canto. Confirmado no log
  (`Calculando rota... -> Rota calculada. Você chegou` em ~1.7s, com
  você ainda longe). Causa: a mudança da rodada passada (usar
  `IProximity.IsAvailableByProximity` como "evento de chegada") estava
  baseada numa leitura errada da minha parte - fui ver a implementação
  real desse método no código decompilado (`Placeable`/`Door`) e,
  apesar do nome, ele NÃO verifica distância nenhuma - checa coisas
  como se o item pode ser pego, se está em modo de decoração, zona
  alugada, etc. Revertido por completo - "chegou" volta a usar só
  distância/arredondamento como antes.
- **Baú ainda preso oscilando "direita"/"esquerda" por mais de um
  minuto - causa achada no mesmo log**: a margem mais apertada de 2
  rodadas atrás (0,15) ficou ESTRITA demais pra um trecho curto de
  rota (2 telhas) - a distância real ficava oscilando entre 0,17 e
  0,49, frequentemente ACIMA dessa margem, nunca resolvendo de vez.
  Voltei pra margem original (0,25, a mesma que já era usada pro
  número arredondado) - deve resolver esse preso específico, mas
  ainda é uma aproximação por distância, então trechos muito curtos
  podem continuar sensíveis.
- Removido o campo extra que tinha sido adicionado pra carregar a
  referência do objeto (usado só pelo "evento" que foi revertido) -
  sem código morto sobrando.
- Perguntei se valeria a pena já investir na reformulação maior
  (checkpoints/coordenadas fixas) dado o vai-e-vem de precisão nos
  trechos curtos - aguardando sua decisão.

**Próximo teste (com F12 ativado):**
1. Vá até a porta de novo - "Você chegou" só dispara quando você
   realmente chega, não mais na hora?
2. Vá até o baú/barril - ainda fica preso oscilando "direita"/
   "esquerda" sem resolver, ou já se firma numa direção e termina?
3. "testei" quando terminar.

**23ª rodada (2026-06-21) - confirmado "razoável", filtro por área,
análise de som direcional de parede/porta:**
- **"Funcionando em razoabilidade"** - confirmado pelo usuário que a
  reversão do "evento" + folga na margem (22ª rodada) melhorou.
- **Filtro por área implementado**: pedido - itens fora da taverna
  não devem aparecer na lista até o jogador sair dela; itens dentro
  (mesmo bloqueados, tipo a porta da adega fechada) podem continuar
  aparecendo, já que tecnicamente tem caminho assim que a porta abrir.
  Implementado comparando a `Location` (conceito amplo, de prédio) do
  jogador com a de cada item - confirmado que a adega tem a MESMA
  Location da taverna principal (não disparava o aviso de "área
  diferente" em testes anteriores), então esse filtro já distingue
  "fora do prédio" (filtra) de "dentro, só atrás de porta" (mantém).
  Não filtra ainda por "rota existe de verdade" (custaria caro demais
  pedir pathfinding pra cada item da lista toda vez) - se a escadaria
  ainda não aparecer depois desse filtro, preciso de mais detalhe de
  onde ela está pra investigar especificamente.
- **Análise do som direcional de parede/porta/corredor** (pedido -
  ainda NÃO implementado, só análise, conforme pedido): achei e
  DESCARTEI uma pista falsa (`Utils.EJPFCKFEMJF`, usado no
  "avoidWalls" do pathfinding, parecia promissor mas na verdade só
  checa altura/coordenada Y - mesmo tipo de erro do "evento" da
  rodada passada, então não vou usar). Caminho recomendado: usar a
  física 2D do PRÓPRIO Unity (`Physics2D.Raycast`) num raio curto nas
  4 direções a partir do jogador - independe de adivinhar a função
  certa do jogo, mesmo princípio já comprovado no som de parede atual
  (que mede movimento real, não uma flag interna). Plano em 2 fases:
  1) Som de parede direcional em loop (lado esquerdo/direito com
     pan, cima/baixo com tom mais agudo/grave), usando esse raycast;
  2) Som de porta em loop por proximidade (já temos as posições de
     todas as portas rastreadas, só falta o loop).
  Não acho necessário um sistema separado pra "corredores" - como o
  próprio usuário notou, a LACUNA no som de parede já indica a
  abertura, sem precisar detectar corredor como conceito separado.
  Aguardando os arquivos de som pra implementar.

**Próximo teste:**
1. A escadaria ainda não aparece na lista? Se não, me diga onde ela
   fica (perto de qual outro objeto/sala) pra eu investigar.
2. Itens de fora da taverna já não aparecem mais na lista enquanto
   você está dentro?
3. Quando tiver os sons de parede/porta, me envie - vou implementar o
   plano de 2 fases acima.

**24ª rodada (2026-06-21) - som direcional de parede implementado
(fase 1, experimental), confirmado por que algumas rotas ainda caem
no formato antigo, diagnóstico pra "Torneira":**
- **Escadaria - confirmado no log que ela JÁ aparece na lista**
  (recebeu orientação, "Guidance to 'Escadaria'") - o que falha é
  achar uma rota até ela, sempre "Pathfinding returned no route". Bate
  com sua própria suspeita: provavelmente ainda está indisponível
  (trancada/bloqueada) - igual a porta da adega fechada, onde a
  telha de passagem não conta como andável pro jogo até abrir.
- **Por que algumas rotas ainda saem em "duas direções" (formato
  antigo) em vez de etapas**: confirmado no log - SÓ acontece quando a
  busca de rota de fato falha (`Pathfinding returned no route`), o que
  só ocorre pra destinos genuinamente sem caminho andável agora
  (porta/escadaria fechada, ou - a confirmar - "Torneira"). Quando a
  busca funciona (porta principal, baú), já vem em etapas
  normalmente. Não tem como dar etapas pra um lugar que o próprio jogo
  diz que não tem caminho andável ainda - isso é o jogo, não um bug
  nosso. Adicionei um log de diagnóstico no cálculo do ponto de
  aproximação (`GetApproachPosition`) especificamente pra confirmar se
  o problema da "Torneira" é o mesmo tipo (porta/passagem bloqueada)
  ou outra coisa (ponto calculado cai num lugar errado, tipo objeto
  preso na parede).
- **Som direcional de parede implementado (FASE 1, experimental)**:
  usando os dois arquivos que você forneceu (`baixo.wav` pra parede
  abaixo, `cima direita e esquerda.wav` pras outras 3 direções,
  com pan esquerda/direita e centro pra cima) - toca em loop
  continuamente enquanto andando, em até 4 direções ao mesmo tempo
  (ex: parede embaixo E à esquerda, num canto). Usei a física do
  próprio Unity (não uma função do jogo) pra detectar parede, como
  combinado - ainda EXPERIMENTAL, preciso que teste bastante pra
  confirmar se detecta parede de verdade sem "falso positivo" (tocar
  sem ter parede) ou "falso negativo" (não tocar com parede ali).
  Fase 2 (som de porta por proximidade) ainda não implementada -
  primeiro precisamos validar a fase 1.

**Próximo teste (com F12 ativado):**
1. Ande perto de paredes em todas as direções - o som certo toca pro
   lado certo (embaixo com o som grave, os outros com o som
   compartilhado, pan esquerda/direita, centro pra cima)?
2. Algum momento em que o som tocou sem parede ali (falso positivo),
   ou não tocou com parede claramente ali (falso negativo)? Me diga
   onde, se notar.
3. Vá até a "Torneira" - ainda sem rota? Vou conferir o novo log de
   diagnóstico do ponto de aproximação dela.
4. "testei" quando terminar.

**25ª rodada (2026-06-21) - som direcional corrigido (causa real
encontrada: ponto único de checagem, não cobria corredor estreito nem
cima/baixo), folga pra suavizar pausas:**
- **"Não ouvi cima e baixo", "nada nos corredores" - causa real
  encontrada (não só ajuste, achei o motivo)**: a primeira versão
  checava um ÚNICO ponto fixo a cerca de uma telha de distância. Num
  corredor estreito (de uma telha de largura), a parede pode estar
  bem mais perto que isso - o ponto checado "passava direto" por ela.
  E um raio (`Raycast`) simples só reporta o PRIMEIRO objeto que
  encontra - como o raio começa bem na posição do jogador, é bem
  provável que o primeiro "objeto" encontrado seja o PRÓPRIO jogador,
  escondendo qualquer parede de verdade atrás dele - explicaria
  cima/baixo nunca acertarem (mais provável de bater o colisor do
  próprio personagem primeiro nessas direções, dependendo do formato
  do colisor dele). Troquei pra checar o caminho INTEIRO numa direção
  (`RaycastAll`) e pego o mais próximo que não seja o próprio
  jogador, em qualquer distância até pouco mais de uma telha - cobre
  parede bem perto (corredor estreito) e parede mais longe (sala
  aberta).
- **"Som com pausas, quero contínuo"**: adicionei uma pequena folga
  (0,15s) antes de considerar que a parede "sumiu" - se a detecção
  cambalear de um frame pro outro perto do limite, isso deve suavizar
  o efeito de pausa. Se ainda notar pausa depois disso, pode ser o
  próprio arquivo de som tendo um pequeno silêncio no início/fim (não
  algo que eu consiga corrigir só no código, precisaria de uma versão
  do arquivo cortada pra dar loop sem esse intervalo).
- **"Som tocou nos dois lados, mas parou no esquerdo quando abri a
  porta"** - isso é o esperado: a porta fechada conta como parede
  sólida ali; aberta, deixa de bloquear - bom sinal de que a detecção
  está funcionando.
- **"Objetos sem ser a porta não guiam em etapas"**: reforçando o que
  já confirmei na rodada passada via log - isso só acontece quando a
  busca de rota genuinamente falha pra aquele destino específico (sem
  caminho andável agora). Se notar isso de novo num objeto que você
  tem certeza que tem caminho livre até ele, me diga qual objeto
  exatamente pra eu conferir no log dessa vez específica.

**Próximo teste (com F12 ativado):**
1. Cima e baixo agora tocam quando há parede nessas direções?
2. Em corredores estreitos (passagem de uma telha), toca dos dois
   lados agora?
3. As pausas no som ficaram menores/desapareceram?
4. "testei" quando terminar.

**26ª rodada (2026-06-21) - sons funcionando, 4 arquivos de parede
distintos, retry de rota pra destinos bloqueados:**
- **"Sons funcionando bem"** - confirmado que a correção da rodada
  passada (RaycastAll + folga) resolveu cima/baixo e corredores.
- **4 arquivos de som de parede, um por direção**: você forneceu
  `cima.wav` e `direita.wav` novos (substituindo o arquivo
  compartilhado "cima direita e esquerda.wav", que não existe mais),
  junto com os já existentes `baixo.wav`/`esquerda.wav`. Cada direção
  agora tem seu próprio som dedicado.
- **Rota com "duas direções" pra portas/objetos bloqueados -
  melhorado com retry**: você reforçou que quer etapas SEMPRE, mesmo
  quando o destino exato está bloqueado (porta fechada). Implementei
  uma segunda tentativa automática: se a rota até o ponto exato falhar,
  tento de novo visando um ponto uma telha antes dele (na mesma
  direção, voltando pro lado do jogador) - já que normalmente só a
  última telha (a soleira da porta) é que fica bloqueada enquanto
  fechada, isso deve te levar bem próximo, em etapas reais, mesmo sem
  conseguir o destino exato. Se mesmo essa tentativa falhar, ainda cai
  no aviso "Não encontrei uma rota até lá."

**Próximo teste (com F12 ativado):**
1. Vá até a porta da adega (fechada) ou outro item antes "sem rota" -
   agora vem em etapas (te levando perto, mesmo sem o destino exato),
   ou ainda cai no formato antigo?
2. Os 4 sons de parede direcionais continuam bons (cima/baixo/
   esquerda/direita)?
3. "testei" quando terminar.

**27ª rodada (2026-06-21) - 3 correções confirmadas por log + 2 ajustes
pedidos diretamente:**
- **Falso positivo do som "cima" num canto - achado e corrigido**: log
  confirmou exatamente o que o usuário sentiu - o raio "cima" estava
  acertando a CAMA (`1130 - Cama del Jugador(Clone)`, dist=0.28), não
  uma parede de verdade. A primeira versão contava qualquer colisor
  sólido, incluindo mobília. Achei um componente dedicado no jogo
  (`PhysicalSpaceWall`, usado pelo próprio sistema de "esconder parede
  na frente da câmera") e passei a só contar como parede um colisor
  que tenha esse componente - mobília/NPCs não acionam mais o som.
- **"Mostra duas direções de uma vez" no aviso de "sem rota ainda" -
  corrigido**: esse aviso (usado só quando a rota de verdade falha)
  juntava as duas distâncias numa frase só. Troquei pra mostrar só o
  eixo maior por vez, igual ao sistema de etapas reais - a outra
  direção aparece naturalmente depois, quando o eixo atual chegar a 0.
  Também achei (relendo esse trecho pra resolver isso) que ele nunca
  convertia a distância pra "telha" (dividia por nada) - mostrava o
  dobro do valor certo. Corrigido junto.
- **`maxNodes` reduzido demais - confirmado pelo log e revertido em
  parte**: o log do "Bar" mostrou a rota falhando (mesmo com a nova
  segunda tentativa) toda vez que o jogador estava longe, e só
  funcionando quando ele já tinha chegado perto andando manualmente -
  ou seja, o orçamento de busca (`maxNodes`, baixado pra 1500 rodadas
  atrás pra deixar mais rápido) estava genuinamente pequeno demais pra
  rotas mais longas, não um caminho de fato bloqueado. Subido pra
  2500 (meio-termo entre o original 3000 e o 1500 que causou esse
  problema).
- **4 sons de parede com volume reduzido pra 60%**, a pedido.

**Próximo teste (com F12 ativado):**
1. O som de "cima" ainda toca em algum lugar sem parede de verdade
   ali (ex: perto de mobília)?
2. A rota "sem rota ainda" (pra coisas como a adega) mostra só uma
   direção de cada vez agora?
3. O "Bar" (e outros itens distantes) agora conseguem rota completa
   em etapas, mesmo de longe?
4. Volume dos sons está bom em 60%?
5. "testei" quando terminar.

**28ª rodada (2026-06-21) - rotas confirmadas certas; som sumiu, causa
ainda não confirmada (diagnóstico adicionado, não um conserto ainda):**
- **"Rotas estão certas agora"** - confirma os 3 consertos da rodada
  passada (parede x mobília, uma direção por vez, maxNodes).
- **"Não escuto mais nenhum som"** - investigado no log antes de
  mudar qualquer coisa (regra do projeto: confirmar antes de agir).
  Achado importante: o log dessa sessão tem ZERO linhas
  "CustomSounds:" - nem mesmo "loaded parede.wav", que sempre apareceu
  em toda sessão anterior, incluindo antes de eu tocar no volume. Ou
  seja, os sons pararam de CARREGAR (não é o valor 60% - isso não
  deixaria mudo, só mais baixo). Ainda não sei exatamente onde a
  cadeia quebrou (não vou arriscar um "conserto" sem saber a causa
  real - mesma regra que já me salvou de erros antes). Adicionei logs
  de diagnóstico sem condição de debug nos pontos-chave
  (`EnsureLoaded` chamado, coroutine `LoadAll` iniciada) e um try/catch
  em volta da chamada, pra próxima sessão mostrar exatamente até onde
  a cadeia chega.
- Por enquanto, NÃO toquei no valor de 60% - seria arriscar mexer de
  novo sem entender a causa raiz, indo contra o pedido de cautela.

**Próximo teste (com F12 ativado), bem específico:**
1. Jogue normalmente até onde os sons deveriam tocar.
2. Diga "testei" - vou olhar se aparece "CustomSounds: EnsureLoaded
   called" e "LoadAll coroutine started" no log, e se algum som tocou
   de verdade ou continua mudo.

**29ª rodada (2026-06-21) - causa real do "som direcional mudo"
encontrada (era o conserto da rodada passada, não o carregamento):**
- **Carregamento confirmado OK**: o log mostrou os logs de diagnóstico
  novos certinho (`EnsureLoaded called`, `LoadAll coroutine started`,
  e os 7 arquivos carregando). Não era isso.
- **"Som de bater na parede funciona, mas o direcional não" - achei a
  causa real**: o log mostrou 172 checagens de parede direcional na
  sessão, NENHUMA detectando nada (nem perto da "WallBack", que
  funcionava bem antes). O filtro que adicionei na rodada passada
  (exigir o componente `PhysicalSpaceWall`) excluiu as paredes de
  verdade também, não só a cama - tentativa errada, confirmada pelo
  próprio log (a mesma regra que vinha me protegendo desse tipo de
  erro). Troquei pra um filtro inverso: em vez de exigir "é parede",
  excluo "é mobília/decoração" - objetos desse tipo são instâncias
  criadas em tempo real pelo jogo, e o Unity sempre coloca "(Clone)"
  no nome desses casos (confirmado: "Cama del Jugador(Clone)" tinha,
  "WallBack" não tinha) - geometria de parede de verdade não é
  instanciada assim, nunca tem esse sufixo.

**Próximo teste (com F12 ativado):**
1. O som direcional de parede volta a tocar perto de paredes de
   verdade?
2. Ainda toca perto de mobília (ex: a cama) sem parede ali?
3. "testei" quando terminar.

**30ª rodada (2026-06-21) - regressão da porta em corredor corrigida,
+ som por item com tom/lado, + som distinto de "bater em item":**
- **"Som direcional não toca os dois lados + baixo na porta fechada,
  em corredor estreito" - causa achada**: portas TAMBÉM são objetos
  instanciados em tempo real (têm "(Clone)" no nome, igual mobília) -
  o filtro da rodada passada (excluir tudo que tem "(Clone)") também
  excluía portas, mesmo fechadas funcionando exatamente como parede.
  Adicionei uma exceção: se o objeto tem um componente de Porta, conta
  como parede mesmo sendo "(Clone)" - mobília comum continua excluída.
- **Som por item (você forneceu `baú.wav`, `cama.wav`, `mesa.wav`,
  `torneira.wav`)**: agora, ao ficar perto de um item reconhecido, toca
  o som daquele item específico em vez do som genérico de "tem item
  aqui" - e com agudo/grave indicando se está na sua frente (em cima,
  agudo) ou atrás (embaixo, grave), ou tocando no lado certo
  (esquerda/direita) se a direção dominante for lateral, igual você
  descreveu.
- **Som de "bater em item" (`batendo em item.wav`)**: ao ficar
  travado tentando andar contra algo que NÃO é parede (porta fechada,
  mobília, etc.), toca esse som em vez do som de bater na parede -
  tanto no toque rápido quanto no travamento contínuo. Aqui, ao
  contrário do som ambiente acima, porta conta como "item", não
  parede, conforme você pediu.

**Próximo teste (com F12 ativado):**
1. Numa porta fechada em corredor estreito, o som direcional volta a
   tocar dos dois lados + embaixo?
2. Perto de um baú/cama/mesa/torneira, toca o som específico daquele
   item, com agudo/grave ou lado certo conforme a direção?
3. Bater numa porta fechada ou mobília dá o som novo "bater em item",
   diferente do som de bater na parede?
4. "testei" quando terminar.

**31ª rodada (2026-06-21) - causa real da porta sem som "embaixo"
achada (não era física), som de item virou loop contínuo com raio e
escalonamento:**
- **"Na porta, virado pra ela, som de baixo não toca" - causa real
  achada**: confirmei no log (parado a 0.3 de distância da porta da
  adega por vários segundos, direção certa sempre "nada"). Fui ver o
  código decompilado de novo: porta fechada bloqueia o caminho através
  do sistema de grade do PRÓPRIO JOGO, não por colisão física - não
  existe colisor físico ali pra detectar de jeito nenhum, então minha
  checagem de física nunca teria como achar isso, em nenhuma versão.
  Troquei pra checar diretamente a posição real do limiar da porta
  (a mesma informação que já uso pra calcular rota até ela) quando ela
  está fechada - mais confiável que esperar um colisor físico no lugar
  certo.
- **Som de corredor só do lado que o personagem está virado**: ainda
  não confirmei a causa raiz disso com certeza (o log que vi mostrava
  um lado bem perto e o outro sem nada por vários segundos, mas não
  tenho prova de que era de fato um corredor estreito de verdade nesse
  trecho específico, pode ter sido só uma parede de um lado só). Com a
  porta corrigida acima, se isso ainda persistir num corredor sem
  porta nenhuma por perto, me avise especificamente - aí vou conseguir
  cruzar log + posição com mais certeza.
- **Som de item virou consciência contínua, não só no prompt de
  ação**: a cada 1 segundo, todo item com som próprio (baú/cama/mesa/
  torneira) dentro de 6 telhas toca seu som, com agudo/grave ou lado
  conforme a direção. Vários itens próximos um do outro tocam em
  sequência (0,3s de intervalo entre cada), não ao mesmo tempo, pra não
  embolar - como pedido.

**Próximo teste (com F12 ativado):**
1. Na porta da adega fechada, toca o som "embaixo" (ou o lado certo,
   dependendo de como você está posicionado) agora?
2. O som de itens toca em loop a cada segundo dentro de ~6 telhas, com
   vários itens próximos tocando em sequência (não ao mesmo tempo)?
3. Se ainda notar o problema do corredor "só o lado que estou virado"
   em algum lugar SEM porta por perto, me diga onde especificamente.
4. "testei" quando terminar.

**32ª rodada (2026-06-21) - porta da entrada principal ainda não toca
os 3 lados (diagnóstico bruto adicionado), raio de item reduzido pra
4, bug real achado no som de "bater em item" em porta fechada:**
- **Achado importante (não é sobre este bug, mas explica mistérios de
  rodadas passadas)**: confirmei que `DebugLogger.LogState` só
  escreve no log se o modo debug (F12) já estiver ativo NO MOMENTO da
  chamada - não é "sempre grava, só não mostra". Como você liga o F12
  alguns segundos DEPOIS do jogo carregar, o carregamento dos sons (que
  acontece assim que o jogo fica pronto) sempre rodou normalmente,
  só nunca apareceu no log a tempo. A "causa" que eu apontei rodada
  28 (carregamento quebrado) nunca existiu de verdade - era só esse
  atraso do F12. Sem prejuízo prático (os sons sempre carregaram), mas
  registrando pra não repetir esse caminho de novo.
- **Porta do salão principal ainda não toca os 3 lados** - a correção
  da rodada passada (checar o limiar da porta diretamente) ainda não
  resolveu esse caso específico, e não tenho certeza do motivo exato
  ainda (pode ser o produto escalar de alinhamento, pode ser outra
  coisa) - adicionei um log bem detalhado (posição do nó, distância,
  alinhamento) que vai aparecer no próximo teste com F12 ativado,
  pra eu conseguir ver exatamente onde isso falha em vez de tentar de
  novo no escuro.
- **Raio do som de item reduzido de 6 para 4 telhas**, e **achado um
  bug real no som de "bater em item"**: ele nunca conseguia classificar
  uma porta FECHADA como "item" (usava só física, e porta fechada não
  tem colisor físico, igual já tínhamos confirmado pro som ambiente) -
  batendo numa porta fechada, sempre caía no som de parede por padrão,
  contra o que você pediu. Corrigido reaproveitando a mesma checagem
  de limiar de porta já usada no som ambiente. Adicionei log de
  diagnóstico nos dois casos (toque rápido e travamento contínuo) pra
  confirmar se esse era o problema completo ou se sobra algo.

**Próximo teste (com F12 já ativado ANTES de entrar no jogo, se
possível):**
1. Na porta do salão principal (parede dos dois lados + porta embaixo),
   toca os 3 ao mesmo tempo agora? Se não, "testei" mesmo assim - o log
   novo vai mostrar o motivo exato.
2. O raio do som de item já não alcança mais outro cômodo (ex: mesas do
   salão não devem mais ser ouvidas no quarto)?
3. Bater numa porta fechada dá o som de "bater em item" agora (não o
   de parede)?
4. "testei" quando terminar.

**33ª rodada (2026-06-21) - causa real da porta achada no log (lista
de nós vazia, não nula), bug de parede-perto-de-porta corrigido,
raio e volumes ajustados:**
- **Causa real achada**: o log mostrou `freeNodes=0` pras duas portas
  testadas (não `null`) - minha checagem só pulava quando era `null`,
  então caía num loop vazio (sem nenhum nó pra checar) e nunca achava
  nada, pra QUALQUER porta com essa configuração. Achei que
  `GetDoorWalkablePosition` (já usada pra calcular rota até a porta)
  já tinha exatamente esse caso coberto - quando não tem nós, usa a
  posição da própria porta. Copiei esse mesmo fallback aqui.
- **"Bati em paredes perto da porta, deu como item" - bug real
  achado e corrigido**: a checagem de porta não comparava com a
  parede de verdade - se a porta estivesse "no alcance" (mesmo só
  meio alinhada), contava como item mesmo que a parede fosse o que
  estava genuinamente bloqueando, bem mais perto. Agora comparo as
  duas distâncias e uso a que for mais próxima de verdade.
- **Raio do som de item**: 4 → 3 telhas.
- **Volume por item**: cama +25%, mesa -40% (mantendo os outros sons
  no volume padrão de 60%).

**Próximo teste (com F12 já ativado antes de entrar no jogo):**
1. Porta do salão: toca os 3 lados juntos agora?
2. Bater em parede perto de uma porta continua dando o som de parede
   (não o de item por engano)?
3. Som da cama mais alto, som da mesa mais baixo, perceptível?
4. Raio de 3 telhas parece bom agora?
5. "testei" quando terminar.

**34ª rodada (2026-06-21) - manchas: anúncio próprio + rota melhor + numeração quando há várias:**

Veio da feature de inventário (limpeza com esfregão): usuário
reportou "continua sem anunciar manchas", "rotas para as manchas são
muito imprecisas", "está muito inconsistente achar as manchas",
"preciso do anúncio dela... mesmo que não tenha anúncio oficial do
jogo, limpar igual a mesa, coloque você".

- **Anúncio próprio adicionado** (`HandleFloorDirtAnnouncement`, novo
  método): `FloorDirt` raramente/nunca mostra a dica visual "[E] ..."
  do próprio jogo de forma confiável (diferente da mesa, que mostra
  sempre) - então o `DialogueAnnouncer` (que só lê texto de tela)
  não tinha nada pra anunciar na maioria das vezes. Agora, sempre que
  `InputByProximityManager.GetCurrentFocusedInputElement()` foca numa
  `FloorDirt` (mesmo sistema de proximidade que o jogo já usa
  internamente, confirmado via `CleaningDebugPatch` no round
  anterior), fala "Próximo: Mancha no chão: segure E pra limpar"
  diretamente - não depende do jogo mostrar nada na tela.
- **Rota até a mancha corrigida**: `BuildTargetList()` registrava a
  posição EXATA do centro da `FloorDirt` sem ajuste - mesmo problema
  já corrigido antes pra Placeables (`GetApproachPosition`, ver nota
  do barril preso na parede) podia estar causando rota imprecisa
  pra cima de manchas também. Agora usa `GetApproachPosition` igual
  os outros alvos.
- **Numeração quando há mais de uma mancha por perto**: antes, 2+
  manchas apareciam todas como "Mancha no chão" idêntico na lista de
  Page Up/Down (categoria "Missão") - impossível saber qual era qual.
  Agora, com mais de uma por perto, ficam "Mancha no chão 1", "Mancha
  no chão 2"... ordenadas por distância (1 = mais próxima).

Build limpo. Não consertei "rotas imprecisas" com certeza absoluta -
é uma correção análoga a um bug já confirmado pra outros alvos, mas
ainda não testada ao vivo especificamente pra manchas.

**35ª rodada (2026-06-22) - numeração instável corrigida + pontos exatos de banco junto da mesa:**

Usuário relatou (sem F12 ativado no teste, então sem log pra
confirmar - baseado direto no relato): "os bancos estão numerados
errados" e "os pontos perto da mesa pra colocar os bancos não
aparecem na lista nem são anunciados".

- **Numeração instável - causa achada por revisão de código**: tanto
  manchas quanto bancos eram numerados (`OrderBy` por distância ATÉ O
  JOGADOR) - como essa lista é reconstruída do zero a cada vez que
  Page Up/Down é usado, e a distância até o jogador muda a cada passo
  que ele dá, "Banco 1" podia apontar pra um banco diferente cada vez
  que a lista era refeita, mesmo sem nada mudar no mundo. Corrigido:
  agora ordena por posição FIXA no mundo (x depois y), não por
  distância até o jogador - o mesmo banco sempre fica com o mesmo
  número.
- **Pontos exatos pra colocar o banco**: confirmado lendo `Table.cs`
  que existe uma resposta precisa pra "onde a mesa quer um banco" -
  um campo privado `seatingGroups` (cada um com sua própria posição
  no mundo e um `occupied` que o jogo já controla). Lido via reflexão
  (`AccessTools.Field`, só leitura, não muda nada do jogo). Agora
  aparecem na lista "Missão" como "Lugar pra banco (mesa)" - só os
  vazios (ocupados não aparecem) - e também são anunciados por
  proximidade ("Próximo: Lugar pra banco junto da mesa"), igual já
  acontecia com manchas/bancos.

Build limpo. Nada disso foi confirmado ao vivo ainda - pedido reteste
com F12 ativado desta vez, pra eu poder confirmar pelo log.

**Próximo teste (com F12 ativado antes de entrar no jogo):**
1. Andar perto de uma mancha no chão - ouve "Próximo: Mancha no chão:
   segure E pra limpar" agora, mesmo sem ver nada na tela?
2. Com 2+ manchas por perto, Page Up/Down (categoria Missão) fala
   "Mancha no chão 1", "2" etc., não mais tudo igual?
3. Ativar o guia (Home) até uma mancha - a rota chega certo, sem
   ficar perdida/imprecisa?
4. Pegue um banco (modo de decoração) e veja se "Banco 1"/"Banco 2"
   continua se referindo ao MESMO banco depois de andar e checar de
   novo no Page Up/Down.
5. Perto de uma mesa, escuta "Próximo: Lugar pra banco junto da
   mesa"? Aparece "Lugar pra banco" na lista de Missão?
6. "testei" quando terminar.

## Mudanças de categoria (rodada 102)

- `CategoryOrder` agora: Portas, **Pendentes** (era "Missão"),
  **Repositivos** (nova), Containers, Máquinas, Coletáveis, Decorativos.
- **Pendentes**: coisas que ainda precisam de ação - manchas, lugares
  livres pra banco, bancos AINDA SEM mesa, mesa suja, e vela totalmente
  gasta. Bancos com `Seat.table != null` (já associados) saem da lista.
- **Repositivos**: consumíveis colocados e funcionando - vela acesa (id
  605). Vela gasta (Crafter `LCCABPFHCOL <= 1`) migra pra Pendentes.
- Proximidade de vela: `HandleCandleAnnouncement` (cache `_cachedCandles`,
  20s) fala "Vela acesa"/"Vela apagada, precisa repor". A % exata está
  pendente (combustível máximo não lido de forma confiável ainda) - o log
  "candle proximity ... fuel=N" captura o valor real pra calcular depois.

## Anúncio de obstáculo ao travar (rodada 105)

Quando o jogador fica preso contra algo enquanto anda (sustained bump,
`HandleWallBump`), além do som de item/parede, agora FALA o que está
bloqueando + direção: "Bloqueado por {nome}, à direita/esquerda/cima/
baixo". `IsBlockedByNonWallItem(pos, dir, out blockerName)` devolve o nome
de QUALQUER collider real atingido (móvel "(Clone)" OU cenário estático,
ex. a pilha de tijolos "Grupo Ladrillos" que prendeu o jogador na porta -
paredes não têm Collider2D aqui, então o que for atingido é nomeável).
`DescribeBlockerCollider` usa o nome localizado do item (se for Placeable)
ou limpa o nome do GameObject ("(Clone)" e id "1234 - "). Anunciado uma
vez por bloqueador (reanuncia só ao mudar). A classificação de SOM
item/parede continua pelo sinal "(Clone)".

Caso achado no log da rodada 105: porta da taverna aberta em (12, 909.48),
mas o jogador preso por "Grupo Ladrillos" (objeto de tutorial/construção)
em (12.5, 908.5) à direita; esquerda/cima/baixo livres.

## Tecla C - coordenadas (rodada 106)

`HandleCoordinateKey`: apertar **C** fala "Você está em X, Y" (arredondado
a telhas inteiras). Se há um alvo selecionado (`_selectedTarget`, via Page
Up/Down), fala também "Alvo NOME em X, Y". Bloqueado se Ctrl/Shift
estiverem pressionados. `KeyCode.C` confirmado não usado no decompilado.
Ajuda o jogador a se localizar e a ALINHAR com passagens estreitas (ex.:
porta a x=12 enquanto ele está a x=12.39).

## Porta - bloqueio é alinhamento + obstáculo, não missão (rodada 106)

Diagnóstico do log: a porta da taverna fica aberta, mas a passagem é em
x=12 e o jogador costuma parar levemente ao lado (x=12.39), com a pilha de
tijolos "Grupo Ladrillos" do outro lado. Não é trava de missão. A rota pra
porta ainda cai em `door.transform.position` porque `freeNodesOnOpen` está
vazio (imprecisão conhecida, não corrigida ainda) - a tecla C compensa
deixando o jogador alinhar manualmente.

## Rodada 107 - passagens, ratos, cama, aviso mais rápido

- **TravelZones em "Portas"**: saídas entre áreas (ex. adega->taverna) são
  `TravelZone`, não `Door` (log: "TravelZone-CellarToTavern"). `BuildTargetList`
  lista `TravelZone` próximas em "Portas" via `DescribeTravelZone` (nome por
  `locationTo`, mapa `LocationName`). Por isso a saída da adega não aparecia.
- **Ratos em "Pendentes"**: `TutorialRat` (objetivo "Remova os ratos da
  adega") listados como "Rato N" (ordem x/y estável).
- **Cama só perto**: antes adicionada sempre; agora filtrada por
  `NearbyDoorRadius` (30u). A adega compartilha a Location da taverna
  (filtro de Location não separa), mas fica ~105u da cama, então some lá.
- **Aviso de bloqueio mais rápido**: a VOZ "Bloqueado por ..." dispara em
  `BlockerAnnounceSeconds` (0.2s); o SOM de bump mantém `WallStuckSeconds`
  (0.6s). Ver `HandleWallBump`.

## Rodada 108 - Tab objetivo, nome ao posicionar, ratos (movem/proximidade)

- **Tecla TAB** (`HandleObjectiveKey`, só fora de menu): lê os objetivos
  ativos ao vivo de `NewTutorialManager.instance.objectives[i].textMesh`
  (só os com gameObject ativo), então a contagem ("2 ratos") vem
  atualizada. `KeyCode.Tab` não usado pelo jogo.
- **Nome ao posicionar** (DecorationModeHandler.HandlePlacementResult):
  "{nome} encaixado na mesa"/"{nome} colocado"; removida a frase "mas não
  num ponto de mesa" (confusa com o force-snap da rodada 106).
- **Ratos se MOVEM**: `TutorialRat` tem corrotinas de wander - por isso a
  rota (que fixa a posição na seleção) fica velha. Live-tracking adiado.
- **Proximidade de rato** (`HandleRatAnnouncement`, cache `_cachedRats`):
  "Rato perto. Use o esfregão pra removê-lo" quando o rato mais próximo
  muda. Interação é o esfregão (diálogo da missão); `TutorialRat` não é
  IInteractable, não há tecla discreta - gesto exato do esfregão não
  investigado ainda.

## Rodada 111 - sons instantâneos/baixo lag + ratos (morte/direção)

- **Som de bater mais rápido + menos lag**: `WallStuckSeconds` 0.25->0.08
  (dispara quase no toque). `HandleWallBump` classifica (parede vs item +
  nome) UMA vez na transição (campos `_bumpClassified`/`_bumpIsItem`/...),
  não raycast por frame. `HandleDirectionalWallSound` usa `RaycastNonAlloc`
  (`_raycastBuffer`) em vez de `RaycastAll` (4x/frame).
- **Rato morre**: `HandleRatAnnouncement` conta refs não-nulos em
  `_cachedRats` (rato morto vira null) -> "Rato removido, faltam N".
- **Direção do rato**: anuncia pra que lado o rato MAIS PRÓXIMO foi
  (throttle 0.6s, ~1 telha de movimento).
- Sons persistentes (volume-toggle, rodada 109) já garantem início/fim
  instantâneo no áudio em si.

## Rodada 112 - LAG (grande), áreas (cômodos), categorias

- **Lag** (log: RefreshSeatSceneCache 348ms, GetEmptySeatSlots 15ms/1.5s):
  ratos via lista viva `SceneReferences.tutorialRats` (sem FindObjectsOfType,
  morte exata); candle scan (FindObjectsOfType<Placeable>, o mais caro) movido
  pra cadência própria de 60s; seats/tables 30s; `GetEmptySeatSlots` com
  early-out se nenhuma mesa por perto; BuildTargetList usa cache em vez de
  FindObjectsOfType.
- **Áreas (cômodos)**: `HandleZoneTypeAnnouncement` lê o `ZoneType`
  (`WorldGrid.AGKGGAFFFGM`) e avisa ao mudar: Cozinha (CraftingRoom), Sala de
  jantar (DiningRoom), Adega (Cellar), Quarto (RentedRoom/RoomPlayerN),
  Corredor (WithoutZone), oficinas.
- **Categorias**: `DrinksTable` e `NinjaPreparationTable` -> "Máquinas".

## Rodada 113 - lag (item proximity), cama, guia, bebidas

- **Lag grande**: `HandleItemProximitySounds` fazia `FindObjectsOfType<
  Placeable>()` A CADA SEGUNDO. Agora há `_cachedAllPlaceables` (15s)
  compartilhado por item-proximity e o filtro de velas. Provável causa
  também dos avisos de área parecerem "só no load".
- **Tecla C**: desativar o guia (Home) limpa `_selectedTarget`.
- **Cama**: rota vai pro `Bed.instance.sleepCollider.bounds.center` (gatilho
  de dormir, caminhável) em vez de `GetPlayerBedPosition()`.
- **Bebidas** (`IsDrinkStation`): DrinkDispenser/DrinksTable -> "Dispensador
  de bebidas"; ServiceBarrel/BanquetBarrel -> "Barril"; todos em "Máquinas"
  (checado antes do Container, pois DrinkDispenser É Container).
- Pendente: "mesa de menu" (identificar), inventário da direita mudo no
  dispensador (precisa do log da tela), precisão geral de rotas.

## Arquivos envolvidos

- `CustomSounds.cs` - carrega e toca os `.wav` próprios do usuário.

- `WorldNavigationHandler.cs` - handler novo desta feature.
- `Main.cs` - inicializa e chama `Update()`.
