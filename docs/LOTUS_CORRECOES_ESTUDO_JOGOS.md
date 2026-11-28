# Correções e novidades do estudo de jogos (28/11/2026)

Branch `claude/project-thread-wk2aj0`, a partir do `main` f3a69ac. Backup: branch `backup/antes-estudo-jogos-2026-11-28` e stash "antes-estudo-jogos-2026-11-28 (copia)". Nada foi commitado, publicado ou instalado.

## O que mudou, item a item

| Item | O que foi feito | Onde |
|---|---|---|
| 1. Sessão fantasma | Processos auxiliares da pasta (CrashReportClient, UnrealCEFSubProcess, crash handlers, anti-cheat) e o lançador pequeno da UE (exe < 2 MB com um gêmeo maior em `\Binaries\`) não contam como o jogo. Só a janela de erro aberta = "o jogo travou": a sessão termina, Perfil Jogo/ajuste da placa/Modo Console voltam, o diário não conta esse tempo, não aparece "Onde eu parei?" e o ícone da bandeja mostra a linha. | `GameProcessRules.Role`, `WindowsGameProcessCheck.Scan`, `SessionEnd.Crashed`, `MainWindow.ReportCrash` |
| 2. Modo Console | Cada troca de taxa vai para o log (pedida, conseguida, desfeita, motivo, Hz exato). Depois da troca, a taxa é conferida 1 vez por segundo por 10 s e pedida de novo até 3 vezes (`RateGuard`). Se outro programa insistir, o Lotus para ("contested"), deixa a tela como está e avisa. A taxa só volta quando o processo do jogo fecha (Alt+Tab e janela de erro na frente não mexem mais nela). A bandeja mostra quando o Modo Console deveria estar ativo e não está. | `FocusDisplaySession`, `RateGuard`, `ForegroundGamePresentation`, `PresentationPlan.ConsoleModeShouldBeOn` |
| 3. RivaTuner | Quando um jogo que depende do RivaTuner é detectado (Steam, atalho, exe), o Lotus abre o RivaTuner, confirma que abriu e pede para reabrir o jogo. Uma recusa não é perguntada de novo por 30 min (não repete o UAC por cima do jogo). | `GamesViewModel.EnsureRtssForDetectedGameAsync` |
| 4. Avisos | Com jogo aberto, nenhum balão do Windows: ficam na fila e saem juntos num balão quando o jogo fecha. Ao abrir um jogo, um balão na tela some. Linhas discretas ficam na dica e no menu do ícone. | `NoticeQueue`, `TrayStatus`, `TrayIcon.SetStatus/ClearBalloon` |
| 5. Gravação | Se a última gravação não teve quadros ou perdeu eventos (ou sobrou uma sessão no registro), antes da próxima o ajudante elevado `--etw-clean` roda `logman` e para só sessões `Lotus*-<32 hex>`. Corrige o fuso de `--date_time` na leitura (as gravações do Lotus usam tempo relativo e não são afetadas). | `EtwSessionCleanup`, `EtwElevatedHelper`, `PresentMonCsv.LocalTime` |
| 6. Gerador de quadros | Cada medição compara os quadros mostrados com os desenhados (tempo de quadro da placa pelo Reflex; FrameType quando nomeia um gerador). Se não bate com a opção "Uso o gerador", o resultado e a tela do Modo Console avisam. | `FrameGenCheck`, `RtssPreferredCaptureProvider` |
| 7. Ganchos | Já existia na 1.0.6. Acrescentados `d3d9.dll` e `winmm.dll`; cada desligar/religar vai para o histórico de Ferramentas. | `GameHooks.Toggle` |
| 8. Compressão de memória | Já existia completo na 1.0.6 (detecção, até 16 GB, UAC, reinício, desfazer). Nada mudou. | — |
| 9. Latência no Neon | Opção A (escolhida pelo José): fórmula do RivaTuner `reflexlatency(0,12)`. Opção em Meus Jogos › Overlay, desligada por padrão. Ligada, a cópia exportada do Neon ativo (`Lotus-Premium-neon.ovl`) ganha 3 fontes e 2 camadas ("LAT n ms" e "LAT —", com VisibilitySource) em X 1789, Y 98, tamanho 28, branco; só os contadores Sources/Layers mudam no texto original. Os arquivos do projeto não foram editados; backup em `artifacts\backup-overlay-2026-11-28`. Sem média móvel própria: o RivaTuner renova a cada 250 ms (RefreshPeriod do Neon). | `LotusOverlayLayout.WithLatency`, `OverlayStylePicker` |
| 10. Limite abaixo da tela | Nova causa no "Por que está travando?" com as duas saídas medidas; escolhe pelo tempo da placa por quadro (< ~13 ms a 75 Hz). Sem esse tempo, mostra as duas. | `StutterDiagnosis.GameCapBelowRefresh` |
| 11. Limitador por jogo | Com o Modo Console ligado, mede o limitador em uso uma vez por partida (60 s, contadores do RivaTuner, sem UAC). Ao fechar o jogo, oferece testar o outro e, depois dos dois, o melhor (menos quadros fora do ritmo; empate = menor latência). Trocar só com o clique. | `LimiterTrial`, `GamesViewModel.Trials`, `MainWindow.Trials` |
| 12. Linha reta / menor latência | Escolha por jogo no cartão do Modo Console, padrão "linha reta", com a latência medida de cada uma. "Menor latência" = RivaTuner sem limite (o jogador põe o limite no menu do jogo). | `PacingPresets`, `GameTunePanel.Pacing` |
| 13. Causa por travada | Nas gravações pela PresentMon, cada quadro > 50 ms usa MsCPUBusy/MsGPUBusy do próprio quadro (memória e disco continuam primeiro). A PresentMon passa a rastrear a placa (sem `--no_track_gpu`). | `HitchCauses.Classify(…, busy)`, `PresentMonCsv` |
| 14. Latência por quadro | `--track_pc_latency` nas gravações; mediana e p95 no Medir FPS e no "Por que está travando?". Teto do jogador no cartão do Modo Console (vale para todos os jogos); acima dele o resultado avisa e nenhum limitador/preset é sugerido. | `LimiterTrial.Latency`, `InterfacePreferences.LatencyCeilingMs` |

## Verificação

- Builds Debug x64 e Release x64: 0 avisos, 0 erros. Suíte: 1058 testes aprovados em cada.
- Testes de tela (FlaUI, cópia do runner do branch `claude/project-thread-fkyvef`, que ainda não está no main): 12 de 12 (9 seções, cartão novo do Modo Console e chave da latência no Overlay) (prints em `artifacts\ui-tests\`).

## O que não foi verificado no PC de verdade

- Nenhum jogo foi aberto: travamento com CrashReportClient, conferência da taxa por 10 s, disputa de taxa, abertura do RivaTuner ao detectar o jogo, fila de avisos durante o jogo, medição dos limitadores e a contagem do gerador de quadros só têm testes automáticos.
- O ajudante `--etw-clean` não foi executado (pede administrador).
- `gpuFrameTimeUs` do Reflex é tratado como o intervalo entre quadros reais (documentação da NVAPI); a contagem do gerador depende disso.
- "Quadros fora do ritmo" é estimado pelo tempo entre quadros visto pelo RivaTuner, não pela tela (a PresentMon mede a tela, mas pede administrador). Serve para comparar dois limitadores no mesmo jogo.
- Item 9: o print "depois" é a prévia do próprio Lotus, não o RivaTuner num jogo. O comportamento real de `reflexlatency`, `validate` e do `?:` no OverlayEditor e quanto o valor oscila não foram vistos em jogo.
- O tempo do processador por quadro não existe nas medições pelo RivaTuner; o item 10 decide só pela placa.
