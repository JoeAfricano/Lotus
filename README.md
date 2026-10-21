# Lotus 2.0 — Fundação 0.1

Aplicativo nativo Windows em C# e WinUI 3. Esta entrega contém somente o shell, a navegação e a base técnica. Não coleta hardware, calcula score, executa benchmark, limpa arquivos ou altera configurações do Windows.

## Abrir e executar

Abra **C:\Projetos\Lotus\Lotus.slnx** no Visual Studio com suporte a WinUI. A pasta `Lotus/` existente foi preservada e não pertence a esta solução: nenhum código ou recurso visual dela foi copiado.

No PowerShell, a partir de `C:\Projetos\Lotus`:

```powershell
dotnet build src/Lotus.App/Lotus.App.csproj -p:Platform=x64
& .\src\Lotus.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Lotus.App.exe
```

Para executar os testes:

```powershell
dotnet test tests/Lotus.Tests/Lotus.Tests.csproj --logger trx
```

O primeiro restore precisa de internet. Requisitos desta entrega: Windows x64, SDK .NET 10.0.401, Windows SDK 10.0.26100.0. O Windows App SDK é incluído na saída; o runtime .NET 10 precisa estar instalado. Execute sem privilégios administrativos. Esta pasta de build não é um instalador.

## Estrutura

- `src/Lotus.App`: aplicação WinUI, composição por injeção de dependências, recursos XAML e navegação com Frame.
- `src/Lotus.Core`: catálogo imutável das dez seções e ShellViewModel com CommunityToolkit.Mvvm, sem dependência de WinUI.
- `src/Lotus.Infrastructure`: implementação de eventos com Serilog e propriedades estruturadas.
- `tests/Lotus.Tests`: testes unitários da navegação e integração de gravação JSON em arquivo temporário exclusivo.

As dez rotas usam uma página básica reutilizável, com título, descrição e próximo passo próprios. Quando cada módulo for desenvolvido, poderá receber sua página especializada. O code-behind limita-se à adaptação dos controles WinUI; a seleção pertence ao ViewModel.

SQLite está previsto para a etapa de persistência. Ainda não há preferências editáveis ou histórico de medições para armazenar, portanto esta entrega não cria um banco vazio nem inclui um serviço sem uso. Não há implementação de domínio de otimizações nesta fase.

## Interface

Tema escuro com rosê, símbolo vetorial original em XAML, cards reutilizáveis, navegação adaptativa e conteúdo com rolagem. Todas as funções futuras aparecem como **Planejado · Funcionalidade ainda não implementada**. Não existem botões que alegam realizar essas funções.

Os eventos de abertura, navegação e encerramento são registrados em `%LOCALAPPDATA%\Lotus\Logs\lotus-AAAAMMDD.jsonl`, com rotação diária, limite de 5 MB por arquivo e retenção de sete arquivos. A página Logs informa o caminho; o visualizador ainda não foi implementado. Exceções da UI são registradas sem serem ocultadas.

## Validação realizada em 21/10/2026

- Restore NuGet concluído.
- Build Debug x64: zero erros, zero avisos.
- Seis testes aprovados: início no Dashboard; dez rotas e notificações MVVM; entrada nula, desconhecida e repetida; integração Serilog/JSON em disco.
- Executável aberto e janela real inspecionada: Dashboard, menu com dez entradas, tema e textos renderizados.
- Uma observação posterior da janela confirmou conteúdo da seção Jogos. Os cliques automatizados adicionais foram interrompidos por mudança de posição/minimização da janela; isso não foi contado como aprovação de teste de interação.

### Checklist manual pendente

1. Clicar nas dez seções e conferir título e descrição, inclusive voltando ao Dashboard.
2. Redimensionar a janela: conferir menu compacto, rolagem e ausência de cortes.
3. Navegar com Tab, setas e Enter; avaliar leitor de tela, foco e escalas de 125%, 150% e 200%.
4. Fechar e reabrir; conferir os eventos JSON no diretório de logs.
5. Validar em outro computador Windows x64. ARM64, x86, Release e empacotamento ainda não foram validados.

Não avançar para monitoramento e alterações do Windows antes da aceitação desta fundação.

## Referência técnica

[Distribuição WinUI sem MSIX — Microsoft](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app). As versões NuGet estão fixadas nos projetos; o SDK estável é selecionado por `global.json`.

O checkpoint inicial usa a identidade técnica local `Lotus Development <lotus@localhost>` apenas no comando de commit, pois não havia identidade Git configurada. Nenhuma configuração global Git é alterada.
