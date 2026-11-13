# Lotus — Ritmo de quadros em monitor sem FreeSync/G-Sync

Estudo feito em 12/11/2026 no PC do José: Ryzen 5 5500, RTX 5060 8 GB em PCIe 3.0 x8, monitor Samsung LF24T35 por HDMI. Esse monitor não tem VRR na NVIDIA e roda a **74,973 Hz**, não a 75.

## Medições (Black Myth: Wukong Benchmark, PresentMon, 140 s, Alto + DLSS, V-Sync do jogo ligado)

| Teste | 1% low | Desvio do frametime | Present → tela |
|---|---|---|---|
| Como estava (RivaTuner 75) | 57,4 | 1,59 ms | 23,4 ms |
| Só V-Sync, sem limitador | 61,2 | 1,17 ms | 26,6 ms |
| V-Sync + RivaTuner 74,95 (async) | 65,1 | 0,80 ms | 19,2 ms (repete 1 quadro a cada ~43 s) |
| V-Sync + RivaTuner 74,97 (async) | 65,0 | 0,81 ms | 22,2 ms |
| **V-Sync + RivaTuner 74,97 (Reflex)** | **65,4** | **0,73 ms** | **19,6 ms** |

## O que aprendemos

- **Um limite acima da taxa real** (75 num monitor de 74,973 Hz) faz os quadros esperarem na fila do V-Sync. O resultado é mais atraso e a linha serrilhada.
- **Um limite abaixo demais** (74,95) faz um quadro se repetir de tempos em tempos.
- **O certo é ficar logo abaixo da taxa real**, arredondando para 0,01 FPS: 74,97. Um limitador só (o do RivaTuner), com o V-Sync ligado no jogo e o limite do próprio jogo desligado.
- **Os picos que sobraram vieram de coisas aparecendo por cima do jogo**: avisos, janelas e prints. Nesses momentos o Windows sai do modo direto de tela (Independent Flip) e passa a compor a imagem, o que gera quadros de 26 a 40 ms. O "Não perturbe ao jogar" do Windows ajuda.
- Fechar FanControl e MSI Afterburner, sozinho, mudou pouco.
- Os picos de carregamento do Unreal (CPU) continuam. Nenhum ajuste de ritmo resolve isso.

## O que o Lotus faz

- Lê a **taxa exata** do monitor principal (`QueryDisplayConfig`, numerador/denominador) em `WindowsSystemProfile.ExactPrimaryRefresh`.
- `FramePacing.FixedCap` calcula o limite: a taxa real (ou a fração exata dela) menos 0,001, arredondada para baixo em 0,01. O botão Ligar do RivaTuner grava esse valor (74,97). O limite inteiro do próprio jogo continua em 75, porque um número inteiro abaixo da taxa repetiria um quadro por segundo.
- Quando a taxa exata é desconhecida, nada muda em relação ao comportamento anterior.
- O "Por que está travando?" avisa quando o limite do RivaTuner (do jogo ou Global) está acima da taxa real em monitor sem VRR (`WhyCapAboveRefresh`).

## Em outros PCs

O 74,97 é do PC do José. O Lotus não grava esse número para todos: ele lê a taxa exata do monitor de cada PC e aplica a mesma regra.

| Monitor (taxa real) | Limite do RivaTuner |
|---|---|
| 60,000 Hz | 59,99 |
| 59,94 Hz (o Windows mostra "59") | 59,93 |
| 74,973 Hz (o do José) | 74,97 |
| 143,856 Hz | 143,85 |
| 144,000 Hz | 143,99 |
| 164,917 Hz | 164,91 |
| PC mais fraco em monitor de 143,856 Hz (metade da taxa) | 71,92 |

- **Monitor com FreeSync/G-Sync:** a regra é outra. O limite fica 3 FPS abaixo da taxa (no mínimo 57), sem V-Sync na fila.
- **Placa NVIDIA com Direct3D 11/12:** usa o limitador Reflex. Nas placas AMD e Intel, e nos jogos em outras APIs, usa o limitador "fluido" (front edge sync).
- **PC que não aguenta a taxa do monitor:** usa a metade exata da taxa (ou um terço), também logo abaixo do valor real.
- **Taxa exata desconhecida** (o Windows não respondeu): usa a taxa inteira, como antes.
- Os testes `FixedRateCapFollowsEachMonitor` conferem a tabela acima.

## Não verificado

- Só um jogo foi medido (benchmark, sem jogar de verdade). O atraso do controle não foi medido, só o tempo do quadro até a tela.
- Outros monitores e taxas (60, 144 Hz) seguem a mesma regra, mas não foram medidos.
