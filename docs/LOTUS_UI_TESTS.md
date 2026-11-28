# Testes de tela (FlaUI)

O Lotus não tinha testes automáticos das telas: a conferência era manual, por prints. O projeto
`tests/Lotus.UITests` usa o [FlaUI](https://github.com/FlaUI/FlaUI) (licença MIT), que controla o app pelo
**UI Automation do Windows**, o mesmo recurso usado por leitores de tela. Nada é injetado no Lotus.

## O que o teste faz

1. Abre o Lotus **já compilado** (Debug x64) como instância de desenvolvimento (`--dev-instance`), maximizado.
   Essa instância não instala atualização, não grava sessão de jogo, não mexe em "Iniciar com o Windows" e não mostra o guia de boas-vindas.
2. Confere que o menu lateral tem todas as seções (Início, Otimizar meu PC, Meus Jogos, Lotus Score, Ferramentas,
   Game list, Backup de saves, Meu diário, Configurações), cada uma com nome legível.
3. Abre cada seção pelo menu e, para cada uma, verifica que:
   - o Lotus continua aberto e a janela continua lá;
   - a tela mostra conteúdo (pelo menos 10 textos visíveis);
   - o Lotus não registrou erro de tela no próprio log ("UI error handled" ou "XAML/composition error") desde o início do teste.
4. Salva um print por seção em `artifacts\ui-tests\<data_hora>\` (`01-home.png`, `02-optimize.png`, …) e o relatório `ui-tests.trx`.
5. Fecha a instância de teste.

## O que ele nunca faz

- Não aperta botões de ação: nenhuma otimização, limpeza, restauração ou desinstalação é iniciada.
- Não compila o Lotus.App nem instala nada.
- Guarda uma cópia do `preferences.json` antes de abrir e devolve no fim (abrir telas pode lembrar a última aba ou o tamanho da janela).
  A cópia fica junto dos prints como `preferences.before.json`.

## Como rodar

Compile o Lotus antes (`dotnet build Lotus.slnx -c Debug -p:Platform=x64`), feche o Lotus Debug se estiver aberto e rode:

```
powershell -ExecutionPolicy Bypass -File tools\Run-UiTests.ps1
```

Para testar outro executável (por exemplo o build Release): `tools\Run-UiTests.ps1 -App <caminho do Lotus.App.exe>`.

Sem a variável `LOTUS_UI_TESTS=1` (o script define sozinho), todos os testes de tela aparecem como **ignorados**.
Assim `dotnet test` na solução continua rápido e nunca abre janelas de surpresa. A suíte normal (`tests/Lotus.Tests`) não muda.

## Limites

- A tela precisa estar livre durante o teste (~1 minuto): mexer no mouse ou cobrir a janela estraga os prints.
- Se houver outro Lotus Debug aberto, o teste para com aviso (só pode existir uma instância de desenvolvimento).
- "Tem conteúdo" e "não deu erro" não provam que a tela está bonita: os prints continuam servindo para olhar.
- Não há CI no GitHub: o teste roda no PC, com janela de verdade.

## Como crescer

Os elementos do Lotus já têm `AutomationProperties.AutomationId` (por exemplo `nav-home`, `diary-search`, `backup-status`).
Um teste novo encontra o elemento por esse id (`Window.FindFirstDescendant(cf => cf.ByAutomationId("..."))`) e confere o que aparece.
Bons próximos passos: abrir abas internas (Otimizar → Desempenho, Configurações → Atualizações), trocar o tema e fotografar claro/escuro, e conferir o idioma en-US.
