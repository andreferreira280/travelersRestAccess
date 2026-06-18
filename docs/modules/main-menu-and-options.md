# Módulo: Menu Principal + Opções

> Notas de continuidade para este módulo. Atualizar sempre que algo mudar de
> forma relevante (nova descoberta de estrutura, decisão de design, bug
> corrigido). Objetivo: qualquer sessão nova (mesmo sem memória da conversa
> anterior) deve conseguir continuar o trabalho lendo só este arquivo.

## Status atual

- Menu Principal (`TitleScreen`): navegação por setas, Enter e Esc
  funcionando e validada pelo usuário.
- "Jogar" (`SaveUI`): navegação funcionando (Novo/Voltar). Sem save
  existente para validar o slot "Carregar" ainda.
- Opções (`OptionsMenuUI`) com as 4 abas (Gráficos, Som, Atribuir Teclas,
  Outros): navegação funcionando, isolada por aba (ver "Decisões" abaixo).
  Em iteração ativa - ver `novo_pedido.txt` para o estado exato do último
  teste.

## Arquivos envolvidos

- `KeyboardUINavigator.cs` - motor de navegação (cursor virtual, detecção de
  controles, modo de ajuste).
- `MenuAnnouncer.cs` - anúncio de abertura/fechamento de janela (`UIWindow`).
- `UITextExtractor.cs` - extração de texto legível de um `GameObject` ou
  `TextMeshProUGUI` direto.
- `Main.cs` - liga tudo, hotkeys globais (F1, F12).

## Arquitetura / como o jogo realmente funciona (descoberto via decompiled/)

### Seleção nativa da Unity não funciona para teclado

O módulo de input do jogo (Rewired) limpa `EventSystem.currentSelectedGameObject`
todo frame quando não há gamepad ativo. Por isso não dá pra usar o sistema de
seleção nativo da Unity - o mod mantém seu próprio "cursor virtual"
(`List<Selectable>` + índice atual) recalculado a cada frame.

### Janelas (`UIWindow`)

- `MainUI.GetCurrentOpenWindows(1)` retorna a pilha de janelas abertas do
  jogador 1; a última (`.Last.Value`) é a janela "do topo" (a que está em
  foco).
- `UIWindow.OnAnyUIOpen` / `OnAnyUIClose` são delegates estáticos (não
  `event` do C#), então dá pra usar `+=` direto sem Harmony.

### `OptionsMenuUI` e suas abas

- Cada aba (Gráficos/Som/Atribuir Teclas/Outros) é uma `UIWindow` SEPARADA,
  guardada em `OptionsMenuUI.panelsUI` (`UIWindow[]`). Os tipos reais são:
  `GraphicsMenuUI`, `SoundMenuUI`, `KeybindUI`, `OthersMenuUI`.
- O conteúdo de cada aba (`panelsUI[i].content`) NÃO fica dentro do
  `.content` da janela externa - é uma raiz separada.
- **IMPORTANTE - já tentamos 2 abordagens que falharam para saber "qual aba
  está ativa agora":**
  1. Confiar em `Selectable.interactable` por elemento - falhou, elementos
     de abas inativas continuavam `interactable=true` em alguns casos
     (confirmado por log: elementos de "Atribuir Teclas" apareciam
     misturados com "Outros").
  2. Confiar em `panel.content.activeSelf` - também não confiável (o código
     decompilado do jogo tem várias variantes obfuscadas de
     `FocusMainPanel` com lógica de `SetActive` inconsistente entre si).
  - **Solução atual (funcionando):** o próprio mod rastreia qual botão de
    aba foi clicado por último (`_selectedOptionsPanelType` em
    `KeyboardUINavigator`), via um dicionário nome-do-botão -> tipo de
    painel (`OptionsTabPanelTypes`). Ao abrir Opções, sempre assume
    Gráficos (aba padrão do próprio jogo). Só essa aba é escaneada.
  - Nomes reais dos botões de aba (confirmados em log):
    `GraphicsMenu`, `SoundMenu`, `Keybind`, `OthersMenu`.

### Controles de volume (`VolumeSliderUI`)

- NÃO é um `Slider` da Unity. É uma classe própria do jogo
  (`VolumeSliderUI`, herda `MonoBehaviour`) com métodos públicos
  `IncrementLevel()` / `DecrementLevel()` e um nível interno 0-10 (cada
  passo = 10%).
- Em Música/Efeitos Sonoros (`SoundMenuUI.musicSlider` /
  `SoundMenuUI.sfxSlider`, ambos públicos): a estrutura real é
  `MusicVolume` (botão/rótulo da linha) > filho `Slider` (tem o componente
  `VolumeSliderUI`) > filho `DecreaseButton`. Ou seja, o componente fica
  "abaixo" (nos filhos) do botão principal da linha, não "acima" (nos
  pais) - por isso a busca precisa checar os dois sentidos:
  `GetComponentInParent<VolumeSliderUI>() ?? GetComponentInChildren<VolumeSliderUI>()`.
- **Cuidado:** esse mesmo componente aparece "por perto" (mesmo pai/filho)
  de várias opções que NÃO são volume de verdade (ver próxima seção) - uma
  busca ingênua por `VolumeSliderUI` pega falso-positivo. Por isso a ordem
  de checagem importa: sempre procurar primeiro um `ToggleButton` real
  antes de considerar `VolumeSliderUI`.
- Só Música e Efeitos Sonoros são tratados como porcentagem (0-100%) na UI -
  confirmado comparando a referência do componente com os campos públicos
  `musicSlider`/`sfxSlider` do `SoundMenuUI` ao vivo. Qualquer outro
  `VolumeSliderUI` encontrado mostra "level N of 10" em vez de porcentagem
  (caso ainda exista algum não mapeado para `ToggleButton`).

### Liga/desliga (`ToggleButton`)

- Classe própria do jogo (não é o `Toggle` da Unity). Propriedade pública
  `DINJBIOPIOH` (bool) = estado ligado/desligado; dispara `ToggleOn`/
  `ToggleOff` (UnityEvent) ao mudar.
- **Confirmado no código-fonte que TODAS estas opções são `ToggleButton`
  (liga/desliga simples, sem níveis):**
  - `SoundMenuUI.chatToggle` (bate-papo) e `tutorialEnabledToggle`
  - `GraphicsMenuUI.fullScreenToggle`, `vSyncToggle`, `flashLightsToggle`,
    `tutorialEnabledToggle`
  - `OthersMenuUI.tutorialEnabledToggle`, `increaseUIToggle`,
    `autoRunUIToggle`, `vibrationUIToggle`, `inviteCodeUIToggle`,
    `easyDifficultyUIToggle`
- Detecção robusta: procurar `ToggleButton` no próprio objeto, nos filhos e
  nos pais (`GetComponent ?? GetComponentInChildren ?? GetComponentInParent`),
  e só considerar `VolumeSliderUI` se NENHUM `ToggleButton` for encontrado.

### Controles "anterior/próximo" (Resolução, Qualidade, Zoom da câmera, Idioma)

- Estrutura: linha (ex: `Resolution`) > filho `MultiSelection` > filhos
  `PreviousButton` e `NextButton`. Sem componente especial - é só
  estrutura de GameObjects.
- O texto do valor ATUAL não fica dentro da linha - é um campo TMP
  separado na classe do painel (`GraphicsMenuUI.resolutionText`,
  `.qualityText`, `.zoomText`; `OthersMenuUI.languageText`). Por isso o
  mod usa um mapa nome-da-linha -> campo de texto
  (`BuildMultiSelectValueReader`) em vez de tentar ler texto da própria
  linha.
- UX: os botões Previous/Next individuais ficam ocultos da lista (dobrados
  em um item só); Enter no item entra no "modo de ajuste" (mesmo usado
  para volume - ver abaixo), setas esquerda/direita chamam os botões
  reais, Enter/Esc confirma.

### "Modo de ajuste" (genérico, usado por volume e anterior/próximo)

Um único mecanismo (`StartAdjusting`/`UpdateAdjusting` em
`KeyboardUINavigator`) compartilhado por:
- Controles de volume (`VolumeSliderUI.Increment/DecrementLevel`)
- Controles anterior/próximo (`Button.onClick.Invoke()` dos botões reais)

Enter no item entra no modo; setas esquerda/direita chamam a ação e
anunciam o novo valor; Enter/Esc/Espaço confirma e sai. Enquanto ativo, a
navegação normal da lista (setas cima/baixo, outros Enters) fica pausada.

## Decisões de design (e por quê)

- **Elementos indisponíveis ficam ESCONDIDOS da lista** (não aparecem, nem
  como "(unavailable)"). Tentamos mostrar como "(unavailable)" antes, mas
  causou confusão (ex: um suposto slot "Carregar" que nem aparecia
  visualmente em tela). Decisão do usuário: só listar o que está realmente
  disponível para uso.
- **Texto sempre vem da tela real (TMPro), nunca inventado.** Só cai para o
  nome interno do objeto (humanizado, tipo "VolumeUpButton" -> "Volume Up
  Button") quando o elemento não tem nenhum texto próprio (ex: botão só
  ícone). Pedido explícito do usuário: nunca inventar rótulo, só usar o que
  está em tela ou, na falta disso, o nome interno como último recurso.
- **Falas de boilerplate (separadores como "(1 of 4)", "on/off") ficam em
  inglês por enquanto** - decisão pendente de revisão futura (ver
  "Perguntas abertas"). O texto do JOGO em si sempre sai na língua
  configurada (português, no caso do usuário).
- **Primeira fala de uma tela recém-aberta não interrompe a fala anterior**
  (`ScreenReader.Say(text, interrupt: false)` só nesse caso) - evita corte
  de fala quando o anúncio de "janela aberta" e o anúncio da lista de itens
  saem quase juntos.
- **Cursor preservado ao recalcular a lista** (`Commit()` tenta manter o
  mesmo item focado por referência, só reseta pro primeiro item se esse
  item não existir mais) - sem isso, qualquer recálculo (inclusive cliques
  em anterior/próximo) "teleportava" o cursor pro topo da lista.
- **Reanúncio só quando o CONJUNTO de itens muda de verdade** (não a
  ordem) - evita repetir a mesma fala por causa de pequenas variações de
  posição/animação.

## Perguntas abertas / pendências

- Em "Atribuir Teclas", alguns elementos do tipo
  `KeybindElementKeyboard(Clone)` são lidos sem nome (cai no nome interno
  do clone) - provavelmente uma tecla ainda não atribuída, ou um slot só
  pra controle (gamepad) sem tecla de teclado. Precisa de investigação
  focada quando chegarmos nessa feature.
- Falas de boilerplate em inglês ("(1 of 4)", "on/off", "Adjusting...",
  "confirmed") - decidir se traduz para português ou mantém em inglês como
  padrão consistente do mod.
- Existe um `increaseUIToggle` no código (`OthersMenuUI`) que ainda não
  identificamos pelo nome em português na tela.

## Como testar este módulo

1. Ativar F12 (debug) - liga log detalhado de cada item da lista
   (`[STATE]   [i] "nome" path=... components=[...] interactable=...`) no
   `Latest.log` do MelonLoader.
2. Abrir Menu Principal -> Jogar / Opções -> cada aba.
3. Setas cima/baixo para navegar, Enter para ativar/ajustar, Esc para
   voltar.
4. Reportar em `novo_pedido.txt` (ver raiz do projeto) o que foi observado.
