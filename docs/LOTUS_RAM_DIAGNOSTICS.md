# Lotus — Diagnóstico inicial de RAM e plano para jogos

Data: 23/10/2026, America/Sao_Paulo. Base: c2ca0ce. Marca em standby, sem novo refinamento nesta entrega.

## Implementado

A página Análise agora mostra RAM física utilizável, disponível, menor disponibilidade na janela, duração observada, estado, recomendação, fonte, última leitura e critério versionado. PT-BR/en-US, temas e componentes WinUI existentes. Sem novos pacotes ou armazenamento de sessões.

`MemoryAvailabilityAnalyzer` é uma regra pura e testável. `MemoryAnalysisViewModel` recebe o stream já entregue por `MonitoringViewModel`. A navegação ativa esse stream nas páginas Monitoramento ou Análise; a análise só processa enquanto sua página está visível. Minimização e saída removem a demanda, salvo outro consumidor existente. Não há segundo loop, timer ou provedor. A mudança de idioma não acrescenta amostras repetidas. O serviço compartilhado ainda coleta o snapshot completo (inclusive GPU); separar demanda por categoria é melhoria futura, não realizada aqui.

## Regra RAM-AVAIL-001 v1

- Capacidade e disponibilidade reais: `GlobalMemoryStatusEx`, bytes convertidos para GiB (2^30).
- Baixa disponibilidade: disponível ≤10% da capacidade **e** ≤1 GiB por pelo menos 30 segundos contínuos.
- Muito baixa: disponível ≤5% **e** ≤512 MiB por pelo menos 30 segundos contínuos próprios.
- Sem condição sustentada: significa somente que esse critério não ocorreu na janela; não significa desempenho ideal, memória suficiente para qualquer jogo ou ausência de gargalo.
- Janela de evidência: últimos 60 segundos, no máximo 61 amostras. Memória ocupada ou capacidade não são somadas a commit.
- Primeira leitura inicia observação; falha, capacidade/fonte alterada ou intervalo >2,5 s recomeçam a observação. Regressão do relógio monotônico invalida a janela. Amostra duplicada não avança a duração.
- Duração contínua de baixa disponibilidade pode exceder 60 s enquanto a condição persistir. O mínimo exibido corresponde apenas à janela limitada.

Os limiares são heurísticas conservadoras de triagem do Lotus, **não limites oficiais do Windows** nem validação de impacto em FPS. São alteráveis por revisão versionada e testes. Confiança: leitura direta da disponibilidade, mas causa e impacto no jogo continuam indeterminados. Não existe pontuação ou botão fictício de otimização.

## O Windows está usando RAM de forma errada?

As fontes e a amostragem atual não permitem essa conclusão. Cache/standby são usos potencialmente úteis e páginas podem ser reutilizadas. RAM livre, RAM disponível, working set, commit e cache são medidas diferentes. Uma falta de página pode ser resolvida em RAM (soft fault) ou exigir leitura de arquivo/pagefile (hard fault); esta última também pode ser carregamento normal de arquivo mapeado. Não atribuir toda falta de página ao pagefile.

Reduzir um working set numericamente não demonstra economia sustentável: páginas reutilizadas podem gerar novas faltas. Desativar pagefile ou impor seu tamanho sem observar commit/limite pode reduzir a margem para alocações e dumps. Não limpar RAM/standby automaticamente, não esvaziar working sets, não desligar compressão, SysMain ou serviços por regra universal.

## Investigação local somente leitura

Condições: Windows 11, Ryzen 5 5500, RTX 5060, cerca de 16 GiB instalados; Visual Studio, navegador e ChatGPT abertos, sem sessão controlada de jogo. Builds e outros aplicativos estavam ativos próximos aos ensaios, portanto as janelas não são diretamente comparáveis.

Consulta externa de 12 amostras em 25,013 s via `Win32_PerfFormattedData_PerfOS_Memory`:

- Disponível: 5440–5555 MiB (aproximadamente 5,31–5,42 GiB).
- Maior commit/limite observado: 71,49%.
- Pages Input/sec máximo: 3; Pages Output/sec máximo: 0. As amostras inicial/final tinham ambos zero; a janela completa não era inteiramente zero.
- Esses contadores são taxas amostradas, não contagem integral de eventos. Não comprovam ausência de paginação entre amostras, stuttering ou bugs.
- CacheBytes foi registrado, mas não é inventário completo da lista standby. Não inferir RAM desperdiçada a partir dele.
- Working sets dos maiores processos também foram registrados apenas para contexto: páginas compartilhadas impedem somar esses valores como RAM exclusiva. Não houve encerramento de processos ou leitura de memória de outros aplicativos.

Ensaio de integração Release: **33 amostras ao longo de 32,005 s**, GlobalMemoryStatusEx. RAM utilizável 17042153472 bytes (15,87 GiB). Disponibilidade inicial 5317586944 bytes (4,95 GiB), final/mínima 4608323584 bytes (4,29 GiB). Estado final: **sem baixa disponibilidade sustentada**, duração baixa zero. O último timestamp UTC corresponde a 23/10/2026 23:12:30 em São Paulo. Não interpretar como teste de jogo.

Artefatos locais: `artifacts/ram-diagnosis/windows-memory-observations.json`, `process-memory-observation.json`, `native-assessment.json` e TRX. Nenhum relatório enviado a terceiros.

## Plano de investigação para o futuro modo de jogos

1. Acrescentar commit global e limite por API oficial (por exemplo GetPerformanceInfo), com definição e estado próprios. Não confundir GlobalMemoryStatusEx.ullTotalPageFile com tamanho físico do pagefile.
2. Medir paginação e atividade de armazenamento em janelas comuns; investigar faltas de página por ETW/WPR quando houver sintoma reproduzível. Quantidade de faltas sozinha não comprova perda de FPS.
3. Registrar processo/executável do jogo, cena, resolução, configurações, VRAM, sessão e frametimes por tecnologia validada (PresentMon). Sem contexto de jogo, não diagnosticar gargalo.
4. Investigar crescimento persistente de commit privado e pools de driver em ensaios longos; não chamar crescimento curto de vazamento. Distinguir alocação legítima e cache de crescimento anormal.
5. Se uma aplicação específica demonstrar competição de RAM durante o jogo, apresentar evidências e permitir ao usuário decidir o que fechar/limitar. Nada de encerrar indiscriminadamente processos ou serviços.
6. Comparar múltiplas execuções antes/depois na mesma cena, com aquecimento/cache comparáveis; analisar frametimes, 1% lows, variação natural, carregamento e efeitos colaterais. Um número menor de RAM ocupada não é critério de sucesso.
7. Somente após evidência e consentimento, criar ação isolada com estado anterior, confirmação, verificação e reversão quando aplicável. Nenhum tweak foi implementado nesta entrega.

## Validação e limites

- Restore Debug/Release aprovado, dependências já atualizadas.
- Build Debug/x64: zero erros/avisos, 11,97 s; Release/x64: zero erros/avisos, 12,33 s.
- Testes: **77 aprovados em Debug e 77 em Release**, zero falhas/ignorados, incluindo 15 novos. TRX `ram-final-debug.trx` / `ram-final-release.trx`.
- Cobertura nova: limiares relativos e absolutos, duração, baixa transitória, recuperação, falhas, origem incompatível, relógio/gaps, capacidade alterada, janela limitada, duplicatas, localização, pausa/retomada e fonte Windows real.
- Primeiro teste incremental apontou CA1416 no teste nativo; corrigido com SupportedOSPlatform e skip explícito fora do Windows. Builds finais sem aviso.
- Release iniciado no Windows, PID 2296, respondendo. Usuário confirmou dados na Análise e reinício da contagem após sair/voltar.
- Captura/UIA automática indisponível: node_repl não iniciou por “windows sandbox failed: helper_unknown_error: setup refresh had errors”. Não há screenshot atual nem homologação automática de layout, DPI, teclado ou minimização desta página.
- Confirmar visualmente todos os estados em pressão real continua pendente; não foi provocada exaustão de RAM. Fakes foram usados somente nos testes.
- Esta entrega não comprova causalidade em jogos, melhoria de FPS ou problema do gerenciador de memória do Windows.

## Consumo observado do processo

Uma medição adicional de 60,55 s do PID 2296 registrou CPU média 0,0043%, working set médio 181,12 MiB, commit privado médio 200,18 MiB, handles 1108→1090 e threads 55→47. A etiqueta do arquivo é `release-ram-analysis`, porém os logs não comprovam que Análise permaneceu ativa: o último evento anterior registrava parada da coleta. Portanto esses valores são somente uma observação do processo após a conferência, **não uma medição homologada do custo da análise ativa**. GPU/DWM não medidos; não afirmar custo nulo nem ausência de vazamento. Evidências `artifacts/ram-diagnosis/release-ram-analysis-{samples.csv,summary.json}`.

## Comandos executados

```powershell
dotnet restore Lotus.slnx -p:Platform=x64
dotnet restore Lotus.slnx -p:Configuration=Release -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build Lotus.slnx -c Release -p:Platform=x64 --no-restore
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=ram-final-debug.trx"
# Somente no processo do terminal de teste, removida ao final:
$env:LOTUS_RAM_DIAGNOSTIC_SECONDS = '32'
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Release --no-build --logger "trx;LogFileName=ram-final-release.trx"
```

## Referências primárias consultadas

- [Microsoft: Memory Performance Information](https://learn.microsoft.com/en-us/windows/win32/memory/memory-performance-information) — correspondências entre métricas, APIs e contadores.
- [Microsoft: Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set) — páginas residentes, soft/hard faults e trimming.
- [Microsoft: Introduction to page files](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/introduction-to-the-page-file) — commit, limite e funções do pagefile.
- [Microsoft: Troubleshooting memory issues, part two](https://techcommunity.microsoft.com/blog/askperf/an-overview-of-troubleshooting-memory-issues---part-two/372679) — disponível inclui standby/free/zero e interpretação de contadores. Material conceitual histórico; não fonte dos limiares de produto.
