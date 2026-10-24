# Lotus — Roadmap verificável

## Prioridade atual — 23/10/2026

Por solicitação do usuário, o refinamento da marca animada está em standby. O checkpoint c2ca0ce permanece como base; há um ajuste local posterior de duração/ritmo em BrandPulse.cs, preservado sem aprovação visual. Compilações posteriores Debug/Release passaram com esse ajuste local, mas isso não homologa o ritmo da marca. As pendências visuais anteriores continuam registradas, sem bloquear a escolha do próximo incremento pelo usuário.

CPU/RAM e GPU NVIDIA já possuem implementação real e 62 testes aprovados por configuração no checkpoint anterior. Escopo posteriormente autorizado e implementado: primeira regra explicável na página Análise sobre disponibilidade de RAM ao longo do tempo, reaproveitando o coletor sob demanda. Não concluir gargalo ou ganho em jogos apenas pelo percentual de uso. Diagnóstico inicial implementado e validado com 77 testes por configuração; dados e reinício da observação confirmados pelo usuário. Não representa autorização para aplicar otimizações. Ver [diagnóstico de RAM](LOTUS_RAM_DIAGNOSTICS.md).

Revisão: 21/10/2026. Roadmap dos módulos futuros, com fundação implementada na Etapa 01. A preferência por alternativas gratuitas e código próprio não autoriza antecipar todos os módulos.

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

## Situação após a Etapa 02

CPU e RAM somente leitura implementados, com fonte identificada, aquecimento de CPU, histórico de 60 s, pausa por demanda e testes de falha/cancelamento. [Validação local e custo medido](LOTUS_STAGE02_VALIDATION.md). Não há GPU, sensores, frequência efetiva, processos externos, diagnóstico ou benchmark implementado.

Próxima fatia recomendada após instrução: diagnóstico inicial de memória com critérios, cobertura e evidências usando os snapshots atuais, sem concluir gargalo de jogo ou prometer ganho de FPS. Antes de mais fontes, ampliar a homologação de hardware/DPI e as medições prolongadas de custo/estabilidade. A implementação não antecipa autorização de módulos seguintes.

## Política de avanço

Para cada etapa: problema → escopo → implementação → build → teste → evidência → limitações → checkpoint Git. Medições que falharem não viram zero nem sucesso. Falha de licença bloqueia apenas o adaptador/artefato afetado; escolher alternativa gratuita ou implementação própria com escopo mensurável. Nenhuma etapa tem promessa antecipada de ganho de FPS.


## Etapa 03 autorizada — GPU e sensores (23/10/2026)

A numeração das entregas atuais separa Etapa 02 (CPU/RAM) de Etapa 03 (GPU/sensores). A ordem conceitual da tabela acima não autoriza implementar diagnóstico nesta entrega.

NVIDIA NVML, VRAM, clocks, temperatura GPU, potência, ventoinha percentual e eventos de clock implementados com estados de capacidade. Ver [contrato](LOTUS_GPU_TELEMETRY.md) e [validação](LOTUS_STAGE03_VALIDATION.md). Conclusão depende dos gates registrados no relatório. Futuro diagnóstico/RTSS/benchmark/otimizações continuam pendentes e exigem nova instrução.


## Complemento documental — catálogo e análise competitiva (23/10/2026)

[Relatório priorizado e fontes](LOTUS_OPTIMIZATION_CATALOG_PLAN.md). A comparação com Carpi/Toptimizer e os READMEs fornecidos não comprova ganhos dos concorrentes, não autoriza copiar código nem executar ajustes. Categorias já existentes ficam nas etapas atuais; não criar uma nova etapa por ferramenta ou quantidade de tweaks.

| Incremento ainda não especificado nesse nível | Onde entra | Gate adicional |
| --- | --- | --- |
| Elegibilidade por proprietário/política e custo atribuível de inicialização | Diagnóstico, refinando inicialização já prevista | Identificar contexto usuário/máquina, GPO/MDM, origem e impacto observado; ausência de domínio não libera política. Sem desativação por contagem de itens. |
| Diagnóstico de rede antes de ajustes | Diagnóstico inteligente, antes das otimizações reversíveis | Interface, erros, perda/jitter e DNS separados; destino e custo do ensaio explícitos; respeitar VPN/NRPT/IPv6; não prometer ping menor pela troca de DNS. |
| Lotus Optimization Catalog declarativo, com conflitos e compensações | Score/histórico/journal e futuras otimizações reversíveis | Manifesto tipado/versionado, handlers permitidos, plano com hash/consentimento, revalidação de estado, dry-run, recuperação após crash, rollback testado e detecção de edição externa. Reutiliza a persistência já planejada. |
| Viabilidade de recuperação antes de debloat individual | Perfis/manutenção, sem antecipar remoções | Dependências/essenciais protegidos, recuperação de pacote e limites de dados/licença conhecidos; exclusão não integra lote que promete reversão exata. |

Energia, CPU/GPU, Clean/TRIM e integridade foram avaliados como refinamentos dos itens existentes, não acréscimos duplicados. EcoQoS/prioridade de app opcional fica como hipótese experimental, sem compromisso de implementação. Timer/parking/serviços/limpeza forçada de RAM em lote permanecem excluídos. Nenhuma implementação ou comando de otimização foi executado nessa análise documental.


## UX e soluções após análise — 23/10/2026

Redesign autorizado: cinco destinos (Início, Otimizar, Meus Jogos, Desempenho, Configurações), marca preservada, textos revisados e ações sem duplicação. Análise explícita de RAM com tempo real/progresso e conclusão em Otimizar > Resultado. Explicações em Configurações > FAQ. [Implementação, evidência manual e limites](LOTUS_UX_REDESIGN.md).

Requisito guardado para os próximos módulos: apresentar soluções na mesma área de resultado, priorizar por evidência, permitir seleção e confirmação, aplicar apenas ações implementadas e verificar/reverter quando possível. Nenhuma aplicação automática ao terminar uma análise e nenhuma limpeza forçada de RAM. O usuário confirmou que as soluções automáticas ficam para as próximas etapas.

Próximo incremento visual autorizado: fundo Lótus + Aurora, independente da telemetria e do catálogo. Não antecipar módulos de hardware/otimização.


Fundo ambiental Lótus + Aurora implementado como camada independente; validação de movimento/consumo separa produção estática por política do Windows da prova explícita. [Relatório atual](LOTUS_AMBIENT_BACKGROUND.md). Não autoriza avançar para outros módulos.
