# Ajuste da placa (undervolt / overclock) — referência

O Lotus guia, o MSI Afterburner aplica, o Lotus testa. O Lotus nunca grava clock ou voltagem por conta própria.

## Finalidades
| Modo | Para quê | Efeito típico |
|---|---|---|
| Undervolt ("mais frio e estável") | frametime mais liso, menos calor/ruído | mesma velocidade com menos energia; clock não oscila |
| Overclock ("mais FPS") | +3–8% de FPS | mais calor, energia e ruído |
| Padrão | voltar ao de fábrica | — |

## Pontos de partida (conservadores; cada chip é diferente — o teste decide)
| Família | Undervolt (curva plana) | Passo | Observação |
|---|---|---|---|
| RTX 5080/5090 | 950 mV @ ~2600 MHz | 15 MHz | driver 595.71 limita voltagem (OC rende menos) |
| RTX 5070 / 5070 Ti | 950 mV @ ~2600 MHz | 15 MHz | 5070 Ti de usuário: 935 mV @ 2955 MHz, mem +1500 |
| RTX 5060 / 5060 Ti | 950 mV @ ~2700 MHz | 15 MHz | guia MSI: 5060 Ti ~2,8 GHz @ 900 mV possível |
| RTX 4090 | 985 mV @ ~2700–2800 MHz | 15 MHz | −40 W com −1% FPS (relato) |
| RTX 4070 / 4080 | 950 mV @ ~2700 MHz | 15 MHz | |
| RTX 4060 / 4060 Ti | 925 mV @ ~2650 MHz | 15 MHz | |
| RTX 3060 | 900 mV @ ~1850 MHz | 25 MHz | |
| RTX 3070/3080/3090 | 875 mV @ ~1800 MHz | 25 MHz | |
| RTX 20 / GTX 16 | 900 mV @ ~1800 MHz | 15 MHz | |
| Radeon (RX 7000/9000) | Adrenalin: −40 mV, depois −25 mV por teste | — | maioria termina entre −70 e −100 mV; limite de energia −10% reduz consumo |

Overclock (NVIDIA): limite de energia no máximo; núcleo +50 MHz por passo; memória +100 MHz por passo
(GDDR6X/GDDR7 corrigem erros e perdem velocidade antes de travar: subir até o pico de desempenho, não até o erro).
Guardar 70% do maior valor que passou (margem para sessões longas, ideia do GPU Auto Optimizer).

## Teste e sinais
- Carga de GPU ~5 min (ou 30 min no jogo mais pesado); parar acima de 83 °C; aviso acima de 80 °C.
- Reset do driver (nvlddmkm / evento de "GPU reset") = instável → voltar um passo (−50 MHz ou +25 mV).
- Artefatos = memória; desfazer o ajuste de memória primeiro. Travamento total = −100 MHz.
- Clock caindo muito sob carga = limite de energia/temperatura.

## Aplicar só no jogo
Perfil 1 = padrão, Perfil 2 = ajuste; Afterburner > Configurações > Perfis: 2D = Perfil 1, 3D = Perfil 2
(a troca automática depende do RivaTuner instalado). Nunca aplicar ao iniciar o Windows.

## Fontes
- MSI: https://www.msi.com/blog/rtx-5070-5060ti-overclocking-undervolting-guide-with-msi-afterburner-part-1 e -part-2
- Curva plana por placa: https://nvidiareview.com/msi-afterburner-flatten-curve/
- RTX 4060: https://tier1settings.com/rtx-4060-undervolt-guide/
- RTX 5060: https://evezone.evetech.co.za/quick-bytes/how-to-undervolt-your-rtx-5060-step-by-step-guide
- Banco de resultados: https://techfuelhq.com/articles/gpu-undervolt-settings-database/
- Radeon: https://techfuelhq.com/articles/rx-9070-xt-undervolt-2026/
- Driver 595.71: https://www.notebookcheck.net/Nvidia-nerfs-voltages-on-RTX-GPUs-with-latest-driver-update-leading-to-lower-performance.1241165.0.html
- GitHub: https://github.com/Rovey/gpu-auto-optimizer, https://github.com/vuplea/simple-nvidia-undervolt, https://github.com/aufkrawall/green-curve
