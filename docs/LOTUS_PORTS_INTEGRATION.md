# Integração com o Lotus Ports (lado do Lotus)

Protocolo completo: `docs/INTEGRACAO_LOTUS.md` no repositório do Lotus Ports. Os tipos do protocolo estão copiados em `src/Lotus.Infrastructure/PortsLink/Contracts` (iguais aos do Lotus Ports: mudar os dois juntos).

## O que o Lotus oferece ao Lotus Ports (pipe `lotus.integration.v1`)

| Método | O que responde |
|---|---|
| `Diagnostics.RunSystemChecks` | Driver de vídeo (nome, versão, data; aviso se tiver mais de um ano), Vulkan (quando o port usa), memória livre e espaço no disco do jogo. |
| `Dependencies.Check` | Visual C++ 2015–2022 x64 (registro), Vulkan (`vulkan-1.dll`), DirectX antigo (`d3dx9_43.dll`), .NET Desktop Runtime. Id desconhecido: "Não verificado". |
| `Journal.QueryRecent` | Ajustes que o Lotus aplicou ou desfez (diário do otimizador) e o driver de vídeo, se for recente. |
| `Session.Start` / `Session.End` | Registra a sessão: o Lotus vai para a bandeja enquanto o jogo está aberto e volta ao fim; a sessão entra no diário (com "Registrar minhas sessões" ligado, a partir de 2 minutos), com a chave `lotusGameKey`. Se o PresentMon do Lotus já estiver instalado, mede os quadros por 60 s, começando 20 s depois de o jogo abrir. Sem administrador por padrão; com "Medir FPS dos jogos do Lotus Ports" ligado em Configurações, o Windows pede administrador a cada partida (ou nunca, com a conta em "Usuários de log de desempenho"). |
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

- `Dependencies.RequestInstall` e `Recommendations.ForGame` continuam sem anúncio no handshake.
## Medir FPS dos jogos do Lotus Ports sem o aviso do Windows

A medição usa o PresentMon, que precisa de administrador **ou** de estar no grupo "Usuários de log de desempenho" do Windows. Com a sua conta no grupo, o Lotus mede sem pedir nada e a opção "Medir FPS dos jogos do Lotus Ports" deixa de abrir o aviso do Windows.

É uma mudança de segurança do Windows, feita por você, uma vez:

1. Abra o PowerShell **como administrador** (menu Iniciar → digite PowerShell → "Executar como administrador").
2. Rode: `Add-LocalGroupMember -SID S-1-5-32-559 -Member $env:USERNAME` (o SID funciona em Windows de qualquer idioma).
3. Saia da conta do Windows e entre de novo (o grupo só vale numa nova sessão).

Para desfazer: `Remove-LocalGroupMember -SID S-1-5-32-559 -Member $env:USERNAME`, como administrador.

## Convite e instalação (1.3.0)

- Cartão "Lotus Ports" no Início (`SectionPage.Ports.cs`): sem o Lotus Ports instalado, abre a tela que explica o que é e oferece **Instalar**; instalado, abre o programa.
- `PortsOffer`: detecta a instalação (entrada `ExePath` do Lotus Ports ou `%LOCALAPPDATA%\Programs\Lotus Ports\app`), lê a release mais recente de `JoeAfricano/LotusPorts` pelo mesmo leitor do atualizador (só endereços do GitHub), baixa o `LotusPorts-Setup-*.exe`, confere o SHA-256 publicado (sem hash ou diferente: nada roda) e abre o instalador com o assistente dele.
- Meus Jogos: com o Lotus Ports instalado e fechado, a faixa mostra um aviso e "Abrir no Lotus Ports".
- Teste da versão de desenvolvimento: `LOTUS_PRETEND_NO_PORTS=1` (só com `--dev-instance`) mostra o convite mesmo com o Lotus Ports instalado. Para a faixa, a entrada `HKCU\Software\Lotus\Integration` precisa de `PipeName` e `ExePath` da versão de teste (o Lotus Ports recusa sem isso).
