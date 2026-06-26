# Módulo: Navegação no mundo (zonas, interagíveis, mapa)

> Ver convenção em `docs/modules/main-menu-and-options.md` (cabeçalho).
> Ainda NÃO implementado - este arquivo documenta a investigação inicial
> (pedida pelo usuário em 2026-06-19: "comece a analisar também como e
> onde estão entradas, objetos, se existe mapa, interagíveis, consigo
> andar mas não sei pra onde ir") e a proposta de próximos passos.

## Status atual

Pesquisa feita, nada implementado ainda. Corresponde à "Feature 4: Core
Gameplay Status + Navigation" já prevista em `docs/feature-plan.md`
(prioridade 4 da lista, depois de menu/tutorial). Próximo round deve
confirmar viabilidade ao vivo antes de eu ligar isso de verdade (regra
do projeto: nunca acessar singleton/classe de jogo sem confirmar
primeiro - risco de crash).

**2026-06-19, segunda rodada:** o log de diagnóstico do prompt de ação
já capturou uma amostra real: `"[E]  Arrumar a Cama"` em
`ButtonsContext/ContentButtonsContext/ButtonsContextPanel/ActionName/Name/Text`
(o jogo lembrando o jogador de arrumar a cama, parte do tutorial).
Confirma o formato esperado (`"[E] " + nome da ação`) e o caminho fixo
do texto na tela - dado suficiente pra implementar o anúncio de "tem
algo aqui" com segurança no próximo round (continua sem ser feito
ainda, por ser uma rodada cheia de outros consertos).

## O que existe no jogo (achado no código decompilado)

1. **Zonas (`PlayerController`)**: evento público
   `OnZoneChanged(int playerNum, ZoneType from, int zoneIndex)`,
   disparado pelo próprio jogo sempre que o jogador entra/sai de uma
   zona. `ZoneType` é um enum pequeno e só cobre áreas DENTRO da
   taverna: `DiningRoom`, `CraftingRoom`, `RentedRoom`, `Cellar`,
   `WoodWorkshop`, `MetalWorkshop`, `StoneWorkshop` (+ variantes de
   quarto de outros jogadores, sem uso no nosso caso solo). Não cobre
   áreas externas (rua, fazenda, pedreira) - essas provavelmente são
   rastreadas de outro jeito (cena Unity? outro sistema de "distrito"?
   - ainda não investigado).
   - **Proposta**: assinar esse evento (`PlayerController.GetPlayer(1)
     .OnZoneChanged += ...`) e anunciar o nome da zona em português
     sempre que mudar - é o jeito mais direto de responder "onde estou
     agora" dentro da taverna. Maior risco: a instância de
     `PlayerController` pode ser recriada entre cenas (igual outros
     singletons já documentados) - preciso confirmar a instância atual
     a cada vez que o jogo fica "ready" (mesmo padrão usado pra
     `MainUI`/outros), e não confiar numa assinatura única feita uma
     vez só.
2. **Interagíveis (`InteractObject`)**: o jogo já rastreia, por conta
   própria, "qual objeto está no alcance de interação agora"
   (`GetCurrentInteractGO()` / `SetCurrentInteract(...)`) - é
   literalmente o mecanismo que decide se o prompt "[E] Fazer algo"
   aparece na tela. **Esse prompt de ação já está sendo capturado pelo
   nosso scanner de diálogo (`DialogueAnnouncer`), só que hoje é
   FILTRADO como ruído** (`ActionPromptPattern`). Adicionei nesta
   rodada um log de diagnóstico (modo debug) que mostra esse texto e
   caminho mesmo filtrado, pra eu conseguir confirmar o formato real
   (`"[E] Arrumar a Cama"` foi visto antes, numa rodada bem anterior,
   mas sem confirmação recente) antes de transformar isso num anúncio
   de "tem algo aqui: ...".
   - **Implementado (2026-06-20):** durante o teste da Etapa 1 da
     navegação no mundo, o usuário relatou explicitamente não ter
     NENHUM retorno ao andar perto de algo ("me sinto andando sem
     direção") - virou prioridade imediata em vez de ficar pendente.
     `DialogueAnnouncer.ScanAndAnnounceText()` agora anuncia
     ("Próximo: " + texto sem o "[E]"/"[Q] ") em vez de filtrar,
     sempre que o prompt aparece/muda, e limpa o último anunciado
     assim que ele desaparece (assim, voltar pro MESMO objeto depois
     anuncia de novo, não fica mudo pra sempre). Sem debounce extra por
     enquanto - se andar por um corredor cheio de itens ficar
     repetitivo, ajustar depois com base em teste real.
   - **Corrigido (2026-06-20, rodada seguinte):** confirmado em teste
     real que ficava repetindo sem parar - causa raiz no log: dois
     prompts simultâneos (lareira mostrando "Abrir" E "Combustível" ao
     mesmo tempo) faziam o rastreamento de "última frase" alternar
     entre as duas a cada frame e re-anunciar ambas pra sempre. Trocado
     por um `HashSet` com as frases atualmente visíveis (anuncia só as
     novas). Também adicionado o nome do objeto ao anúncio (ex:
     "Porta: Abrir") via `WorldNavigationHandler.GetNearestInteractionName()`.
   - **Corrigido (2026-06-21):** durante a feature de inventário, o
     usuário tentou usar o esfregão pra limpar a mesa e testou várias
     teclas no escuro (Q, F, E, Ctrl+Enter, clique do mouse) sem
     conseguir confirmar qual funcionava. Achado: a tecla certa estava
     na própria dica visual do jogo (`"[E] Limpar"`) o tempo todo - só
     que `DialogueAnnouncer` REMOVIA essa parte (`ActionPromptPattern`)
     antes de anunciar. Adicionado `ActionPromptKeyPattern` (captura a
     letra) e o anúncio agora inclui "(tecla X)" no final - ex:
     "Próximo: Mesa grande: Limpar (tecla E)". Resolve de raiz qualquer
     ação cujo atalho não seja já conhecido pelo usuário, não só
     "Limpar".
3. **Mapa**: existem várias telas de mapa - `CityMapUI` (mapa da
   cidade), `MiniMapUI` (minimapa), `TreasureMapUI` (mapa de tesouro).
   Todas são visuais (posições espaciais numa imagem) - tornar isso
   acessível de verdade exige "linearizar" a informação espacial (ex:
   listar pontos de interesse com distância/direção em vez de mostrar
   a imagem) - confirmado como desafio médio/alto já em
   `feature-plan.md`. Não investigado a fundo ainda - feature separada,
   maior, pra depois.
4. **Portas/entradas**: várias classes de porta especializadas (`Door`,
   `CellarDoor`, `JapaneseDoor`, `BridgeDoorController`,
   `RentedRoomDoor`, etc.) - todas parecem ser variações de
   `InteractObject` (interagíveis), então devem cair naturalmente no
   mecanismo do item 2 acima (prompt de ação) sem precisar de código
   específico por tipo de porta.

## Próximos passos (precisa de teste ao vivo antes de implementar)

1. Usuário testa andando pela taverna (e se possível saindo dela) com
   F12 ativado, parando perto de portas/objetos/baús/NPCs - sem
   precisar fazer nada além de andar perto e se afastar.
2. Ler o log: confirmar o texto/caminho real do prompt de ação
   (`"Action prompt seen (filtered): ..."`) e se há qualquer log de
   "UI opened"/zona relacionado.
3. Com esses dados reais, decidir e implementar:
   - Anunciar prompt de ação (interagível por perto) em vez de filtrar.
   - Assinar `OnZoneChanged` pra anunciar zona da taverna (com a
     confirmação de segurança de instância mencionada acima).
4. Mapa: decisão de escopo separada, não é prioridade imediata.

## Mais dois gaps confirmados (2026-06-19, rodada 3)

- **Pegar item (tecla Q) não anuncia nada**: confirmado no log - a
  tecla Q foi pressionada várias vezes, o usuário ouviu um som de
  pegar item, mas NENHUM texto novo apareceu em tela (nosso scanner é
  todo baseado em texto TMP, então não tem nada pra capturar aqui).
  Precisa achar o evento real de "item adicionado ao inventário" no
  código decompilado (provavelmente em alguma classe de inventário do
  jogador) e anunciar o nome do item nós mesmos, sem depender de texto
  em tela. Ainda não pesquisado.
- **Entrar na taverna não anuncia nada**: confirmado no log - nenhum
  sinal de texto, zona ou UI aparece no momento em que o usuário relata
  ter entrado. Mesma lacuna já proposta acima (zona via
  `OnZoneChanged`), reforça que isso devia ser a prioridade da próxima
  rodada de implementação (não só pesquisa).

## Pedido grande (2026-06-19, rodada 4): navegação até objetos/entradas/personagens

O usuário descreveu um sistema mais completo: escolher um item/entrada/
objeto/personagem numa lista (ex: com Page Up/Down), e com Ctrl ouvir
orientação de direção pra chegar até lá (ex: "2 pra direita, 1 pra
esquerda"), pelo caminho mais curto possível, evitando ficar presa em
paredes - exemplo concreto dado: não conseguir achar o caminho até a
cama. Pedido relacionado, menor: um som quando o personagem fica
parado batendo numa parede (hoje não faz nenhum som/aviso).

**Pesquisa de viabilidade (ainda não implementado):** o jogo já tem o
PRÓPRIO sistema de pathfinding (A*) usado pelos NPCs -
`PathRequestManager.RequestPath(PathRequestInfo)`, que recebe posição
de início/fim (`startPos`/`goalPos`, `Vector3`) e devolve a rota real
(`Vector2[]`) por callback, já evitando paredes/objetos
(`avoidWalls`/`avoidObjects` no `PathRequestInfo`). Ou seja, NÃO
precisamos construir pathfinding do zero - podemos pedir uma rota pra
esse sistema (posição do jogador -> posição do alvo escolhido) e
traduzir os pontos da rota em orientação falada (e ir recalculando
enquanto o jogador anda). Viável, mas é uma feature grande - vai
precisar de várias etapas (lista de alvos navegáveis por perto,
obtenção da posição de cada um, integração com o pedido de rota,
tradução rota -> fala, repetição enquanto o jogador caminha). Tratar
como projeto de várias rodadas, não uma correção pontual.

**Som de parede**: pedido menor, ainda não pesquisado (precisa achar
como o jogo detecta colisão com parede, e como tocar um som nosso ou
reaproveitar um som do próprio jogo).
