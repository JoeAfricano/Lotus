# Gerador de quadros sem G-Sync/FreeSync — 22/11/2026

Pedido do José: um preset de frame generation para quem não tem G-Sync/FreeSync, com o rasgo fora da tela e o menor atraso possível, testado no Gothic 1 Remake.

## Condições

Ryzen 5 5500, RTX 5060 8 GB, monitor Samsung LF24T35 a 74,973 Hz por HDMI, sem VRR. Gothic 1 Remake (UE5, DLSS Frame Generation via Streamline, Reflex + Boost ligados no jogo). RTSS 7.3.5. PresentMon 2.6.0 com `--track_pc_latency`, 30 s por modo, o José parado no mesmo lugar do jogo. Script: `tools/Compare-NoVrrFrameGen.ps1` (troca só o perfil do RTSS do jogo e devolve o original no fim). CSVs em `artifacts/novrr-framegen-20261122/` (não versionados).

## Resultados (jogo de verdade, não menu)

| Gerador | Sincronia | Ritmo na tela | Atraso do PC (mediana / p99) |
|---|---|---|---|
| Ligado | RTSS Reflex 74,97, sem V-Sync (o que o Lotus fazia) | Quadros em pares: 0,2 ms e 28 ms alternando, com rasgo (parece 37 FPS) | 38 / 42 ms |
| Ligado | RTSS Scanline Sync −40 (rasgo na faixa escondida) | Liso: 10–17 ms, nenhum acima de 20 ms | 105 / 116 ms |
| Ligado | V-Sync do driver NVIDIA + RTSS Reflex 74,97 | Liso, sem rasgo, 7 quadros acima de 20 ms | 58 / 73 ms |
| **Ligado** | **V-Sync do driver NVIDIA, RTSS sem limite** | **Liso, sem rasgo, 2 quadros acima de 20 ms** | **50 / 63 ms** |
| Desligado | V-Sync do driver + RTSS Reflex 74,97 | 68 FPS: o V-Sync repete ~200 quadros em 30 s | 57 / 71 ms |
| Desligado | V-Sync do driver, sem limite | Igual ao anterior | 58 / 69 ms |

Com o gerador ligado, o jogo trava o próprio V-Sync; o do driver só vale depois de reabrir o jogo.

### Em movimento (o José andando e girando a câmera, gerador ligado, V-Sync do driver)

| RivaTuner | Quadros acima de 20 ms | Frametime p99 | Atraso do PC (mediana / p99) |
|---|---|---|---|
| Sem limite | 26 (um pico de 173 ms) | 26,7 ms | 57 / 84 ms |
| **Reflex 74,97** | **18** | **13,6 ms** | **59 / 75 ms** |

Nenhum dos dois rasgou (V-Sync no envio). Parado, "sem limite" tinha ganhado por 8 ms; andando, o Reflex 74,97 deixou a linha mais reta e o pior atraso menor. A prioridade do José é a linha reta, então o preset usa o Reflex.

## Conclusão

- **Com gerador de quadros, sem VRR, NVIDIA:** V-Sync do driver ligado só para o jogo e limitador Reflex do RTSS no limite exato (74,97 neste monitor).
- **Scanline Sync** (o "rasgo fora da tela") deixa a imagem lisa, mas com gerador de quadros mais que dobra o atraso: o RTSS espera a linha da tela a cada envio, real e gerado. Continua sendo a escolha do Lotus **sem** gerador, quando sobra GPU (`LimiterChoice`, modo "scanline").
- Neste lugar do Gothic, o gerador com V-Sync do driver ficou mais liso e com menos atraso do que sem gerador, porque sem ele o jogo não segura 75.

## O que o Lotus faz agora

- Modo novo `fgvsync`. Escolhido automaticamente quando o jogador marca "Uso o gerador de quadros neste jogo", a placa é NVIDIA (Direct3D 11/12) e o monitor não tem G-Sync/FreeSync: `LimiterChoice.Pick` e `PresentationPlan.ResolveLimiter` (modo Automático).
- Ao salvar ou clicar em Ligar, o ajudante elevado (o mesmo do RTSS) grava o perfil do RTSS do jogo com o limitador Reflex e liga o V-Sync do driver só para aquele exe (`NvidiaDriverVsync`, NVAPI DRS: `VSYNCMODE` = `FORCEON`). Antes de mudar, guarda o estado anterior em `RtssBackups/<exe>.nvvsync.json`. Se o exe não tinha perfil no driver, o Lotus cria "Lotus - <jogo>".
- Desligar, Restaurar ou trocar de modo devolve o perfil do RTSS e o V-Sync do driver como estavam (apaga o perfil criado pelo Lotus).
- O modo só é reconhecido como ativo quando o perfil do RTSS está em Reflex, o backup do Lotus existe e o driver confirma o V-Sync forçado.

## Verificado

- Leitura do driver: o Gothic mostrou `FORCEON` depois que o José ligou no Painel NVIDIA.
- Ciclo completo com um exe fictício pelo ajudante elevado: criar o perfil, forçar o V-Sync, salvar e restaurar (perfil apagado, backup removido, perfil do RTSS removido).
- 981 testes passando (Debug/x64), build sem avisos.

## Não verificado

- O clique de Salvar na tela com o Gothic (o jogo estava aberto e o Lotus pede para fechá-lo antes).
- Outros jogos com DLSS FG, FSR/XeSS FG, AMD/Intel (o modo só existe para NVIDIA) e monitores de 60 Hz.
- Atraso de clique até a tela com equipamento óptico. O "atraso do PC" é o PC Latency do Reflex lido pela PresentMon, não inclui monitor nem mouse.
- Uma passada por modo, sem repetição. A ordem não foi aleatória.
