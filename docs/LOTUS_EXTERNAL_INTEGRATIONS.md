# Lotus — Integrações externas e licenças

Consulta: 21/10/2026. Nenhum código, binário, driver ou SDK externo foi incorporado ao Lotus. Links de branches refletem conteúdo consultado, não versão aprovada para distribuição. Antes de integrar, fixar tag/commit, hash, arquivos de licença e dependências transitivas. A política de produto é priorizar uso gratuito e implementação própria quando os direitos ou custos não forem adequados.

## Resultado por ferramenta

### MSI Afterburner

A [página oficial MSI](https://www.msi.com/Landing/afterburner) anuncia uso gratuito e funções de monitoramento e controle de GPU. Localmente foi encontrado `MSIAfterburner.exe` versão **4.6.6.16757**, em `C:\Program Files (x86)\MSI Afterburner`. Isso não confirma que esteja em execução nem que exporte sensores neste momento.

O SDK local contém `Include/MAHMSharedMemory.h` e o exemplo `Samples/SharedMemory/MAHMSharedMemorySample/MAHMSharedMemorySampleDlg.cpp`. O cabeçalho define assinatura, versão, tamanhos, contagens e horário de atualização; a representação de tempo merece atenção em x64. Existe também MACM de controle, fora do escopo de leitura do Lotus. Foram consultados arquivos, não executados exemplos.

Decisão: bridge opcional para detectar, abrir e ler MAHM validada. Perfis permanecem sujeitos a mecanismo e semântica comprovados: abrir aplicativo pode carregar ajustes já configurados pelo usuário, portanto deve ser uma ação explícita. Não acionar clocks, tensão, power limit ou ventoinha. EULA/redistribuição do programa e licença de cópia dos exemplos não foram estabelecidas pela página nem pelos cabeçalhos lidos. Não redistribuir ou copiar até resolver esse gate.

### RTSS

A [página oficial Guru3D](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) identifica freeware, monitoramento de frames e OSD. A página retornada listava 7.3.7, com data de atualização de 2025; não é prova de qual é a última versão em toda fonte. Instalação local: **7.3.5.28314**, em `C:\Program Files (x86)\RivaTuner Statistics Server`.

Fontes primárias locais efetivamente lidas:

- `SDK/Include/RTSSSharedMemory.h`: assinatura, versão e layouts de estatísticas/OSD.
- `SDK/Include/RTSSExports.h`: exportações do host; não confundir com interface de perfis.
- `SDK/Samples/SharedMemory/RTSSSharedMemorySample/RTSSProfileInterface.h` e `.cpp`: o exemplo resolve exports de `RTSSHooks.dll`, incluindo leitura/escrita de propriedades, load/save/update; `FramerateLimit` está descrita no cabeçalho.

Decisão: investigar ABI e arquitetura da DLL antes de usar no Lotus x64; existência de DLL x64 na instalação não comprova exports equivalentes. Se necessário, helper da arquitetura compatível, isolado e autenticado. Não editar INI arbitrariamente nem automatizar cliques.

OSD requer slot/identificador próprio, validação do layout compartilhado, limites de strings/offsets e liberação apenas do conteúdo Lotus. Leituras vencidas, mudança de versão e assinatura inválida geram indisponibilidade. Perfis FPS: snapshot incluindo existência prévia, confirmação, escrita mínima, atualização, leitura posterior e restauração com detecção de conflitos. Preservar perfis globais e de outros aplicativos.

Os manuais `Doc/ReadMe.pdf` locais foram pesquisados por termos de licença: são principalmente histórico de versões. Uma nota de 2013 sobre restrição de OSD x64 não deve ser generalizada para a versão atual. Não foi estabelecida licença atual completa de redistribuição/cópia de SDK. Gate pendente; nenhuma tentativa de contornar restrições.

### CapFrameX

O [README](https://github.com/CXWorld/CapFrameX) apresenta captura, análise, comparação e overlays sobre PresentMon. O [LICENSE consultado](https://github.com/CXWorld/CapFrameX/blob/master/LICENSE) começa com MIT e inclui avisos de componentes com outros termos; MIT do projeto não libera automaticamente todo binário empacotado.

Foram consultadas a árvore `source`, a separação entre contratos/captura/estatística e [FrametimeStatisticProvider.cs](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.Statistics.NetStandard/FrametimeStatisticProvider.cs). O código separa low average e quantis e possui tratamentos de outliers. Decisão Lotus: referência metodológica e comparador independente; implementar estatística própria com definições publicadas. Não incorporar a aplicação inteira. Importação de captura poderá ser avaliada com schema e consentimento explícitos; não assumir API universal ou equivalência de métricas.

### Intel PresentMon

O [repositório](https://github.com/GameTechDev/PresentMon) distingue biblioteca de análise ETW, console CSV e serviço/API. O README declara MIT para o projeto; preservar aviso de copyright/licença e revisar componentes de fornecedor antes de distribuição. [Fonte/licença](https://github.com/GameTechDev/PresentMon/blob/main/README.md).

O [serviço/SDK](https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md) documenta carregamento de `PresentMonAPI2.dll` da instalação compatível e alerta que uma cópia própria pode não corresponder ao serviço. Há loader e códigos de status; reconexão após reinício é responsabilidade do cliente.

Decisão: primeiro protótipo futuro de benchmark com console separado e schema fixado, depois avaliar API/serviço se reduzir custo ou oferecer métricas necessárias. Nunca parar uma sessão ETW de outro programa; nome único por sessão Lotus. Validar permissão, cancelamento, saída do jogo, perdas, DLLs e arquitetura. GPU Busy é sinal contextual, não classificador automático de gargalo. Comparar serviço versus console com o mesmo cenário antes de escolher definitivamente.

### LibreHardwareMonitor

[README](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) e [Computer.cs](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Hardware/Computer.cs) mostram biblioteca .NET, grupos de hardware e ciclo de abertura/atualização/fechamento. Alguns sensores requerem administrador. Nem todo sensor existe em todo equipamento.

[Licença MPL-2.0](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE): preservar notices, disponibilizar o código coberto exigido para binários distribuídos e modificações nos arquivos cobertos. Não implica automaticamente abrir todo o Lotus. Existem componentes com outros termos; revisar [notices](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/THIRD-PARTY-NOTICES.txt) da versão escolhida antes de inclusão. A lista completa desses componentes ainda não foi auditada nesta entrega.

Decisão: candidato opcional a sensores, após APIs Windows básicas. Habilitar apenas grupos necessários, evitar varredura agressiva e respeitar bloqueios de drivers/proteções. Não desativar segurança para obter temperatura. Se indisponível, oferecer monitoramento parcial e explicar a fonte ausente.

### Microsoft Sysinternals

[Portal](https://learn.microsoft.com/en-us/sysinternals/) e [termos](https://learn.microsoft.com/en-us/sysinternals/license-terms) consultados. A licença permite instalação/uso nos dispositivos, mas restringe publicação para terceiros copiarem e transferência. Não embutir a suíte no instalador Lotus. Dumps e traces podem conter dados sensíveis.

Decisão: Process Explorer, Autoruns e RAMMap como instrumentos opcionais de diagnóstico/validação; conferir documentação individual na fase de uso. Não tratá-los como SDK estável de sensores. Não aceitar EULA nem enviar arquivos automaticamente. Para funções do Lotus, preferir implementação própria sobre APIs públicas Windows e uma lista explícita de fontes de inicialização; não prometer a cobertura integral do Autoruns no primeiro incremento.

### Windows Performance Toolkit

[WPT](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/) integra WPR, WPA e suporte a Xperf; WPR produz traces ETW, permite perfis e interface de controle. [ADK oficial](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install) é a origem de instalação. Trata-se de ferramenta Microsoft com termos próprios, não biblioteca aberta com redistribuição presumida. Termos do instalador/artefatos específicos permanecem por validar antes de redistribuição; nada foi instalado.

Decisão: diagnóstico avançado sob demanda, com duração e orçamento de arquivo limitados. UI não deve coletar ETW amplo em repouso. Registrar providers, buffers, eventos perdidos e custo; não encerrar sessões de terceiros. Exportar ETL só com consentimento e revisão de dados sensíveis.

## Contrato de integração proposto

Estados: NotInstalled, InstalledStopped, RunningUnavailable, Compatible, Incompatible, AccessDenied, Stale e Faulted. Descoberta futura por registro de instalação/arquitetura e caminho escolhido, sem varrer todo disco. Validar caminho absoluto, versão e assinatura quando disponível; carregar DLL por caminho controlado. Versão conhecida não substitui negociação/verificação de capabilities.

O launcher exibe detecção, estado, origem e ações permitidas; não hospeda a janela de terceiros nem exige automação visual. Timeouts, retentativas limitadas e buffers bounded. Dados compartilhados são entrada não confiável: validar assinatura, versão, contagens, offsets, tamanhos e overflow antes de acessar. Testar produtor encerrando durante a leitura e alteração de versão. Nunca confundir DLL carregável, licença válida e compatibilidade funcional.

## Alternativas gratuitas e código próprio

| Necessidade | Opção preferida | Alternativa sem redistribuição restrita | Limitação |
| --- | --- | --- | --- |
| CPU/RAM/disco/processos | Código Lotus sobre APIs públicas Windows | Adaptadores próprios por fonte | Sensores profundos não são garantidos |
| Captura de frames | PresentMon com licença/versão fixadas | Consumidor ETW próprio apenas se custo e correção justificarem | Reconstruir pipeline de apresentação é trabalho especializado |
| Análise/comparação | Estatística própria testada | Comparação externa CapFrameX | Igualar definições antes de comparar |
| Overlay | RTSS instalado, se uso permitido | Janela overlay própria para modos compatíveis | Não prometer fullscreen exclusivo nem compatibilidade com anticheat |
| FPS cap | Perfil por mecanismo validado ou configuração do jogo | Recomendar cap nativo e medir | Um timer no Lotus não limita frames de outro processo |
| Temperaturas/VRAM | LHM ou API de fornecedor licenciada | Mostrar somente fontes disponíveis | Sem substituição fictícia ou driver próprio improvisado |

Se uma dependência exigir pagamento ou direitos incompatíveis, excluir essa dependência do caminho obrigatório e implementar a função viável com código original. Não copiar expressão/código protegido nem remover verificações de licença. Não presumir que reproduzir uma interface proprietária concede direito de distribuição. Medir overhead da alternativa; código próprio não garante mais eficiência por definição.

Gate de distribuição: inventário de artefatos e versões, termos por arquivo/pacote, notices, fontes exigidas, dependências nativas/driver, evidência de autorização quando necessária e testes de compatibilidade. Até esse gate, RTSS/Afterburner/Sysinternals/WPT permanecem instalações externas opcionais, sem bundling. Nenhum custo de licença obrigatório foi introduzido nesta análise.


## NVIDIA NVML — Etapa 03

Integração de consulta ao componente NVML já instalado com o driver. Nenhum binário, SDK ou código de terceiros foi incorporado. NVML é proprietária; disponibilidade com o driver não significa licença aberta de redistribuição. Os bindings C# foram escritos no Lotus. Fontes e funções em [LOTUS_GPU_TELEMETRY.md](LOTUS_GPU_TELEMETRY.md). Não foi necessário adicionar NVAPI ou LibreHardwareMonitor. Não há taxa/API cloud nessa integração local; não se presume licença para empacotar DLLs NVIDIA.


## Referências competitivas — 23/10/2026

Carpi Ultimate/Toptimizer: somente páginas públicas/indexadas, com divergência entre oferta antiga indexada e redirecionamento atual; nenhuma instalação, binário ou inspeção de implementação. Não copiar assets/código proprietário nem inferir licença gratuita pela publicidade. Nos dois repositórios sugeridos, os READMEs declaram MIT; o arquivo LICENSE do primeiro não foi recuperado, e a URL LICENSE do segundo retornou 404. Nenhuma cópia realizada. Antes de eventual reutilização, obter commit, texto integral e notices de todos os arquivos/dependências. A existência de interfaces públicas do Windows permite planejar implementação própria, sem contornar licenças. [Escopo, limitações e links consultados](LOTUS_OPTIMIZATION_CATALOG_PLAN.md).
