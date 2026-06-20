# Módulo: Início de Novo Jogo (Character Creator + primeiras telas)

> Ver convenção em `docs/modules/main-menu-and-options.md` (cabeçalho).

## Status atual

- TESTADA pela primeira vez (live log lido). A navegação genérica
  (`KeyboardUINavigator`) já funcionou de cara - achou 33 itens sem
  nenhum código específico pra essa tela, porque `CharacterCreatorUI` é
  um `UIWindow` normal (diferente do sistema de diálogo).
- Problema encontrado e corrigido: cada parte do corpo (Olhos, Nariz,
  Boca, Barba, Cabelo, Camisa, Calça, Sapatos) aparecia como DOIS itens
  separados sem nome útil ("Previous Button" / "Next Button"), porque o
  padrão de Anterior/Próximo aqui é diferente do de Opções (lá os dois
  botões ficam dentro de um objeto "MultiSelection"; aqui eles são
  irmãos diretos, sem esse wrapper). Generalizei a detecção pra cobrir
  os dois padrões - agora cada parte do corpo é UM item só, lendo o
  texto real da linha (ex: "Olhos 1 (1 de 25)"), igual ao que já tínhamos
  pra Resolução/Qualidade em Opções. NÃO RETESTADO ainda depois do fix.
- Também corrigi um bug que fazia essa tela ser lida DUAS VEZES (uma vez
  item por item pelo `KeyboardUINavigator`, outra vez tudo de uma vez
  como um bloco gigante pelo `DialogueAnnouncer` - eles estavam brigando
  pelo mesmo texto). Ver `docs/modules/dialogue-system.md`.

## Itens encontrados ao vivo (nomes reais, log)

- Botões Anterior/Próximo por parte do corpo (Olhos, Nariz, Boca, Barba,
  Cabelo, Camisa, Calça, Sapatos) - cada um colapsado em 1 item agora.
- Botões de cor: `ColorButton`/`ColorButton1`/`ColorButton2`/`ColorButton3`
  por parte - ainda sem nome amigável (ex: não sabemos se dá pra saber
  qual cor está selecionada). Pendente.
- `Male`/`Female` - botões de gênero, nomes já legíveis.
- `Button` (Random), `ButtonLeft` (provavelmente girar o preview do
  personagem) - nomes genéricos, pendente melhorar.
- Dois `InputField (TMP)` (nome do jogador, nome da taverna) - focam e
  são anunciados ("Nome...", "Nome da Taverna..." quando vazios/com
  placeholder), mas digitação de texto de verdade ainda não foi testada.
- `AcceptButton` - nome já legível ("Aceitar").

## v2 - rodada de correções (não testadas ainda)

Depois de um teste real, o usuário relatou vários problemas específicos.
O que foi corrigido:

- **Campos de texto (nome do jogador, nome da taverna)**: Enter agora
  chama `ActivateInputField()` de verdade + foco real do Unity
  (`EventSystem.SetSelectedGameObject`), e entra num "modo de edição"
  onde nosso código para de interceptar as setas/Enter, deixando o
  campo de texto nativo cuidar da digitação. Enter ou Escape confirma e
  sai do modo de edição, anunciando o texto digitado.
- **Gênero (Male/Female)**: agora anuncia "selected"/"not selected" lendo
  o estado real via reflexão nos campos privados `maleFocused`/
  `femaleFocused` (achei no código - eles ficam brancos quando
  selecionados, cinza quando não).
- **"Button" (Random) e "ButtonLeft" não pareciam fazer nada**: na
  verdade provavelmente funcionavam, só não davam nenhum retorno
  sonoro. Adicionei anúncio ("Personagem aleatório" / "Girando
  visualização") depois de ativar.
- **Nome de cada cor**: o jogo não guarda nome nenhum pras cores - só
  RGB. Implementei uma aproximação: pega a cor real do botão e acha o
  nome mais próximo numa paleta fixa (vermelho, azul, verde, etc.) -
  não é exato, é uma estimativa.
- **Foco voltando pro topo (Olhos) depois de escolher uma cor**: hipótese
  forte (não 100% confirmada): abrir/fechar o seletor de cor (que é uma
  janela separada, `ColorPickerUI`) pode deixar `MainUI.IsAnyUIOpen`
  reportando "fechado" por UM frame durante a troca de janelas, o que
  fazia nosso código resetar a lista toda e voltar pro primeiro item.
  Adicionei uma pequena espera (0,15s) antes de considerar que tudo foi
  fechado de verdade, pra absorver esse intervalo de um frame.

## v3 - rodada de 2026-06-18 (fim de tarde) - respostas do usuário + novas correções

Respostas do usuário às perguntas da v2:

- Seletor de cor: ele quer Enter para abrir, **esquerda/direita** para
  navegar entre as cores (não cima/baixo - decisão explícita, diferente
  do padrão do resto do mod, porque as cores ficam em fileira
  horizontal), Enter para escolher. IMPLEMENTADO: quando a janela aberta
  é `ColorPickerUI`, o `KeyboardUINavigator` troca as teclas de
  movimento pra Esquerda/Direita só nessa tela específica - todas as
  outras continuam cima/baixo.
- Foco voltando pro topo (Olhos) depois de escolher cor: a hipótese do
  atraso de 0,15s NÃO resolveu (confirmado pelo usuário). Causa real
  encontrada: ao entrar no `ColorPickerUI` (uma janela diferente), o
  cursor daquela lista (Character Creator) se perdia porque o item
  focado dentro do seletor de cor não existe na lista de volta.
  IMPLEMENTADO: agora o mod GUARDA, pra cada janela, qual item estava
  focado antes de trocar de janela (`_rememberedAnchors`), e restaura
  esse item específico ao voltar - por exemplo, ao fechar o seletor de
  cor, volta pro "ColorButton1" de Olhos (onde você apertou Enter), não
  pro primeiro item da lista. NÃO TESTADO ainda.
- "Todas as cores estão brancas" (bug no nome de cor que implementei):
  causa encontrada no código - o jogo usa um sistema de troca de
  MATERIAL pra essas cores (`useCharacterMaterial = true`), então a
  imagem do próprio botão fica sempre branca (neutra) e a cor real vem
  de outro lugar (`CharacterMaterial.sampleColor`, um campo que só
  existe internamente, não acessível sem reflexão). Corrigido pra ler
  esse valor real em vez da imagem do botão. NÃO TESTADO ainda - pode
  ainda não estar perfeito (é uma aproximação pro nome mais próximo
  numa lista pequena de cores conhecidas).

## v4 - rodada de 2026-06-19 - resultados do teste anterior + novas correções

CONFIRMADO funcionando:
- Esquerda/direita no seletor de cor: funciona.
- Enter pra escolher a cor: funciona.
- Foco voltando pro lugar certo depois de escolher uma cor (a segunda
  tentativa, "lembrar âncora por janela", funcionou - confirmado pelo
  usuário: "o comportamento está ok, ele volta para o elemento da cor").
- Masculino/Feminino e o botão de girar: dão retorno de voz, ok.
- Digitação nos campos de nome: funciona, e o nome confirmado É lido
  certo ao apertar Enter (confirmado no log: digitou "bobe" e "bobelandia",
  os dois foram lidos de volta corretamente).

Problemas encontrados e corrigidos nesta rodada:

1. **Nomes de cor repetidos** ("Olhos só tem cinza e marrom", "Barba
   repete rosa e marrom"): causa - minha paleta de 12 cores primárias
   puras (vermelho, azul, verde...) não tem variação suficiente pra
   tons naturais de olho/cabelo/pele, que raramente são cores puras.
   Ampliei pra ~29 tons, incluindo variações claras/escuras e tons
   específicos (castanho, ruivo, loiro, azul acinzentado, etc.) que
   fazem mais sentido pra personalização de personagem. Também adicionei
   um log (modo debug) com o RGB real de cada cor lida, pra eu poder
   ajustar com precisão se ainda houver confusão. NÃO TESTADO ainda.
2. **Campo de nome volta a anunciar "Nome..." mesmo depois de digitado**:
   achei a causa no log - confirmar a digitação funciona e lê o nome
   certo na hora ("bobe" foi lido), mas ao navegar pra longe do campo e
   voltar depois, ele lia de novo o rótulo genérico "Nome..." em vez do
   texto real digitado. Corrigido: agora lê o texto real do campo
   sempre que houver algum digitado, só usa o rótulo genérico quando
   está vazio. NÃO TESTADO ainda.
3. **Botão "Random" sem rótulo ao navegar** (só dizia "Button (10 of 24)"
   e só falava "personagem aleatório" depois de ativado): corrigido pra
   já anunciar "Personagem aleatório" ao navegar até ele, não só depois
   de apertar Enter. Mesma coisa pro botão de girar ("Girar
   visualização"). NÃO TESTADO ainda.
4. **Barra de espaço fechando a tela inteira** (problema sério): a barra
   de espaço estava configurada pra ativar QUALQUER item focado, igual
   o Enter - e ao que parece estava ativando o botão "Aceitar" de forma
   inesperada, fechando a Criação de Personagem como se tivesse aceitado
   tudo. Restringido: agora a barra de espaço só funciona especificamente
   no botão "Aceitar" - todos os outros itens só respondem a Enter.
   NÃO TESTADO ainda - esse é importante de confirmar, já que pode
   afetar outras telas que ainda não testamos com espaço.

## v5 - rodada de 2026-06-19 (continuação) - causas raiz encontradas

Testando a v4, o usuário achou 2 problemas que minhas correções não
resolveram, e eu encontrei as causas reais no código pra essa rodada:

1. **Nome de cor não mudava nunca** ("escolhi azul, mas continua lendo
   cinza escuro" - em qualquer parte do corpo, e só funcionava certo
   depois de usar o botão "Random"). Achei a causa real no código: existe
   um método (`EEGKEGOFHCA` no código original, sem nome legível) que
   pega a cor atual de cada parte do corpo a partir dos dados reais do
   personagem (`humanInfo`) e atualiza o botão de cor da tela principal -
   o jogo chama esse método ao abrir a tela e em alguns outros momentos
   (entre eles, ao usar "Random" - por isso funcionava só nesse caso),
   MAS NÃO chama ele depois de fechar o seletor de cor. Ou seja: o botão
   de cor da tela principal só ficava com o valor antigo porque nada
   avisava ele que mudou. Corrigido: agora, ao fechar o seletor de cor e
   voltar pra Criação de Personagem, eu mesmo forço essa atualização (via
   reflexão, chamando o método privado do jogo direto). NÃO TESTADO
   ainda - se funcionar, o nome da cor também deve ficar mais preciso
   (já que vai ler o valor certo, não mais sempre o mesmo).
2. **Campos de nome**: ajustei o formato pra "Nome: bobe" / "Nome da
   taverna: bobelandia" (rótulo fixo + valor), em vez de só o valor sem
   contexto. NÃO TESTADO ainda.
3. **Barra de espaço AINDA fechando a tela em qualquer lugar** (minha
   correção anterior - restringir nosso próprio código pra só aceitar
   espaço no botão Aceitar - não resolveu nada, confirmado pelo
   usuário). Isso me disse que o problema não estava no NOSSO código de
   navegação - a barra de espaço nunca passava por ali. Hipótese nova:
   o Unity tem um sistema de seleção "de verdade" (diferente do nosso
   cursor virtual, que a gente ignora de propósito) e Espaço é, por
   padrão, uma tecla de "confirmar" nesse sistema, igual o Enter. Se o
   jogo deixa ALGO selecionado de verdade nessa tela (o próprio botão
   Aceitar, por exemplo) e nunca limpa essa seleção (diferente do resto
   do jogo, que limpa a cada frame, conforme já tínhamos confirmado em
   Opções), a barra de espaço aciona esse botão direto pelo sistema do
   Unity, sem nem passar pelo nosso código. Corrigido: agora a gente
   mesmo limpa essa seleção "de verdade" a cada quadro, do mesmo jeito
   que o resto do jogo já faz. NÃO TESTADO ainda - essa é a correção
   mais importante de confirmar, porque se não funcionar preciso
   repensar a causa.

## v6 - rodada de 2026-06-19 (mais uma volta) - a v5 não resolveu 2 dos 3

Testando a v5: o formato "Nome: valor" funcionou, mas os outros dois
problemas continuaram:

1. **Nome de cor AINDA não muda** ("cor do olho ainda continua cinza
   escuro apenas", barba/cabelo "a primeira cor é anunciada sempre"). A
   correção da v5 (forçar a atualização do botão ao fechar o seletor) não
   resolveu. Como já tentei duas vezes sem sucesso, mudei de estratégia:
   em vez de tentar uma terceira correção no escuro, **adicionei logs de
   diagnóstico** (modo debug) em volta da própria tentativa de correção -
   confirmando se o método de atualização do jogo foi encontrado, se foi
   chamado sem erro, e deixando o log já existente do RGB lido. Isso vai
   me dizer com certeza, no próximo teste, ONDE exatamente a cadeia está
   quebrando (método não encontrado? erro ao chamar? ou chama certo mas o
   dado de personagem ainda não tinha atualizado?), em vez de eu ficar
   tentando às cegas. Pista nova e importante que o usuário deu: quando
   usa "Random", a cor anunciada na opção é a cor real - mas ao ABRIR o
   seletor pra trocar, o cursor não entra na cor que estava selecionada
   (entra em outra qualquer) - ou seja, mesmo quando o nome está certo,
   não há um jeito confiável de saber "qual swatch no seletor é a cor
   atual" - isso é uma limitação separada a resolver depois.
2. **Barra de espaço AINDA fechando a tela em qualquer lugar** mesmo
   depois de limpar a seleção "de verdade" do Unity a cada quadro - ou
   seja, essa hipótese também não era a causa raiz (provavelmente um
   problema de ordem/tempo: o Unity já processa a tecla antes da nossa
   limpeza rodar no mesmo quadro). Em vez de continuar caçando a causa
   exata, segui a sugestão direta do usuário: **a barra de espaço foi
   completamente desligada da Criação de Personagem (e de todos os
   menus do `KeyboardUINavigator`)** - só Enter ativa qualquer coisa
   agora. Espaço continua livre para avançar diálogo (isso já era do
   `DialogueAnnouncer`, não muda).

Também ajustado nesta rodada:
- **Feedback de nome confirmado**: ao confirmar a digitação (Enter), agora
  anuncia "Nome definido: bobe" / "Nome da taverna definido: xxxx" (ou
  "Nome vazio" se nada foi digitado), deixando claro que foi uma
  confirmação, não só uma releitura passiva.
- **Aviso de carregamento**: ao entrar na tela de carregamento
  (`LoadingScene`), agora anuncia "Carregando jogo..." antes da dica do
  jogo (que já estava sendo lida, mas sem nenhum contexto).

## v7 - rodada de 2026-06-19 (continuação) - confirmado o que funciona, achada a causa real do espaço, pausado nome de cor

Testando a v6, o usuário confirmou:
- **"Nome definido: valor" funciona** (log confirma: "Nome definido:
  bobe", "Nome da taverna definido: bobelandia").
- **Nome de cor ainda bagunçado** - mesmo com a correção da v5/v6.

**Causa real da barra de espaço, finalmente encontrada no log**: o
usuário sugeriu que talvez a barra de espaço estivesse "vazando" do
sistema de avançar diálogo - investiguei e essa hipótese específica foi
DESCARTADA (não há nenhum log de "Advance dialogue" no momento do bug).
Mas o log mostrou o momento exato do problema: o usuário estava com o
cursor em "Cor 1: castanho" (item 2 de 25, claramente NÃO no botão
Aceitar) e 0,6 segundos depois apareceu "[STATE] UI closed:
CharacterCreatorUI" seguido da PRÓXIMA fala de diálogo da história -
ou seja, a tela realmente fecha/aceita de QUALQUER lugar, confirmando
o relato. Isso prova que não é a barra de espaço "vazando" pro
diálogo - é a tela de Criação de Personagem mesmo aceitando direto.

Achei a causa categoricamente diferente no código decompilado: essa
tela chama repetidamente `UISelectionManager...Select(...)` para
manter uma seleção "de verdade" do Unity em algum botão (provavelmente
o botão Aceitar) - diferente da maioria das outras telas. Minha
correção anterior (limpar essa seleção a cada quadro, dentro do
`OnUpdate`) não bastou porque a ORDEM de execução dentro do mesmo
quadro importa: se o jogo redefine a seleção DEPOIS da nossa limpeza
no mesmo quadro, a barra de espaço ainda encontra algo selecionado na
hora de processar a tecla. Corrigido: agora a limpeza roda DE NOVO no
`OnLateUpdate` (depois de tudo mais no quadro já ter rodado), o que
deve garantir que nada fique selecionado de verdade ao entrar no
próximo quadro. Essa é a TERCEIRA tentativa pra esse bug - mantive
também um log de diagnóstico (o que estava selecionado no momento
exato da barra de espaço) pra confirmar com certeza no próximo teste,
caso ainda não resolva.

**Nome de cor - decisão tomada com o usuário**: investiguei mais a
fundo e confirmei que o valor de cor de cada parte do corpo
(`colorButtons[i]`) realmente NÃO se atualiza quando uma cor nova é
escolhida no seletor - testei forçar a atualização (v5/v6) e o valor
lido continua sendo o mesmo de antes da escolha, mesmo sem erro. Ou
seja, a cor "de verdade" aplicada no personagem não é refletida em
nenhum dado que eu consiga ler de forma confiável sem investigar muito
mais a fundo um código extremamente embaralhado (dezenas de métodos
quase idênticos, provavelmente gerados automaticamente pelo Editor do
Unity, um por cada botão de cor da cena). **Perguntei ao usuário se
queria que eu continuasse investigando ou pausasse - ele escolheu
pausar por agora.** A navegação/escolha de cor em si continua
funcionando normalmente; só o NOME falado da cor pode ficar incorreto.
Isso fica registrado como limitação conhecida, não como algo a corrigir
nas próximas rodadas, a menos que o usuário peça pra retomar.

Também ajustado nesta rodada (já confirmado funcionando):
- **Feedback de nome confirmado**: ao confirmar a digitação (Enter),
  anuncia "Nome definido: bobe" / "Nome da taverna definido: xxxx".
- **Aviso de carregamento**: ao entrar na tela de carregamento
  (`LoadingScene`), anuncia "Carregando jogo..." antes da dica do jogo.

## v8 - rodada de 2026-06-19 (mais uma volta) - causa real do espaço encontrada (Harmony), aviso de carregamento corrigido

Testando a v7: a 3ª tentativa do espaço (limpar seleção também no
`OnLateUpdate`) TAMBÉM não resolveu, e "Carregando jogo..." continuou
não sendo ouvido.

**Aviso de carregamento**: achei a causa - o aviso ATÉ estava sendo
chamado, mas o `DialogueAnnouncer` lia a dica da tela de carregamento
quase no mesmo instante (a tela `MainUI` persiste entre trocas de
cena, então nosso sistema volta a funcionar quase imediatamente), e
toda fala nova INTERROMPE a anterior - "Carregando jogo..." estava
sendo cortado pela dica antes de terminar de falar. Corrigido: agora,
ao entrar na tela de carregamento, seguro por 1,5s o `DialogueAnnouncer`
antes de deixá-lo falar qualquer coisa, dando tempo do aviso de
carregamento ser ouvido por completo primeiro. NÃO TESTADO ainda.

**Barra de espaço - causa real, finalmente encontrada**: usei o
diagnóstico que tinha deixado (o que estava selecionado no Unity no
momento exato do espaço) e descobri algo importante: a seleção JÁ
estava vazia (`null`) no momento do bug - ou seja, minhas 3 tentativas
anteriores (restringir nosso código, limpar seleção no `OnUpdate`,
limpar de novo no `OnLateUpdate`) estavam todas atacando uma causa
ERRADA. A seleção "de verdade" do Unity nunca teve nada a ver com isso.

Fui direto no código decompilado do jogo procurar quem realmente fecha
a tela, e achei: existe uma função do próprio jogo (`PlayerInputs`) que,
a cada quadro, verifica um botão virtual chamado "Pause" - e quando ele
é pressionado (sem usar controle), chama `MainUI.CloseLastWindowOpen`,
que simplesmente fecha a última janela aberta (`CloseUI()`). Pelo jeito,
esse "Pause" virtual do jogo está configurado pra responder tanto a Esc
quanto à barra de espaço - e fechar a Criação de Personagem por esse
caminho genérico não tem uma forma de "cancelar e descartar", então
fechar = aceitar tudo. Isso explica tudo: não tinha nada a ver com
nosso código, nem com seleção do Unity - é um atalho genérico do
próprio jogo que também responde à barra de espaço.

**Correção** (a primeira vez que precisei usar Harmony pra "interceptar"
uma função do jogo, não só observar): agora intercepto especificamente
a chamada `MainUI.CloseLastWindowOpen` - se a tecla que disparou foi a
barra de espaço E a tela aberta é a Criação de Personagem, a chamada é
BLOQUEADA (a tela não fecha). Esc continua funcionando normalmente pra
sair da tela (não toquei nesse caminho), e nenhuma outra tela é afetada
- só a Criação de Personagem, só a barra de espaço. NÃO TESTADO ainda -
essa é a 4ª tentativa, mas agora baseada em prova direta do código, não
em suposição.

## v9 - rodada de 2026-06-19 (mais uma volta) - achei a causa REAL desta vez (lendo o código certo)

Testando a v8: a 4ª tentativa (Harmony em `MainUI.CloseLastWindowOpen`)
TAMBÉM não resolveu, e "Carregando jogo..." continuou sem ser ouvido.
O usuário pediu por uma investigação mais minuciosa e por ferramentas
melhores de debug, em vez de mais tentativas no escuro.

**Por que as 4 tentativas anteriores falharam**: eu estava sempre
olhando o caminho "genérico" de input do jogo (`MainUI`,
`PlayerInputs`'s dispatcher central). O log confirmou que meu patch no
`MainUI.CloseLastWindowOpen` nunca disparou - ou seja, eu estava
interceptando a função ERRADA o tempo todo.

**Causa real, finalmente confirmada**: fui ler diretamente o `Update()`
da própria `CharacterCreatorUI` no código decompilado (em vez de
assumir que o input passa por um lugar central) e achei a linha exata:

  else if (IsOpen() && ... && GetButtonDown("ClosePopUp")
      && (campo de nome não está em foco) && (campo da taverna não
      está em foco))
  {
      AcceptButton();
  }

Ou seja: a PRÓPRIA TELA chama `AcceptButton()` direto, todo quadro,
sempre que a ação "ClosePopUp" do Rewired (sistema de input do jogo)
dispara - sem passar por seleção de UI, sem passar por nenhum código
nosso. E essa ação parece estar configurada pra responder tanto a Esc
quanto à barra de espaço.

**Lição pra próximas investigações**: esse jogo trata input "por
tela", não só por um despachante central - sempre que uma tecla faz
algo que a gente não controla, olhar primeiro o `Update()` da tela
ESPECÍFICA em questão, não só os sistemas genéricos de input.

**Correção** (5ª tentativa, agora certa): intercepto
`CharacterCreatorUI.AcceptButton()` direto - se foi a barra de espaço
que disparou, bloqueio a chamada; Enter continua funcionando
normalmente (é assim que nosso código e o botão real ativam o Aceitar
de verdade). NÃO TESTADO ainda.

**Ferramentas de debug novas**, atendendo o pedido do usuário: agora
todo quadro (em modo debug) registro QUALQUER tecla que for pressionada,
mesmo que nenhum dos nossos sistemas reaja a ela (`[INPUT-RAW] KeyDown:
...`) - isso teria mostrado o problema de cara, sem precisar de 4
rodadas. Também documentei a lição acima em `docs/game-api.md` (seção
"Known Issues") pra próximas investigações serem mais rápidas.

**"Carregando jogo..." ainda sem ser ouvido**: percebi um problema na
minha correção anterior - eu estava segurando o `DialogueAnnouncer` por
"1,5 segundos de tempo real", mas durante uma trava de carregamento o
tempo do jogo pode "saltar" várias segundos de uma vez quando ele volta
a responder - ou seja, minha janela de 1,5s podia já estar "vencida"
no exato instante em que tudo volta a funcionar, sem proteger nada de
verdade. Corrigido: agora seguro por 90 QUADROS (não segundos) - isso
não é afetado por travas de carregamento, porque só conta quadros que
realmente rodaram. NÃO TESTADO ainda.

## v10 - rodada de 2026-06-19 (mais uma volta) - BARRA DE ESPAÇO RESOLVIDA

Testando a v9, o usuário confirmou: **"barra de espaços agora foi
corrigida totalmente, travada no menu muito bem"** - a 5ª tentativa
(bloquear `CharacterCreatorUI.AcceptButton()` direto quando disparado
pela barra de espaço) funcionou. Esse bug está RESOLVIDO. Esc não foi
testado explicitamente, mas como não tocamos nesse caminho, não deveria
ter mudado.

Carregamento ainda não foi ouvido - investigado e corrigido em
`docs/modules/dialogue-system.md` (v5): a tela de carregamento ao
iniciar Novo Jogo não é uma troca de cena de verdade, é um painel
dentro da própria cena do menu - o aviso "Carregando jogo..." agora
detecta esse painel diretamente, em vez de depender de troca de cena.
Também corrigido nessa mesma rodada um ruído de HUD (dinheiro, status
da taverna, nível da mina, versão do jogo todos lidos juntos numa frase
sem sentido) - ver detalhes em `dialogue-system.md`.

Também adicionado, a pedido do usuário, mais visibilidade de eventos
que podem ser perdidos durante testes: um log (modo debug) que avisa
sempre que um popup de tutorial do jogo aparece (`NewTutorialManager`),
já que a próxima rodada de testes deve avançar para depois da Criação
de Personagem, onde tutoriais são esperados.

## v11 - rodada de 2026-06-19 (mais uma volta) - "Carregando" confirmado, regressão grave encontrada e corrigida

Testando a v10: **"carregando jogo foi anunciado"** - confirmado
funcionando.

**Regressão grave encontrada**: o usuário reportou que não conseguia
mais editar o nome do personagem nem da taverna. Causa: a correção da
3ª tentativa do bug do espaço (`OnLateUpdate` limpando a seleção real
do Unity TODO quadro, sem checar se estávamos editando texto) continuou
no código mesmo depois da causa real ter sido encontrada (5ª tentativa,
via Harmony) - e como a digitação de texto DEPENDE de manter o foco
real do Unity no campo (`TMP_InputField.isFocused`), essa limpeza
estava desfazendo o foco a cada quadro, quebrando a digitação por
completo. Como a correção definitiva do espaço (bloquear
`AcceptButton()` via Harmony) não depende dessa limpeza de seleção,
removi esse mecanismo inteiro (em `Main.OnLateUpdate` e também a
chamada equivalente que sobrava em `KeyboardUINavigator.Update`) - era
código de uma tentativa anterior já comprovada sem efeito no bug real,
e agora ativamente prejudicial. NÃO TESTADO ainda - mas a causa está
clara e a remoção é direta.

**Validado o pedido sobre dificuldade**: a dica de carregamento
menciona mudar o "nível de dificuldade" em Opções - confirmei no código
que é só um toggle on/off (não níveis de verdade), específico pra
facilitar eventos/desafios especiais - ver detalhes em
`main-menu-and-options.md`. Já tratado normalmente pelo nosso código
genérico de toggle, nada a corrigir aqui.

## Pendente / preciso confirmar no próximo teste

- Digitação do nome do personagem e da taverna funcionando de novo
  (prioridade - é uma regressão que quebrou algo que já funcionava).
- Ruído de HUD (dinheiro/status/versão) não aparecendo mais junto com
  a dica de carregamento.
- (Quando chegar lá) qualquer popup de tutorial sendo capturado no log
  ("Tutorial popup shown: ...") e, se possível, sendo ouvido também
  (provavelmente já é lido pelo scanner geral de diálogo, mas ainda não
  confirmado).

## Limitação conhecida (pausada, não é bug a corrigir)

- Nome de cor: pode ficar incorreto (lê sempre o valor inicial daquela
  parte do corpo, não o que foi escolhido) - decisão do usuário foi
  pausar essa investigação por enquanto. Retomar só se ele pedir.

## Esta tela é mais simples do que eu temia

A introdução narrativa (diálogos do encapuzado etc.) é uma etapa
SEPARADA e ANTERIOR a esta tela - não faz parte do Character Creator,
é o sistema de diálogo (`docs/modules/dialogue-system.md`). Isso
simplifica bastante: esta tela é "só" uma tela de Opções com mais itens,
sem nenhuma narrativa entrelaçada.

## Como se chega aqui

- Fluxo: Menu Principal -> "Jogar" -> "Novo" (`SaveUI`) -> o jogo chama
  `CharacterCreatorUI.Get(playerNum).SetCharacter(playerNum)`.
- `CharacterCreatorUI : UIWindow` - mesma base de janela que já conhecemos
  (`OnAnyUIOpen`/`OnAnyUIClose` devem disparar normalmente).

## Estrutura encontrada no código (campos principais)

- `TMP_InputField nameInput` - nome do personagem. **Já vem preenchido por
  padrão** com o nome da conta Steam/Galaxy (ou um nome salvo em
  `PlayerInfo`) - o jogador não É OBRIGADO a digitar, só pode aceitar o
  padrão.
- `TMP_InputField tavernInput` - nome da taverna. Também vem preenchido por
  padrão (ex: "{nome}'s Tavern"). Fica desabilitado (`interactable = false`)
  em pelo menos um fluxo (precisa confirmar quando).
- Seleção de gênero: `SetMaleGender()` / `SetFemaleGender()` (chamam
  `characterCreator.SetMaleGender()`/`SetFemaleGender()`), com imagens de
  destaque (`maleFocused`/`femaleFocused`) e grupos de objetos que ativam/
  desativam (`maleGameObjectsToActivate`/`femaleGameObjectsToActivate`).
  Provavelmente 2 botões simples (Masculino/Feminino).
- Partes do corpo/aparência, todas com um padrão "Cycle" (igual ao
  anterior/próximo que já resolvemos em Opções - Resolução/Qualidade):
  `CycleNose`, `CycleShoes`, `CycleBeard`, `CycleTorso`, `CycleMouth`,
  `CycleEyes`, `CycleHair` (todas recebem um `int` - direção, +1/-1).
  Existe também `CharacterCreatorBodyPartText[] textsBodyParts` - cada um
  com um `TextMeshProUGUI textMesh` próprio, o que sugere que CADA parte do
  corpo já tem um texto legível dedicado (bom sinal para acessibilidade,
  diferente de Resolução/Qualidade que não tinham texto na própria linha).
- Cores (`ColorButton[] colorButtons`): cada `ColorButton` representa uma
  amostra de cor clicável (`OnColorChanged`, `OnColorIndexChanged`,
  `OnMaterialChanged` como eventos). Tem uma flag `openPicker` - sugere que
  clicar abre um seletor de cor (popup), não troca direto - precisa
  investigar a popup separadamente quando chegarmos nela.
- Botões de ação: `AcceptButton()` (confirma e segue - se for o primeiro
  personagem, inicia o jogo via `StartCoroutine` de fade; se for edição
  posterior, só salva e fecha), `RandomCharacterButton()` (sorteia
  aparência), `cancelButton` (visibilidade depende de estar ou não no
  tutorial obrigatório - `firstStart`).

## Por que isso é mais complexo que Opções

1. **Campos de texto editáveis** (`nameInput`/`tavernInput`) - é um tipo de
   controle totalmente novo pra nós. O código confirma que o jogo verifica
   `nameInput.isFocused`/`tavernInput.isFocused` antes de fechar a tela com
   Esc (`ClosePopUp`), ou seja, o foco real da Unity (`EventSystem`) é
   usado de verdade aqui - diferente da navegação por botões, onde
   ignoramos o EventSystem por completo. Precisamos descobrir, ao testar,
   se `TMP_InputField.ActivateInputField()` funciona para realmente focar o
   campo e permitir digitação normal (incluindo leitura pelo NVDA), ou se o
   mesmo problema de "foco limpo todo frame" que tivemos antes também
   afeta isso.
2. **Seletor de cor com popup** - não é só um botão simples, abre algo por
   cima (estado aninhado, parecido com o "modo de ajuste" que já temos,
   mas precisa confirmar a estrutura real da popup antes de decidir como
   navegar nela.
3. Telas "tipo diálogo": o usuário mencionou que a tela mostra textos
   estilo diálogo e, conforme avançam, opções vão aparecendo. Isso pode
   ser uma introdução narrativa SEPARADA (antes do Character Creator,
   talvez via `NewTutorialManager`/sistema de diálogo) e não parte do
   `CharacterCreatorUI` em si, OU pode ser uma revelação progressiva de
   campos dentro da própria tela. Não confirmado ainda - aguardando
   descrição do usuário.

## Próximos passos

1. Usuário descreve a tela ao vivo (ou testa com F12 + "testei") - ver
   `novo_pedido.txt`.
2. Confirmar: é uma tela só, ou uma sequência de telas/diálogos?
3. Decidir abordagem para `TMP_InputField` (foco real vs. nosso cursor
   virtual) - provavelmente vai exigir um modo de interação diferente do
   "modo de ajuste" que já temos.
4. Mapear a popup de seleção de cor.
5. Só então implementar navegação.

## Perguntas abertas

- A tela de diálogo/narração é parte do Character Creator ou uma etapa
  separada antes dele?
- `tavernInput` fica desabilitado em qual situação exatamente (edição de
  personagem existente, talvez)?
- Existe seleção de dificuldade nessa sequência, ou isso já foi resolvido
  em Opções (`easyDifficultyUIToggle`)?
