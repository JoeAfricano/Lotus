# Lotus — funções de qualidade de vida (próxima versão)

Pesquisa de 12/11/2026 em projetos abertos do GitHub, seguida da implementação dos itens escolhidos pelo usuário (1, 3, 4, 5 e 8) e da correção das capas. Tudo é somente leitura no PC do jogador, salvo o que já existia (proteger/restaurar saves sempre com confirmação).

| Função | Onde | Fonte / licença | Como funciona | Limites conhecidos |
| --- | --- | --- | --- | --- |
| Capas que faltavam | Início ("Na sua lista"), diário, painel do jogador | Steam (API da loja), biblioteca, Lotus Ports | `DiaryArt`: 8 s após abrir, procura uma vez as capas ausentes ou "adivinhadas": retrato exato da Steam (consulta em lote), capa da biblioteca, capa do Lotus Ports, ou a loja Steam pelo **título exato**. Imagem que falha na tela é procurada de novo uma vez. Não muda a ordem da estante. | Jogo fora da Steam e sem capa na biblioteca (ex.: Modrinth App) continua só com o nome. |
| Saves de milhares de jogos | Backup de saves | [ludusavi-manifest](https://github.com/mtkennerly/ludusavi-manifest) (MIT), dados da PCGamingWiki | `SaveManifestStore` baixa o YAML (≈18 MB) no máximo semanalmente (ETag) e grava um índice compacto em `%LOCALAPPDATA%\Lotus\cache\save-manifest.json`. `LudusaviManifest` lê o YAML sem dependência nova; só pastas de save do Windows. `SaveDiscovery` usa o índice depois do catálogo próprio. Busca por id Steam/GOG; outras lojas pelo título exato e único (`manifest-name`, com aviso para conferir). | Recusa pastas compartilhadas (Documentos, AppData, My Games…), caminhos fora da base, pastas com mais de 4 GB ou 20 mil arquivos. Saves no registro e Xbox/Game Pass continuam fora. |
| Tempo para zerar | Editor do diário, "O que jogar agora?" | howlongtobeat.com (sem API oficial); método do [playnite-howlongtobeat-plugin](https://github.com/Lacro59/playnite-howlongtobeat-plugin) (MIT, referência) | `HowLongToBeatClient` descobre o endereço de busca no script do site, pega o token em `/init` e pesquisa só pelo título. Aceita o resultado com o mesmo id Steam ou um único título exato. `DiaryLengths`: até 12 jogos (Jogando/Pausado/Quero jogar) por início, nunca com jogo aberto; achado vale 90 dias, não achado 14. | Se o site mudar, falha em silêncio (sem tempos). |
| Bateria do controle | Preparar e Jogar | Windows.Gaming.Input; XInput (`xinput1_4.dll`) | Aviso (não bloqueia) com 20% ou menos; XInput só informa nível. Carregando/com cabo nunca avisa. | Controles que não informam bateria ao Windows não aparecem. |
| Processador no limite | "Por que está travando?" e bandeja durante o jogo | Contadores do Windows (PDH) | `% Performance Limit` e zonas térmicas ACPI, sem administrador e sem driver. Diagnóstico: média abaixo de 85% ou zona ≥ 90 °C com travadas/FPS baixo. Aviso uma vez por sessão: ~2 min abaixo de 75% ou 1 min ≥ 95 °C. | Não é a temperatura do núcleo; muitos desktops não têm zona térmica. A LibreHardwareMonitor foi descartada: exige administrador e driver de kernel (conflita com `asInvoker` e antitrapaças). |
| Driver de vídeo novo | Aviso no app e na bandeja; Configurações | Serviço de drivers da própria NVIDIA (já usado em Ferramentas) | Semanal, 3 min após abrir, nunca com jogo aberto; uma vez por versão; link só para nvidia.com. AMD/Intel: driver com mais de 6 meses, link do fabricante. | AMD e Intel não têm consulta pública de versão. |

## Validação

- Testes novos: `DiaryArtTests`, `SaveManifestTests` (inclui leitura do manifesto real com `LOTUS_MANIFEST`), `GameLengthTests`, `PlayPreparationTests` (bateria), `CpuHeatTests`, `DriverNoticeTests`.
- Verificações ao vivo opcionais (somente leitura) com `LOTUS_LIVE=1`: `LiveServicesTests` (HowLongToBeat, download do manifesto, driver NVIDIA, contadores de calor, baterias XInput).
- Manifesto real (11/11/2026): 17.699 jogos com pastas de save do Windows, 20.869 pastas, leitura em ~0,7 s.

## Ideias pesquisadas e não implementadas

Status no Discord ([discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp), MIT), áudio por jogo ([NAudio](https://github.com/naudio/NAudio), MIT), capas pelo [SteamGridDB](https://github.com/craftersmine/SteamGridDB.NET) (MIT, exige chave), mira na tela, importadores do [Playnite](https://github.com/JosefNemec/Playnite) (MIT) e drivers de controles PlayStation antigos ([DsHidMini](https://github.com/nefarius/DsHidMini)).
