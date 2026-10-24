# Etapa 05 — fundação do otimizador

Implementada e validada em 24/10/2026. Substitui a prioridade de diagnóstico avançado. Nenhuma otimização do Windows está habilitada.

## Escopo funcional

- `OptimizationEngine` e contratos tipados: elegibilidade, definição declarativa, estado, plano, aplicação por comparação de estado, verificação e reversão.
- Catálogo mínimo: `lotus.test.preference.v1`, risco baixo, compatível exclusivamente com configuração SQLite descartável do Lotus, reversível enquanto não houver conflito. Ativa `enabled`; não promete FPS nem modifica preferências reais.
- `DisposableOptimizationStore`: banco separado em `%LOCALAPPDATA%/Lotus/OptimizerSandbox/journal.db`, schema versionado 1, transações e synchronous=FULL. Estado anterior e intenção confirmada são persistidos antes da mutação. Registro do benchmark permanece intacto.
- Exclusão entre instâncias por arquivo aberto com FileShare.None; falha de aquisição bloqueia a operação. Escrita atômica exige valor, revisão e identificador de escrita esperados, evitando sobrescrever mudanças externas e mudanças de ida e volta realizadas pelo contrato.
- Recuperação reabre o journal e observa o estado: confirma aplicação/reversão já feita, retorna a um estado que exige nova confirmação ou marca conflito. Nunca retoma uma mutação automaticamente. Exceções mantêm a intenção pendente, preservam o backup e são registradas no logging existente.
- Idempotência por ID de operação; revisão aumenta na reversão, preservando o valor anterior e a detecção de conflitos. Não há executor genérico de comandos nem elevação. Manifesto mantém asInvoker.

## Interface e arquivos

Otimizar > Restaurar contém o painel de teste, identificado explicitamente como descartável. Analisar prepara a ação; confirmação habilita aplicação ou reversão; a autorização é consumida após cada operação e ao trocar a seleção. Estados e erros em pt-BR/en-US. O seletor permite consultar até 100 operações recentes; o journal mantém as anteriores.

Arquivos: Core/Optimization (motor e ViewModel), Infrastructure/Optimization (SQLite e ação descartável), App/Controls/OptimizationPanel, DI em App.xaml.cs, passagem do ViewModel por MainWindow/SectionViewModel, conteúdo de SectionPage, recursos de idioma e OptimizationTests.

## Validação executada

- Debug/x64: solução compilada, zero avisos/erros. Release/x64: aplicativo compilado com OutDir=artifacts/stage05/Release para preservar a versão já aberta, zero avisos/erros.
- Suíte completa: **142 testes em Debug e 142 em Release**, todos aprovados. Treze novos casos: confirmação, aplicação/verificação, idempotência, reversão, conflitos antes de aplicar/reverter, falha do journal, falha após mutação, falha na verificação, recuperação antes/depois de escrita/reversão, concorrência entre instâncias e rejeição de schema futuro.
- Falhas/interrupções reproduzidas por injeção de exceção e journal intermediário com reabertura. Não foi simulado corte físico de energia; durabilidade ainda depende do armazenamento e das garantias SQLite.
- Execução real Release pelo Computer Use: analisar, conferir bloqueio sem confirmação, confirmar/aplicar, verificar sucesso, confirmar/reverter e observar valor anterior restaurado. Capturas temporais e árvore de acessibilidade registradas na conversa.
- Operação real `5d94a962-4261-4ede-ab08-b7a34541b0c1`: estado final Reverted, valor NULL, revisão 2. Conferência SQLite independente somente leitura: integrity_check=ok. Evidência em artifacts/stage05/ui-journal-verification.json (fora do Git).

## Limites e próxima etapa

Reversão é condicional, não garantia universal. O mecanismo atual suporta uma única ação atômica descartável, não transações distribuídas de várias configurações Windows. Arquivo/banco corrompido ou acesso negado bloqueia a operação; não se apaga o journal para contornar falhas. A aba de restauração é um laboratório nesta etapa, não uma lista de melhorias reais.

A Etapa 06 deverá autorizar e validar individualmente cada ação real, com detecção e rollback específicos. Não ampliar privilégios nem reutilizar o executor de teste como executor administrativo. Nenhuma configuração de Windows, GPU, jogo, proteção ou serviço foi modificada.

Referências consultadas: [transações SQLite](https://www.sqlite.org/lang_transaction.html) e [funcionamento do UAC](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/how-it-works). Não há integração com Registro nesta etapa.
