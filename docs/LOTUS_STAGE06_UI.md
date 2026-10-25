# Etapa 06 — Interface e identidade visual (24/10/2026)

Redesenho da interface WinUI 3 e da marca, sem mudar arquitetura MVVM/DI nem funcionalidades.

- **Identidade:** logo do usuário recriada em vetor (flor com contorno ouro rosé e pétalas carmesim; wordmark "Lotus" em Georgia com degradê ouro). Tokens de cor: base ameixa `#170B13`, destaque carmesim `#D6306F`, rosa `#F27BA8`, ouro rosé `#EEC1A8`. `ThemePalette` atualiza os pincéis em tempo real; o tema Grafite mantém os destaques em superfícies neutras; alto contraste usa as cores do sistema.
- **Shell:** barra de título integrada, navegação translúcida, área de conteúdo arredondada, cards translúcidos, linhas de configuração no estilo do Windows 11, entrada escalonada (`EntranceThemeTransition`), desligada por "Reduzir animações".
- **Marca animada (`BrandPulse`):** substitui a grade de pixels. Usa a máscara alfa do texto/flor (nítida em qualquer DPI): halo que respira, reflexo a cada 7 s e flor com efeito mola ao aparecer. Só compositor, sem temporizador de UI.
- **Perfis (Otimizar):** cards Jogo/Diário/Personalizado, "Alterações previstas" (energia, inicialização somente leitura, ações reversíveis) e faixa de segurança. Game Mode do conceito **não** foi incluído: não existe no Lotus. Os textos evitam prometer reversão absoluta.
- **Inicialização (aprendizado do ResetExplode):** status Habilitado/Desabilitado lido de `Explorer\StartupApproved` (convenção do Gerenciador de Tarefas), somente leitura. Corrigida a duplicidade de `HKCU\...\Run` lido nas visões de 64 e 32 bits (não é redirecionado) e ignorado `desktop.ini`. Consulta real: 17 entradas (5 habilitadas, 12 desabilitadas), antes reportadas como 33.
- **Ícone e atalho:** `tools/New-LotusIcon.ps1` gera `src/Lotus.App/Assets/Lotus.ico` a partir do mesmo vetor; `tools/New-LotusShortcut.ps1` cria `Lotus.lnk` (ignorado pelo Git) apontando para o build Release.

Validação: builds Debug/x64 e Release/x64 sem avisos/erros; 161 testes aprovados em cada configuração (154 anteriores + 7 novos de inicialização). Capturas de Início, Otimizar, Desempenho e Configurações verificadas no app real. Pendente: alteração real do plano de energia (aguarda aprovação específica) e checkpoint Git.
