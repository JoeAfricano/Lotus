# Lotus 1.7.0 — candidata local para revisão

Preparada em 14/11/2026. Versão técnica `1.7.0-alpha`, seguindo a convenção atual do Lotus. **Não publicada e não instalada automaticamente.** O usuário pediu explicação dos resultados e nova aprovação antes da publicação no GitHub.

## Alterações

- Três overlays nativos do RTSS: Equilíbrio (768×152, topo central), Neon (300×304, canto direito) e Pulse (220×300, canto esquerdo), com referência 1920×1080. Números brancos, arte com transparência e framepacer rosa. Não usam CapFrameX, HTTP nem um renderizador auxiliar do Lotus.
- Exportação inclui `.ovl` e atlas PNG. Ativação habilita o OverlayEditor nativo e remove o preenchimento automático que criava o retângulo escuro. Há backup, verificação e rollback; o limite de FPS não é alterado. Troca/restauração não reinicia RTSS sob um jogo aberto.
- Seletor com cartões distribuídos em linhas, imagens geradas dos sete `.ovl` reais, visualização na proporção 1920×1080 ou em detalhes e números explicitamente ilustrativos. A miniatura experimental também passa a caber inteira. RTSS continua sendo o renderizador final, com possíveis diferenças tipográficas.
- Ajuste da placa mantém os cartões, paleta e estilos do Lotus. Objetivo escolhido passa a comandar o teste automático. Conexão com Afterburner, progresso, resultado aplicado e uso nos jogos ficam separados; instruções manuais e histórico ficam expansíveis. A exportação para o Perfil 2 é opcional e explicitamente identificada.
- Undervolt confere todos os pontos da curva; o controle geral de clock derivado pelo Afterburner não invalida uma curva correta. Overclock, memória e potência continuam verificados. Exportação de perfis agora confere esses campos além da curva.
- Aplicação que falha ou lança exceção tenta restaurar imediatamente; falhas de restauração mantêm o journal e a recuperação pendente. Teste, aplicação, exportação e restauração não são oferecidos simultaneamente. Teste automático não inicia com jogo da biblioteca aberto.
- Leitura de temperatura/clock ausente ou antiga interrompe a carga. Temperatura excessiva encerra a sequência automática. Cancelamento e falha de restauração não aparecem como sucesso.
- Painéis deixam de acumular assinaturas de eventos e agrupam atualizações de interface. O progresso não reconstrói a tela inteira. Removidas compactação completa e expulsão periódica do working set; testes da placa não ativam modo de economia nem descarregam a página.
- Corrigidas duas concorrências encontradas durante os testes: limpeza da fila de diagnóstico de RAM durante atualização e status “parando” sobrescrevendo o cancelamento já concluído do benchmark.

## Validação executada

| Verificação | Resultado |
| --- | --- |
| Build Debug/x64 da solução final | Sucesso, zero avisos/erros |
| Suíte completa Debug | 733 aprovados, zero falhas/ignorados |
| Suíte completa Release/x64 | 733 aprovados, zero falhas/ignorados |
| Regressões RAM/benchmark/experiência | Cancelamento síncrono, concorrência Add/Reset e pausa durante notificações verificados na suíte completa |
| RTSS nativo | Overlay completo observado em Wukong DX12 1920×1080; CapFrameX e renderizador auxiliar fechados |
| Afterburner real, RTX 5060 | Curva v2.3 com 127 pontos; aplicação/leitura de 925 mV/2775 MHz e +100/+500 MHz confirmadas, com restauração após cada teste |
| Exportação real de perfis | Helper retornou 0; carregamento dos perfis conferido; cinco arquivos originais restaurados com hashes idênticos; estado da placa restaurado e lido de volta |
| Uso automático no jogo | Log confirmou aplicação ao abrir Wukong e restauração ao fechar; journal removido após sucesso |
| Wukong Benchmark Tool | Uma rodada completa: 74 FPS médios, 76 máximos, 50 mínimos, 1080p/Alto/DLSS, sem geração de quadros; limite anterior de 74,970 FPS preservado |

Os números do Wukong foram lidos da tela do próprio benchmark, não calculados pelo Lotus. Não houve comparação A/B nem homologação prolongada de estabilidade, consumo ou ganho de FPS. O teste não prova que todo travamento relatado foi eliminado.

O RTSS ficou configurado com Equilíbrio na pasta normal `%LOCALAPPDATA%/Lotus/Overlays`, com backup em `%LOCALAPPDATA%/Lotus/RtssBackups`. O limite existente de 74,970 FPS foi preservado. A configuração não depende da pasta de preparação isolada.

## Limites e revisão antes da publicação

- Os três novos layouts usam dimensões de referência 1080p. Uma janela 3D pequena ou redimensionada pode recortar/reposicionar o desenho: outras resoluções ainda precisam de homologação. O layout foi visto completo no Wukong 1080p.
- As estatísticas nativas são `1000/swavg(Frametime,0)` e os inversos dos percentis 99/99,9 do frametime. Estes percentis não são a média dos 1%/0,1% quadros mais lentos usada em outros módulos. Não comparar as definições como métricas idênticas.
- A fonte-base Raster3D de 64 px dá nitidez aos números, mas outros contadores de OSD do RTSS podem herdar a escala. Essa combinação também precisa de revisão visual antes da publicação.
- Os sensores dependem do suporte do RTSS/HAL e do hardware. A escrita no Afterburner está limitada ao SDK v2.3 e a uma GPU identificada; formatos desconhecidos e múltiplas GPUs são recusados.
- RTSS instalado é necessário para os overlays nativos. MSI Afterburner instalado e aberto é necessário para aplicar ajustes da placa. O instalador do Lotus inclui seus runtimes .NET/Windows App SDK, não os instaladores desses programas de terceiros.
- A troca automática do próprio Afterburner entre perfis 2D/3D não é configurada. O uso automático nos jogos é controlado pelo Lotus enquanto ele está aberto ou na bandeja.
- A ferramenta de controle de janelas recusou a instância de desenvolvimento por divergência de identificação com o Lotus instalado. A inspeção interativa final das telas WinUI e o teste de atualização pelo instalador **ficam pendentes da revisão local**. Os renders dos `.ovl`, builds, testes e integrações reais acima foram executados.
- O instalador não foi executado. Assinatura digital e compatibilidade com outras placas/resoluções não foram homologadas nesta sessão.

## Continuidade no Claude Code ou Codex

O código está no projeto normal `C:\Projetos\Lotus`, em C#/.NET 10/WinUI 3. Não há dependência de um agente específico. A preparação isolada em `C:\Projetos\Lotus-1.6.1-codex` foi usada para não sobrescrever alterações anteriores. Os arquivos relacionados foram integrados após conferência de hashes; os demais trabalhos locais foram preservados. Não foram criados commits, tags, pushes ou releases.

Backup anterior à integração: `artifacts/lotus-170-before-codex`. Evidências da sessão ficam na preparação isolada, em `artifacts/validation-170`, `gpu-validation`, `gpu-candidate`, `gpu-candidate-oc` e `afterburner-validation`. Os testes que expuseram falhas foram mantidos nos registros; as suítes finais são os arquivos `*-170-verified.trx`.

Comandos usuais: `dotnet build Lotus.slnx -c Debug -p:Platform=x64`, `dotnet test tests/Lotus.Tests/Lotus.Tests.csproj -c Debug -p:Platform=x64` e a mesma suíte em Release. Para gerar o instalador, usar `tools/Build-Installer.ps1`; preservar a configuração local de OAuth conforme `docs/LOTUS_GOOGLE_DRIVE.md`, sem colocar credenciais no Git ou no ZIP de fontes.

Antes de publicar: revisar as telas/instalador, obter a aprovação explícita do usuário e preparar um commit revisável que preserve o trabalho local anterior, incluindo apenas o escopo autorizado. Não promover automaticamente esta candidata.

## Revisão do cartão Antes de começar — 14/11/2026

O cartão agora aparece após os objetivos e antes da execução automática, fora dos detalhes manuais. A seleção muda seu resumo e seus itens: undervolt explica a verificação real da curva; overclock não exige desbloqueio de tensão; Padrão esclarece que selecionar o cartão não restaura a placa.
A tela verifica instalação e conexão com o MSI Afterburner a cada três segundos enquanto está carregada; interrompe a atualização ao sair. Atualizações sem mudança não reconstroem a interface nem reiniciam os valores do teste. O botão Verificar novamente também atualiza o estado.
RivaTuner e uso automático nos jogos aparecem como informações opcionais, não bloqueios nem atestados de ajuste aplicado. Configuração de tensão salva não é apresentada como comprovação de suporte da curva. Os parâmetros aplicados continuam sujeitos à verificação real do controle existente.
Validação desta revisão: compilação completa Debug/x64, zero avisos/erros; 736 testes aprovados em Debug/x64 e 736 em Release/x64, sem falhas ou ignorados. Três novos testes cobrem mudança de conexão, objetivos/requisitos opcionais e detecção de instalação/configuração. Não houve nova escrita na GPU ou benchmark nesta revisão da checagem. A inspeção visual interativa e o teste do instalador continuam pendentes; publicação depende de aprovação explícita.

## Revisão do fluxo completo e cena gráfica — 14/11/2026

- Sequência: Teste a placa → Compare os perfis → Aplique sua escolha → Use nos jogos. Teste automático e uso nos jogos dividem o mesmo painel, em seções consecutivas.
- O comando inicial compara padrão, undervolt e overclock (sem antecipar a etapa combinada). Selecionar um cartão não escreve na GPU. O comando de aplicar exige um candidato válido de uma sessão concluída para a mesma identificação de placa. Cancelamento, interrupção, medições inválidas e troca de placa bloqueiam a recomendação. Voltar ao padrão permanece disponível para recuperação, sem exigir outro teste.
- Cartões e detalhes avançados exibem os valores e medições reais do resultado selecionado. Antes do teste não exibem o ponto inicial de 950 mV como recomendação. O seletor avançado e o objetivo agora ficam sincronizados. Os relatórios desta tela ficam em memória nesta sessão; o ajuste mantido continua usando a persistência existente.
- Botões Mudar Perfil no MSI Afterburner e Voltar ao padrão abaixo dos cartões. O primeiro usa a integração direta com confirmação dos valores; não depende de preencher offsets manualmente nem equivale a exportar um arquivo. O segundo confirma curva, offsets e potência padrão pelo controle existente, desliga o uso automático no Lotus e não apaga perfis/ventoinhas. Exportação de arquivos do Perfil 2 foi mantida nos detalhes avançados, com aviso de que o helper carrega o Perfil 1 ao terminar; estado de aplicação é invalidado após exportar/restaurar esses arquivos.
- Janela Lotus: prévia limitada em altura e largura, sem ampliar o cartão até ocupar a página; renderizações atrasadas de opções antigas são descartadas e mudanças rápidas de sliders são agrupadas. Aplicação de estilo sinaliza ocupação antes de consultar jogos, impedindo comandos duplicados. A prévia 3D recebe o renderizador Janela Lotus pelo PID do processo, com descarte ao encerrar.
- Cena de teste/prévia: escultura de pétalas, núcleo facetado, metal rosê, base luminosa discreta e ambiente escuro. A nova carga é usada só no ajuste da placa e na prévia; a cena histórica do Lotus Score permanece separada para preservar comparabilidade. Pontuações desta nova carga de ajuste não devem ser comparadas com as anteriores.
- Evidência gráfica: shader HLSL compilado e três quadros 1920×1080 renderizados offscreen na NVIDIA GeForce RTX 5060 com a ferramenta GpuSceneValidation, sem alterar clocks/perfis. As imagens são quadros da cena, não capturas de uma sessão completa de undervolt nem números de benchmark.
- Validação: 745 testes aprovados em Debug/x64 e 745 em Release/x64. Os novos testes incluem seleção sem escrita, obrigação de teste antes de aplicar, relatórios incompletos, troca de GPU, falha de aplicação/restauração, padrão confirmado antes de limpar configuração, exclusão de comandos duplicados e callback da prévia. Duas falhas intermediárias foram corrigidas: sincronização que fazia Padrão voltar a Undervolt e dependência ausente no teste de disponibilidade de comparação.
- Limitações: não foi repetida a busca física completa de perfis (cerca de uma hora) nesta revisão; os testes reais anteriores de aplicação/leitura/restauração continuam evidência do protocolo existente. A interação final da tela WinUI e da Janela Lotus sobre a prévia 3D continua pendente de teste local. O instalador é gerado, mas não executado automaticamente. Nada foi publicado; aprovação explícita continua obrigatória.

## Revisão espacial, posição da Janela Lotus e destaque das etapas — 14/11/2026

- Janela Lotus agora se ancora ao retângulo da área cliente do jogo/benchmark, convertido para coordenadas de tela com ClientToScreen. O desenho usa os quatro cantos dessa área, não os do monitor; em clientes pequenos o cartão inteiro é reduzido para caber. Cada instância usa sua própria classe nativa, evitando conflito entre janela do jogo e janela de prévia.
- Cinco novos casos de teste verificam os quatro cantos, DPI de 100–300%, monitor com coordenada negativa, origem da janela e área pequena. A suíte completa passou com 750 testes em Debug/x64 e 750 em Release/x64.
- Verificação interativa: a ferramenta GpuSceneValidation abriu a cena real 1280×720 e a Janela Lotus foi observada inteiramente dentro do cliente, no canto superior direito. Foram 110 renders até fechar a janela. Nessa ferramenta isolada a origem de frames não estava conectada (ReceivingFrames=false), por isso a Janela exibiu travessões; isso não é uma medição de desempenho. O teste confirmou posição/desenho, não homologou a cadeia de sensores da interface instalada.
- A captura inicial mostrou dois desenhos porque a ferramenta forçou uma Janela de diagnóstico enquanto a Barra Lotus já estava ativa no RTSS. O processo foi fechado; os arquivos/perfis do RTSS não foram alterados para a verificação. A ferramenta agora segue a mesma checagem de estilo ativo usada pelo Lotus, sem forçar o segundo renderizador.
- Nova cena final: flor de pétalas pontiagudas em três camadas, luz solar dourada/rosê, estrelas FIXAS, nebulosa discreta, asteroides rochosos em movimento ao fundo e malha espacial sob a flor. O shader foi compilado e três quadros 1920×1080 foram renderizados na RTX 5060. A cena continua separada da carga histórica do Lotus Score; comparações de pontuações de ajuste entre revisões diferentes não são válidas.
- Ordem dos estilos: Barra Lotus, Lotus Neon, Lotus Pulse Premium e Janela Lotus primeiro; opções clássicas depois. Preferências de posição, escala, itens e perfil do usuário são preservadas.
- A frase do fluxo virou quatro etapas visuais em um cartão com borda rosê/dourada, etapa atual e indicadores de conclusão; quatro colunas em espaço amplo e duas em janelas menores. O cabeçalho Ajuste da placa também ganhou título maior e descrição legível. A recuperação para padrão sem teste prévio não marca etapas de medição como concluídas.
- O instalador continua sendo uma candidata local, sem execução automática e sem publicação. A interface principal WinUI e a instalação/atualização ainda dependem de revisão local; a observação da prévia feita aqui usa o mesmo renderizador gráfico e a mesma classe de overlay do produto, mas não substitui o teste de navegação completo.

## Revisão de persistência, duração e controles — 14/11/2026

- O relatório de ajuste agora é salvo nas preferências, com identificação da GPU, data, candidatos medidos e modo de duração. Reabrir recupera os cartões sem escrever no hardware. Relatórios de outra GPU, incompletos, inválidos ou de esquema desconhecido não liberam aplicação. Repetir permanece explícito; um teste cancelado não substitui o último relatório salvo. Na desinstalação, os resultados sobrevivem se os dados locais forem mantidos; apagar esses dados também apaga as medições.
- Durações selecionáveis: Rápido, cerca de 6–8 minutos (45 s/candidato, busca reduzida); Equilibrado, cerca de 15–20 minutos (75 s/candidato, busca intermediária); Completo, cerca de 45–60 minutos ou mais (180 s/candidato, busca integral). São estimativas, não prazos garantidos. Confirmação do candidato de overclock e restauração continuam obrigatórias. O rápido é triagem, não garantia de estabilidade prolongada. Mudar a seleção não reclassifica medições antigas.
- O executor limita aquecimento e medição ao tempo restante, evitando iniciar outro bloco inteiro de 40 segundos no fim de um candidato curto. Limites térmicos, detecção de sensores ausentes, cancelamento e recuperação não foram removidos.
- Janela Lotus atualiza a cada 100 ms (antes 250 ms); FPS usa os últimos 250 ms para reagir mais rápido, enquanto média e lows mantêm a janela longa de amostras. Resumos sem frames novos são reutilizados. Mostrar/ocultar tem botão e atalho configurável Ctrl+Alt+F6 até F11, ou sem atalho; Ctrl+Alt+O muda o canto. Os atalhos só são registrados enquanto a janela do jogo é a ativa.
- A miniatura Pulse Premium falhava por concorrência na inicialização do encoder PNG e mantinha a exceção no cache. A geração agora é serializada, usa encoder explícito e descarta entradas falhas. Teste com 70 pedidos simultâneos decodificou os sete estilos nativos, incluindo Pulse.
- Opção antiga Barra no topo removida do seletor, preservando suporte aos arquivos antigos. Ferramentas RTSS ficam recolhidas, sem repetir aplicar/prévia/restaurar; exportação usa o estilo ativo. A tela ilustrativa ganhou borda contrastante, fundo distinto e indicação de resolução. Menu Otimizar meu PC ganhou agrupamento em cartão e espaçamento maior.
- Prévia redimensionável/maximizável, F11 para tela cheia sem exclusividade e Esc para voltar. Asteroides alongados e facetados com veios dourados/rosê, estrelas fixas e flor solar; a cena histórica Lotus Score permanece intacta.
- Verificação interativa em janela real: F11 ocupou 1920×1080; Esc restaurou 1280×720. Na ferramenta específica de controles, Ctrl+Alt+F10 ocultou e mostrou a Janela Lotus, e Ctrl+Alt+O mudou o canto: 325 renders, 2 alternâncias, 1 mudança. Essa ferramenta forçou o renderizador somente para diagnóstico, sem alterar o perfil RTSS. ReceivingFrames=false nela; travessões não representam sensores medidos. A prévia normal manteve apenas o estilo RTSS ativo.
- Validação automatizada: 761 testes em Debug/x64 e 761 em Release/x64, sem falhas ou ignorados; compilação do app Debug/x64 sem avisos/erros. Shader final compilado e três quadros reais 1920×1080 renderizados na RTX 5060. Duas expectativas de testes foram corrigidas após falha inicial: FPS passou a refletir 250 ms e PNGs têm resolução dupla por supersampling.
- Não foi executada nova sessão física completa de busca nos três modos nem instalação automática. As estimativas vêm do orçamento de candidatos; a navegação completa da interface principal e instalação/atualização ainda precisam de revisão local. Evidências anteriores de aplicar/ler/restaurar MSI continuam separadas dos testes desta revisão. Não há garantia de ausência de todos os bugs.
- Publicação ficará a cargo do usuário, conforme sua última instrução. Nenhum push, tag ou release foi feito. Código comum para Claude Code e Codex, sem integração exclusiva com um assistente.
