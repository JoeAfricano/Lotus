# Lotus

Aplicativo nativo Windows em C#/.NET 10 e WinUI 3, com MVVM, injeção de dependências e logging estruturado. Fundação visual em preto, grafite e rosê. Não coleta hardware, calcula score, executa benchmark, limpa arquivos ou altera configurações do Windows nesta etapa.

Estado atual: [Etapa 01 — resultados e limitações](docs/LOTUS_STAGE01_VALIDATION.md). Builds Debug/x64 e Release/x64 sem erros/avisos; 24 testes aprovados em cada configuração. Navegação nas dez áreas, preferências, idiomas e temas em tempo real inspecionados no Windows. DPI adicional, leitor de tela, alto contraste completo e consumo continuam pendentes. [Etapa 00](docs/LOTUS_STAGE00_AUDIT.md) preservada como histórico.

## Abrir e executar

Abra `C:/Projetos/Lotus/Lotus.slnx` no Visual Studio. Defina Lotus.App como projeto inicial, Debug/x64 e F5. A pasta antiga `Lotus/` foi preservada e não pertence à solução atual.

Na raiz do projeto, com a instância da mesma configuração fechada:

```powershell
dotnet restore Lotus.slnx -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
& .\src\Lotus.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Lotus.App.exe
```

Testes:

```powershell
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug --logger trx
```

Requisitos: Windows x64, SDK .NET 10.0.401, Windows SDK 10.0.26100.0 e runtime .NET 10. Windows App SDK incluído na saída. Executar sem privilégios administrativos. Build não equivale a instalador; o primeiro restore precisa de acesso ao NuGet.

## Componentes existentes

- `Lotus.App`: shell WinUI, Frame, templates compartilhados, layout adaptativo e animações do compositor.
- `Lotus.Core`: navegação, localização pt-BR/en-US, preferências e ViewModels testáveis.
- `Lotus.Infrastructure`: JSON de preferências e eventos com Serilog.
- `Lotus.Tests`: navegação, recursos, autosave, falhas de arquivo, backup e logging.

Configurações: português inicial e inglês; temas Preto/Rosê e Grafite/Rosê; redução de movimento; sidebar persistente na janela ampla. Aplicação e gravação imediatas, sem botão Salvar ou reinício. Arquivo `%LOCALAPPDATA%/Lotus/preferences.json`; versão anterior `.bak`. SQLite é futuro para sessões/histórico, sem dependência adicionada agora.

Logs: `%LOCALAPPDATA%/Lotus/Logs/lotus-AAAAMMDD.jsonl`, rotação diária/5 MB/sete arquivos. A página Logs informa o local; visualizador ainda planejado. Perfil Player não editável. Demais módulos exibem sua condição de planejados, sem métricas simuladas.

Mínimo de janela 600 × 480 DIP com rolagem; menu compacto em telas estreitas. Marca Lotus com pulsação discreta; páginas com entrada curta. Reduzir animações desativa os efeitos. Não há alegação de ganho de FPS/consumo.

Veja [arquitetura](docs/LOTUS_ARCHITECTURE.md), [decisões](docs/LOTUS_TECHNICAL_DECISIONS.md) e [roadmap](docs/LOTUS_ROADMAP.md). Não avançar automaticamente para a próxima etapa.

Checkpoints usam a identidade técnica local Lotus Development <lotus@localhost> apenas no comando de commit; configuração global Git permanece intacta.
