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
