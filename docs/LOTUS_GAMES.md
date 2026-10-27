# Meus Jogos — entrega 1 (25/10/2026)

Primeira entrega da versão focada no jogador leigo (ver decisões de produto em CLAUDE.md). Biblioteca de jogos com detecção automática, adição manual, busca, filtro por loja, ocultar e abrir o jogo. Somente leitura nas bibliotecas dos launchers.

| Loja | Detecção (somente leitura) | Abertura |
| --- | --- | --- |
| Steam | `libraryfolders.vdf` + `appmanifest_*.acf` (só `StateFlags & 4`, sem ferramentas como Proton/Redistributables); capa em `appcache\librarycache` (formatos antigo e novo) | `steam://rungameid/{id}` |
| Epic | `ProgramData\Epic\...\Manifests\*.item`; ignora itens sem a categoria `games` | `com.epicgames.launcher://apps/{ns}%3A{item}%3A{app}?action=launch&silent=true` |
| Xbox/Game Pass | `.GamingRoot` de cada disco fixo (fallback `XboxGames`) + `Content\MicrosoftGame.config`; logo do pacote como capa | `explorer.exe shell:AppsFolder\{PFN}!{AppId}` (PFN calculado: SHA-256 do publisher, base32 Crockford) |
| GOG | `HKLM\...\GOG.com\Games` | executável + `launchParam` |
| EA app | Entradas de desinstalação da Electronic Arts com `__Installer\installerdata.xml` (título pt-BR/en-US, contentIDs) | `origin2://game/launch?offerIds=...` |
| Ubisoft | `HKLM\...\Ubisoft\Launcher\Installs` + nome em `Uninstall\Uplay Install {id}` | `uplay://launch/{id}/0` |
| Manual | `%LOCALAPPDATA%\Lotus\games.json` (gravação atômica) | `.exe` escolhido pelo usuário |

- **Segurança na abertura:** apenas os esquemas acima e `shell:AppsFolder` sem metacaracteres; executáveis só se `.exe` existente, com `ShellExecute` para respeitar jogos que pedem elevação. Nenhum comando arbitrário.
- **Ocultar:** qualquer item pode ser ocultado (ex.: Wallpaper Engine, app listado como jogo pela Steam); chaves em `games-hidden.json`, com "Mostrar ocultos". Ocultar/remover nunca desinstala nada.
- **Interface:** cards com capa (decodificada no tamanho do card) ou iniciais, etiqueta da loja, "Jogar" e ocultar; busca sem acento/maiúsculas; filtro por loja; varredura em segundo plano na primeira visita.
- **Referência:** desenho inspirado no `GameLibrary` do ResetExplode (MIT, GustavoReset, commit ce1d9b3), reimplementado com Xbox/Game Pass e EA app.
- **Resultado real neste PC:** 4 itens da Steam (God of War, No Rest for the Wicked, Persona 5 Royal, Wallpaper Engine), 3 com capa; nenhuma outra loja instalada. Abertura real de jogos não foi acionada nos testes.
- **Testes:** 225 aprovados em Debug e Release (18 novos: parsers, PFN contra valor conhecido da Microsoft, biblioteca falsa Steam/Epic/Xbox em pasta temporária, lista de esquemas permitidos, armazenamento, ViewModel e varredura real somente leitura).
- **Limitações:** EA/GOG/Ubisoft/Xbox não testados com instalações reais nesta máquina (cobertos por parsers e arquivos sintéticos); capas só onde o launcher guarda imagem local (Steam e logos de pacotes Xbox).

## Ajustes pedidos pelo usuário (25/10/2026)

- **Escanear jogos** (substitui o ícone de atualizar): procura de novo em todas as plataformas; se houver jogos instalados que foram removidos da lista, oferece "Colocar de volta" com seleção. O escaneamento de pasta chegou a ser implementado, mas foi removido porque não era o comportamento desejado.
- **Remover da lista**: menu "⋯" em cada card com confirmação; vale para jogos de launchers e manuais (o manual continua salvo e volta pelo escaneamento ou ao adicioná-lo de novo). Nada é desinstalado.

## Aplicar perfil em um clique (Otimizar)

- "Aplicar perfil" analisa todas as ações do perfil (plano de energia e, no Perfil Jogo, otimizações para jogos em janela), mostra um único diálogo com "de → para" e itens já adequados, e ao confirmar aplica e verifica cada uma pelo seu motor com journal. A caixa "Confirmo..." e o botão "Analisar" saíram; o diálogo é a confirmação explícita.
- "Desfazer perfil" restaura a última alteração do Lotus de cada ação, apenas onde nada mudou por fora; mudanças externas entre a prévia e a aplicação viram conflito e não são sobrescritas.
- A prévia grava planos "Planejado" no journal (aparecem no histórico) sem alterar o Windows.
- Testes: 228 aprovados em Debug e Release (prévia sem mutação, aplicar/desfazer tudo, conflito externo, Perfil Diário só com as próprias ações). Verificação na interface por UI Automation; capturas de tela saíram pretas por falta de tela ativa no momento (sem erros no log).

## Configuração inteligente dos jogos — base (25/10/2026)

- **Perfil do sistema** (`WindowsSystemProfile`, somente leitura): GPU e VRAM pela chave de classe de vídeo do registro, threads, RAM e monitor principal. Neste PC: RTX 5060 8 GB, 12 threads, 16 GB, 1920×1080 a 75 Hz.
- **Recomendação** (`SettingsAdvisor`): pontuação relativa de GPU (`GpuScore`, ≈100 = classe RTX 3060; heurística documentada) dividida pela demanda (pixels × FPS-alvo). Upscaler conta como ganho (Q ×1,3, E ×1,5, D ×1,8; em ≤1080p no máximo Equilibrado). DLSS em RTX, XeSS em Arc, FSR nas demais. Texturas limitadas pela VRAM. PCs fracos miram 60 FPS estáveis. Limite de FPS = meta. Neste PC: Alta, texturas Altas, DLSS Qualidade, limite 75.
- **Receitas** (`IGameSettingsRecipe`: ler texto bruto → transformação pura → gravar e conferir): `UnrealEngineRecipe` genérica (`GameUserSettings.ini`, grupos `sg.*` 0–3 e `FrameRateLimit`; preserva todas as outras linhas; pede para abrir o jogo uma vez se o arquivo não existir). Sem receita = modo recomendação (o que escolher no menu do jogo).
- **Segurança**: só com o jogo fechado (processo sob a pasta de instalação), original salvo uma única vez em `%LOCALAPPDATA%\Lotus\GameSettingsBackups`, verificação após gravar, "Restaurar original" e confirmação no diálogo.
- **Jogos deste PC**: No Rest for the Wicked guarda opções em JSON no registro (`DeviceSettings_Key`: `QualityPreset`, `UpscaleMethod`, `RenderScale*`, `FrameRateCap`, `DRSEnabled/DRSTargetFPS`) — receita depende de calibrar os números dos presets; Persona 5 Royal e God of War ficam em modo recomendação.
- Testes: 249 aprovados (pontuação de GPU, recomendações de entrada a topo, receita Unreal com jogo falso, editor INI, aplicar/bloquear jogo aberto/restaurar, leitura real somente leitura).

## Ciclo de medição e receita calibrada (25/10/2026)

- **Regra principal**: a aplicação inicial nunca reduz qualidade sem evidência; a qualidade muda só pelo ciclo **Medir e ajustar**, um nível por vez.
- **Medir e ajustar** (menu do jogo, com o jogo aberto): contagem de 10 s para o usuário voltar ao jogo, captura PresentMon de 45 s (elevada; o Windows pede permissão) e, em paralelo, uso de GPU pelo contador do Windows `\GPU Engine(*engtype_3D)\Utilization Percentage` (`PdhGpuLoadSampler`, qualquer fabricante). `QualityTuner`: abaixo da meta (média < 95% ou 1% low < 70%) com GPU ≥ 80% → baixar um nível; abaixo da meta com GPU < 80% → gargalo de CPU/jogo, não mexe nos gráficos e explica; na meta com GPU < 65% → subir um nível; senão, manter. O ajuste fica pendente e é aplicado com o jogo fechado, com backup do original e verificação.
- **No Rest for the Wicked** (`NoRestForTheWickedRecipe`): calibrado pelas telas do jogo — `QualityPreset` 4 = Qualidade Máxima (escala de 0 a 4, de Desempenho a Qualidade Máxima), `UpscaleMethod` 1 = DLSS, `UpscaleQualityMode` 0 = Qualidade, `FrameRateCap` 0 = desligado. Aplicação inicial: limite de FPS na meta e DLSS Qualidade em RTX; preset só pelo ciclo de medição. JSON preservado (só chaves alteradas mudam).
- **Unreal**: o passo altera todos os grupos `sg.*` exceto texturas.
- Testes: 260 aprovados (regras de decisão, limites, receitas, medir→ajustar com captura simulada, leitura real do contador de GPU). Nenhuma configuração real de jogo foi alterada.

## Complemento: SCSKiller (pré-carregar shaders, 25/10/2026)

- SCSKiller 1.3.2 é GPL-3.0-or-later (BlueHeisenberg; downloads oficiais só em https://github.com/BlueHeisenberg/SCSKiller/releases). Reaproveitar o código tornaria o Lotus GPL; por decisão do usuário o Lotus **integra pela CLI documentada** (`scskiller status`, `scskiller compile <id> --idle`), como programa separado, sem copiar, ligar ou redistribuir código.
- Como ele funciona (pela documentação da CLI): lê o índice de shaders da engine nos arquivos do jogo (Oodle/AES quando necessário), pode gravar pipelines com um `d3d12.dll` na pasta do jogo e compila no cache do driver; recompila após atualizar o driver.
- No Lotus: status por card (prontos / recompilar após driver / pré-carregados / não suportado), "Pré-carregar shaders" no menu do jogo (jogo fechado, confirmação, prioridade baixa, progresso no status). Sem SCSKiller instalado: aviso opcional com link para a página oficial (o Lotus nunca baixa).
- Neste PC: God of War precisa recompilar (driver 617.42 → 621.01), No Rest for the Wicked pronto, Persona 5 Royal não suportado. Nenhuma compilação real foi executada.
- Testes: 264 aprovados (parser com a saída real da CLI, correspondência por id/nome, fluxo com ferramenta simulada, oferta quando ausente).

## Complemento: RivaTuner — leitura de FPS (25/10/2026)

- RivaTuner 7.3.7 e MSI Afterburner 4.6.6 instalados neste PC. Perfis do RivaTuner ficam em Program Files (somente leitura para usuários): editar exigirá um pedido de administrador por aplicação.
- `RtssSharedMemory` lê a memória compartilhada documentada no SDK (`RTSSSharedMemoryV2`, assinatura `'RTSS'`, entradas com PID, nome, janela de tempo, frames e frametime), sem administrador e sem injeção por parte do Lotus.
- `RtssPreferredCaptureProvider`: no "Medir e ajustar", se o RivaTuner acompanha o processo do jogo, mede por ele (sem aviso do Windows); senão usa o PresentMon elevado. Pelo RivaTuner, o 1% low é aproximado (frametime amostrado a cada ~10 ms), registrado em Diagnostics.
- Leitura real: o RivaTuner acompanhava 3 aplicativos no momento do teste. Testes: 266 aprovados (layout do SDK em memória isolada, preferência RTSS→PresentMon, leitura real segura).

## Complemento: RivaTuner — overlay do Lotus e tela Integrações (25/10/2026)

- **Configurações › Integrações** (antes vazia): RivaTuner, MSI Afterburner e SCSKiller com estado real (instalado, versão, overlay padrão ou do Lotus), "Configurar overlay do Lotus", "Restaurar original" e link oficial do SCSKiller.
- **Overlay do Lotus = OSD do RivaTuner** (decisão do usuário): FPS inteiro, gráfico de frametime, texto ouro rosé sobre fundo escuro translúcido, canto superior esquerdo. Só chaves de `[OSD]` do perfil `Global`; limitador intocado.
- **Escrita segura**: perfis ficam em Program Files (só leitura para usuários). O Lotus continua `asInvoker` e relança a si mesmo elevado só para a operação (`--rtss-apply <request.json>` em `%LOCALAPPDATA%\Lotus\Requests`, ou `--rtss-restore <perfil>`). O auxiliar aceita apenas a lista fechada (`[OSD]` de overlay e `[Framerate] Limit/LimitDenominator/ReflexSleep`), nomes `Global` ou `<jogo>.exe`, valores numéricos/hex; guarda o original uma vez em `%LOCALAPPDATA%\Lotus\RtssBackups`; chama `UpdateProfiles` do `RTSSHooks64.dll` para o RTSS recarregar; o resultado é conferido lendo o perfil.
- Testes: 276 aprovados em Debug e Release (lista fechada contra injeção de seção/caminho/valor, backup único e restauração exata, perfil novo removido ao restaurar, fluxo com auxiliar simulado e UAC recusado, estados da tela, leitura real do perfil sem admin). Nenhuma alteração real foi feita no RivaTuner.

## Overlay completo do Lotus para o RivaTuner (25/10/2026)

- Layout `Lotus.ovl` do OverlayEditor (recurso embutido): cartão escuro com borda rosé, FPS grande, AVG, 1% LOW, 0.1% LOW, frametime com gráfico, linhas CPU/GPU/VRAM/RAM (uso, barra, temperatura, clock ou GB) e rodapé API | resolução | ms.
- **Exportar** (sem admin): salva em `%LOCALAPPDATA%\Lotus\Overlays\Lotus.ovl` e abre a pasta para importar no OverlayEditor. **Ativar** (UAC, com confirmação): o auxiliar só aceita `.ovl` dentro dessa pasta, guarda `OverlayEditor.cfg` original uma vez e define `[Settings] Layout`. **Restaurar** devolve o arquivo original exato.
- Limitação: a semântica das tags do OverlayEditor foi inferida dos exemplos que acompanham o RTSS; o visual precisa ser conferido dentro de um jogo. 278 testes aprovados.

## LOTUS SCORE (25/10/2026)

- Nova aba **LOTUS SCORE**: teste de ~50 s sem alterar o Windows. CPU 1 núcleo e todos os núcleos (ray tracer determinístico próprio) e GPU (cena 3D ray-marched 1920×1080 fora da tela, Direct3D 11 via Vortice 3.8.3, adaptador de alto desempenho, software/WARP ignorado). Cada teste: 1 aquecimento + 3 passadas de 3 s, mediana; variação acima de 3% é sinalizada como resultado instável.
- Pontuação total: média geométrica ponderada (GPU 50%, multi 30%, single 20%); 10 000 = PC gamer intermediário. Fatores calibrados no PC de desenvolvimento (Ryzen 5 5500 + RTX 5060 → 1 400 / 10 500 / 11 500).
- **Offline**: tabela embutida de 6 faixas (entrada a entusiasta) com valores estimados a partir de desempenho relativo público; rotulado como estimativa. **Online**: botões que abrem pc-builds, 3DMark e How Many FPS com as peças do PC; sem raspagem de sites nem envio de dados pelo Lotus.
- Histórico das últimas 20 execuções em `%LOCALAPPDATA%\Lotus\score-history.json`. 285 testes aprovados em Debug e Release.

## Revisão de código (25/10/2026)

- Corrigidos: erro de escrita nas configurações de jogo virava travamento (agora mostra mensagem); backup e restauração do RivaTuner/OverlayEditor agora byte a byte (ANSI com acentos preservado; `.ovl` exportado em ANSI); o auxiliar elevado recebe a pasta Lotus do usuário (`--lotus-data`), porque o UAC pode elevar outra conta; exportar overlay sem tratamento de erro; reativar item de inicialização caía em conflito permanente (agora ativa normalmente); corrida entre reescanear e remover jogo; bibliotecas Steam em compartilhamento de rede (UNC); textos do Score ao trocar idioma.
- GPU do Score com lotes adaptativos (≥ 20 ms por sincronização) para não comprimir GPUs rápidas; fator recalibrado. 288 testes aprovados em Debug e Release.

## Ajustes pedidos após o primeiro teste (25/10/2026)

- **Overlay RivaTuner refeito** com a sintaxe real do OverlayEditor (conferida nos layouts que acompanham o RTSS e no manual): valores como `%Fonte%`; média, 1% e 0.1% low calculados ao vivo por fórmulas (`swavg`/`percentile` sobre os últimos 1024 frametimes), pois `<FRAVG>`/`<FR10L>` só funcionam no modo benchmark; posições em células; minigráfico de uso por linha. Gerado por `tools/Make-LotusOverlay.js`. Se o layout Lotus estiver ativo, o arquivo é atualizado ao abrir a tela.
- **RivaTuner em Meus Jogos** (cartão reutilizado também em Integrações); overlay completo como opção principal e o simples dentro de "Alternativa".
- **Pré-carregamento de shaders**: cartão com etapa, barra feitos/total, velocidade e tempo restante (lidos da linha de progresso do SCSKiller), Cancelar e "Ver no SCSKiller" (também no menu do jogo).
- **LOTUS SCORE**: escolha dos testes (CPU 1 núcleo, CPU todos, GPU); prévia ao vivo da cena da CPU; teste da GPU numa janela própria (lotes medidos, apresentação ~50 Hz, sem limite do monitor; fechar cancela); pontuação total só com o conjunto completo; busca individual de CPU/GPU com pontos estimados e diferença para o seu PC; textos sobrepostos corrigidos. Recalibrado: 1 400 / 10 500 / 11 500 no PC de referência.
- **Desempenho › Comparar** em 3 passos (jogo, duração, medir), com dica quando nenhum jogo está aberto e opções técnicas recolhidas.
- 292 testes aprovados em Debug e Release. Validação visual destas telas fica com o usuário.

## Preparar e jogar e overlay do usuário (25/10/2026)

- **Preparar e jogar** (botão principal do cartão do jogo; "Jogar" direto fica no menu): uma confirmação lista as mudanças pendentes do perfil Jogo (mesmo journal e desfazer da tela Otimizar) e, quando houver SCSKiller, oferece pré-carregar shaders (marcado se o driver mudou). Aplica, verifica relendo o estado, abre o jogo e acompanha (até 3 min para iniciar, depois até o último processo fechar). Ao fechar, pergunta se volta ao normal; só desfaz o que o Lotus mudou e ninguém alterou depois.
- **Overlay oficial = "Lotus Neon Corrigido"** criado pelo usuário (`Lotus.ovl` + `Lotus-Arte-Alpha.png`, embutidos e exportados byte a byte). O gerador anterior foi removido; o Lotus não regrava o layout sozinho. Botão "Ver o overlay agora" abre a cena 3D num processo separado para o RivaTuner desenhar o overlay sobre ela.
- 295 testes aprovados em Debug e Release.

## "Por que está travando?" (25/10/2026)

- Menu do jogo, com o jogo aberto: mede 30 s (mesma captura de "Medir e ajustar" + carga da GPU) e ordena causas prováveis: shaders não pré-carregados (travadas bruscas + SCSKiller "Ready/Stale"), RAM ≥ 85% (cita os 3 programas que mais usam, sem fechar nada), PC fora do perfil Jogo, GPU no limite (deixa o ajuste de qualidade pronto para depois de fechar) ou processador/segundo plano limitando. Única correção aplicada na hora, com confirmação: o perfil Jogo (reversível). 300 testes aprovados.

## Aba Ferramentas (25/10/2026)

- **Liberar memória para jogar**: programas com janela que mais usam RAM (agrupados por nome, processos do sistema e o próprio Lotus/RTSS fora); o usuário marca e confirma; fechamento normal (`CloseMainWindow`), nunca encerramento forçado.
- **Arquivos temporários**: só `%TEMP%` do usuário, itens com mais de 24 h, enviados para a Lixeira (`SHFileOperation` com desfazer); itens em uso ficam.
- **Teste de conexão**: 20 pings ao roteador, 1.1.1.1 e 8.8.8.8; veredito separa rede da casa de internet (instável, ping alto, boa, sem internet).
- **Saúde do disco** (WMI Storage, só leitura) e **driver de vídeo** (registro, só leitura) com link apenas para o site oficial do fabricante; o Lotus não baixa nem instala drivers.
- Validado no PC real: conexão boa, SSD saudável, driver identificado. 304 testes aprovados em Debug e Release. Pendente: desinstalador completo com resíduos para a Lixeira.
- **DNS** (Ferramentas › Teste de conexão): pesquisa indicou que não há resolvedor universalmente mais rápido (varia por região e horário), então o Lotus mede no PC do usuário: 5 domínios × 3 rodadas por consulta UDP ao DNS atual e a Cloudflare, Google, Quad9 e OpenDNS (mediana e falhas). Recomenda trocar só se um público for ≥ 20% e ≥ 5 ms mais rápido sem falhas. A troca (IPv4, só o adaptador com gateway) passa pelo auxiliar elevado com lista fechada de provedores, guarda o estado anterior (automático/DHCP ou servidores manuais) e restaura exatamente; limpa o cache de DNS. Medição real: atual 8.8.8.8 = 63 ms, Cloudflare = 18 ms. Troca real não executada (decisão do usuário). Diálogos com botão "Cancelar" correto. 307 testes aprovados.

## Atualização 25/10/2026 — Início, Game list e páginas de jogo

- **Início enxuto** (sem repetir o menu): "Continue jogando" (último jogo pela data `LastPlayed` do manifesto Steam, arte da Steam, Jogar/Detalhes), **Prontidão do PC** ao vivo (0–100: memória 40, CPU 25, temperatura da GPU 15, programas pesados 10, disco 10; mostra só o maior problema e leva à tela certa; os sensores leem só com o Início visível), destaque da Game list e "Seus jogos" com Reescanear. Até concluir/pular, o **assistente guiado** ocupa o lugar da prontidão (analisa, pergunta, aplica só o aceito; tudo reversível).
- **Game list** (fora do menu: botão no Início e na barra de título): listas que se atualizam a cada 6 h — Em alta e Mais bem avaliados (SteamSpy), Mais vendidos, Lançamentos e Em breve (Steam). Busca em toda a loja Steam, filtros (ordem, nota mínima, esconder os que tenho, só gratuitos) e **sorteio** por gênero/dificuldade. Créditos: inspirado no MyGameList (mygamelist.club). O MyGameList usa IGDB, que exige credencial secreta da Twitch; por isso o Lotus usa fontes abertas sem chave.
- **Página do jogo** (Meus Jogos e Game list): sinopse, gêneros, lançamento, estúdio, Metacritic (cores do site), análises da Steam, **dificuldade estimada** pelas tags da comunidade Steam (dito na tela), capturas de tela, Jogar quando instalado. Jogos de outras lojas são encontrados na Steam pelo nome; GOG usa a descrição da própria GOG quando a Steam não tem.
- **Banner** com destaques da Steam e os jogos do jogador no topo de Meus Jogos (transição suave, zoom lento; para com "Reduzir movimento").
- Dados em cache em `%LOCALAPPDATA%\Lotus\Catalog` (funciona offline com o último download). Idioma das fontes segue o idioma ao abrir o Lotus.
- Sons: sons nativos do Fluent nos controles + sinos sintetizados do Lotus (concluído, falha, Score, boas-vindas), desligáveis em Configurações.
- Correção encontrada na revisão: reflexo (sheen) e "hover" no mesmo elemento causavam conflito de composição e impediam o Início de carregar; o reflexo agora usa o compositor da thread. Erros desse tipo e de XAML adiado passam a ir para o log.
- Validação: 343 testes aprovados (Debug). Conferido na tela com dados reais (Metacritic, listas, prontidão). Instalador **não** gerado: aguardando revisão do usuário.

## Versão 0.1.0-alpha (26/10/2026) — primeira entrega

- **Instalador**: `tools\Build-Installer.ps1` → `artifacts\dist\Lotus-Setup-0.1.0-alpha.exe` (Inno Setup 6, ~63 MB, por usuário, sem administrador, pt/en). Instala em `%LOCALAPPDATA%\Programs\Lotus`, cria atalhos (Iniciar e Área de Trabalho opcional), entrada em Aplicativos instalados e desinstalador. Fecha um Lotus aberto (inclusive na bandeja) antes de instalar/desinstalar. Dados do jogador (`%LOCALAPPDATA%\Lotus`, Documentos\Lotus) nunca são apagados. Testado: instalar, atualizar com o app aberto, desinstalar, reinstalar.
- **Game list**: listas da loja Steam só com jogos (Mais vendidos; Lançamentos = mais vendidos lançados nos últimos 120 dias; Em breve), Em alta/Mais bem avaliados (SteamSpy). Capas com fallback até a arte exata da loja. Sorteio usa as listas completas por gênero do SteamSpy + filtro "Difficult" e confirma a dificuldade pelas tags do jogo. Busca por nome em toda a loja (sem trilhas sonoras), também com Enter.
- **Perfil do jogador** (clique no nome): último jogo, horas na Steam por jogo (`localconfig.vdf`), favoritos (aparecem primeiro no Início) e anotação curta por jogo (`%LOCALAPPDATA%\Lotus\player.json`). "Editar perfil" fica no painel; "Rever boas-vindas" nas Configurações.
- **Sons**: kit "kalimba" sintetizado (sol maior pentatônico) para iniciar, navegar, abrir/fechar painéis, favoritar, concluído, falha, Score e aviso; sons do Fluent nos controles; volume em Configurações.
- **Créditos** (Configurações › FAQ): criado por Joeafricano com o Claude (Anthropic), código aberto, fontes de dados e ferramentas de terceiros.
- **Correções da varredura**: abrir qualquer ferramenta fechava o app (animação × hover); erros de interface não fecham mais o Lotus; fechar pelo X mantém na bandeja (antes encerrava); instância única (segundo atalho traz a janela de volta); teste do Score sem freio de segundo plano do Windows; menu só com ícones em janelas estreitas; Game list sem sublinhado errado no menu; banner com fallback de captura e memória de falhas; foco do teclado discreto; textos cortados; FAQ/integrações atualizados; versão 0.1.0-alpha nas Configurações.
- **Observação**: compilações Debug deixam o cálculo do LOTUS SCORE ~7× mais lento (otimizações do .NET desligadas). Notas valem só na versão Release/instalada (1 núcleo ≈ 1.412 neste PC).
- Validação: 347 testes aprovados; telas conferidas em 1280×820, 1000×700 e maximizado; fluxos testados de ponta a ponta (assistente aplicando, Score, relatório, perfil, favoritos/notas, bandeja, instalador).

## Diário de jogos (27/10/2026)

- **Meu diário** (seção fora do menu; abre pelo perfil, pelo Início e pela celebração): perfil com foto, nome, nível, título, barra de XP e "Sobre você"; números (concluídos, horas, nota média, este ano); 14 medalhas com progresso; abas com contagem (Todos, Jogando, Concluído, Quero jogar, Em espera, Abandonado), busca (nome e análise) e ordenação. Cada jogo mostra capa, posição, gêneros/ano, data, plataforma, horas, conquistas, nota colorida (verde ≥ 7,5, amarelo ≥ 5, vermelho) e a análise.
- **Registrar** (Game list, Meus Jogos, Início e perfil): nota 0,5–10 por arrasto, estado, plataforma, horas (preenche com as horas da Steam), início/fim, conquistas (Platina/100%, NG+, Dificuldade máxima, Sem mortes, Speedrun, Pacifista, Todos os finais, Co-op) e análise até 5.000 caracteres. **Quero jogar** com um clique na página do jogo e na estrela de cada capa da Game list.
- **Gamificação**: XP por jogo (concluir 100, nota 10, análise 30/60, conquista 20, platina 60, 1 XP a cada 2 h até 50); níveis 50·n·(n−1) com títulos (Recruta → Lótus de Ouro); medalhas calculadas sempre a partir do diário. Ao concluir, subir de nível ou ganhar medalha: celebração com confete, capa girando, XP contando, barra virando de nível e medalhas aparecendo.
- **Início**: card "Meu diário" com nível e XP ao lado da Game list e faixa "Na sua lista" (Jogando e Quero jogar). **Perfil** (clique no nome) virou a miniatura do diário.
- Dados em `%LOCALAPPDATA%\Lotus\diary.json` (+ `.bak`, recuperado automaticamente se o arquivo estragar). Nada sai do PC.
- **Correção**: o X do painel do perfil não fechava. Ao fechar, o painel chamava `FocusManager.TryMoveFocus` sem o parâmetro que o WinUI desktop exige, gerando erro em todo fechamento. Agora usa `FocusHelper.Commit`, e o painel abre abaixo da barra de título.
- Validação: 351 testes; fluxo testado na tela (Quero jogar, registrar concluído com nota e análise, celebração, diário, Início, perfil).

## Versão 0.2.0-alpha — "Lotus Base" (26/10/2026)

- **Meu diário em destaque** ao lado do nome do jogador (com o nível), visível em todas as telas; o Início perdeu o card repetido e ficou só com a faixa "Na sua lista".
- **Conquistas**: 54 conquistas do Lotus (Diário, Caçador, Biblioteca, Lotus e Secretas) em bronze/prata/ouro/platina, com XP; conquistas da Steam lidas da cache local da Steam (sem login, funciona com perfil privado) e raridade global; tudo soma no nível do diário (novos títulos Mito e Imortal). Diário com seções Jogos / Conquistas / Steam, "Quase lá", vitrine das mais raras, aviso ao desbloquear e conquistas do jogo na página do jogo. Detalhes técnicos: `src/Lotus.Core/Achievements/`.
- **Backup**: Configurações › Backup, arquivo `.lotusbackup` offline (salvar/restaurar com reinício), cópia automática semanal e Google Drive opcional. Ver `docs/LOTUS_BACKUP.md`.
- Instalador: `tools\Build-Installer.ps1 -Name "Lotus Base"` → `artifacts\dist\Lotus-Base-Setup-0.2.0-alpha.exe`. A atualização visual (pacote "Conceito Premium") sai depois como "Lotus New".

## Versão 0.3.0-alpha — "Lotus New" (27/10/2026)

Atualização visual a partir do pacote "Conceito Premium" (Rosé Quartz):
- **Temas** Escuro, Claro (porcelana rosada, contraste rosa forte), Sistema (segue o Windows ao vivo) e Grafite; **acentos** Rosê, Violeta, Azul, Verde (e os antigos Carmesim, Dourado, Escuro). Configurações › Aparência com cartões de tema e cores. Uma única autoridade de cores (`ThemePalette`), troca sem reiniciar; alto contraste usa as cores do sistema.
- **Navegação lateral** (completa, só ícones ou recolhível conforme a largura), com Game list e **Meu diário em dourado** com o nível; Configurações e perfil embaixo; títulos de página grandes; heróis com foto sempre escuros; o nome **Lotus** dourado com efeitos em todos os temas; "Lotus Score" em caixa normal.
- Inclui a correção do botão Jogar das capas (0.2.1).
- Instalador: `tools\Build-Installer.ps1 -Name "Lotus New"` → `artifacts\dist\Lotus-New-Setup-0.3.0-alpha.exe`; instala por cima (atualização), sem desinstalar e sem tocar nos dados.
