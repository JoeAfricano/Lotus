# Lotus — funcionalidades para jogadores casuais (30/10/2026)

Três recursos: **"Onde eu parei?"**, **"Preparar e Jogar"** e **"Proteção de progresso"** (saves).
Nada foi commitado. Todas as alterações estão locais, aguardando autorização.

Validação final (30/10/2026):
- Build Debug x64 e Release x64 sem erros.
- Os únicos avisos são CA1416, já existentes, em `tests/GameDiaryTests.cs` (arquivo não alterado).
- **431 testes aprovados** em Debug e **431** em Release, com 0 falhas.
- Eram 391 antes deste trabalho. Foram criados 40 testes: 9 de anotações, 12 de preparação, 18 de saves e 1 dos ícones de humor.

---

## Etapa 1 — "Onde eu parei?"

1. **Funcionalidades**
   - Uma anotação por jogo, com até 600 caracteres.
   - **Editor do diário:** seção "Onde eu parei?" com escrever, salvar, cancelar e limpar. Funciona também para jogos sem entrada no diário.
   - **Início:** o bloco "Continuar jogando" mostra "Onde você parou" e o botão "Ver anotação", que abre a anotação com opção de editar.
   - **Painel do jogador:** usa a mesma anotação (fonte única).
   - **Fim de sessão:** um aviso discreto com Anotar / Agora não / Não perguntar novamente.
     - Fica pendente se o Lotus estiver oculto ou se o Windows estiver em modo silencioso, e expira em 3 h.
     - Só aparece em sessões encerradas normalmente.
   - **Configurações:** chave "Perguntar ao fechar um jogo".
2. **Arquivos**
   - Core:
     - `Diary/ResumeNotes.cs`, `Diary/DiaryEditorResume.cs` (novos)
     - `Diary/PlaySessions.cs` (chave estável `DiaryGames.KeyFor`, motivo do fim da sessão)
     - `Diary/DiaryViewModels.cs`, `Diary/GameDiary.cs`
     - `Games/PlayerJournal.cs`
     - `Preferences.cs`
   - Infrastructure: `Games/JsonResumeNoteStore.cs` (novo), `Backup/LotusBackupArchive.cs`
   - App:
     - `Controls/GameNotice.xaml(.cs)` (novo)
     - `Controls/DiaryEditorView.xaml(.cs)`, `Controls/HomeCards.cs`, `Controls/PlayerPanel.xaml`
     - `MainWindow.Sessions.cs`, `MainWindow.xaml(.cs)`, `SectionPage.xaml`
   - Textos pt/en: chaves `Rn*` e `SetResumePrompts*`.
3. **Reutilizado:** detecção de sessão (`PlaySessionTracker`), o diário, o painel do jogador, o modelo de gravação atômica dos JSON e a animação `Motion`.
4. **Persistência**
   - `%LOCALAPPDATA%\Lotus\resume-notes.json`, versão 1, gravado de forma atômica com `.bak`.
   - Migração única das notas antigas do `player.json`; o original é mantido.
   - Incluído no `.lotusbackup`. Backups antigos, sem o arquivo, continuam restaurando.
5. **Riscos tratados**
   - Falha ao gravar não perde a anotação em memória.
   - Rascunho não salvo entra na proteção de alterações do editor.
   - Uma falha no aviso não impede o registro no diário.
   - O conteúdo das anotações nunca vai para o log.
6. **Testes:** `ResumeNoteTests` (9) e `MoodChipsUseRealIcons`, todos aprovados.
7. **Limitações:** o aviso de fim de sessão depende da detecção de sessão (verificação a cada 10 s).
8. **Preservado:** diário, conquistas, painel, backup e temas. Corrigido também o bug dos ícones de humor (□□□□).
9. **Validação manual pendente:** o aviso de fim de sessão com um jogo real, que não pôde ser simulado na interface.

## Etapa 2 — "Preparar e Jogar"

1. **Funcionalidades**
   - Todo botão Jogar passa pelo mesmo fluxo:
     - (A) jogo ainda instalado;
     - (B) perfil Jogo, só se autorizado para aquele jogo;
     - (C) controle conectado, se o jogador pediu;
     - (D) memória ≥ 90% ou menos de 2 GB livres;
     - (E) aviso de saves.
   - Fica em silêncio se nada estiver errado. Só pergunta quando há aviso real, e o jogo sempre pode ser aberto mesmo assim.
   - Depois da sessão, o perfil é restaurado, inclusive na próxima abertura do Lotus após uma queda.
   - O cartão "Preparar e jogar" na página do jogo guarda as escolhas desse jogo.
   - Configuração global "Preparar antes de jogar".
2. **Arquivos**
   - Core:
     - `Games/PlayPreparation.cs`, `Games/GamesViewModel.Prepare.cs`, `Games/GamePrepareViewModel.cs` (novos)
     - `Games/GamesViewModel.cs`
     - `Games/PlaySession.cs` (`SessionHub`)
   - Infrastructure: `Games/PlayPrepareSupport.cs` (novo).
   - App: `MainWindow.Prepare.cs`, `Controls/PrepareCard.xaml(.cs)` (novos) e `Controls/GameDetailView.xaml`.
   - Textos `Prep*`.
3. **Reutilizado:**
   - perfil Jogo (`GameProfileAdapter` com journal e detecção de conflito);
   - inicializador único (`LaunchNow`);
   - verificação de 10 s já existente, sem segundo loop de polling;
   - leitura de memória.
4. **Persistência:** `game-prefs.json` (preferências por jogo e restauração pendente), incluído no `.lotusbackup`.
5. **Riscos tratados**
   - Cada verificação tem limite de 600 ms. Disco lento conta como "desconhecido", nunca como "jogo ausente". Esse bug foi encontrado por teste e corrigido.
   - Uma exceção na preparação ainda abre o jogo.
   - A restauração do perfil detecta mudança externa e não sobrescreve.
   - Arquivos antigos sem campos novos mantêm os padrões. Bug corrigido nesta etapa, com teste.
6. **Testes:** `PlayPreparationTests` (12), aprovados.
7. **Limitações**
   - Não há como impedir que outro programa mude o plano de energia entre as leituras.
   - Detectar o controle depende de `Windows.Gaming.Input`.
8. **Preservado:** Jogar continua abrindo o jogo igual quando a preparação está desligada. Perfis e journal não foram alterados.
9. **Validação manual pendente:**
   - o diálogo de aviso antes de jogar, com memória alta ou disco cheio;
   - aplicar e restaurar o perfil com um jogo real, o que exige aprovação para alterar o Windows.

## Etapa 3 — "Proteção de progresso" (saves)

1. **Funcionalidades**
   - **Descoberta segura**, sem adivinhar pelo nome nem pelo executável:
     - por ID da loja, com um catálogo versionado de 18 jogos da Steam;
     - pela pasta Steam Cloud do app;
     - por pasta escolhida pelo jogador.
     - Xbox/Game Pass aparece como **não suportado**.
   - **Ativação por jogo**, com confirmação. A proteção só fica "ligada" depois de um primeiro backup **verificado**.
   - **Backups**
     - Gravados em `<id>.partial`, com SHA-256 calculado durante a cópia.
     - Tamanho e data reconferidos; releitura e verificação; manifesto (formato 1); movimentação final.
     - Sem duplicar quando nada mudou. Arquivo em uso ou mudando gera novas tentativas.
   - **Retenção:** 3, 5 (padrão) ou 10 automáticos. Os manuais e os "antes de restaurar" ficam separados.
   - **Backup automático após a sessão**
     - Espera 20 s e confirma que o jogo fechou. Roda fora da thread de interface.
     - Só avisa se falhar.
   - **Restauração**
     - Verifica o hash e monta o plano: substituir, recriar ou manter.
     - Exige o jogo fechado (na dúvida, considera aberto) e mostra o aviso de nuvem.
     - Pede confirmação e faz uma cópia de segurança do estado atual.
     - Troca os arquivos via `.lotus-restore` e verifica. Se falhar, desfaz tudo, inclusive arquivos criados.
     - Uma interrupção (Lotus fechado ou queda) é detectada na abertura seguinte, com "Recuperar estado anterior".
   - **Cartão "Proteção de progresso"** na página do jogo instalado:
     - status, pasta, tamanho, arquivos e data;
     - abrir e escolher pasta;
     - ativar, backup agora e desativar (os backups ficam);
     - automático e retenção;
     - lista com tipo, tamanho e integridade, com Restaurar e Excluir.
     - O disco só é lido quando o cartão é aberto.
   - **Integração com "Preparar e Jogar":** um aviso curto se houver restauração interrompida, pasta inacessível ou falha no último backup. Nunca bloqueia o jogo.
2. **Arquivos**
   - Core: `Saves/SaveVault.cs`, `Saves/SaveProtection.cs`, `Saves/GameSavesViewModel.cs` (novos).
   - Infrastructure: `Games/SaveProtectionSupport.cs` (novo, pastas conhecidas via `SHGetKnownFolderPath` e `save-protection.json`).
   - App:
     - `MainWindow.Saves.cs`, `Controls/SaveProtectionCard.xaml(.cs)` (novos)
     - `Controls/GameDetailView.xaml`, `MainWindow.xaml.cs`
   - Textos `Sp*` pt/en.
3. **Reutilizado:**
   - `SessionHub`, para o fim da sessão;
   - verificação de processo do jogo;
   - `GameNotice`;
   - padrão de JSON atômico e allow-list do `.lotusbackup`.
4. **Persistência**
   - Configurações em `save-protection.json`, que vai no `.lotusbackup`.
   - Os arquivos de save ficam em `%LOCALAPPDATA%\Lotus\SaveBackups\<jogo>-<hash>\<id>\` (`files/` e `manifest.json`).
   - **Não** vão para o `.lotusbackup` nem para o Google Drive.
5. **Riscos tratados**
   - Links, junctions e reparse points não são seguidos.
   - Arquivos só na nuvem (placeholders) são ignorados.
   - Caminhos do manifesto passam por validação: `..`, absolutos, nomes reservados, `:` e barras invertidas.
   - Jogos com o mesmo nome em lojas diferentes ficam separados.
   - Falta de espaço, arquivo bloqueado, erro de escrita e interrupção nunca deixam backup parcial como válido.
   - Formatos futuros do manifesto são ignorados.
   - Duas operações no mesmo jogo não rodam ao mesmo tempo.
   - Desativar nunca apaga backups.
6. **Testes:** `SaveProtectionTests` (18), aprovados, só com pastas temporárias e arquivos fictícios. Cobrem:
   - descoberta (encontrada, inexistente, vazia, Xbox, Steam Cloud);
   - subpastas; arquivo grande (24 MB); bloqueado; alterado durante a cópia (com e sem nova tentativa);
   - sem espaço; falha de escrita; `.partial` interrompido; hash adulterado; manifesto corrompido; duplicado;
   - mesmo nome em lojas diferentes; junction real (`mklink /J`); manifesto malicioso;
   - restauração completa com cópia de segurança; jogo aberto; cancelamento;
   - falha no meio com rollback exato; queda no meio com recuperação;
   - retenção; ativação só com backup verificado; backup após sessão;
   - configurações com `.bak` e arquivo antigo; allow-list do `.lotusbackup`; formato futuro.
7. **Limitações**
   - **Não é universal.** Funciona para jogos do catálogo, jogos Steam com Steam Cloud ou quando o jogador escolhe a pasta.
   - Xbox/Game Pass não é suportado. Jogos que guardam saves no registro ou na nuvem sem cópia local não são cobertos.
   - O catálogo tem 18 jogos.
   - Com Steam Cloud ativa, a nuvem pode pedir para escolher a versão após restaurar. O Lotus avisa, mas não controla isso.
   - O backup automático usa prioridade normal de thread; é limitado pelo disco.
8. **Preservado:** `.lotusbackup` antigo, diário, Jogar, temas claro/escuro, idiomas pt/en e redução de movimento.
9. **Validação manual pendente**
   - Ativar a proteção, fazer backup e restaurar com saves **reais** não foi feito: exige sua autorização.
   - Conferido na interface (instância dev): o cartão aparece e encontra a pasta Steam Cloud de um jogo instalado, sem ativar nada.
   - O aviso de falha automática e a recuperação de restauração interrompida só foram exercitados nos testes.

## Etapa 4 — validação integrada

- Builds Debug/Release e a suíte completa (431) foram executados de fato, com os resultados acima.
- Preferências reais do usuário conferidas após os testes na instância dev: sem alteração.
- Não foram criados `save-protection.json` nem `SaveBackups` reais.
- Sem commits, instalador ou envio ao GitHub. Tudo aguarda pedido.
