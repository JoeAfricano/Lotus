# Contribuindo com o Lotus

Obrigado pelo interesse! O Lotus é feito para o jogador de PC que quer jogar mais e configurar menos.

## Reportar um problema
1. Veja se já existe uma [issue](../../issues) parecida.
2. Abra uma nova com o modelo **Problema**, dizendo a versão (Configurações › Atualizações), o Windows e os passos para repetir.
3. Se puder, anexe o log de `%LOCALAPPDATA%\Lotus\Logs` (ele não contém senhas nem anotações pessoais).

## Sugerir uma ideia
Use o modelo **Ideia** e conte qual problema do dia a dia ela resolve.

## Enviar código
- Requisitos e comandos de build estão no [README](README.md#compilar).
- Siga a arquitetura: regras e cálculos em `Lotus.Core` (testáveis, sem WinUI), Windows e serviços em `Lotus.Infrastructure`, telas em `Lotus.App`.
- Todo texto da interface fica nos arquivos de idioma (`Strings.resx` e `Strings.en-US.resx`), em português e inglês.
- Inclua testes para regras novas e rode `dotnet test tests/Lotus.Tests -c Debug -p:Platform=x64` antes de abrir o pull request.
- Princípios que não mudam: nada altera o Windows sem confirmação e tudo pode ser desfeito; sem administrador; sem números inventados; o app precisa continuar leve.