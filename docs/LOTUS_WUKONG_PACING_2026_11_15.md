# Wukong: cadência e sincronização — 15/11/2026

Seis passadas reais no Black Myth: Wukong Benchmark Tool, a pedido do usuário. Captura PresentMon 2.6.0 com hash conferido, 185 s por passada. Arquivos CSV, condições, perfis aplicados, logs e hashes em `artifacts/pacing-wukong-20261115/`. Nenhum dado simulado ou filtro de suavização.

## Condições

Ryzen 5 5500, RTX 5060 8 GB, driver 621.24, 16 GB RAM. 1920 × 1080, janela sem bordas, monitor 74,973 Hz, RTSS 74,97 FPS, V-Sync ligado, qualidade Alta, DLSS, sem geração de quadros e sem ray tracing. Mesmo processo/cena entre passadas; configurações gráficas e taxa do Windows preservadas. A primeira passada pode conter aquecimento/cache diferente das seguintes; ordem não aleatorizada. Não é homologação de todos os jogos, AMD ou 60 Hz.

O coletor oficial foi o único processo elevado. Cada perfil foi lido após gravação e o recarregamento solicitado pela API oficial `UpdateProfiles`. Nenhuma interface foi manipulada durante o trecho comparado. Não foi registrado foco continuamente; portanto não se exclui interferência externa com certeza. Não foram executadas compilações/builds durante as capturas.

## Duas janelas fixas, preservadas no resultado

A janela principal, 12–154 s, preserva a fase inicial. Após constatar que os maiores picos se concentravam aproximadamente em 12–16 s, foi acrescentada uma análise secundária de **30–150 s para todas as passadas**, sem remover pontos dentro dela. A janela principal continua no JSON e os CSV completos foram preservados. Assim, não se oculta um início lento para declarar o modo vencedor.

| Configuração | >50 ms em 12–154 s | >50 ms em 30–150 s | P99 do desvio absoluto de exibição (ms), 30–150 s | Exibições >1,5× mediana, 30–150 s | Mediana Present→exibição (ms), 30–150 s |
|---|---:|---:|---:|---:|---:|
| Front Edge, espera passiva | 2 | 0 | 0,051 | 2 | 20,45 |
| Front Edge, espera ativa, passada 1 | 0 | 0 | 0,116 | 2 | 22,89 |
| Front Edge, espera ativa, repetição | 5 | 0 | 0,057 | 2 | 18,67 |
| Back Edge, espera ativa | 5 | 0 | 0,067 | 3 | 25,22 |
| Async, espera ativa | 0 | 0 | 0,175 | 6 | 22,45 |
| NVIDIA Reflex | 3 | 0 | 0,100 | 3 | 18,50 |

As medianas de exibição ficaram próximas de 13,338 ms em todos os modos. O desvio P99 aqui é o percentil nearest-rank de `abs(intervalo − mediana)`, não a latência. Intervalos de exibição ausentes/zero não são convertidos em medições; quantidade e descartes ficam no JSON. Picos acima de 50 ms não foram classificados automaticamente como compilação de shader: a captura não identifica essa causa.

**Conclusão:** Front Edge com espera passiva é uma base razoável para priorizar cadência sem custo extra de espera ativa. A espera ativa não apresentou benefício consistente que justificasse torná-la padrão. Reflex teve menor atraso de apresentação nesta amostra, com cadência de exibição também próxima, e permanece opção para NVIDIA compatível e preferência quando há geração de quadros NVIDIA. O teste não prova um vencedor universal nem garante ausência de stuttering. A variação entre as duas passadas Front Edge ativas impede atribuir os picos grandes somente ao limitador.

`msUntilDisplayed` mede o caminho do Present até a exibição. Exclui amostragem de entrada e espera anterior ao Present, que varia com a estratégia. Portanto não é click-to-photon e não demonstra sozinho a menor latência de entrada. Não há equipamento óptico nesta validação.

## Mudanças no Lotus

- Aplicação de configurações gráficas fica junto dos respectivos cards; salvar fluidez continua no card de Hz/RivaTuner.
- Rodapé: restaurar originais, preparar shaders e iniciar jogo. Restaurar também desativa o plano de fluidez salvo e restaura o perfil RTSS anterior. Preparar shaders é ação explícita, com a confirmação já existente; não roda automaticamente ao iniciar o jogo.
- Alterar opções de fluidez mostra aviso de alterações não salvas e impede iniciar pelo rodapé até salvá-las.
- O modo automático explicita a prioridade de cadência com resposta, mantendo Front Edge como base e Reflex para geração de quadros NVIDIA compatível. Não se afirma uma calibração automática de todos os modos em cada jogo.
- Ao salvar o modo, configura-se `Statistics/FrametimeCalc=1` para Front Edge e `0` para os demais. O valor é conferido em arquivo. Isso alinha o ponto de diagnóstico à estratégia; não suaviza dados nem elimina travadas reais. Não altera o perfil Global.
- Espera ativa não foi imposta ao jogador. Preferências e histórico locais existentes foram preservados.

## Fontes e integridade

Ajuda oficial instalada do RTSS: `Help/Properties/General/SYNC_LIMITER`, `FRAMETIME_CALC` e `ENABLE_PASSIVE_WAITING`. Explica as diferenças entre início de quadro, Present e retorno, além do custo de CPU da espera ativa.

[Notas oficiais do CapFrameX com explicação do autor do RTSS](https://github.com/CXWorld/CapFrameX/releases/tag/v1.7.4): não existe ponto de medição universalmente melhor; deve corresponder ao limitador. [PresentMon](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md): métricas de apresentação e exibição são distintas de latência óptica completa.

Todas as seis capturas terminaram com código 0. As colunas de perda ETW do CSV vieram vazias; os relatórios periódicos do coletor registraram EventsLost=0, BuffersLost=0 e OverflowedPresents=0. São os contadores reportados, sem transformar campos ausentes em zero. O perfil original de Wukong foi restaurado e conferido byte a byte (`restored.json`, Restored=true). Não houve mudanças permanentes de Hz, driver, EDID ou CRU.

Verificação final: todos os CSV contêm somente PID 15508, o benchmark real na instalação Steam. Depois das capturas, uma tarefa externa `scskillerw.exe rewarm-stale --if-driver-changed` iniciou às 11:25, com trabalhador Wukong às 11:25:23; a última captura havia terminado às 11:19:15 (America/Sao_Paulo). A fila foi encerrada novamente antes de reabrir o Lotus. A agenda do Windows não foi alterada.

Validação do código: 812 testes aprovados em Debug/x64 e 812 em Release/x64; builds WinUI Debug/x64 e Release/x64 sem avisos ou erros. Resultados em `tests/Lotus.Tests/TestResults/pacing-ui-debug.trx` e `pacing-ui-release.trx`. A inspeção automatizada da janela Lotus continua limitada pelo identificador duplicado da instalação e da compilação local, já registrado na documentação anterior; não se declara teste visual do novo rodapé aprovado. A Release é aberta para conferência do usuário, sem instalador ou publicação.
