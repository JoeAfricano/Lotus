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
