# Lotus — Redesign UX/UI

Data: 23/10/2026. Base: 633c14b. Solução, provedores e serviços existentes preservados. Nenhum motor de otimização ou módulo de hardware adicionado.

## Navegação e responsabilidade de cada área

| Área principal | Conteúdo e ações | Rotas preservadas |
| --- | --- | --- |
| Início | Único ponto para iniciar, repetir ou cancelar a análise. Resumo da última leitura de CPU/GPU/RAM, sem coleta decorativa. | dashboard → home |
| Otimizar | Resultado e próximos passos reunidos; detalhes técnicos expansíveis; Restaurar com estado vazio real. Nenhum botão de aplicação sem backend. | analysis/optimizations/advice → analysis; restore |
| Meus Jogos | Estado vazio sobre biblioteca/detecção ainda não implementada; nenhum falso botão de busca. | games |
| Desempenho | Guias Ao vivo, Comparar e Histórico. Só Ao vivo demanda sensores continuamente. | monitoring; benchmark; history |
| Configurações | Aparência, estado de monitoramento, integrações previstas, segurança/restauração, Avançado/logs e FAQ. Atalhos de monitoramento/restauração repetidos foram retirados. | settings, settings-monitoring, settings-integrations, settings-security, logs, faq |

A sidebar tem cinco itens, 232 DIP expandida e 64 DIP compacta. Seleção, hover e foco usam NavigationView nativo. Subáreas usam SelectorBar e carregamento x:Load; Configurações usa seletor de categoria. A seleção interna não recria serviços nem adiciona entradas ao backstack. Mudança de idioma mantém a rota exata.

## Fluxo real da análise

ExperienceViewModel orquestra a regra RAM existente e o stream singleton. O usuário inicia em Início, recebe progresso real, e ao concluir é levado automaticamente para Otimizar > Resultado. Não há aba extra de Orientações nem outra cópia dos comandos de análise. Cancelamento, falta de leituras ou timeout de 45 segundos geram resultado incompleto, nunca sucesso. Minimizar interrompe a análise; Ao vivo pausa conforme a demanda existente.

O escopo é explicitamente disponibilidade sustentada de RAM, não diagnóstico completo do computador. O resultado normal não declara FPS, saúde total ou suficiência para todos os jogos. Hardware no Início é a última leitura com horário; não cria assinatura própria. A regra, fontes e provedores não foram alterados.

OperationProgress é um componente visual reutilizável: relógio monotônico de tempo decorrido e barra dos 30 segundos de dados contínuos usados pela regra. Não é estimativa de término, contagem de FPS ou percentual inventado. Gaps/falhas reiniciam a janela válida; o relógio total continua. Existe um PeriodicTimer de apresentação a 1 Hz somente durante a operação, cancelado e aguardado ao encerrar. Não é loop de sensores nem timer por card ou por frame. Reduzir animações mantém indicador estático quando faltam dados, preservando o texto de estado.

A explicação “Por que acompanhar por 30 segundos?” foi retirada do card e do tooltip e movida para Configurações > FAQ, por solicitação do usuário. FAQ também explica escopo, segurança e futuras soluções. A área principal fica com etapa, tempo, barra e cancelamento.

## Visual e linguagem

Tokens de cores compartilhados: base #0C0B12, pane #111018, cards #18141E, elevada #211723, rosê #E584B1, textos #F7F1F6/#BDB1BC e bordas #493044. Estilos comuns para cabeçalho, card principal, cards compactos, legenda, títulos, badge e estados vazios. CardStyle centralizado no dicionário Foundation para não depender da ordem de carregamento de recursos posteriores. OnRoseBrush permite contraste correto de botões no fallback de alto contraste.

Linguagem pt-BR/en-US revisada para explicar decisões sem excesso de avisos, jargões ou metadados de risco que não se aplicam. Motivos técnicos ficam em detalhes/FAQ. Recursos futuros são identificados, sem switches ou controles que simulem funcionamento. Perfil Player, temas existentes, preferências e marca pixelada foram preservados. BrandPulse.cs tinha ajuste local de tempo anterior a esta tarefa; não foi refinado aqui e permanece fora do checkpoint do redesign.

## Verificações e evidências

- Build Debug e Release do fluxo/relógio: zero avisos/erros (12,13 s / 12,44 s). 101 testes aprovados por configuração, zero falhas/ignorados.
- FAQ adicionada depois: builds Debug/Release aprovados, zero avisos/erros (11,01 s / 12,04 s); 102 testes aprovados em Debug e Release, zero falhas/ignorados. TRX ux-faq-debug.trx e ux-faq-release.trx.
- Testes novos: cinco destinos e aliases antigos, guias/idioma, demanda por rota/visibilidade, análise real via regra, conclusão automática, retenção do resultado, timeout/cancelamento e parada do relógio sem progresso de dados ausentes. Fakes somente nos testes.
- Execução Release no Windows confirmada, janela respondendo. Reinício PID 8760: arquivo de preferências com hash preservado; pt-BR, Midnight, sidebar expandida e movimento habilitado carregados. Sem alteração de preferências do Windows.
- Usuário confirmou as cinco áreas/guias/sidebar, análise, troca imediata de tema/idioma e ausência de cortes em janela pequena/maximizada. Confirmou depois relógio, resultado direto e textos. A mudança final que transfere a explicação para FAQ ainda depende de conferência visual específica.
- Duas capturas fornecidas pelo usuário mostram Início/análise e a antiga aba de orientações; outra mostra a explicação temporal antes de ser movida. São evidências dessas revisões, não cinco capturas finais.
- Automação/captura/UIA nativa não iniciou, inclusive após reset: “windows sandbox failed: helper_unknown_error: setup refresh had errors”. Nenhuma captura final automática foi fabricada. A dimensão exata das janelas testadas manualmente não foi registrada: 600×480, 800×600, 1366×768, 1920×1080 e DPI 100/125/150% não são homologados individualmente por essa conferência.
- Logs de navegação e início/parada/fechamento disponíveis em artifacts/ux-redesign/runtime-events.json; preferências em restart-preferences.json. TRX em tests/Lotus.Tests/TestResults/ux-*.trx. Artefatos são locais e ignorados pelo Git.

## Pendências legítimas e próximos passos

Soluções automáticas, confirmação por plano, aplicação, verificação, rollback, biblioteca de jogos, benchmark e histórico persistente continuam no roadmap. Pedido registrado: após análise, levar diretamente ao resultado/soluções e, quando houver backend, permitir selecionar e confirmar ações ali, sem navegar por abas redundantes. Não executar automaticamente uma alteração somente por concluir a análise.

Após este incremento, o usuário autorizou uma camada ambiental Lótus + Aurora independente, preservando o layout. A implementação e o consumo dessa camada terão relatório próprio. Capturas finais, leitor de tela, matriz exata de DPI e ensaios visuais automatizados continuam pendentes quando a ferramenta estiver disponível.

Referências consultadas: [NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview) e [orientação Microsoft sobre alternativas ao Pivot](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/pivot). SelectorBar foi conferido no metadata WinUI 2.3.9 instalado; nenhum pacote novo.
