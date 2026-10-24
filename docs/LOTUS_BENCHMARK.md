# Lotus Benchmark — primeira entrega funcional

Validação local: 24/10/2026. Escopo: captura real, resultado, armazenamento e comparação inicial em Desempenho > Comparar. Marca e fundo ambiental permanecem em standby. Não há otimizações ou integração RTSS/Afterburner nesta entrega.

## Tecnologia e limites de confiança

`IFrameCaptureProvider` isola o PresentMon CLI 2.6.0 do Core e da interface. A CLI evita incorporar um serviço/SDK nesta primeira entrega; permite preservar CSV e controlar o ciclo de vida sem injeção no processo observado. O adaptador pode ser substituído posteriormente sem refazer a UI.

`tools/Install-PresentMon.ps1` obtém a versão oficial por usuário, verifica SHA-256 e Authenticode e mantém LICENSE.txt/THIRD_PARTY.txt. O provedor revalida o hash antes de executar. Hash fixado: `B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF`. Nenhum executável do PresentMon é incorporado ao Git.

Fonte: [PresentMon 2.6.0](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0), [documentação CLI](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md), [licença MIT e obrigações](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/LICENSE.txt), [terceiros](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/THIRD_PARTY.txt). CapFrameX foi consultado como referência, sem copiar código.

Novas dependências: Microsoft.Data.Sqlite 10.0.13 (MIT) e SQLitePCLRaw.bundle_e_sqlite3 3.0.5 (Apache-2.0), que inclui SQLite 3.53.4 (domínio público, conforme LICENSE.txt do pacote local). A versão inicial vulnerável foi substituída antes da validação final; restore final sem aviso NuGet. Antes de distribuição, reunir os notices de todos os pacotes transitivos e da biblioteca nativa.

## Contratos e coleta

- Core: `BenchmarkModels`, `BenchmarkMath`, `BenchmarkViewModel`, `IFrameCaptureProvider`, `IBenchmarkRepository`, `ICaptureTargetSource`.
- Infrastructure: `PresentMonCaptureProvider`, `PresentMonCsv`, `SqliteBenchmarkRepository`, `WindowsCaptureTargets`.
- App: `BenchmarkPanel` e `FrametimeChart`; DI singleton e encerramento coordenado no App. Recursos pt-BR/en-US. Gráfico com até 600 pontos de envelope de picos; cálculos usam os intervalos completos antes da redução visual.
- Identidade por executável, PID e início do processo; revalidada antes da execução. Lista inicial limitada a processos com janela acessível.
- Um coletor por provedor, assíncrono, cancelável e com nome de sessão ETW exclusivo. Duração 10–120 s; buffers/arquivos limitados a 300 mil linhas, 64 swap chains e CSV de 128 MiB.
- Argumentos: `--process_id`, `--output_file`, `--session_name`, `--timed`, `--terminate_after_timed`, `--no_console_stats`, `--no_track_input`, `--no_track_gpu`, `--track_frame_type`. Não coleta sensores novos para o benchmark.
- A navegação/minimização não cancela uma captura explicitamente iniciada: o jogo pode estar em outra janela. Fechar o Lotus solicita parada; após falha abrupta do Lotus, o limite temporal do coletor continua sendo a salvaguarda, sem garantia de parada imediata.
- Acesso ETW sem elevação foi negado nesta máquina. A autorização explícita eleva somente o coletor verificado; UAC permanece sob controle humano. Parar antes do limite pode solicitar nova confirmação. Não altera grupos, serviços ou configurações do Windows.

## Definições estatísticas — present-interval-v1

Fonte: `TimeInMs` e `MsBetweenPresents` do CSV validado. Mede cadência de apresentação da aplicação, não FPS efetivamente exibido pelo monitor nem tempo de GPU. A primeira linha de cada swap chain é descartada; seleciona-se a cadeia com mais amostras válidas, sem concatenar cadeias diferentes.

Para N intervalos positivos finitos dt em ms: FPS médio = `1000*N/sum(dt)`; duração medida = `sum(dt)/1000`. 1% low = `1000/mean(os ceil(N*0,01) maiores dt)`. P95/P99 usam nearest rank, índice `ceil(p*N)-1`. O 1% low não é o inverso do P99. Pico = dt maior que `max(50 ms, 3*mediana)`, uma heurística descritiva, não diagnóstico de stuttering.

São necessários simultaneamente 1.000 intervalos e 10 s de duração medida. Não há remoção de outliers. Linhas inválidas, limite excedido ou perda ETW reportada impedem estatísticas. Sessões insuficientes/falhas preservam evidência sem inventar números.

Tipos de frame diferentes de `Application` são excluídos e registrados. Entretanto, o PresentMon também mapeia alguns tipos não classificados/repetidos para Application: a distinção de frames gerados não é completa. Não anunciar FPS renderizado ou ganho por frame generation com estes dados.

**Limitação conhecida de integridade:** o CSV padrão 2.6.0 validado não expõe contadores de perda ETW. A flag experimental de status foi investigada e rejeitada: no schema legado produziu cabeçalho incompatível com as linhas; no padrão não forneceu os contadores. A UI informa a ausência da verificação. `Completed` indica processamento concluído, não comprova ausência de perda de eventos.

Comparação exige sessões distintas, concluídas, mesmo caminho de executável, versão do provedor e algoritmo. Diferença absoluta = atual−referência; percentual = `100*(atual/referência−1)`. Condições são anotações do usuário; hardware usa a última telemetria disponível com horário, sem nova coleta. Driver não disponível fica explícito. Compatibilidade de esquema não comprova condições experimentais equivalentes.

## Persistência e recuperação

`%LOCALAPPDATA%/Lotus/benchmarks.db`, SQLite com `user_version=1`, migração transacional e rejeição de schema futuro. Uma transação por sessão, sem escrita por frame; acesso fora da UI e serializado. JSON no banco inclui metadados, métricas e preview limitado. Últimas 100 sessões visíveis; registros anteriores não são excluídos.

Cada pasta `%LOCALAPPDATA%/Lotus/Benchmarks/<id>/` conserva request.json, frames.csv quando produzido, SHA-256 e result.json. Falha de banco não elimina o CSV; recuperação automática/importação ainda não tem interface. Fundamentos: [transações SQLite](https://www.sqlite.org/lang_transaction.html). Dados podem conter nomes/caminhos locais; não são enviados a terceiros pelo Lotus.

## Evidências executadas

Ambiente: Windows 11 x64, Ryzen 5 5500 (12 processadores lógicos), RTX 5060, aproximadamente 16 GiB. Workload gráfico WPF/D3D real e animado, janela 800×500; ferramenta separada `tools/BenchmarkValidation`, fora da solução de produção. Não foi uma sessão de jogo comercial.

| Sessão real na UI | Intervalos | Duração medida | FPS médio | 1% low | P95/P99 |
| --- | ---: | ---: | ---: | ---: | --- |
| 197fac83-380e-469e-9cfe-0bf1234c0ab6 | 4431 | 59,915 s | 73,9545 | 31,5503 | 13,7355 / 13,9928 ms |
| 6c430d4b-ea45-4ec3-bfe2-5ec1f2f2b1c0 | 1652 | 22,035 s | 74,9727 | 71,2722 | 13,6981 / 13,9267 ms |

Os cálculos das duas sessões foram reproduzidos a partir do CSV por `tools/Verify-BenchmarkCsv.py` (Python standard library), todos coincidentes. Essa é verificação aritmética independente, não um segundo coletor ETW. Evidências locais em `artifacts/benchmark/lotus-ui-session.json`, `lotus-ui-reference.json` e `lotus-ui-second-session.json`; artefatos ficam fora do Git.

Computer Use capturou a execução Release e selecionou a referência de 10:07 para a sessão de 10:11. Screenshot e árvore acessível mostraram: FPS +1,02 (+1,4%); 1% low +39,72 (+125,9%); P95 −0,04 ms (−0,3%); P99 −0,07 ms (−0,5%). Durações diferentes e condições sem anotação impedem atribuir isso a uma melhoria. As capturas visuais estão registradas na conversa de validação.

Ensaios técnicos: captura concluída e SQLite salvo/reaberto; cancelamento em aproximadamente 6 s e encerramento do processo alvo em aproximadamente 6 s produziram estado insuficiente, sem métricas fabricadas. CSV e resultado preservados. Evidências em `artifacts/benchmark/final-capture`, `stop-capture` e `target-exit`. Encerramento normal do Lotus confirmado pelo log `Application closed`, sem processo Lotus/PresentMon remanescente naquele momento; Release reaberto com sucesso. Após reiniciar, o seletor da UI recuperou as duas sessões concluídas e as tentativas negadas do SQLite.

## Consumo observado e alcance

Na sessão de 60 s da UI: 59 amostras em 59,120 s; Lotus CPU média 0,4383% normalizada pelos 12 processadores lógicos, working set médio 213,61 MiB. Coletor 0,0044% e 11,98 MiB. A medição termina com a coleta e não inclui todo o pós-processamento; resolução dos contadores limita leituras muito baixas.

Baseline separado: 20 amostras em 20,225 s, Lotus 0,0837% e 142,21 MiB. Não houve pareamento rigoroso de página, aquecimento/JIT, foco e duração: a diferença não mede isoladamente overhead do benchmark. Não foi medido impacto no FPS do workload nem custo de GPU do Lotus. Próxima homologação deve incluir rodadas controladas, jogos e APIs gráficas diferentes; não há alegação de ausência de vazamento ou custo nulo.

## Builds e testes já executados

Restore aprovado; Debug/x64 e Release/x64: zero avisos e zero erros. 120 testes aprovados em cada configuração (102 anteriores + 18 de benchmark), zero falhas. TRX: `tests/Lotus.Tests/TestResults/benchmark-debug.trx` e `benchmark-release.trx`. Cobrem fórmulas, limites, schema, frame types, cancelamento, falhas, migração, reabertura, duplicidade, schema futuro e ciclo de vida do ViewModel. Builds/testes não foram repetidos na conclusão documental porque o código aprovado não mudou.

Comandos: `dotnet restore Lotus.slnx -p:Platform=x64`; `dotnet build Lotus.slnx -c <Debug|Release> -p:Platform=x64 --no-restore -m:1 -nodeReuse:false`; `dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c <Debug|Release> --no-build --no-restore`.

## Resultado e próximos gates

Primeira prova funcional entregue: capturar, persistir, recuperar e comparar intervalos reais com limitações explícitas. Não representa homologação universal de jogos, anti-cheats, geração de frames ou perda ETW. Próximo passo recomendado: validar jogos reais e custo em condições controladas, mediante instrução; resolver contadores de perda antes de usar o benchmark como comprovação de otimizações. Histórico global, exportação e importação automática permanecem futuros.
