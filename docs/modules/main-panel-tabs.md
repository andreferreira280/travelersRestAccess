# Módulo: Painel principal (Inventário/Missões/Receitas/Habilidades) e Enciclopédia

> Ver convenção em `docs/modules/main-menu-and-options.md` (cabeçalho).

## Status atual

- Navegação dentro das abas de `MainPanelUI` (Inventário, Missões,
  Receitas, Habilidades, Coleções, Estatísticas da Taverna):
  **corrigida, confirmada funcionando pelo usuário.**
- Conflito "setas também andam com o personagem": **corrigido,
  confirmado funcionando.**
- Enciclopédia (seções sem nome distinto): **corrigida, confirmada
  funcionando.** Conteúdo do tópico (corpo de texto): **corrigido
  2026-06-20, ainda não testado.** Item "Voltar" (existe de verdade,
  só estava fora de posição): **corrigido 2026-06-20, ainda não
  testado.** Ver "Enciclopédia" abaixo.
- Missão principal não aparecia em Missões: **corrigida de verdade
  2026-06-20 (rodada 2 - a primeira correção foi substituída por pedido
  do usuário), ainda não testada ao vivo.**
- Som de navegação (todas as listas): **implementado e ajustado a
  partir de feedback real 2026-06-20 (som de fronteira não fica mais
  em cima do som de movimento; som diferente para "escolher resposta"
  vs. "avançar fala"), ainda não testado ao vivo.** Ver
  `docs/game-api.md` seção 7 (Áudio).
- Botão genérico "VersatileButton" sem rótulo (ex: Missões "Button (8
  of 9)"): ainda sem solução - ver abaixo.

## MainPanelUI - abas misturadas (corrigido)

**Sintoma reportado pelo usuário:** "não está navegando pelo
inventário", e o mesmo bug se manifestava (sem o usuário saber a causa
em comum) como "não consigo navegar por missões/receitas/habilidades".

**Causa raiz confirmada via log ao vivo:** a primeira tentativa de
correção (rodada anterior) assumia que `panel.content.activeInHierarchy`
identificava a aba selecionada - **errado**: TODAS as abas de
`MainPanelUI` ficam "ativas" ao mesmo tempo (as não-selecionadas só são
deslocadas pra fora da tela, não desativadas - achei um deslocamento de
`(20000, 20000)` no código decompilado). Confirmado ao vivo: abrir o
Inventário gerava uma lista de 57 itens, uma mistura aleatória de slots
de Inventário + Missões + Receitas + Habilidades + Coleções juntos.

**Correção:** em vez de checar `activeInHierarchy`, o mod agora lê
direto o campo privado que o PRÓPRIO jogo usa pra saber qual aba está
selecionada (`MainPanelUI.HACEDOOFMBE`, via reflexão - confirmado no
código decompilado que esse campo é mantido sempre atualizado,
inclusive usado pelas próprias chamadas internas `FocusMainPanel(...)`
do jogo). Ver `KeyboardUINavigator.CollectItems()`.

## Setas também moviam o personagem (corrigido)

**Sintoma reportado:** "setas estão andando igual a w s d" com o
Inventário aberto.

**Causa raiz confirmada no código decompilado** (`PlayerController
.Update()`): diferente de Pausa/Opções/Criador de Personagem, abrir
`MainPanelUI` **não pausa o movimento de verdade** - é proposital do
jogo, pra dar pra andar enquanto olha o inventário. A checagem que
bloqueia movimento só olha `PauseMenuUI.IsOpen()`, nada relacionado a
`MainPanelUI`. Como o jogo usa a MESMA ação ("HorizontalMove"/
"VerticalMove") tanto para W/A/S/D quanto para as setas, as setas
sempre moviam o personagem também, ao mesmo tempo que tentavam navegar
nosso menu.

**Decisão tomada com o usuário:** perguntei se preferia usar o teclado
numérico ou Tab para navegar essas telas - o usuário pediu para manter
W/A/S/D andando (como já funciona) e deixar as setas livres só para a
nossa navegação, sem precisar de tecla alternativa.

**Correção:** `MovementAxisPatch.cs` (novo patch Harmony em
`PlayerInputs.GetAxis(string)`) - o resultado de "HorizontalMove"/
"VerticalMove" é recalculado só a partir de W/A/S/D brutos
(`Input.GetKey`), ignorando o que o Rewired calcularia das setas.
W/A/S/D continuam andando exatamente como antes; as setas não
contribuem mais para o movimento.

**Atualizado na rodada seguinte (mesmo dia):** a flag
`SuppressArrowMovement` começou como condicional (só ativa enquanto
uma tela de navegação estava aberta), mas o usuário pediu pra travar
o uso de setas pra andar SEMPRE, mesmo fora de menus (são usadas pra
reler mensagens de diálogo - ver `dialogue-system.md` v6 - e no futuro
talvez pra mais coisas). Agora a flag é ligada permanentemente em
`Main.cs`, uma única vez, ao aplicar os patches.
**Limitação conhecida (aceitável para este usuário):** se algum dia
usar um controle/gamepad, o movimento pelo analógico também seria
ignorado (a recomputação considera só teclado). Não é um caso de uso
real aqui (mod é pra teclado + leitor de tela).

## Enciclopédia - seções sem nome distinto (corrigido, não testado)

**Sintoma reportado:** "a lista na enciclopédia está toda bagunçada" -
na prática, as 13 seções da Enciclopédia eram lidas todas como "Button
Components" (nome genérico do botão-prefab, igual em todas as 13, sem
texto próprio - cai no fallback de humanizar o nome do GameObject).

**Tentativa anterior (rodada 4):** um diagnóstico gated em `justOpened`
foi adicionado pra logar `encyclopediaData.sections[i]` antes de
implementar de verdade - mas NUNCA disparou. Causa: `justOpened`
(`!_wasOpen`) significa "nenhuma UI estava aberta no frame anterior" -
falso sempre que uma janela abre por CIMA de outra já aberta (aqui,
Enciclopédia abre a partir do menu de Pausa, que já estava aberto) -
mesma classe de bug do "_lastStoryMessage perdido" (`dialogue-system.md`
v6).

**Correção de verdade (rodada 5):** o nome real de cada seção existe
como chave de localização
(`EncyclopediaUI.encyclopediaData.sections[i].sectionTitleID`,
resolvido com `LocalisationSystem.Get(...)`). O dígito em "SectionN" não
é um índice confiável pro array de dados (confirmado ao vivo:
`GetComponentsInChildren` retornou "Section11" antes de "Section9"/
"Section10" - ordem de hierarquia, não numérica) - em vez disso, o mod
casa por POSIÇÃO na mesma lista ordenada por Y/X que o usuário realmente
ouve (`visible`, já calculada para decidir a ordem de navegação) contra
a ordem do array de dados. Funciona porque listas paralelas
UI/dados como essa são quase sempre criadas na mesma ordem dos dois
lados, mesmo que os nomes dos GameObjects não reflitam isso. Loga um
aviso (modo debug) se a quantidade de botões de seção não bater com a
quantidade de dados, indicador de que a suposição estaria errada.

**Confirmado funcionando, mas achou outro bug (mesma rodada):** os
nomes agora saem certos, porém a lista começa no item 11/12 em vez do
1. Causa: `_rememberedAnchors` (mecanismo de preservar o cursor ao
VOLTAR de um popup aninhado, tipo ColorPicker -> CharacterCreator)
guarda a posição por instância de `UIWindow` - como várias janelas
(Enciclopédia inclusive) são singletons persistentes do jogo (mesma
instância sempre), reabrir a Enciclopédia bem depois, já numa sessão de
jogo diferente, reaproveitava a posição de quando ela foi vista por
último, em vez de recomeçar do topo. Corrigido limpando esse dicionário
inteiro sempre que TUDO fecha (`MainUI.IsAnyUIOpen(1)` vira falso) - a
preservação de cursor continua funcionando para o caso original
(retornar de um popup com a janela de fora ainda aberta), só não
"vaza" mais entre sessões totalmente separadas.

**Conteúdo do tópico não era lido (corrigido, 2026-06-20):** usuário
abriu a seção "Controles Básicos" e não ouviu nenhuma informação.
Causa: o corpo de texto de um tópico (`EncyclopediaUI.sectionTitle`/
`sectionText`, ambos `[SerializeField] private TextMeshProUGUI`
confirmados no código decompilado) é só exibição, sem `Button` -
nunca apareceria na navegação por setas. Também não cai no scanner
global de diálogo (`DialogueAnnouncer`), que se desliga de propósito
sempre que qualquer UI já está aberta (pra não duplicar leitura com o
`KeyboardUINavigator`) - e a Enciclopédia é uma UI. Corrigido com
`AnnounceEncyclopediaContentIfChanged` (mesmo padrão usado pra missão
principal antes da correção dela: lê os dois campos via reflexão,
anuncia só quando o texto muda). Ainda não testado ao vivo.

**"Voltar" estava na posição errada (corrigido de verdade 2026-06-20,
2ª tentativa):** conclusão anterior (sem botão real) estava errada - o
campo `backButton` (`GamepadSprite`) era só um ícone, mas existe um
Button real ligado por evento da Inspector do Unity (invisível ao
código decompilado). 1ª tentativa de correção (mesmo dia) chutou que
ele compartilhava o caminho `TabsListContent` das seções - **errado
também**, e pior, isso moveu um item de SUBSEÇÃO sem relação alguma
pro fim da lista. Causa real, confirmada lendo o log ao vivo do
usuário (`Latest.log`, com F12 ativado): o botão de verdade é o padrão
"VersatileButton" já conhecido de outras abas (`MenuUI/.../
VersatileButton/Button`), parecido mas SEM relação com
`TabsListContent`. Corrigido com o caminho real - qualquer
`Selectable` chamado "Button" com pai "VersatileButton" dentro da
Enciclopédia recebe o rótulo "Voltar" e vai pro fim da lista.
**Confirmado funcionando pelo usuário.**

**Bônus do mesmo log: subseção "Controles Básicos" inalcançável
(corrigido):** ao expandir uma seção (ex: "Essenciais"), suas
subseções são clones Unity de um prefab só, sob um pai "SubSections" -
a convenção de nomes do próprio Unity batiza a PRIMEIRA cópia com o
nome puro ("Subsection") e só sufixa as repetições ("Subsection (1)"
.."(4)") - confirmado no log que a "Subsection" sem sufixo é a
subseção 1 de verdade ("1.1 Controles Básicos"), só que sua posição Y
na tela não acompanha as outras, jogando ela pro fim de uma lista
de 19 itens (inalcançável na prática). Corrigido com
`ReorderEncyclopediaSubsections()`: reordena cada grupo de irmãos sob
"SubSections" pelo sufixo "(N)" (sem sufixo = primeiro), ignorando a
posição Y. **Confirmado funcionando pelo usuário** (ordem certa e
conteúdo lido).

**Reler o conteúdo de uma subseção já lida (corrigido):** usuário
pediu pra poder reler o conteúdo de uma subseção já escolhida (hoje só
anuncia quando o texto MUDA, então clicar de novo na mesma não lia
nada). Em vez de criar um item de lista novo só para isso (mudança
maior), `Activate()` agora limpa `_lastAnnouncedEncyclopediaContent`
sempre que o item ativado está sob "SubSections" - assim a próxima
leitura trata como "mudou" e anuncia de novo. Ainda não testado ao
vivo.

## Missão principal não aparecia em Missões (corrigido, rodada 2)

**Sintoma reportado:** "quando abro missões nem aparece minha missão" -
investigação inicial (rodada 1) concluiu, errado, que a missão
principal ativa (`MainQuestItemUI`) era um componente de exibição pura
SEM `Button`/`Selectable` - na verdade ela tem um campo público
`public Button button` (confirmado no código decompilado,
`MainQuestItemUI.cs`), ligado a `ButtonClicked()` (alterna o "foco" da
missão, igual ao que o jogo já faz ao clicar nela com mouse/gamepad).
O erro veio de checar `GetComponent<Button>()` na própria
`MainQuestItemUI` (que não tem), sem notar que o campo `button` aponta
pra um filho.

**Correção rodada 1 (substituída):** anunciar separadamente
(`AnnounceMainQuestIfChanged`) sempre que a aba Missões era selecionada
ou a missão mudava - o usuário reportou (rodada de testes seguinte) que
isso lia a missão "ao léu" e pediu pra em vez disso poder achar a
missão na lista e interagir com ela.

**Correção rodada 2 (atual):** `mainQuestItem.button` agora é incluído
diretamente na lista de navegação (`CollectItems`), com um
`labelReader` que monta título+descrição ao vivo
(`DescribeMainQuest`). Setas chegam nele como qualquer outro item, e
Enter chama o mesmo `onClick` que o jogo usaria (alterna foco da
missão) - sem precisar do refactor maior de `NavItem` (o item É um
`Selectable` real, só precisava ser explicitamente adicionado, já que
`mainQuestParent` parece ficar fora da raiz `content` normalmente
escaneada). Ainda não testado ao vivo.

## Som de navegação (implementado, ajustado, ainda não testado)

**Pedido do usuário:** um som a cada movimento de navegação em
QUALQUER lista (vertical ou horizontal), e um som DIFERENTE quando não
há pra onde ir (lista com um item só, ou alcançou o topo/fim da
lista). Pedido pra reaproveitar som do próprio jogo em vez de criar
um novo.

**Implementado:** achei no código decompilado o "banco" de sons do
jogo (`Sound.GGFJGHHHEJC`, ver `docs/game-api.md` seção 7) com os
clipes `uiClickPos`/`uiClickNeg` (clique válido/inválido - são os
mesmos usados pelo jogo em telas como o Inventário).

**Ajuste (mesma rodada, feedback imediato):** a primeira versão tocava
`uiClickPos` OU `uiClickNeg` (um ou outro). O usuário esclareceu: quer
o som de item (`uiClickPos`) tocando SEMPRE, em todo movimento, e o som
de aviso (`uiClickNeg`) tocando JUNTO (os dois ao mesmo tempo) só na
situação de fronteira (lista de 1 item, ou voltou ao início/fim) - não
substituindo o som do item. Corrigido em `KeyboardUINavigator.Move()` e
`DialogueAnnouncer.HandleResponseInput()`.

**Também adicionado (mesmo pedido, ampliado):** o usuário pediu
confirmação sonora também para Espaço (avançar diálogo/confirmar
escolha de resposta) e Enter (selecionar algo, fechar uma janela) -
"as vezes fico com a impressão que dou várias [teclas] e não sei
quando foi aceito". Toca o mesmo som de navegação (`uiClickPos`) nesses
casos também - `KeyboardUINavigator.Activate()` (Enter) e
`DialogueAnnouncer.HandleAdvanceAndRereadInput()` /
`HandleResponseInput()` (Espaço).

**Ajustes rodada de testes seguinte (2026-06-20), a partir de feedback real:**
- **Som de movimento + som de fronteira ficaram indistinguíveis:**
  confirmado pelo usuário ("tocam muito juntos para identificar
  diferença") - causa veio do próprio design da rodada anterior: tocar
  os dois no MESMO frame faz eles se misturarem num só som. Corrigido
  com `UISound.PlayBoundaryDelayed()` (espera ~0.15s via
  `MelonCoroutines.Start` antes de tocar o som de fronteira), usado em
  `KeyboardUINavigator.Move()` e `DialogueAnnouncer.HandleResponseInput()`
  no lugar do `PlayBoundary()` direto.
- **Som diferente pedido para "escolher uma resposta de diálogo" vs.
  "só avançar/pular para a próxima fala":** adicionado
  `UISound.PlayChoiceConfirm()` (mesmo clipe `uiClickPos`, só com pitch
  mais alto) usado especificamente quando Espaço escolhe uma resposta
  de diálogo (`DialogueAnnouncer.HandleResponseInput`).
- **Revertido 2026-06-20 (rodada seguinte):** usuário reportou
  consistentemente NENHUM som ao avançar fala linear (mesmo com o
  avanço funcionando) - o clique normal (`PlayNavigate`, pitch
  padrão) estava aparentemente fácil demais de não notar por cima do
  ambiente do jogo. Pediu explicitamente o MESMO som da escolha de
  resposta também aqui - `HandleAdvanceAndRereadInput` agora usa
  `PlayChoiceConfirm()` em vez de `PlayNavigate()`. Efeito colateral
  sabido: a distinção sonora entre "escolher resposta" e "avançar
  fala" pedida antes deixou de existir (as duas ficaram iguais de
  novo) - aceito como troca, já que o pedido mais recente prevalece.
- **"Os dois sons não tocam" ao navegar/voltar a jogar pelo menu:**
  ainda não confirmada a causa raiz (precisa do próximo teste com F12
  ativado) - adicionado log de diagnóstico em `UISound` (modo debug)
  que avisa se `Sound.GGFJGHHHEJC` está nulo ou se o `blockSound` do
  próprio jogo está ativo no momento da chamada, em vez de tentar
  adivinhar a causa sem evidência.

## Botão genérico "VersatileButton" sem rótulo (ainda sem solução)

Reaparece em várias abas (Inventário, Receitas, Missões, Coleções) com
significado diferente em cada uma - não está em lugar nenhum do código
decompilado (nome puro de cena/prefab), e o ícone que carrega
(`NewUI_BlackBackground`) é o MESMO em todas as instâncias, então não
ajuda a diferenciar. Sem pista nova ainda.

## Arquivos envolvidos

- `KeyboardUINavigator.cs` - `CollectItems()` (seleção de aba do
  MainPanelUI, nomes de seção da Enciclopédia, item navegável da missão
  principal via `DescribeMainQuest`, conteúdo de tópico da Enciclopédia
  via `AnnounceEncyclopediaContentIfChanged`), `Move()`/`Activate()`
  (som de navegação).
- `MovementAxisPatch.cs` - patch Harmony do conflito de movimento.
- `UISound.cs` - sons reaproveitando clipes do jogo (`PlayNavigate`,
  `PlayBoundary`/`PlayBoundaryDelayed`, `PlayChoiceConfirm`).
- `Main.cs` - aplica os patches.
