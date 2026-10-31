# Histórico de versões

Todas as versões estão em [Releases](../../releases). Para atualizar, rode o instalador novo por cima do atual: seus jogos, diário, conquistas e configurações continuam como estão.

## 1.0.0-alpha "Lotus" (31/10/2026)

**A primeira versão 1.0 do Lotus (alpha).** Ela reúne tudo o que foi construído até aqui:
- Meus Jogos com "Preparar e Jogar" e "Onde eu parei?";
- otimização reversível;
- Lotus Score;
- Meu diário completo;
- Backup de saves;
- backup com Google Drive;
- atualizações automáticas pelo GitHub;
- temas.

O repositório também ganhou README novo com capturas de tela, Política de Privacidade, guia de contribuição, política de segurança e modelos de issue.

### Novo: filtros da Game list
- **Tipo de jogo:** uma janela nova com os tipos mais escolhidos pelos jogadores e, em "Avançado", mais de 100 gêneros e temas organizados em grupos: ação, aventura, RPG, estratégia, simulação, esportes e corrida, temas, visual e câmera, história, jogar sozinho ou com amigos, clima e estilo.
  - Tem busca e permite escolher vários tipos.
  - Mostra os jogos que têm qualquer um dos escolhidos ou todos eles ao mesmo tempo.
  - Os dados vêm da Steam, com cache de 3 dias.
- **Preço:** todos, só gratuitos ou **sem gratuitos**.

### Corrigido nesta versão
- Um envio ao Google Drive que precisasse de nova tentativa (instabilidade do Google) não conseguia reenviar o arquivo. Agora reenvia completo.
- A tela de Atualizações volta a procurar sozinha ao ser aberta depois de uma falha (por exemplo, sem internet).

### Google Drive funcionando
- **Conecte sua conta Google e pronto:** o Lotus abre o navegador, você escolhe a conta e autoriza. Nada de criar projetos ou credenciais: o Lotus já vem com o próprio registro no Google (projeto "Lotus", publicado).
- **Só a permissão mínima:** o Lotus pede apenas acesso aos arquivos que ele mesmo cria no Drive (`drive.file`). Ele não vê nenhum outro arquivo seu.
- **Pasta organizada:** os backups ficam em `Lotus/Backups` no seu Drive. O Lotus encontra a pasta que já criou antes (também em outro PC) e nunca duplica.
- **Enviar automaticamente:** escolha entre **só quando eu clicar**, **toda semana** ou **todo mês**. O Drive guarda as 5 cópias mais recentes.
- **Carregar backup do Drive:** lista os backups para restaurar neste PC, com opção de excluir.
- **Mensagens claras** quando o acesso expira ou é removido na conta Google (o Lotus pede para conectar de novo), sem internet, sem permissão, Drive cheio ou excesso de solicitações (o Lotus tenta de novo sozinho antes de avisar).
- **Segurança:** login no navegador com PKCE e verificação de estado, a credencial fica guardada protegida pelo Windows e "Desconectar" revoga o acesso no Google.
- Nova [Política de Privacidade](PRIVACY.md).
## 0.7.0-alpha "Lotus" (30/10/2026)

A partir desta versão o produto se chama só **Lotus** (o instalador é `Lotus-Setup-<versão>.exe`). Para atualizar a partir da 0.6, rode o instalador por cima: tudo continua como está.

### Novo: Atualizações (Configurações › Atualizações)
- **Procurar atualizações:** o Lotus compara a versão instalada com a mais nova publicada nas releases oficiais do GitHub.
- **Aviso de versão nova:** um aviso discreto com "Ver novidades" e um ponto em Configurações. O Lotus confere sozinho um pouco depois de abrir e a cada 12 horas.
- **Novidades na tela:** as notas da versão aparecem antes de você atualizar, com link para a página no GitHub.
- **Baixar e instalar com um clique**, com animação: um anel se enche em volta da flor do Lotus, com porcentagem, velocidade e tempo restante.
  - Cada instalador é conferido pelo SHA-256 publicado junto. Um arquivo diferente é descartado e nada é instalado.
  - O Lotus fecha, atualiza e abre de novo sozinho.
- **Atualizar automaticamente (opcional):** baixa sozinho e instala em seguida, sempre com aviso e opção de adiar, e nunca com um jogo aberto.
- Com "Efeitos de animação" desligado no Windows, o anel só se enche, sem brilho nem faísca.

### Backup do Lotus com os saves
- O arquivo `.lotusbackup` agora leva **a cópia mais recente e conferida de cada jogo protegido**.
- Ao restaurar (neste ou em outro PC), essa cópia volta para a aba **Backup de saves**, de onde você restaura no jogo quando quiser. Os saves do jogo nunca são trocados sem você confirmar.
- As cópias automáticas semanais continuam pequenas (não levam saves) e a prévia da restauração mostra quantos jogos têm saves no backup.

### Google Drive
- **Você decide quando enviar:** o Lotus nunca manda nada ao Drive sozinho. O Drive guarda as 5 cópias mais recentes para não ocupar espaço demais.
- **Carregar backup do Drive:** mostra os backups guardados no seu Drive para escolher qual restaurar, e cada um pode ser **excluído** do Drive.
- O backup enviado inclui o último save de cada jogo protegido.
- **Google Drive ativado:** basta clicar em "Conectar conta Google" e entrar pelo navegador. O Lotus nunca vê sua senha e só enxerga os arquivos que ele mesmo criou na pasta "Lotus Backups".

### Também
- A aba **Backup de saves** (no menu, abaixo da Game list) mostra todos os jogos em capas, com o selo de proteção, filtros, busca e os totais. Clique num jogo para ver, criar, restaurar ou excluir cópias.

## 0.6.0-alpha "Lotus New" (30/10/2026)

Esta versão é para quem joga sem tempo para configurar nada: o Lotus lembra onde você parou, confere o PC antes de jogar e guarda cópias dos seus saves.

### Novo: "Onde eu parei?"
- **Uma anotação por jogo** (até 600 caracteres), para lembrar o que estava fazendo mesmo semanas depois.
- Aparece em três lugares, sempre a mesma anotação:
  - no **Início**, em "Continuar jogando", com o botão "Ver anotação";
  - no **editor do diário**, na seção "Onde eu parei?" (funciona até para jogos que ainda não estão no diário);
  - no **painel do jogador**.
- **Ao fechar um jogo**, um aviso discreto pergunta se quer anotar, com "Agora não" e "Não perguntar novamente".
  - Se o Lotus estiver escondido ou o Windows em modo "não perturbe", o aviso espera você voltar.
  - Dá para desligar em Configurações.
- As anotações entram no arquivo `.lotusbackup`. Backups antigos continuam restaurando normalmente.

### Novo: "Preparar e Jogar"
- **Todo botão Jogar passa a conferir o PC antes de abrir o jogo**, em silêncio:
  - se o jogo ainda está instalado;
  - memória quase cheia ou pouco espaço livre no disco;
  - controle conectado (quando você pede, para aquele jogo);
  - avisos da proteção de saves.
- Só pergunta algo quando há um problema de verdade, e o jogo sempre pode ser aberto mesmo assim.
- **Perfil Jogo automático (opcional, por jogo):** você autoriza uma vez, vendo cada mudança. O Lotus aplica o perfil antes de jogar e desfaz depois, inclusive na próxima abertura se o PC ou o Lotus fecharem no meio.
- Cartão "Preparar e jogar" na página de cada jogo instalado e chave geral em Configurações.

### Novo: aba "Backup de saves"
- **Nova aba no menu, logo abaixo da Game list**, com todos os seus jogos em capas.
  - No topo, um destaque com o total de jogos protegidos, backups guardados e espaço usado.
  - Filtros Todos, Protegidos, Saves encontrados e Sem saves, com a contagem de cada um, e busca por nome.
  - Cada capa mostra um selo de estado (Protegido, Atenção, Saves encontrados, Sem saves) e quantos backups existem, com a data do último.
  - Jogos protegidos que foram desinstalados continuam na lista, para os backups não sumirem.
- **Ao clicar num jogo:** banner com a arte dele, estado e ações à esquerda, e a lista de backups à direita com Restaurar e Excluir.
- Na página de cada jogo, a linha "Backup de saves" tem o botão **Gerenciar**, que abre a aba direto naquele jogo.

### Novo: Proteção de progresso (saves)
- **Encontra a pasta de saves com segurança**, nunca adivinhando pelo nome do jogo:
  - catálogo de 18 jogos conhecidos da Steam (Stardew Valley, Elden Ring, Cyberpunk 2077, The Witcher 3, Baldur's Gate 3 e outros);
  - a pasta Steam Cloud do próprio jogo;
  - ou uma pasta que você escolher.
- **Ativar por jogo:** a proteção só conta como ligada depois que a primeira cópia é feita e conferida.
- **Backup automático depois de jogar:** espera o jogo fechar de verdade e só cria uma cópia nova se algo mudou. Guarda as últimas 3, 5 ou 10 cópias.
- **Cada cópia é conferida** arquivo por arquivo (SHA-256). Uma cópia incompleta ou danificada nunca é usada.
- **Restaurar com segurança:**
  - mostra arquivo por arquivo o que vai mudar;
  - exige o jogo fechado e avisa quando a nuvem da loja também sincroniza a pasta;
  - guarda antes uma cópia de como seus saves estão agora;
  - se algo falhar no meio, devolve tudo como estava;
  - se o Lotus fechar durante a restauração, avisa na próxima abertura e oferece "Recuperar estado anterior".
- As cópias ficam só neste PC, em `%LOCALAPPDATA%\Lotus\SaveBackups`. Não vão para a internet, para o Google Drive nem para o `.lotusbackup`, que leva apenas as configurações.
- **Não funciona para todos os jogos:** jogos do Xbox / Game Pass guardam os saves num local protegido da Microsoft, e o Lotus não mexe neles.

### Animações mais fluidas
- Todas as animações (contadores, barras de XP e de progresso, anéis de nota, retrospectiva, avisos) agora acompanham a taxa de atualização da tela e só rodam enquanto algo se mexe.
- Painéis e janelas abrem e fecham com as mesmas curvas suaves. As páginas entram com uma leve aproximação.
- A configuração de animações do Windows é respeitada em todo o app.

### Corrigido
- Os ícones das sensações no editor do diário apareciam como quadrados (□□□□).
- No tutorial, as cores e os temas prontos aparecem juntos de novo: escolher um desmarca o outro.
- Configurações gravadas por versões anteriores não perdem mais os valores padrão das opções novas.

## 0.5.0-alpha "Lotus New" (30/10/2026)

Esta versão é sobre o **visual**: o Lotus passa a ter temas completos e editáveis, com fundos animados.

### Novo: temas
- **Galeria com 12 temas prontos**, cada um com cores e fundo animado próprios:
  - escuros: Lotus Rosé, Synthwave, Cyberpunk, Aurora, Oceano, Floresta, Ouro Real, Monocromático e Vampiro;
  - claros: Sakura, Gelo e Porcelana.
- **Estúdio de temas** (Configurações › Aparência › Criar tema):
  - cinco cores à sua escolha: fundo, superfície, texto, destaque e detalhe;
  - base escura ou clara e "Começar de uma cor", que monta o tema inteiro a partir de uma cor;
  - transparência dos cartões, para o fundo aparecer por trás;
  - o app inteiro funciona como prévia ao vivo enquanto você edita; "Cancelar" pergunta antes de descartar.
- **Leitura garantida:** o Lotus confere o contraste de cada texto (padrão WCAG), inclusive com cartões transparentes, ajusta sozinho o que ficaria ilegível e avisa o que mudou. Em destaques claros (amarelo neon, verde-menta), os botões usam texto escuro.
- **Fundos animados:**
  - degradê de 2 a 5 cores, pétalas caindo, aurora, partículas, ondas neon ou a flor de lótus original;
  - velocidade, intensidade e granulado ajustáveis;
  - pausam com a janela minimizada ou inativa e ficam parados com "Reduzir animações".
- **Compartilhar temas:** "Copiar código" gera um texto para mandar numa conversa; "Colar código" aplica o tema e o guarda em "Meus temas" (com usar, editar, copiar e excluir).
- O banner de destaque acompanha as cores do tema.
- **O logo do Lotus nunca muda:** flor, nome e "PLAY BETTER." mantêm as cores da marca em qualquer tema.

### Tutorial inicial melhorado
- As boas-vindas mostram as 5 etapas reais (perfil, objetivo, visual, programas e pronto).
- "Seu visual" ganhou **6 temas prontos** aplicados ao vivo, além dos modos e cores. Escolher um tema pronto desmarca a cor, e escolher uma cor desmarca o tema pronto (volta ao modo anterior).
- Programas recomendados em duas colunas, sem precisar rolar.
- O tour final mostra o **Meu diário** (sessões que entram sozinhas, "O que jogar agora?" e retrospectiva) no lugar do exemplo "preparar e jogar", e o "Por que está travando?" cita os overlays.

### Corrigido
- O fundo animado não recomeça mais a cada mudança de preferência (por exemplo, ao mexer no volume).
- Botões principais e superfícies de destaque usam a cor de texto calculada pelo tema, em vez de branco fixo.

## 0.4.0-alpha "Lotus New" (30/10/2026)

Esta versão é sobre o **Meu diário**: ficou mais bonito, mais esperto e passou a trabalhar sozinho enquanto você joga.

### Novo no diário
- **Três jeitos de ver os jogos:** grade de capas, lista detalhada ou tabela com colunas ordenáveis. O Lotus lembra a escolha.
- **Filtros:**
  - por gênero, plataforma, loja, faixa de nota e ano, montados a partir dos seus próprios jogos;
  - os filtros ativos aparecem por escrito;
  - **filtros salvos** com nome, guardados no arquivo do diário e incluídos no backup.
- **Meu Top 4:** quatro favoritos escolhidos por você, em destaque.
- **Capas especiais:**
  - brilho e faixa "PLATINA" para jogos platinados;
  - moldura e selo dourados para nota 10;
  - capa esmaecida para jogos abandonados.
- **Topo do perfil compacto ou completo**, à sua escolha, para os jogos virem primeiro.
- **Sessões automáticas:**
  - com um jogo da biblioteca aberto, o Lotus anota o tempo e as datas no diário e avisa perto do relógio quando a sessão termina;
  - funciona também com jogos da Epic e da GOG;
  - "Quero jogar" e "Pausado" passam para "Jogando"; jogos concluídos ou abandonados mantêm o status;
  - pode ser desligado em Configurações.
- **O que jogar agora?** Sugere um jogo da sua lista pelo tempo que você tem (pouco, algumas horas, bastante), pelo clima (relaxar, desafio, história, ação) e pelas notas que você costuma dar. Sempre diz o porquê e tem o botão "Outra sugestão".
- **Promoções da sua lista:**
  - jogos de "Quero jogar" em promoção na Steam aparecem no diário com o desconto e o preço;
  - quando surge uma promoção nova, chega um aviso discreto perto do relógio.
- **Aba Estatísticas:**
  - números principais: jogos, concluídos, horas, nota média, tempo médio até concluir, horas por jogo concluído;
  - gráficos: horas por mês (as sessões registradas caem no mês exato), distribuição das notas, gêneros favoritos, plataformas, médias por categoria, sensações e dificuldade.
- **Retrospectiva do ano:**
  - telas no estilo stories: horas, jogos, gênero do ano, o jogo que mais prendeu você, maior nota e sensação do ano;
  - termina num cartão-resumo que vira imagem (em Imagens\Lotus) para compartilhar.

### Editor de jogo redesenhado
- **Banner** com a arte oficial da loja de onde o jogo veio (Steam, Epic ou GOG), em movimento suave.
- **Nota** num anel colorido com rótulo, de "Ruim" a "Obra-prima".
- **Status ilustrados**, cada um na sua cor.
- Duas colunas que se empilham em janelas estreitas.
- **Detalhes novos:**
  - notas por categoria (história, jogabilidade, gráficos, som), com "usar a média";
  - sensações (empolgante, relaxante, desafiador...);
  - dificuldade em que jogou;
  - outras zeradas, cada uma com datas, horas e plataforma.

### Novo no PC e nos jogos
- **Overlays e travadas** (Ferramentas):
  - mostra o que costuma causar travadas: Discord com aceleração de hardware, overlays da NVIDIA e da AMD, captura do Xbox, Overwolf, Medal, OBS, papéis de parede animados e apps de RGB;
  - explica como desligar cada um. Nada é alterado pelo Lotus;
  - também entra como causa no "Por que está travando?".
- **Prioridade ao jogo** (opcional, em Configurações):
  - com um jogo aberto, ele fica acima do normal, e navegadores, launchers e apps de sincronização ficam abaixo do normal;
  - tudo volta como estava quando o jogo fecha ou o Lotus é encerrado;
  - o Discord nunca é alterado.

### Corrigido
- A cópia das configurações originais de cada jogo agora é gravada de forma segura. Antes, uma queda de energia no meio da gravação podia deixar uma cópia cortada que o "Restaurar" devolveria ao jogo.
- A busca de jogos e o FPS estimado não falham mais quando o cache da Steam não pode ser gravado (arquivo bloqueado ou disco cheio).
- Leitura das artes das lojas fora da tela principal, sem travadinhas ao abrir o editor.
- Média de notas igual no topo do diário e nas estatísticas.

## 0.3.1-alpha "Lotus New" (29/10/2026)

### Novo
- **Otimizar este jogo:** painel lateral em Meus Jogos (botão Otimizar de cada jogo). Reúne:
  - o que o jogo tem, lido dos arquivos dele: DLSS, Geração de quadros, FSR, XeSS e o motor;
  - as configurações para o seu PC e o FPS esperado pelo seu Lotus Score;
  - os ajustes dentro do jogo em ordem de impacto, com o nome das opções nos menus;
  - a última análise em jogo, aplicar ou restaurar configurações e pré-carregar shaders.
- **Fluidez e resposta**, pelo RivaTuner, num perfil só daquele jogo e com desfazer:
  - **Fluidez (estilo console):** ritmo de quadros cravado e linha do framepacer reta, em qualquer placa;
  - **Resposta:** limitador NVIDIA Reflex, com menos atraso, inclusive em jogos sem Reflex no menu.
- **FreeSync/G-Sync.** O Lotus pergunta uma vez, antes da primeira análise. Numa placa NVIDIA, o driver responde sozinho. Na AMD e na Intel, a identificação do monitor indica se há suporte e o Lotus recomenda ativar.
- **Limite de FPS pelo monitor de cada jogador:**
  - **com FreeSync/G-Sync:** 3 FPS abaixo da taxa;
  - **sem:** a própria taxa ou uma fração exata dela (por exemplo 37,5 em 75 Hz), com V-Sync, sem trepidação. Vale para monitores de 165, 240 Hz ou mais.
- **Modo leve:** com um jogo aberto, ou com o Lotus minimizado, ele entra no modo eficiência do Windows, baixa a prioridade e devolve memória (cerca de 20 MB minimizado). Nunca reduz nada durante uma medição.
- **Restaurar o Lotus** (Configurações › Segurança e restauração): volta ao início sem apagar nada. Faz uma cópia de segurança antes e guarda os dados antigos.
- **Tutorial inicial** redesenhado, com exemplos em cada passo e escolha de tema.
- **Instalador** com o visual do Lotus.

### Melhorado
- **Análise em jogo (Ctrl + Shift + L):**
  - textos reescritos;
  - barra de progresso dentro do jogo;
  - resultado com FPS médio, 1% low, travadas e uma dica.
- **Barra de título** limpa, com o selo "Jogando agora" quando um jogo está aberto.
- **Game list** com até 100 jogos por lista e carregamento aos poucos.
- **Imagens** carregadas no tamanho em que aparecem, o que reduz o uso de memória.
- **Detecção de jogo aberto** com uma única varredura leve de processos.

### Corrigido
- Perfis de jogo do RivaTuner agora usam o nome que ele reconhece (`<jogo>.exe.cfg`).
- A geração de quadros não é mais sugerida em monitor sem FreeSync/G-Sync.
- As dicas não parecem mais opções selecionáveis.
- O modo leve não interfere no Lotus Score, nas medições nem na análise em jogo.

## 0.3.0-alpha "Lotus New" (28/10/2026)

- **Visual novo:** navegação lateral, temas Escuro, Claro (porcelana rosé), Sistema e Grafite, e cores de destaque.
- **Otimizar:** perfis com confirmação animada ao aplicar ou trocar.
- **Lotus Score:**
  - notas de gráficos e CPU, estabilidade e sensores;
  - FPS estimado por jogo, com busca de qualquer jogo da Steam e fontes;
  - gargalo.
- **Meus Jogos:** aba Overlay e Jogar direto da capa.
- **Meu diário:** 14 medalhas com critério e progresso; o editor pede confirmação antes de descartar alterações.
- **Desempenho:** Comparar em duas colunas.
