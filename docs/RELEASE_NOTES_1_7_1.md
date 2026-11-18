# Lotus 1.7.1 — Fluidez por jogo e melhorias de desempenho

Mais controle sobre a fluidez dos jogos, com ajustes opcionais e uma interface mais organizada.

- **Hz por jogo:** escolha uma taxa suportada pelo monitor, mantendo a resolução. O Lotus aplica a troca enquanto o jogo está em primeiro plano e restaura a taxa anterior ao dar Alt+Tab ou fechar. Mantenha o Lotus aberto.
- **RivaTuner por jogo:** modo automático ou escolha manual entre Front Edge Sync, Back Edge Sync, Async e NVIDIA Reflex, conforme compatibilidade. Salve antes de abrir o jogo.
- **Diagnóstico de frametime:** ponto de medição alinhado ao modo do limitador, preservando oscilações e travadas reais.
- **Interface reorganizada:** configurações gráficas junto aos cards; ações de restaurar originais, preparar shaders e iniciar jogo no rodapé. A preparação de shaders continua opcional e exige confirmação.
- **Menos trabalho em segundo plano:** melhorias na liberação de páginas e imagens e no retorno do Lotus após fechar o jogo, preservando os serviços dos recursos ativos.
- **Cena de testes unificada:** flor Lotus substitui a cena antiga de esferas, com animação baseada no tempo real.
- **Lotus Score corrigido:** calibração compatível com a versão distribuída, preservação do histórico e identificação dos resultados afetados. Repita o teste para obter uma pontuação atualizada.

**Validação:** 812 testes aprovados em Debug/x64 e 812 em Release/x64. Também foram realizadas seis capturas reais no Black Myth: Wukong Benchmark Tool para comparar os modos do limitador.

**Observações:** os resultados variam por jogo e computador. Nenhum modo garante ausência de stuttering ou uma linha perfeitamente reta. As configurações gráficas aplicáveis dependem da integração com cada jogo; recomendações de upscaling podem exigir ajuste no próprio jogo. Não há promessa de ganho universal de FPS, redução percentual de RAM ou latência de entrada medida.

**Instalação:** use `Lotus-Setup-1.7.1.exe` para instalar ou atualizar. O instalador preserva os dados locais do jogador. Disponível para Windows x64; instalador sem assinatura digital.
