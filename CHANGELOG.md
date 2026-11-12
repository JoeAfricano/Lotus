# Histórico de versões

Todas as versões estão em [Releases](../../releases). Para atualizar, rode o instalador novo por cima do atual: seus jogos, diário, conquistas e configurações continuam como estão.

## 1.5.0-alpha "Lotus" (12/11/2026)

### Novo
- **Tempo para zerar na página de cada jogo.** Na Game list e em Meus Jogos, a página do jogo mostra quanto tempo ele leva para zerar, segundo o HowLongToBeat: só a história, com extras e 100%, com link para o site. Funciona também para jogos que não estão no diário. Quando o jogo está no diário, o tempo fica salvo nele.
- **RivaTuner ao jogar.** Se o Lotus usa o RivaTuner no jogo (overlay ou limitador) e ele está fechado, o Lotus abre o RivaTuner antes do jogo. A permissão de administrador é pedida pelo próprio RivaTuner. Se ele não abrir, o jogo abre mesmo assim e o Lotus avisa.
- **Geração de quadros.** Na NVIDIA, Resposta (Reflex) passa a ser a opção principal, porque o DLSS Frame Generation não combina com outros limitadores. Na AMD e na Intel, o Lotus indica usar o limite do próprio jogo ou do driver. O Reflex só é oferecido em jogos Direct3D 11/12.
- **Fluidez com um botão.** No Otimizar de cada jogo, o cartão do RivaTuner tem só **Ligar** e **Desligar**. O Lotus escolhe o modo pelo seu monitor (taxa e FreeSync/G-Sync) e pela última análise, e diz o motivo: Fluidez (ritmo de console), Resposta (Reflex) ou a nova **Resposta sem V-Sync** (experimental), que deixa os quadros cravados como no V-Sync, mas sem o atraso dele, quando a placa tem folga.
- **Atraso do PC e quadro a quadro.** O "Por que está travando?" mede cada quadro pelo RivaTuner (sem pedir administrador) e, em jogos com Reflex, mostra o atraso entre o comando e a placa terminar o quadro, além de quanto a placa realmente trabalhou naquele jogo. Funciona para jogos abertos depois do Lotus; nos abertos antes, a medição continua como era.
- **Gerador de quadros só quando compensa.** O Lotus só sugere a geração de quadros com FreeSync/G-Sync em monitor de 120 Hz ou mais. Nos outros, a dica é deixá-la desligada: num teste em RTX 5060 com monitor de 75 Hz, ela subiu o atraso de 50 para 81 ms e piorou as quedas. A escolha continua sua: marcando "Uso o gerador de quadros" no cartão do RivaTuner, o Lotus para de insistir e ajusta o limitador para combinar com ele.
- **Limite global do RivaTuner** aparece no cartão do RivaTuner e no Otimizar. No "Por que está travando?", um FPS preso no limite passa a ser explicado como limite, e não como culpa da placa ou do processador.

### Corrigido
- **DLSS e geração de quadros em jogos Unreal** não eram reconhecidos (os arquivos ficam em pastas mais fundas). Agora aparecem nas etiquetas e nas dicas.
- **Topo da página do jogo vazio.** Às vezes a imagem grande do jogo travava no download sem dar erro, e o topo ficava só com a cor de fundo. Agora, se ela não carregar em 6 segundos, o Lotus usa o fundo da loja ou a capa menor.

### Por dentro
- O overlay de FPS respeita a trava de escrita do RivaTuner (SDK v2.14+), e a pasta do RivaTuner é lida do registro.

## 1.4.1-alpha "Lotus" (12/11/2026)

### Melhorado
- **Otimizar este jogo abre no centro da tela.** A barra lateral virou uma janela grande, com a arte do jogo maior, textos e valores recomendados mais legíveis e duas colunas quando há espaço: configurações e dicas à esquerda; fluidez, última análise e ferramentas à direita. Em janelas pequenas, tudo fica numa coluna só.

### Corrigido
- **Menu "…" dos jogos sem nomes.** Em Meus Jogos, o menu mostrava só os ícones. Agora aparecem "Editar nome e capa…" e "Remover da lista".
- **Botão "Otimizar" cortado** ("Otimiz") nos cartões de Meus Jogos.
- **Textos que passavam da borda** na janela Otimizar (última análise e título das dicas).

## 1.4.0-alpha "Lotus" (12/11/2026)

### Novo
- **Saves de milhares de jogos.** A Proteção de progresso passa a conhecer a pasta de saves de cerca de 17 mil jogos, pelo catálogo aberto **Ludusavi** (dados da PCGamingWiki). Jogos da Steam e da GOG são reconhecidos pelo código da loja; os de outras lojas, pelo nome exato, e nesse caso o Lotus pede para você conferir a pasta antes de proteger. O catálogo é atualizado no máximo uma vez por semana e só quando mudou.
- **Tempo para zerar.** O editor do diário mostra quanto tempo o jogo leva (história, com extras e 100%, do HowLongToBeat), e o "O que jogar agora?" passa a dizer quando falta pouco para zerar um jogo ou quando um jogo da lista dá para terminar em poucas noites.
- **Bateria do controle.** O Preparar e Jogar avisa quando o controle sem fio está com 20% ou menos. Carregando ou com cabo, nada é dito.
- **Processador no limite.** O "Por que está travando?" reconhece quando o processador reduz a própria velocidade (calor ou limite de energia) e, durante o jogo, um aviso discreto aparece uma vez se isso durar alguns minutos. Funciona sem administrador e sem drivers extras.
- **Driver de vídeo novo.** Uma vez por semana o Lotus confere no site da NVIDIA se saiu driver mais novo que o seu e avisa uma vez por versão, com o link da NVIDIA. Para AMD e Intel, avisa quando o driver tem mais de seis meses. Pode desligar em Configurações. O Lotus nunca baixa nem instala drivers.

### Corrigido
- **Capas em "Na sua lista" (Início) e no diário.** Jogos de outras lojas, adicionados à mão, do Lotus Ports e lançamentos recentes da Steam apareciam só com o nome. O Lotus agora encontra a capa sozinho, uma vez, sem mudar a ordem da lista. As capas do Lotus Ports chegam mesmo com o Lotus Ports fechado.
- **Programas sempre abertos não contam mais como "jogo aberto".** O Wallpaper Engine e os launchers (Steam, Epic e outros) impediam as tarefas que esperam o jogo fechar, como o tempo para zerar e o aviso de driver.

## 1.3.1-alpha "Lotus" (12/11/2026)

### Novo
- **Medir FPS dos jogos do Lotus Ports** (Configurações, desligado por padrão). Com a opção ligada, quando um jogo aberto pelo Lotus Ports está rodando, o Lotus mede os quadros por 60 segundos (começando 20 segundos depois de o jogo abrir) e o resultado entra no relatório da sessão. O PresentMon precisa de administrador, então o Windows pede confirmação a cada partida. Para nunca mais ser perguntado, coloque sua conta no grupo "Usuários de log de desempenho" (passo a passo em [docs/LOTUS_PORTS_INTEGRATION.md](docs/LOTUS_PORTS_INTEGRATION.md)). Com a opção desligada, nada muda: o Lotus nunca pede administrador sozinho.

### Por dentro
- Conferência automática de que o motor de temas do Lotus Ports continua igual ao do Lotus (cada app mantém sua própria escolha de tema; no Lotus Ports o padrão segue "Igual ao Lotus").
- Testes sem avisos de compilação.

## 1.3.0-alpha "Lotus" (07/11/2026)

### Novo
- **Lotus Ports chegou.** O app irmão de ports nativos de jogos clássicos está publicado, e a integração com o Lotus está completa.
  - **Convite no Início:** um cartão pequeno ("NOVO") abre uma tela que explica o que é o Lotus Ports. **Instalar** baixa a versão mais recente do GitHub oficial, confere o arquivo pelo SHA-256 publicado e abre o instalador do próprio Lotus Ports, onde você confirma, sem precisar de administrador. Se o arquivo não bater com a conferência, nada é aberto. Com o Lotus Ports instalado, o mesmo cartão o abre.
  - **Meus Jogos:** "Seus jogos do Lotus Ports" com **Jogar**. Com o Lotus Ports fechado, um aviso oferece abri-lo, e os jogos aparecem assim que ele responde.

### Melhorado
- **Pasta de instalação limpa:** o programa agora fica numa subpasta `app`, e a pasta do Lotus mostra só o atalho "Lotus" e o desinstalador (o mesmo modelo do Lotus Ports). Ao atualizar, os arquivos da organização antiga saem sozinhos. Atalhos, "Iniciar com o Windows" e a integração com o Lotus Ports são ajustados para o novo lugar.
## 1.2.2-alpha "Lotus" (06/11/2026)

- Revisão de bugs e otimização.

## 1.2.1-alpha "Lotus" (06/11/2026)

### Lotus Ports: em breve
- O **Lotus Ports**, app irmão de ports nativos de jogos clássicos, será lançado em breve. A integração já vem pronta no Lotus: quando ele chegar, seus jogos dele aparecem em Meus Jogos com **Jogar**, e o Lotus passa a ajudar com diagnóstico, dependências e sessões. Até lá, nada muda na sua tela.

### Melhorado
- **O instalador pede ao Windows para atualizar o cache de ícones**, para atalhos, menu Iniciar e barra de tarefas mostrarem sempre o ícone certo do Lotus.

## 1.2.0-alpha "Lotus" (06/11/2026)

### Novo
- **Integração com o Lotus Ports** (app irmão de ports nativos de jogos clássicos). Os dois conversam por um canal local só do seu usuário. Cada lado confere que o outro é mesmo o programa registrado, e nada é executado fora do que o protocolo prevê.
  - **Meus Jogos:** nova faixa "Seus jogos do Lotus Ports" (instalados e favoritos), com **Jogar** e **Abrir no Lotus Ports**. O Lotus Ports abre o jogo com as próprias conferências, ou a página do jogo se algo precisar da sua confirmação. A faixa só aparece com o Lotus Ports instalado e aberto.
  - **O Lotus ajuda o Lotus Ports:**
    - conferências do sistema no "Resolver": driver de vídeo, Vulkan, memória livre e espaço no disco;
    - dependências instaladas: Visual C++ 2015–2022, Vulkan, DirectX antigo e .NET Desktop;
    - mudanças recentes que o Lotus fez no PC;
    - perfil de hardware;
    - relatório da sessão: FPS médio, 1% low e travadas só quando a medição foi válida; senão, nenhum número.
  - **Ao jogar pelo Lotus Ports:** o Lotus vai para a bandeja e volta quando o jogo fecha, e a sessão entra no **diário** (com "Registrar minhas sessões" ligado).
  - **Segurança:** nenhum pedido do Lotus Ports altera o PC. Instalar dependências e sugestões automáticas ficam de fora até existirem com confirmação na interface do Lotus.
  - O Lotus Ports será lançado em breve.

## 1.1.1-alpha "Lotus" (04/11/2026)

### Melhorado
- **Ao abrir um jogo, o Lotus vai para a bandeja** (ícone ao lado do relógio) em vez de ficar minimizado na barra de tarefas, e **volta sozinho quando o jogo fecha**, do jeito que estava (maximizado continua maximizado).
  - Só volta se foi o próprio Lotus que se escondeu: se você abrir o Lotus pela bandeja durante o jogo, ele não some de novo nem muda nada quando o jogo fecha.
  - Se o jogo não chegar a abrir (launcher fechado, página da loja), o Lotus volta depois de alguns minutos.
  - Se o ícone da bandeja não estiver disponível, ele apenas minimiza, para nunca ficar inacessível.
  - A opção em Configurações passou a se chamar "Ir para a bandeja ao abrir um jogo" e continua podendo ser desligada.
## 1.1.0-alpha "Lotus" (04/11/2026)

A aba **Ferramentas** foi reconstruída em volta de um motor de manutenção. Todas as ferramentas seguem o mesmo fluxo:
1. detectar e analisar;
2. classificar o risco (baixo, revisão ou avançado);
3. recomendar;
4. pedir confirmação;
5. executar;
6. conferir de novo;
7. registrar e, quando possível, desfazer.

Abrir ou analisar nunca muda nada.

### Novo
- **Check-up do PC:** cartão em destaque no topo da aba. Um clique roda todas as análises (só leitura) e mostra o resultado em ordem: o que pede atenção, as dicas e o que está em ordem. Cada linha leva à ferramenta certa. Não há nota inventada.
- **Limpeza Inteligente** (substitui "Arquivos temporários"):
  - **O que analisa:** temporários do usuário e do Windows, despejos de falha, relatórios de erro, miniaturas, Lixeira, caches do Chrome/Edge/Brave/Opera/Opera GX/Firefox/Discord, caches das páginas da Steam e da Epic, registros antigos de apps e launchers, e falhas de jogos.
  - **O que mostra:** antes, o tamanho real e o risco de cada item; depois, o que saiu de fato, o que estava em uso e o que foi pulado.
  - **O que nunca toca:** saves, mods, configurações, cookies, sessões, senhas e caches de shader.
  - **Proteções:** caches de programas abertos são pulados; links não são seguidos; pastas do usuário e do sistema nunca são usadas inteiras. A Lixeira nunca vem marcada.
- **Memória e Processos** (substitui "Liberar memória"):
  - RAM e CPU reais de cada programa, com o local.
  - Fechar é normal; "Encerrar à força" é avançado e tem aviso próprio.
  - Lista "fechar antes de jogar", marcada com estrela.
  - Windows, antitrapaças, jogos e o Lotus (com RTSS, Afterburner e PresentMon) nunca podem ser fechados.
- **Arquivos e Armazenamento:**
  - espaço por disco, arquivos grandes, pastas que mais ocupam, duplicados comparados pelo conteúdo inteiro (SHA-256) e downloads com mais de 90 dias;
  - "Abrir local" no Explorer;
  - remover só manda para a Lixeira e sempre deixa uma cópia de cada duplicado;
  - arquivos do Windows e de programas ficam só para consulta.
- **Inicialização do Windows:** nome, editor, assinatura digital, caminho e mecanismo de cada item. Desligar e religar usam o mesmo desfazer do Otimizar. Não há "impacto" inventado.
- **Monitor na taxa certa:** mostra a taxa atual e a máxima do monitor. "Usar X Hz" e "Desfazer" usam o mesmo ajuste do Otimizar.
- **Recursos do Windows para jogos:** Modo de Jogo, gravação em segundo plano, otimizações em janela e agendamento de GPU, com o estado, um conselho e o ajuste reversível de cada um.
- **Por que meu jogo fechou?:**
  - lê as falhas e travamentos de jogos registrados pelo Windows;
  - cruza com o que aconteceu no mesmo minuto: driver de vídeo reiniciando, falta de memória, erro de hardware ou de disco;
  - explica a causa provável e o que fazer, com o código técnico para pedir suporte.
- **Windows em dia:** mostra se há reinício pendente do Windows Update e há quanto tempo o PC está ligado (com aviso sobre a Inicialização Rápida). Cria ponto de restauração do Windows; ele só aparece como criado se a lista do Windows realmente aumentar.
- **Saúde do Disco:**
  - modelo, tipo e conexão, capacidade, espaço e a saúde informada pelo Windows;
  - temperatura, desgaste, horas e erros quando o Windows libera, ou "Indisponível";
  - verificação do Windows `chkdsk /scan`, somente leitura;
  - nunca desfragmenta SSD.
- **Sistema** (no cartão Drivers):
  - processador, vídeo, RAM, placa-mãe, BIOS e Windows;
  - dispositivos com erro no Gerenciador de Dispositivos;
  - erros relevantes do Windows na última semana;
  - SFC e DISM com confirmação.
- **Desinstalar programas:** ordenar por nome, tamanho, data ou editor.
- **Internet e DNS:** histórico dos últimos testes, só com números medidos.
- **Overlays e travadas:** RAM e CPU reais de cada item. Launchers aparecem como informativos e RTSS, Afterburner e PresentMon como compatíveis.

### Melhorado
- **Início:** o aviso "Antes de começar" virou uma nota discreta no fim da tela, e a faixa "Seus jogos" usa a largura toda.

### Corrigido
- **Entrada "Iniciar com o Windows" do Lotus:** podia apontar para outra cópia do programa. Agora o Lotus instalado corrige o caminho sozinho, e builds de desenvolvimento nunca gravam essa entrada.

## 1.0.4-alpha "Lotus" (04/11/2026)

### Corrigido
- **Saves do Palworld (e de muitos outros jogos da Steam) não eram encontrados.**
  - Esses jogos guardam os saves fora da Steam (o Palworld usa `%LOCALAPPDATA%\Pal\Saved\SaveGames`) e só usam a Steam para sincronizar (o "Auto-Cloud").
  - Agora o Lotus lê o índice da própria Steam (`remotecache.vdf`), que lista cada arquivo de save e de qual pasta do Windows ele vem, e encontra a pasta certa sozinho, para qualquer jogo que use esse recurso.
  - Segurança: o Lotus nunca usa uma pasta genérica inteira (Documentos, AppData…), não aceita caminhos que saiam da pasta base e só considera pastas que existem e têm arquivos.
  - A lista de jogos conhecidos e a escolha manual da pasta continuam funcionando como antes.

## 1.0.3-alpha "Lotus" (04/11/2026)

### Novo
- **Adicionar jogo, do jeito certo:** procure o jogo na loja e o Lotus traz o nome e a capa, ou preencha do seu jeito com uma imagem sua.
  - O `.exe` é opcional, o que serve para jogos de outros launchers, como Minecraft pelo Modrinth.
  - A mesma janela também aparece na aba **Backup de saves**, para adicionar um jogo e já escolher a pasta dos saves.
- **Editar nome e capa** de qualquer jogo: pelo menu **"…"** de cada cartão em Meus Jogos. Dá para voltar à capa original quando quiser.
- **Remover da lista** ficou à mão no mesmo menu.
  - Jogos da loja só saem da lista (Reescanear traz de volta).
  - Jogos adicionados só pelo nome são apagados de vez.
  - Backups e diário nunca são apagados.

### Corrigido
- **"Escolher pasta…" desligado** quando o Lotus não encontrava a pasta de saves sozinho, exatamente quando ela mais faz falta.
- **Capa sumindo** no Backup de saves depois de desinstalar um jogo.
- **O Lotus demorava (ou não chegava) a minimizar** ao abrir um jogo.
  - Agora minimiza assim que você clica em Jogar.
  - Se o jogo não chegar a abrir, o Lotus volta depois de alguns minutos.
- **Faixa "Seus jogos" no Início** cortava o último jogo; agora rola para o lado.

## 1.0.2-alpha "Lotus" (31/10/2026)

### Corrigido
- **Capas borradas ou sumindo na Game list:** em vários lançamentos, a Steam guarda a capa vertical num endereço que muda, e o Lotus acabava esticando a imagem horizontal pequena.
  - Agora o Lotus pergunta à Steam o endereço exato da capa (600x900), várias capas numa consulta só, e guarda a resposta para as próximas vezes.
  - Se um jogo não tiver capa vertical, a imagem horizontal aparece inteira, sem cortar nem borrar.
  - Ao adicionar um desses jogos ao diário, ele também recebe a capa certa.

## 1.0.1-alpha "Lotus" (31/10/2026)

### Novo
- **O Lotus sai da frente quando o jogo abre:** minimiza sozinho e volta, do jeito que estava, quando o jogo fecha.
  - Funciona com jogos abertos pelo Lotus ou pela loja.
  - Se você mesmo minimizou ou escondeu o Lotus, ele não volta sozinho.
  - Chave "Minimizar ao abrir um jogo" em Configurações.
- **Game list com 250 jogos por lista** (antes eram 100).
- **Filtros com muito mais jogos:**
  - com um tipo escolhido, a lista mostra os jogos da aba atual, na posição deles, e completa com até 300 jogos populares daquele tipo;
  - preço e nota mínima também valem para esses jogos.

### Melhorado
- **Filtro por tipo mais certo:** usa a busca por marcadores da própria loja Steam, que considera só os marcadores principais de cada jogo e já vem ordenada por vendas. Jogos como Stardew Valley não aparecem mais em "Terror".
- **"Em alta"** agora mostra os mais jogados neste momento.
- A Game list passou a mostrar a nota das análises e quais jogos são gratuitos também nas listas da loja.

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
- Os filtros valem para todas as listas, inclusive **Mais vendidos**: dá para ver os mais vendidos de cada tipo, na ordem de vendas. A frase acima da lista mostra quantos jogos aparecem com os filtros.
- O sorteio "Não sei o que jogar" ficou mais simples: escolha só a dificuldade, e ele usa o tipo de jogo e o preço escolhidos na lista.

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
