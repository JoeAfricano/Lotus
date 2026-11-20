# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

Only Lotus' own files, built from this repository by the [Build workflow](.github/workflows/build.yml) on GitHub-hosted runners:

- `Lotus.App.exe` and the `Lotus.*.dll` libraries inside the app folder;
- the installer `Lotus-Setup-<version>.exe`.

Third-party files shipped with the app (.NET runtime, Windows App SDK and NuGet libraries) keep their publishers' own signatures and are not re-signed. Tools Lotus can download on demand (PresentMon) or open when installed by the player (RivaTuner, SCSKiller) are not signed by this project.

## Team roles

- **Committers and reviewers:** [José Bento (@JoeAfricano)](https://github.com/JoeAfricano). Changes from other contributors are merged only after his review.
- **Approvers:** [José Bento (@JoeAfricano)](https://github.com/JoeAfricano). Each signing request is approved manually in SignPath before a release.

All team members use multi-factor authentication on GitHub and SignPath.

## Privacy

Lotus does not send data to its developer and has no telemetry. It connects to other services only for the features described in the [privacy policy](PRIVACY.md) (for example, update checks on GitHub, Steam data and the optional Google Drive backup).

## Release integrity

Each release on GitHub has the installer and a `.sha256` file computed after signing. The app's updater checks the downloaded installer against it before running it.

---

# Política de assinatura de código (pt-BR)

Assinatura de código gratuita fornecida pela [SignPath.io](https://about.signpath.io), certificado da [SignPath Foundation](https://signpath.org).

- **O que é assinado:** só os arquivos do próprio Lotus (`Lotus.App.exe`, `Lotus.*.dll` e o instalador `Lotus-Setup-<versão>.exe`), compilados deste repositório pelo GitHub Actions. Arquivos de terceiros mantêm a assinatura original de quem os publica.
- **Papéis:** José Bento (@JoeAfricano) revisa, aprova o código e aprova cada pedido de assinatura. Todos usam autenticação em duas etapas.
- **Privacidade:** o Lotus não envia dados ao desenvolvedor; veja a [Política de Privacidade](PRIVACY.md).
