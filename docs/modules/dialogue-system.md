# Módulo: Diálogos / Narrativa (caixas de texto estilo "visual novel")

> Ver convenção em `docs/modules/main-menu-and-options.md` (cabeçalho).

## Status atual

- Primeira tentativa (v1): tratar qualquer `UIWindow` sem botão como
  "diálogo". TESTADA - falhou totalmente (nada foi lido, nem o diálogo
  nem o botão de avançar). O log mostrou que durante toda a sequência de
  diálogo/criação de personagem, NENHUM `UIWindow` chegou a abrir
  (nenhuma linha "UI opened" no log nesse trecho) - ou seja, essa tela
  não passa pelo sistema de janelas (`MainUI`) que usamos pra tudo até
  agora.
- v2 (atual, ainda não testada): abandonei a dependência de `UIWindow`.
  Agora o `DialogueAnnouncer` varre a TELA TODA procurando por texto
  (`TextMeshProUGUI`) que muda, sem depender de estar dentro de uma
  janela conhecida. Também loga (só em modo debug) qualquer botão clicável
  que encontrar quando nenhum menu normal está ativo, pra eu conseguir
  achar o botão de "avançar diálogo" no próximo teste.
- Ainda NÃO implementei como avançar o diálogo (apertar o botão "next") -
  preciso primeiro do log com o nome/caminho desse botão pra mirar certo,
  em vez de arriscar interferir com outras telas.

## v2 testada - funcionou (com ruído)

O log confirmou que a varredura global PEGA o diálogo de verdade -
narração do encapuzado ("As lendas falam de um reino de muitos anos
atrás...", etc.) e até falas ambiente de NPCs (família do jogador:
Holly, Buzz, Arthur, Violet) foram lidas. Só que também leu bastante
ruído de HUD: relógio do jogo ("06:05", atualiza toda hora), contador de
dia ("Seg. 1"), e dica de ação ("[E] Arrumar a Cama"). Adicionei filtro
por padrão de texto pra esses 3 casos específicos (regex simples - hora,
dia da semana abreviado + número, e "[X] ..."). Ainda NÃO testado depois
desse filtro.

Também descobri que meu log de "botão solto" (pra achar o botão de
avançar diálogo) não achou nada durante a narração - ou seja, o controle
de avançar provavelmente NÃO é um `Button` de Unity normal, é algum
outro tipo de clique customizado (o jogo tem várias classes próprias
tipo `ColorButton`/`ToggleButton`, então não seria surpresa). Troquei a
busca pra qualquer coisa que implemente `IPointerClickHandler` (interface
de clique do Unity, mais ampla que `Button`), pra ver se acha esse
controle no próximo teste.

**Resolvido:** confirmado pelo usuário - tudo isso aconteceu ANTES da
tela de Criação de Personagem. Ou seja, a introdução não é uma "cena
separada" - ela acontece dentro da própria cena `Gameplay` (o mundo já
carregado), com o jogador sentado na taverna enquanto a narração rola.

## v3 - estrutura real confirmada (caminhos de hierarquia)

Com o log anotando o caminho de cada texto, ficou claro que existem TRÊS
sistemas de texto diferentes rodando ao mesmo tempo:

1. **Conversa em primeiro plano** (jogador + outro personagem, ex: o
   amigo convencendo a comprar a taverna, depois Torik na carroça):
   path contém `Dialogue UI Intro Variant/.../Subtitle Panel/Subtitle Text`
   (ou `Dialogue UI/...` mais adiante, com Torik). Tem nome do personagem
   separado (`.../Portrait Name Frame/Portrait Name`, ex: "Torik"). Avança
   só com um botão clicável chamado **"Continue Button"** (`UIButtonExtended`,
   que na verdade É um `Button` de Unity de verdade - só não aparecia no
   meu log antigo porque o filtro de "nenhum menu aberto" não estava
   passando no momento certo).
2. **Narração/lenda em segundo plano** (a história do Rei Cedric/Rygar):
   path contém `Intro Canvas/Intro/Story Parent/Text Panel/Subititles`.
   Avança SOZINHA (no tempo certo), e o próprio jogo já tem uma dica de
   "Mantenha pressionado ESC para pular" (`.../Story Parent/Hold To Skip`)
   - ou seja, essa parte não precisa de botão nosso, já é navegável.
3. **Falas ambiente de NPCs** (familia da taverna - Holly, Buzz, Arthur,
   Violet, e outros NPCs andando por aí): path contém
   `.../Bubble Template Standard Bark UI/.../Text (TMP)`. Dá pra
   identificar e diferenciar isso da narração principal pelo caminho.

## v3 - o que foi implementado

- **Avançar diálogo pelo teclado**: Enter, Espaço ou Enter do numérico
  agora clicam automaticamente no "Continue Button" quando ele estiver
  visível e nenhum menu normal estiver aberto. NÃO TESTADO AINDA.
- **Reler a última fala**: seta para cima ou para baixo repete a última
  fala anunciada (útil se perder o início). NÃO TESTADO AINDA.
- **Corrigido bug de anúncio duplicado**: o leitor de diálogos estava
  rodando mesmo com um menu normal aberto, causando leituras duplicadas/
  bagunçadas - por exemplo lia "Carregar. Novo" extra na tela de saves, e
  leu a tela INTEIRA de Criação de Personagem como um bloco só ("Olhos 1.
  Nariz 1. Boca 1...") em cima do que o `KeyboardUINavigator` já lê item
  por item. Agora só varre a tela quando NENHUM menu normal está aberto.

## v4 - testada e confirmada funcionando (rodada de 2026-06-18, fim de tarde)

Tudo abaixo foi CONFIRMADO pelo usuário em teste real:

- Avançar diálogo com Espaço: funciona. Enter foi removido do avanço
  porque o próprio jogo já usa Enter pra outra coisa (confirmado em
  `docs/game-api.md` - "Avoid: ... Enter/Return ... confirmed used by
  the game"), o que fazia o avanço por Enter não funcionar direito.
- Reler com seta cima/baixo: funciona.
- Prefixo "Conversa ao redor: " nas falas ambiente: funciona.

**Bug encontrado e corrigido nesta mesma rodada:** quando uma fala
ambiente (Arthur/pai/mãe) chegava DEPOIS da fala principal da história,
ela sobrescrevia a última fala lembrada - então cima E baixo só
repetiam a fala ambiente, perdendo a fala da história. Corrigido
separando em duas variáveis (`_lastStoryMessage`/`_lastAmbientMessage`):
agora **seta para cima relê a última fala da história/conversa
principal, seta para baixo relê a última fala ambiente** - essa
separação foi uma escolha explícita do usuário (ele ofereceu duas
alternativas e essa foi a mais simples).

## Pendente / não resolvido ainda

- Campos de nome (`TMP_InputField`) - implementação de edição real
  feita nesta rodada (ver `new-game-setup.md`), mas digitação de verdade
  ainda não foi testada pelo usuário.
- Vídeo da introdução (pergaminho com legendas) - ainda não confirmamos
  se as legendas aparecem no nosso scanner (provavelmente sim, já que
  tudo mais apareceu, mas não foi mencionado especificamente num teste
  com F12).

## Contexto - a introdução do jogo

Ao iniciar um jogo novo, antes da tela de criação de personagem, existe
uma sequência longa de diálogo narrativo (~24 telas, descrita pelo usuário
em `novo_pedido.txt` de uma rodada anterior - vale reler se for prosseguir
nessa investigação): um homem encapuzado conta a história do Rei Cedric e
da "Sociedade dos Taverneiros" numa taverna, inclui um trecho tipo vídeo
(pergaminho com ilustração + legendas), termina com o jogador comprando uma
taverna por 1000 moedas de ouro, desmaiando, e despertando numa carroça
puxada por um NPC chamado "TORIK" que o leva até a taverna - aí sim entra a
tela de criação de personagem (`CharacterCreatorUI`).

As caixas de diálogo têm: nome do personagem que fala (ex: "TORIK") acima
do texto, retrato do personagem ao lado, texto da fala, e um indicador
(triângulo) para avançar.

## Por que a abordagem é genérica (não uma classe específica)

Procuramos no código decompilado por uma classe óbvia de diálogo
(`StandardDialogueUI`, `DialogueManager`, etc. - padrões comuns do
PixelCrushers Dialogue System, que o jogo referencia - ex:
`DialogueNPCBase.dialogueUiInUse` é do tipo `StandardDialogueUI`) mas essa
classe NÃO aparece decompilada em `decompiled/` (provavelmente o ilspycmd
não conseguiu gerar esse arquivo específico). Sem saber o nome exato do
componente de texto, não dava pra mirar uma classe certa como fizemos com
`VolumeSliderUI`/`ToggleButton` em Opções.

**Solução adotada:** em vez de mirar uma classe, miramos um COMPORTAMENTO:
qualquer `UIWindow` aberta que NÃO tenha nenhum elemento clicável
(`Selectable.interactable`) é tratada como "janela de texto passiva" (uma
caixa de diálogo, não um menu de navegação) - o `KeyboardUINavigator`
ignora essas janelas (porque `_items.Count == 0`), e o `DialogueAnnouncer`
as observa, lendo qualquer texto (`TextMeshProUGUI`) que apareça ou mude
dentro dela. Isso evita conflito com telas que TÊM botões (Opções,
Criação de Personagem, etc.) - essas continuam só com o `KeyboardUINavigator`.

Para não atropelar um efeito de "máquina de escrever" (texto aparecendo
letra por letra), só anunciamos um texto depois que ele ficar parado por
0,4s sem mudar (mesma técnica de estabilização usada em
`KeyboardUINavigator`).

## Riscos / coisas que podem não funcionar de primeira

- Se a caixa de diálogo NÃO for implementada como `UIWindow` (pode ser um
  Canvas sempre ativo que só liga/desliga visibilidade de um jeito
  diferente), `MainUI.GetCurrentOpenWindows` pode nunca detectá-la, e
  nada seria lido. Precisa confirmar testando.
- Se o "vídeo" (pergaminho com legendas) for um `VideoPlayer` da Unity com
  legenda DESENHADA NO PRÓPRIO VÍDEO (vídeo gravado com texto embutido),
  não tem texto pra ler via código - precisaríamos achar a legenda como
  dado separado (string/legenda) se existir, ou aceitar que esse trecho
  específico fica sem áudio descritivo por enquanto.
- Pode haver textos curtos demais (nome do personagem, "...") sendo lidos
  em momentos estranhos, ou textos de UI que não são diálogo de verdade
  sendo pegos por engano, já que o critério ("sem nenhum Selectable") é
  uma aproximação.

## Pendências - pedido do usuário sobre descrição de ambiente/personagens

O usuário pediu duas coisas relacionadas, que são bem diferentes em
dificuldade:

1. **Anunciar quando um NOVO personagem entra na conversa, ou quando o
   AMBIENTE/cena muda** (ex: "encapuzado apareceu", "cena mudou para a
   vila"). Isso é viável de forma limitada: dá pra detectar quando o nome
   do personagem que fala muda (texto da "placa de nome"), e talvez
   detectar troca de cena via algum identificador (nome da imagem de
   fundo, nome da cena/Scene do Unity). Ainda não implementado - precisa
   de dados reais de log pra saber como identificar isso de forma
   confiável.
2. **Descrição rica do ambiente** (tipo "uma taverna em estilo pixel art,
   iluminada por velas...") **igual o usuário escreveu manualmente
   olhando a tela.** Isso o jogo NÃO tem como dado/texto - é uma descrição
   visual que só existe porque o usuário (ou alguma ferramenta de visão)
   olhou a imagem e descreveu. O mod não tem acesso a isso automaticamente.
   Duas formas de resolver, bem diferentes em esforço:
   - **Escrever à mão** essas descrições para os momentos fixos da
     introdução (conteúdo não muda, é sempre a mesma história) - viável,
     mas trabalho manual nosso, cena por cena.
   - **Usar uma IA de visão em tempo real** (capturar a tela e descrever
     automaticamente) - tecnicamente possível mas é uma feature bem maior
     (precisa de internet, uma chave de API, captura de tela dentro do
     mod, e custo por chamada) - decisão de escopo separada, ainda não
     decidida com o usuário.

## Próximos passos

1. Usuário testa a introdução do jogo com F12 ativado e diz "testei".
2. Ler o log: confirmar se a caixa de diálogo foi detectada como janela
   passiva, se o texto certo foi lido, e timing (não cortado, não
   atrasado demais).
3. Decidir com o usuário qual abordagem usar pra descrição de ambiente
   (manual vs IA de visão), e se/como anunciar troca de personagem/cena.
