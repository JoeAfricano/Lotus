# Lotus — Telemetria de GPU e sensores

Revisão: 23/10/2026. Implementação da Etapa 03. O relatório de validação distingue testes executados de verificações visuais pendentes.

## Arquitetura

O serviço singleton existente continua sendo o único proprietário do loop, com intervalo padrão de 1 s. Um CompositeHardwareTelemetryProvider combina WindowsHardwareTelemetryProvider (CPU/RAM preservados) e IGpuTelemetryProvider. O primeiro adaptador de fabricante é NvmlGpuTelemetryProvider. Novos fabricantes implementam o contrato sem depender de RTSS, Afterburner, interface ou benchmark.

Não há subprocesso nvidia-smi por amostra. A coleta serial ocorre no worker, fora da UI. O provedor usa somente exports de consulta. Cada GPU tem identidade UUID quando disponível; o índice é fallback identificado como tal. A interface mantém um ViewModel por identidade, evitando reconstruir os cards a cada amostra. GPUs removidas desaparecem da coleção, evitando apresentar a última leitura como atual.

Cada SensorReading contém valor anulável, SensorUnit, TelemetryStatus, Source com função da API, CapturedAt UTC e erro NVML quando aplicável. O horário corresponde à aquisição local, não ao relógio interno do sensor. GpuTelemetryResult preserva o estado de enumeração independentemente dos estados de cada métrica.

O serviço chama IMonitoringSessionProvider.Stop no finally, após terminar a leitura. O adaptador encerra NVML, solta a biblioteca e limpa delegates/identidades. Reaquisição inicia outra sessão serial, sem leitura concorrente. DisposeAsync aguarda o worker antes de destruir a janela. A liberação de assinaturas continua ocorrendo ao sair do Monitoramento, minimizar e fechar.

## ABI e dependências

- Biblioteca do driver já instalado: caminho absoluto System32/nvml.dll ou Program Files/NVIDIA Corporation/NVSMI/nvml.dll. Não procurar no diretório corrente nem no PATH.
- NativeLibrary, delegates Cdecl, StructLayout sequencial. nvmlMemory_v2_t tem 40 bytes em x64; versão 0x02000028. nvmlUtilization_t tem dois uint32.
- nvmlInit_v2, nvmlShutdown, nvmlDeviceGetCount_v2, nvmlDeviceGetHandleByIndex_v2, GetUUID e GetName.
- GetUtilizationRates, GetClockInfo, GetTemperature, GetPowerUsage, GetPowerManagementLimit, GetFanSpeed, GetMemoryInfo_v2 e GetCurrentClocksEventReasons.
- Export ausente produz Unsupported; driver ausente produz Unavailable. Inicialização/enumeração com falha tem nova tentativa após 30 s. Falhas individuais não apagam outras leituras.
- Enumeração limitada defensivamente a 64 dispositivos; cache de handles acompanha apenas dispositivos presentes.
- Nenhum pacote NuGet, DLL, header NVIDIA ou driver foi acrescentado à distribuição. Bindings próprios; NVML permanece componente proprietário do driver instalado.

## Definições

| Métrica | Unidade / significado |
| --- | --- |
| GPU | Percentual de tempo ocupado reportado por GetUtilizationRates; não é diagnóstico de gargalo |
| Clock gráfico | MHz, domínio GRAPHICS=0; não frequência nominal nem média efetiva garantida |
| Clock de memória | MHz, domínio MEM=2; não converter em taxa efetiva de transferência sem conhecer arquitetura |
| Temperatura | °C, sensor GPU=0; não hotspot, memória ou CPU |
| Potência | mW convertidos em W; na RTX 5060 a API representa média aproximada de 1 s |
| Limite de potência | W configurados reportados; somente leitura, não margem disponível |
| Ventoinha | Percentual de velocidade pretendida da API; não RPM nem necessariamente medição de tacômetro; 0 pode ser fan-stop |
| Memória total/usada/livre/reservada | Campos em bytes de uma única chamada GetMemoryInfo_v2; exibir GiB binários |
| VRAM percentual | 100 × used / total da mesma resposta; não usar orçamento DXGI ou RAM compartilhada |
| Eventos de clock | Bitmask preservado; 0x20/0x40 térmico, 0x4/0x80 potência; demais bits não são traduzidos em diagnóstico |

A RTX local retornou total = used + free + reserved. Os campos são preservados, sem supor que free = total - used. A documentação também descreve regras de contabilização dependentes de plataforma/driver; não reinterpretar nem corrigir campos para forçar uma igualdade. Em WDDM são alocações reportadas, não atividade efetiva da memória nem memória por processo.

Valores não finitos/negativos são inválidos. Utilização e VRAM percentual: 0–100. Limites defensivos de sanidade: temperatura até 250 °C, clocks até 100.000 MHz e potência até 10.000 W; não são faixas operacionais recomendadas. Memória exige total positivo e cada campo <= total. Ausência nunca é substituída por zero.

## Histórico e interface

Duas séries adicionais por GPU (utilização e VRAM), até 60 pontos e até 60 s, com timestamp monotônico do ciclo. Valores ausentes interrompem a linha; não interpolar pausas. Os mesmos controles XAML desenham CPU/RAM/GPU/VRAM. Sem biblioteca gráfica adicional. Os gráficos GPU/VRAM têm eixo vertical 0–100% e janela de 60 s.

A pedido do usuário, visão principal compacta de CPU/RAM/GPU/VRAM, temperatura e potência. Fontes/estado/horário, clocks, ventoinha, identidade, memória reservada e explicações ficam em expanders. Gráficos com eixos numéricos 0/50/100% e −60/−30 s/agora; altura total 90 DIP (inclui eixo temporal). Recursos pt-BR/en-US. Layout empilhado abaixo de 900 DIP. Leitor de tela recebe nomes dos gráficos e valores textuais. Mudança de idioma atualiza os objetos existentes.

## Limitações explícitas

- Apenas NVIDIA com NVML nesta entrega; arquitetura permite AMD/Intel, mas nenhum adaptador desses fabricantes foi implementado.
- Múltiplas GPUs: contrato/UI/testes simulados cobrem várias NVIDIA; a máquina local tem somente uma GPU NVIDIA para integração real.
- Temperatura do Ryzen indisponível: APIs de CPU/RAM não expõem sensor de die confiável. Sensores ACPI não são apresentados como temperatura de CPU. LibreHardwareMonitor não foi necessário para GPU e não foi incluído.
- RPM, hotspot, temperatura de VRAM, frequência efetiva de CPU e consumo por processo de GPU não são coletados.
- O cancelamento é verificado entre consultas nativas; uma chamada dentro do driver não é interrompível com CancellationToken. Não descarregar biblioteca enquanto uma chamada está em andamento.
- Retorno de energia/temperatura não certifica desempenho, estabilidade sob carga ou precisão de instrumentos.
- A marca foi redesenhada a partir das referências visuais enviadas pelo usuário: palavra e flor pixeladas com retângulos adjacentes unidos por linha, contornos claros, cores por linha, bloom e 12 partículas por marca visível. ShapeVisual alimenta CompositionVisualSurface apenas no tamanho exibido (60 × 18, 128 × 30 e 36 × 36 DIP). Animação de cores/opacidade/partículas no compositor, ciclo de 6 s, sem timers de UI, imagens em sequência ou biblioteca externa. Redução de movimento e alto contraste são respeitados; a minimização suspende a animação. Efeito visual ainda depende de validação na tela atual.

## Fontes primárias consultadas

- [NVML e localização da biblioteca instalada](https://docs.nvidia.com/deploy/nvml-api/nvml-api-reference.html).
- [Consultas e semântica de memória/potência](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html).
- [Layout de memória v2](https://docs.nvidia.com/deploy/nvml-api/latest/api/structnvmlMemory__v2__t.html).
- [Eventos de clock](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlClocksEventReasons.html).
- [Avisos legais NVIDIA](https://docs.nvidia.com/deploy/nvml-api/latest/Notices.html).

Essas referências descrevem a API e não concedem licença de redistribuição de qualquer binário NVIDIA. Uma distribuição futura com componentes adicionais exige revisão específica de termos. Não houve cópia de implementação de terceiros.


## Marca e validação visual

A geometria é uma adaptação vetorial própria inspirada nas imagens do usuário, não uma reprodução dos PNGs em resolução integral. Palavra e flor mantêm fallback estático e nomes acessíveis; alto contraste restaura os elementos originais. Com movimento reduzido a nova forma permanece estática. Minimizar interrompe o ciclo; perder o foco não o desliga.

As animações alteram cores/opacidade/posição sem invalidar o layout XAML a cada quadro. Isso limita trabalho de UI, mas não prova custo zero de renderização: o ensaio de CPU do processo não mede DWM/GPU. A última revisão da marca deve ser medida separadamente da execução de 15 minutos anterior.

Referências Microsoft: [CompositionVisualSurface](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionvisualsurface), [máscara de DropShadow](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.dropshadow.mask).
