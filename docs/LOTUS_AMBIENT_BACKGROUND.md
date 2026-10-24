# Lotus — Fundo ambiental Lótus + Aurora

Sessão iniciada em 23/10/2026, com validação após a virada para 24/10/2026 (America/Sao_Paulo). Base UX: f581d94. Nenhum módulo de hardware ou otimização novo.

## Abordagem

`Controls/LotusAmbientBackground.cs` é uma FrameworkElement decorativa única na janela, dentro da área de conteúdo e atrás do Frame. Não fica dentro das páginas; navegar não instancia outra camada. Frame/Page transparentes deixam visíveis apenas as áreas livres; cards e sidebar preservam suas superfícies opacas, dimensões e hierarquia.

- Base existente #0C0B12 mantida. Dois SpriteVisuals do tamanho do viewport usam CompositionRadialGradientBrush, com cores #9F4279/#C75B92/#E584B1 e bordas transparentes. Opacidades atuais 0,12 e 0,10; stop intermediário com alpha 220/255 para evitar desaparecimento prematuro.
- EllipseCenter dos gradientes se move em ciclos independentes de 18 e 23 segundos, ida/volta com easing suave. Não há piscar, rotação ou animação de layout.
- Flor abstrata: mesmos cinco contornos angulares da identidade, construídos uma vez com 30 retângulos finos transformados. Escala de layout proporcional entre 350 e 550 DIP, ancorada no terço direito.
- Um contêiner separado pulsa a flor de 1 a 1,02 e volta, em 14 segundos. Opacidade atual 0,05–0,07; modo estático 0,065, contornos de 1,8 DIP na geometria base. Resize altera tamanho/posição do contêiner externo, sem recriar geometria ou disputar a propriedade animada.
- 76 objetos de composição registrados na criação (incluindo raiz e stops). Sem arquivos de imagem, superfícies bitmap, vídeo, WebView, blur, GetAlphaMask, CompositionMaskBrush ou bibliotecas novas. A renderização de gradientes é feita pelo compositor; não há rasterização de bitmap nem callbacks de layout por frame na UI.

## Ciclo de vida e preferências

Carregamento aguarda dimensões válidas; atualização é idempotente. Redimensionamento reaproveita objetos. Janela sem foco, oculta/minimizada ou com Reduzir animações deixa a composição estática; alto contraste a oculta. O Windows UISettings.AnimationsEnabled é respeitado na execução normal. A política é reconsultada ao ativar/restaurar e mudar preferências; não foi adicionada uma inscrição WinRT global potencialmente incompatível. Uma mudança do sistema enquanto a janela permanece ativa não tem homologação de atualização instantânea.

Unloaded/fechamento interrompem animações, desconectam o visual e liberam os objetos. O descarte marca a camada como liberada para impedir recriação por eventos tardios de fechamento. Loaded posterior permite reinicialização. Não há assinaturas de telemetria nem timers. Exceção ArgumentException/COM registra warning e mantém o fundo base; fallback não é declarado como efeito aprovado.

A preferência de intensidade não foi adicionada: os limites são conservadores e o controle existente de movimento é suficiente para este incremento. A marca animada mantém sua política anterior, independente do fundo. Configurações > FAQ explica essa diferença sem ocupar a tela principal.

## Prova de movimento e condições reais

O Windows desta máquina retorna AnimationsEnabled=false. Na primeira execução normal Release, PID 17392, foram criados 76 objetos, 2 auroras e 30 segmentos; a política registrou Running=false, Active=true, WindowsAnimations=false. Esse resultado estático é esperado e não deve ser chamado de animação comprovada.

Para testar separadamente sem modificar o Windows ou JSON do usuário, existe a flag explícita de diagnóstico `--ambient-motion-proof`. Somente nessa execução a política global é ignorada; redução local, foco, visibilidade e alto contraste continuam respeitados. `--ambient-motion-disabled` mantém a mesma composição estática e tem precedência sobre a prova. Não são modos expostos como ajustes de hardware.

A ferramenta de captura/UIA permaneceu indisponível nesta sessão: “windows sandbox failed: helper_unknown_error: setup refresh had errors”. Não há frames temporais finais ou comparação de pixels. Foi solicitada conferência manual de movimento, leitura, navegação e restauração na prova; o usuário informou que o fundo parecia ausente. A captura fornecida em seguida mostra contornos quase invisíveis e aurora muito fraca; essa revisão visual não foi aprovada. Após a medição, a prova foi reaberta para permitir essa conferência. Logs de Running=true comprovam a solicitação ao compositor, não substituem essa verificação visual.

## Compilações e testes

Build Debug/x64 final: 11,69 s, zero erros/avisos. Release/x64: 12,59 s, zero erros/avisos. Testes existentes: **102 aprovados em Debug e 102 em Release**, zero falhas/ignorados; TRX ambient-final-debug.trx / ambient-final-release.trx. Provedores, regra RAM e ciclo de sensores não foram modificados.

A primeira compilação Debug encontrou MSB3026 transitório por Lotus.Core.dll bloqueada por MSBuild.exe (3388); a repetição interna concluiu com um warning. Nenhum processo MSBuild do usuário foi encerrado. Rodada final serial (-m:1 -nodeReuse:false) passou sem avisos. Não foi necessário excluir binários ou recriar a solução.

Release normal iniciou e respondeu sem exceção fatal na consulta de logs; fechamento às 23:59:03 registrou 76 objetos liberados e Application closed. Isso confirma o caminho de descarte dessa execução, não prova ausência de vazamento em todas as condições.

## Comparação de consumo

Ferramenta `tools/Compare-LotusAmbient.ps1` reutiliza `Measure-LotusProcess.ps1`: duas execuções Release de 60 s, movimento ambiental ligado versus estático. A marca e a composição estática ficam iguais nas duas condições; nenhuma preferência do Windows ou arquivo de preferências é alterado. A prova usa a flag de diagnóstico devido à política do sistema.

Cada sessão aquece por 8 s; amostras de CPU/working set/commit/handles/threads a 1 Hz. Contadores oficiais Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine consultados para Lotus/DWM no início/fim, com nomes dos engines preservados, sem somar engines sobrepostos. São amostras inteiras esparsas: zero não significa custo nulo e os contadores do DWM incluem o desktop inteiro.

O script registra foco/visibilidade/movimento durante cada janela, navegação, demanda de sensores e política efetiva. Se houver perda de foco, navegação ou interrupção, a sessão não demonstra o custo de animação contínua em repouso. Resultados em 24/10/2026, mesma página Início, tema Midnight, sidebar expandida, 12 processadores lógicos, sem sensores. Logs confirmam foco/visibilidade durante ambas as janelas, sem navegação. Na sessão ligada, Running=true permaneceu durante a medição; isso valida a condição solicitada ao compositor, sem substituir prova visual.

| Condição | Amostras / duração | CPU média normalizada | Working set médio | Commit privado médio | Handles início→fim | Threads início→fim |
| --- | --- | --- | --- | --- | --- | --- |
| Movimento ligado (prova) | 60 / 60,474 s | 0,32943% | 139,810 MiB | 160,679 MiB | 1050→1047 | 57→53 |
| Composição estática | 60 / 60,423 s | 0,04310% | 139,342 MiB | 160,453 MiB | 1040→1037 | 56→52 |

Diferença observada: +0,28633 ponto percentual de CPU e +0,468 MiB de working set; pico de CPU por intervalo 1,952% ligado e 0,649% estático. São duas execuções curtas, sem replicação estatística ou teste prolongado; não atribuir toda a diferença exclusivamente ao fundo. A composição já foi limitada a dois gradientes sem blur/bitmaps para conter custo. O ensaio não estabelece meta universal de desempenho.

As quatro consultas GPU (início/fim de cada condição), com 54 entradas Lotus/DWM cada, retornaram valores inteiros zero. Não ofereceram medição útil para isolar o custo GPU do efeito; não declarar GPU 0% ou custo nulo. Seria necessário ensaio adicional com instrumentação de GPU/DWM apropriada.

Artefatos locais em artifacts/ambient-background/: ambient-motion-{on,off}-samples.csv, resumos, eventos, snapshots GPU e comparison-conditions.json. Ao terminar, o script encerrou as duas instâncias graciosamente e reabriu uma execução normal, sem tocar no JSON de preferências. Nenhuma captura foi inventada.

## Teste adicional de movimento reduzido

Em execução de prova ativa/visível, ReduceMotion=true resultou em Running=false mesmo com Proof=true. Evidência: artifacts/ambient-background/reduced-motion-proof.json. O valor original ReduceMotion=false foi restaurado, preservando os outros campos da configuração. Não foi alterada preferência do Windows. A prova foi reaberta (PID 15312) para conferência manual; isso não equivale a validar visualmente uma troca dinâmica pelo controle da interface.

## Arquivos e limites

App: nova Controls/LotusAmbientBackground.cs; MainWindow.xaml/.cs; transparência de SectionPage.xaml. Core: somente textos localizados de FAQ. Ferramenta: Compare-LotusAmbient.ps1. Documentos: este relatório e referências em arquitetura/decisões/roadmap. BrandPulse.cs tinha ajuste preexistente em standby e permanece fora deste checkpoint.

Pendentes: captura temporal real, matriz de DPI, leitor de tela, ensaio prolongado de GPU e memória, comprovação visual de todos os ciclos após minimizar/reabrir e repetidas navegações. Não afirmar ausência de vazamento por sessões curtas. Não afirmar custo desprezível sem considerar foco/política e compositor. Backend de soluções automáticas continua futuro, conforme roadmap.

Referências: [CompositionRadialGradientBrush](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionradialgradientbrush), [Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes), [animações](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-animation) e [XAML interop](https://learn.microsoft.com/en-us/windows/apps/develop/composition/xaml-comp-interop). APIs conferidas por compilação contra o SDK instalado (WinUI 2.3.9); nenhum código UWP/WinUI 2 presumido como compatível.

## Correção de visibilidade — 24/10/2026

A captura do usuário confirma que a camada não estava totalmente encoberta: o contorno da flor aparece, mas com contraste insuficiente. O gradiente anterior combinava alpha 105/255 no stop intermediário com opacidade de 0,065–0,085: intensidade intermediária de apenas 2,7–3,5%, antes do desvanecimento até a borda. O problema de visibilidade foi delimitado como intensidade insuficiente, sem evidência de falha de criação do visual. Não confundir isso com comprovação do movimento.

A alteração é pontual: dois gradientes mais presentes (máximo 12%), cores centrais mais claras, stop intermediário menos transparente, contornos de 1,8 DIP e flor em 5–7%. O deslocamento lento aumentou de 8–9 para 12–14% do viewport, mantendo ciclos de 18/23 s, escala 1–1,02 e 76 objetos. Não houve alteração na navegação, cards, marca, serviços ou dependências. Log de criação agora inclui dimensões: execução Release observada com viewport 1031 × 770 DIP, sem dimensão zero.

Build Debug/x64: 15,38 s; Release/x64: 11,53 s; ambos sem erros ou avisos. Testes: 102 aprovados em cada configuração, TRX ambient-visibility-debug.trx e ambient-visibility-release.trx. A nova intensidade ainda exige confirmação visual; a captura anterior não é evidência do resultado atualizado. A política normal continua respeitando AnimationsEnabled=false deste Windows; prova diagnóstica não altera essa preferência.


Novo ensaio de 30 amostras por condição, artifacts/ambient-visibility/: movimento ligado 0,805% CPU / 183,460 MiB working set; estático 0,474% / 140,471 MiB. Comparação descartada para atribuir custo ao efeito: a sessão ligada registrou navegação e ativação de sensores, e a estática perdeu foco. Esses números descrevem as sessões completas, não o custo incremental do fundo. A medição anterior de 60 s pertence à intensidade anterior e não certifica o impacto desta correção. Nenhum ensaio comprova ausência de vazamento.

Próximo passo restrito a esta entrega: confirmar visibilidade e movimento da revisão em execução de prova; depois validar modo normal estático neste Windows, sidebar, restauração e redução de movimento. Não iniciar módulos novos antes de fechar essa pendência visual. Captura automática continua indisponível na sessão.
