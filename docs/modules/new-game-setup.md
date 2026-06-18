# Módulo: Início de Novo Jogo (Character Creator + primeiras telas)

> Stub inicial - preencher conforme investigamos. Ver convenção em
> `docs/modules/main-menu-and-options.md` (cabeçalho).

## Status atual

- Ainda não iniciado. Acabamos de criar a branch `feature/newGame` a partir
  da `main` (que já tem Menu Principal + Opções funcionando).

## O que sabemos até agora (de `docs/game-api.md` e leitura rápida do código)

- No fluxo "Jogar" -> "Novo" (`SaveUI`, botão `SlotButton`), o jogo abre
  `CharacterCreatorUI.Get(playerNum).SetCharacter(playerNum)`.
- `PlayerInfo` (classe estática) guarda `tavernName` e nome(s) do(s)
  jogador(es) - provavelmente definidos nessa tela de criação.
- Ainda não exploramos `CharacterCreatorUI.cs` em detalhe (estrutura de
  UI, quantos passos/abas, que campos existem).

## Próximos passos de investigação

1. Ler `decompiled/CharacterCreatorUI.cs` por completo.
2. Testar em jogo entrando em "Jogar" -> "Novo" e usar OCR/observação do
   usuário para mapear cada tela do fluxo de início de jogo.
3. Documentar aqui a estrutura real antes de implementar navegação.

## Perguntas abertas

- Quantas telas existem entre "Novo" e o jogo realmente começar (criação de
  personagem, nome da taverna, talvez seleção de dificuldade)?
- Alguma dessas telas reaproveita os mesmos padrões já resolvidos em Opções
  (ex: `MultiSelection` anterior/próximo, `ToggleButton`)?
