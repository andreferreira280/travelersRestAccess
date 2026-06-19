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

## Pendente / preciso confirmar no próximo teste

- Esquerda/direita no seletor de cor funcionando.
- Foco voltando pro lugar certo (não mais pro topo) depois de escolher
  uma cor.
- Nome da cor não aparecendo mais sempre como "branco".
- Digitação real nos campos de nome (jogador/taverna) - implementado na
  rodada anterior, ainda sem confirmação de teste.

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
