# Lotus 1.8 — base técnica do ajuste visual

Pesquisa de fontes primárias em 15/11/2026. Este documento separa padrões de reprodução, medições que não temos e escolhas de preferência do Lotus. A proposta de redesenho de Ferramentas gerada por imagem foi rejeitada; não foi implementada.

## De onde saem os números atuais

Brilho e contraste vêm dos valores mínimo/atual/máximo reportados pelo próprio monitor via DDC/CI. A porcentagem normaliza esse intervalo. Na leitura local anterior, o Samsung LF24T35 informou brilho 65 e contraste 60, ambos em 0–100. Isso não demonstra luminância, contraste óptico ou qualidade de calibração; também não prova que sejam valores de fábrica.

A curva SDR é lida do Windows. Gamma 1,00× no Lotus preserva essa curva: não significa que o painel tenha gamma óptico 1,0. A intensidade é lida do driver NVIDIA, incluindo seu mínimo, máximo e padrão neutro. Não se supõe que todo driver tenha neutro 50 ou a mesma escala. Em saídas sem integração compatível, o controle fica indisponível, em vez de simular aplicação global.

Os perfis pareciam semelhantes porque o Lotus deliberadamente preservava contraste e branco. Variar os números apenas para diferenciá-los daria aparência de calibração sem evidência. A similaridade é explicada na interface; a pesquisa não encontrou tabela universal que transforme modelo/uso em percentuais confiáveis para qualquer unidade.

## Padrões de referência, com contexto

### Calibração e caracterização

O [International Color Consortium](https://www.color.org/displaycalibration/) distingue ajustar o comportamento da tela de criar/usar um perfil que descreva sua reprodução. O espaço de trabalho deve considerar conteúdo e gamut disponível; conteúdo web pode usar sRGB. A consequência para o Lotus é preservar a base existente e explicar o papel de um ICC do monitor. Instalar um sRGB genérico não caracteriza o painel real. Alterar apenas brilho, contraste e saturação também não substitui essa caracterização.

### sRGB

A [especificação interpretativa do ICC](https://registry.color.org/rgb-registry/files/sRGB.pdf) descreve primárias, branco D65 (x=0,3127; y=0,3290), luminância de referência de 80 cd/m² e uma curva por trechos, frequentemente resumida como gamma aproximado 2,2. São condições de referência, não instruções para pôr o slider do monitor em 80%. A transformação da prévia local usa a codificação/decodificação sRGB por trechos e processamento de intensidade em luz linear; isso não transforma um monitor desconhecido em sRGB calibrado.

### Edição em ambiente de trabalho

A [orientação da EIZO](https://www.eizo.eu/colour-management-and-calibration/colour-management-the-complete-workflow/monitor-adjustment) considera o ambiente e sugere 120–140 cd/m² para um posto médio iluminado por luz diurna, além de gamma 2,2, gamut nativo e nível de preto mínimo como alvos de ajuste naquele contexto. Esse valor não contradiz a condição de referência sRGB: as finalidades e condições diferem. Sem medir a luminância emitida, o Lotus não converte 120 cd/m² em uma porcentagem do controle. Tampouco corrige uniformidade ou amplia o gamut físico com software.

### Vídeo e filmes SDR

A [ITU-R BT.1886](https://www.itu.int/rec/R-REC-BT.1886-0-201103-I/en) define uma EOTF de referência para monitores de produção HDTV. O expoente 2,4 e os termos dependentes do preto e branco medidos não são equivalentes a escolher correção relativa 2,4× em uma LUT existente. Logo, o perfil Filmes não força esse número. Preserva a curva de origem e deixa preferência/ambiente explícitos. Este trabalho não implementa certificação de referência para cinema, HDR ou produção profissional.

## Projetos que fornecem uma base real

| Base | O que demonstra | Decisão no Lotus |
|---|---|---|
| [ArgyllCMS dispcal](https://www.argyllcms.com/doc/dispcal.html) | Usa instrumento, patches e alvos de branco, luminância e resposta para obter curvas de calibração. | Não fingir medição com uma foto nem deduzir precisão só pelo EDID. Não instalar pacote ou driver nesta entrega. |
| [DisplayCAL](https://displaycal.net/#presets) | Recomenda presets como pontos de partida; mede, caracteriza e verifica perfis com ArgyllCMS. | Manter a diferença entre preferência e fidelidade; sugerir ICC e gestão de cores para edição. Referência GPL, sem copiar código. |
| [novideo_srgb](https://github.com/ledoge/novideo_srgb) | Faz gamut clamp baseado em EDID/ICC e pode aplicar LUT–matriz–LUT via API NVIDIA não documentada; cinza/gamma variam entre unidades. | Uma LUT 1D não faz a transformação de gamut/saturação completa. Não incorporar o projeto GPL nem importar calibração de outra unidade como se fosse medição local. |
| [ColorControl](https://github.com/Maassoft/ColorControl) | Orquestra presets, controles NVIDIA/AMD e gerenciamento de perfis. | Referência para fluxo reversível; não equivale a descobrir automaticamente a calibração ideal de qualquer tela. Sem nova dependência. |
| [Monitorian](https://github.com/emoacht/Monitorian) | Controle físico por monitor, DDC/CI e diferenciação de capacidades. | Reutilizar nossa integração nativa e operar apenas nos controles expostos, sem polling no arrasto da prévia. Referência MIT, sem copiar trechos nesta revisão. |

## Saturação, Digital Vibrance e fidelidade

A [NVIDIA descreve Digital Vibrance](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/Display/To_use_Digital_Vibrance_to_make_your_desktop_colors_richer.htm) como controle da intensidade/riqueza das cores. Não é um alvo colorimétrico. O Lotus usa a integração NVIDIA já existente, com leitura, backup e verificação dentro da mesma transação de brilho/curva. O valor original permanece no backup e é restaurado junto com os demais controles. A ABI existente é referenciada no aviso MIT de jNizM/NVIDIA_NvAPI.

A [AMD documenta Custom Color](https://www.amd.com/en/resources/support-articles/faqs/DH3-021.html), incluindo saturação e uso global ou em perfis de jogos. Isso demonstra uma função do Adrenalin, mas não valida uma chamada de API equivalente no Lotus. A aplicação de saturação AMD não está implementada nesta entrega. Não usamos um filtro só na imagem para alegar alteração real no driver.

Para dia a dia, edição e filmes, o ponto de partida é a intensidade neutra informada pelo driver, não um aumento universal de saturação. Em Jogos, o preset oferece um pequeno aumento opcional: 10% do intervalo entre neutro e máximo, que pode ser reduzido ou removido antes de aplicar. Exemplo de driver 0–100 com neutro 50: sugestão 55. **Esse incremento é uma escolha de preferência do Lotus, não um valor prescrito por NVIDIA, ICC, ITU ou medição.** Intensidade, vibração e saturação de um pipeline proprietário não são matematicamente idênticas à simulação local.

## Heurísticas que não são padrões de calibração

Os passos relativos de brilho para o ambiente declarado (-8 no escuro, +5 no claro) e a correção de meios-tons 1,04× em Jogos continuam sendo preferências conservadoras para experimentar. Não vêm de uma curva luminância×controle medida no Samsung e não são anunciados como nits, D65 ou gamma absoluto. Edição preserva controles físicos e curva originais; o branco não recebe uma suposta temperatura inventada. Contraste preservado evita criar recorte de sinais sem verificar os padrões. As explicações junto dos controles descrevem o efeito e o risco visual em uma frase.

## Por que havia atraso e o que mudou

A [Microsoft documenta cerca de 40 ms para GetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-getmonitorbrightness) e [50 ms para SetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness), além de suporte incompleto a MCCS em alguns monitores. O antigo fluxo juntava debounce, persistência durável, várias leituras e verificação física a cada arrasto. O firmware podia tornar essa sequência mais lenta. Tirar as verificações tornaria o resultado menos confiável.

Na versão 1.8, arrastar controles **não chama DDC, NVAPI nem persistência de backup**. A prévia usa duas imagens pequenas e um corte geométrico para a divisória. Só uma transformação trabalha por vez; pedidos intermediários são descartados. As cenas são decodificadas em até 800 px, apenas quando mudam. Nenhum loop roda quando não há alteração; sair do painel libera os bitmaps. O teste no monitor é uma transação explícita separada, preservando leitura de identidade, backup, aplicação, confirmação e reversão.

A [Microsoft desaconselha SetDeviceGammaRamp para calibração precisa](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp): pode ignorar a escrita, sofrer interferência externa e não tem comportamento definido em HDR. O fallback SDR do Lotus permanece experimental, bloqueia HDR/Advanced Color e verifica a leitura após escrever. O resultado BOOL sozinho não é tratado como sucesso.

## Limites da prévia e validação

A prévia é uma simulação subjetiva: não modifica a luz física do painel e não conhece a transformação proprietária exata de Digital Vibrance. Processamento de gamma/contraste/branco e cores na imagem ajuda a escolher uma direção, mas o teste na tela inteira continua necessário para decidir manter. A linha Antes/Depois age somente sobre a imagem e não exibe medições de erro Delta E, gamut ou luminância inventadas. Durante teste físico, a divisória e os controles de rascunho são ocultados/bloqueados, evitando somar filtro local a um ajuste já aplicado globalmente.

Teste do algoritmo local em Release, com entrada sintética de 800×450 e 12 amostras após aquecimento: mediana 2,9832 ms, máximo 3,3345 ms. Evidência: `artifacts/calibration-18-preview-latency.json`. É medição real do processamento CPU, excluindo agendamento da UI, upload do bitmap e resposta do monitor; não equivale a latência completa ou FPS do jogo.

### Fechamento da revisão — 16/11/2026

- 56 testes direcionados e suite completa com 904 testes aprovados em Debug/x64 e outros 904 em Release/x64. Evidências em `artifacts/TestResults/calibration-18-full-debug.trx` e `calibration-18-full-release.trx`.
- Builds finais normais de Debug/x64 e Release/x64 sem avisos ou erros; build de revisão com identidade separada `Lotus.Preview` também aprovado. Essa identidade não participa do instalador.
- Conferência real da interface por Computer Use: leitura LF24T35, brilho 65 e contraste 60; perfil salvo identificado; sugestão Jogos com intensidade 55; cenas distintas nas etapas; gamma na imagem; divisória arrastada de 0,50 para 0,267; intensidade zero tornou somente o lado Depois cinza; minimizar e reabrir preservou etapa e rascunho. Nenhum comando físico foi solicitado nesses testes de interface.
- Corrigidos texto antigo na revisão final, aviso de aplicação exibido durante mera leitura, identificação da sugestão versus perfil salvo e intensidade em backups antigos. Símbolos de sombras/luzes acompanham a transformação local; durante teste físico permanecem sem filtro local.
- O teste físico autorizado anteriormente retornou `DriverError`, com reversão e conferência da base (`artifacts/calibration-physical-smoke.json`). Não é apresentado como aprovação da escrita física completa. A versão mantém o recurso experimental, valida leitura após escrita e não declara calibração óptica medida.
- Instalador gerado pelo fluxo existente para Windows x64; instalação/atualização sobre o Windows do jogador não executada nesta revisão. A verificação dos arquivos e checksums acompanha o pacote final.
