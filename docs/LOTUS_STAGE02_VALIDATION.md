# Lotus — Etapa 02: validação de CPU/RAM e neon

Data: 21/10/2026, America/Sao_Paulo. Base Git `408cfd0`. Solução preservada, aplicativo x64 e bibliotecas Any CPU. Nenhum pacote novo, instalação, serviço, driver, elevação do aplicativo ou alteração do Windows.

## Entrega

Monitoramento somente leitura de CPU e RAM na seção Monitoramento, fontes identificadas, atualização aproximada de 1 s, status e último horário, gráficos reais de até 60 s, métricas do processo Lotus e recursos pt-BR/en-US. Ausência é travessão/status; não há números simulados na aplicação. Dashboard não adquire coleta. Frequência efetiva permanece indisponível. Neon com gradiente luminoso móvel nas letras e brilho; movimento reduzido mantém a marca estática.

Separação: Core (snapshots, matemática, histórico, serviço e VM), Infrastructure (Win32/registro somente leitura), App (DI, dispatcher, ciclo da janela, gráficos XAML e composição neon), Tests (fakes apenas nos testes e integração Windows real). [Contrato, fontes, fórmulas e limites](LOTUS_TELEMETRY.md).

## Build e testes finais

| Verificação | Resultado |
| --- | --- |
| Restore Debug/Release x64 | Aprovado, versões de pacotes inalteradas |
| Debug/x64 | 0 erros e 0 avisos; 10,07 s |
| Release/x64 | 0 erros e 0 avisos; 10,11 s |
| Testes Debug | 42 aprovados; 0 falhas/ignorados; TRX 22:16:47; 594 ms |
| Testes Release | 42 aprovados; 0 falhas/ignorados; TRX 22:16:49; 616 ms |

18 resultados novos além dos 24 da fundação. Cobrem percentuais, unidades binárias, valores inválidos, primeira CPU/reset, histórico temporal/quantitativo, ciclo por consumidor, ausência, fila de UI desativada, cancelamento durante leitura, release/reacquire sem sobreposição e recuperação do provedor. Teste Windows real leu modelo/topologia/RAM/working set; seu snapshot é do processo de testes, não do Lotus. Não se simula falha de kernel na máquina do usuário; cenários de erro são exercitados por fakes nos testes.

Comandos executados da raiz:

```powershell
dotnet restore Lotus.slnx -p:Platform=x64
dotnet restore Lotus.slnx -p:Configuration=Release -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build Lotus.slnx -c Release -p:Platform=x64 --no-restore
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug --no-build --no-restore --logger trx --results-directory artifacts/etapa02/tests-debug
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Release --no-build --no-restore --logger trx --results-directory artifacts/etapa02/tests-release
```

## Execução e comparações observadas

Windows 11 Pro 10.0.22621, Ryzen 5 5500, 6 núcleos físicos/12 lógicos, 15,871742 GiB de RAM utilizáveis pelo Windows. SDK .NET 10.0.401; janela inicial 1280 × 820, monitor local 1920 × 1080, escala 100%.

- Debug e Release iniciados no Windows; leitura CPU aquecendo na primeira amostra e valores nas seguintes. Exemplos observados: CPU 0,5%, 0,8% e 3,2%; RAM 66,2–67,5%, 15,87 GiB total, cerca de 10,5–10,7 GiB utilizados e 5,2–5,4 GiB disponíveis. São instantes distintos, não estatísticas de benchmark.
- Consulta oficial independente: Win32_Processor confirmou Ryzen 5 5500/6/12. Em 22:08:58, Win32_OperatingSystem informou 15,871742 GiB total, 5,198906 GiB disponíveis/67,244% usados; contador Win32_PerfFormattedData_PerfOS_Processor `_Total` indicou 2%. A UI em instantes próximos mostrou 66,7–67,5% RAM e 3,2% CPU às 22:09:09. Comparação aproximada, sem sincronização estrita, com janelas de atualização e precisão distintas; não é ensaio de calibração nem alegação de igualdade amostra a amostra.
- CPU do Lotus observada na UI e working set/commit separados; gráficos acumularam mais de um minuto de leituras e descartam pontos antigos. Lacunas não são preenchidas.
- Navegação Dashboard/Monitoramento/Configurações exercitada. Logs mostraram início/parada por demanda. Minimização em Debug e Release suspendeu o worker; restauração produziu aquecimento novo. Idioma e tema trocados ao vivo, neon estático/animado alternado e página nova legível na janela mínima com rolagem e em tela maximizada.

## Problema reproduzido e correção

Um protótipo do neon usava CompositionMaskBrush com GetAlphaMask. Após minimizar/redimensionar/reabrir a sidebar, o WinUI retornou brush incompatível e houve exceção fatal às 22:13:53. O registro foi preservado. Correção: gradiente no Foreground via XamlCompositionBrushBase; máscara apenas no DropShadow, sem descartar a máscara emprestada pelo XAML, inicialização após tamanho válido e reavaliação após layout. Falha decorativa tem fallback para a cor original e warning, sem encerrar o app. Medições anteriores à correção ficam identificadas como intermediárias; não representam a versão final.

Também foram corrigidos a ambiguidade C# de Path e o alinhamento da legenda temporal. Build com a instância antiga aberta encontrou arquivo bloqueado; fechamento normal liberou a saída, sem apagar arquivos ou recriar a solução.

Reteste da correção em Release: recolher/expandir sidebar, reduzir para 600 × 480, minimizar, restaurar e maximizar novamente foi executado em 22:17–22:21. A falha das 22:13:53 não reapareceu; até a conferência das 22:21:39 os logs da nova sessão continham somente Information, sem fallback/warning, Error ou Fatal.

## Consumo medido

| Modo final | Duração real / amostras | CPU média total | Pico por intervalo | Working set médio (faixa) | Commit privado médio |
| --- | --- | --- | --- | --- | --- |
| Dashboard sem coleta | 60.34 s / 60 | 0.129% | 1.154% | 156.4 MiB (154.2–158.5) | 173.8 MiB |
| Monitoramento ativo | 60.34 s / 60 | 0.274% | 1.803% | 179.0 MiB (171.5–192.0) | 191.4 MiB |

Medição corrigida, PID 20368: repouso 22:17:28–22:18:28; monitoramento 22:19:17–22:20:18. Handles 1129→1120 no repouso e 1160→1167 no monitoramento; threads 56→49 e 54→54. Isso não prova ausência de leak em sessões longas. Resultados anteriores (release-idle/release-monitoring) são intermediários, anteriores à correção do neon; os arquivos válidos para esta tabela têm prefixo release-final-. A medição ativa inclui o início da coleta, além da atualização contínua.
 Ferramenta própria: `tools/Measure-LotusProcess.ps1`, com Get-Process, Stopwatch e amostras externas de 1 s. CPU média = diferença TotalProcessorTime / (tempo real × 12 processadores lógicos) × 100; working set e commit privado em MiB. Nenhuma leitura de performance é injetada na UI.

As capturas são sequenciais na mesma instância Release sem debugger, janela inicial visível, sidebar expandida, pt-BR, Grafite/Rosê, animações habilitadas, sem cliques ou navegação durante cada intervalo. Codex/Opera e processos usuais permaneceram no computador. Custo do observador e DWM/GPU estão fora do processo Lotus. Uma rodada por modo; sem afirmar causalidade exata, ausência de leaks ou consumo universal.

## Arquivos e evidências

- Core: `Telemetry/TelemetryModels.cs`, `HardwareMonitoringService.cs`, `MonitoringViewModel.cs`; `SectionViewModel.cs`; recursos `Localization/Strings.resx` e `Strings.en-US.resx`.
- Infrastructure: `Telemetry/WindowsHardwareTelemetryProvider.cs`.
- App: `App.xaml.cs`, `MainWindow.xaml.cs`, `BrandPulse.cs`, `UiDispatcher.cs`, `SectionPage.xaml/.cs`, `Controls/MonitoringPanel.xaml/.cs`, `TelemetryChart.cs`.
- Testes/ferramenta: `TelemetryTests.cs`, `tools/Measure-LotusProcess.ps1`.
- Documentos: README, arquitetura, roadmap, decisões, contrato de telemetria, relatório, validação de desempenho e direção visual.
- Evidências locais em `artifacts/etapa02/`: logs de build, TRX, CSV de amostras, JSON de sumários/comparação e capturas PNG observadas pela árvore de acessibilidade. Artefatos de execução ignorados no Git; fontes e documentação versionadas.
- Backup preexistente `Lotus.slnx.backup-20261021-200919-583` preservado e excluído do checkpoint desta etapa.

## Limites e próxima etapa

Não há GPU/sensores, frequência efetiva, processos externos, diagnóstico de gargalo, score, benchmark, limpeza ou otimização. CPU total em múltiplos grupos é explicitamente Unsupported; hardware local tem um grupo e não homologa o outro cenário. Inventário é lido uma vez; hotplug/topologia alterada exige novo processo. Não foram homologados DPI adicionais, múltiplos monitores, leitor de tela, alto contraste completo/dinâmico, sessão de horas ou todos os drivers/hardwares.

Próximo passo recomendado, somente após instrução: ampliar homologação e ensaios prolongados; depois diagnóstico explicável sobre memória observada, sem inferir desempenho de jogos ou aplicar ajustes. Não iniciar GPU, benchmark ou otimizações automaticamente.

## Encerramento e conclusão local

A versão final foi fechada pelo botão nativo durante a coleta às 22:23:19. Logs: Monitoring stopped às 22:23:19.326 e Application closed às 22:23:19.334. Consulta de processos às 22:23:23 confirmou **zero processos Lotus.App**. O worker pertence ao mesmo processo, não há helper ou serviço externo. Evidência: `artifacts/etapa02/shutdown-final.json`.

Sessão final 22:16:14–22:23:19: somente Information nos logs, sem warnings, Error ou Fatal. A ocorrência fatal anterior à correção não foi apagada nem considerada parte da sessão aprovada. Captura final em português: `artifacts/etapa02/monitoring-pt-release-final.png`; árvore UIA no arquivo de mesmo prefixo. A captura antiga em inglês representa a versão intermediária e não a evidência final do neon.

Etapa 02 validada no escopo local solicitado: fontes reais, UI/gráficos atualizando, pause/resume, falhas/cancelamento testados, builds/testes aprovados e encerramento sem processo residual. Sem regressão conhecida nas verificações executadas; isso não amplia a homologação de DPI/hardware além dos limites listados. Nenhum módulo seguinte foi iniciado. O identificador do checkpoint é apresentado na resposta da entrega e consultável por `git log -1 --oneline`.
