# Lotus — Roadmap verificável

Revisão: 21/10/2026. Somente planejamento. Aguardar instrução de implementação após esta entrega. A preferência por alternativas gratuitas e código próprio não autoriza antecipar todos os módulos.

Pré-requisito Etapa 00: auditoria de fundação aprovada; [resultados e pendências](LOTUS_STAGE00_AUDIT.md). Isso não conclui todos os gates de localização, DPI, consumo e acessibilidade da etapa 1 abaixo. Aguardar instrução para o próximo incremento; nenhum módulo novo implementado na auditoria.

## Etapas e gates

| Ordem | Entrega limitada | Dependência | Critério de aceitação |
| --- | --- | --- | --- |
| 1. Fundação e estabilidade | Preservar shell; robustez de início/fechamento, layout, idiomas pt-BR/en-US | Solução atual | Build Debug/x64 e Release/x64; testes existentes; dez rotas; DPI/teclado; zero instâncias de teste residuais; recursos nos dois idiomas; baseline de custo |
| 2. Monitoramento real | Primeira fatia: CPU total e RAM somente leitura; depois disco/processos e GPU/sensores por capacidade | Gate 1 e contratos de amostra | Fonte, unidade, timestamp e indisponibilidade visíveis; primeira amostra CPU tratada como aquecimento; comparação com ferramenta independente; cancelamento e custo medidos |
| 3. Diagnóstico inteligente | Regras determinísticas explicáveis, começando por pressão de recursos | Telemetria validada | Cada achado tem janela de dados, limiar versionado, confiança e contraexemplos; não diagnosticar gargalo de jogo sem frames/contexto |
| 4. Score e histórico | SQLite, migrações, histórico e score de estado | Gates 2–3 | Persistência após reinício; falha/recuperação testada; score com cobertura, regras e pesos publicados; sem equivalência com FPS |
| 5. RTSS Bridge | Detectar/abrir/ler; depois overlay; perfil FPS separado | Licença/ABI/capacidades verificadas, journal persistente | Ausente/fechado/incompatível tratados; OSD próprio sem apagar terceiros; snapshot e restauração de perfil; escrita exige confirmação |
| 6. Afterburner Bridge | Detectar/abrir/telemetria MAHM | Gate de licença, leitura binária validada | Leituras iguais à fonte no mesmo intervalo; dados vencidos sinalizados; nenhuma chamada automática de controle de hardware |
| 7. Benchmark real | Captura PresentMon por processo, análise, comparação e Frame Pacing | Protocolo de medição e armazenamento | Arquivos brutos preservados; perda de eventos tratada; fórmulas testadas; DX11/DX12/Vulkan quando suportados; capturas repetidas; metadados completos |
| 8. Otimizações reversíveis | Uma ação por vez com evidência | Gates 4 e 7; autorização por plano | Backup antes de escrever; verificação posterior; crash recovery; comparação com incerteza; reversão confirmada |
| 9. Perfis de jogos | Biblioteca autorizada, executáveis, preferências e atalhos | Persistência, bridges e rollback | Identidade estável além do nome; edição e exclusão de perfil não alteram o jogo; mudança externa detectada; importações validadas |
| 10. Clean e Health | Inventário seguro de temporários e diagnóstico de integridade | Plano/consentimento/journal | Prévia exata, revalidação do arquivo, proteção de links/reparse points e dados de jogos; nenhuma exclusão automática de shader cache; sem promessa de FPS |
| 11. Refinamento/distribuição | Instalador, assinatura, atualização, notices e suporte | Gates anteriores | Máquina limpa, instalação/desinstalação, migração, recuperação, consumo e matriz de compatibilidade aprovados; licenças de cada artefato verificadas |

Uma função pode ficar indisponível se requisitos não forem atendidos. A aprovação de detecção não aprova instalação, escrita de perfil, ajuste de hardware ou aplicação de otimizações. RTSS/Afterburner são opcionais: o núcleo deverá iniciar sem eles. Escritas experimentais de perfil na etapa 5 não serão anunciadas como ganho comprovado antes da etapa 7.

## Próximo incremento recomendado: etapa 1

1. Confirmar o ponto de partida e preservar as alterações locais existentes dos `.csproj`.
2. Migrar textos para pt-BR/en-US e garantir que a marca exibida seja apenas Lotus. Manter códigos de diagnóstico e logs invariantes.
3. Exercitar todas as rotas, foco, janela mínima, menu compacto, restauração/maximização e DPI 100/125/150/200%.
4. Testar inicialização com diretório de logs indisponível, encerramento e múltiplas instâncias; corrigir somente falhas reproduzidas.
5. Medir inicialização fria/quente, CPU e memória em repouso; guardar condições e amostras. Metas propostas estão no documento de validação.
6. Compilar Debug e Release, executar testes e fechar instâncias de validação antes de recompilar. Registrar checkpoint e relatório objetivo.

Não inclui monitoramento nem SQLite nessa primeira fatia. O usuário não precisa escrever código: o Codex executará edição, build e testes após autorização da etapa. Solicitar interação apenas para consentimentos de sistema ou avaliação subjetiva que não possa ser automatizada.

## Política de avanço

Para cada etapa: problema → escopo → implementação → build → teste → evidência → limitações → checkpoint Git. Medições que falharem não viram zero nem sucesso. Falha de licença bloqueia apenas o adaptador/artefato afetado; escolher alternativa gratuita ou implementação própria com escopo mensurável. Nenhuma etapa tem promessa antecipada de ganho de FPS.
