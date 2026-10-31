# Lotus — Google Drive (31/10/2026)

## 1. Análise inicial
- Solução: C#/.NET 10, WinUI 3 (App), Core (net10.0, MVVM com CommunityToolkit) e Infrastructure (net10.0). Os serviços são compostos no `MainWindow`.
- Backup existente: `LotusBackupArchive` (arquivo `.lotusbackup`, com o último save de cada jogo protegido), `BackupViewModel` e `BackupPanel` (Configurações › Backup).
- Já existia um cliente do Drive próprio (`GoogleDriveBackup`, HTTPS direto na Drive API v3, OAuth com PKCE) e o cofre de credencial `WindowsTokenVault` (Windows Credential Locker). Ele foi **reaproveitado e completado**, em vez de trocado.
- **Decisão sobre bibliotecas:** as bibliotecas oficiais `Google.Apis.Drive.v3`/`Google.Apis.Auth` não foram adicionadas.
  - A implementação existente já cumpre o fluxo pedido: Authorization Code + PKCE S256, loopback 127.0.0.1, `state` e renovação.
  - O `FileDataStore` padrão da biblioteca grava tokens em JSON sem criptografia, o que exigiria um armazenamento próprio de qualquer forma.
  - Evita duas dependências grandes para meia dúzia de chamadas.
  - Se preferir as bibliotecas oficiais, a interface `ICloudBackup` permite trocar a implementação sem mexer na interface.

## 2. Google Cloud (feito pelo Claude in Chrome e conferido na página depois de salvar)
| Item | Configuração |
|---|---|
| Conta | contato.bentojr@gmail.com |
| Projeto | **Lotus** — ID `lotus-513308`, sem organização, **sem faturamento** |
| API | Google Drive API — **Ativado** |
| Google Auth Platform | App "Lotus", e-mail de suporte e de contato contato.bentojr@gmail.com, público **Externo** |
| Escopo | somente `https://www.googleapis.com/auth/drive.file` (não confidencial) |
| Cliente OAuth | "Lotus Desktop", tipo **App para computador** |
| Branding | página inicial https://github.com/JoeAfricano/Lotus · privacidade https://github.com/JoeAfricano/Lotus/blob/main/PRIVACY.md · domínio autorizado github.com · sem logotipo |
| Publicação | **Em produção** (aprovada pelo usuário) |

- **Verificação e limites:**
  - O Google só exige verificação com logotipo, mais de 10 domínios ou escopos confidenciais/restritos. Nada disso se aplica.
  - O limite de 100 usuários (vida útil do projeto) vale só para escopos confidenciais/restritos não aprovados, então não afeta o `drive.file`.
  - O limite de usuários de teste (100) só existe em modo "Testando".
  - As cotas de uso da Drive API são as padrão do projeto e gratuitas.
  - Adicionar um logotipo depois exigirá enviar o app para verificação.
- **Credencial:**
  - Fica em `secrets\google-oauth.json` (ignorado pelo Git) e entra só no instalador.
  - Um app de computador é um cliente público: o "client secret" distribuído não é tratado como segredo. A proteção vem do PKCE e do redirecionamento local.
  - O cliente antigo (projeto "Default Gemini Project", outra conta) ficou guardado em `secrets\google-oauth.old-default-gemini.json` e não é mais usado. Ele pode ser excluído no console dessa outra conta.

## 3. Autenticação no Lotus
- **Login:** navegador padrão, PKCE S256, `state` aleatório comparado em tempo constante, redirecionamento `http://127.0.0.1:<porta livre>/`, tempo limite de 5 minutos.
- **Permissões:**
  - Escopo único `drive.file`.
  - A conta exibida vem de `about.user`, da própria Drive API, sem escopos extras.
  - Se o jogador desmarcar a permissão do Drive na tela do Google, a conexão não é guardada.
- **Credencial no PC:** só o token de renovação, no Windows Credential Locker (protegido pelo Windows para o usuário). Nada em texto puro e nada no repositório.
- **Token expirado ou revogado:**
  - Um 401 renova o acesso uma vez.
  - `invalid_grant` apaga a credencial, e a tela volta a mostrar "Conectar conta Google" com uma mensagem clara.
- **Desconectar:** revoga o token no Google e apaga a credencial e o cache de pastas.

## 4. Drive
- **Pastas:** a pasta `Lotus/Backups` é procurada (a mais antiga vence) e criada só se faltar.
  - Os IDs ficam em `%LOCALAPPDATA%\Lotus\drive-folders.json`, por conta.
  - Antes de reusar, o Lotus confere se a pasta ainda existe e não está na lixeira.
  - Um semáforo evita pastas duplicadas.
  - Outra instalação ou PC encontra as mesmas pastas, porque `drive.file` enxerga o que o app criou.
- **Operações:** enviar (multipart, refeito se houver erro temporário), listar, baixar e excluir. O Drive guarda as 5 cópias mais recentes.
- **Envio:** só quando o jogador clicar, toda semana ou todo mês.
  - A verificação acontece 2 minutos depois de abrir e a cada 6 horas.
  - Só envia se a última cópia no Drive for mais antiga que o período escolhido.
- **Erros:** `CloudException` com o tipo (login, rede, permissão, cota, Drive cheio, não encontrado).
  - Erros 429, 5xx e limites de taxa são refeitos com pausas crescentes.
  - Cada tipo tem uma mensagem em pt/en.

## Validação
- Builds Debug e Release: sem erros. Os avisos CA1416 em `GameDiaryTests.cs` já existiam.
- **451 testes aprovados** em Debug e em Release. Novos: `GoogleDriveTests` (9) com um Drive em memória:
  - escopo, PKCE e `state`;
  - login e e-mail; permissão desmarcada;
  - pastas criadas uma vez e reaproveitadas entre execuções e entre PCs; pasta na lixeira trocada; chamadas simultâneas sem duplicar;
  - envio, lista, download, exclusão e limite de 5;
  - novas tentativas em 429/503/limite de taxa; Drive cheio; permissão; cota; sem internet;
  - acesso revogado e desconexão com revogação;
  - mensagem clara no app; envio semanal/mensal.
- **Teste real** com a conta contato.bentojr@gmail.com, pela instância de desenvolvimento:
  - login pelo navegador concluído;
  - "Conectado como contato.bentojr@gmail.com";
  - dois envios de 14,9 MB;
  - no Drive real, **uma** pasta `Lotus` e **uma** `Backups`, com os arquivos;
  - lista carregada;
  - prévia de restauração baixada do Drive ("Saves de jogos: 2") e cancelada;
  - um backup excluído pelo Lotus. Ficou um backup de teste no seu Drive.
- **Funcionalidades preservadas:** backup em arquivo, cópias automáticas semanais locais, restauração, Backup de saves e Atualizações.

## Pendências
- O envio semanal/mensal foi testado só nos testes automatizados, não esperando uma semana real.
- A instância de desenvolvimento usa a mesma credencial do Windows que o Lotus instalado. Por isso o Lotus instalado também aparece conectado depois deste teste.
