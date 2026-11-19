# Ajustes gráficos por jogo (Meus Jogos › Otimizar)

Estado em 19/11/2026. Antes, a sugestão (Baixo/Médio/Alto/Ultra, upscaling e limite) dependia só do hardware: todo jogo recebia a mesma resposta no mesmo PC. Agora cada jogo tem a sua.

## Como a sugestão é escolhida

1. **Curva do jogo neste PC** (`LotusScore.CurveForAsync` → `GameFpsCurve`): para cada qualidade, o FPS que a placa de vídeo desenha e o FPS que o processador alimenta, na resolução do monitor. Vem da mesma base do "FPS estimado" do Lotus Score:
   - pontos do teste completo do Lotus Score, ou, sem ele, os pontos da tabela de peças para a placa e o processador do PC (nome do processador lido do registro, só leitura);
   - demanda do jogo, nesta ordem: tabela `data/fps-reference.json` (laboratórios, 138 jogos); app companheiro de um jogo da tabela ("… Benchmark Tool", "Demo"); **base Steam** `data/steam-demand.json` (1.000 jogos, embutida); busca ao vivo dos requisitos recomendados da Steam (uma por jogo por sessão; só aceita o mesmo Steam ID ou o mesmo nome);
   - medição do próprio jogo no PC (Preparar e jogar / Por que está travando?) calibra a curva (0,6× a 1,4×).
2. **Meta** (`SettingsAdvisor.Recommend(..., GameFpsCurve, GameTech)`): a taxa do monitor; o limite do próprio jogo quando é menor (ex.: Elden Ring 60). Se nem em Médio (perfil Equilibrado) o PC segura a taxa, a meta vira uma fração exata da taxa do monitor (sem FreeSync/G-Sync) ou o que o PC sustenta (com FreeSync/G-Sync).
3. **Escolha**: todas as combinações qualidade × upscaling são ordenadas pela aparência (2 pontos por nível, −1 por degrau de upscaling; empate fica com imagem nativa). Vence a primeira que segura a meta com 8% de folga para cenas pesadas. Upscaling só com o que o jogo traz (DLSS em RTX, XeSS em Arc, FSR); sem detecção, o da placa.
4. **Processador**: quando o teto do processador no jogo fica abaixo da meta, o Lotus avisa que baixar gráficos não resolve.
5. **Texturas** seguem a VRAM; perfis "Mais FPS" e "Competitivo" continuam baixando 1 e 2 níveis.
6. Sem dados do jogo ou das peças, vale a regra antiga só por hardware, e o painel diz isso.

O painel mostra "deve rodar a cerca de X FPS" com a origem (laboratório, tabela sem laboratório, requisitos da Steam ou calibrada pela medição). É sempre estimativa; só "Medido no seu PC" é medição.

Exemplo (RTX 5060 + Ryzen 5 5500, 1080p 75 Hz, sem VRR): Counter-Strike 2 → Ultra nativo; Black Myth: Wukong → Alto + DLSS Qualidade (~82 FPS estimados, perto do medido no PC em 12/11); Elden Ring → meta e limite 60.

## Base Steam (`data/steam-demand.json`)

Gerada no PC por `dotnet run tools/BuildSteamBase.cs -- 1000` (lista: SteamSpy, mais donos primeiro; requisitos recomendados da loja, ou mínimos quando não há). Guarda só o Steam ID, o nome, as peças reconhecidas e os números na escala do Lotus (FPS em 1080p Alto numa RTX 5060 + Ryzen 5 5500, limitados a 1.000 = "muito leve") e a RAM recomendada. O texto dos requisitos não é guardado. Confiança sempre baixa: a medição do jogo no PC corrige.

19/11/2026: 1.000 jogos (2.216 vistos, 1.216 pulados: não são jogos, sem requisitos ou sem placa nomeada). Ficam de fora jogos cujos requisitos não nomeiam placa (CS2, Dota 2, Terraria, Stardew Valley: "1 GB de vídeo", "DirectX 11"); para eles vale a tabela de laboratório (CS2) ou a medição. `artifacts/steam-base-unmatched.txt` lista as linhas de placa não reconhecidas, para completar `ScoreParts` (peças antigas marcadas `RequirementsOnly`, fora do ranking do Lotus Score).

Quando a estimativa é incerta, o painel convida a medir ("Medir e ajustar" ou "Por que está travando?"); a medição fica salva por jogo e calibra a curva.

## Bases pesquisadas

| Base | O que tem | Licença | Uso no Lotus |
|---|---|---|---|
| Tabela do Lotus (`data/fps-reference.json`) | FPS por qualidade (laboratórios) e teto de CPU | MIT (do projeto) | Usada: curva principal |
| Steam (requisitos recomendados) | Placa/processador/RAM recomendados por jogo | Termos da Steam; lido por jogo, não redistribuído | Usada: jogos fora da tabela |
| [PCGamingWiki](https://www.pcgamingwiki.com/) (API Cargo) | Motor, DLSS/FSR/XeSS, limite de FPS, **onde fica o arquivo de configuração** | CC BY-NC-SA 3.0 ([Wikipedia](https://en.wikipedia.org/wiki/PCGamingWiki)) | Proposta: consulta por jogo com crédito e cache local, sem embutir os dados (NC/SA não combina com redistribuição MIT) |
| [playnite-systemchecker](https://github.com/Lacro59/playnite-systemchecker-plugin) | Compara requisitos (Steam/PCGamingWiki) com o PC | MIT | Referência: confirma a abordagem; não traz tabela de desempenho |
| [gpu-database (Hugging Face)](https://huggingface.co/datasets/Jr23xd23/gpu-database) | Especificações de placas (origem TechPowerUp) | Não declarada | Não usado |
| [ludusavi-manifest](https://github.com/mtkennerly/ludusavi-manifest) | Pastas de saves de milhares de jogos | MIT | Já usado nos saves |
| NVIDIA App (configurações ideais) | Ajustes por placa, sem considerar o processador | Fechado, sem API | Não usado; o Lotus cobre CPU, Hz, limite e medição |
| Calculadoras de FPS (sites) | Números gerados | Sem fonte/licença | Recusadas (regra: sem números inventados) |

Não existe hoje uma base aberta (licença compatível com MIT) com FPS por jogo × placa × processador × resolução. Por isso o Lotus combina a tabela própria com fontes, requisitos da Steam e a medição no PC do jogador.

## Aplicar de forma reversível (já existe e como crescer)

Já existe: receitas `IGameSettingsRecipe` (Unreal Engine genérica via `GameUserSettings.ini`; No Rest for the Wicked via registro; Minecraft Java via `options.txt` do perfil jogado por último em Modrinth/Prism/CurseForge/.minecraft, só `maxFps`, `renderDistance` e `simulationDistance`, sem mexer em shaders/Sodium/Iris; sem curva do jogo só o limite de FPS muda, e `javaw.exe` aberto conta como jogo aberto). Regras: só com o jogo fechado, guarda o original uma vez, grava só as chaves conhecidas, relê para conferir, botão "Restaurar originais". Medir → um passo de qualidade por vez.

Próximos passos propostos (cada um com teste e confirmação do jogador):
1. PCGamingWiki por jogo: confirmar DLSS/FSR/XeSS e o limite próprio, e achar o arquivo de configuração para novas receitas.
2. Novas receitas por motor, começando pelos jogos instalados no PC do José: Unity (PlayerPrefs no registro), Source 2 (`cs2_video.txt`), RE Engine (`config.ini`), Frostbite (`PROFSAVE_profile`), Decima/IW (`*.cfg`/`*.json`). Cada receita: transformação pura testada com arquivos reais copiados, backup e restauração.
3. Ciclo de medição: depois de aplicar, "Medir" confirma a meta e sobe/baixa um nível; o resultado calibra a curva daquele jogo.

## Levantamento no PC do José (19/11/2026)

- Final Fantasy Rebirth Simulator (UE5), Queen's Domain (UE4) e Palworld (UE5): cobertos pela receita Unreal.
- Black Myth: Wukong Benchmark Tool: formato próprio (`UISettingData` em `[/Script/GSGameSettings.GSGameUserSettings]`), códigos sem documentação e diferentes dos `sg.*`. Sem receita por decisão do José até calibrar no menu do jogo.
- Yakuza: Like a Dragon: sem arquivo de texto de gráficos; só sugestões.
