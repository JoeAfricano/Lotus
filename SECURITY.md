# Segurança

## Versões cobertas
Correções de segurança saem sempre na versão mais recente publicada em [Releases](../../releases). O próprio Lotus avisa e instala as atualizações.

## Como relatar uma falha
Não abra uma issue pública. Envie os detalhes para **contato.bentojr@gmail.com** (ou use "Report a vulnerability" na aba Security do GitHub) com:
- a versão do Lotus e do Windows;
- o que acontece e como repetir;
- o impacto que você imagina.

Você recebe uma resposta assim que possível. Assim que a correção for publicada, o crédito é dado a você, se quiser.

## O que o Lotus protege
- Alterações no Windows só com confirmação, registradas e reversíveis; sem administrador.
- Instaladores baixados pelo atualizador são conferidos com o SHA-256 publicado na release e baixados só de github.com.
- Google Drive com a permissão mínima `drive.file`; credencial guardada pelo Windows e revogável ("Desconectar").
- Veja também a [Política de Privacidade](PRIVACY.md).