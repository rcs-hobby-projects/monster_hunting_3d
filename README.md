# Caçador de Monstros 3D

Jogo de aventura e **terror noturno** em primeira pessoa, feito em C# com OpenTK (OpenGL + OpenAL).
É noite fechada: só a lua, as estrelas e a sua lanterna iluminam a arena. Sobreviva aos
**12 níveis** de monstros que surgem do nada, rosnando no escuro.

Música de terror e todos os efeitos sonoros são 100% gerados por código — nenhum
arquivo de áudio. Se o sistema não tiver OpenAL, o jogo roda normalmente sem som.

## Como jogar

```bash
dotnet run
```

O jogo abre na **tela inicial** — clique em **INICIAR** (ou aperte Enter) para começar.
Lá também estão as telas de **COMANDOS** e **SOBRE**.

## Controles

| Tecla / Botão        | Ação                       |
|----------------------|----------------------------|
| Mouse                | Olhar ao redor             |
| W / A / S / D        | Andar                      |
| Shift esquerdo       | Correr                     |
| Espaço               | Pular                      |
| Botão esquerdo ou F  | Atacar                     |
| E                    | Trocar de arma (perto de uma) |
| Esc ou P             | Pausar / continuar         |
| R                    | Reiniciar (após morrer)    |

No menu de pause há botões clicáveis de **CONTINUAR** e **SAIR** (volta para a tela inicial).

## Os 12 níveis

- Os monstros **surgem do nada** perto de você, com um som arrepiante — fique atento aos rosnados
  (o áudio é posicional: dá para ouvir de que lado eles vêm).
- Cada nível traz mais monstros, e eles ficam mais fortes a cada nível.
- Vença o nível 12 para ver a tela de vitória!

## Monstros

- **Slime (verde)** — lento e comum. Olhos verdes brilhantes.
- **Diabrete (vermelho)** — rápido, mas frágil. Olhos vermelhos.
- **Brutamontes (roxo)** — enorme e muito forte, solta rugidos graves. Olhos violeta.

## Armas

As 3 armas mais fortes ficam **flutuando pelo mapa desde o início**, cada uma marcada
por um **feixe de luz colorido** visível de longe (até através da névoa). Chegue perto
e aperte **E** para trocar: a arma que estava na sua mão fica flutuando no lugar,
e você pode voltar para buscá-la depois.

| Arma                | Dano | Cor do feixe |
|---------------------|------|--------------|
| Espada              | 34   | prata (inicial) |
| Machado             | 60   | aço          |
| Lâmina Sombria      | 85   | roxo         |
| Martelo de Guerra   | 130  | cinza-azulado |

## Mecânicas

- Sua vida regenera aos poucos se você ficar 4 segundos sem tomar dano.
- HUD: barra de vida, recarga do ataque, arma atual, nível e um quadradinho
  vermelho para cada monstro vivo.
- O título da janela mostra HP, nível e total de mortes.
# monster_hunting_3d
