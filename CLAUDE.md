# Lotus — continuidade no Claude Code

Leia `AGENTS.md` e siga suas diretrizes. Responda em português brasileiro. O projeto continua sendo Lotus, sem sufixo de versão no nome do produto.

## Estado em 24/10/2026

- Solução Windows nativa C#/.NET 10, WinUI 3, MVVM e DI. Não converter para web nem recriar a solução.
- Código em `src/`, testes em `tests/`, utilitários em `tools/`, decisões e relatórios em `docs/`.
- Telemetria real CPU/RAM/GPU, diagnóstico de RAM, localização pt-BR/en-US, preferências e benchmark PresentMon existentes devem ser preservados.
- Marca e fundo ambiental estão em standby. Não retomar refinamentos decorativos nem homologação ETW sem solicitação.
- Etapa 05: motor de otimização e journal SQLite implementados e validados em configuração descartável.
- Etapa 06 está EM ANDAMENTO e ainda sem checkpoint próprio. Há alterações locais anteriores e arquivos não rastreados que devem ser preservados.

## Etapa 06: implementação atual

- Perfis Jogo, Diário e Personalizado: seleção, persistência de escolhas, análise de diferenças e confirmação explícita.
- `PowerSchemeAction` usa `WindowsPowerSchemes`, com APIs nativas PowrProf para listar, consultar e ativar somente planos existentes.
- `SqliteOptimizationJournal` reutiliza a infraestrutura anterior, migração v2 para perfis; o armazenamento descartável da Etapa 05 continua disponível.
- `PowerProfilesViewModel` e `Controls/PowerProfilesPanel.xaml` integrados a Otimizar. Adaptadores, motor e interface têm testes com mutações simuladas.
- `WindowsStartupInventory`: leitura de Run HKCU/HKLM e pastas de inicialização; não altera entradas nem presume que todas estejam habilitadas.
- Testes reais autorizados em 24/10/2026: (1) jogos em janela ativado e desfeito para `SwapEffectUpgradeEnable=0;`; (2) Alto desempenho aplicado e verificado, Desfazer restaurou QuickCpuScheme (detalhes em `docs/LOTUS_STAGE06_UI.md`). Novas mudanças reais continuam exigindo aprovação específica.
- Consulta real: plano ativo `QuickCpuScheme`, GUID `74a73e13-bafc-4f87-a926-4cc004e707c3`. Também estavam disponíveis Equilibrado, Alto desempenho e Economia de energia. Reconsultar antes de qualquer operação.
- Foram encontradas 33 entradas de inicialização, sem fontes indisponíveis na consulta realizada.
- Windows não oferece compare-and-set atômico para o plano ativo. A implementação verifica antes/depois e bloqueia rollback diante de mudança detectada; interrupções ambíguas viram conflito. Não prometer proteção contra mudança externa entre leituras ou garantia absoluta de rollback.

## Estado em 26/10/2026 (Claude Code)

- Primeira entrega **0.1.0-alpha** pronta: instalador `artifacts\dist\Lotus-Setup-0.1.0-alpha.exe` (gerar de novo com `tools\Build-Installer.ps1`). Resumo em `docs/LOTUS_GAMES.md` (seção 0.1.0-alpha).
- Versão definida em `Directory.Build.props`. **0.2.x-alpha "Lotus Base"** (diário, conquistas, backup; 0.2.1 corrige o Jogar na capa) no worktree `C:\Projetos\Lotus-base`, instalador `artifacts\dist\Lotus-Base-Setup-*.exe`.

## Atualização 24/10/2026 (Claude Code)

- Interface redesenhada e nova identidade (logo vetorial ouro rosé/carmesim, paleta ameixa) — ver `docs/LOTUS_STAGE06_UI.md`. Marca e animação saíram do standby a pedido do usuário.
- Inicialização: status via StartupApproved (somente leitura) e correção de duplicidade HKCU; 17 entradas reais.
- ResetExplode: binário conferido e descompilado (`docs/LOTUS_RESETEXPLODE_REFERENCE.md`, fontes em `artifacts/references/ResetExplode/decompiled/`).
- Navegação superior; recursos de jogos do Windows somente leitura (orientação manual). Validação atual: Debug/Release sem avisos; 173 testes aprovados em cada. Atalho `Lotus.lnk` na raiz (build Release).
- Plano mestre recebido (Etapas 06–14). Produto afunilado para o jogador leigo: Meus Jogos → Preparar e jogar → overlay FPS → "Por que está travando?". Entrega 1 (Meus Jogos) concluída: `docs/LOTUS_GAMES.md`. Overlay pausado a pedido do usuário.
- Aguardar comandos do usuário antes das próximas etapas.

## Validação já executada (anterior)

- Builds Debug/x64 e Release/x64: aprovados, zero avisos/erros.
- Suite completa: 154 testes aprovados em Debug e 154 em Release.
- Binários de validação: `artifacts/stage06/Debug/` e `artifacts/stage06/Release/`.
- Release iniciou e a tela Início foi capturada. Validação visual/interativa dos perfis ainda pendente: Computer Use encontrou `coordinate input geometry is unavailable` e depois `user input was detected in this window; call get_window_state before continuing`. O usuário pausou o trabalho nesse ponto.
- Não repetir builds/testes sem alteração que invalide essas evidências. Não tratar a Etapa 06 como concluída.

## Próximos passos autorizados do desenvolvimento

1. Continuar validação da interface dos perfis: seleção, salvar, analisar e consulta de inicialização, inicialmente sem aplicar.
2. Solicitar aprovação específica para testar alteração real do plano e restauração condicional. Preservar `asInvoker`; não ampliar permissões automaticamente.
3. Registrar resultados e limitações em documentação concisa; criar checkpoint apenas dos arquivos pertinentes, preservando alterações anteriores.
4. Não avançar automaticamente para outros módulos.

## Transferência

O ZIP contém o diretório do projeto com histórico `.git`, arquivos rastreados, não rastreados, referências locais e artefatos existentes. Apenas o cache `.vs` e o próprio ZIP de saída ficam fora da cópia.
Dados de execução em `%LOCALAPPDATA%/Lotus` e ferramentas do ambiente do Codex estão fora do repositório e não fazem parte deste pacote.
Extraia o ZIP, abra a pasta `Lotus` no Claude Code e peça para ler este arquivo e `AGENTS.md`. Os binários Windows exigem Windows x64 e as dependências declaradas no projeto.
O pedido de exportação não autoriza aplicar otimizações ao Windows nem descartar alterações locais.

## Estado em 28/10/2026 (Claude Code)

- **0.3.0-alpha "Lotus New"**: redesign do ZIP `Lotus-Conceito-Premium` concluído (guias 01–15): tema rosé porcelana/escuro/grafite/sistema, navegação lateral, Início, perfis com confirmação animada, Meus Jogos com aba Overlay, Lotus Score com detalhes, FPS estimado (busca de qualquer jogo via requisitos da Steam, fontes com link) e gargalo, Game list (hero, filtros com indicação textual, até 100 jogos com rolagem contínua), painel do jogador, medalhas do diário, editor do diário com proteção de alterações, Comparar em duas colunas.
- Instalador de atualização: `artifacts\dist\Lotus-New-Setup-0.3.0-alpha.exe` (gerar de novo com `tools\Build-Installer.ps1 -Name "Lotus New"`). Nunca instalar no PC.
- 369 testes aprovados (Debug). Animação de "perfil aplicado" não foi vista com mudança real do Windows (exige aprovação).

## Estado em 30/10/2026 (Claude Code)

- Funcionalidades para jogadores casuais (Onde eu parei?, Preparar e Jogar, Proteção de progresso de saves) implementadas e NÃO commitadas; relatório em `docs/LOTUS_CASUAL_FEATURES.md`. 431 testes aprovados (Debug/Release).
- Ativar/restaurar saves reais e aplicar perfil ao jogar exigem aprovação do usuário.

## Estado em 31/10/2026 (Claude Code)

- **1.0.0-alpha** (título "Lotus 1.0 Alpha"): Google Drive funcionando (projeto Google Cloud `lotus-513308` na conta contato.bentojr@gmail.com, só `drive.file`, publicado em produção; credencial em `secrets\google-oauth.json`), envio ao Drive manual/semanal/mensal, atualizações pelo GitHub, Backup de saves. Detalhes em `docs/LOTUS_GOOGLE_DRIVE.md`.
