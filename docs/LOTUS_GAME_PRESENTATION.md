# Fluidez por jogo — implementação e validação local

Em Meus Jogos → Otimizar este jogo, o card opcional reúne monitor, Hz, modo do RivaTuner e confirmação de salvamento. A configuração começa desligada. Salvar prepara somente o perfil do executável no RTSS e as preferências do Lotus; não muda o monitor. O aplicativo permite maximizar e alternar tela cheia por F11.

## Critério de escolha

| Modo | Uso no Lotus | Limite da interpretação |
|---|---|---|
| Front Edge Sync (`SyncLimiter=1`) | Padrão automático para priorizar regularidade entre chamadas Present | Não é sincronização vertical e não garante a melhor cadência física ou menor latência em todo jogo |
| Back Edge Sync (`2`) | Alternativa manual; fim de Present/início do quadro seguinte | Pode compensar atrasos; compare com o jogo real |
| Async (`0`) | Alternativa manual; intervalo mínimo entre inícios de quadro | Não compensa atrasos; o gráfico Present a Present pode variar |
| NVIDIA Reflex (`3`) | Preferido com geração de quadros NVIDIA selecionada; opção manual em NVIDIA compatível | Exige Direct3D 11/12, não substitui integração nativa do jogo; RTSS pode cair para Async em outras APIs |

Fonte primária dos quatro modos: ajuda oficial instalada do RTSS, `C:\Program Files (x86)\RivaTuner Statistics Server\Help\Properties\General\SYNC_LIMITER`. Com Reflex, o host aguarda confirmação da API por até cinco segundos antes de autorizar a troca de Hz. O perfil permanece editável antes de iniciar o jogo. Não há alteração do perfil Global.

Sem VRR, reutiliza-se o limite do Lotus ligeiramente abaixo da taxa real, sem alegar que a margem é universal. Com VRR informado como ativo, a sugestão inicial é três FPS abaixo do teto; existe limite manual. V-Sync continua sendo uma escolha do jogo/driver, explicada nas opções avançadas. Esta opção não liga V-Sync nem Reflex nativo automaticamente.

## Troca temporária e recuperação

- Enumera apenas modos progressivos compatíveis com a resolução e profundidade de cor atuais do monitor selecionado. Obtém taxas racionais por DXGI e lê a taxa ativa por QueryDisplayConfig.
- Usa eventos de primeiro plano, sem varrer continuamente a lista de processos. Confere nome do executável e instalação do jogo; launcher e programa homônimo fora da pasta não ativam a troca.
- Aplica apenas frequência, com teste prévio e `ChangeDisplaySettingsEx` sem `CDS_UPDATEREGISTRY`. Nunca salva uma nova taxa padrão do Windows.
- Ao perder foco, fechar o jogo ou encerrar o Lotus, restaura a taxa anterior. Um registro de recuperação é gravado antes da mudança para retomada após falha do Lotus.
- Preserva alterações externas do usuário/jogo/driver. Monitor desconectado mantém a recuperação pendente. Resolução ou taxa real diferentes da preparada impedem uma ativação incoerente com o limite salvo.
- Confere novamente o perfil RTSS antes de ativar; não grava perfis, pede UAC ou reinicia RTSS a cada Alt+Tab. Desativar a opção restaura o perfil anterior byte a byte.
- Uma troca física pode causar breve apagamento da tela. Tela cheia exclusiva pode controlar seus próprios modos. Se Lotus for encerrado à força, a recuperação depende de abri-lo novamente; não é possível prometer restauração instantânea por um processo encerrado.

## Pesquisa e vídeo

O vídeo foi analisado pela transcrição completa e por uma amostra de comentários destacados, recentes e respostas; não se afirma leitura de todos os comentários. Relatos sobre resolução, escalonamento, Vulkan e Fullscreen Optimizations variam e não constituem testes controlados. Não foram generalizadas alterações de CS2 para outros jogos.

- [Vídeo analisado](https://www.youtube.com/watch?v=X3CWaVcfEKU&t=1194s).
- [Microsoft: flip model e Independent Flip](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model): janela sem borda pode apresentar diretamente; composição, isoladamente, não prova blt nem atraso fixo de um quadro. Os textos correspondentes no Lotus foram corrigidos.
- [Microsoft: ChangeDisplaySettingsEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw): modos válidos, teste prévio e aplicação temporária sem persistência no registro.
- [Microsoft: taxas racionais de atualização](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_path_target_info).
- [NVIDIA: Reflex](https://developer.nvidia.com/performance-rendering-tools/reflex): integração de baixa latência não se resume a selecionar um limitador externo.
- [PresentMon: métricas e limites](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md): início de quadro, apresentação e exibição são medições distintas; gráfico plano não comprova menor latência óptica total.
- [CRU: autor e limitações](https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU): EDID override não é necessário para escolher taxas já suportadas. CRU não foi incorporado, nem foram alterados EDID, drivers ou temporizações personalizadas.

## Evidências

Leitura real, sem mudar hardware: monitor `DISPLAY1`, NVIDIA GeForce RTX 5060, 1920 × 1080, ativo em 74,973 Hz; modos 50, 59,94005994, 60 e 74,973 Hz. Executada por `tools/GpuSceneValidation --display-modes`.

807 testes aprovados em Debug/x64 e 807 em Release/x64. Incluem 33 casos novos de planejamento, foco/restauração com display controlado, recuperação, persistência, permissões recusadas, rollback, resolução protegida, AMD/Reflex e os quatro modos RTSS em perfis temporários. Resultados em `tests/Lotus.Tests/TestResults/presentation-debug.trx` e `presentation-release.trx`. Esses testes de troca de Hz usam um display controlado; não equivalem a um teste físico dentro de um jogo.

Compilações Debug/x64 e Release/x64 concluídas sem avisos ou erros. A Release foi iniciada pelo executável de `src/Lotus.App/bin/x64/Release`, com `--dev-instance --maximized`; o processo respondeu e registrou navegação para Meus Jogos. A inspeção visual automatizada ficou bloqueada pelo identificador duplicado entre a instalação e a compilação local: `window id 4784834 no longer belongs to ...; current owner is ...` (a ferramenta informou o mesmo caminho instalado nas duas partes). A seleção foi atualizada e houve uma única nova tentativa, sem controlar outra janela.

Não se declara teste visual nem troca física de Hz em jogo aprovados por essa evidência. Ambos ficam para o teste do usuário antes de gerar o instalador. Nenhum instalador ou publicação faz parte desta entrega.

## Correção da transição após teste do usuário

Notificações transitórias de foco agora são agrupadas por 200 ms antes de acessar o driver. Após aplicar um modo, há 300 ms de estabilização antes de interpretar perda momentânea de foco como Alt+Tab. Perda de foco persistente continua restaurando o monitor. O teste de regressão confirma que uma perda transitória seguida de retorno ao jogo não produz a sequência 60 → 75 → 60 Hz. O apagamento físico normal de uma troca de sinal não pode ser eliminado por esta correção.

Também foi identificada uma fila externa `scskillerw.exe rewarm-stale --if-driver-changed`: o trabalhador Wukong estava ativo desde 09:49 e, ao terminar, a fila iniciou um trabalhador Palworld. Os executáveis estavam em pastas `SCSKiller/games/.../work/stage-*`, com oito threads e 2 GB solicitados. Isso é compilação externa de shaders, não uma janela de teste iniciada nesta entrega. A fila, a interface SCSKiller e seus trabalhadores foram encerrados conforme a solicitação do usuário; nenhuma tarefa agendada ou configuração de inicialização foi alterada. Não se atribui toda a tela preta a essa atividade sem captura de apresentação do jogo real.

Após a correção: 808 testes aprovados em Debug/x64 e 808 em Release/x64; ambas as compilações WinUI aprovadas. Reabertura apenas do Lotus, sem benchmark, prévia 3D ou compilação de shaders.
