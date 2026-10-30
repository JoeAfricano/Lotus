# Histórico de versões

Todas as versões estão em [Releases](../../releases). Para atualizar, rode o instalador novo por cima do atual: seus jogos, diário, conquistas e configurações continuam como estão.

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
- "Seu visual" ganhou **6 temas prontos** aplicados ao vivo, além dos modos e cores. Com um tema pronto escolhido, a escolha de cor some, porque o tema já tem as próprias cores.
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
