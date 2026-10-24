# Diretrizes do Codex — Lotus

- Responder em português brasileiro, preservando nomes técnicos originais. Usar sempre o nome Lotus.
- Inspecionar primeiro somente arquivos relacionados à tarefa; ampliar a investigação quando houver necessidade concreta.
- Reutilizar arquitetura, documentação, serviços, ferramentas locais e testes existentes. Priorizar execução local e alterações mínimas, corretas e verificáveis.
- Preservar funcionalidades, histórico Git e alterações locais existentes. Não ampliar o escopo nem implementar funcionalidades futuras sem autorização.
- Aproveitar evidências ainda válidas; não repetir pesquisas, auditorias, builds ou testes sem motivo. Uma alteração que afete sua validade exige nova verificação.
- Durante o desenvolvimento, executar testes direcionados aos componentes modificados. Ao concluir incrementos relevantes, executar validação completa em Debug/x64 e Release/x64, proporcional ao risco e aos critérios da tarefa.
- Manter verificações necessárias de segurança, persistência e integração, mesmo quando custosas. Nunca declarar teste aprovado sem execução comprovada, nem apresentar dados simulados como medições reais.
- Diante de falhas persistentes, interromper repetições improdutivas, registrar a mensagem original e a operação bloqueada e propor alternativa verificável.
- Preservar segurança, integridade dos dados, precisão estatística e reversibilidade. Respeitar permissões; não realizar reparos administrativos ou mudanças globais sem autorização aplicável.
- Documentar decisões relevantes sem criar documentação extensa para alterações triviais. Entregar respostas concisas com alterações, testes, resultado e pendências.
- Para benchmark, jogos e diagnóstico relacionados ao ResetExplode, consultar primeiro `docs/LOTUS_RESETEXPLODE_REFERENCE.md`. Reaproveitar os aprendizados e casos registrados, sem tratar resultados externos como testes aprovados do Lotus; respeitar licença e critérios de segurança.

## Etapa 04 — Benchmark

- Concentrar a primeira entrega na prova funcional de captura real com PresentMon, reutilizando o contexto técnico e o código existentes.
- Inspecionar apenas captura, persistência e interface de Desempenho, expandindo quando uma dependência concreta exigir. Consultar documentação oficial para dúvidas técnicas específicas.
- Priorizar a menor integração funcional verificável, avaliando custo-benefício de tempo, eficiência e confiabilidade. Não antecipar recursos secundários.
- Se o sandbox impedir a captura real, registrar o bloqueio e preservar o código funcional, sem repetir tentativas indefinidamente.
- Manter marca e fundo ambiental em standby. Não implementar otimizações, RTSS, Afterburner ou perfis de jogos nesta etapa.
- Limitar o relatório final da etapa a 12 linhas, incluindo evidências, testes e limitações.
