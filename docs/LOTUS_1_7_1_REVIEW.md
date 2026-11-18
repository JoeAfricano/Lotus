> CORREÇÃO POSTERIOR: a calibração de CPU descrita abaixo foi obtida em Debug e não é válida para a escala Release distribuída. A baixa dispersão não detectou esse erro. A revisão 3 foi recalibrada em Release e validada pelo fluxo real; consulte [método e evidências](LOTUS_SCORE_METHODOLOGY.md). Os registros abaixo são históricos e não certificam a calibração anterior. Suíte atual: 774 aprovados em cada configuração.

# Lotus 1.7.1 — memória, retorno do jogo e cena unificada

## Alterações

- Banners passam a carregar a imagem necessária, sem antecipar o download e a decodificação de todas as fotos. O cache de bitmaps usa referências fracas e tem limite de 16 chaves; falhas têm limite de 64. Eventos de imagem se desligam ao concluir/falhar ou descarregar. A lista de slides em cache deixa de capturar a SectionPage e sua árvore visual.
- A arte do detalhe do jogo tem limite de decodificação de 1600 pixels e interrompe o timer de fallback ao descarregar.
- A página oculta é liberada após cerca de 3 segundos, em vez de depender do polling e da espera anterior de 20 segundos. View models e serviços permanecem vivos. Há uma solicitação otimizada de coleta em segundo plano, sem bloquear/compactar e sem esvaziar o working set. Não há limpeza global de RAM, encerramento de aplicações do jogador ou GC periódico forçado.
- Ao voltar, a prioridade normal do Lotus é restaurada antes de reconstruir a página. O fechamento do processo do jogo sinaliza uma nova verificação imediatamente, sem aumentar a frequência do polling de 10 segundos. O PID é conferido com a data de início; a biblioteca é reconsultada para reconhecer a troca de launcher para jogo. Fallback periódico e exclusão de verificações simultâneas são preservados.
- Overlay, fontes de dados, frequência de sensores, cálculo de FPS/lows/frametime, diário, atalhos, ajustes e restauração não são removidos. Medições e aplicação de ajustes bloqueiam liberação de página/economia enquanto estão ativas.
- Flor em todos os caminhos de teste: cena de CPU renderizada de verdade pelo processador (16 pétalas, sombras, reflexos e céu), e shader floral compartilhado por GPU, ajuste e prévia. Cena antiga de esferas removida. A animação agora usa tempo real; lotes adaptativos mantêm o teste responsivo. CPU single-thread continua limitado à velocidade real de renderização do processador; não exibe uma animação fictícia como medição.
- A pontuação de GPU mede o trabalho enviado até a confirmação de conclusão antes de Present, excluindo a espera do compositor/limite de FPS. Isso é throughput da carga, não o FPS apresentado do overlay. O limite global do RTSS não foi modificado.
- Lotus Score revisão de carga 2: novas medições são calibradas para a nova cena; resultados antigos ficam no histórico com identificação de cena anterior e não alimentam o resultado atual ou estimativas da nova carga. Nada do histórico é apagado. Referências de outras máquinas continuam estimativas, como antes.

## Testes executados

- 767 testes aprovados em Debug/x64 e 767 em Release/x64, sem falhas ou ignorados.
- Compilação do aplicativo Debug/x64 sem avisos/erros; publicação Release/x64 e instalador local pelo fluxo existente.
- Testes novos: saída real de um processo filho, cancelamento sem encerrar processo, proteção contra PID reutilizado, JSON de histórico anterior, separação de revisões sem apagar dados, pixels finitos da cena floral. Suíte existente cobre sensores, overlays, persistência e restauração.
- CPU real Ryzen 5 5500: 1 aquecimento + 3 medições de 3 segundos, por modo. Medianas: single 312,1951 e multi 3102,9013 unidades brutas, com dispersões aproximadas de 0,89% e 2,05%.
- GPU real RTX 5060: após corrigir a medição para excluir Present, 1 aquecimento + 3 medições de 3 segundos. Valores 1200,7296 / 1188,3804 / 1174,6989; mediana 1188,3804 e dispersão aproximada de 2,19%. Dados completos em flower/gpu-work-confirmation.json. Ensaios anteriores que mediam a espera do display ficam como evidência do problema, não como a calibração final.
- A imagem cpu-flower.png foi produzida pelo renderizador real de CPU. Não representa uma captura de jogo.

## Limitações e entrega

- A instância de desenvolvimento abriu, mas a ferramenta de UI recusou a vinculação: "window id 1509114 no longer belongs to ...; current owner is ..." com o mesmo identificador instalado para as duas instâncias. Nenhum clique foi enviado à janela ambígua; só o processo de teste criado nesta revisão foi encerrado, preservando o instalado.
- A observação isolada de memória foi feita na Home, sem jogo, antes do último incremento da cena. NÃO é comparação A/B. Não há comprovação de redução percentual de RAM, ganho de FPS ou latência exata de retorno em um jogo real nesta revisão.
- A instalação/atualização não foi executada automaticamente. Não foi repetida uma busca física completa de undervolt/overclock. O instalador local não tem assinatura digital.
- Nenhum push, tag ou release foi publicado. Publicação manual pelo usuário. Fonte comum para Claude Code e Codex, preservando as alterações locais anteriores.
