# Lotus Score — método e correção da calibração da 1.7.1

## O erro encontrado

A cena floral foi calibrada em Debug e distribuída em Release. A otimização do código altera muito a velocidade do renderizador de CPU; reutilizar o fator de Debug multiplicou indevidamente as notas. Repetir uma escala errada com pouca dispersão não a torna correta. Os testes unitários com valores artificiais verificavam a conversão, mas não detectavam esse erro de configuração.

A revisão 3 corrige os fatores com medições Release/x64 reais. A revisão 2 fica no histórico com aviso de calibração incorreta; não fornece nota atual, classificação, ranking ou estimativa de FPS. A revisão 1 continua identificada como cena anterior. Não há reescrita retroativa nem exclusão de resultados.

## Protocolo e interpretação

- Executar binários otimizados em Release/x64, sem depurador. A montagem que contém a carga é inspecionada pelo atributo `DebuggableAttribute`; compilação sem otimização não pode salvar pontuação comparável ou gerar calibração.
- Fechar jogos e tarefas pesadas; manter alimentação, refrigeração, drivers e ajustes equivalentes entre comparações. O Lotus não aplica overclock, undervolt ou troca de perfil para este ensaio.
- CPU: renderizador floral de 320 × 180, um trabalhador no teste single e um por processador lógico no multi. Conta pixels efetivamente renderizados por tempo decorrido, divididos por 100. A animação acompanha o tempo; a carga inclui cálculo e escrita da imagem. Não mede IPC universal, GHz ou desempenho de todos os programas.
- GPU: cena floral Direct3D 11 em 1920 × 1080. Conta renderizações concluídas e mede o tempo de parede do envio até a confirmação de conclusão; exclui `Present`. Inclui custo de CPU/driver/sincronização. É throughput dessa carga, não tempo isolado de GPU, nem FPS apresentado ou FPS de jogo. A apresentação pode influenciar o estado térmico/clock entre lotes; não se promete independência de todo limitador externo.
- Por componente: um aquecimento de 3 s descartado e três passadas medidas de 3 s. Usa mediana, sem remoção oculta de outliers. Registra todas as passadas, mediana, amplitude, revisão da calibração, runtime, processadores lógicos e duração no JSON do histórico.
- Amplitude relativa = `(máximo − mínimo) / mediana × 100`. A tela usa a maior amplitude dos componentes. Menos de 3% é uma heurística de boa repetibilidade deste teste curto; não é intervalo de confiança, precisão absoluta ou certificação de estabilidade prolongada. O índice secundário `100 − amplitude` também não equivale ao protocolo do 3DMark.
- Escala própria: `raw / mediana de referência × pontos de referência`. Os pontos convencionais do PC de referência são 1.400 / 10.500 / 11.500. Não representam unidades físicas ou notas de outro benchmark. Não se identifica o modelo de hardware para impor ou limitar sua nota.
- Total: média geométrica ponderada dos índices normalizados, com pesos convencionais single 20%, multi 30%, GPU 50%. Os pesos expressam uma escolha do Lotus; não provam desempenho em qualquer jogo. Resultados de cargas/revisões diferentes não são diretamente comparáveis.
- Outras máquinas da base são referências estimadas, não medições delas nesta cena. A extrapolação a partir de benchmarks externos tem limitações, pois arquiteturas respondem de modo diferente a cada carga. O ranking é orientativo, não uma classificação universal de hardware.
- FPS por jogo continua sendo modelo estimado, claramente separado de captura real. Foi removida a afirmação de erro universal de ±20%, não comprovada. Para FPS real, medir o próprio jogo nas condições desejadas. A correção não modifica as leituras dos overlays.

## Evidência local

PC: AMD Ryzen 5 5500, 12 processadores lógicos, NVIDIA GeForce RTX 5060, .NET 10.0.13. Calibração Release independente do ensaio de confirmação:

| Componente | Passadas brutas Release | Mediana usada na escala |
| --- | --- | --- |
| CPU single | 4927,6564 / 4889,8529 / 4902,2465 | 4902,246529357166 |
| CPU multi | 33030,2261 / 33715,7228 / 33326,1540 | 33326,154042430426 |
| GPU | 1187,1551 / 1203,2955 / 1170,7133 | 1187,155103196998 |

Confirmação pelo `LotusScoreViewModel` real, com CPU/GPU reais e histórico isolado em memória: single 1.382, multi 10.625, GPU 11.486, total 10.998; maior amplitude 2,2322%. Dados completos: `artifacts/validation-171-scorefix/actual-score-release.json`. Não é ensaio da interface WinUI nem captura de um jogo. O histórico do usuário foi preservado.

Uma segunda confirmação usou cópias das DLLs `Lotus.Core` e `Lotus.Infrastructure` do pacote publicado, verificadas byte a byte: 1.379 / 10.680 / 11.466, total 11.001. A amplitude single foi 3,3884% (multi 1,8858%; GPU 1,8191%); o Lotus exibiu corretamente **repetibilidade baixa**. Não foi descartada essa rodada para favorecer a estatística. A compilação/compactação do instalador ocorreu durante parte da execução, portanto não é um ensaio com máquina isolada. O teste comprova o fluxo funcional com os binários distribuídos, sem certificar precisão absoluta ou estabilidade térmica. Evidência: `published-score-release.json`; hashes em `published-probe-binaries.json`.

Esta validação local não estabelece uma base de resultados medidos para todos os processadores e GPUs. O teste curto não mede estabilidade térmica após uma hora. Três amostras não sustentam um intervalo de confiança universal.

## Referências técnicas consultadas

- [BenchmarkDotNet — Good Practices](https://benchmarkdotnet.org/articles/guides/good-practices.html): medir em Release e evitar depurador; a revisão aplica esse princípio sem substituir o mecanismo existente por BenchmarkDotNet.
- [BenchmarkDotNet — Jobs](https://benchmarkdotnet.org/articles/configs/jobs.html): distinguir aquecimento, iterações, ambiente e erro estatístico; a heurística de três passadas do Lotus não equivale ao controle adaptativo de erro dessa ferramenta.
- [SPEC CPU 2017 — Overview](https://www.spec.org/cpu2017/Docs/overview.html) e [Run and Reporting Rules](https://www.spec.org/cpu2017/Docs/runrules.html): normalização em referência medida, mediana, média geométrica e divulgação das condições. São referências de metodologia; o Lotus não executa nem implementa a suíte SPEC e suas notas não são SPECratios.
- [Microsoft — D3D11 timestamp disjoint](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_query_data_timestamp_disjoint): tempo isolado de GPU exige consultas de timestamp válidas e tratamento de descontinuidades. O tempo de parede atual do Lotus não é apresentado como essa métrica.
