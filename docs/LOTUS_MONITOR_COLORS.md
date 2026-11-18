# Cores do monitor — pesquisa e primeiro incremento

## Decisão

O usuário pediu controle automático NVIDIA e escolheu oferecer os perfis Natural e Vivo. A primeira integração controla **Digital Vibrance**, com confirmação, teste de 15 segundos e restauração. Não se apresenta esse ajuste como calibração medida. Os números de brilho/contraste/gamma ideais não podem ser inferidos com precisão apenas do modelo ou EDID; não se altera isso arbitrariamente. Não modifica a aplicação de configurações gráficas dos jogos, que continua pausada. O instalador 1.7.1 já entregue não foi substituído.

## Projetos verificados em 15/11/2026

| Projeto | Utilidade | Licença / decisão |
| --- | --- | --- |
| [ColorControl](https://github.com/Maassoft/ColorControl/tree/d0d3bb4682c483b0b2c14a794958abf2d142100e) | Presets NVIDIA/AMD, formato e profundidade de saída, HDR, dithering e perfis. | GPL-3.0 e componentes com licenças próprias. Referência de capacidades; nenhum código ou binário incorporado. |
| [novideo_srgb](https://github.com/ledoge/novideo_srgb/tree/6d8f875934fe095a5a5c9cfb5bb77e2342000159) | Conversão de gamut para sRGB/outros espaços com EDID ou ICC; calibração LUT-Matrix-LUT. | GPL-3.0. Não incorporado: usa API privada, documenta conflitos com HDR/driver, risco de dupla correção de ICC e banding. EDID não é uma medição da unidade. |
| [vibranceGUI](https://github.com/juv/vibranceGUI/tree/919a9f2588ccf534f5fb341fdcd4f157d3b79f12) | Intensidade/saturação por aplicativo. | API do GitHub não identificou licença raiz; isso não equivale a permissão de cópia. Somente referência de comportamento. |
| [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) | Wrapper .NET amplo da NVAPI. | LGPL-3.0. Desnecessário adicionar um wrapper inteiro para duas chamadas. Não incorporado. |
| [NVIDIA_NvAPI](https://github.com/jNizM/NVIDIA_NvAPI/tree/b29bd4fb711c38982a07599c509b6945db69836d) | Exemplos pequenos com o ABI DVC Ex (20 bytes, versão 1), leitura de mínimo/máximo/neutro e escrita. | MIT. Referência para implementação própria em C#; copyright e licença completos incluídos em THIRD-PARTY-NOTICES.md e copiados à saída do aplicativo. |
| [Monitorian](https://github.com/emoacht/Monitorian) | Brilho físico via DDC/CI em múltiplos monitores. | MIT. Útil para outro tipo de controle, mas não necessário neste incremento de intensidade NVIDIA. Não incorporado. |

A análise examinou documentação, licença e interfaces relevantes; os aplicativos externos não foram instalados nem testados neste PC. Popularidade não foi usada como comprovação de eficácia.

## Fontes e limites técnicos

- [NVIDIA — Adjust Desktop Color Settings](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/Display/CS_Adjust_Color_Settings_Advanced.htm): intensidade e pipeline de precisão são conceitos diferentes.
- [NVIDIA — display handles](https://docs.nvidia.com/nvapi/group__disphandle.html): resolver novamente pelo nome de saída após alterações de modo. Não guardar ponteiros entre operações.
- [NVIDIA — IDs públicos](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h): inicialização e associação do nome do display. DVC Ex é API não documentada, identificada separadamente nos exemplos MIT.
- [Microsoft — pipeline MHC/ICC](https://learn.microsoft.com/en-us/windows/win32/wcs/display-calibration-mhc): calibração com transformações e gerenciamento de cores; não equivale a aumentar saturação.
- [Microsoft — Windows HDR Calibration](https://support.microsoft.com/en-us/windows/hardware/display-graphics/calibrate-your-hdr-display-using-the-windows-hdr-calibration-app): fluxo próprio para HDR.
- [novideo_srgb — notas e limitações](https://github.com/ledoge/novideo_srgb#known-issues): não impor clamp SDR universal, especialmente com HDR ou perfis já ativos.

## Comportamento e segurança

- Em Ferramentas → Bônus experimentais → Cores do monitor; monitor explícito e dois perfis. Natural = neutro informado pelo driver; Vivo = neutro + 20% do intervalo entre neutro e máximo (normalmente 50 → 60%). É um preset moderado, não um valor medido como ideal para todos os monitores.
- Aplicação real somente após diálogo e clique de confirmação. Mantém ou desfaz; timer independente da página. Navegar para fora cancela o teste. Fechamento inesperado tenta recuperar o teste na próxima abertura, sem prometer restauração com o processo encerrado.
- Altera Digital Vibrance no monitor inteiro, não apenas em um jogo. Não altera brilho, contraste, gamma, resolução, Hz, ICC, gamut, bit depth ou menu físico. Não há shader/injeção ou processamento por frame, nem polling durante os jogos. Não foi feita medição A/B de custo em FPS.
- Somente NVIDIA conectada diretamente à saída reconhecida; sem fallback fingindo sucesso para AMD/Intel, notebooks híbridos ou drivers não compatíveis. Clone de telas com a mesma origem é bloqueado.
- DLL somente do System32, já instalada pelo driver. Não baixa/carrega DLL de terceiros. Não abre nem automatiza o painel NVIDIA.
- Identidade persistida é o caminho do monitor retornado por QueryDisplayConfig; nome GDI sozinho não redireciona restauração a outro monitor.
- Bloqueia alteração/restauração se Advanced Color estiver ativo ou desconhecido (conservador: inclui ACM SDR, além de HDR). Nunca desativa HDR. Não aplica sRGB clamp; aplicativos que usem NVAPI HDR independentemente do estado do Windows não são identificados por este diagnóstico Windows.
- Backup atômico e flush antes da mudança; verifica retorno do driver e releitura. Falha parcial tenta desfazer. Falhas de gravação, desconexão e alteração externa preservam o backup. Não sobrescreve automaticamente ajuste feito por outra ferramenta; uma restauração explícita exige confirmação e uma nova conferência do estado autorizado. Sem reimposição automática de presets na inicialização.
- O backup contém apenas identidade e intensidade DVC. Preserva o primeiro valor anterior ao Lotus através de múltiplos presets e reinícios; cancelar um segundo teste retorna ao preset confirmado anterior.

## Evidências

- Leitura real, sem escrita: `artifacts/monitor-colors-readonly.log`. LF24T35, `DISPLAY1`, DVC atual 53, mínimo 0, máximo 100, neutro 50, AdvancedColor=false. Isso confirma detecção e leitura, não eficácia colorimétrica nem aplicação física.
- Testes direcionados finais: 36 aprovados. Simulam driver e relógio para falhas e rollback; não são medições de hardware. Teste de persistência usa arquivo temporário isolado.
- Aplicação física e avaliação visual dependem da confirmação do jogador pelo novo controle. Não foram alteradas as cores durante a validação automatizada.

- Personalização: slider de intensidade sem escrita ao arrastar; o botão de teste usa o mesmo backup, prazo e verificação dos presets. Padrões estáticos sRGB gerados em código (tons de pele ilustrativos, cores primárias/secundárias, degradês e passos de sombra/luz), com fundo neutro e sem assets externos. Não são fotografias de referência nem medições de colorimetria.
- Durante o teste, diálogo com os padrões, contagem regressiva e ações Manter/Desfazer. Expiração fecha a prévia e tenta restaurar, sem depender da página estar visível.

## Validação final e entrega para teste

- 848 testes aprovados em Debug/x64 e 848 em Release/x64, sem falhas ou ignorados: `monitor-colors-debug.trx` e `monitor-colors-release.trx`. Após ajuste de análise de nulidade sem mudança funcional, 36 testes direcionados aprovados novamente.
- Builds WinUI Debug/x64 e Release/x64 finais com zero avisos/erros (`artifacts/monitor-colors-app-debug.log`, `monitor-colors-app-release.log`).
- Compilação Release aberta para o jogador testar; confirmação do processo pelo caminho local e estado Responding. Não se executou escrita física DVC na validação automatizada.
- Verificação visual automatizada bloqueada pelo mapeamento duplicado de identidade já conhecido: `window id 2097826 no longer belongs to C:\Users\JUNIOR\AppData\Local\Programs\Lotus\app\Lotus.App.exe; current owner is C:\Users\JUNIOR\AppData\Local\Programs\Lotus\app\Lotus.App.exe`. Não foram enviados cliques à janela ambígua.
- Nenhum novo instalador foi gerado nem houve publicação. O instalador anterior não está mais no caminho original; não se afirma nova conferência de seu hash. Durante a primeira abertura normal, a preferência existente de atualização automática encontrou a tag 1.7.2-alpha e iniciou instalação/reabertura, conforme log local às 16:06. A sessão para teste foi reaberta com `--dev-instance --maximized`: a proteção já existente em InstallUpdate impede substituir essa instância pela release. Nenhuma preferência de atualização do usuário foi modificada.
