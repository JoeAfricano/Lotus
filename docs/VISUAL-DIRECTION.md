# Direção visual — Neon japonês

A referência fornecida pelo usuário em 21/10/2026 orienta a identidade visual. Textos, números e alegações de desempenho da imagem não são requisitos funcionais.

## Primeira aplicação

- Preto profundo e grafite, rosa neon e rosa claro para textos de destaque.
- Barra de título integrada, navegação lateral, marca vetorial e área visual de perfil.
- Dashboard com saudação, painel ilustrado em XAML com lua, montanhas, pagode e pétalas.
- Cards de score e sistema reservam espaço para os módulos futuros. Exibem “não avaliado” e “sem leitura”, sem dados simulados.
- Cards compartilhados, hierarquia tipográfica e bordas finas com destaque rosa.
- As demais seções mantêm título próprio, descrição e indicação de funcionalidade planejada.

A ilustração é vetorial e faz parte do XAML. Não há dependência do PNG de referência em Downloads.

## Validação desta entrega

Compilação Debug/x64 sem erros ou avisos; seis testes existentes aprovados. Dashboard inspecionado na janela real. Navegação Dashboard → Logs → Dashboard verificada por interação com os controles nativos. A instância de validação foi fechada ao concluir para liberar o executável.

Há regras adaptativas para empilhar os cards em janelas menores. Redimensionamento, escalas de DPI e leitor de tela ainda precisam de uma validação dedicada; a tentativa de redimensionamento nesta inspeção não alterou o tamanho da janela e não foi contabilizada como aprovação.

Próximos refinamentos visuais serão guiados pelas instruções do usuário. Nenhum módulo de monitoramento ou otimização foi implementado nesta alteração.

## Atualização Etapa 01

Dois temas escuros e idiomas mudam ao selecionar. Marca Lotus com pulsação discreta de opacidade/escala (1 a 1,015; 4,4 s), entrada de páginas com fade e deslocamento curto. Reduzir animações desativa os efeitos. Sem timer de aplicação por frame.

Conteúdo centralizado em telas amplas, cards em coluna nas estreitas, rodapé compacto sem texto cortado. Recorte após maximizar/restaurar reproduzido e corrigido retirando largura vinculada e centralização do conteúdo do ScrollViewer. Inspecionados tamanho inicial, estreito, mínimo 600 × 480 e maximizado no monitor local. Outras escalas e avaliação subjetiva da fluidez pendentes. [Resultados](LOTUS_STAGE01_VALIDATION.md).
