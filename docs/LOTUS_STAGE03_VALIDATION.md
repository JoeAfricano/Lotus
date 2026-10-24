# Lotus — Etapa 03: GPU, sensores e marca pixelada

Data: 23/10/2026, America/Sao_Paulo. Base Git: a3e916a. Solução preservada; nenhum projeto recriado ou excluído. Aplicativo x64 e bibliotecas/testes Any CPU na solução. Checkpoint de implementação registrado no relatório da entrega; pendências visuais permanecem explícitas, sem declarar aprovação integral.

## Estado

Implementação concluída em código; validação visual final pendente. A automação Windows não iniciou nesta sessão: node_repl retornou "windows sandbox failed: helper_unknown_error: setup refresh had errors", inclusive após recuperação/reset. Não foram capturadas screenshots nem árvore de acessibilidade atuais. O usuário confirmou cards presentes e preferiu a versão compacta. Pediu numeração nos gráficos e posteriormente forneceu referências mais específicas para a marca, incorporadas após o início da medição prolongada. Esses últimos ajustes precisam de conferência na janela real.

Não declarar a Etapa 03 integralmente aprovada enquanto os gates visuais pendentes abaixo não forem verificados.

## Entregas

- Adaptador NVIDIA NVML somente leitura, usando biblioteca do driver instalado por caminho absoluto.
- Inventário por UUID/nome/fabricante, várias GPUs no contrato/UI; somente uma NVIDIA disponível no ensaio real.
- Utilização, clocks gráfico/memória, temperatura GPU, potência/limite, ventoinha percentual, memória v2 e eventos de clock.
- Cada sensor: unidade, status, fonte/função, horário UTC de aquisição e erro nativo quando disponível.
- CPU/RAM mantidos; composição serial no mesmo worker, isolamento de falha em ambas as direções, Stop após último consumidor.
- Histórico GPU/VRAM de até 60 pontos/60 s; indisponibilidade vira lacuna, não zero.
- Visão compacta solicitada pelo usuário: quatro cards principais, temperatura/potência complementares, detalhes técnicos expansíveis.
- Eixos explícitos 0/50/100% e −60/−30 s/agora; altura total de cada gráfico 90 DIP, incluindo eixo temporal.
- Palavra LOTUS e flor em geometria pixelada nativa, bloom e 12 partículas por marca, ciclo de 6 s. Fonte visual tem apenas o tamanho exibido. Sem sequência de imagens, fonte adicional ou pacote gráfico.
- Recursos pt-BR/en-US; movimento reduzido, alto contraste, compactação e minimização respeitados na implementação.

## Compilações e testes

Restore Debug/x64 e Release/x64 executados; nenhuma dependência nova.

Debug/x64: build final aprovado, 0 erros/0 avisos.
Release/x64: build final aprovado, 0 erros/0 avisos; 12,10 s. O aplicativo da medição foi encerrado normalmente antes de compilar para evitar bloqueio do executável.

Suítes Debug e Release: 62 aprovados em cada uma, 0 falhas, 0 ignorados. São 42 resultados anteriores e 20 novos. Em máquinas sem NVML, NvmlFact marca os testes de integração como ignorados, em vez de produzir aprovação sem consulta. Aqui os dois testes de hardware foram efetivamente executados com RTX 5060.

Ensaio prolongado adicional do mesmo teste de integração: 900,105 s, 900 amostras, zero falhas, histórico máximo GPU 60 e histórico CPU final 60. Cancelamento encerrou o worker e novas leituras cessaram. Resultado TRX stage03-soak-15min.trx e JSON artifacts/etapa03/native-soak-15min.json.

Cobertura: limites/unidades, ausência sem valor fictício, códigos nativos, identidade/horário, múltiplas GPUs, desaparecimento, idioma, histórico temporal/quantitativo, falha de GPU preservando CPU e vice-versa, recuperação, cancelamento, Stop/reaquisição e integração real de NVML.

Comandos principais:

```powershell
dotnet restore Lotus.slnx -p:Platform=x64
dotnet restore Lotus.slnx -p:Configuration=Release -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build Lotus.slnx -c Release -p:Platform=x64 --no-restore
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug --no-build --logger trx
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Release --logger trx
$env:LOTUS_GPU_SOAK_SECONDS = '900'
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~NativeGpuContinuousSessionKeepsHistoryBoundedAndReleases" --logger trx
```

A compilação intermediária da nova marca identificou uma referência de cor incorreta; corrigida para Color.FromArgb. Erros XAML subsequentes eram decorrentes da ausência de assembly nessa compilação e desapareceram após a correção. O build Debug final passou. A falha inicial não foi tratada como sucesso. Ao tornar a ausência de NVML um skip explícito, surgiu CA1416 no teste Windows; corrigido com a anotação de plataforma. Uma repetição revelou corrida no teste anterior MissingSnapshotsNeverDisplayInventedValues: ele esperava SampleCount antes da entrega ao dispatcher. Corrigido para aguardar trabalho de UI enfileirado. A implementação de coleta não foi alterada por essa correção de teste; as duas suítes finais passaram novamente.

## Hardware e leituras reais

NVIDIA GeForce RTX 5060, driver 617.42 (Windows 32.0.16.1742). Ryzen 5 5500 e 12 processadores lógicos. Snapshot de integração real às 21:48:32 locais:

| Métrica | Leitura daquela amostra |
| --- | --- |
| GPU | 0% |
| Clock gráfico | 2.520 MHz |
| Clock memória reportado | 14.201 MHz |
| Temperatura GPU | 54 °C |
| Potência | 22,473 W |
| Limite configurado | 145 W |
| Ventoinha | 0% |
| VRAM reportada total | 8.546.942.976 bytes (7,96 GiB) |
| VRAM alocada reportada | 696.709.120 bytes |
| VRAM não alocada | 7.582.846.976 bytes |
| VRAM reservada | 267.386.880 bytes |
| VRAM percentual | 8,15156% |
| Eventos de clock | 0x400; sem inferir limitação térmica/energética a partir desse bit |

As leituras são instantes, não benchmark ou certificação de precisão. Novas execuções podem produzir números diferentes. NVML e nvidia-smi compartilham fonte; sua comparação verifica consistência do adaptador, não independência física.

Comparação oficial Windows às 22:01:15 locais: contador GPUAdapterMemory com instância de uso dedicado 1.025.798.144 bytes (~978 MiB); nvidia-smi em instante próximo reportou 965 MiB alocados, 255 MiB reservados, 55 °C e 22,25 W. Engines Windows sem utilização positiva e NVML 0%. Duas instâncias LUID foram retornadas, uma com DedicatedUsage zero; não se estabeleceu vínculo UUID-LUID para todas e não foram contadas como duas GPUs físicas. Não exigir igualdade entre contabilizações/janelas de amostragem diferentes.

Evidências: artifacts/etapa03/native-gpu-snapshot.json, native-soak-15min.json e windows-gpu-comparison.json. A consulta de módulos do processo confirmou nvml.dll carregada a partir de System32.

## Consumo e estabilidade

Método: tools/Measure-LotusProcess.ps1, 1 amostra/s, CPU normalizada pelos 12 processadores lógicos, working set físico e commit privado separados; handles e threads pelo processo. Não inclui DWM/GPU, outros processos ou overhead de observadores.

Repouso inicial, PID 20808, 60,520 s: CPU acumulada sem aumento mensurável (0,000% segundo o contador do processo), working set médio 137,045 MiB, commit privado médio 159,221 MiB, handles 1071→1068, threads 56→53. Não interpretar a quantização do contador como custo zero total de renderização.

Medição contínua do Lotus PID 7028: **900,092 s, 894 amostras de processo, CPU média 0,018951% e pico de intervalo 1,295794%; working set médio 184,290 MiB (177,719–190,656 MiB); commit privado médio 202,390 MiB; threads 58→52 e handles 1112→1108**. As 894 amostras do observador externo diferem das 900 amostras do teste do coletor; são processos/temporizadores distintos, com custo e agendamento próprios. A janela foi mantida em Monitoramento pelo usuário; logs de início em 21:58:10, sem parada durante os 15 minutos. Encerramento solicitado às 22:14:38: logs confirmaram Monitoring stopped seguido de Application closed; o processo terminou antes de recompilar. Tema Midnight, pt-BR e movimento habilitado. Houve teste de integração NVML em outro processo durante parte do período; a amostragem do Lotus mede apenas o PID alvo. Não houve jogo ou carga sintética de GPU criada pelo agente.

O ensaio de 15 minutos do processo usa a interface compacta anterior aos eixos e ao redesenho final da marca. Esses ajustes foram preparados em arquivos enquanto o executável continuava aberto, para não interromper a amostragem. Medir a revisão final separadamente. A mesma coleta de GPU permanece, exceto correção do estado exibido durante retry após falha de enumeração, caminho que não foi acionado no ensaio.

Sessão final da marca, PID 13108, 60,118 s, sem consumidor de telemetria registrado: CPU do processo sem incremento mensurável (0,000% com a resolução usada), working set médio 142,601 MiB e commit privado 160,687 MiB; handles 1053→1041 e threads 52→45. Não se observou visualmente a animação durante o ensaio; isso não certifica custo GPU/DWM. Artefato release-final-visual-session-summary.json.

Comparação histórica da Etapa 02: 0,274046% CPU média e 179,021875 MiB de working set durante 60,342 s de monitoramento. Duração, interface e contexto diferem; não atribuir causalidade nem alegar ganho de desempenho por comparar diretamente os valores.

Working set inicial/final: 180.480→185.961 MiB; houve oscilações e uma diferença positiva. Handles/threads não cresceram entre os extremos. Não há evidência suficiente para declarar ausência de vazamentos em toda configuração. O ensaio observa apenas esta máquina, driver e intervalo. Testes sob jogo/carga, outras GPUs, suspensão, hotplug e execução de várias horas continuam pendentes.

## Execução e gates manuais

Processos Release iniciaram e responderam; logs registraram navegação até Monitoramento, início e encerramento normais em instâncias anteriores. O usuário confirmou cards reais presentes. Nenhuma exceção de UI foi encontrada nas sessões atuais no acompanhamento disponível.

Pendentes na revisão visual final:
- Conferir palavra/flor pixeladas realmente animadas e respeitando redução de movimento.
- Conferir eixos numéricos em CPU/RAM/GPU/VRAM e expanders.
- Repetir alternância entre páginas, minimizar/restaurar, idiomas/temas e janela pequena/maximizada.
- Captura visual, UIA, leitor de tela e DPI adicional não verificados nesta sessão.
- Encerramento da última revisão verificado às 22:19:32: zero processos Lotus residuais, logs somente Information. O aplicativo foi reaberto para conferência (nova instância intencional).

Ausência de automação visual não foi mascarada como aprovação. As pendências visuais impedem declarar a etapa integralmente concluída.

## Limitações e segurança

Temperatura do Ryzen, RPM, hotspot e temperatura de VRAM indisponíveis/não implementados. NVAPI e LibreHardwareMonitor não foram incluídos; não se instalou driver adicional. AMD/Intel ainda precisam de adaptadores. Resultados multi-GPU foram testados com fakes somente nos testes, não em hardware múltiplo local. Uma chamada de driver em andamento não pode ser abortada por CancellationToken; o cancelamento ocorre entre chamadas.

Nenhum controle de clock, tensão, ventoinha, potência, Registro, serviço ou proteção do Windows. Nenhuma otimização, RTSS, Afterburner, diagnóstico ou benchmark implementado. NVML é proprietária do driver; nenhum SDK/binário NVIDIA foi redistribuído. Zero pacote NuGet novo.

## Arquivos alterados

README.md; documentos LOTUS_ARCHITECTURE, ROADMAP, PERFORMANCE_VALIDATION, EXTERNAL_INTEGRATIONS, TECHNICAL_DECISIONS e TELEMETRY; novos LOTUS_GPU_TELEMETRY e LOTUS_STAGE03_VALIDATION.

App: App.xaml.cs, BrandPulse.cs, MainWindow.xaml/.cs, Controls/MonitoringPanel.xaml, TelemetryChart.cs; novos GpuPanel.xaml/.cs.
Core: recursos Strings.resx/Strings.en-US.resx, TelemetryModels, HardwareMonitoringService e MonitoringViewModel; novos GpuTelemetry e GpuViewModel.
Infrastructure: novo Telemetry/NvmlGpuTelemetryProvider.cs.
Tests: novo GpuTelemetryTests.cs e correção de sincronização em TelemetryTests.cs.
Tools: Measure-LotusProcess.ps1 (até 3.600 s e CSV de progresso).

Artefatos de ensaio e TRX ficam locais/ignorados no Git. Backup preexistente Lotus.slnx.backup-20261021-200919-583 preservado, fora do checkpoint. Nenhuma alteração em Lotus.slnx ou nos .csproj.

## Próximo passo

Concluir a conferência visual desta etapa. Depois, somente com nova autorização, ampliar homologação e decidir o próximo incremento. Não iniciar diagnóstico, RTSS, benchmark ou otimizações automaticamente.


## Correção posterior da marca — 23/10/2026

Causa estática confirmada: política global do Windows bloqueava as animações. Prova ampliada da flor confirmada visualmente pelo usuário. Correção nativa e varredura do nome implementadas; Debug/Release com zero avisos/erros e 62 testes aprovados em cada configuração. Essa rodada não substitui conferência visual do nome/estados da janela, ainda pendente por falha da ferramenta de captura. [Evidências, política local, medição e limites](LOTUS_BRAND_MOTION_DIAGNOSIS.md). Nenhuma alteração nos coletores ou controles do hardware.
