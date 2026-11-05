# Lotus: evolução da aba Ferramentas

Estado em 04/11/2026. Tudo continua dentro da aba **Ferramentas**: mesma grade de cartões escuros com detalhes rosé e "Abrir ferramenta"; cada ferramenta abre no próprio cartão. Nada foi publicado nem instalado.

## 1. Funcionalidades implementadas

| Ferramenta | O que faz |
|---|---|
| **Limpeza Inteligente** | "Analisar PC" mostra, sem alterar nada, cada categoria com arquivos, tamanho real e risco. As categorias são: temporários do usuário e do Windows, despejos de falha, relatórios de erro, miniaturas, Lixeira, cache do Chrome/Edge/Brave/Opera/Opera GX/Firefox/Discord, registros antigos, caches web da Steam e da Epic, registros de launchers e falhas de jogos. "Limpar selecionados" informa os bytes realmente removidos, os arquivos em uso, as falhas e o que foi pulado, e depois analisa de novo para conferir. |
| **Memória e Processos** | RAM e CPU reais por programa (duas leituras com ~1 s de intervalo), com nome e local. Fechar é normal (como clicar no X); "Encerrar à força" é avançado e tem aviso próprio. Há uma lista "fechar antes de jogar", marcada com estrela. |
| **Arquivos e Armazenamento** | Espaço por disco; arquivos grandes (≥ 100 MB), pastas que mais ocupam, duplicados (mesmo tamanho e depois SHA-256 do conteúdo inteiro) e downloads com mais de 90 dias. "Abrir local" abre o Explorer; remover só manda para a Lixeira. |
| **Inicialização do Windows** | Nome, editor (assinatura ou dados do arquivo), assinatura digital (WinVerifyTrust), caminho, mecanismo e estado. Ligar e desligar usam a mesma ação com registro do Otimizar, com desfazer exato. |
| **Desinstalar Programas** | Ganhou ordenação por nome, tamanho, data e editor. Já existiam a busca instantânea, o desinstalador oficial e a análise opcional de sobras. |
| **Internet e DNS** | Ganhou um histórico simples dos últimos testes, só com números medidos. Trocar e restaurar o DNS agora fica no registro. |
| **Saúde do Disco** | Modelo, tipo e conexão, capacidade, espaço livre, saúde do Windows e estado operacional. Temperatura, desgaste, horas e erros aparecem quando o Windows libera; caso contrário, "Indisponível". Oferece o `chkdsk /scan` do Windows (somente leitura), numa janela visível. |
| **Drivers e Sistema** | Drivers como antes. Nova seção Sistema: processador, vídeo, RAM, placa-mãe, BIOS e Windows; dispositivos com erro no Gerenciador de Dispositivos; erros relevantes dos últimos 7 dias, agrupados. SFC e DISM só rodam com confirmação. |
| **Jogos e Overlays** | RAM e CPU reais de cada item e orientação individual. Launchers aparecem como informativos; RTSS, Afterburner e PresentMon aparecem como compatíveis. Só overlays e apps de captura ou efeito contam como "vale olhar". |

**Motor central** (`src/Lotus.Core/Maintenance`): todas as ferramentas seguem o fluxo detectar → analisar → classificar o risco (`MaintenanceRisk`: Baixo, Revisão, Avançado) → recomendar → confirmar na interface → executar → verificar (nova leitura) → registrar → reverter quando possível. Para limpar outra coisa, basta acrescentar uma `CleanRule` com pastas, idade, padrões e o programa que a bloqueia. O registro fica em `%LOCALAPPDATA%\Lotus\Maintenance\history.json` (últimas 200 operações, sem conteúdo de arquivos).

## 2. Funcionalidades reaproveitadas

- **Do Otimizar:** inventário de inicialização e `StartupToggleAction`, ligado pelo `PowerProfilesViewModel`, com journal SQLite e desfazer exato.
- **Lixeira:** `RecycleBin.Send` (SHFileOperation).
- **Desinstalador:** `UninstallerViewModel` e `WindowsUninstaller`, com as sobras.
- **Rede:** `PingConnectionTester` e `WindowsDns` (comparar, aplicar via UAC e restaurar).
- **Drivers:** `DriverUpdates` (serviço da NVIDIA e Windows Update).
- **Hardware:** `WindowsDriverInfo` e `QuickHardwareFacts`.
- **Overlays:** `WindowsOverlayScanner`, ampliado sem mudar o "Preparar e jogar".
- **Componentes:** `TaskState`/`TaskProgressView` e o padrão de cartões do `ToolsPanel`.

Também foi corrigido o seguinte: a entrada "Iniciar com o Windows" do Lotus podia apontar para outra cópia, como o build de desenvolvimento. Agora a versão de desenvolvimento nunca grava essa entrada, e o Lotus instalado corrige um caminho errado.

## 3. Arquivos modificados

**Novos:**
- **Core** (`src/Lotus.Core/Maintenance/`): `Maintenance.cs`, `CleanEngine.cs`, `Processes.cs`, `Storage.cs`, `StartupTool.cs`, `DiskTool.cs`, `SystemTool.cs`
- **Infrastructure** (`src/Lotus.Infrastructure/Maintenance/`): `WindowsCleanServices.cs`, `WindowsProcessSampler.cs`, `StorageServices.cs`, `WindowsStartupDetails.cs`, `WindowsDiskInspector.cs`, `WindowsSystemInspector.cs`
- **Testes:** `CleanEngineTests.cs`, `ProcessesTests.cs`, `StorageTests.cs`, `StartupToolTests.cs`, `DiskSystemOverlayTests.cs`

**Alterados:**
- `ToolsViewModel.cs`, `Uninstaller.cs`, `Games/Overlays.cs`
- `Infrastructure/Games/WindowsOverlayScanner.cs`, `Infrastructure/Setup/WindowsStartup.cs`
- `Lotus.Infrastructure.csproj` (pacote `System.Diagnostics.EventLog` 10.0.0)
- `App/Controls/ToolsPanel.xaml(.cs)`, `App/Controls/UninstallerCard.xaml`, `App/MainWindow.xaml.cs`
- `Strings.resx` e `Strings.en-US.resx`
- `tests/UninstallerTests.cs`

## 4. Medidas de segurança

- **Abrir ou analisar não altera nada.** Toda execução pede confirmação e lista o que será feito. A Lixeira tem aviso próprio, e itens de revisão ou avançados nunca vêm marcados.
- **O que a limpeza não apaga:** saves, mods, configurações, sincronização, cookies, sessões, senhas e caches de shader. Os caches dos navegadores se limitam a `Cache_Data`/`cache2`. Um teste confere que nenhuma regra aponta para dados sensíveis.
- **Antes de apagar, cada arquivo é conferido de novo:**
  - precisa estar dentro da pasta da regra;
  - nenhum link é seguido, nem no caminho;
  - a idade é conferida de novo;
  - arquivo somente leitura é recusado;
  - arquivo em uso fica onde está.
- **Pastas protegidas:** nunca a raiz de um disco nem a pasta do usuário, Documentos, AppData, Windows ou Arquivos de Programas inteiras, mesmo que uma variável como TEMP aponte para lá. Arquivos temporários do próprio Lotus ficam.
- **Caches de programas abertos** são pulados e o motivo é informado.
- **Processos:** Windows, antitrapaças, jogos (pastas de launchers e jogos da biblioteca), o Lotus, RTSS, Afterburner e PresentMon nunca podem ser fechados. Cada processo é reclassificado antes de qualquer ação. Não há limpeza de memória à força.
- **Arquivos:** nunca saem sozinhos, só para a Lixeira. Dos duplicados sempre fica uma cópia de cada conteúdo. Arquivos de sistema e de dados de aplicativos só aparecem para consulta. Um arquivo alterado depois da análise não é movido.
- **Inicialização:** só itens do usuário (HKCU), usando a mesma marca do Gerenciador de Tarefas, com estado anterior preservado e desfazer. Itens de todos os usuários ficam só para consulta (`asInvoker` mantido).
- **Disco e sistema:** só as ferramentas do próprio Windows (`chkdsk /scan`, SFC, DISM), em janela visível, com UAC e confirmação. Sem desfragmentação de SSD e sem drivers de terceiros.
- **Sem números inventados:** nenhum ganho de FPS, RAM, CPU ou ping é prometido, e não há "impacto" estimado de inicialização. O texto do DNS diz que ele não muda o ping na partida.
- **Leveza:** tudo é assíncrono e só roda ao clicar. A análise de limpeza e a de armazenamento podem ser canceladas. Não há tarefas residentes.

## 5. Testes executados e resultados

- **Compilação:** Debug x64 e Release x64 sem avisos nem erros nos projetos do app.
- **Testes:** 514 aprovados em Debug e 514 em Release (eram 466; 48 novos, todos em pastas temporárias ou com simulações). Cobrem:
  - tamanhos reais e idade;
  - arquivos em uso e somente leitura;
  - links simbólicos;
  - TEMP apontando para a pasta do usuário;
  - programa aberto bloqueando o cache;
  - cancelamento;
  - Lixeira nunca pré-marcada;
  - registro persistente;
  - regras sem dados sensíveis;
  - classificação de processos e proteção contra fechar o que não pode;
  - duplicados pelo conteúdo;
  - "uma cópia sempre fica";
  - arquivo alterado depois da análise;
  - ordenação do desinstalador;
  - histórico de rede;
  - inicialização com conflito;
  - correção da entrada do Lotus;
  - disco "indisponível";
  - agrupamento de eventos;
  - reparos só SFC/DISM;
  - overlays com uso real.
- **Teste de processos reais:** roda isolado, sem paralelismo, para não atrapalhar os testes de tempo.
- **Validação visual no app de desenvolvimento (somente leitura):**
  - análise de limpeza: 1,4 GB recuperáveis;
  - processos;
  - Downloads: 10,1 GB, com duplicados reais encontrados;
  - inicialização: 20 itens;
  - disco ADATA SU650;
  - sistema e overlays.
- **Sem exclusões reais:** nenhuma limpeza, envio à Lixeira, fechamento de programa, mudança de inicialização, chkdsk, SFC ou DISM foi executado no PC.

## 6. Limitações conhecidas

- **Temperatura, desgaste, horas e erros do disco:** em muitos PCs o Windows só informa como administrador. Aparecem como "Indisponível".
- **Assinatura:** arquivos do Windows assinados por catálogo aparecem como "sem assinatura digital embutida", porque a verificação é do arquivo.
- **Atalhos na pasta Inicializar:** o atalho não é resolvido até o programa de destino, então a assinatura não é verificada.
- **Itens fora do alcance:** inicialização de todos os usuários (HKLM), `C:\Windows\Temp` e parte dos relatórios precisam de administrador. Ficam só para consulta ou são pulados.
- **Duplicados:** só arquivos a partir de 1 MB; discos inteiros podem levar tempo (a análise pode ser cancelada).
- **CPU por processo:** é uma amostra de cerca de 1 s, não uma média longa.
- **Desfazer:** a Limpeza remove caches e temporários de vez, porque mandar para a Lixeira não liberaria espaço. Arquivos pessoais vão para a Lixeira.

## 7. Melhorias futuras recomendadas

- Usar a lista "fechar antes de jogar" no **Preparar e jogar**, sempre com confirmação.
- Resolver atalhos (`.lnk`) para verificar o programa de destino.
- Leitura opcional, com UAC, dos contadores SMART que exigem administrador.
- Agendamento opcional e manual da Limpeza, sem tarefa residente.
- Novas regras de limpeza para outros launchers (Battle.net, GOG, Riot) depois de validar as pastas reais.
- Exportar o histórico de manutenção junto com o backup do Lotus.

## Novidades adicionadas depois (04/11/2026)

| Ferramenta | O que faz |
|---|---|
| **Check-up do PC** | Cartão em destaque no topo da aba. Um clique roda todas as análises de leitura (monitor, falhas de jogos, sistema, discos, Windows, processos, overlays, inicialização, limpeza). O resultado fica em ordem: pede atenção → dica → em ordem, com contadores e "Abrir ferramenta" em cada linha. Não há nota inventada. |
| **Monitor na taxa certa** | Mostra a taxa atual e a máxima do monitor em números grandes. "Usar X Hz" e "Desfazer" usam a mesma ação com registro do Otimizar (`DisplayRefreshAction`). |
| **Recursos do Windows para jogos** | Modo de Jogo, gravação em segundo plano, otimizações em janela e agendamento de GPU, com estado e conselho. Ajustar e desfazer são do Otimizar; também há atalho para a página das Configurações. |
| **Por que meu jogo fechou?** | Lê os registros 1000/1002 do Windows (por posição de campo, funciona em qualquer idioma) só dos jogos e cruza com o que aconteceu no mesmo minuto: reinício do driver de vídeo, falta de memória, erro de hardware ou de disco. Depois explica a causa provável e mostra o código técnico. |
| **Windows em dia** | Reinício pendente (Windows Update e componentes), arquivos esperando reinício, tempo ligado e aviso de Inicialização Rápida. O ponto de restauração usa o `Checkpoint-Computer` do Windows, com UAC, e só é dado como criado se a lista do Windows cresceu. |

**Também mudou:** o aviso "Antes de começar" saiu do lado da faixa de jogos e virou uma nota discreta no fim do Início.

**Segurança:**
- tudo é leitura até você confirmar;
- ajustes do monitor e dos recursos de jogo são reversíveis;
- o ponto de restauração e o chkdsk/SFC/DISM rodam com UAC e confirmação.

**Testes:** 524 aprovados em Debug e Release, com 10 testes novos.

**Telas conferidas no PC, só lendo:**
- Check-up: 1,4 GB recuperáveis, monitor já em 75 Hz, nenhum jogo fechou sozinho;
- Recursos para jogos;
- Windows em dia: sem reinício pendente, PC ligado há 3 dias;
- Por que meu jogo fechou?;
- Início com a nota no fim.

**Não executados:** nenhuma mudança de taxa, ajuste de recurso nem ponto de restauração foi feito no PC.
