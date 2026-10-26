# Backup do Lotus (0.2.0-alpha)

Configurações › **Backup**.

## Arquivo neste PC (offline)

- **Salvar backup…** grava um arquivo `.lotusbackup` (zip com `manifest.json`) onde o jogador escolher.
- **Restaurar de um arquivo…** mostra o que o backup contém (data, PC, jogador, jogos no diário, conquistas, favoritos, medições), pede confirmação, guarda o arquivo como restauração pendente e reinicia o Lotus. A restauração é aplicada em `App.OnLaunched`, antes de qualquer serviço ler os arquivos, então nada em memória sobrescreve os dados restaurados. Antes de aplicar, uma cópia `before-restore-*.lotusbackup` do estado atual vai para `%LOCALAPPDATA%\Lotus\Backups` (as 5 mais recentes ficam).
- **Cópia automática semanal**: `auto-*.lotusbackup` em `%LOCALAPPDATA%\Lotus\Backups` (as 4 mais recentes).

**Entra:** `preferences.json`, `player.json`, `diary.json`, `achievements.json`, `games-hidden.json`, `score-history.json`, `benchmarks.db` e as pastas `Profiles`, `Library`, `Overlays`, `Profile`, `Benchmarks`. A foto de perfil fora do Lotus (ex.: cache da Steam) vai junto e volta para `Lotus\Profile` se o original não existir mais.

**Não entra:** caches (`Catalog`, `Icons`), `Logs`, `Tools` e o journal do otimizador (`Optimizer\journal.db`): ele descreve alterações do Windows deste PC e restaurá-lo em outro PC seria perigoso. Arquivos `.bak/.tmp/.lock` ficam de fora. A restauração só grava caminhos da lista acima (entradas com `..` ou fora da lista são ignoradas).

## Google Drive

Login no navegador do jogador (OAuth para apps de desktop: redirect loopback `127.0.0.1` + PKCE). O Lotus nunca vê a senha. Escopo `drive.file`: o Lotus só enxerga os arquivos que ele criou, na pasta **Lotus Backups** (mantém os 10 mais recentes). O token de atualização fica no Cofre de Credenciais do Windows (`Lotus.GoogleDrive`). **Desconectar** revoga o token.

### Ativar (uma vez, pelo desenvolvedor)

1. Em <https://console.cloud.google.com/>, crie um projeto (ex.: "Lotus").
2. **APIs e serviços › Biblioteca**: ative a **Google Drive API**.
3. **Tela de consentimento OAuth**: tipo *Externo*, nome "Lotus", seu e-mail; escopo `.../auth/drive.file`. Enquanto estiver em *Teste*, adicione os e-mails que vão usar (até 100).
4. **Credenciais › Criar credenciais › ID do cliente OAuth › App para computador**. Baixe o JSON.
5. Salve o arquivo como `secrets\google-oauth.json` na raiz do repositório (fora do Git). `tools\Build-Installer.ps1` copia para o instalador. Para testar no Debug, copie também para `%LOCALAPPDATA%\Lotus\google-oauth.json`.

Sem esse arquivo o painel mostra que o Google Drive não está ativado nesta versão; o backup em arquivo funciona normalmente.

## Testes

`tests/Lotus.Tests/BackupTests.cs`: ida e volta com reinício simulado, lista de arquivos, foto externa, recusa de arquivo inválido/formato novo/caminhos inseguros, cópias automáticas, leitura do JSON do Google e fluxo do Drive (upload, lista, download, desconectar) com servidor simulado. Validação real em 26/10/2026: salvar pela janela do Windows (22 arquivos, 355 KB) e restaurar com reinício (log "Backup restored", cópia `before-restore` criada).
