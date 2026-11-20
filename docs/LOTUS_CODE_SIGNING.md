# Lotus — Assinatura de código (SignPath Foundation)

Objetivo: o instalador e o `Lotus.App.exe` saírem assinados, para o Windows mostrar um publicador conhecido em vez do aviso "Editor desconhecido" do SmartScreen. A SignPath Foundation dá o certificado de graça para projetos de código aberto; a assinatura é feita a partir do GitHub Actions, nunca no PC.

## O que já está no repositório

- `.github/workflows/build.yml`: em cada pull request compila e roda os testes. Na execução manual (Actions › Build › Run workflow) gera o instalador; com a opção **sign** marcada, envia os arquivos para a SignPath, espera a aprovação e monta o instalador com os arquivos assinados, assina o instalador e só então calcula o `.sha256`.
- `CODE_SIGNING_POLICY.md`: a política pública que a SignPath Foundation exige (o que é assinado, quem aprova, privacidade).
- `tools\Build-Installer.ps1 -SkipPublish`: monta o instalador a partir da pasta do app já assinada.

## O que só o José pode fazer (uma vez)

1. **Pedir a inscrição** em https://signpath.org (formulário "Apply"). Dados: repositório `https://github.com/JoeAfricano/Lotus`, licença MIT, link da política (`CODE_SIGNING_POLICY.md`) e da privacidade (`PRIVACY.md`). A fundação analisa o projeto; pode levar alguns dias ou semanas.
2. **Ligar a verificação em duas etapas** no GitHub (se ainda não estiver) e na conta SignPath.
3. Depois de aprovado, na SignPath:
   - instalar o **SignPath GitHub App** no repositório Lotus;
   - adicionar o *trusted build system* **GitHub.com** e ligar ao projeto;
   - conferir os nomes (slugs): projeto `Lotus`, política `release-signing` (se forem outros, cadastrar como variáveis abaixo);
   - criar duas *artifact configurations* com os XMLs abaixo, chamadas `app` e `installer`;
   - criar um usuário de API (CI) com permissão de *submitter* e copiar o token.
4. No GitHub (Settings › Secrets and variables › Actions):
   - **Secret** `SIGNPATH_API_TOKEN` = token da SignPath;
   - **Variable** `SIGNPATH_ORGANIZATION_ID` = id da organização na SignPath;
   - (opcional) variables `SIGNPATH_PROJECT_SLUG` e `SIGNPATH_POLICY_SLUG` se diferentes de `Lotus` e `release-signing`;
   - (opcional) secret `GOOGLE_OAUTH_JSON` = conteúdo de `secrets\google-oauth.json`, para o instalador do GitHub ter o backup no Google Drive. Sem ele, o instalador sai só com backup em arquivo.

### Artifact configuration `app`

```xml
<?xml version="1.0" encoding="utf-8"?>
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <pe-file path="Lotus.App.exe"><authenticode-sign/></pe-file>
    <pe-file-set>
      <include path="Lotus.*.dll"/>
      <for-each><authenticode-sign/></for-each>
    </pe-file-set>
  </zip-file>
</artifact-configuration>
```

### Artifact configuration `installer`

```xml
<?xml version="1.0" encoding="utf-8"?>
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <pe-file-set>
      <include path="Lotus-Setup-*.exe"/>
      <for-each><authenticode-sign/></for-each>
    </pe-file-set>
  </zip-file>
</artifact-configuration>
```

## Como publicar uma versão assinada

1. GitHub › Actions › **Build** › Run workflow (branch `main`), marcar **sign**.
2. Na SignPath, aprovar os dois pedidos (app e instalador) quando chegarem.
3. Baixar o artefato `Lotus-Setup-signed` (instalador + `.sha256`) e anexar na release, como hoje.

## Limites

- O desinstalador gerado pelo Inno Setup (`unins000.exe`) não é assinado nesta primeira etapa.
- O SmartScreen também usa reputação: mesmo assinado, as primeiras versões ainda podem mostrar aviso até o certificado ganhar histórico.
- Não verificado: nenhum pedido de assinatura foi feito ainda (depende da aprovação da SignPath Foundation).
