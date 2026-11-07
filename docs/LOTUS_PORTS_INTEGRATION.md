# Integração com o Lotus Ports (lado do Lotus)

Protocolo completo: `docs/INTEGRACAO_LOTUS.md` no repositório do Lotus Ports. Os tipos do protocolo estão copiados em `src/Lotus.Infrastructure/PortsLink/Contracts` (iguais aos do Lotus Ports: mudar os dois juntos).

## O que o Lotus oferece ao Lotus Ports (pipe `lotus.integration.v1`)

| Método | O que responde |
|---|---|
| `Diagnostics.RunSystemChecks` | Driver de vídeo (nome, versão, data; aviso se tiver mais de um ano), Vulkan (quando o port usa), memória livre e espaço no disco do jogo. |
| `Dependencies.Check` | Visual C++ 2015–2022 x64 (registro), Vulkan (`vulkan-1.dll`), DirectX antigo (`d3dx9_43.dll`), .NET Desktop Runtime. Id desconhecido: "Não verificado". |
| `Journal.QueryRecent` | Ajustes que o Lotus aplicou ou desfez (diário do otimizador) e o driver de vídeo, se for recente. |
| `Session.Start` / `Session.End` | Registra a sessão: o Lotus vai para a bandeja enquanto o jogo está aberto e volta ao fim; a sessão entra no diário (com "Registrar minhas sessões" ligado, a partir de 2 minutos), com a chave `lotusGameKey`. Se o PresentMon do Lotus já estiver instalado, mede os quadros por 60 s, começando 20 s depois de o jogo abrir, sem pedir administrador. |
| `Session.GetReport` | FPS médio, 1% low e travadas só quando a medição foi válida; senão `measured: false` e nenhum número. Temperaturas: não medidas (nulas). |
| `System.GetHardwareProfile` | Processador, placa de vídeo e RAM. |

Não anunciados (ficam fora até existirem de verdade): `Dependencies.RequestInstall` (instalar dependências exige confirmação na interface do Lotus) e `Recommendations.ForGame`.

Segurança: o pipe só aceita o usuário atual e o processo cujo caminho é o `ExePath` que o Lotus Ports gravou em `HKCU\Software\LotusPorts\Integration`. Primeiro o `Handshake`; métodos desconhecidos recebem `-32601`. Nenhum método altera o PC.

## O que o Lotus pede ao Lotus Ports

Meus Jogos mostra "Seus jogos do Lotus Ports" (instalados e favoritos), com **Jogar** (`Ports.Launch`: o Lotus Ports abre o jogo com as próprias conferências, ou a página do jogo se algo precisar de confirmação) e **Abrir no Lotus Ports** (`Ports.Open`). O bloco só aparece quando o Lotus Ports está instalado e responde. O servidor do pipe é conferido pelo caminho registrado.

## Registro

Só o build Release, e não com `--dev-instance`, grava `HKCU\Software\Lotus\Integration` (`PipeName`, `ProtocolVersion`, `AppVersion`, `ExePath`) e apaga `PipeName` ao fechar. O build de desenvolvimento serve o pipe mas não mexe no registro, então o teste ponta a ponta com os dois apps é feito com os instaladores.

## Testes

`tests/Lotus.Tests/PortsIntegrationTests.cs`: pipe de verdade, handshake, versão incompatível, método desconhecido, parâmetros inválidos, conferências, dependências, diário, perfil, sessão sem medição e programa não autorizado.

## Teste ponta a ponta (06/11/2026)

- **Montagem:** build de desenvolvimento do Lotus (`--dev-instance`) e do Lotus Ports. As entradas de registro foram criadas à mão e apagadas no fim.
- **Resultado:**
  - conexão automática;
  - "Seus jogos do Lotus Ports" mostrou The Legend of Zelda: Majora's Mask;
  - "Jogar" abriu o jogo pelo Lotus Ports;
  - `Session.Start` foi registrado.
- **Medição:** o PresentMon saiu com código 6 sem administrador, então a sessão fica "não medida".

## Pendências

- Medir FPS com administrador (o PresentMon sem administrador saiu com código 6 neste PC). Exigiria pedir permissão do Windows a cada jogo; a preferência que chegou a ser criada para isso foi retirada até existir o fluxo completo.
- `Dependencies.RequestInstall` e `Recommendations.ForGame` continuam sem anúncio no handshake.