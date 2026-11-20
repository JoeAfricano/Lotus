# Boas-vindas: abertura, "Conheça o Lotus" e Novidades

Pedido do José (20/11/2026): boas-vindas no estilo do PowerToys (uma página por recurso, animação curta, texto simples e botão para experimentar) e uma página "Novidades" depois de cada atualização, tudo depois da intro em vídeo.

## Fluxo

1. **Intro em vídeo** (`Controls/IntroView`): completa (7,4 s) na primeira abertura e depois de cada atualização; corte curto (2,4 s) nas outras. Clique ou Esc pula. Configurações › "Animação de abertura" desliga.
2. **Primeira vez:** só o guia **Conheça o Lotus** (`Controls/WelcomeTourView`): Bem-vindo, **Seu perfil** (conta Steam ou nome e foto próprios), **Seu visual** (modo, temas prontos e cor de destaque, com prévia ao vivo), Otimizar meu PC, Meus Jogos, Lotus Score, Ferramentas, Backup de saves, Meu diário e Novidades. Perfil e visual são salvos ao fechar o guia. A tela antiga de perfil (`OnboardingView`) ficou só para Configurações › Editar perfil; objetivo e programas recomendados não são perguntados na primeira vez (objetivo fica "Equilibrado").
3. Configurações › "Rever boas-vindas" abre o guia com perfil e visual; "Conheça o Lotus" abre o guia sem eles.
4. **Depois de uma atualização:** abre direto na página **Novidades**, com a seção da versão atual do `CHANGELOG.md` (vai junto com o app; sem internet).
5. Nada abre por baixo da intro: o guia espera a intro terminar (`MainWindow.AfterIntro`).

Configurações › Aparência › "Conheça o Lotus" tem os botões **Ver o guia** e **Novidades**.

## Cada página

- Cena animada desenhada com formas (sem vídeo nem imagens extras), sempre no palco ameixa escuro, com a etiqueta "Ilustração" (não são dados do PC).
- Título, frase simples e três pontos de "Como funciona".
- **Experimentar**: fecha o guia e abre a tela do recurso. Em Novidades, o botão abre a página de versões no GitHub.
- Teclado: setas mudam de página, Esc fecha.

## Leveza

- As cenas usam animações independentes (rodam na composição) em um ciclo de 5,6 s, só enquanto o guia está aberto; ao trocar de página ou fechar, a animação para e os elementos são liberados.
- Com "Reduzir movimento" a cena aparece parada, já no estado final.
- O CHANGELOG é lido só quando o guia abre.

## Quando aparece

`WelcomePlan.Decide` (Core/Welcome/WelcomeTour.cs):

| Situação | Abre |
|---|---|
| Primeira vez (perfil ainda não feito) | guia completo, com conta e visual |
| Versão diferente da última vista (inclui quem atualiza de 1.9.0) | Novidades |
| Mesma versão | nada |
| Início minimizado na bandeja | nada |
| Instância de teste (`--dev-instance`) | nada, a não ser com `--welcome` ou `--whats-new` |

A versão vista (`WelcomeSeenVersion`) é gravada ao fechar o guia. A instância de teste nunca grava.

## Referência

Padrão adaptado do OOBE do [PowerToys](https://github.com/microsoft/PowerToys) (MIT): uma página por módulo com imagem, "como usar", botão para abrir o recurso e a página de notas da versão após atualizar. Nenhum código do PowerToys foi copiado; a implementação é própria do Lotus.

## Não verificado

- Notas de Novidades vêm do CHANGELOG em português também no idioma inglês.
- Fluxo completo de primeira execução e de atualização real só testado com os argumentos de teste.
