# Histórico de versões

Todas as versões estão em [Releases](../../releases). Para atualizar, rode o instalador novo por cima do atual: seus jogos, diário, conquistas e configurações continuam como estão.

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
