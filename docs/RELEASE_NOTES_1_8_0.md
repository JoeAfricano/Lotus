# Lotus 1.8.0 — Estúdio visual e comparação Antes/Depois

- Estúdio visual experimental com sugestões por uso e iluminação, imagens medievais e explicações curtas para cada controle.
- Prévia local de brilho, contraste, gamma, branco e intensidade das cores, com linha arrastável Antes/Depois. Arrastar não envia comandos ao monitor.
- Teste no monitor inteiro separado da prévia, com confirmação, backup e reversão de segurança. Saturação global disponível apenas quando o driver NVIDIA expõe controle compatível.
- Correções de bloqueio da interface, sessão interrompida ao minimizar e ícones sem contraste nos cards selecionados.
- Prévia com imagens reduzidas, buffers reutilizados e descarte de alterações intermediárias, sem trabalho contínuo quando está parada.

O ajuste é experimental e não substitui calibração com instrumento e perfil ICC específico. A prévia do brilho físico e de Digital Vibrance é aproximada; o resultado real deve ser conferido no teste do monitor. HDR/Advanced Color continuam bloqueados neste recurso. A documentação técnica e as fontes estão em LOTUS_1_8_COLOR_RESEARCH.md.

**Validação:** 904 testes aprovados em Debug/x64 e 904 em Release/x64; compilações sem avisos ou erros. Fluxo de prévia, divisória, saturação e retorno após minimizar conferidos na interface.

**Instalação:** use `Lotus-Setup-1.8.0.exe` para instalar ou atualizar. Para conferir o download, publique também `Lotus-Setup-1.8.0.exe.sha256`. Disponível para Windows x64; instalador sem assinatura digital. O instalador preserva os dados locais do jogador.

**Compatibilidade experimental:** a aplicação física depende do monitor e do driver. Quando não é possível confirmar a escrita, o Lotus informa falha e tenta reverter. Saturação global AMD e calibração HDR não estão disponíveis neste recurso.
