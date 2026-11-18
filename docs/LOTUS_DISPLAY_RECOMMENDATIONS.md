# Recomendações visuais e fluidez — 15/11/2026

## Revisão de interação: aplicar primeiro, refinar depois

A escolha agora usa quatro cards com ícones (Dia a dia/Jogos/Edição/Filmes), três cards de iluminação e o botão explícito Aplicar sugestão e testar no monitor. Imagens e sliders só aparecem quando Begin devolve Preview; em falha, a tela de escolhas continua visível e mostra o erro. Na tela de ajuste, etapas ficam imediatamente acima da imagem, com comparação Original/Com ajuste, estados de processamento/confirmação, Salvar este visual e Desfazer e voltar à escolha. Valores neutros são identificados como preservação do original, sem sugerir mudança inexistente.

Causa do cancelamento ao minimizar: MainWindow.LightMode liberava ContentFrame depois de 3 segundos, disparando Unloaded e CancelPreview no painel. O modo leve agora preserva a página e a prioridade normal enquanto há prévia ou operação de calibração em curso. Navegar para outra ferramenta ainda cancela uma prévia não salva; minimizar não tem esse significado. A reversão de segurança permanece independente da página: agora 2 minutos sem alterações e teto de 10 minutos, informados na confirmação e contagem. Perfis confirmados continuam salvos.

A comparação não usa debounce de slider, pode substituir um pedido pendente e só confirma Original/Com ajuste depois do resultado verificado. Repetir o mesmo destino evita escrita e persistência redundantes, mas mantém leitura de conflito e prazo de segurança. Salvar evita reaplicar um perfil já conferido quando nenhuma alteração ficou pendente. Essas reduções de trabalho não prometem eliminar a latência física de DDC/CI.

O painel é construído uma só vez por instância, evitando reparentear controles em um segundo Loaded. Minimização real e revisão visual precisam de teste manual: a ferramenta de inspeção de janela apresentou o bloqueio de identidade já registrado. Não foram alterados controles físicos do monitor nesta revisão de desenvolvimento.

## Pesquisa e decisão

- [DisplayCAL, presets](https://displaycal.net/#presets): pontos de partida dependem do monitor, ambiente e objetivo. Não há configuração universal correta. Medição e perfil exigem instrumento. [ArgyllCMS dispcal](https://www.argyllcms.com/doc/dispcal.html) recebe alvos de branco, luminância e resposta, mostra patches e usa suas leituras colorimétricas; o Lotus não reproduz isso sem sensor.
- [novideo_srgb](https://github.com/ledoge/novideo_srgb): gamut clamp com EDID/ICC e API NVIDIA não documentada; calibração completa LUT-Matrix-LUT é distinta de saturação/gamma simples. O próprio projeto alerta que gamma/cinza variam entre unidades. GPL-3.0; referência técnica, sem incorporar código/binários.
- [ColorControl](https://github.com/Maassoft/ColorControl): presets NVIDIA/AMD, formatos de saída, HDR, profundidade e perfis. Demonstra orquestração de preferências, não medição automática da fidelidade de qualquer monitor. GPL-3.0 e componentes com outros avisos; nenhuma dependência adicionada.
- [Monitorian](https://github.com/emoacht/Monitorian): referência MIT para controles físicos por monitor e ambiente com sensor quando existente. O Lotus não detecta a iluminação nesta entrega: pergunta ao jogador.
- [Microsoft SetDeviceGammaRamp](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp): API legada, pode ignorar escrita, conflitar com calibradores e ser substituída em eventos de exibição. Microsoft recomenda ICC para calibração. Continua experimental no Lotus, com leitura de verificação, HDR bloqueado e backup; não é declarada confiável apenas pelo retorno BOOL.

## Entrega utilizável

Sugestões locais instantâneas, sem consultas de rede ou DDC ao escolher objetivo/ambiente. A execução física só começa após a confirmação existente. Objetivos: Dia a dia, Jogos, Edição, Filmes. Todos preservam contraste e branco originais, sem saturação. Edição preserva toda a base original e orienta ICC específico e aplicativo com gestão de cores. Não instala perfil genérico e não promete nits, D65 ou gamma absoluto medidos.

Heurística explicitamente de preferência: iluminação escura sugere -8 pontos no controle de brilho; clara +5; normal preserva. Limitada a 0–100 no intervalo físico informado, não corresponde a luminância. Jogos oferece correção relativa de meios-tons 1,04× quando há LUT SDR; os demais preservam 1,00×. Esses números são escolhas conservadoras do Lotus para comparação subjetiva, não valores de calibração publicados pelos projetos. Ajuste ausente não é inventado. Base inicial persistida evita acumulação em escolhas repetidas.

## Travamento e eficiência

A interface consultava HasPreview/HasBackup/ProfileFor usando o lock de transação. O worker mantinha esse lock durante DDC/CI; o ticker de UI esperava o hardware e congelava. Leitores agora usam um snapshot publicado atomicamente, enquanto persistência/escrita/restauração continuam serializadas. Teste de regressão segura a escrita e exige que o leitor de UI termine antes de liberar o driver.

Read(identity) agora lê DDC apenas do monitor escolhido, preservando enumeração de topologia para identidade e clones. Sliders atualizam os números na hora, reúnem eventos por 350 ms, descartam versões antigas e mantêm só uma transação física em curso. O aviso informa quando o monitor está aplicando; não se promete reduzir o tempo físico do firmware. Nenhum polling de DDC em segundo plano ou durante jogos foi adicionado.

## Ferramentas

Busca em largura disponível, filtros por categoria, cards horizontais mais compactos, até três colunas adaptáveis e sem animação de zoom por card. Check-up continua em destaque. Todos os IDs, ações e regras anteriores de disponibilidade foram preservados; categorias e busca só filtram a apresentação.

## Validação

43 testes direcionados aprovados em Release/x64, incluindo regressão do bloqueio e recomendações por uso/ambiente/capacidade. Build WinUI Debug/x64 e Release/x64 sem avisos ou erros. Evidências completas da suite no relatório final desta entrega. Nenhuma nova escrita no monitor realizada para esta alteração: o ensaio anterior falhou na aplicação e confirmou restauração, conforme LOTUS_VISUAL_STUDIO.md.

Suite completa: 891 aprovados, zero falhas/ignorados, tanto Debug/x64 quanto Release/x64. Logs: artifacts/calibration-responsive-tests-debug.log e calibration-responsive-tests-release.log; TRX calibration-responsive-debug.trx e calibration-responsive-release.trx. Release reaberto maximizado com --dev-instance para teste manual; sem instalador. Inspeção visual automática continua sem validação devido ao bloqueio já registrado da ferramenta de janela.

Validação da revisão de fluxo: 44 testes direcionados e suite completa de 892 aprovados em Debug/x64 e Release/x64, sem falhas/ignorados. TRX calibration-flow-debug.trx e calibration-flow-release.trx. Builds finais WinUI Debug/Release: zero avisos/erros, logs artifacts/calibration-flow-app-debug.log e calibration-flow-app-release.log. Após aplicar/salvar/desfazer, o painel traz a confirmação para a área visível, evitando manter um deslocamento de rolagem da tela anterior. Release de desenvolvimento reaberto maximizado, sem instalador; validação visual e minimizar no hardware continuam pendentes do teste do jogador.
