# Módulo: Modo de Decoração (tecla B)

> Notas de continuidade para este módulo. Atualizar sempre que algo mudar de
> forma relevante (nova descoberta de estrutura, decisão de design, bug
> corrigido). Objetivo: qualquer sessão nova (mesmo sem memória da conversa
> anterior) deve conseguir continuar o trabalho lendo só este arquivo.

## Status atual

Implementado, ainda NÃO testado ao vivo. Surgiu como pedido extra durante a
feature de inventário (rodada com texto de tutorial sobre mesa+assentos),
tratado como módulo separado por ser uma mecânica distinta (posicionamento
de objetos, não inventário/limpeza).

**Escopo desta primeira versão**: reposicionar um objeto JÁ existente no
mundo (mesa, banco, etc.) sem mouse - mover o cursor do jogo por teclado,
pegar/soltar, ouvir se a posição é válida. NÃO cobre trazer um item novo de
um menu de compra/construção pra dentro do modo de decoração por
teclado - esse é um ponto de entrada separado, ainda não investigado.

## Como o jogo faz isso (mouse, achado no código decompilado)

1. **`DecorationMode.DMBFKFLDDLH`** (bool) - true enquanto o modo está
   ativo (tecla B, nativa do jogo).
2. **`CursorManager`** mantém uma posição de cursor em coordenadas de
   MUNDO (não tela), com getter/setter públicos:
   - `CursorManager.GetPlayer(1).GetCursorWorldPosition()` (lê)
   - `CursorManager.SetCursorPositionFromWorld(1, Vector3)` (escreve, static)
3. **Confirmado que o próprio jogo já move esse cursor por GAMEPAD**
   (`Placeable.ALFOFLNNPMJ()`, achado lendo o código): quando
   `PlayerInputs.IsGamepadActive(1)` é true, aperta de D-pad
   esquerda/direita/cima/baixo soma/subtrai 0.5 (uma "telha") na posição do
   cursor via exatamente esse `SetCursorPositionFromWorld`. Replicado esse
   MESMO padrão pra teclado (setas), sem precisar imitar gamepad nem
   mexer em nada do jogo - só chamar a mesma API pública.
4. **`Placeable.WhileSelected()` → `ALFOFLNNPMJ()` → `SetPosition(...)`**
   rodam every-frame enquanto aquele objeto é o `selected` atual,
   independente de COMO o cursor chegou na posição atual (mouse, gamepad,
   ou nós) - ou seja, não precisei de Harmony patch nenhum, só usar as
   APIs públicas e o jogo já segue o cursor sozinho.
5. **`Placeable.canBePlaced`** (bool público) - o MESMO valor que pinta o
   contorno vermelho/destaque pra quem vê. Usado pra anunciar
   "Posição válida"/"Posição inválida".
6. **Pegar um item**: `SelectObject.GetPlayer(1).SelectPlaceable(Placeable)`
   (público) - internamente chama `placeable.MouseUp(1)` como
   gate/validação antes de registrar como `selected`.
7. **Soltar/confirmar**: `SelectObject.GetPlayer(1).Deselect()` (público).
   Ver a seção dedicada abaixo - a mecânica real é mais sutil do que
   "canBePlaced" (que na verdade é um campo morto, sempre true).

## Mecânica de confirmação de posicionamento (Deselect) - REFERÊNCIA (rodada 35)

Esta é a parte que custou várias rodadas a entender. Documentada aqui pra
acelerar tarefas futuras de posicionamento de qualquer item.

**Caminho real ao apertar "soltar":**
`SelectObject.Deselect()` -> `Placeable.Deselect(int)` (linha ~1841 do
decompilado).

`Placeable.Deselect` só coloca o item se TODAS estas condições baterem:
1. **Não é `isAccessElement`** (nossos itens não são).
2. **`attachedToPlaceable != null` OU `IsObjectInValidLocation(true)`** - ou
   seja, a validade é checada com o parâmetro `BIOKGEFFNAA = TRUE` (não
   false!). Esse era o erro: a gente checava/anunciava com `false`. Pra a
   maioria dos itens true e false dão o mesmo resultado, mas o parâmetro
   `true` também dispara textos de erro e checagens extras (porta de quarto,
   etc.).
3. **`base.enabled`** (o componente Placeable está ativo).
4. **`DeselectAction(...)` retorna true.** Dentro dela: `if (!canBePlaced)
   return false;` - MAS `canBePlaced` é declarado `= true` e NUNCA é
   reatribuído em lugar nenhum (confirmado por grep na árvore toda). É um
   campo morto: essa checagem sempre passa. Não confie nele pra nada.

**`IsObjectInValidLocation(bool)`** (o juiz de verdade) roteia assim:
- `areaSpace != null && !areaSpace.IsAreaSpaceValid()` -> inválido.
- `itemSpace != null && currentSurface == null` -> `itemSpace.IsItemSpaceValid()`
  (caminho do banco e da planta - lê `buildSquares[i].GetCentrePosition()`,
  que é `transform.position + offset`, atualiza NA HORA quando movemos o
  transform - por isso planta/banco "funcionam fácil" quando movemos direto).
- senão, `itemBase != null` -> `APNKIDLNFLC()` (caminho do quadro/parede e do
  forro/superfície). Esse lê `itemBase.bounds` (Collider2D), que NÃO atualiza
  na mesma frame de um `transform.position =` sem um `Physics2D.SyncTransforms()`.
  Trata superfície (`IsObjectOnASurface()`), parede (`FNPBNFFEBAF`, exige os
  cantos em tiles de parede na mesma altura) e chão, mais `physicalSpace.
  ValidPosition()` (overlap físico).

**Lições práticas (rodada 35):**
- Pra saber se um ponto é válido, NÃO replicar a checagem na mão (a tentativa
  da rodada 93 deu ponto errado). Mover o objeto pra lá + `Physics2D.
  SyncTransforms()` + chamar `IsObjectInValidLocation` do próprio jogo. É o
  único jeito que bate 100% com o que o Deselect aceita. (=
  `WorldNavigationHandler.FindNearestValidPosition`.)
- O `itemBase.bounds` (parede/superfície) exige `SyncTransforms` depois de
  mover por código; o `itemSpace` (banco/planta) não precisa.
- NÃO adiar o Deselect por uma frame: a `Placeable.WhileSelected` do jogo roda
  toda frame enquanto o item está selecionado e pode "brigar" com a gente
  (re-soltar da superfície, mover pela posição do cursor). Fazer snap +
  SyncTransforms + Deselect tudo na MESMA frame (rodada 99/35ª) evita isso.

## Implementado (`DecorationModeHandler.cs`)

- Anuncia "Modo de decoração ativado/desativado" ao detectar a mudança de
  `DMBFKFLDDLH` (não precisa de tecla nova - só ouve o estado).
- **Sem nada selecionado**: Enter tenta pegar o que estiver embaixo do
  cursor atual (`Physics2D.OverlapPointAll` na posição do cursor, procura
  um `Placeable`). Anuncia se não achou nada ou se a tentativa falhou.
- **Com algo selecionado**: setas movem o cursor em passos de 0.5 (mesma
  "telha" usada no resto do mod); anuncia "Posição válida"/"Posição
  inválida" só quando o valor MUDA (não repete a cada frame); Enter chama
  `Deselect()` pra tentar soltar - anuncia "Item colocado" ou
  "Não posso soltar aqui".
- **2ª rodada (2026-06-21)**: usuário levantou um ponto importante -
  "posição válida" (`canBePlaced`, sem sobreposição) não é a mesma coisa
  que "posição que a missão aceita". Confirmado lendo `Seat.cs`: um banco
  só conta pro objetivo de "assentos disponíveis" se ficar adjacente a uma
  mesa (`Seat.table`, associado automaticamente via
  `Seat.GetNeighbourTable()` ao posicionar - às vezes com 1 frame de
  atraso, daí o `_pendingSeatCheck` no código). Ao soltar um banco agora,
  anuncia "Banco colocado, associado a uma mesa" ou "Banco colocado, mas
  sem mesa por perto" em vez do genérico "Item colocado".
- Também adicionado em `WorldNavigationHandler.cs`: anúncio de proximidade
  pra bancos (`HandleSeatAnnouncement`, baseado em distância simples - Seat
  não é `IProximity` como `FloorDirt`/`Table`, então não dá pra reusar o
  sistema de foco do próprio jogo aqui), incluindo se já está associado a
  uma mesa ou não.

## CORRIGIDO: hipótese de "banco precisa ser construído" estava ERRADA

A rodada anterior concluiu (errado) que nenhum banco existia no mundo
ainda, e que isso explicava o Enter não achar nada. Usuário corrigiu
direto: os bancos JÁ ESTAVAM lá, o texto da missão nunca mencionou
construir nada - era só mover um banco existente pra perto da mesa.

**Causa real, achada com a pista certa**: o anúncio de proximidade
(`HandleSeatAnnouncement`, baseado na posição do JOGADOR) confirmava o
banco bem ali. Mas o Enter pra pegar usava a posição do CURSOR DO MOUSE
(`CursorManager`) - e pra alguém que nunca move o mouse de verdade, esse
cursor pode estar parado em QUALQUER lugar da tela, sem nenhuma relação
com onde o jogador está parado no mundo. Por isso o anúncio achava o
banco (usa posição do jogador) e o Enter não (usava posição do cursor,
desconectada).

**Corrigido em `DecorationModeHandler.HandleGrab`**: agora procura por
posição do JOGADOR (`Physics2D.OverlapCircleAll` num raio de ~1.5
telhas), não do cursor. Depois de pegar com sucesso, o cursor é
"teleportado" pra posição do próprio objeto pego
(`CursorManager.SetCursorPositionFromWorld`), dando um ponto de partida
sensato pras setas moverem a partir daí - sem isso, a primeira tecla de
seta depois de pegar continuaria a partir de onde o cursor já estivesse
(de novo, sem relação com o jogador).

**Bancos faltando na lista "Missão" - causa achada**: o anúncio de
proximidade (que escaneia `Seat` direto) achava os bancos, mas a lista
de Page Up/Down (que só escaneia `Placeable`, e dentro de cada um
verificava `GetComponent<Seat>()`) não - confirma que o componente
`Seat` não fica necessariamente no MESMO GameObject que o `Placeable`
do banco. Corrigido: `BuildTargetList()` agora escaneia `Seat`
diretamente, igual já faz com `FloorDirt` (mesmo padrão, não um
GetComponent dentro do loop de Placeable).

**Mesa não aparecendo em "Missão" (ainda sem solução)**: a mesa só
entra em "Missão" hoje se estiver suja (`Table` tem um nível de
sujeira público) - não existe nenhum sinal parecido pra "esta mesa
ainda precisa de mais bancos por perto". Não inventei uma regra pra
isso ainda por não ter certeza - fica como limitação conhecida.

## Riscos/pontos não confirmados ainda (sem teste ao vivo)

- Setas já são usadas por `DialogueAnnouncer` (reler diálogo) e suprimidas
  globalmente pra movimento do personagem (`MovementAxisPatch`) - não
  testei se algum desses conflita durante o modo de decoração
  especificamente.
- `Physics2D.OverlapPointAll` pra "pegar" pode achar o objeto errado se
  houver vários colliders empilhados no mesmo ponto (pega o primeiro da
  lista, sem ordenar por z/prioridade).
- Não testei cancelar uma seleção sem soltar (não tem tecla de cancelar
  ainda - só "soltar se válido"). Se isso for necessário, fica pendente.
- "Banco precisa aparecer em Missão": adicionado em
  `WorldNavigationHandler.CategorizePlaceable` (`Seat` sempre cai em
  "Missão", mesmo sem checar se o objetivo do tutorial pede assento
  agora) - ver `world-object-navigation.md`.

## 3ª rodada (2026-06-22) - achado importante: T e R já existem nativamente

Usuário descobriu (sem eu ter sugerido): "consigo pegar o item com T e
posicionar com R, então acho que não precisa do Enter pra isso". Faz
sentido - são bem provavelmente as teclas padrão do jogo pra
"Select"/"Rotate" ou ação parecida (não confirmado o nome exato da
ação Rewired, só que funcionam). Meu Enter não foi removido (não faz
mal nenhum continuar existindo como alternativa - meu código só tenta
pegar quando nada está selecionado, e só tenta soltar quando algo já
está, então não conflita com T/R de jeito nenhum), mas pode ser
redundante na prática.

Dois bugs relatados, ambos endereçados:
- **"O banco continua sendo anunciado mesmo já tendo pegado"**:
  confirmado o motivo - o banco continua sendo um GameObject de
  verdade na cena enquanto está na mão (só que seguindo o cursor em
  vez de ficar parado), então os escaneamentos de proximidade
  continuavam achando ele. Corrigido: tanto o anúncio de proximidade
  quanto a lista de Page Up/Down agora EXCLUEM o que está atualmente
  selecionado/segurado.
- **"Já usei uma posição válida perto da mesa, não devia aparecer
  mais"**: ainda não confirmado se é um bug de verdade ou se o
  `occupied` do jogo só demora a atualizar - usuário pediu
  validação/debug em vez de mais suposição. Adicionado log (modo
  debug, 1x por segundo) mostrando o estado `occupied` de CADA vaga
  de assento perto, pra confirmar no próximo teste se ele realmente
  vira `true` depois de colocar um banco lá (usando T/R, não meu
  Enter) ou se continua `false` pra sempre.

Também adicionado log (modo debug) toda vez que
`SelectObject.selectedGameObject` muda - pra QUALQUER causa, inclusive
T/R, não só meu próprio Enter - confirma de verdade se pegar/soltar
está realmente acontecendo no nível de jogo, não só na minha leitura.

## 4ª rodada (2026-06-22) - log lido: T não faz nada, R só gira, Enter é quem realmente pega/solta; bug de raiz único explicando 3 problemas

Log da rodada anterior lido (com F12 ativado de verdade desta vez).
Achados, todos confirmados por evidência, não suposição:

- **"T" não teve nenhum efeito observável no log** - apertado às
  09:28:33, zero mudança de estado depois. A dica visual do próprio
  jogo (capturada pelo `DialogueAnnouncer`) mostrou as teclas reais:
  **"Banco grande: Pegar (tecla Q)"** e **"Banco grande: Rotacionar
  (tecla R)"** - ou seja, Q pega, R GIRA (não solta/posiciona!). O
  banco foi pego com sucesso pouco depois de um Enter (único pressionar
  de tecla relevante na janela de tempo certa) - foi o MEU código que
  pegou e soltou o banco a rodada toda, não T nem R.
- **Causa raiz única achada, explica 3 bugs ao mesmo tempo**:
  confirmado que `Seat` tem seu próprio campo `public Placeable
  placeable;` - ou seja, o componente `Seat` NUNCA fica no mesmo
  GameObject que o `Placeable` do banco (`selectedGameObject`).
  Comparações diretas (`seat.gameObject == heldObject`,
  `beingPlaced.GetComponent<Seat>()`) nunca podiam funcionar.
  Corrigido em 3 lugares (comparando por `seat.placeable.gameObject`
  em vez de `seat.gameObject`):
  1. Exclusão do banco seguro no anúncio de proximidade (por isso
     "continuava informando o mesmo banco").
  2. Exclusão do banco seguro na lista de Page Up/Down.
  3. A mensagem específica de banco ("associado a uma mesa"/"sem
     mesa por perto") nunca disparava - sempre caía no genérico "Item
     colocado", mesmo o usuário tendo soltado fora do lugar da mesa
     (que é um comportamento ESPERADO - válido ≠ útil - só a
     mensagem certa nunca aparecia pra avisar disso).
- **`SeatingGroup.occupied` confirmado MORTO**: o log mostrou
  `occupied=False` em TODAS as 144 ocorrências da sessão inteira,
  mesmo depois de 2 colocações bem-sucedidas. Investigado mais a
  fundo: os únicos métodos que escrevem nesse campo
  (`Table.PlaceSeatingGroup`/`GetSeatingGroup`) não tem NENHUM lugar
  que os chame em todo o código decompilado - não é bug meu, é um
  campo que o jogo simplesmente não mantém atualizado nessa versão.
  Troquei por uma verificação real: olho se já existe um banco de
  verdade bem perto daquela posição específica (~0.3 unidades), em
  vez de confiar nesse flag.
- **Numeração sempre, mesmo com só um item** (pedido explícito): antes
  só numerava com 2+ itens - "Mancha no chão"/"Banco"/"Lugar pra
  banco"/"mesa" sem número quando só tinha um. Agora numera sempre.

Build limpo. Pedido reteste, F12 ativado, focando especificamente em
soltar o banco BEM PERTO de uma vaga de mesa (não em qualquer lugar
válido) pra confirmar a mensagem "associado a uma mesa" e o
desaparecimento daquela vaga específica da lista.

## 5ª rodada (2026-06-22) - lag (causa real), numeração, robustez do agarrar

Usuário não conseguiu testar a rodada anterior (lag/travamento) -
testou de novo depois, lag melhorou mas ainda presente, e trouxe mais
3 relatos. Tudo investigado via log de verdade.

- **Lag - causa mais completa achada**: a rodada anterior só limitou
  a CADA QUANTO TEMPO as buscas pesadas (`Object.FindObjectsOfType`)
  rodavam (a cada 0,3s) - mas ainda rodava até 3 buscas separadas por
  ciclo (~10/segundo no total), bem mais que o padrão já usado em
  outro lugar do mod (1 busca/segundo). Separei as duas
  responsabilidades: agora a busca pesada roda só 1x por segundo e o
  resultado fica guardado num cache compartilhado; a lógica de
  anúncio (rápida, só olha distâncias no cache) pode rodar todo
  quadro sem problema. Também troquei a busca por reflection
  (`AccessTools.Field`) de "a cada chamada" pra "uma vez só, guardada".
- **"Quando peguei um segundo banco, anunciava 'Banco' mas dizia que
  não conseguia pegar"** - log confirmou: o item mais PRÓXIMO do
  jogador não era o banco, era um item de cenário fixo ("Grifo" -
  torneira), que não pode ser selecionado de verdade. Meu código só
  tentava o mais próximo e desistia. Corrigido: agora tenta TODOS os
  candidatos por perto, em ordem de distância, até um funcionar.
- **"Não informa o número de qual banco peguei/coloquei, nem da
  vaga"** - corrigido: peguei um número GLOBAL (baseado em posição
  fixa, igual o resto do mod) pra bancos, mesas e vagas, usado em
  TODOS os lugares (anúncio de proximidade, lista de Page Up/Down, e
  agora também nas mensagens de pegar/soltar do modo de decoração) -
  assim "Banco 3" significa sempre o mesmo banco físico, não importa
  de onde a pergunta vem.
- **"O banco continua sendo anunciado mesmo depois de pego"** -
  reinvestigado com o log mais recente: NÃO é mais esse bug (a
  exclusão de quem está na mão funciona) - o que o usuário ouviu foi
  o MESMO banco real sendo anunciado de novo DEPOIS de ter sido
  solto/colocado longe de qualquer mesa - comportamento correto (uma
  vez solto, ele deixa de estar "na mão" e volta a contar como
  "por perto" de verdade).
- **"Aparece 'Conversar', mas nada acontece com E"** - log confirmou:
  o jogador se afastou (tecla A, andando) entre ouvir o aviso e
  apertar E - quando apertou, já não tinha mais nada por perto pra
  interagir (confirmado pelo log: foco de proximidade já tinha virado
  "nenhum"). Não é bug nessa instância específica, mas o jogo (e o
  mod) não avisam quando isso acontece - você aperta E e não tem
  nenhum retorno dizendo que não havia nada ali. Não toquei nisso
  ainda (é um sistema separado do modo de decoração) - avise se quiser
  que eu adicione um aviso de "nada para interagir aqui" pro E.

Build limpo.

## 6ª rodada (2026-06-22) - numeração instável (causa real achada), diagnóstico de precisão, aviso de "nada pra interagir"

Log lido (F12 ativado de verdade). Usuário relatou: pegou o mesmo banco
(GameObject confirmado único no log) e ouviu "Banco 8" duas vezes e
"Banco 4" uma vez - números diferentes pro MESMO banco físico; tentar
soltar exatamente onde o anúncio diz a vaga continua dando "não posso
soltar aqui", e soltar por perto dá "sem mesa por perto"; perguntou se
o banco "volta sozinho" pro lugar de onde foi pego.

- **Numeração instável - causa real confirmada, corrigida**:
  `GetSeatNumber`/`GetTableNumber` recalculavam a ordenação por POSIÇÃO
  ATUAL a cada chamada - funciona bem pra objetos que nunca se movem,
  mas decoração existe justamente pra mover bancos/mesas. Mover um
  banco muda sua posição na lista ordenada, então o MESMO banco físico
  passa a ocupar um índice diferente - exatamente o que o log mostrou
  (objeto "1135 - Banco Grande(Clone)" virou "Banco 8" duas vezes e
  "Banco 4" da terceira vez, sem nenhum outro banco envolvido). Corrigido:
  agora cada banco/mesa recebe um número UMA VEZ (na primeira vez que
  algo pergunta sobre ele, ordenado por posição NAQUELE momento entre os
  ainda não numerados) e esse número fica fixo depois disso, mesmo que o
  objeto se mova. O rótulo "mesa N" usado nas vagas vazias
  (`GetEmptySeatSlots`) tinha o mesmo problema (mesa também foi movida
  várias vezes nesta sessão) - mesma correção aplicada.
- **"O banco volta sozinho pro lugar onde foi pego?"** - não, nenhum
  código faz isso (confirmado lendo o próprio `DecorationModeHandler` -
  nada reverte posição). O efeito observado é muito provavelmente a
  ilusão causada pelo bug de numeração acima: o MESMO banco físico
  recebendo um número diferente a cada vez parece "outro banco" ou
  "voltou pro lugar errado". Deve ficar claro agora que a numeração é
  estável.
- **Precisão de posicionamento ("não deixa soltar exatamente onde
  anuncia"; "perto, mas sem mesa por perto") - NÃO corrigido às cegas,
  diagnóstico adicionado em vez disso**: confirmado lendo o código
  decompilado que a associação banco-mesa (`Seat.GetNeighbourTable`/
  `Table.GetSeatingGroup`) depende não só de posição mas também da
  DIREÇÃO que o banco está virado (`Placeable.GetDirection()`), com
  tolerância bem estreita (0.225 unidades) - e que objetos com
  `SnapToGrid` ativo (a maioria dos móveis) já encaixam sozinhos no
  grid ao soltar, então a imprecisão provavelmente não é "o cursor não
  alinha", e sim a posição+direção exigida ser mais específica do que
  a vaga anunciada deixa claro. Em vez de tentar mais uma correção sem
  prova, adicionei `WorldNavigationHandler.LogSeatPlacementDiagnostics`
  (chamado a cada banco colocado, modo debug) que registra a posição e
  direção REAIS do banco após soltar, e a distância exata até cada vaga
  de mesa próxima - vai mostrar com números reais, no próximo teste,
  exatamente o quão perto "perto" foi e se a direção bate com o que a
  vaga espera (`SeatingGroup.direction`).
- **"Nada para interagir aqui" implementado**: pedido reaparecido nesta
  rodada (já levantado e não implementado na 5ª) - ao apertar E sem
  nenhum alvo de interação no momento (`InteractObject` não acha nada),
  agora anuncia isso em vez de ficar em silêncio. Vive em
  `WorldNavigationHandler.Update`, não em `DecorationModeHandler` (é um
  sistema de interação geral, não específico de decoração).

Build limpo. Próximo teste deve focar em soltar um banco bem perto de
uma vaga e conferir, pelo log, a distância/direção exatas reportadas
pelo novo diagnóstico - isso decide se o próximo passo é ajustar o que
anunciamos como posição da vaga, orientar o usuário sobre direção, ou
outra coisa ainda não identificada.

## 7ª rodada (2026-06-22) - numeração confirmada estável; "nada pra interagir" removido (falso positivo)

Log lido (F12 ativado). Usuário testou e relatou: "banco 8" apareceu
duas vezes e "banco 4" duas vezes (resultado do teste 1 pedido na
rodada anterior), e que o aviso "Nada para interagir aqui" disparava
mesmo tendo algo ali, inclusive ao abrir a porta - pediu pra tirar o
aviso ou só mostrar quando realmente não tiver nada.

- **Numeração: CONFIRMADA estável pelo log** - o mesmo banco físico
  ("1135 - Banco Grande(Clone)") foi pego duas vezes e anunciado como
  "Banco 8" as duas vezes; um segundo banco nunca pego apareceu 4 vezes
  na proximidade, sempre como "Banco 4". A correção da rodada anterior
  funcionou - sem ação necessária aqui.
- **Diagnóstico de posicionamento já deu o primeiro dado útil**: na
  segunda tentativa de soltar o banco 8, ele ficou virado "Right" a
  ~1.55-3.1 unidades de qualquer vaga, mas as vagas próximas exigem
  "Left"/"Right" dependendo do lado da mesa - ou seja, o banco ficou
  longe E (possivelmente) virado pro lado errado. Confirma a suspeita
  da rodada anterior (direção importa, não só posição) com números
  reais - ainda não o suficiente pra implementar uma correção (precisa
  de uma tentativa BEM perto da vaga pra isolar se distância ou direção
  é o fator decisivo), mas já é progresso real.
- **"Nada para interagir aqui" - REMOVIDO**, não ajustado. O sinal que
  ele usava (`InteractObject.GetCurrentInteractGO()`) se mostrou
  enganoso: zero linhas de log "CurrentInteract CHANGED" na sessão
  inteira (nunca ficou diferente de nulo nem uma vez), mesmo com o
  jogador parado a 0.3 unidades de uma porta que abriu minutos depois -
  ou seja, esse campo simplesmente não rastreia portas (rastreia cama,
  confirmado em rodada anterior, mas não toda interação). Reintroduzir
  isso exigiria um sinal de proximidade mais confiável (cruzar com
  `InputByProximityManager`, por exemplo) - não tentei outra vez sem
  prova de que funciona.

Build limpo.

## 8ª rodada (2026-06-22) - encaixe automático na vaga (girar + posicionar com Enter); fala melhorada; numeração duplicada sob investigação

Usuário pediu, sem novo log desta vez (mesma sessão da 7ª rodada,
arquivo de log não mudou): tirar a obrigação de acertar a marca da vaga
manualmente, girar o banco automaticamente pro lado certo, frases mais
claras ("Banco 4 pego"/"Banco 4 solto"/"Banco 4 posicionado na vaga 1"),
e relatou ter visto "Banco 8" (e depois "Banco 4") em dois lugares ao
mesmo tempo depois de mover o banco pra extremidade oposta.

- **Encaixe automático implementado** (`DecorationModeHandler.
  HandleConfirmPlacement` + `WorldNavigationHandler.FindNearestEmptySlot`):
  ao apertar Enter pra soltar um banco, se houver uma vaga livre a até 3
  telhas (`SnapToSlotRadius`), o banco agora é automaticamente
  posicionado EXATAMENTE na vaga e virado pra direção que ela exige
  (`Placeable.SetDirection` - a mesma função pública que a tecla R usa)
  antes de confirmar - não precisa mais acertar a marca a mão nem saber
  pra que lado virar. Só confirma do jeito que o jogador estava segurando
  se não houver nenhuma vaga livre por perto. Sem teste ao vivo ainda -
  é a primeira vez que isso roda.
- **Numeração de vaga também estabilizada** (`GetSlotNumber`, mesmo
  padrão de atribuição única e permanente de `GetSeatNumber`/
  `GetTableNumber` - `SeatingGroup` é uma classe, não struct, então sua
  identidade de objeto pode ser usada como chave da mesma forma).
- **Frases atualizadas**: "Banco N pego..." (era "Segurando Banco
  N..."), e ao soltar: "Banco N posicionado na vaga V" quando o encaixe
  automático funcionou, "Banco N solto, associado à mesa M" /
  "Banco N solto, mas sem mesa por perto" quando soltou sem vaga por
  perto (sem o encaixe automático ter agido).
- **"Vi Banco 8 (e Banco 4) em dois lugares ao mesmo tempo" - NÃO
  corrigido às cegas, ainda sob investigação**: reli o log da 7ª rodada
  inteiro de novo (é o mesmo arquivo, nada de novo foi gravado) e ele
  NÃO mostra contradição nenhuma - o GameObject do banco movido
  ("1135 - Banco Grande(Clone)") foi sempre "Banco 8"; o outro banco,
  nunca pego, sempre "Banco 4". Pela lógica do código atual (número
  único e permanente por objeto), dois bancos DIFERENTES anunciarem o
  MESMO número não devia ser possível - mas não vou descartar o relato
  sem prova. Hipótese mais provável a confirmar: o raio de anúncio de
  proximidade é de só 1.5 unidades (3 telhas) - se a "extremidade
  oposta" da sala não for tão longe assim, é possível que o que pareceu
  "dois bancos 8" tenha sido o MESMO banco, já bem longe, ainda dentro
  do raio de detecção vindo de uma direção inesperada. Precisa de um
  teste novo com F12 ativado focado nisso especificamente.

Build limpo.

## 9ª rodada (2026-06-22) - bug real achado no encaixe automático (offset errado); diagnóstico de identidade pra investigar a numeração

Log lido (F12 ativado, sessão nova). Usuário reportou: o encaixe
automático (8ª rodada) não estava funcionando nada - "Banco 8" tentado
várias vezes bem onde a vaga é anunciada e não posicionava; "Banco 4"
pego uma vez e não conseguiu colocar em canto nenhum.

- **Causa real achada (não foi suposição) - o encaixe automático
  colocava o banco NO PONTO ERRADO**: o log mostrou repetidamente
  `confirm placement -> False snapped=True` (mais de 10 vezes) - ou
  seja, o encaixe automático ESTAVA rodando, mas a posição calculada
  sempre dava "posição inválida". Reli as fórmulas do próprio jogo
  (`Seat.GetNeighbourTable` e `Table.GetSeatingGroup`) e confirmei: o
  ponto que eu estava lendo (`SeatingGroup.transform.position`) é a
  referência do LADO DA MESA, não o lugar onde o banco deve sentar - o
  banco precisa ficar meio passo (0.5 telha) AFASTADO da mesa, na
  direção que a própria vaga indica, pra não ficar dentro da mesa.
  Corrigido (`WorldNavigationHandler.GetSeatTargetPosition`): agora
  soma esse meio passo antes de mover o banco pra lá. Também adicionei
  log de diagnóstico extra (posição antes/depois do encaixe, validade)
  pra confirmar com números reais se este ajuste realmente resolveu, ou
  se ainda falta alguma fração - não testado ao vivo ainda.
- **Numeração "duplicada" (Banco 8/4 trocando) - NÃO resolvido ainda,
  causa provável diferente do que eu pensava**: percebi um problema na
  minha própria forma de investigar isso - o nome do objeto no log
  ("1135 - Banco Grande(Clone)") é o ID do ITEM (compartilhado por
  TODOS os bancos iguais no jogo), não um identificador único daquele
  banco físico específico. Ou seja, eu não tinha como provar pelo nome
  se duas linhas de log eram "o mesmo banco" ou "dois bancos iguais,
  só parecidos no nome" - é bem possível que existam 2+ bancos "Banco
  Grande" na taverna, cada um com seu próprio número estável (o que
  explicaria tudo sem ser bug nenhum: você pode estar pegando bancos
  DIFERENTES achando que é sempre o mesmo). Adicionei o ID de instância
  real do Unity nos logs de pegar/segurar - isso vai provar de vez se é
  o mesmo banco ou não no próximo teste.

Build limpo.

## 10ª rodada (2026-06-22) - encaixe ainda não confirmado (raio aumentado), investigação de lentidão iniciada

Usuário testou a correção da 9ª rodada com banco 8: "soltou fora" (não
encaixou na vaga) e relatou o jogo muito lento.

- **"Soltou fora" - causa ainda não confirmada, mas achado real**: o
  log mostrou `confirm placement -> False snapped=False` nas duas
  tentativas - ou seja, o encaixe automático nem CHEGOU a tentar
  (nenhuma vaga foi considerada "perto o suficiente", raio de 3
  telhas). Não tenho a posição exata do banco no momento do Enter pra
  confirmar se ele estava só um pouco fora do raio ou bem longe mesmo -
  esse dado não existia ainda no log. Aumentei o raio de 3 pra 5 telhas
  (mais generoso, já que o ponto da função é não precisar de precisão)
  e adicionei um log que mostra a distância real até a vaga mais
  próxima sempre que o encaixe não disparar - vai mostrar com número
  exato se 5 telhas resolve ou se o problema é outro.
- **Lentidão - investigação iniciada, não corrigida ainda**: medindo
  os intervalos entre linhas do log, encontrei pausas de ~1 segundo se
  repetindo ao longo de QUASE TODA a sessão (não só durante o modo de
  decoração) - bate com o ciclo de 1x/segundo de três rotinas
  diferentes que já existiam (`RefreshSeatSceneCache`, o escaneamento
  de portas, e o log de diagnóstico de vagas dentro de
  `GetEmptySeatSlots`). Não consegui confirmar qual delas (ou se é a
  combinação das três) é a real culpada só lendo timestamps - adicionei
  cronômetro (`Stopwatch`, só em modo debug) em cada uma, que grava no
  log quanto tempo cada uma realmente levou sempre que passar de 3ms.
  Próximo teste com F12 vai mostrar isso com números exatos.

Build limpo.

## 11ª rodada (2026-06-22) - causa real da lentidão confirmada e corrigida; causa real do "soltou fora" também achada

Usuário testou: "lentidão melhorou, resto nada" (a parte do banco
continuou sem encaixar).

- **Lentidão - causa real confirmada pelos cronômetros, e corrigida**:
  o log mostrou que UMA chamada de `Object.FindObjectsOfType` nesta
  cena custa ~150-180ms por si só (achando só 8 bancos e 1 mesa) -
  quase certamente porque o custo dessa chamada do próprio Unity
  depende do total de objetos na cena (móveis, telhas decorativas
  etc.), não da quantidade encontrada. Isso rodava 1x por segundo em
  TRÊS lugares diferentes (cache de banco/mesa, escaneamento de portas,
  e a verificação de vagas), somando uns 300-400ms de travamento por
  segundo - exatamente a sensação de "muito lento". Como bancos, mesas
  e portas não trocam de identidade durante a sessão (só mudam de
  posição, e isso já vem de graça pelos objetos guardados), não havia
  necessidade real de reescanear a cada segundo - esse intervalo foi
  bem alargado (20s, só como rede de segurança) e o escaneamento de
  portas agora também é guardado em cache, em vez de rodar de novo
  toda hora. Além disso, a própria verificação de vagas
  (`GetEmptySeatSlots`) custava ~25ms TODO FRAME (sem nenhum limite de
  frequência) - isso por si só já passava do tempo disponível por
  frame; agora também só roda a cada 0,3s.
- **"Soltou fora" - causa real confirmada (não é mais só "raio
  pequeno")**: o novo log de diagnóstico mostrou as distâncias reais
  nas tentativas: 4.61, 2.67 (3x), 2.61 e 5.11 unidades - ou seja,
  mesmo com o raio mais generoso, o banco NUNCA estava perto o
  suficiente da vaga no momento do Enter. A causa real: enquanto você
  movia o cursor com as setas segurando o banco, não existia NENHUM
  aviso te dizendo a que distância/direção a vaga estava - o aviso de
  "Lugar pra banco" só reage à posição do SEU PERSONAGEM, que não muda
  enquanto você só aperta as setas parado no lugar. Ou seja, você
  estava navegando "no escuro". Corrigido: agora, enquanto seguro um
  banco, um aviso "Vaga: X pra direita/esquerda, Y pra cima/baixo" (em
  telhas) é dito sempre que essa distância mudar, igual ao formato já
  usado na navegação até porta/cama - e diz "Vaga bem aqui, pode
  soltar" quando a distância chegar a zero.

Build limpo.

## 12ª rodada (2026-06-22) - causa real do "se perdeu" achada: confusão entre setas e WASD

Usuário reportou: "ele está demorando muito para falar as direções
pra ir até a vaga. se perdeu em certo momento e não falou mais nada".

- **Causa real confirmada pelo log de teclas (não foi suposição)**: o
  log de teclas brutas (captura QUALQUER tecla, não só as que o mod
  usa) mostrou que, do momento que pegou o banco até o aviso parar, só
  apareceram teclas W/A/S/D - nenhuma seta foi apertada nesse período.
  WASD move o SEU PERSONAGEM (e a câmera, que segue ele) - não move o
  banco que você está segurando, só as SETAS fazem isso
  (`HandleCursorMovement` só reage a seta). O aviso de distância até
  mudava um pouco enquanto você andava com WASD (porque a câmera
  arrasta o ponto onde o banco fica "pendurado"), mas nunca de um jeito
  que leva à vaga de verdade - e quando parou de andar, o aviso parou
  de mudar também, daí o silêncio.
- **Corrigido**: adicionei um aviso único (só a primeira vez por banco
  pego) - se você apertar W/A/S/D enquanto está segurando um banco e
  ainda não tiver apertado nenhuma seta, ouve "Pra mover o banco, use
  as setas do teclado, não W A S D".

Build limpo.

## 13ª rodada (2026-06-22) - causa real do "lugar nenhum" achada: a vaga-alvo trocava sozinha (e outro bug de lentidão achado)

Usuário testou com as setas (confirmado pelo log de teclas - usou setas
de verdade dessa vez): "só diminuiu andando, mas mesmo assim ainda está
tudo confuso me levando a lugar nenhum".

- **Causa real confirmada no log - "vaga" mudava de alvo sozinha**: o
  aviso "Vaga: X pra direita" ficava OSCILANDO entre dois números (9/10,
  depois 5/6, etc.) decenas de vezes, nunca diminuindo de verdade. A
  mesa tem 6 vagas em 2 colunas - como eu recalculava "qual vaga está
  mais perto" a cada verificação, bastava o banco ficar perto do meio
  das duas colunas pra ele trocar de vaga-alvo a cada passinho,
  fazendo a distância "pular" pra frente e pra trás pra sempre, em vez
  de diminuir. Corrigido: agora, ao pegar um banco, uma vaga é
  escolhida UMA VEZ e o aviso sempre guia pra essa MESMA vaga até você
  soltar (só troca se outra pessoa ocupar essa vaga antes de você
  chegar). O Enter também usa essa mesma vaga já travada, em vez de
  recalcular - assim o que foi anunciado é exatamente onde o banco vai
  encaixar.
- **Bug de lentidão extra achado (não reportado, mas confirmado no
  código)**: a própria busca "qual vaga está livre mais perto"
  reescaneava TODOS os bancos/mesas da cena (o escaneamento caro de
  ~150-180ms da 11ª rodada) TODA VEZ que era chamada - e agora estava
  sendo chamada a cada 0,3s enquanto seguro um banco. Isso sozinho já
  bastava pra travar o jogo de novo especificamente durante o modo de
  decoração. Corrigido com o mesmo tipo de cache de longa duração já
  usado em outros lugares.

Build limpo.

## 14ª rodada (2026-06-22) - trava ainda quebrando sozinha; diagnóstico cirúrgico adicionado

Usuário testou de novo: "continua ainda sem diminuir".

- **Confirmado no log: a trava da 13ª rodada estava quebrando sozinha,
  SEM nenhuma tecla apertada no meio** (log de teclas brutas confere:
  zero teclas entre as duas mensagens). "Vaga: 5 pra direita, 5 pra
  cima" virou "Vaga: 10 pra direita, 2 pra baixo" 0,85s depois, sem
  você ter feito nada - ou seja, não é mais sobre seta vs WASD, nem
  sobre tremedeira de arredondamento: é a trava (`_lockedTargetSlot`)
  sendo descartada e re-escolhida por conta própria.
- **Causa ainda não confirmada - precisa de mais um teste com F12**:
  a trava só é descartada quando `IsSlotEmpty` diz que a vaga travada
  "deixou de estar livre" - o que não devia acontecer se nada mudou no
  cenário. Adicionei um log cirúrgico que mostra, no exato momento que
  isso acontece, QUAL banco (com ID único) fez a vaga parecer ocupada,
  e onde esse banco estava - em vez de tentar adivinhar mais uma vez.

Build limpo.

## 15ª rodada (2026-06-22) - trava confirmada estável; oscilação é em outro lugar

Usuário testou de novo (já com setas reais, confirmado no log) -
"testado, valide".

- **A trava do `_lockedTargetSlot` está, sim, segura**: o novo log
  cirúrgico mostrou que o aviso de "vaga descartada" NUNCA disparou -
  a mesma vaga (11.85, 905.83) ficou travada do início ao fim. Então o
  problema não é mais a trava se quebrando - é outra coisa fazendo o
  número pular mesmo com a vaga-alvo fixa: às vezes apertar uma seta
  diminuiu o número certo (bom sinal, a lógica básica funciona), mas
  em outros momentos, SEM nenhuma tecla, o número aumentava de novo
  sozinho.
- **Diagnóstico mais fundo adicionado**: como a vaga-alvo é
  confirmadamente fixa, só resta uma explicação - a posição do PRÓPRIO
  banco (`beingPlaced.transform.position`) está mudando sozinha.
  Adicionei um log que mostra esse valor bruto (a posição exata do
  banco) a cada verificação, pra confirmar isso com números reais em
  vez de inferir.

Build limpo.

## 16ª rodada (2026-06-22) - causa raiz real encontrada: o próprio jogo cancelava nosso movimento

Usuário testou o diagnóstico de posição bruta: "mesmo comportamento".
Também perguntou se existe um jeito de recarregar o mod sem reabrir o
jogo - não existe (o MelonLoader carrega o DLL e aplica os patches uma
vez só, no início; trocar o arquivo exige reiniciar o jogo).

- **Causa raiz real, confirmada lendo o código do próprio jogo (não
  foi suposição)**: o log mostrou a posição do banco parada em
  (6.25, 906.98) por várias verificações, pulando pra (6.75, 906.98)
  numa ÚNICA verificação (logo depois de uma seta), e voltando pra
  (6.25, 906.98) na verificação seguinte - SEM nenhuma tecla nesse
  meio tempo. Fui direto na classe `Placeable` do jogo (decompilada) e
  achei a fórmula real que ele usa pra posicionar qualquer item sendo
  segurado: posição final = posição do cursor + um "offset do mouse"
  guardado internamente. O jogo recalcula esse offset sempre que
  pensa que o mouse de verdade está sendo usado, de um jeito que
  CANCELA exatamente o quanto a gente tinha movido o cursor - por
  isso nosso "mover a seta" só durava 1 verificação antes de ser
  desfeito pelo próprio jogo.
- **Corrigido usando uma função pública do próprio jogo**: achei
  `Placeable.SetMouseOffset(Vector3)`, que define esse offset
  diretamente. Agora, ao pegar um banco (e de novo a cada seta
  apertada, por garantia), eu zero esse offset - assim a posição final
  vira exatamente a posição do cursor, sem nada pra cancelar o nosso
  movimento.

Build limpo. Essa é a primeira correção que ataca a causa raiz de
verdade (todas as anteriores nessa área eram sintomas dela) -
expectativa alta de que resolva de vez, mas precisa confirmar com
teste real.

## 17ª rodada (2026-06-22) - SetMouseOffset não resolveu; segunda causa achada e atacada de vez

Usuário testou: "mesmo erro, por favor valide a fundo isso, estamos
girando em círculo".

- **Confirmado no log: a correção da 16ª rodada (zerar o offset) NÃO
  resolveu** - a posição continuou alternando entre dois valores fixos
  (ex: 3.75 e 4.25) mesmo depois de apertar a seta várias vezes em
  sequência, sempre voltando pro mesmo valor "preso".
- **Segunda causa raiz, achada lendo mais a fundo o código do jogo**:
  `Placeable.SetPosition` só aplica a posição nova SEM CONDIÇÃO quando
  o campo `currentSurface` do item está vazio (null). Se esse campo
  estiver preenchido, a posição só é aplicada se passar por uma
  verificação (`IsNewPosOnSurface`) que exige `isPlaceableOnSurface`
  ser verdadeiro - e isso é falso para mobília de chão como um banco.
  Ou seja: se `currentSurface` ficou preenchido nesse banco por
  qualquer motivo, TODA tentativa de mover ele enquanto seguro seria
  silenciosamente ignorada pelo próprio jogo - exatamente o "preso"
  que vimos.
- **Corrigido (e com diagnóstico completo se eu errei de novo)**: ao
  pegar o banco, chamo `Placeable.RemoveFromSurface(false)` - método
  público do próprio jogo pra "esse item não está mais numa
  superfície" (ele já usa isso em outros lugares quando um item é
  pego). Se `currentSurface` já estava vazio, não faz nada; se não
  estava, deve resolver de vez. Também deixei um log que mostra, a
  cada verificação, o valor exato de `currentSurface`,
  `surfaceCollider`, `isPlaceableOnSurface` e a posição real do cursor
  - se essa correção ainda não for suficiente, o próximo log já vai
  mostrar exatamente o que falta, sem precisar de mais um chute.

Build limpo.

## 18ª rodada (2026-06-22) - causa raiz teoria da "superfície" descartada com dados reais; mudança de arquitetura

Usuário testou: "feito, tudo igual" - o log com diagnóstico completo
veio junto.

- **A teoria da "superfície" (17ª rodada) foi DESCARTADA com prova
  real**: `currentSurface`, `surfaceCollider`, `isPlaceableOnSurface`
  e `isOnSurface` apareceram null/false em TODAS as linhas do log, sem
  exceção - nunca foi essa a causa.
- **A pista real estava nos números**: o log mostrou a posição do
  cursor (`cursorPos`) e a posição real do banco (`cur`) - e elas eram
  completamente DIFERENTES uma da outra (não só levemente atrasadas,
  números sem relação nenhuma), e as DUAS oscilavam sozinhas entre
  dois valores, de forma independente uma da outra. Ou seja: o sistema
  de "cursor" que essa função inteira foi construída em cima dele
  (`CursorManager`) não está se comportando de um jeito confiável aqui,
  por um motivo que resistiu a três rodadas inteiras de leitura do
  código do jogo.
- **Mudança de abordagem (não mais um ajuste pontual)**: em vez de
  continuar tentando convencer o sistema de cursor do jogo a cooperar,
  parei de depender dele. Agora o mod guarda, na própria memória, a
  posição que o banco DEVERIA estar (`_heldIntendedPosition`,
  começando na posição de quando você pega o banco) e força essa
  posição diretamente no banco TODO FRAME (não só quando uma seta é
  apertada) - sem pedir pro jogo "seguir o cursor", apenas colocando o
  banco onde decidimos que ele deve estar. O encaixe automático
  (Enter) também foi ajustado pra fazer a mesma coisa.

Build limpo. Essa é uma mudança mais estrutural que as tentativas
anteriores - elimina a dependência do sistema de cursor do jogo por
completo para esta função, em vez de tentar mais um ajuste nele.

## 19ª rodada (2026-06-22) - andar com as setas funcionou; achado o motivo de "não associou" e do segundo banco preso

Usuário testou: "agora funcionou andar com o cursor com setas, mas ele
disse que não foi associado. com o segundo banco nem deixou colocar,
ele provavelmente está tentando colocar no mesmo lugar que o primeiro
foi colocado" - acertou na suspeita.

- **Movimento com as setas: confirmado funcionando** - a mudança de
  arquitetura da 18ª rodada resolveu a oscilação de vez.
- **Causa real do "não associou" e do banco preso, achada lendo mais
  uma vez o código do jogo**: `Seat.GetNeighbourTable` (a função que
  decide a qual mesa um banco pertence) NÃO usa a posição visual do
  objeto - ela usa um sistema de "grade do mundo" separado
  (`buildSquare.GetCentrePosition()`), que só é atualizado através do
  método interno `Placeable.SetPosition` (que faz `PixelSnap`/
  `snapToGrid.Snap`/registra nas telhas do mundo). Como a correção da
  18ª rodada movia só a posição visual diretamente (sem passar por
  `SetPosition`), o banco aparecia no lugar certo na tela, mas o jogo
  continuava "pensando" que ele estava no lugar antigo - por isso não
  achava a mesa, e por isso a vaga continuava "livre" pro segundo
  banco (que então tentava ocupar o mesmo lugar do primeiro).
- **Corrigido**: agora, a cada movimento (e no encaixe final), além de
  forçar a posição visual, também chamo `Placeable.SetPosition`
  (a mesma chamada que o próprio jogo usa internamente pro D-pad de
  controle) - isso atualiza a "grade do mundo" junto com a posição
  visual, igualando os dois sistemas. Deixei também um log de
  segurança que detecta diretamente se a posição do banco (visual) e a
  posição do seu "assento" (lógico) ainda discordam, caso essa correção
  não resolva tudo de uma vez.

Build limpo.

## 20ª rodada (2026-06-22) - causa raiz definitiva achada: o "assento" é um objeto totalmente separado do banco visual

Usuário testou a correção da 19ª rodada (`Placeable.SetPosition`):
"continua igual, as setas andam, mas não associa, e nem deixou colocar
o segundo" - ainda não tinha resolvido.

- **Causa raiz definitiva, confirmada com diagnóstico real**: o log
  mostrou que existem objetos de "assento" (`Seat`) com nomes como
  "UpperSeat"/"MiddleSeat"/"LowerSeat" cuja posição NUNCA bate com a
  posição do item visual a que pertencem (isso é esperado pra certos
  móveis com vários assentos, mas confirmou de vez algo importante):
  o objeto `Seat` (o "assento" lógico que decide a qual mesa o banco
  pertence) é um GameObject SEPARADO do banco visual - eles só têm uma
  referência cruzada um pro outro, não são pai/filho no Unity. Isso
  significa que mover o banco visual NUNCA move o assento sozinho -
  nada que fizemos até agora (incluindo o `SetPosition` da 19ª rodada)
  tinha qualquer chance de corrigir isso, porque o Unity só propaga
  posição automaticamente entre pai e filho, e eles não são isso.
- **Corrigido de vez**: agora, toda vez que movo o banco (com as setas
  ou no encaixe final), também movo o `Seat` correspondente pra MESMA
  posição, diretamente. Como `Seat.GetNeighbourTable` (a função que
  decide a mesa) e a verificação de "vaga ocupada" usam a posição do
  `Seat`, não do banco visual, isso deve corrigir os dois problemas de
  uma vez: a associação à mesa, e o segundo banco não reconhecer que a
  vaga já tinha sido ocupada.

Build limpo.

## 21ª rodada (2026-06-22) - mover o Seat também não resolveu; diagnóstico de hierarquia real (sem chute novo)

Usuário testou: "testei, mesmo problema" - mover o `Seat` (20ª rodada)
não foi suficiente.

- **Achado mais um nível de indireção**: `Seat.GetNeighbourTable` (a
  função que decide a mesa) não usa nem a posição do banco visual NEM
  a posição do `Seat` diretamente - ela usa um terceiro objeto interno
  ainda mais escondido (`buildSquare`), referenciado uma única vez,
  nunca reatribuído em nenhum lugar do código que consegui ler (ou
  seja, é uma referência fixa, definida no editor do jogo, não algo
  recalculado em tempo real).
- **Não tentei mais um chute desta vez** - já tentamos mover 3 coisas
  diferentes (banco visual, depois o assento) sem sucesso, então em
  vez de arriscar uma quarta suposição, adicionei um diagnóstico que
  mostra a hierarquia REAL desses 3 objetos no jogo (quem é "pai" de
  quem, e a posição de cada um) - isso vai mostrar com certeza onde
  está a desconexão, em vez de eu continuar adivinhando.

Build limpo. Dado que já estamos travados nesse mesmo problema por
muitas rodadas, perguntei ao usuário (no `novo_pedido.txt`) se quer
autorizar alternar para o modelo Opus nesta investigação específica,
conforme o próprio combinou anteriormente.

## 22ª rodada (2026-06-22) - causa raiz real, finalmente isolada por leitura completa (não chute) + chamada direta

Usuário testou e autorizou trocar pra Opus ("sim pode trocar"), mas o
erro persistiu - importante: a troca de modelo só pode ser feita pelo
próprio usuário com "/model" (Claude não tem ferramenta pra trocar o
próprio modelo durante a sessão); respondido isso pra ele de volta.

O log do diagnóstico da 21ª rodada mostrou algo decisivo: `Seat` e
`buildSquare` SÃO filhos de verdade do banco visual na hierarquia do
Unity ("Banco Grande(Clone) > UpperSeatRESERVED" e "... > ItemSpace >
BuildSquare (3)") - ou seja, eles JÁ seguem o banco automaticamente
quando ele se move (o pequeno offset entre eles é normal, é só a
posição daquela célula específica dentro do móvel). Isso eliminou de
vez a teoria de "objeto desconectado" das rodadas 84/85 - a posição
nunca foi o problema real.

Lendo o método por completo (não só por trechos, como nas rodadas
anteriores), achei a causa raiz verdadeira: a associação à mesa só é
recalculada automaticamente enquanto o banco está SENDO SEGURO (a
cada quadro, via um callback do próprio jogo). No instante em que
soltamos o banco (Deselect), esse recálculo automático PARA de
acontecer. Há uma disputa de tempo entre o código do mod e o do jogo
sobre QUAL dos dois quadros (mover pra posição final vs soltar) roda
primeiro - dependendo da ordem, o jogo podia soltar o banco antes do
seu próprio recálculo "ver" a posição final.

Corrigido removendo essa dependência de tempo por completo: agora,
bem antes de soltar o banco, chamo eu mesmo, diretamente, as duas
funções do jogo que fazem esse recálculo (`GetNeighbourTableAround` e
`GetNeighbourTable`) - sem depender de quando o jogo decide rodar isso
por conta própria. Também adicionei um log que mostra exatamente o
resultado dessa chamada (achou mesa? qual? a mesa permite encaixe
nessa direção?) pra confirmar com certeza se funcionou ou se ainda
existe algum motivo pelo qual aquela mesa específica rejeita aquela
direção de banco.

Build limpo.

## 23ª rodada (2026-06-22) - a chamada direta funcionou, mas confirmou que a busca falha por distância, não por tempo

Usuário testou: "erro ainda o mesmo" - mas o log da chamada direta
(22ª rodada) trouxe um dado novo e decisivo: `table=null` mesmo
chamando a função do jogo diretamente (ou seja, não era mais um
problema de tempo/timing - a busca em si não está achando a mesa).

- **Medição concreta**: o log de diagnóstico já existente (que
  compara a posição do banco direto com a vaga) mostrou uma distância
  de 0.613 unidades entre o banco encaixado e a vaga-alvo - bem maior
  que a tolerância de 0.225 que o próprio jogo usa nessa busca.
- **Suspeita concreta (ainda não confirmada com 100% de certeza)**: a
  fórmula que uso pra calcular onde encaixar o banco (`GetSeatTarget
  Position`, da 71ª rodada, criada pra evitar visualmente sobrepor a
  mesa) soma um deslocamento de 0.5 unidades na direção da vaga. A
  própria busca do jogo (`GetNeighbourTable`) TAMBÉM soma um
  deslocamento de 0.5 unidades na mesma direção, a partir da posição
  do banco. Se os dois deslocamentos forem na mesma direção, eles se
  somam (ao invés de se cancelarem), jogando o ponto de busca quase
  uma "casa" de distância além de onde a mesa realmente está.
- **Não mudei a fórmula ainda** - antes de mais um chute, adicionei um
  log que mostra o ponto EXATO de busca que o jogo usa, comparado à
  posição real de cada mesa próxima, pra confirmar essa suspeita (ou
  descartá-la) com um número concreto, em vez de mais inferência.

Build limpo.

## 24ª rodada (2026-06-22) - causa raiz real confirmada por número, não por suspeita: direção invertida

O log do diagnóstico (23ª rodada) trouxe o número decisivo: o ponto de
busca do jogo ficou a 1.771 unidades da mesa - bem mais que a teoria
de "meio passo duplicado" (que daria algo perto de 0.5/0.75)
explicaria. Isso me fez olhar de novo, e achei a causa raiz de
verdade, sem mais suspeita:

- **A vaga ("slot") tem uma direção que indica DE QUE LADO da mesa ela
  está** (confirmado pelos números: a vaga ficou à esquerda da mesa no
  mundo, e sua direção era "Left/Esquerda" - bate exatamente). Eu
  estava usando essa MESMA direção pra fazer o banco "olhar" (`SetDirection`)
  - ou seja, fazia o banco virar de costas pra mesa (olhando ainda
  mais pra esquerda, se afastando) em vez de virar DE FRENTE pra ela.
  A função do jogo que acha a mesa (`Seat.GetNeighbourTable`) procura
  exatamente na direção que o banco está olhando - se ele olha pro
  lado errado (longe da mesa), nunca vai achar nada, não importa a
  posição.
- **Corrigido**: agora oriento o banco na direção OPOSTA à da vaga
  (`Utils.ABNPPDOGEPM` - a mesma função que o jogo usa internamente
  pra inverter direções) - assim ele passa a olhar DE FRENTE pra mesa,
  e a busca do jogo passa a procurar do lado certo. Não toquei na
  fórmula de posição (`GetSeatTargetPosition`) - ela sempre esteve
  certa, o problema era só a direção que o banco ficava olhando.

Build limpo. Essa é a primeira correção desta sequência baseada num
número medido diretamente (não numa suspeita por eliminação) - boa
chance de ser a correção definitiva.

## 25ª rodada (2026-06-22) - virar o banco era necessário mas não suficiente; achada e corrigida a fórmula de posição também

Usuário testou: "ainda continua o mesmo problema" - e pediu pra
focar 100% em debugar isso, fazendo todas as verificações necessárias
em código, usando subagentes se precisasse.

- **Confirmado pelo log que virar o banco (24ª rodada) ajudou, mas não
  resolveu**: a distância caiu de 1.771 para 0.797 unidades (bem
  melhor, prova que a direção estava mesmo errada), mas ainda muito
  acima da tolerância de 0.225 que o jogo usa.
- **Causa raiz da distância restante, achada lendo o código completo
  de `Table.PlaceSeatingGroup`** (a função que o PRÓPRIO JOGO usa
  internamente pra colocar um assento numa vaga): ela calcula a
  posição a partir de uma célula específica da MESA (um "buildSquare"
  que pertence à mesa, não ao banco) - não a partir da posição da vaga
  como eu vinha assumindo desde a 72ª rodada. Ou seja, minha fórmula
  usava a referência errada o tempo todo - "perto" mas nunca dentro da
  margem mínima exigida.
- **Corrigido**: `GetSeatTargetPosition` agora usa a mesma célula da
  mesa que o jogo usa (em vez da posição da própria vaga), replicando
  exatamente a fórmula interna do jogo.
- **Reforço extra**: como ainda existia uma chance de outro motivo
  (camada de colisão errada, ou um "trigger" sendo ignorado pela busca
  do jogo), também adicionei um diagnóstico que roda a EXATA mesma
  busca física que o jogo faz e mostra o que ela realmente encontra -
  assim, mesmo que a distância ainda não seja perfeita, vamos saber
  com certeza se o problema é só de distância ou outra coisa.

Build limpo.

## 26ª rodada (2026-06-22) - RESOLVIDO

Usuário testou: "sucesso funcionou." - a correção da 25ª rodada
(usar a célula da mesa, não a posição da vaga, igual o jogo faz
internamente) resolveu de vez tanto a associação à mesa quanto o
segundo banco ficar preso. Fim desta sequência de ~16 rodadas de
depuração (rodadas 71-89).

Resumo da causa raiz final (as duas partes que faltavam):
1. O banco precisa olhar PRA mesa, não pro lado que a vaga indica
   (vaga "Esquerda" = vaga fica à esquerda da mesa, então o banco
   precisa olhar pra Direita, não Esquerda) - corrigido com
   `Utils.ABNPPDOGEPM` (inverte a direção).
2. A posição exata de encaixe precisa ser calculada a partir de uma
   célula da MESA (`table.placeable.itemSpace.buildSquares[...]`),
   não a partir da posição da própria vaga - eram parecidas mas nunca
   coincidiam o suficiente pra entrar na margem de 0.225 que o jogo
   exige.

Feature de decoração (mover móveis, encaixar bancos em mesas) agora
está funcionalmente completa.

## 27ª rodada (2026-06-22) - decoração genérica: pintura, planta, centro de mesa (itens sem Seat)

Usuário pediu pra estender a mesma experiência de orientação +
encaixe automático dos bancos pra QUALQUER item decorativo, citando
3 itens recebidos no último teste (log confirma: "Pintura
desgastada", "Planta morta"/"Planta Moribunda", "Centro de mesa
surrado"). Usei um agente de busca pra mapear o terreno, mas **corrigi
um erro real dele antes de implementar** (ver abaixo) - mesma regra de
sempre.

### Achado 1 (correção de um bug antigo, beneficia os bancos também)

O anúncio "Posição válida/inválida" sempre leu o campo
`Placeable.canBePlaced` - conferi com grep em TODO o `decompiled/` e
esse campo NUNCA é reatribuído em lugar nenhum (sempre `true`, fixo).
Esse anúncio estava lendo um campo morto desde o início. O portão real
usado por `Deselect()` é `IsObjectInValidLocation(bool)` (público) -
troquei pra usar essa função diretamente. Não deveria mudar nada pros
bancos na prática (mesmo caminho de validação), mas agora é
honestamente correto, e é o ÚNICO jeito de ter uma resposta certa pra
itens sem assento (que usam um caminho de validação diferente).

### Achado 2 (correção de um erro do agente de busca)

O agente disse que a anexação a uma "superfície" (mesa/estante,
necessária pra alguns itens) só acontece em salvar/carregar jogo ou
randomização de taverna, nunca durante o segurar/arrastar do jogador.
**Errado, conferido lendo mais a fundo**: existe sim uma anexação
automática AO VIVO (`Placeable.PEFFMJOMPMN`, chamada todo quadro
durante `WhileSelected` - a MESMA função que faz a busca de mesa dos
bancos) - só que ela acha a superfície usando a posição DO CURSOR DO
MOUSE (`CursorManager.GetCursorWorldPosition()`), não a posição visual
do item. Essa é exatamente a mesma fonte que já provamos não
confiável pra movimento por teclado (rodada 82, na investigação dos
bancos).

### Implementado

- **Itens que precisam de uma superfície** (`Placeable.
  isPlaceableOnSurface == true` - provavelmente o caso de pelo menos
  um dos 3 itens novos): em vez de confiar na anexação automática do
  jogo (que usa o cursor do mouse), repito a mesma lógica (`Utils.
  CCCCIKOMAEN<SurfaceSortOrder>` + `IsItemAllowed`) usando a posição
  REAL do item (a mesma que já controlamos com confiança pros bancos)
  - mesmo padrão da correção dos bancos: tomar posse direta em vez de
    confiar no sistema automático do jogo.
  - Nova orientação "Superfície: X pra direita/esquerda/cima/baixo" /
    "Superfície bem aqui, pode soltar" - mesmo padrão de fala já usado
    pra vaga de banco (`WorldNavigationHandler.FindNearestValidSurface`,
    novo, análogo a `FindNearestEmptySlot`).
  - Anexa/desanexa automaticamente enquanto move (`AddPlaceableToSurface`/
    `RemoveFromSurface`, funções públicas do próprio jogo) - sem
    precisar de um passo de "encaixe" separado no Enter, porque
    superfícies (diferente de vagas de banco) não têm direção/giro
    pra acertar.
- **Itens que não precisam de superfície** (`placeableAnywhere` ou
  itens de parede `isPlaceableOnWall`): usam o fluxo genérico já
  existente (mover + Enter), agora com o anúncio de validade corrigido
  (achado 1). **Não implementei orientação especial pra parede ainda**
  - não existe no jogo um sistema de "anexação automática de parede"
    equivalente ao de superfície (conferido), e não tive evidência de
    qual dos 3 itens realmente precisa disso. Adicionei um diagnóstico
    (abaixo) que vai mostrar exatamente disso cada item precisa, pra
    decidir se isso é necessário numa próxima rodada.
- **Diagnóstico** (`Main.DebugMode`): ao pegar qualquer item sem
  assento, loga `placeableAnywhere`/`isPlaceableOnSurface`/
  `isPlaceableOnWall`/`onlyInAllowedSurfaces`/`itemSpace`/`itemBase`/
  `currentSurface` - essas flags são valores configurados no editor do
  jogo por item, impossíveis de descobrir só lendo o código (confirmado
  pelo agente de busca) - só dá pra saber pegando o item de verdade.

Build limpo.

## Próximo teste (com F12 ativado antes de entrar no jogo)

1. **Revalidar bancos** (não deveria ter mudado nada, mas confirmar):
   pegue um banco, encaixe numa vaga - ainda funciona normal?
2. **Testar os itens novos** - pegue a pintura, a planta e o centro de
   mesa (um por vez) e tente posicionar cada um:
   - Anuncia "Superfície: X pra..." ao se mover? Anexa quando chega
     perto de uma mesa/estante (some o anúncio de direção, diz
     "Superfície bem aqui")?
   - Enter solta com sucesso ("Item solto na superfície" ou "Item
     solto")?
   - Se algum não funcionar, "testei" mesmo assim - o diagnóstico vai
     mostrar no log exatamente que tipo de item ele é.
3. "testei" quando terminar, mesmo que algum item não tenha funcionado.

## 28ª rodada (2026-06-22) - achado do ponto de entrada real, velocidade do cursor, lista de estilos

Usuário testou a 27ª rodada: bancos continuam funcionando. Sobre os 3
itens novos: "quadro planta e forro de mesa são usados com f para
posicionar, mas sem orientações" + pedido de lista acessível de
estilos pra planta + pedido de cursor mais rápido.

### Achado importante (não era bug de orientação, era bug de diagnóstico)

Conferi no log: pintura/centro de mesa/planta são colocados a
primeira vez através de uma tecla nativa do hotbar (o "F" do usuário)
que entra no Modo de Decoração e pega uma instância nova
automaticamente - log confirma a sequência exata: "Modo de decoração
ativado" -> "Item pego" -> "selectedGameObject changed to ...". Isso
passa pelo MESMO caminho (`SelectObject.selectedGameObject`) que nosso
código já reage - ou seja, a orientação JÁ estava funcionando (achei
no log "Superfície: 9 pra direita, 1 pra cima" pro centro de mesa,
rodada anterior!). O que realmente faltava: nosso diagnóstico
("grabbed item category") só vivia dentro do NOSSO próprio
`HandleGrab` (acionado por Enter), que esse caminho nativo nunca
chama - por isso nunca tínhamos os dados de categoria desses 3 itens.
Movido pra dentro do bloco que já reage a QUALQUER mudança de
`selectedGameObject`, então agora vai capturar de verdade no próximo
teste.

### Cursor mais rápido

Confirmado: não tem relação com debug/monitoramento (como o usuário
suspeitou) - é porque `Input.GetKeyDown` só dispara uma vez por
aperto físico da tecla, exigindo soltar e apertar de novo pra cada
peça (0.5 unidade). Implementado "repetir enquanto mantém pressionado"
(`HandleCursorMovement`): primeiro toque sempre move exatamente uma
peça, mantendo pressionado repete a cada 0.08s após um atraso inicial
de 0.25s - mesmo comportamento de um cursor de texto.

### Lista acessível de estilos (tecla T)

Pesquisado o sistema nativo de "Estilo" (`Placeable.NextSkin`/
`ChangeSkin(int)`/`GetSkinIndex()`/`skins`/`skinsGameObjects`/
`multipleSkins` - todos públicos, confirmados por grep) - o
comportamento nativo só avança um índice silenciosamente, sem nenhuma
interface. Implementado `HandleStyleTrigger`/`HandleStylePicker`: T
abre uma lista ("Estilo N de M"), setas navegam, Enter confirma
(`ChangeSkin`), Esc cancela e volta pro estilo original. Limitado de
propósito ao caso simples (array de skins) - existe um caminho mais
complexo (`skinVariationGropus`, liga/desliga várias skins em
combinação, não escolhe uma entre N) que deixei de fora até confirmar
se algum item realmente usa isso.

### Orientação pra parede (quadro/pintura) - ainda não implementada

Pesquisei: dados de parede no jogo são só células de Tilemap
(`WorldGrid.ALNFLFCLIEP`), não existem GameObjects de parede pra
buscar com `FindObjectsOfType` (que é como `FindNearestValidSurface`
funciona pras superfícies). Buscar "parede mais próxima" precisaria
escanear células de grade, não a lista de objetos da cena - mais
trabalho, e ainda não confirmei se a pintura realmente precisa disso
(`isPlaceableOnWall`). O diagnóstico desta rodada (agora corrigido)
vai responder isso no próximo teste - aí decido se vale a pena
construir.

Build limpo.

## Próximo teste (com F12 ativado antes de entrar no jogo)

1. Pegue a pintura, a planta e o centro de mesa de novo (com F, do
   jeito que você já faz) - agora deve aparecer no log
   "grabbed item category" pra cada um.
2. Teste mover mais rápido (segurando a seta, não só tocando) - tá
   melhor?
3. Com a planta selecionada, aperte T - anuncia "Lista de estilos.
   Estilo 1 de N..."? Setas navegam, Enter escolhe, Esc cancela?
4. "testei" quando terminar.

## 29ª rodada (2026-06-22) - dados reais confirmaram parede pro quadro, bug no aviso de WASD

Usuário testou: estilo funcionou pra planta. Soltar planta e quadro
não funcionou, sem dicas de onde colocar. Centro de mesa deu dicas mas
precisou andar com o personagem (não com as setas), e não sabe se
ficou no lugar certo. Pedido: aprofundar quadro e planta.

### O que o log revelou

Com o diagnóstico finalmente capturando dados (rodada 28 corrigiu
onde ele vivia):
- **Quadro ("Cuadro Raido")**: `isPlaceableOnWall=True`. Tentado 8
  vezes (regarrando), NUNCA ficou em posição válida nenhuma vez -
  confirma que realmente precisa de parede, e não tínhamos orientação
  nenhuma pra isso.
- **Centro de mesa**: `isPlaceableOnSurface=True` - funcionou de
  verdade! Log mostra a sequência completa: "Posição inválida" ->
  "Superfície: 5 pra direita, 1 pra baixo" -> "Posição válida" ->
  "Superfície bem aqui, pode soltar" -> "Item solto na superfície".
  O usuário reandar com o personagem entre tentativas (regarrando a
  cada vez) foi na real o que aproximou o suficiente - cada nova
  tentativa nasce na posição atual do personagem.
- **Planta**: `hasItemSpace=True`, sem superfície nem parede - usa a
  mesma checagem genérica de "área livre" que bancos/assentos usam.
  Não chegou a testar com as setas (log mostra só teclas WASD durante
  a tentativa) - antes de mover, foi direto testar o estilo (que
  funcionou). Não é um item que precisa de orientação especial - só
  precisa que o usuário use as SETAS (não WASD) pra achar um espaço
  livre no chão.
- **Bug achado**: o aviso "Pra mover o banco, use as setas..." é
  disparado pra QUALQUER item (confirmado no log: disparou seguro a
  planta) mas tinha a palavra "banco" fixa no texto - confuso.
  Corrigido pra "Pra mover o item, use as setas...".

### Implementado: orientação de parede

Li o código real da checagem de parede (`Placeable.FNPBNFFEBAF`,
`WorldGrid.ALNFLFCLIEP`/`KHJJCAGIJAP`, ambos públicos e estáticos,
confirmados por grep) - exige que os 4 cantos da caixa do item estejam
em blocos de parede, numa altura consistente. Como parede não é um
GameObject pra buscar (é só dado de grade/tilemap), criei
`WorldNavigationHandler.FindNearestWallPoint` (escaneia pontos da
grade testando `WorldGrid.ALNFLFCLIEP`, em vez de
`FindObjectsOfType` como as superfícies). Nova orientação "Parede: X
pra direita/esquerda/cima/baixo" / "Parede bem aqui, pode soltar" -
mesmo padrão da superfície, só que com dados de tilemap. É uma
aproximação (só testa o ponto central, não os 4 cantos exatos como o
jogo faz) - o anúncio "Posição válida/inválida" continua sendo a
resposta final de verdade sobre se o Enter vai funcionar.

Build limpo.

## 30ª rodada (2026-06-22) - parede corrigida com a checagem real, superfície ganhou auto-encaixe, planta ganhou diagnóstico

Usuário testou a rodada 29 (orientação de parede aproximada) e
reportou: quadro continua sem funcionar; centro de mesa funciona mas
"não tem como deixar igual ao banco, está muito inconstante"; planta
não encaixou em lugar nenhum, mesmo tentando vários lugares andando e
só com as setas.

Achei o projeto com a build QUEBRADA ao continuar - uma sessão
anterior já tinha lido `Placeable.FNPBNFFEBAF` a fundo e escrito
`WorldNavigationHandler.IsValidWallPosition` (checagem real dos 4
cantos + altura consistente, não mais um ponto só) e atualizado
`FindNearestWallPoint` pra usá-la, mas nunca atualizou quem chama esses
métodos em `DecorationModeHandler.cs` - faltava um argumento
(`placeable`), erro de compilação. Terminei essa troca: `HandleWallGuidance`
agora chama `WorldNavigationHandler.IsValidWallPosition` (a checagem
real) tanto pra decidir "Parede bem aqui" quanto pra alimentar a busca
do ponto mais próximo - antes disso, "Parede bem aqui" podia disparar
sem nunca corresponder a uma posição realmente válida (exatamente o
que o log da rodada 29 mostrou: anunciou "bem aqui" duas vezes, e nas
duas vezes o Enter respondeu "Não posso soltar aqui").

### Centro de mesa - encaixe automático ao confirmar (igual ao banco)

O log confirmou que o centro de mesa FUNCIONA, só que de um jeito
trabalhoso: cada vez que pega de novo (F) nasce na posição atual do
personagem, e a orientação "Superfície: X pra..." exige alinhar
manualmente, telha por telha, até bater exatamente no ponto da
superfície - bem diferente do banco, que já encaixa sozinho com Enter
mesmo de um pouco longe. Adicionado em `HandleConfirmPlacement`: se o
item precisa de superfície (`isPlaceableOnSurface`) e existe uma
superfície válida dentro do mesmo raio generoso do banco (2.5
telhas), Enter encaixa direto nela (`Placeable.AddPlaceableToSurface`,
a mesma chamada que já usamos ao vivo durante o movimento) em vez de
exigir posição pixel-perfeita.

### Planta - diagnóstico em vez de mais suposição

A planta usa a MESMA checagem genérica de espaço livre que os bancos
(`ItemSpace.IsItemSpaceValid`) - que funciona pra banco. Mas o log
mostrou ela nunca validando em NENHUMA posição testada, com tentativas
de verdade usando as setas (confirmado lendo as teclas brutas do log).
Não dava pra adivinhar a causa com confiança (tinha uma teoria - a
posição de nascimento, vinda de onde o personagem estava ao apertar F,
pode nunca cair exatamente alinhada com a grade do jogo, e mover em
passos de exatamente 0.5 nunca corrigiria esse desalinhamento - mas
isso é só uma teoria, não confirmada lendo o código sozinho). Em vez
de aplicar um fix às cegas, replica os mesmos testes internos que
`ItemSpace.IsItemSpaceValid` faz (`WorldNavigationHandler.
LogItemSpaceValidityDiagnostic`, ambos os métodos usados são públicos)
e loga o resultado de cada quadrado de construção (posição, tipo de
local, se a localização é válida, se o quadrado em si é válido) -
gated por F12, dispara periodicamente enquanto a posição estiver
inválida. O próximo teste vai mostrar exatamente qual checagem está
rejeitando, em vez de mais uma tentativa de correção sem prova.

Build limpo.

## 31ª rodada (2026-06-23) - andar agora move os itens sem assento; diagnóstico mais fundo; ainda sem confirmação ao vivo

Usuário testou a rodada 30 e reportou: quadro - andando chegou o mais
perto possível ("1 pra direita") mas não tinha como ir mais pra
direita, e não conseguiu soltar; planta - nenhuma orientação recebida;
centro de mesa - **conseguiu** (encaixe automático funcionou). Pedido
explícito: só gerar um novo teste quando tudo estiver testável e
funcionando de forma parecida (ainda não é o caso); e se pra
quadro/planta o jeito real de jogar é andando (não só com as setas),
que as instruções se atualizem conforme anda.

### Centro de mesa: confirmado funcionando

Sem mudanças necessárias - o encaixe automático da rodada 30 resolveu.

### Achado importante sobre o quadro: a orientação nunca mudou durante toda a sessão

Lendo o log cru (teclas físicas) desta rodada: durante uma sessão
inteira de 27 segundos segurando o quadro, com pelo menos uma seta
direita confirmada apertada, o anúncio ficou parado em "Parede: 1 pra
direita" do início ao fim - nunca virou "Parede bem aqui". Não existia
nenhum log mostrando a posição real do quadro durante esse tipo de
checagem (só a checagem de vaga de banco tinha esse log) - adicionado
agora (`DecorationMode: wall guidance calc cur=... target=...`), pra
confirmar de vez se a tecla simplesmente não está movendo o quadro
nessas situações, ou se move mas o arredondamento nunca cruza pra
"bem aqui". Sem isso, qualquer correção seria só mais um palpite.

### Planta: diagnóstico aprofundado

A rodada 30 já tinha confirmado `squareValid=False` de forma estável,
mas não dizia QUAL das várias checagens internas (`BuildSquare.IsValid`
testa zona, tipo de piso, se é parede, se tem jogador em cima, entre
outras) é a culpada. Adicionado ao mesmo diagnóstico: zona do local
vs. zona exigida pelo item, tipo de piso vs. exigido, se o ponto é
parede, e a distância até o jogador - todos lidos de APIs públicas do
próprio jogo. Não dei nenhum fix ainda - só ficou claro que NÃO é o
local genérico (Tavern, sempre `locationOk=True`); precisa do próximo
log pra saber qual das checagens mais específicas é a real culpada.

### Mudança de arquitetura: itens sem assento agora seguem o jogador andando

O usuário descreveu como realmente jogou: pro banco, navega o cursor
virtual com as setas (já funciona, decoupled do jogador desde a rodada
18); pros outros itens, precisa andar pra chegar perto, e pediu que a
orientação se atualize enquanto anda, se for assim mesmo que funciona.

Confirmado no código: desde a rodada 18 (decoupling do cursor),
`_heldIntendedPosition` só muda com as setas - andar com WASD não tinha
EFEITO NENHUM no item seguro (só regarrar com F, que nasce na posição
atual, dava a impressão de que andar ajudava). Implementado: pra itens
SEM Seat (quadro/planta/centro de mesa), o item segurado agora rastreia
a posição do jogador ao vivo + um deslocamento ajustável pelas setas
(zerado ao pegar) - andar move a base, setas ajustam fino por cima
(necessário pro caso da parede, onde o ponto exato pode ficar um
pouco além de onde a colisão do personagem deixa chegar andando). O
banco continua exatamente como estava, sem mudança. Também ajustado: o
aviso "use as setas, não WASD" agora só dispara pra banco - pros
outros itens, WASD é o movimento principal esperado agora, então o
aviso antigo estaria contradizendo o próprio comportamento novo.

**Importante**: esta mudança de arquitetura ainda NÃO foi testada ao
vivo nenhuma vez. Build limpo, mas não vou pedir um teste isolado só
disso - juntando com os dois diagnósticos pendentes (quadro e planta)
num próximo teste único, conforme pedido.

## 32ª rodada (2026-06-23) - quadro: causa raiz medida e corrigida com encaixe automático; planta: rastreado até a checagem real de ocupação

Usuário testou a rodada 31 (andar move o item agora): confirmou que
funciona bem ("muito bom"). Mas: quadro continua não dando pra
colocar - "ele queria que eu fosse pra direita, sendo que eu já
estava na parede"; planta - sem nenhuma orientação ainda. Pedido:
"resolva isso em definitivo".

### Quadro: causa raiz medida (não suposta) e corrigida

O novo log (com o diagnóstico `wall guidance calc cur=... target=...`
da rodada 31) deu a resposta exata: o jogador andou até ser bloqueado
pela colisão da própria parede em `cur=(5.75, 910.21)`, mas o ponto
realmente válido era `target=(6.02, 910.10)` - uma diferença real de
~0.27 unidades. Faz sentido: o ponto válido fica praticamente dentro/
atrás da parede, e o corpo do personagem para na face de fora dela -
uma seta resolveria (o item segurado não tem colisão), mas o usuário,
já encostado na parede, não tinha motivo pra achar que precisava
trocar de andar pra setas bem naquele momento.

Corrigido com o MESMO padrão já comprovado pro banco e pro centro de
mesa: `HandleConfirmPlacement` agora encaixa automaticamente no ponto
de parede mais próximo ao apertar Enter (dentro do mesmo raio
generoso), em vez de exigir alinhamento manual exato. Como isso usa
exatamente o padrão que já funcionou duas vezes antes (banco, rodada
71; superfície, rodada 94), a confiança aqui é alta - mas ainda não
testado ao vivo.

### Planta: rastreado até a checagem real de ocupação do jogo

O diagnóstico da rodada 31 (zona/piso/parede) descartou todos esses -
mesmo numa posição com piso real, fora de qualquer parede, e a 8-10
unidades de distância do jogador, `squareValid` continuou `False`. Lendo
`BuildSquare.IsValid` até o fim, a última checagem que sobra é
`WorldGrid.NGDHDMAMGPI` - testa o `WorldTile.canPlaceObjects` e se já
existe algo registrado em `blockingObjects` NAQUELE tile específico
(dado de ocupação do próprio jogo, não uma checagem de colisão ao
vivo). Adicionado ao diagnóstico: lê o `WorldTile` direto e loga o
nome de qualquer objeto bloqueando ali.

**Possibilidade real a considerar**: esse mesmo cantinho da taverna foi
o local de teste de mais de 90 rodadas desta funcionalidade (bancos,
mesas, itens soltos, quadros - tudo testado ali repetidamente) - pode
ser que sobrou bagunça registrada como "ocupado" naquela área
específica, não um bug de código. Vale tentar a planta numa área mais
limpa da taverna no próximo teste, além de olhar o log.

Build limpo. Não vou pedir um teste isolado - aguardando o usuário
decidir quando testar os dois juntos.

## 33ª rodada (2026-06-23) - bug real achado no próprio encaixe automático: confirmação na mesma frame lia dados físicos desatualizados

Usuário testou a rodada 32: planta colocou com sucesso (mas pediu a
mesma "acertividade" do banco); quadro "completamente louco" - disse
que ia colocar mas depois as coordenadas aumentaram tudo de novo.
Também esclareceu o pedido sobre frequência de testes: só pedir teste
quando tiver correção concreta pra validar, ou precisar mesmo de mais
log - não "só quando tudo estiver no mesmo nível" (mais estrito do que
o pretendido).

### Causa real do "quadro ficou louco" - achada no log, não suposta

O log mostrou a sequência exata: "wall snap attempt" calcula a posição
certa -> `Deselect()` chamado NA MESMA frame -> "Não posso soltar
aqui" -> **uma frame depois, na MESMA posição, "Posição válida"/
"Parede bem aqui" apareceram**. Ou seja: o ponto estava certo, só que
o `Deselect()` da engine lê `itemBase.bounds` (dado de física do
Collider2D), e o Unity não atualiza esse dado instantaneamente quando
a posição é escrita direto no `transform` no mesmo Update() - precisa
de uma frame pra "assentar". Como o `Deselect()` falhou, o item
continuou seguro, e a frame seguinte do `HandleCursorMovement` (o
modelo de seguir o jogador da rodada 95) recalculou a posição a partir
de `posição do jogador + deslocamento ANTIGO`, jogando o item de volta
pra longe - exatamente o "aumentou as coordenadas de novo" relatado.

Corrigido: o encaixe automático (parede e superfície) agora espera uma
frame antes de chamar `Deselect()` - igual ao banco já fazia desde a
rodada 71 (mesma classe de problema de tempo, motivo diferente: o
banco precisa de uma frame pra Seat.GetNeighbourTable recalcular,
parede precisa de uma frame pra Collider2D.bounds atualizar). Também
corrigido: ao encaixar, o deslocamento-do-jogador é atualizado junto,
pra não ser desfeito numa frame seguinte mesmo se o Deselect() falhar
de novo por outro motivo.

### Planta: mesma precisão do banco, agora de verdade

Como não existe checagem "pura" pra espaço livre genérico (a checagem
real lê a posição de cada quadrado de construção, que só existe se o
objeto estiver literalmente naquela posição - diferente da parede, que
tinha uma versão sem precisar mover o objeto), criei
`FindNearestValidItemSpacePosition`: move o objeto real candidato por
candidato, testa, e devolve pra posição original - tudo dentro da
mesma chamada (sem renderizar frame no meio), então não pisca na
tela. Com isso, a planta agora tem orientação de verdade ("Lugar
livre: X pra direita...") e encaixe automático ao confirmar, igual ao
banco/parede/superfície.

Build limpo. Essas são correções concretas com evidência direta do
log (não suposição) - pedindo teste agora.

Build limpo.

## 34ª rodada (2026-06-23) - causa do "cursor não bate com minha posição" achada; modelo unificado (tudo com setas, igual ao banco) e checagem de validade unificada usando a do próprio jogo

Usuário testou a rodada 33 e deu a pista decisiva. Resultados: planta
colocou de novo; quadro continua quebrado ("disse que ia colocar e as
coordenadas aumentaram de novo", fala "1 pra direita" mas não dá pra
ir). E o ponto-chave: "quando abro o item com F, ele diz tipo 5 pra
direita, mas eu estou a muito mais distância disso - é como se o
cursor que você está olhando e a minha posição não estivessem se
comparando." Mais: confusão por ter dois modelos (banco: B+setas;
itens: F+andar) e pedido de acertividade.

### As duas causas, confirmadas no log

1. **Descompasso de coordenadas**: itens pegos com o F nativo setam o
   `selectedGameObject` direto, sem passar pelo nosso `HandleGrab` - aí
   o `_heldIntendedPosition` ficava nulo, o `HandleCursorMovement`
   saía na hora todo frame, e o item ficava CONGELADO onde nasceu
   (ex.: (3.58, 909.60), perto da parede) enquanto o jogador estava em
   outro lugar (ex.: (6.43, 906.68)). A orientação comparava o item
   congelado com o alvo = sem relação com onde o jogador está.
   Exatamente o que o usuário descreveu.
2. **Parede não coloca**: a checagem caseira da rodada 93
   (`IsValidWallPosition`, 4 cantos da caixa) devolvia um ponto que a
   checagem REAL do jogo rejeita (confirmado: alvo (6.08, 910.10), o
   deselect adiado deu False uma frame depois - não é timing, é alvo
   errado mesmo).

### Correção em duas partes

1. **Busca de validade unificada** (`FindNearestValidPosition`): usa a
   PRÓPRIA checagem do jogo (`Placeable.IsObjectInValidLocation`) -
   move o objeto, `Physics2D.SyncTransforms()`, checa, do mais perto
   pro mais longe com parada no primeiro válido. Substitui tanto a
   replicação de parede quanto a busca de espaço-livre da rodada 33.
   Garante que o ponto achado é exatamente um que o Enter aceita. Se
   não acha nenhum, loga "NO valid spot - may need a different facing/
   rotation" - se o quadro precisar girar, vamos saber na próxima.
2. **Modelo de movimento unificado**: revertida a experiência das
   rodadas 94/95/96 ("itens sem assento seguem o jogador andando").
   TODOS os itens agora usam o mesmo cursor virtual por setas do banco
   (desacoplado do andar), inicializado ao pegar em QUALQUER via (a
   inicialização no F-grab era a peça que faltava). Isso remove o
   descompasso de coordenadas de vez (não há comparação jogador-vs-
   item; as setas movem o item e a orientação é exata) e dá um único
   modelo consistente, o que o usuário já conhece do banco. Removido
   `_heldOffsetFromPlayer`; o aviso de WASD volta a valer pra todos os
   itens; os blocos de encaixe de parede e espaço-livre viraram um só.

**OBS**: isso reverte o pedido da rodada 95 (andar) - mas aquele
pedido veio de as setas PARECEREM quebradas (que era o bug de
validade, não as setas). Sinalizado ao usuário; reversível se ele
preferir andar.

A única diferença que sobra entre banco e itens é a TECLA de entrada
(banco: B; itens: F nativo) - depois disso, os dois são setas + Enter,
igual.

Build limpo. Correções concretas com evidência do log - pedindo teste.

## 35ª rodada (2026-06-23) - planta movimentou bem; quadro/forro "diziam válido mas não soltavam" - achada a causa real no Deselect

Usuário testou a rodada 34: planta colocou e ficou "bem ajustada no
movimento". Mas quadro (parede) e forro (superfície) não soltavam,
"mesmo dizendo que a posição era a certa". Autorizou subagentes e
pediu pra entendermos a fundo essas mecânicas, pra evoluir mais rápido
em tarefas futuras.

### A causa real (lendo `Placeable.Deselect` de verdade)

O log mostrou o padrão exato: planta `Deselect -> True`; quadro e forro
diziam "Posição válida"/"bem aqui" mas `Deselect -> False` na MESMA
posição. Lendo o código:
- `Deselect` (linha 1847) usa `IsObjectInValidLocation(BIOKGEFFNAA:
  TRUE)` - a gente checava com `false`. (Pra esses itens o resultado é
  o mesmo, então não era essa a diferença real.)
- `canBePlaced` (checado em `DeselectAction`) é campo MORTO, sempre
  true - confirmado por grep. Não era ele.
- **A real**: a gente ADIAVA o Deselect uma frame (rodada 96, pra a
  caixa de colisão "assentar"). Mas nessa frame extra a `Placeable.
  WhileSelected` do jogo roda e BRIGA com a gente: re-solta o forro da
  superfície e/ou move o quadro pela posição do cursor (que a gente
  contorna desde a rodada 82). Aí no Deselect adiado o item já não está
  mais válido. A planta sobrevivia porque o caminho dela (`itemSpace`)
  lê `transform.position` direto, sem depender de `currentSurface` nem
  da caixa de colisão.

(Ver a nova seção "Mecânica de confirmação de posicionamento (Deselect)
- REFERÊNCIA" no topo deste arquivo - documentei tudo isso pra acelerar
tarefas futuras, como o usuário pediu.)

### A correção

Snap + `Physics2D.SyncTransforms()` + `Deselect` tudo na MESMA frame
(`DecorationModeHandler.SnapAndConfirm`), em vez de adiar. O motivo
original de adiar (caixa de colisão atrasada) agora é resolvido pelo
SyncTransforms, então uma frame só é correto E não dá a brecha pro jogo
interferir. O bloco de superfície também passou a reaproveitar o
`currentSurface` já anexado se houver. Adicionado `WorldNavigationHandler.
LogDeselectGate`: loga todos os fatores da decisão real do Deselect
(IsObjectInValidLocation true/false, canBePlaced, currentSurface,
physicalSpace) bem na hora - se ainda falhar, o log aponta o sub-check
exato em vez de mais teoria.

Build limpo. É uma correção concreta (mais rede de diagnóstico) -
pedindo teste.

## 36ª rodada (2026-06-23) - quadro com setas e vela/forro de mesa: duas causas raiz (lista de física assenta só no FixedUpdate; encaixe sendo desfeito)

Usuário testou a rodada 35. Resultados: planta OK; forro colou mas em
dúvida se ficou sobre a MESA; quadro só colou "por acidente" andando até
a parede (com as SETAS anunciava certo mas o Enter não colava); item
novo VELA pedia mesa, usou 10 e a missão NÃO concluiu; e pediu que o
inventário informe a QUANTIDADE quando há mais de um item igual.

### Causa raiz 1 - "anuncia válido com as setas mas Enter falha" (quadro, e o flicker do forro)

Lendo o log novo + `PhysicalSpaceWall.ValidPosition()` (linha 664):
- A MESMA posição (10.96, 910.23) deu `validTrue=False physicalSpaceOk=
  False` com as setas (jogador longe) e `validTrue=True physicalSpaceOk=
  True` andando até lá. Nos gates de superfície, `physicalSpaceOk`
  OSCILAVA True/False na mesma coordenada (11.85, 906.31) entre
  tentativas.
- `ValidPosition()` percorre uma lista `colliders` populada por
  `OnTriggerEnter2D`/`OnTriggerExit2D` - e esses callbacks SÓ disparam
  no PASSO DE FÍSICA (FixedUpdate), NÃO num `transform.position =` +
  `Physics2D.SyncTransforms()` da mesma frame. Logo após teletransportar
  o item pelas setas, a lista ainda guarda sobreposições velhas -> lê
  inválido num lugar genuinamente válido. Andar até lá funcionava porque
  o movimento é gradual e a física assenta.
- Ou seja: a abordagem "tudo numa frame" da rodada 35/99 (boa pra evitar
  a WhileSelected brigar) é justamente RUIM pro caminho parede/superfície,
  porque não dá tempo da física processar os triggers.

**Correção**: `SnapAndConfirm` não desiste mais se o primeiro Deselect
falhar. Arma um RETRY (`_pendingSettleDeselect`, até 30 frames): mantém o
item FIXADO no ponto válido (re-cravando posição + cursor + SyncTransforms
toda frame, pra a WhileSelected não arrastar) e re-tenta o Deselect até a
lista de triggers limpar e ficar válido, ou estourar o orçamento. Loga
"settle deselect -> True" no sucesso ou "settle gave up" + deselect gate
se desistir.

### Causa raiz 2 - vela/forro de mesa não contam pra missão (encaixe sendo desfeito)

Lendo `SurfaceSortOrder.UpdatePosition` (linha 1003) + a construção do
`snapToPositionArray` a partir de `tablecloths.tableCovers` (linha 242):
forro E itens de superfície (vela, centro de mesa) têm PONTOS DE ENCAIXE
(`SnapToPosition`) próprios DA MESA. `UpdatePosition` move o
`transform.position` EXATAMENTE pra cima do ponto de encaixe. MAS o nosso
`HandleCursorMovement` (e o `SnapAndConfirm`), logo depois de chamar
`UpdatePosition`, fazia `transform.position = pos` (posição crua do
cursor) - DESFAZENDO o encaixe. Sem ficar no ponto designado, a missão
não conta o item.

**Correção**: só re-cravo a posição crua quando `placeable.
snappedToPosition == false` (superfície genérica sem ponto designado,
ex. quadro num móvel). Quando encaixou num ponto (`snappedToPosition ==
true`), o item FICA no encaixe. Vale tanto no movimento ao vivo quanto no
retry de confirmação. Diagnóstico novo: "surface attach surface=... 
snapped=True/False finalPos=..." em cada anexação, pra o próximo log
mostrar em que superfície a vela encaixou e se encaixou de verdade.

### Quantidade no inventário (pedido 4)

`KeyboardUINavigator.DescribeSlotUI` agora lê `Slot.Stack` (decompilado,
campo público): se > 1, anuncia "nome, N" (ex. "Vela, 10"); item único
continua só com o nome.

Build limpo. Correções concretas baseadas em log + código, mais rede de
diagnóstico - pedindo teste.

## 37ª rodada (2026-06-23) - vela nunca encaixava (snapped=False); agora mira o ponto de encaixe real da mesa; uso rápido com quantidade

Usuário testou a rodada 100: quadro colou só na 2ª/3ª tentativa (ainda
precisou andar); 10 velas colocadas mas "no mesmo lugar" e a missão NÃO
concluiu; pediu que o uso rápido informe quantidade e atualize ao usar.

### Causa raiz da vela (achada no log da rodada 100)

O diagnóstico "surface attach" mostrou a vela grudando numa superfície
genérica chamada "Surface" com **snapped=False TODA vez**. Itens de
superfície que precisam contar pra missão (vela, forro, centro de mesa)
só registram quando encaixam num PONTO designado (`SnapToPosition`) -
nossa correção da rodada 100 (preservar o snap) não adiantava porque o
snap NUNCA acontecia: a gente pousava num lugar qualquer permitido, não
no ponto da mesa.

Lendo `SurfaceSortOrder.GetSnapItem` + a estrutura `SnapToPosition`
(campos públicos: `item`/`items`/`transform`/`used`/`canBeRepeated`): o
jogo escolhe o snap pela posição do CURSOR (decorrelacionada das setas
desde a rodada 82), por isso falhava pra gente.

### A missão

O objetivo real (log) é "Coloque seus novos itens na taverna" - vale pra
TODOS os itens novos (pintura, centro de mesa, planta, vela), não é
"10 velas". As 10 são só o tamanho da pilha. Dois itens não podem ir no
mesmo snap (`!canBeRepeated` -> `GetAnyItemSnappedToPosition` bloqueia em
`IsItemAllowed`), então vão em pontos/mesas diferentes.

### A correção

Novo `WorldNavigationHandler.FindNearestSnapPosition` - varre o
`snapToPositionArray` público de toda `SurfaceSortOrder`, acha o ponto
LIVRE (`!used`) cujo `item`/`items[]` casa com o item, e retorna a
`transform.position` exata. Não usa o cursor (evita o problema da rodada
82).
- `HandleConfirmPlacement` (branch surface): tenta primeiro esse snap
  (`SnapAndConfirm(..., "surfaceSnap")`) - leva a vela ao ponto exato, aí
  `AddPlaceableToSurface` seta `snappedToPosition=true`. Fallback pro
  comportamento antigo (superfície genérica) se o item não usar snap.
- `HandleSurfaceGuidance`: trava no snap point quando existe
  (`_lockedTargetSnapPos`) e fala "Lugar na mesa: X pra ..." / "Lugar na
  mesa bem aqui", em vez do centro genérico da superfície.
- `HandlePlacementResult`: distingue "Item encaixado na mesa"
  (snappedToPosition) x "Item solto na superfície, mas não num ponto de
  mesa" x "Item solto".
- Diagnóstico `LogSnapTargets` (no grab de item de superfície): lista
  toda mesa com snap livre pra esse item (nome/pos/used/dist), ou avisa
  "NO snap target" se o item não usa snap.

### Quadro (parede) - ainda parcial

O retry da rodada 100 ajudou (colou, antes nem colava) mas precisou de
2-3 tentativas. Se o ponto travado encostar noutra decoração, o retry
esgota - o log "settle gave up" + "deselect gate" do próximo teste
mostra o motivo exato. Sem mudança nova aqui além do que já existe.

### Uso rápido com quantidade (em InventoryTransferHandler.cs)

`OnHotbarSelectionChanged` agora fala "nome, N" quando N>1.
`PollSelectedHotbarStack` (novo, roda por frame via
`EnsureHotbarSelectionAnnouncer`) acompanha o slot selecionado e anuncia
a contagem ao DIMINUIR ("9", "8"... "acabou"). E `DescribeSlotUI`
(inventário) já passou a ler `Slot.Stack` na rodada 100.

Build limpo. Pedindo teste.

## Próximo teste (com F12 ativado antes de entrar no jogo)

1. Vela: leve até perto de uma MESA - deve ouvir "Lugar na mesa: X pra
   ..." e, no ponto, "Lugar na mesa bem aqui". Enter -> "Item encaixado
   na mesa". Coloque várias em mesas/pontos diferentes; ver se a missão
   "Coloque seus novos itens na taverna" conclui.
2. Forro: igual - confirmar "Item encaixado na mesa".
3. Quadro (parede): com as SETAS. Se precisar de várias tentativas,
   mandar o log ("settle gave up").
4. Uso rápido: selecionar velas (fala quantidade), usar algumas (conta
   9, 8...).
5. Banco: teste rápido (B + setas) só pra garantir que não quebrei nada.
6. "testei" quando terminar. Logs novos: "snap target ...", "surface
   attach ... snapped=...", "settle gave up", "deselect gate [...]".

## 38ª rodada (2026-06-23) - parede simplificada; vela encaixa ao vivo (não amontoa); categorias; proximidade de vela

Usuário testou a rodada 37 (escolheu "tudo de uma vez"):

### Parede (A) - simplificada a pedido
"andar até uma parede, só informe se aquela parede está livre" - a
orientação por coordenada (FindNearestValidPosition) mandava pra paredes
ocupadas. `HandleWallGuidance` agora só fala "Parede livre aqui, pode
soltar" / "Aqui não dá, mova pra uma parede livre" pro lugar ATUAL do
quadro (via `IsObjectInValidLocation`, o mesmo juiz do Deselect). O
confirm da parede coloca onde o item está (settle-retry absorve o flicker
de física). Removidos `_lockedTargetWallPoint`/`_lastWallGuidanceTileOffset`
(agora `_lastWallFree`).

### Vela (B) - encaixa ao vivo, não amontoa
Rodada 37 encaixava só às vezes ("a última das cinco"). Causa: o attach
ao vivo (`HandleCursorMovement`) grudava a vela SOLTA numa superfície
genérica (snapped=False), então o ponto nunca ficava `used` e a próxima
vela ia pro mesmo lugar. Agora, quando a orientação trava um ponto livre
(`_lockedTargetSnapPos`) e a vela chega perto, ela é puxada exatamente pro
ponto e encaixa (marca `used`); e NÃO gruda solta numa superfície genérica
quando existe um ponto de mesa de verdade. Reutiliza o lock da orientação
(sem varredura cara por frame).

### Categorias (C1) - em WorldNavigationHandler
"Missão" -> "Pendentes". Nova "Repositivos". Vela (id 605) colocada ->
Repositivos enquanto acesa, -> Pendentes quando gasta (limite do jogo:
Crafter fuel <= 1). Bancos com `Seat.table != null` saem da lista.

### Proximidade de vela (C2 - parcial)
Vela é um `Crafter` (`Placeable.SetFuel`/`Crafter.LCCABPFHCOL`).
`HandleCandleAnnouncement` (cache `_cachedCandles` no mesmo ciclo de 20s
dos bancos) fala "Vela acesa" / "Vela apagada, precisa repor" (limite
fuel <= 1) ao se aproximar. A % EXATA ficou pendente: o combustível
máximo da vela não é lido de forma confiável no código ofuscado - o
diagnóstico "candle proximity ... fuel=N" loga o valor real pra calcular
a % na próxima rodada (sem chutar).

Build limpo. Pedindo teste (roteiro em novo_pedido.txt).

## 39ª rodada (2026-06-23) - parede: orientação de volta (mirando parede LIVRE por geometria estável)

Rodada 102 testada: vela de proximidade OK; QUADRO não colou em nenhuma
parede, e o usuário ficou travado nele.

Causa no log: remover a orientação (rodada 102) PIOROU. O quadro ficava
em y≈906 (dentro da sala), mas paredes válidas ficam em y≈910 (onde colou
nas rodadas 99/100). Sem nenhuma dica de "suba", o jogador cego varria as
setas à toa - "wall free check" dava free=False sempre, e o único confirm
mostrou `physicalSpaceOk=True` mas `validTrue=False` (a falha era a
GEOMETRIA da parede, não a física - o quadro simplesmente nunca esteve
sobre uma parede).

Correção: orientação direcional de volta, mas mirando uma parede LIVRE de
verdade. `WorldNavigationHandler.FindNearestFreeWallTile` varre com a
checagem de tile estável `WorldGrid.ALNFLFCLIEP` (a flag `.wall` do tile -
sem o flicker da `IsObjectInValidLocation`/physicalSpace, que quebrou a
`FindNearestValidPosition` pra parede) + distância a Placeables de parede
existentes (ocupação). `HandleWallGuidance` agora fala "Parede livre: X
pra cima/...", "Parede livre aqui, pode soltar" quando o ponto é mesmo
válido, ou "Nenhuma parede livre por perto". O confirm encaixa no tile de
parede livre travado quando o ponto atual não é válido (perdoa, +
settle-retry). Diagnósticos "wall lock"/"wall free check ... lockedWall="
pra confirmar na próxima se falta um pequeno offset entre o tile de parede
e o ponto que o jogo aceita.

Build limpo. Pedindo teste.

## 40ª rodada (2026-06-23) - ERRO DE RAIZ achado: validade de parede usa as 4 quinas do itemBase.bounds, não a transform.position

Usuário: quadro ainda não cola; autorizou subagentes, pediu pra aprofundar
e IMPLEMENTAR a solução forçando no ponto de parede válido mais próximo.

Rodei um subagente que leu a mecânica de validade de parede em
`Placeable.cs`, e VALIDEI lendo os métodos eu mesmo (`FNPBNFFEBAF`:1688,
`HHAEKEAPKOE`:1594, `WorldGrid.KHJJCAGIJAP`:1330).

### O erro de raiz de TODAS as rodadas de parede

`IsObjectInValidLocation` -> (parede) `APNKIDLNFLC` -> `FNPBNFFEBAF`. Essa
checagem olha as **4 QUINAS do `itemBase.bounds`** (a caixa de colisão),
NÃO a `transform.position`. Todas as tentativas anteriores (inclusive a
rodada 103, que checava o transform contra o grid de parede) testavam a
coisa errada - por isso a orientação levava a lugares que o jogo nunca
aceitava.

Regra real de `FNPBNFFEBAF` (replicada exatamente):
- As 4 quinas do bounds em tiles de parede (`WorldGrid.ALNFLFCLIEP` -> flag
  `.wall` do tile).
- Cada quina com chão embaixo (`WorldGrid.KHJJCAGIJAP`) na MESMA altura
  (arredondada a 0.5).
Ambos são lookup de tile puro - ESTÁVEIS, sem o flicker de física (o
flicker só existe no sub-check de `physicalSpace`, que não dá pra varrer
sincronamente).

### A correção

`WorldNavigationHandler.FindNearestValidWallPosition` replica
`FNPBNFFEBAF` exatamente sobre posições candidatas das quinas do bounds (o
offset transform->bounds é medido em runtime do `itemBase.bounds` ao vivo,
porque é específico do item), + check de ocupação sem flicker (distância a
Placeables de parede). O confirm da parede agora FORÇA o quadro no ponto
válido mais próximo no Enter (pedido explícito), e o settle-retry confirma
com a checagem real do jogo em frames reais. A orientação aponta pro mesmo
ponto. Diagnósticos "wall lock"/"wall confirm"/deselect gate mantidos pra
pinar qualquer resto (ex.: `FCGPPPPDFMB` x-sweep ou zona - não replicados,
confiando na checagem real no confirm).

Build limpo. Pedindo teste.

## 41ª rodada (2026-06-24, round 106) - vela com force-snap (igual ao quadro)

Quadro e vela confirmados funcionando ao vivo. Usuário pediu pra deixar a
vela tão tolerante quanto o quadro: ao apertar Enter, achar o ponto de
mesa válido (que a missão exige) mais próximo e ARRASTAR a vela pra lá
mesmo de longe. Feito: o branch de superfície do `HandleConfirmPlacement`
agora chama `FindNearestSnapPosition` com `SeatSlotGuidanceSearchRadius`
(sala toda) em vez de `SnapToSlotRadius` (~2.5u), e força o snap via
`SnapAndConfirm` ("surfaceSnap") - settle-retry confirma. Diagnóstico
"candle force-snap" adicionado.

Build limpo. Pedindo teste.

## Arquivos relevantes

- `DecorationModeHandler.cs` - handler novo desta feature. Modelo
  unificado: todos os itens movem por setas (desacoplado do andar),
  inicializado ao pegar em qualquer via. `SnapAndConfirm` faz snap +
  SyncTransforms + Deselect na mesma frame.
- `Main.cs` - inicializa e chama `Update()` (incondicional, fora do
  `anyUiOpen`).
- `WorldNavigationHandler.cs` - `Seat` na categoria "Missão";
  numeração estável de banco/mesa; `FindNearestValidPosition` (busca
  unificada usando a checagem real do jogo, p/ parede e espaço livre);
  `FindNearestValidSurface`/`FindSurfaceAtPosition` (superfície);
  `LogDeselectGate` (loga a decisão real do Deselect);
  `LogItemSpaceValidityDiagnostic` (diagnóstico de espaço livre).
