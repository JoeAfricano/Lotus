# FPS estimado: fontes e método

O Lotus Score mostra quantos FPS cada jogo deve fazer no PC do jogador. É sempre uma **estimativa**, nunca uma medição do jogo; o cartão de cada jogo diz o nível de confiança.

## De onde vêm os números

A tabela fica em `data/fps-reference.json` (com data e fontes) e vai embutida no Lotus como cópia de segurança. Ela é gerada a partir de `tools/fps-reference.csv` por `tools/Build-FpsReference.ps1`.

| Confiança | O que significa | Origem |
|---|---|---|
| alta | 2 ou mais laboratórios com até 15% de diferença | (ainda nenhum jogo) |
| média | 1 laboratório | Notebookcheck, GeForce RTX 5060 desktop, 1080p, por qualidade (Baixo/Médio/Alto/Ultra) |
| baixa | sem medição de laboratório | tabela inicial do Lotus (médias públicas, sem registro de origem) ou requisitos recomendados da Steam |

Fontes de laboratório: Notebookcheck, comparativo [RTX 5060 vs RTX 5070](https://www.notebookcheck.net/GeForce-RTX-5060-vs-GeForce-RTX-5070_12954_12952.247598.0.html) e [review da Asus Prime RTX 5060 OC](https://www.notebookcheck.net/Asus-Prime-GeForce-RTX-5060-OC-review-Strong-price-performance-GPU-for-1080p-gaming.1039116.0.html). Valores marcados com "~" na página não foram usados.

Para usar depois (ainda não lançados na tabela): Tom's Hardware GPU hierarchy 2026, TechSpot/Hardware Unboxed (review RTX 5060) e GamersNexus, para cruzar e subir jogos para confiança alta. Calculadoras geradas por site (How Many FPS, CheckFPS e parecidas) não são usadas.

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

134 jogos: 32 com confiança média (Notebookcheck) e 102 com confiança baixa (22 da tabela inicial e 80 populares calculados pelos requisitos da Steam). Nenhum ainda com confiança alta. Pendente: cruzar com Tom's Hardware, TechSpot e GamersNexus; mostrar "Medido no seu PC" quando o jogo já foi medido pelo Lotus.
