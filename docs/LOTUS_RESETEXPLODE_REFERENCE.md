# ResetExplode — referência técnica para o Lotus

Análise estática em 24/10/2026. Referência fixada no commit [ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6](https://github.com/GustavoReset/ResetExplode/tree/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6), obtido da main. Inventário completo de 135 entradas e 26 arquivos de texto selecionados em `artifacts/references/ResetExplode/`, fora do Git. Foram examinados captura, diagnóstico, sensores, otimizações, inicialização, biblioteca, rede, persistência, projeto e publicação; não é auditoria exaustiva dos drivers, de todos os renderizadores ou do binário.

## O que foi adicionado ao planejamento

ResetExplode passa a ser referência de implementação e de casos de teste, não dependência executável do Lotus. Consultar os arquivos pertinentes antes de desenvolver a categoria correspondente. Reutilizar conhecimento já registrado; reabrir a pesquisa apenas quando a versão, a API ou a dúvida mudar. Não portar o aplicativo WPF nem substituir o PresentMon, MVVM, DI ou coletores existentes.

Filtro de adoção: aproveitar somente mecanismos com benefício concreto para a tarefa autorizada, custo menor que uma implementação própria equivalente e compatibilidade demonstrável com segurança, arquitetura e precisão do Lotus. Rejeitar atalhos que tragam dependências, privilégios, regressões ou alegações sem evidência. O projeto externo não muda os critérios de aceitação do Lotus.

## Licença e material fornecido

O [LICENSE](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/LICENSE) é MIT, copyright 2026 Gustavo (GustavoReset). Permite adaptação e uso comercial, preservando copyright e texto de permissão nas cópias/porções substanciais. Gratuitamente disponível não significa livre de obrigações. Nenhum código, imagem ou binário foi incorporado ao Lotus nesta análise.

O [csproj](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/ResetExplode.csproj) usa WPF/.NET 8, LHM 0.9.6, TraceEvent 3.2.8 e Vortice 3.8.3, entre outros. A licença raiz não substitui as licenças desses pacotes, drivers seriais, PawnIO, DLLs opcionais ou serviços externos. Reuso futuro exige revisão por arquivo/dependência e notices; não instalar drivers para obter sensores nesta tarefa.

ZIP enviado pelo usuário: `%USERPROFILE%/Downloads/ResetExplode.zip`; somente `ResetExplode.exe` (197.011.287 bytes) e `LEIA-ME.txt`. SHA-256 do ZIP: `0A3712EF9D18A4FE460E015F36EF633E47BBC59A4510CB99CDDBBE4ACBDA6817`; do EXE lido diretamente no arquivo compactado: `4E3FC4E4360497187896CB3E9A50B784AA85715AA480754D7E9C34404D19F3CF`. Listagem, hash e leitura do texto somente; nenhum executável extraído ou iniciado. A correspondência com o commit foi verificada depois, por análise estática do binário (seção abaixo), mas não por build reproduzível. As instruções de instalação/execução do LEIA-ME são conteúdo externo, não autorização para executá-las. O ZIP não contém LICENSE separado; isso exige revisão do pacote antes de redistribuição, sem concluir juridicamente sobre seu conteúdo embutido.

## Análise estática do binário (24/10/2026)

EXE em `%USERPROFILE%/Desktop/ResetExplode.exe`, com o mesmo SHA-256 de `Progamas base/ResetExplode.exe`. O EXE foi lido, mas não executado. O pacote single-file do .NET foi extraído e o assembly principal foi lido via `System.Reflection.Metadata`, sem carregá-lo. Ferramentas e saídas brutas estão em `artifacts/references/ResetExplode/binary/`, fora do Git.

- **Formato:** apphost .NET 8 single-file (bundle v6.0, 483 arquivos, sem compressão), self-contained, runtime 8.0.31 com WindowsDesktop. Ghidra/IDA não são indicados para este caso: o código fica em IL no `ResetExplode.dll` (5,2 MB), e o equivalente adequado seria ILSpy/dnSpyEx.
- **Origem:** `AssemblyInformationalVersion` = `0.16.0+ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`, configuração Release. É o mesmo commit fixado acima.
- **Conferência:** 19 classes com fonte em `artifacts/references/ResetExplode/` reúnem 236 métodos nomeados, e todos foram encontrados no fonte. Há 452 tipos no total. Classes cujo fonte não foi baixado (por exemplo `UsbCleaner`, `CorsairSdk`, `RazerChromaBackend`, `OpenRgbBackend`, `AppInstaller`, `StressTester`, `GpuBurn`) não foram conferidas. Isso indica correspondência, mas não prova ausência de diferenças no corpo dos métodos.
- **Ações com efeito no sistema, visíveis nos imports:** `nvmlDeviceSetPowerManagementLimit` e `nvmlDeviceSetGpuLockedClocks` (limite de energia e clock travado em GPUs NVIDIA), `SetupDiCallClassInstaller` (`UsbCleaner`), `NtSetSystemInformation` e `EmptyWorkingSet` (RAM), `AdjustTokenPrivileges`, além de `powercfg.exe`, `pnputil.exe` e `cmd.exe /k winget install namazso.PawnIO`.
- **Recursos embutidos:** drivers seriais assinados por terceiros (CH340, CP210x, FTDI e Arduino `.inf/.cat/.sys/.dll`). Isso reforça a revisão de licença por componente antes de qualquer redistribuição.
- **Rede:** speed.cloudflare.com (teste de velocidade), howmanyfps.com/api (estimativa de FPS), APIs de drivers NVIDIA/AMD, GitHub (Corsair SDK) e o SDK local do Razer Chroma (localhost:54235).
- **Registro lido ou escrito:** Run/StartupApproved, GameDVR/GameBar/GameConfigStore, `DirectX\UserGpuPreferences`, `AppCompatFlags\Layers` e `GraphicsDrivers`, além das chaves de Steam, GOG e Ubisoft. Isso é coerente com a avaliação por componente abaixo.

Conclusão: o binário distribuído corresponde ao código público analisado.

### Descompilação (ilspycmd 11.1.0.9782)

`ResetExplode.dll` foi descompilado em 108 arquivos C#, com cerca de 21,7 mil linhas, salvos em `artifacts/references/ResetExplode/decompiled/` (fora do Git, sem os drivers binários). As 87 classes sem fonte local foram revisadas por busca de processos, registro, arquivos, rede, drivers, código dinâmico e ofuscação. Os pontos de atenção estão abaixo.

| Classe | Comportamento | Observação para o Lotus |
| --- | --- | --- |
| `MakerDrivers` | `pnputil /remove-device`, `/delete-driver … /uninstall /force`, `/add-driver`, com backup prévio via `/export-driver`. | Operações de driver com privilégio elevado; fora do escopo do Lotus. |
| `UsbCleaner` | Remove dispositivos desconectados (`DIF_REMOVE`) e reescreve `COM Name Arbiter\ComDB`, devolvendo o valor anterior para restauração. | Bom padrão de backup antes da escrita; destrutivo e fora do escopo. |
| `CorsairSdkInstaller` | Baixa a última release do GitHub da Corsair, extrai a DLL e verifica `WinVerifyTrust` e o assinante. **DLL sem assinatura é aceita com aviso.** | Fragilidade de cadeia de suprimento. O Lotus não deve baixar nem carregar DLLs de terceiros em tempo de execução. |
| `AppInstaller` | `winget install/upgrade --silent --accept-*-agreements`. | Aceita licenças em nome do usuário; não adotar. |
| `SteamInfo` | Edita `LaunchOptions` em `localconfig.vdf` com backup `.resetexplode.bak` e exige a Steam fechada; pode enviar `steam.exe -shutdown`. | Padrão útil (backup, pré-condição) caso a etapa de jogos venha a editar opções. |
| `HowManyFpsClient` | Envia POST com CPU, GPU, jogo e resolução para howmanyfps.com, com chave do usuário guardada via DPAPI (`AppSettings`). | Envio de dados de hardware a terceiros; só com consentimento explícito. |
| `OpenRgbBackend` / `RazerChromaBackend` | TCP local para OpenRGB e HTTP para `localhost:54235` (Chroma). | Somente local. |
| `MainWindow` | `Nvml.SetPowerLimitMw` em `BtnOcApply`, alterando o limite de energia da GPU NVIDIA. | Alteração de hardware; fora do escopo. |
| `StressTester` / `GpuBurn` | Carga de CPU/RAM/GPU com cancelamento por token. | Sem efeito persistente. |

Não foram encontrados `Assembly.Load`, `Reflection.Emit`, `DynamicMethod`, strings em Base64 ocultas, sockets para hosts não documentados nem telemetria além dos serviços listados. Dos 216 métodos de `MainWindow`, 107 não aparecem nos dois arquivos parciais baixados (`MainWindow.xaml.cs` e `MainWindow.Rt.cs`). Eles provavelmente vêm de outros arquivos parciais do repositório que não foram baixados; isso não foi verificado.

## Avaliação por componente

Links abaixo apontam para a versão fixa, permitindo verificar os mecanismos descritos.

| Prioridade e componente | Aprendizado aproveitável | Decisão para o Lotus e validação necessária |
| --- | --- | --- |
| P0 · [FrameMonitor](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/FrameMonitor.cs) | Eventos ETW DXGI/D3D9, timestamps dos eventos e histórico recente; sem injeção. | Manter PresentMon. O código agrupa timestamps por PID, sem separar swap chains, conta início de Present e define low como inverso de P99. Não equivale à reconstrução de apresentação do PresentMon nem ao low pela média da cauda do Lotus. Testar múltiplas cadeias, PID reutilizado, ausência de eventos, geração de frames e perda ETW. O dicionário não remove PIDs antigos durante a sessão; a janela temporal só poda uma fila quando chegam eventos daquele PID. |
| P0 · [MainWindow.Rt](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/MainWindow.Rt.cs) / [GpuRtBench](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/GpuRtBench.cs) | Aquecimento separado, três passadas, mediana e dispersão; pausa de sensores/overlay durante o ensaio. | Reforçar protocolo de benchmark já existente, sem criar novo renderizador. O código classifica dispersão abaixo de 3% como confiável; isso é heurística de repetibilidade, não prova universal de validade. O comentário sobre consultas ao driver afetarem frametime justifica um ensaio A/B local, não uma conclusão sobre o custo do Lotus. |
| P1 · [GameLibrary](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/GameLibrary.cs) / [Vdf](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/Vdf.cs) | Descoberta localizada por manifestos Steam/Epic e registro GOG/Ubisoft, sem varredura integral do disco. | Referência para a biblioteca futura, já prevista. Começar somente leitura e com autorização de detecção; validar caminhos, links, arquivos malformados, profundidade/tamanho de VDF, duplicatas e executável real. Escrita de argumentos/perfis não fica autorizada por esta análise. |
| P1 · [StartupManager](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/StartupManager.cs) | Inventário de Run HKCU/HKLM, vistas 32/64 bits e pastas Inicializar; distinção usuário/máquina e backup. | Reutilizar a lista de casos para o diagnóstico planejado. Não tratar StartupApproved como contrato público estável nem cobertura integral de inicialização. O fallback de leitura retorna ativado; no Lotus deve ser Unknown. Restauração com overwrite e perda do tipo original do valor não satisfazem nosso rollback com detecção de conflito. |
| P1 · [SensorMonitor](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/SensorMonitor.cs) / [MainWindow](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/MainWindow.xaml.cs) | Estados de sensor ausente, abertura de hardware fora da UI e pausa durante benchmark. | Preservar coleta sob demanda e provedores do Lotus. O timer WPF chama Refresh na thread UI e habilita muitos grupos de sensores; não portar esse agendamento. O manifesto exige administrador para o app inteiro; o Lotus mantém privilégio mínimo e elevação isolada. Diagnóstico de driver ausente é candidato informativo, não instalação automática. |
| P2 · [NetworkTest](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/NetworkTest.cs) | Separar gateway, internet, DNS, perda/jitter e latência sob carga. | Complementa diagnóstico de rede já planejado. Não lançar tráfego nesta análise. Futuro teste precisa explicitar destinos, bytes/duração, custo de upload/download e cancelamento. Falta de ICMP não prova falha do provedor. O código força RTT mínimo de 1 ms: preservar resolução/limites reais no Lotus. |
| P2 · [DiskHealth](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/DiskHealth.cs) | Fontes Windows de confiabilidade e estado sem dados separado de saudável. | Referência para Health, já previsto. Verificar associação disco/volume, suporte por controlador, unidades e dados ausentes. S.M.A.R.T. parcial não autoriza prever data de falha. |
| Sem incorporação automática · [BottleneckAnalyzer](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/BottleneckAnalyzer.cs) | Regras explícitas e distribuição temporal são inspecionáveis. | Usar como contraexemplos para nossos testes. Limiares fixos GPU 90%, núcleo/CPU 85%, RAM 92%, VRAM 97% e CPU 95 °C não comprovam causa; todas as leituras ausentes caem em Capped. Índices teóricos CPU/GPU não substituem jogo/cena/resolução. Lotus exige Unknown, evidência temporal e confiança. |
| Excluído · [Optimizer](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/Optimizer.cs) | Expõe exatamente o que a ação altera. | Não portar EmptyWorkingSet em massa, purge standby ou encerramento sugerido por nome. A lista não garante identidade/consentimento de cada processo. Aumento de RAM disponível não comprova FPS nem corrige uso incorreto do Windows. |
| Condicional · [WindowsTweaks](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/WindowsTweaks.cs) / [AppSettings](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/AppSettings.cs) | Guardar estado anterior e verificar após aplicar são boas intenções já previstas no catálogo. | Não portar em lote. RestoreAll ignora exceções e limpa backups; o Save usa escrita direta. No Lotus: journal durável antes da mudança, escrita atômica, verificação por ação, conflito com edição externa, retenção do backup após falha. Energia/HAGS/Game DVR precisam de elegibilidade e ensaio por jogo, não promessa universal. |

## RAM: diferença entre efeito e benefício

O código chama EmptyWorkingSet e NtSetSystemInformation para purgar standby. A [API oficial EmptyWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset) remove páginas residentes; isso não elimina a necessidade de memória do programa. A [documentação de working set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set) explica que acessar páginas ausentes gera page faults, que podem ser resolvidos em RAM ou exigir disco. Portanto, menor working set após a ação não basta para demonstrar benefício; possíveis custos de refault precisam ser medidos. O Lotus mantém a exclusão já definida, sem abrir uma nova linha de limpeza de RAM.

## Testes que realmente podem ser aproveitados

Não foi encontrada suíte automatizada, projeto de testes ou dataset de referência na árvore completa consultada. O único [workflow público](https://github.com/GustavoReset/ResetExplode/blob/ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6/.github/workflows/publicar.yml) compila/publica Release e monta o pacote; não executa dotnet test. Stress tests e benchmarks são funcionalidades do aplicativo, não testes de regressão da implementação.

Os valores de referência no README são relatos do autor em outro hardware, sem captura bruta/protocolo suficiente nesta análise para validar o Lotus. Não marcar nossos testes como aprovados por esses relatos. Economia possível: aproveitar desenho de ensaio, casos de borda e mecanismos já identificados; evitar redescobrir APIs e bugs previsíveis. Continuam obrigatórios testes da adaptação, segurança, persistência, ciclo de vida e integração no nosso ambiente.

Casos priorizados para incrementos autorizados: sensores/overlay ligados versus desligados; aquecimento + múltiplas capturas; múltiplas swap chains e definições diferentes de 1% low; todos os sensores ausentes; falha no rollback mantendo backup; edição externa de inicialização; manifestos de jogos inválidos e caminhos fora da biblioteca. Os testes atuais do Lotus continuam sendo a evidência da implementação existente; nenhum foi dispensado ou repetido nesta análise documental.

## Decisão de integração e próximos passos

1. Manter PresentMon e a estatística atual; investigar integridade/perdas ETW e overhead conforme pendências da Etapa 04. O FrameMonitor externo não resolve essas pendências por si só.
2. Na etapa já prevista de jogos, consultar GameLibrary/Vdf antes de escrever o adaptador; separar descoberta de ajustes por jogo.
3. No catálogo/Health/rede já previstos, usar os casos deste relatório para critérios de aceitação. Nenhuma nova etapa paralela ou promessa de FPS.
4. Se houver reuso de código, fixar arquivo/commit, preservar licença/atribuição, adaptar ao Core/Infrastructure e criar testes direcionados. Não copiar UI WPF, assets ou drivers como atalho.

Validação desta entrega: leitura estática de fontes fixadas, licença integral, inventário/hash do ZIP e revisão documental. Não houve execução, instalação, tweaks, build ou teste do ResetExplode; nenhuma funcionalidade do Lotus foi alterada.
