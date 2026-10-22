# Lotus — Validação de desempenho

Revisão: 21/10/2026. Protocolo proposto, ainda não implementado. Os resultados de build/test ao final são reais; não foi executado benchmark de jogo nesta auditoria.

Atualização da fundação: a [Etapa 00](LOTUS_STAGE00_AUDIT.md) acrescentou builds limpos e seis testes aprovados tanto em Debug/x64 quanto Release/x64, com execução visual nas duas configurações e encerramento normal. Zero registros Fatal nas sessões auditadas. Nenhuma medição de desempenho de jogo ou consumo foi realizada; esses critérios continuam propostos, não aprovados.

## Contrato de captura

Salvar versão Lotus, regra/estatística/provedor, processo e início do processo, executável e versão do jogo, cenário/replay, início e duração efetiva, contagem de frames válidos e eventos perdidos. Registrar CPU/GPU/RAM, Windows/build, drivers, resolução, preset, modo de janela, API gráfica, monitor/Hz, cap, VRR, V-Sync, Reflex/alternativa quando disponível, frame generation, upscaling, energia AC/bateria e estado térmico observável. Campo desconhecido permanece desconhecido e reduz comparabilidade.

Separar frames apresentados dos efetivamente exibidos, descartados e gerados. Não combinar swap chains ou processos silenciosamente. Registrar campo original e unidade do provedor; não chamar GPU Busy de uso global da GPU. Tempos relativos usam relógio monotônico; UTC serve para correlação entre sessões. Dados originais imutáveis, com hash, filtros e transformações documentados.

PresentMon documenta diferenças entre saída CSV, versões de métricas e opções de captura; o adaptador deverá declarar o schema aceito e rejeitar incompatibilidades. [Console oficial](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md). O consumidor ETW também documenta caminhos de apresentação não distinguíveis e limitações de composição; nem toda aplicação permite a mesma interpretação. [Implementação consultada](https://github.com/GameTechDev/PresentMon/blob/main/PresentData/PresentMonTraceConsumer.hpp).

## Estatísticas Lotus propostas

Para N intervalos válidos positivos `t[i]` em milissegundos, de uma sequência homogênea:

- FPS médio = `1000 * N / soma(t[i])`. Não usar média aritmética dos FPS instantâneos.
- Frametime médio/mediano e P95/P99 em ms. Percentil: ordenar crescentemente e usar nearest-rank `ceil(p*N)`, índice 1-based; publicar o algoritmo para não confundir diferenças de interpolação.
- “1% low médio” = `1000 / média(dos ceil(0,01*N) maiores frametimes)`. Identificar explicitamente que é cauda por contagem de frames. Não confundir com `1000/P99` ou quantil de FPS. Suprimir interpretação da cauda com menos de 100 amostras nessa cauda; esse mínimo é política inicial Lotus, não garantia estatística universal.
- Zero, negativo, NaN, intervalo corrompido e lacuna de captura não entram na estatística; preservar motivo e contagem. Não preencher com último valor nem interpolar frames perdidos.
- Pausas/carregamentos só podem ser excluídos por regra definida antes do teste; exibir resultado bruto e recorte. Não remover automaticamente os piores frames para melhorar números.
- Heurística experimental de stutter: intervalo acima de `max(50 ms, 3 * mediana dos 2 s anteriores válidos)`; aquecimento sem janela completa não classificado. Agrupar intervalos consecutivos em episódios; relatar contagem, duração e percentual de tempo. Limiar é versionado e ajustável, não definição universal de percepção humana.

O código consultado do CapFrameX distingue quantis, low averages e métricas integrais. A comparação entre ferramentas precisa igualar definições e filtros, não apenas rótulos. Não foi copiada implementação. [FrametimeStatisticProvider](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.Statistics.NetStandard/FrametimeStatisticProvider.cs).

## Experimento antes/depois

1. Definir hipótese, uma alteração, métrica primária, limiar de relevância prática e critérios de interrupção antes de coletar.
2. Fixar cena, duração e condições. Preferir replay/benchmark interno repetível. Aquecer jogo e caches sem apagar shader cache. Registrar temperatura e processos interferentes.
3. Começar com pelo menos cinco execuções por condição quando viável, alternando/contrabalanceando A/B e revalidando o estado entre execuções. Proposta inicial: 60–120 s por captura, ajustada ao cenário. Três ou menos execuções são exploratórias; cinco não garantem poder estatístico.
4. Analisar cada execução. Relatar mediana, dispersão, número de pares e deltas absolutos/relativos. A unidade independente é a execução, não cada frame autocorrelacionado.
5. Quando a amostra permitir, bootstrap de pares/execuções para intervalo de confiança de 95%; preservar a estrutura temporal. Não usar milhões de frames como milhões de repetições independentes. Declarar limitações de n pequeno e análise de múltiplas métricas.
6. Delta FPS `% = 100*(B-A)/A`, para A positivo. Para frametime menor é melhor, mostrar também ms e convenção de sinal. Base nula/ausente não permite porcentagem.
7. “Melhoria mensurável neste cenário” exige efeito acima do limiar pré-definido, incerteza compatível com melhora e ausência de regressão relevante em fluidez, estabilidade ou temperatura. Caso contrário: inconclusivo, sem diferença detectável ou regressão. Nunca generalizar a todos os jogos.

## Diagnóstico e Frame Pacing

Recomendação deve guardar regra/versão, evidências, hipótese, confiança qualitativa e razões, alternativas, risco, benefício esperado e plano de validação. Confiança não é porcentagem inventada.

CPU/GPU: correlacionar carga por núcleo, duração de trabalho GPU, cap, esperas e frames. CPU total baixa não elimina limitação em uma thread; GPU baixa pode refletir cap ou espera. RAM: combinar memória disponível, commit, paginação e I/O; cache ocupado sozinho não é desperdício. VRAM alocada não prova residência nem pressão. Térmico/energia: temperatura isolada não prova throttling; exigir flags ou evidência temporal coerente. Drivers: versão antiga não implica causa do problema; recomendar teste direcionado e rollback, sem atualização automática.

FPS cap, VRR, V-Sync e Reflex serão comparados por jogo e monitor. Testar uma combinação por vez, registrar suporte e efeitos em latência/tearing. Não impor “Hz menos 3”, HAGS, prioridade ou plano de energia como regra universal. Medida de frametime não equivale a latência ponta a ponta; esta exige fonte e metodologia próprias.

## Custo do próprio Lotus

Metas iniciais de engenharia, ainda NÃO medidas: CPU média total normalizada abaixo de 1% em repouso/monitoramento básico; memória privada estável abaixo de 250 MiB; início quente até janela utilizável p95 abaixo de 2 s em máquina de referência. Medir pelo menos dez inicializações e 30 minutos de repouso, incluindo slope de memória, handles e threads. Revisar metas com hardware de referência identificado.

Medir jogo com Lotus fechado, aberto sem coleta, com coleta, com captura e com overlay separadamente. Proposta: custo em FPS médio inferior a 1% e sem regressão de cauda além da variação do cenário; se não houver precisão para demonstrar esse orçamento, resultado é inconclusivo. Registrar frequência de coleta, alocações, I/O e perdas. Usar WPR/WPA em sessões diagnósticas específicas, contabilizando também seu overhead. [WPT oficial](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/).

## Testes futuros exigidos

Vetores sintéticos apenas em testes, com resultados analíticos conhecidos: sequência constante, picos, empates, captura curta/vazia, divisão por zero, cancelamento, locale, schema desconhecido e truncamento. Fixtures reais autorizadas e anonimizadas para replay de parser. Bridges testados com ferramenta ausente, reinício, ABI inválida, permissão negada e dados vencidos. Otimizações testadas com aplicação parcial, falha de backup, alteração concorrente e reversão após reinício. Validar métricas equivalentes contra uma ferramenta independente sem misturar capturadores conflitantes.

## Evidência da auditoria local

Em 21/10/2026, a partir de `C:\Projetos\Lotus`:

```powershell
dotnet restore Lotus.slnx -p:Configuration=Debug -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
dotnet test Lotus.slnx -c Debug -p:Platform=x64 --no-build --no-restore --logger trx
```

Restore aprovado; build aprovado com 0 avisos e 0 erros (12,24 s); seis testes aprovados, nenhum ignorado, 37 ms reportados para os testes. TRX: `tests/Lotus.Tests/TestResults/JUNIOR_DESKTOP-41CMO49_2026-10-21_20_41_37_net10.0.trx`. O tempo de build não mede inicialização do produto.

Cobertura efetiva: estado inicial, dez rotas no ViewModel/notificação, navegação nula/desconhecida/repetida, JSON estruturado em arquivo. Não cobre sensores, XAML visual, desempenho, SQLite ou reversão. Nesta auditoria não se iniciou jogo nem aplicativo; execução visual anterior está registrada em `VISUAL-DIRECTION.md`. Release, máquina limpa, idiomas e consumo permanecem pendentes.

## Fundação visual — Etapa 01

Builds Debug/Release e 24 testes por configuração executados. Navegação, maximização/restauração, rolagem, idioma e tema imediatos inspecionados no Windows. [Comandos e limites](LOTUS_STAGE01_VALIDATION.md). Uso do compositor não equivale a ganho comprovado de FPS/consumo. Não houve benchmark de jogo, captura ETW ou medição de GPU/CPU nesta etapa.
