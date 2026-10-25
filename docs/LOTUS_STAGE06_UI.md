# Etapa 06 — Interface e identidade visual (24/10/2026)

Redesenho da interface WinUI 3 e da marca, sem mudar arquitetura MVVM/DI nem funcionalidades.

- **Identidade:** logo do usuário recriada em vetor (flor com contorno ouro rosé e pétalas carmesim; wordmark "Lotus" em Georgia com degradê ouro). Tokens de cor: base ameixa `#170B13`, destaque carmesim `#D6306F`, rosa `#F27BA8`, ouro rosé `#EEC1A8`. `ThemePalette` atualiza os pincéis em tempo real; o tema Grafite mantém os destaques em superfícies neutras; alto contraste usa as cores do sistema.
- **Shell:** barra de título integrada, navegação translúcida, área de conteúdo arredondada, cards translúcidos, linhas de configuração no estilo do Windows 11, entrada escalonada (`EntranceThemeTransition`), desligada por "Reduzir animações".
- **Marca animada (`BrandPulse`):** substitui a grade de pixels. Usa a máscara alfa do texto/flor (nítida em qualquer DPI): halo que respira, reflexo a cada 7 s e flor com efeito mola ao aparecer. Só compositor, sem temporizador de UI.
- **Perfis (Otimizar):** cards Jogo/Diário/Personalizado, "Alterações previstas" (energia, inicialização somente leitura, ações reversíveis) e faixa de segurança. Game Mode do conceito **não** foi incluído: não existe no Lotus. Os textos evitam prometer reversão absoluta.
- **Inicialização (aprendizado do ResetExplode):** status Habilitado/Desabilitado lido de `Explorer\StartupApproved` (convenção do Gerenciador de Tarefas), somente leitura. Corrigida a duplicidade de `HKCU\...\Run` lido nas visões de 64 e 32 bits (não é redirecionado) e ignorado `desktop.ini`. Consulta real: 17 entradas (5 habilitadas, 12 desabilitadas), antes reportadas como 33.
- **Ícone e atalho:** `tools/New-LotusIcon.ps1` gera `src/Lotus.App/Assets/Lotus.ico` a partir do mesmo vetor; `tools/New-LotusShortcut.ps1` cria `Lotus.lnk` (ignorado pelo Git) apontando para o build Release.

Validação: builds Debug/x64 e Release/x64 sem avisos/erros; 161 testes aprovados em cada configuração (154 anteriores + 7 novos de inicialização). Capturas de Início, Otimizar, Desempenho e Configurações verificadas no app real. Pendente: alteração real do plano de energia (aguarda aprovação específica) e checkpoint Git.

## Incremento — navegação superior e recursos de jogos (24/10/2026)

- **Navegação superior** (`PaneDisplayMode=Top`) a pedido do usuário; marca na barra de título, perfil como chip compacto. A preferência da barra lateral fica preservada e não é sobrescrita no modo superior. Pílulas e círculos decorativos foram substituídos por etiquetas com barra de destaque e seleções em quadrado arredondado.
- **Recursos do Windows para jogos** (somente leitura, `WindowsGameFeatures`): Modo de Jogo (`GameBar\AutoGameModeEnabled`), gravação em segundo plano (`GameDVR\HistoricalCaptureEnabled`), otimizações para jogos em janela (`DirectXUserGlobalSettings`) e HAGS (`GraphicsDrivers\HwSchMode`). Valor ausente aparece como "Padrão do Windows"; só os padrões documentados (Modo de Jogo ligado, gravação desligada) contam como adequados. Recomendações por perfil em `GameFeatureRules`; HAGS sem recomendação (depende de GPU/driver, exige reinício). Orientação manual: botão abre apenas URIs `ms-settings:`; não há gravação por falta de API oficial verificável.
- **Correção:** o painel de perfis voltou a carregar planos e histórico ao abrir (chamada `RefreshAsync` perdida na reescrita do code-behind), detectada pela validação por UI Automation.
- **Validação interativa (UIA, sem aplicar):** seleção Jogo/Diário/Personalizado, salvar, analisar ("Diferença encontrada"), Aplicar bloqueado sem confirmação, inicialização (17 entradas), recursos (1 item a ajustar). Plano ativo QuickCpuScheme idêntico antes e depois.
- Testes: 173 aprovados em Debug e Release (12 novos de recursos/regras/leitura real).

## Teste real autorizado — plano de energia (24/10/2026)

Autorização específica do usuário para mudança temporária com restauração condicional. Executado pelo build Release via UI Automation, conferindo `powercfg /getactivescheme` a cada passo:

1. Antes: QuickCpuScheme `74a73e13-bafc-4f87-a926-4cc004e707c3`; Alto desempenho disponível.
2. Perfil Jogo → Analisar ("Diferença encontrada") → Confirmar → Aplicar: "Plano ativo verificado. Isso não comprova ganho de desempenho." Ativo: Alto desempenho `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`.
3. Confirmar → Desfazer: "Plano anterior restaurado e verificado." Ativo: QuickCpuScheme.
4. Rede de segurança (restauração por powercfg) não foi necessária. Sem elevação (`asInvoker`), sem reinício.

Critério de conclusão da Etapa 06 atendido: Selecionar perfil → Analisar → Confirmar → Aplicar → Verificar → Desfazer funcionando com uma ação real. Limitações mantidas: sem compare-and-set atômico do Windows; mudança externa entre leituras não é impedida; nenhum ganho de desempenho foi medido. Pendentes para incrementos: desativação seletiva da inicialização e perfis personalizados nomeados.

## Diagnóstico de hardware (somente leitura, 24/10/2026)

Card "Verificação de hardware" no Início, executado automaticamente na primeira visita e sob demanda. Busca configurações que limitam o hardware, sem alterar nada:

- **Monitor:** `EnumDisplaySettings`; atenção quando a taxa atual fica mais de 1 Hz abaixo da máxima na mesma resolução (link para Configurações › Tela).
- **RAM:** tabela SMBIOS tipo 17 via `GetSystemFirmwareTable('RSMB')` (sem dependência WMI). Canal único, módulos no mesmo canal, velocidade configurada abaixo da nominal (XMP/EXPO/DOCP) e aviso informativo para velocidades JEDEC básicas (DDR4 ≤ 2666, DDR5 ≤ 4800), já que a velocidade nominal do SMBIOS costuma ser JEDEC.
- **CPU:** `CallNtPowerInformation(ProcessorInformation)`; atenção se `MhzLimit` < 90% do máximo.
- **GPU:** motivos de limitação NVML da última leitura do monitoramento (sem segunda sessão NVML). Térmica/proteção de hardware = atenção; teto de energia = normal sob carga; ociosa = verificar com jogo aberto. Adaptadores por `EnumDisplayDevices`, com detecção heurística de GPU híbrida (link para Configurações › Gráficos).

Resultado real neste PC: 1920×1080 a 75 Hz (máximo), 2×8 GB DDR4 3200 em dual channel, CPU sem limite, RTX 5060 dedicada; nenhum limitador encontrado. Limitação da GPU depende de uma leitura recente em Desempenho. Testes: 197 aprovados em Debug e Release (24 novos, incluindo parser SMBIOS com bytes sintéticos e leitura real somente leitura).

## Segunda ação real: otimizações para jogos em janela (24/10/2026)

`WindowedOptimizationsAction` (`windows.directx.windowed-optimizations.v1`) usa o mesmo motor/journal da ação de energia. Grava `SwapEffectUpgradeEnable=1` em `HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings` — o valor que Configurações › Tela › Gráficos escreve —, preservando os demais pares (AutoHDR, VRR) e a ordem. Sem elevação; elegível no Windows 11 (build ≥ 22000).

- Já ativado (inclusive com formatação diferente) = `NoChange`, sem regravar. Mudança externa antes de aplicar ou antes de desfazer = conflito, nada sobrescrito. Desfazer restaura o valor original exato (inclusive ausente). Recuperação após interrupção não atribui autoria (`CanAttributeRecovery=false`).
- O motor agora recupera e lista apenas os planos da própria ação, permitindo várias ações no mesmo journal; o bloqueio global contra operações concorrentes foi mantido.
- Interface: "Ativar pelo Lotus" e "Desfazer" na linha do recurso, sempre com diálogo de confirmação explícita; status com verificação. Detecção e ação usam o mesmo parser.
- Estado real neste PC: `SwapEffectUpgradeEnable=0;` (desativado explicitamente). Nenhuma escrita real foi feita; testes automatizados usam registro simulado. Testes: 207 aprovados em Debug e Release.

### Teste real autorizado — jogos em janela (24/10/2026)

Build Release via UI Automation, confirmando no diálogo do Lotus e conferindo o registro a cada passo: antes `SwapEffectUpgradeEnable=0;` → "Ativar pelo Lotus" + Confirmar: "ativadas e verificadas", valor `SwapEffectUpgradeEnable=1;` → "Desfazer" + Confirmar: "Valor anterior restaurado e verificado", valor `SwapEffectUpgradeEnable=0;`. Rede de segurança não usada; sem elevação. Nenhum ganho de desempenho foi medido neste teste.
