# FPS estimado: fontes e método

O Lotus Score mostra quantos FPS cada jogo deve fazer no PC do jogador. É sempre uma **estimativa**, nunca uma medição do jogo; o cartão de cada jogo diz o nível de confiança.

## De onde vêm os números

A tabela fica em `data/fps-reference.json` (com data e fontes) e vai embutida no Lotus como cópia de segurança. Ela é gerada a partir de `tools/fps-reference.csv` por `tools/Build-FpsReference.ps1`.

| Confiança | O que significa | Origem |
|---|---|---|
| alta | 2 ou mais laboratórios com até 15% de diferença | Notebookcheck cruzado com TechSpot/HUB ou GamersNexus |
| média | 1 laboratório | Notebookcheck, GeForce RTX 5060 desktop, 1080p, por qualidade (Baixo/Médio/Alto/Ultra) |
| baixa | sem medição de laboratório | tabela inicial do Lotus (médias públicas, sem registro de origem) ou requisitos recomendados da Steam |

Fontes de laboratório: Notebookcheck, comparativo [RTX 5060 vs RTX 5070](https://www.notebookcheck.net/GeForce-RTX-5060-vs-GeForce-RTX-5070_12954_12952.247598.0.html) e [review da Asus Prime RTX 5060 OC](https://www.notebookcheck.net/Asus-Prime-GeForce-RTX-5060-OC-review-Strong-price-performance-GPU-for-1080p-gaming.1039116.0.html). Valores marcados com "~" na página só foram usados quando outro laboratório confirmou (Black Myth: Wukong, confirmado pelo GamersNexus).

Cruzamento: [TechSpot/Hardware Unboxed, review RTX 5060](https://www.techspot.com/review/2992-nvidia-geforce-rtx-5060/) e [GamersNexus, review RTX 5060](https://gamersnexus.net/gpus/forbidden-review-nvidia-rtx-5060-gpu-benchmarks). Quando só existe o valor de uma review no preset mais alto, o Lotus estima o Alto como esse valor ÷ 0,78 (o passo Alto→Ultra da tabela). As páginas do Notebookcheck, TechSpot e Tom's Hardware recusam leitura automática (403), então os valores foram passados à mão; Tom's Hardware ainda não foi incorporado. Calculadoras geradas por site (How Many FPS, CheckFPS e parecidas) não são usadas.

## Como a conta é feita

1. **Placa de vídeo:** com valores de laboratório por qualidade, o Lotus usa o valor da qualidade escolhida, escalado pela nota de GPU do jogador (o PC de calibração é a RTX 5060 = 11 500 pontos) e pela resolução (1440p ≈ 68%, 4K ≈ 36%). Sem valores por qualidade, usa o valor em Alto e passos fixos por qualidade.
2. **Processador:** os laboratórios usam um Ryzen 7 9800X3D. Para não superestimar PCs como Ryzen 5 5500, cada jogo tem um teto de processador. Quando a tabela não traz esse teto, ele é estimado em 62% do valor em Baixo do laboratório (cenas limitadas pelo processador); é uma aproximação documentada.
3. **Resultado:** vale o menor entre placa e processador (mínimo suave), e o limite do próprio jogo quando existe (por exemplo, Elden Ring em 60), que o cartão mostra como selo.
4. **Jogos populares sem laboratório:** o Lotus lê os requisitos recomendados da Steam, assume que aquelas peças rodam o jogo a cerca de 60 FPS em 1080p Alto e escala para o PC do jogador. O resultado fica guardado 30 dias.

## Atualização

- Uma vez por semana, no máximo, e só com a opção ligada (Configurações › Aparência e comportamento › "Atualizar a tabela de FPS sozinho"), o Lotus pede `https://raw.githubusercontent.com/JoeAfricano/Lotus/main/data/fps-reference.json` com ETag. Sem mudança, nada é baixado.
- A cópia nova só é usada se passar na validação (esquema, quantidade de jogos, faixas de valores, fontes conhecidas) e não for mais antiga que a atual. Qualquer falha mantém a tabela em uso, sem aviso.
- No painel "FPS estimado por jogo", o rodapé mostra a data dos dados, as fontes e o botão "Atualizar agora".

## Como atualizar a tabela (no lançamento)

1. Edite `tools/fps-reference.csv` com os valores lidos nas fontes (uma linha por jogo).
2. Rode `tools/Build-FpsReference.ps1` e confira o diff de `data/fps-reference.json`.
3. Rode os testes (validação da tabela) e publique junto com a versão.

## Estado em 13/11/2026

136 jogos: 4 com confiança alta (Black Myth: Wukong, God of War Ragnarök, S.T.A.L.K.E.R. 2, Star Wars Outlaws), 35 com confiança média e 97 com confiança baixa (22 da tabela inicial e 75 populares calculados pelos requisitos da Steam). Os jogos de esports (CS2, Valorant, Fortnite, Apex, LoL, R6, Rocket League) seguem sem dado de laboratório confiável para a RTX 5060 e ficam como "Estimativa simples".

## Medido no seu PC

Quando o Lotus já mediu o jogo neste PC (Preparar e jogar / medição com PresentMon), o cartão mostra "Medido no seu PC: X FPS (1% low Y)" e a estimativa daquele jogo é calibrada: os números são escalados para que 1080p Alto bata com a média medida (limitado entre 0,6× e 1,4×, porque a medição pode ter usado outra qualidade ou resolução).
