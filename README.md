# Lotus

Aplicativo nativo Windows em C#/.NET 10 e WinUI 3, com MVVM, injeção de dependências e logging estruturado. Fundação visual em preto, grafite e rosê. Cinco áreas: Início, Otimizar, Meus Jogos, Desempenho e Configurações. CPU/RAM e GPU NVIDIA são coletadas em Desempenho > Ao vivo ou durante uma análise iniciada pelo usuário. Início não mantém sensores ativos. Não calcula score, executa benchmark, limpa memória/arquivos ou altera configurações do Windows.

[Fundo ambiental Lótus + Aurora: política, testes e consumo](docs/LOTUS_AMBIENT_BACKGROUND.md). Em execução normal, respeita as preferências de movimento do Windows e do Lotus.

[Redesign UX, progresso temporal, FAQ e validação](docs/LOTUS_UX_REDESIGN.md). A análise leva diretamente ao resultado; ações automáticas continuam futuras.

Diagnóstico inicial de RAM: [regra, evidências, 77 testes por configuração e plano de jogos](docs/LOTUS_RAM_DIAGNOSTICS.md). Dados e reinício da contagem confirmados pelo usuário; não comprova gargalo de jogo.

Estado atual: [Etapa 03 — resultados e verificações pendentes](docs/LOTUS_STAGE03_VALIDATION.md). CPU/RAM preservados e GPU NVIDIA por NVML do driver instalado, somente leitura. Interface compacta com detalhes expansíveis e gráficos reais dos últimos 60 s. Ausência aparece como travessão/status; nenhum dado simulado no aplicativo. Não há driver adicional ou novo pacote NuGet. [Fontes, unidades e limitações de GPU](docs/LOTUS_GPU_TELEMETRY.md). A validação visual atual depende de conferência manual porque a ferramenta de inspeção Windows não iniciou nesta sessão. [Etapa 02](docs/LOTUS_STAGE02_VALIDATION.md) e [Etapa 00](docs/LOTUS_STAGE00_AUDIT.md) preservadas como histórico.

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
- `Lotus.Core`: navegação, localização, preferências, snapshots, cálculos, serviço compartilhado e ViewModels testáveis.
- `Lotus.Infrastructure`: JSON de preferências, eventos Serilog e provedor Windows nativo de CPU/RAM e adaptador NVIDIA NVML.
- `Lotus.Tests`: navegação, recursos, autosave, falhas de arquivo, backup, logging, CPU/RAM, GPU, histórico, ciclo de vida e falhas de provedores.

Configurações: português inicial e inglês; temas Preto/Rosê e Grafite/Rosê; redução de movimento; sidebar persistente na janela ampla. Aplicação e gravação imediatas, sem botão Salvar ou reinício. Arquivo `%LOCALAPPDATA%/Lotus/preferences.json`; versão anterior `.bak`. SQLite é futuro para sessões/histórico, sem dependência adicionada agora.

Logs: `%LOCALAPPDATA%/Lotus/Logs/lotus-AAAAMMDD.jsonl`, rotação diária/5 MB/sete arquivos. A página Logs informa o local; visualizador ainda planejado. Perfil Player não editável. Demais módulos exibem sua condição de planejados, sem métricas simuladas.

Mínimo de janela 600 × 480 DIP com rolagem; menu compacto em telas estreitas. Marca Lotus neon com esmaecimento e pequenos pixels luminosos no compositor; páginas com entrada curta. Reduzir animações mantém o neon estático. Não há alegação de ganho de FPS/consumo.

Veja [arquitetura](docs/LOTUS_ARCHITECTURE.md), [decisões](docs/LOTUS_TECHNICAL_DECISIONS.md) e [roadmap](docs/LOTUS_ROADMAP.md). Não avançar automaticamente para a próxima etapa.

Checkpoints usam a identidade técnica local Lotus Development <lotus@localhost> apenas no comando de commit; configuração global Git permanece intacta.


Ensaio prolongado do coletor real (opcional; não substitui observação da UI):

```powershell
$env:LOTUS_GPU_SOAK_SECONDS = '900'
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Release --filter "FullyQualifiedName~NativeGpuContinuousSessionKeepsHistoryBoundedAndReleases" --logger trx
Remove-Item Env:LOTUS_GPU_SOAK_SECONDS
```

Sem essa variável o teste faz um ensaio curto de 2 s; a suíte comum não exige esperar 15 minutos. Em máquinas sem NVML instalada, os testes de integração específicos são marcados como ignorados, sem tentar instalar nada; testes de contratos/falhas continuam válidos. Nesta máquina NVIDIA a integração foi efetivamente executada.
