# Lotus

Game HUB e otimizador inteligente para Windows Gaming em C#/.NET. Detecta o hardware, diagnostica gargalos, cria perfis de jogos e aplica ajustes reversíveis, com benchmarks de antes e depois. Sem placebo, sem FPS inventado.

**Versão atual: 0.3.0-alpha "Lotus New"**. Baixe o instalador em [Releases](../../releases).

## O que o Lotus faz

- **Otimizar meu PC.** Perfis Jogo, Diário e Personalizado. Mostra cada mudança antes de aplicar e verifica o resultado. Tudo pode ser desfeito e nada muda no Windows sem confirmação.
- **Meus Jogos.** Biblioteca de Steam, Epic, GOG e outras lojas, com Jogar direto da capa, perfil por jogo e overlay de FPS (RTSS).
- **Lotus Score.** Teste de CPU e GPU com notas separadas, estabilidade entre passadas e sensores. Também mostra o FPS estimado por jogo e resolução, inclusive buscando qualquer jogo pelos requisitos da Steam, sempre com as fontes. E aponta o gargalo.
- **Desempenho.** CPU, RAM, GPU e VRAM ao vivo. Mede o FPS real com PresentMon e compara sessões.
- **Por que está travando?** (Ctrl + Shift + L). Mede com o jogo aberto e diz o que está causando a travada.
- **Game list.** Listas da Steam (em alta, mais bem avaliados, mais vendidos, lançamentos, em breve), busca e sorteio de jogos por gênero e dificuldade.
- **Meu diário.** Notas, análises, horas, medalhas e 54 conquistas, além das conquistas da Steam lidas localmente, sem login.
- **Ferramentas.** Liberar memória, limpar temporários, testar internet e DNS, ver a saúde do disco, o driver de vídeo e desinstalar programas.
- **Backup.** Arquivo `.lotusbackup` local e cópia opcional no Google Drive. A opção "Restaurar o Lotus" volta ao início sem apagar nada.
- Temas Escuro, Claro (porcelana rosé), Sistema e Grafite, com cores de destaque. Português e inglês.

## Instalar

Baixe `Lotus-New-Setup-<versão>.exe` em [Releases](../../releases) e execute. A instalação é por usuário, sem administrador, em `%LOCALAPPDATA%\Programs\Lotus`. Para atualizar, rode o instalador novo por cima, sem desinstalar. Os dados do jogador ficam em `%LOCALAPPDATA%\Lotus` e nunca são apagados pelo instalador.

Requer Windows 10 2004 (19041) ou mais novo, x64.

## Compilar

Requisitos: Windows x64, SDK .NET 10 e Windows SDK 10.0.26100.0.

```powershell
dotnet restore Lotus.slnx -p:Platform=x64
dotnet build Lotus.slnx -c Debug -p:Platform=x64 --no-restore
& .\src\Lotus.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Lotus.App.exe
```

Testes:

```powershell
dotnet test tests/Lotus.Tests -c Debug -p:Platform=x64
```

Instalador (Inno Setup 6.7+):

```powershell
.\tools\Build-Installer.ps1 -Name "Lotus New"
```

O resultado fica em `artifacts\dist\`. O backup no Google Drive só fica ativo quando existe `secrets\google-oauth.json` (um cliente OAuth "Desktop app"). Essa pasta fica fora do git.

## Estrutura

- `src/Lotus.App`: interface WinUI 3 (shell, telas, temas, animações).
- `src/Lotus.Core`: ViewModels, regras e cálculos testáveis (perfis, Score, diário, conquistas, catálogo).
- `src/Lotus.Infrastructure`: Windows e serviços (planos de energia, journal SQLite, telemetria, Steam, PresentMon, backup).
- `tests/Lotus.Tests`: testes xUnit.
- `tools/`: instalador e utilitários.
- `docs/`: decisões técnicas e relatórios ([arquitetura](docs/LOTUS_ARCHITECTURE.md), [otimizador](docs/LOTUS_OPTIMIZER.md), [jogos](docs/LOTUS_GAMES.md), [backup](docs/LOTUS_BACKUP.md)).

## Princípios

- Nenhuma alteração no Windows sem confirmação explícita. Cada mudança é registrada e pode ser desfeita.
- Sem permissões de administrador (`asInvoker`).
- Sem números inventados. Estimativas aparecem como estimativas, com as fontes.
