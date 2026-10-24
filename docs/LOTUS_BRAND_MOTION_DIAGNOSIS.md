# Lotus — Diagnóstico da marca estática

Data: 23/10/2026. Base: checkpoint 7e459fa. Investigação existente preservada; nenhuma dependência adicionada.

## Causa confirmada

A chamada real Windows.UI.ViewManagement.UISettings.AnimationsEnabled retornou false; MinAnimate era 0. A preferência local ReduceMotion estava false. MainWindow.UpdateBrandMotion combinava essas condições com AND, portanto passava animate=false ao BrandPulse. O construtor executava o retorno estático antes de StartAnimation. Não era necessário substituir imagens ou a geometria para corrigir essa decisão.

Evidência: artifacts/branding-diagnostics/system-policy.json e log "Brand motion policy" da execução de diagnóstico: WindowsAnimations=false, ReduceMotion=false, Proof=true, Animate=false (os efeitos completos ficam intencionalmente desligados durante a prova).

A leitura da API WinRT exigiu Windows PowerShell 5 nesta máquina; PowerShell moderno não carregou esse tipo WindowsRuntime. O resultado null dessa primeira tentativa não foi interpretado como false. A consulta subsequente retornou false de forma explícita. Nenhuma configuração do Windows foi modificada.

## Prova mínima implementada

Somente mediante --brand-motion-proof:
- Reutiliza o desenho pixelado de BrandPulse em estado estático.
- Visual correto obtido por Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual.
- Vector3KeyFrameAnimation em Visual.Scale: 1 → 1,035 → 1, duração 4,4 s, Forever, easing suave.
- CenterPoint calculado pela metade das dimensões reais.
- Sidebar: 36 × 36 DIP; superfície ampliada isolada: 228 × 228 DIP, com moldura fixa.
- Início após Loaded/Opened com tamanho válido, sem alteração de layout por frame.
- Redução de movimento, alto contraste e minimização continuam pausando o teste; o bloqueio global do Windows é ignorado somente nesse diagnóstico explícito.
- Nenhuma mudança na tipografia, geometria ou layout definitivo nesta fase.
- Não usa CompositionMaskBrush nem GetAlphaMask.

Build Release da prova: aprovado, zero erros/avisos. Execução PID 11624: log às 22:35:25 registrou Scale iniciada nos elementos BrandScalePreview (228 × 228) e BrandFlower (36 × 36). Esse log comprova a chamada, não comprova movimento visual.

Uma primeira tentativa do diagnóstico tentou compartilhar Geometry.Data com outro Path e falhou com ArgumentException. Corrigida para reutilizar a fábrica existente, criando a mesma geometria sem reparentear objetos XAML. A construção do diagnóstico também passou para dentro do tratamento de exceções. A falha foi registrada; não foi declarada como execução válida.

## Validação mínima e correção definitiva

O usuário confirmou visualmente a prova ampliada: “Sim, a flor cresce e diminui”. Essa confirmação liberou a implementação do nome; o log sozinho não foi utilizado como prova visual.

A política de produção agora segue a preferência local **Reduzir animações** do Lotus. O bloqueio global de animação do Windows é registrado para diagnóstico, mas não impede esse efeito explicitamente solicitado pelo usuário. Nenhuma preferência do Windows foi modificada. Um aviso localizado em Configurações explica essa decisão.

`BrandPulse` preserva o desenho pixelado existente. A flor pulsa no ContainerVisual próprio, evitando disputa com transformações gerenciadas pelo XAML. O nome recebe um CompositionLinearGradientBrush com StartPoint/EndPoint animados horizontalmente, sobre a mesma geometria das letras. Não há máscaras de texto, timers por frame, mudanças de layout por frame nem dependências adicionais. A iluminação é suave; partículas foram reduzidas de 12 para seis por marca e as antigas animações individuais de cor por linha foram removidas.

Dimensões XAML: flor expandida 38 × 38 DIP; nome 100 × 30 DIP; flor compacta 30 × 30 DIP. O NavigationView nativo oculta o cabeçalho quando recolhido; a flor compacta ocupa o rodapé existente, sem substituir o template inteiro da navegação.

O início é idempotente e aguarda elemento carregado com dimensões válidas. Preferências, visibilidade, minimização e estado do painel reavaliam a política. A redução de movimento mantém a marca estática; o descarte interrompe animações e libera superfícies, formas e brushes. A prova ampliada continua disponível somente com `--brand-motion-proof`.

## Verificação final — 23/10/2026

- Debug/x64: aprovado, zero avisos/erros, 14,94 s.
- Release/x64: aprovado, zero avisos/erros, 12,82 s.
- Testes existentes: **62 aprovados em Debug e 62 em Release**, zero falhas/ignorados. Evidências locais `tests/Lotus.Tests/TestResults/brand-final-debug.trx` e `brand-final-release.trx`.
- Comandos-base: `dotnet build Lotus.slnx -c Debug -p:Platform=x64`, equivalente Release; `dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug --logger "trx;LogFileName=brand-final-debug.trx"`, equivalente Release. A rodada foi executada após as alterações funcionais.
- Execução normal Release: política registrada às 22:47:09 com Animate=true, WindowsAnimations=false, ReduceMotion=false, Proof=false. Inicialização de Scale na flor e HorizontalSweep nos nomes registrada sem exceção fatal nessa sessão.
- Captura nativa continua indisponível: “windows sandbox failed: helper_unknown_error: setup refresh had errors”. Não foram obtidos frames temporais comparáveis. A pulsação ampliada foi confirmada pelo usuário; varredura do nome e testes visuais de sidebar/minimizar/restaurar/redução de movimento continuam aguardando conferência. Não são declarados aprovados apenas pelos logs.
- DPI 100%/125%/150%, temas e cortes na revisão definitiva não foram homologados visualmente. Não há alegação de ausência de regressões visuais sem essa conferência.

## Consumo

Medição com `tools/Measure-LotusProcess.ps1`, 60 amostras por condição após 8 s de estabilização, Release/x64, Dashboard inicial, mesma máquina, tema Midnight e sidebar expandida; somente ReduceMotion mudou entre execuções. A preferência original (false) foi restaurada e o aplicativo foi reaberto. CPU normalizada pelos 12 processadores lógicos; working set físico e commit privado não são somados.

| Condição | Duração | CPU média | Working set médio | Commit privado médio | Handles início → fim | Threads início → fim |
| --- | --- | --- | --- | --- | --- | --- |
| Movimento ligado | 60,474 s | 0,12488% | 140,559 MiB | 160,958 MiB | 1096 → 1093 | 55 → 51 |
| Movimento desligado | 60,405 s | 0% registrado | 137,988 MiB | 160,151 MiB | 1060 → 1057 | 56 → 52 |

Diferença observada entre execuções: +0,125 ponto percentual de CPU e +2,57 MiB de working set. São duas sessões curtas, sem repetição estatística ou controle de toda carga externa; não isolam causalidade nem demonstram ausência de vazamento. Zero no contador não significa custo nulo. GPU/DWM não foram medidos. Não comparar diretamente estes valores de Dashboard com os 15 minutos de Monitoramento da Etapa 03.

Evidências locais: `artifacts/branding-diagnostics/release-motion-{on,off}-samples.csv` e respectivos `-summary.json`. Encerramento gracioso das duas sessões completou dentro do prazo de 10 s; uma única instância foi reaberta intencionalmente ao final. Logs do intervalo final sem Warning/Error/Fatal na consulta realizada.

## Referências consultadas e compatibilidade

Windows App SDK 2.5.1; pacote WinUI 2.3.9. APIs conferidas no metadata Microsoft.UI.Xaml.xml instalado. O código usa tipos Microsoft.UI.Composition/Microsoft.UI.Xaml, não Windows.UI.Composition de UWP.

- [Composition animations](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-animation).
- [XAML–Composition interoperability](https://learn.microsoft.com/en-us/windows/apps/develop/composition/xaml-comp-interop).
- [Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes).
- [Windows App SDK Samples](https://github.com/microsoft/WindowsAppSDK-Samples). O link de SceneGraph retornou bloqueio ao navegador nesta sessão; não se presume inspeção de um exemplo que não foi aberto.
- [Lottie-Windows](https://github.com/CommunityToolkit/Lottie-Windows).
- [AnimatedVisualPlayer](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/animated-visual-player).

Lottie foi consultado somente como alternativa. O README distingue CommunityToolkit.WinUI.Lottie de CommunityToolkit.Uwp.Lottie e menciona requisitos de Win2D. Nenhum pacote foi instalado, código copiado ou compatibilidade presumida. A prova nativa foi confirmada; não há justificativa técnica para adicionar Lottie nesta correção.

## Arquivos desta correção

- App: `BrandPulse.cs`, `BrandMotionProof.cs`, `MainWindow.xaml`/`.cs`, `SectionPage.xaml`.
- Core: recursos `Strings.resx` e `Strings.en-US.resx`.
- Documentação: este diagnóstico, arquitetura, decisões técnicas e relatório Etapa 03.

CPU/RAM/GPU, contratos de telemetria, projetos, pacotes e solução preservados. O backup preexistente da solução não foi modificado nem incluído no checkpoint.
