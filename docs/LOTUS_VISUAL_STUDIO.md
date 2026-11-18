# Estúdio visual — revisão após teste do jogador

15/11/2026. O usuário esclareceu que quer brilho, contraste, gamma e branco, **não saturação**. A versão inicial DVC foi insuficiente: o driver passou de 53 para 50 após o teste Natural, mas isso não comprova diferença óptica perceptível. Seus controles saíram da tela principal. O backup DVC anterior permanece acessível para restauração explícita nos detalhes.

## Jornada e controles

Ferramentas → Bônus experimentais → Estúdio visual do monitor. Quatro etapas: Sombras, Luzes, Branco e Comparar. Ícones, passos e confirmação pelo próprio jogador; não há nota inventada de precisão ou etapa automaticamente "aprovada". Vitrine de Ferramentas com ícones em badges, cabeçalhos por categoria, cards mais espaçosos e correção do registro das grades em `wraps`, que deixava o ajuste de colunas sem efeito.

- Brilho e contraste físicos via DDC/CI, somente se declarados e lidos pelo monitor. Alternativas digitais pela LUT quando o controle físico não existe e a curva SDR é suportada.
- Gamma de meios-tons, temperatura relativa e matiz via curva SDR do Windows, composta com a curva original; não substitui a curva existente por uma linha arbitrária. Os números são correções relativas, não uma medição de gamma 2.2 ou de temperatura em Kelvin.
- Não modifica saturação. Não automatiza nem move os sliders do painel NVIDIA. A interface e os limites explicam a diferença entre os controles físicos e a curva do Windows.
- "Começar ajuste" exige confirmação. Depois, sliders alteram a imagem real do monitor, com coalescência de 250 ms e operações serializadas. Não há filtro falso só na imagem de teste.
- Preview desfeito após 30 segundos sem ajuste e limitado a 3 minutos. Revisão do timer impede callback antigo de desfazer um teste mais recente. "Ver antes" compara a configuração integral original; ao manter, a versão ajustada é reaplicada e verificada primeiro.
- Backup durável com flush e substituição atômica antes de cada escrita. Guarda original, último confirmado, estado anterior do preview e transição de atualização parcial. Restaurar não apaga backup diante de falha, desconexão ou conflito. Recuperação no início só de previews inacabados; nenhuma reaplicação periódica durante jogos.
- Detecta identidade estável, clone de saídas, HDR/Advanced Color e capacidades. Nenhuma DLL externa ou driver instalado. Não muda resolução, Hz ou arquivo ICC. Releituras verificam o resultado; gamma tolera no máximo 256 unidades em 65535 para quantização de um nível de 8 bits.

## Pesquisa e limitações

[GetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-getmonitorbrightness), [SetMonitorContrast](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorcontrast): DDC pode ser incompleto em monitores e exige validação física; não é uma configuração ideal universal.

[SetDeviceGammaRamp](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp): API legada com limitações importantes. Pode devolver TRUE e ignorar a curva; por isso há releitura. HDR é bloqueado. Jogos, eventos de display e outros calibradores podem substituir a LUT. A Microsoft recomenda ICC para calibração de precisão. Este bônus é **personalização experimental SDR**, não substitui colorímetro/ICC nem garante persistência da LUT após mudar Hz ou abrir todos os jogos. Não há polling que lute contra outro programa nem teste A/B de impacto em FPS.

Leitura local sem escrita em `artifacts/display-calibration-readonly.log`: Samsung LF24T35, brilho 65/100, contraste 60/100, gamma disponível, AdvancedColor=false. As leituras confirmam suporte anunciado, não a eficácia óptica.

## Imagens e prompts

Ferramenta utilizada: **imagegen integrada**, sem CLI/API própria. Duas gerações iniciais e duas edições, adotadas após o usuário pedir estilo medieval realista e descartar desenho. Assets finais copiados para o projeto, sem depender da pasta temporária ou de rede:

- `src/Lotus.App/Assets/ColorCalibration/medieval-shadows.png`
- `src/Lotus.App/Assets/ColorCalibration/medieval-daylight.png`

Prompt final sombras: "Transform it into a photorealistic premium medieval RPG castle observatory reference scene for a monitor adjustment wizard. Preserve the wide composition, luminous textured snowy mountains, fine stone texture, deep shadow details and realistic restrained lighting. Replace the large brass telescope and any modern instruments with a medieval brass armillary sphere, wooden astrolabe, parchment and antique books. Keep dark leather, charcoal cloth and a small warm lantern. Photorealistic medieval game environment, NOT a drawing or cartoon. No UI, no text, no logo or watermark. Do not crush blacks or blow out snow details, no exaggerated cinematic color tint."

Prompt final luz natural: "Transform all clothing and objects into a premium photorealistic MEDIEVAL RPG world, NOT a drawing/cartoon. Preserve the adult woman's natural skin, balanced soft cloudy daylight, alpine lake, white cloth and ceramic cup, naturally colorful fruit, gray stone and silver metal. Replace modern jacket with a muted olive medieval wool cloak over an ivory linen tunic; remove the wristwatch and modern buckles/equipment, replace the compass with a small medieval silver goblet. Tents medieval canvas and natural leather satchel. Preserve visible detail in white linen, skin and dark foliage. Realistic restrained colors, neutral white balance, no rose or teal/orange grading. Wide landscape composition. No UI, no text, no logo or watermark. Illustration for visual comparison, not a calibrated reference photograph."

As imagens geradas não têm valores colorimétricos de referência. Símbolos e níveis de preto/branco são construídos por código, sem solicitar valores RGB exatos à IA. Decodificação limitada a 1600 px e imagem trocada só ao mudar a cena.

## Validação

35 testes direcionados aprovados inicialmente em Release/x64: transformação sem acumulação, monotonicidade e limites, preservação da curva, DDC e fallback, conflito, falha parcial e rollback, snapshot explícito para restauração, JSON integral, timeout e callbacks antigos. Drivers/relógio simulados nestes testes, arquivos temporários isolados. Compilação WinUI Debug/x64 inicial sem avisos/erros. Validação completa e ensaio físico registrados abaixo ao concluir.

Validação final em 15/11/2026:
- Suite completa: 883 aprovados em Debug/x64 e 883 em Release/x64, sem falhas ou testes ignorados. Evidências: `tests/Lotus.Tests/TestResults/calibration-studio-debug.trx` e `calibration-studio-release.trx`.
- WinUI compilado novamente em Debug/x64 e Release/x64: zero avisos/erros, incluindo a remoção do antigo padrão de cores não utilizado. Logs: `artifacts/calibration-app-debug.log` e `calibration-app-release.log`.
- `git diff --check` passou nos componentes alterados desta entrega.
- Na primeira entrega, o ensaio físico aguardava confirmação. O resultado da execução posterior autorizada está registrado abaixo. Nenhuma eficácia óptica foi medida.
- Release de desenvolvimento iniciado maximizado com `--dev-instance`, sem gerar instalador. A inspeção visual automática foi bloqueada pelo identificador de janela da ferramenta: `window id 3933900 no longer belongs to ...Lotus.App.exe; current owner is ...Lotus.App.exe` (mesmo caminho reportado). Não se declara validação visual aprovada.


## Cenas por etapa e teste físico posterior


Cada etapa usa agora uma ambientação distinta: observatório escuro (sombras), mosteiro nevado (luzes), aventureira junto ao lago (branco/pele) e mercado medieval costeiro (comparação geral). As duas novas imagens foram geradas pela ferramenta imagegen integrada, modo generate, sem imagem de referência, fundo opaco. Assets `src/Lotus.App/Assets/ColorCalibration/medieval-highlights.png` e `medieval-comparison.png`.

Prompt luzes: Photorealistic premium medieval RPG environment, wide landscape image for the highlights step of a monitor adjustment wizard. A bright snowy mountain monastery courtyard under soft overcast daylight. Detailed white snowdrifts, textured ivory stone, carved arches, pale linen banners, silver armor resting beside dark wooden doorway, subtle distinct shadow detail. No people. Balanced neutral exposure, visible texture in whites, realistic natural colors, no dramatic color grading, no blown highlights. Completely different environment from an indoor observatory or lakeside portrait. Not a drawing or cartoon. No UI, text, logo, watermark. Illustrative visual comparison scene, not a measured calibration reference.

Prompt comparação: Photorealistic premium medieval RPG environment, wide landscape image for the final overall comparison step of a monitor adjustment wizard. A bustling sunlit medieval coastal market square, eye-level wide view: baskets of apples lemons and leafy vegetables, deep red cloth awning, muted blue and olive fabrics, white linen, varied natural skin tones on a few adult merchants, gray stone pavement, bronze vessel, dark shaded timber stalls and blue sea visible through a distant arch. Soft neutral daylight, realistic restrained colors, balanced exposure preserving highlights and shadows. Rich varied textures for comparing the whole monitor appearance. Clearly different from snowy monastery, dark indoor observatory or alpine lake portrait. Not a drawing or cartoon. No UI, text, logo or watermark. Illustrative scene, not a measured calibration reference.

Ensaio físico autorizado e executado uma vez: `artifacts/calibration-physical-smoke.json`. Resultado de aplicação `DriverError`, sucesso=false. O controlador reverteu e a leitura posterior confirmou brilho 65, contraste 60 e curva original (tolerância 256/65535); arquivo de recuperação vazio após restauração. A leitura feita pelo ensaio ocorreu após o rollback: não permite identificar qual chamada nativa falhou nem comprovar os valores intermediários. Não se declara aplicação física aprovada ou precisão óptica medida. Não foram repetidas escritas sem diagnóstico concreto.


Após separar as quatro cenas, Debug/x64 e Release/x64 recompilados com zero avisos e erros (`artifacts/calibration-scenes-debug.log`, `artifacts/calibration-scenes-release.log`). Os quatro assets foram conferidos distintos por SHA256 e copiados para a saída Release. Os 883 testes anteriores continuam válidos para o núcleo, que não mudou neste incremento visual. Lotus reaberto para revisão manual; sem instalador.
