# Lotus — Etapa 01: fundação funcional e estabilidade visual

Validação local: 21/10/2026, Windows x64, escala 100%, SDK .NET 10.0.401. Origem: checkpoint `41fe06c`. Solução e funcionalidades anteriores preservadas. Nenhum pacote adicionado, privilégio administrativo solicitado pelo aplicativo ou configuração do Windows alterada.

## Resultado

Implementação e verificações locais aprovadas nos casos abaixo. A homologação completa de acessibilidade/DPI e o custo das animações continuam pendentes; não se declara aprovação dessas verificações por inferência. Não avançar automaticamente para a Etapa 02.

| Verificação | Resultado comprovado |
| --- | --- |
| Restore NuGet | Aprovado; dependências atualizadas, sem alteração de versões |
| Build final Debug/x64 | 0 erros, 0 avisos; 9,94 s |
| Build final Release/x64 | 0 erros, 0 avisos; 10,03 s |
| Testes finais Debug | 24 aprovados, 0 falhas, 0 ignorados; TRX 21:47:52 |
| Testes finais Release | 24 aprovados, 0 falhas, 0 ignorados; TRX 21:47:53 |
| Execução | Janelas WinUI reais abertas em Debug e Release; início/encerramento registrados |
| Navegação | Dez destinos exercitados individualmente: Dashboard, Análise, Otimizações, Monitoramento, Benchmark, Jogos, Histórico, Restaurar, Configurações e Logs |
| Repetição | Mesmo destino não recria página nem gera navegação extra; teste unitário e observação de UI |
| Layout | Janela inicial 1280 × 820, estreita aproximadamente 800 × 820, mínima 600 × 480 e maximizada 1920 × 1040, incluindo restaurar após maximizar |
| Sidebar | Expandida e recolhida; rodapé compacto inteiro; rolagem dos itens inferiores na altura mínima; seleção ativa e tooltips |
| Idiomas | Inglês → português no mesmo processo, textos/menu/opções atualizados; recursos e ida/volta testados |
| Temas | Grafite/Rosê → Preto/Rosê no mesmo processo; cores e seleção atualizadas |
| Persistência | JSON conferido após seleção; nova execução recuperou idioma, tema, sidebar recolhida e movimento habilitado |
| Teclado | Foco visível com Tab e alternância de Reduzir animações com espaço; não equivale a auditoria completa com leitor de tela |
| Logs | Sessões da correção a partir de 21:38:43 sem registros Error/Fatal na conferência; falhas anteriores de desenvolvimento preservadas no histórico |

Os screenshots e árvores de acessibilidade foram observados diretamente durante a sessão. São evidências assistidas de execução, não uma suíte automatizada de regressão visual. O estado compacto após reinício recebeu uma correção adicional de carregamento; foi retestado na compilação final Debug com itens visíveis e clique em Configurações funcional.

## Correções e melhorias

- Corte à esquerda após restaurar: conteúdo do ScrollViewer combinava Width vinculada ao viewport, MaxWidth e centralização. Substituído por largura natural limitada pelo viewport e margens simétricas calculadas na mudança de tamanho. Reprodução e restauração conferidas na janela real.
- Rodapé compacto usa somente ícone com nome acessível e tooltip; textos reaparecem quando expandido.
- Estado compacto restaurado depois de carregar o controle, evitando itens sem área de interação na inicialização. A redução automática da janela não sobrescreve a preferência da janela ampla.
- Preferências aplicadas e salvas no momento da seleção. Removidos botão Salvar redundante e aviso de reinicialização. Erro de gravação não anuncia sucesso e devolve controles ao estado persistido.
- ComboBox de tema usa opções vinculadas ao ViewModel; mantém texto selecionado após trocar idioma.
- Marca Lotus com pulsação discreta de opacidade e escala no compositor (4,4 s); entrada de páginas com fade de 180 ms e deslocamento de 220 ms. Sem timers por frame. Reduzir animações e a preferência de movimento do sistema desativam os efeitos; marca pausa com janela inativa.
- Templates compartilhados, cabeçalhos acessíveis e conteúdo adiado por `x:Load`. Dashboard não é instanciado nas páginas de módulos.
- Durante desenvolvimento, assinatura de HighContrastChanged causou COM 0x80070490 na inicialização; retirada. Consulta estática de paleta permanece. Tentativa de build com instância antiga aberta encontrou MSB3027/MSB3021; encerramento normal liberou o arquivo e a recompilação passou. Não foi necessário apagar binários nem recriar a solução.

## Arquivos

- App: `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `SectionPage.xaml`, `SectionPage.xaml.cs`, `ThemePalette.cs`, `BrandPulse.cs`, `Controls/Foundation.xaml`.
- Core: `Lotus.Core.csproj`, `Navigation.cs`, `Preferences.cs`, `SectionViewModel.cs`, `Localization/TextCatalog.cs`, `Localization/Strings.resx`, `Localization/Strings.en-US.resx`.
- Infrastructure: `JsonPreferencesStore.cs`.
- Tests: `LocalizationTests.cs`, `PreferencesTests.cs`; seis testes anteriores preservados, 24 resultados totais.
- Documentação: README, arquitetura, roadmap, decisões técnicas, validação de desempenho, direção visual e este relatório.

O backup preexistente `Lotus.slnx.backup-20261021-200919-583` permanece preservado e fora do checkpoint desta etapa. A solução não precisou ser modificada. O hash do checkpoint pode ser consultado por `git log -1 --oneline`; é apresentado no relatório final da entrega.

## Comandos e artefatos

Na raiz da solução, com o Lotus fechado antes de recompilar a mesma configuração:

```powershell
dotnet restore Lotus.slnx -p:Configuration=Debug -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
dotnet restore Lotus.slnx -p:Configuration=Release -p:Platform=x64
dotnet build Lotus.slnx -c Release -p:Platform=x64 --no-restore
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug --no-build --no-restore --logger trx --results-directory artifacts/etapa01/tests-debug
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Release --no-build --no-restore --logger trx --results-directory artifacts/etapa01/tests-release
```

Logs de build e TRX: `artifacts/etapa01/` (artefatos locais ignorados no Git). Executável Debug: `src/Lotus.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lotus.App.exe`; Release usa a pasta equivalente. Visual Studio: abrir `Lotus.slnx`, selecionar Lotus.App como inicial, Debug/x64 e F5.

## Limitações e próximo passo

Não homologados: DPI 125/150/200%, múltiplos monitores, Narrador, alto contraste completo/dinâmico, desempenho de renderização, consumo em repouso e concorrência de várias instâncias. Alto contraste é consultado ao iniciar/aplicar tema; não se garante atualização ao alterar essa opção do sistema com o app aberto. Sem dependência nova, mas sem prova quantitativa de custo das animações.

Funciona hoje: shell, rotas, estados vazios específicos, preferências, localização, temas e logging. Score, sensores, benchmark, otimizações, biblioteca de jogos, restauração e visualizador de logs continuam planejados. O perfil Player ainda não é editável. O template de carregamento está disponível para futuras operações reais, sem spinner fictício em uso.

Recomendação para Etapa 02, após aceite: primeira fatia de monitoramento CPU/RAM somente leitura, provedores independentes, dados ausentes explícitos, cancelamento e medição de custo. Encerrar antes as pendências de homologação visual necessárias ao público de destino.
