# Lotus — Decisões técnicas

Revisão: 21/10/2026. “Adotado” descreve o código atual; “Diretriz” orienta trabalho futuro; “Proposto” exige validação antes de implementação.

| ID | Estado | Decisão e consequência |
| --- | --- | --- |
| ADR-001 | Adotado | Preservar solução e UI WinUI 3/.NET 10. x64 no aplicativo, Any CPU nas bibliotecas pela solução. Sem migração para web. |
| ADR-002 | Diretriz | Marca pública apenas **Lotus**. Português brasileiro principal, inglês americano segundo idioma. Novos textos devem ser localizáveis; migrar literais atuais na etapa 1. |
| ADR-003 | Adotado | Core, Infrastructure, App e Tests são suficientes hoje. Não criar projetos vazios para cada nome de módulo. |
| ADR-004 | Diretriz | Telemetria separada de UI e diagnóstico. Ausência, atraso, permissão e erro têm estados próprios; zero só representa leitura real igual a zero. |
| ADR-005 | Proposto | SQLite via Microsoft.Data.Sqlite para preferências, sessões e journal. Sem dependência adicionada agora; migração e recuperação devem anteceder ações externas persistentes. |
| ADR-006 | Diretriz | Aplicativo asInvoker. Elevação só para operação necessária, explícita e delimitada; não elevar toda a aplicação por sensor opcional. |
| ADR-007 | Proposto | Benchmark inicialmente via PresentMon separado; API/serviço após prova de necessidade e compatibilidade. Não confundir telemetria de hardware e eventos de frames. |
| ADR-008 | Diretriz | Bridges internos opcionais com capabilities e versões. Ferramenta ausente não impede inicialização; sem automação da UI externa. |
| ADR-009 | Diretriz | Preferir opções gratuitas e código original. Uso gratuito não é autorização para copiar/redistribuir. Licença e dependências analisadas por artefato. |
| ADR-010 | Diretriz | Otimização exige evidência e fluxo completo de confirmação, backup, aplicação, verificação, medição e reversão. Sem promessa universal. |
| ADR-011 | Diretriz | Estatística com fórmulas versionadas e capturas originais. “1% low” deve indicar definição; incerteza é parte do resultado. |
| ADR-012 | Diretriz | Score descreve estado e oportunidades; jamais equivale a FPS ou ganho percentual. Desconhecido não vale zero nem nota máxima. |
| ADR-013 | Adotado/parcial | Logs JSONL por usuário, rotação diária e por 5 MB, sete arquivos retidos. Falhas de gravação/concorrência e redaction ainda não validadas. |
| ADR-014 | Diretriz | Dados locais por padrão; exportação exige ação explícita. Caminhos, processos e traces podem revelar dados pessoais; minimizar coleta e permitir retenção configurável. |
| ADR-015 | Diretriz | Não implementar limpeza forçada de RAM, encerramento indiscriminado de serviços, tweaks genéricos, exclusão automática de shaders ou desativação de proteções. HAGS, pagefile, timers, energia e prioridades só mediante hipótese específica e ensaio. |

## Score: proposta inicial

Pesos solicitados: sistema 20%, processos em segundo plano 20%, inicialização 15%, energia 15%, memória 10%, armazenamento 10%, drivers/GPU 10%. Não há algoritmo implementado. Cada componente precisará de regras objetivas, fonte, faixa, validade temporal e justificativa. Quantidade de processos/itens de inicialização não será penalidade automática; “alto desempenho” não será nota máxima automática.

Exibir cobertura de dados e critérios cumpridos. Proposta conservadora: sem nota global enquanto componentes necessários não tiverem evidência suficiente; manter componentes disponíveis individualmente, sem renormalização silenciosa. Mudança de versão da regra impede comparação direta de notas sem recálculo rastreável.

## Achados e dívida técnica priorizada

| Prioridade | Achado real | Próxima ação |
| --- | --- | --- |
| Alta | Não existe banco, coleta ou motor de diagnóstico | Evitar tratar cards e seções como funcionalidades prontas |
| Alta | Testes limitados a navegação e logging | Estender por comportamento ao implementar serviços; não inventar cobertura de UI |
| Alta | Termos completos de RTSS/Afterburner SDK não estabelecidos | Validar direitos antes de copiar exemplos/redistribuir; bridge opcional e alternativa própria |
| Média | Textos de UI e catálogo em português literal; marca antiga no título do README | Migrar recursos pt-BR/en-US e corrigir nomenclatura documental |
| Média | Uma SectionPage instancia conteúdo de Dashboard também nas outras rotas e o oculta | Medir custo antes de separar páginas/usar carregamento adiado |
| Média | Logging configurado no início sem política explícita de falha de armazenamento | Ensaiar indisponibilidade; projetar fallback legível sem ocultar exceção |
| Média | Janela inicial fixa 1280x820, arte vetorial com largura fixa, VSM ainda pouco exercitado | Validar janelas pequenas, DPI, foco e alto contraste |
| Média | Sem política explícita de multi-instância | Testar concorrência de logs antes de escolher single-instance |
| Média | Versões diretas fixas, sem lockfile/SBOM de distribuição | Introduzir restore reproduzível e inventário na etapa apropriada |

Não se afirma que todos esses riscos já causem falhas. São limites constatados da implementação e da evidência disponível.

## Relatório desta entrega

- Novos arquivos: os cinco `docs/LOTUS_*.md` solicitados. Ajuste documental do título do README para a marca Lotus.
- Nenhuma alteração funcional ou de pacote; solução e arquivos preservados.
- Restore Debug/x64 aprovado; build com zero avisos e erros; seis testes aprovados. Evidência detalhada em [validação](LOTUS_PERFORMANCE_VALIDATION.md).
- Nenhuma nova execução visual nesta auditoria; inspeções anteriores constam em [direção visual](VISUAL-DIRECTION.md). Nenhuma captura de jogo, otimização ou mudança de hardware realizada.
- SDKs locais de RTSS 7.3.5.28314 e Afterburner 4.6.6.16757 inspecionados somente em leitura. Presença não significa compatibilidade aprovada.
- Há alterações preexistentes em `Lotus.Core.csproj`, `Lotus.Infrastructure.csproj` e `Lotus.Tests.csproj`, além do backup da solução; ficam fora deste checkpoint documental.

As bases e pendências de licença estão em [integrações](LOTUS_EXTERNAL_INTEGRATIONS.md). O próximo passo é a etapa 1 do [roadmap](LOTUS_ROADMAP.md), mediante instrução do usuário. Não iniciar implementação dos novos módulos nesta entrega.
