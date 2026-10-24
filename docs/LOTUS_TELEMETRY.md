# Lotus — CPU e RAM: contrato da Etapa 02

## Implementação

`Lotus.Core/Telemetry` contém snapshots imutáveis, `Reading<T>`, status, cálculo de deltas, histórico, `IHardwareTelemetryProvider`, `HardwareMonitoringService`, `IUiDispatcher` e `MonitoringViewModel`. Não referencia WinUI ou APIs Windows. `Lotus.Infrastructure/Telemetry/WindowsHardwareTelemetryProvider` implementa as leituras. `Lotus.App` registra singletons por DI, adapta DispatcherQueue e desenha dois gráficos XAML sem pacote adicional.

`Subscribe` adquire uma lease descartável. Um único worker Task.Run usa PeriodicTimer de 1 s e chama o provedor serialmente. Sem consumidor, cancela. Reaquisição durante cancelamento aguarda a leitura anterior sair antes de reiniciar; não há workers de coleta sobrepostos. A periodicidade pode sofrer atraso do agendador; ticks atrasados são coalescidos. Não se promete precisão de relógio de 1 ms.

Somente a página Monitoramento é consumidora nesta etapa. `AppWindow.Changed` e ativação da janela reavaliam a demanda; minimizado suspende, janela visível em outra aplicação pode continuar enquanto a página Monitoramento permanece selecionada. Não há timer por card, polling da janela ou coleta do Dashboard. Trocas de idioma preservam o serviço e atualizam a apresentação.

Snapshots são enviados à DispatcherQueue com prioridade baixa, no máximo uma entrega pendente; uma amostra mais nova substitui a pendente. A versão de ativação impede aceitar callback antigo após navegar. UI recebe somente snapshots imutáveis; nenhuma P/Invoke, inventário ou espera bloqueante na thread UI. No fechamento, a aplicação cancela e aguarda o worker antes de fechar a janela; cliques repetidos não iniciam vários encerramentos.

## Métricas e fontes

| Métrica | Definição/fonte |
| --- | --- |
| Modelo CPU | Leitura única da chave de hardware Windows `HKLM/HARDWARE/DESCRIPTION/System/CentralProcessor/0/ProcessorNameString`, somente leitura. Ausência não impede outras métricas. |
| Núcleos físicos | Número de registros RelationProcessorCore de GetLogicalProcessorInformationEx. Não inferir dividindo lógicos por dois. |
| Processadores lógicos | GetActiveProcessorCount(ALL_PROCESSOR_GROUPS); não usa a contagem limitada por quota de Environment. |
| CPU total (%) | GetSystemTimes. Denominador Δkernel + Δuser; numerador denominador − Δidle. Kernel já inclui idle. Tempos em 100 ns; exige duas amostras. É ocupação por tempo, não utilidade ajustada por frequência. |
| CPU Lotus (%) | Δ(GetProcessTimes.kernel + user) / (Δtempo monotônico × lógicos ativos × 10.000.000) × 100. Normalizado pela capacidade total da máquina; não percentual de um único núcleo. |
| Frequência efetiva | Não implementada: Unsupported/null. Nenhum valor nominal é usado como frequência atual. |
| RAM total | GlobalMemoryStatusEx.ullTotalPhys: física utilizável pelo Windows; pode ser menor que capacidade instalada/reservada por firmware. |
| RAM disponível | ullAvailPhys: páginas físicas disponíveis, incluindo reaproveitáveis; não apenas páginas livres. |
| RAM utilizada | total físico utilizável − disponível. Percentual = utilizada / total × 100; não usa commit para inferir RAM. |
| Lotus working set | K32GetProcessMemoryInfo.WorkingSetSize: páginas privadas e compartilhadas residentes no processo. Não equivale necessariamente à coluna padrão Memória do Gerenciador de Tarefas, que pode mostrar working set privado. |
| Lotus commit privado | PrivateUsage: memória virtual privada comprometida; não representa RAM residente ou tamanho real de arquivo de paginação. Não somar com working set ou RAM física. |
| Tempo | CapturedAt UTC para apresentação local; Stopwatch para deltas e janela temporal, sem depender de ajuste de hora do Windows. |

Todas as capacidades são armazenadas em bytes (ulong) e formatadas em GiB = 2³⁰ bytes ou MiB = 2²⁰. Percentuais e tempo têm unidades explícitas nos nomes dos campos. Processo usa pseudohandle corrente; não há handle de processo aberto por amostra. O buffer de topologia é gerenciado e limitado a 4 MiB; registro é fechado com using. Sem WMI por amostra, drivers, contadores PDH adicionais ou elevação.

## Disponibilidade e falhas

Status: Available, WarmingUp, Unavailable, Invalid, AccessDenied, Unsupported e Error. Ausência tem Value=null; interface mostra travessão e status, nunca zero inventado. Zero legítimo/percentual arredondado continua sendo valor válido. Fonte é identificada por leitura.

CPU rejeita deltas regressivos, intervalos não positivos e resultados fora de 0–100%. Falha de GetSystemTimes invalida a linha de base para aquecer novamente; falha de RAM não altera o delta de CPU. RAM rejeita total zero ou disponível maior que total. Erros Win32 não encerram o app; acesso negado é distinto de indisponibilidade. Exceções inesperadas do provedor produzem snapshot Error e nova tentativa em ticks seguintes. Logging não grava cada leitura; registra início/parada e falha de provedor, evitando repetição de exceção contínua.

GetSystemTimes agrega somente o grupo primário da thread em máquinas com mais de 64 processadores. O provedor detecta múltiplos grupos e apresenta CPU total Unsupported; não apresenta uma porcentagem parcial como global. Essa configuração não foi homologada em hardware local. Expansão exigirá provedor de ocupação por grupo validado.

## Histórico e gráficos

Histórico em memória: no máximo 60 amostras e idade menor que 60 segundos, com tempo monotônico; reinício da coleta limpa a série para não ligar sessões separadas. Amostras ausentes ou inválidas viram lacunas. Renderização com PathGeometry/LineSegment nativos, no máximo 60 vértices por gráfico, atualização a aproximadamente 1 Hz e apenas na página ativa. Intervalos maiores que 2,5 s também quebram a linha. Eixo horizontal fixo de −60 s até agora; vertical fixo 0–100%. Nenhuma suavização inventa pontos. A linha começa quando existem amostras suficientes; não se preenche o período ainda não observado.

## Testes e extensão

Testes cobrem fórmulas, GiB/MiB, amostras inválidas, aquecimento/reset, ausências, recursos bilíngues, limite temporal/quantitativo, consumidores compartilhados, cancelamento durante leitura, release/reacquire, recuperação de falha e callbacks desativados. O teste Windows chama APIs reais e registra snapshot do processo de testes; esse working set não é confundido com o processo Lotus da validação visual.

GPU, processos externos e armazenamento poderão adicionar snapshots próprios ou provedores por capacidade. Manter leituras independentes, unidades e status, serialização/cancelamento e demanda real. Sensores lentos ou com privilégios não devem ser colocados no loop curto atual sem isolamento. Não há diagnóstico de gargalo, score, benchmark, otimização ou limpeza nesta etapa.

## Referências oficiais consultadas

- [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes) e [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes).
- [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) e [MEMORYSTATUSEX](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex).
- [GetLogicalProcessorInformationEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex).
- [GetProcessMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo) e [PROCESS_MEMORY_COUNTERS_EX](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex).
- [Compositor com XAML/GetAlphaMask](https://learn.microsoft.com/en-us/windows/apps/develop/composition/using-the-visual-layer-with-xaml): neon com máscara das letras, gradiente móvel e DropShadow; opção de movimento reduzido mantém neon estático.

Fontes e ABI oficiais consultadas; código próprio, sem copiar implementações de terceiros ou instalar bibliotecas pagas. [Resultados locais e custo medido](LOTUS_STAGE02_VALIDATION.md).

A comparação com o Gerenciador de Tarefas deve distinguir tempo de CPU de utilidade; a Microsoft documenta essa diferença em [CPU usage/Processor Utility](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/cpu-usage-exceeds-100).


## Expansão Etapa 03

CPU/RAM preservados. HardwareTelemetrySnapshot ganhou GpuTelemetryResult sem alterar o construtor existente. O serviço passa a encerrar provedores de sessão após o último consumidor. Definições adicionais em [telemetria GPU](LOTUS_GPU_TELEMETRY.md); evidências e limites em [validação](LOTUS_STAGE03_VALIDATION.md).
