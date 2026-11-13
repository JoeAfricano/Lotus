# Ajuste da placa (undervolt / overclock) — referência

O Lotus guia, o MSI Afterburner aplica, o Lotus testa. O Lotus nunca grava clock ou voltagem por conta própria.

## Finalidades
| Modo | Para quê | Efeito típico |
|---|---|---|
| Undervolt ("mais frio e estável") | frametime mais liso, menos calor/ruído | mesma velocidade com menos energia; clock não oscila |
| Overclock ("mais FPS") | +5–10% de FPS quando a placa é o limite | mais calor, energia e ruído; comece pelo OC Scanner do Afterburner |
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

## Overclock observado em reviews (teto da escada do Lotus)
| Placa | Núcleo estável | Memória estável | Fonte |
|---|---|---|---|
| RTX 5060 (MSI Gaming OC) | +450 MHz (clock médio 2742 → 3220 MHz) | +1000 MHz | TheFPSReview |
| RTX 5060 (MSI) | até ~3037 MHz | 28,9 Gbps | HotHardware |
| RTX 5060 Ti (várias) | +200 a +375 MHz | +375 a +1000 MHz | TechPowerUp, FPS Review, PC Gamer |
| Demais famílias | +400 MHz (teto genérico) | +1000 MHz | — |

O Lotus para a escada no teto da família (`GpuTuning.CardStart.OcCoreMax / OcMemMax`) e guarda 70% do último passo estável.
Na RTX 5060 o limite de energia de fábrica é baixo: depois de certo ponto mais clock não rende e tensão extra não ajuda.
Não usar o "bypass" que libera +3000 MHz de memória (banco de dados modificado + versão beta específica do Afterburner): não é oficial.

## Undervolt: o que esperar
- NVIDIA Ada (RTX 40): de fábrica ~1050–1080 mV; alvo típico 950–1000 mV. 80% dos chips aguentam −50 a −100 mV; os 10% piores só −20 a −40 mV; os 10% melhores ~800–900 mV.
- RTX 4070: ~950 mV segura 2700–2750 MHz em muitos chips (relatos: 2610 MHz @ 910 mV; 2805 MHz @ 1000 mV).
- RTX 30 (3060/3070): sem valores publicados confiáveis; achar empiricamente (mesmo clock, tensão descendo).
- Radeon RX 9070/9070 XT: ajustar primeiro a tensão com o limite de energia no padrão; começar entre −50 e −75 mV; −85 mV estável em vários relatos; −100/−125 mV costumam passar em benchmark curto e falhar em sessões longas. Limite de energia −15% manteve o desempenho; −30% custou ~6%.
- RX 9060 XT: quase nada publicado; usar o mesmo método do 9070, com passos menores.
- Teste curto não garante: estável em DX11 pode falhar em DX12 e vice-versa; confirmar em jogo por 30 min.

## Teste e sinais
- Carga de GPU ~5 min (ou 30 min no jogo mais pesado); parar acima de 83 °C; aviso acima de 80 °C.
- Reset do driver (nvlddmkm / evento de "GPU reset") = instável → voltar um passo (−50 MHz ou +25 mV).
- Artefatos = memória; desfazer o ajuste de memória primeiro. Travamento total = −100 MHz.
- Clock caindo muito sob carga = limite de energia/temperatura.

## Aplicar só no jogo
Perfil 1 = padrão, Perfil 2 = ajuste; Afterburner > Configurações > Perfis: 2D = Perfil 1, 3D = Perfil 2
(a troca automática depende do RivaTuner instalado). Nunca aplicar ao iniciar o Windows.

## Fontes
- RTX 5060 OC: https://www.thefpsreview.com/2025/06/13/msi-geforce-rtx-5060-8g-gaming-oc-video-card-review/7 · https://hothardware.com/reviews/nvidia-geforce-rtx-5060-blackwell-gpu-preview?page=5 · https://techpowerup.com/review/msi-geforce-rtx-5060-gaming-oc
- RTX 5060 Ti OC: https://www.pcgamer.com/hardware/graphics-cards/yes-you-can-overclock-8-gb-graphics-cards-to-bring-the-benchmark-performance-in-line-with-the-16-gb-versions-but-thats-not-quite-the-whole-story/ · https://www.thefpsreview.com/2026/01/12/pny-geforce-rtx-5060-ti-8gb-overclocked-dual-fan-video-card-review/3
- Ada undervolt: https://tier1settings.com/rtx-4070-undervolt-guide/ · https://www.evetech.co.za/how-to-undervolt-gpu-better-temps/e/3684
- RX 9070: https://evezone.evetech.co.za/build-lab/rx-9070-undervolting-guide-voltage-offset-power-limit
- Bypass de memória (não usar): https://www.kitguru.net/components/graphic-cards/joao-silva/msi-afterburner-bypass-allows-you-to-overclock-gddr7-memory-on-rtx-50-gpus/
- MSI: https://www.msi.com/blog/rtx-5070-5060ti-overclocking-undervolting-guide-with-msi-afterburner-part-1 e -part-2
- Curva plana por placa: https://nvidiareview.com/msi-afterburner-flatten-curve/
- RTX 4060: https://tier1settings.com/rtx-4060-undervolt-guide/
- RTX 5060: https://evezone.evetech.co.za/quick-bytes/how-to-undervolt-your-rtx-5060-step-by-step-guide
- Banco de resultados: https://techfuelhq.com/articles/gpu-undervolt-settings-database/
- Radeon: https://techfuelhq.com/articles/rx-9070-xt-undervolt-2026/
- Driver 595.71: https://www.notebookcheck.net/Nvidia-nerfs-voltages-on-RTX-GPUs-with-latest-driver-update-leading-to-lower-performance.1241165.0.html
- GitHub: https://github.com/Rovey/gpu-auto-optimizer, https://github.com/vuplea/simple-nvidia-undervolt, https://github.com/aufkrawall/green-curve
