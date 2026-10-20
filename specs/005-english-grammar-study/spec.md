# Especificação de funcionalidade: Estudo de gramática inglesa

**Branch da funcionalidade**: `agents/english-grammar-study`

**Criada em**: 2026-10-16

**Status**: Rascunho

**Entrada**: Requisitos do anexo “Página de estudo de gramática inglesa”.

## Cenários de usuário e testes

### História 1 — Encontrar um tópico e estudar a teoria (Prioridade: P1)

Como estudante, quero ver os tópicos de gramática disponíveis e abrir a teoria de um tópico para aprender as regras e consultar exemplos antes de praticar.

**Por que esta prioridade**: A teoria e a descoberta de tópicos são a entrada principal do fluxo e tornam o conteúdo existente acessível.

**Teste independente**: Com tópicos e seções teóricas ativos, abrir a página de tópicos, selecionar um tópico e confirmar que título, descrição, nível, teoria e quantidade de exercícios correspondem ao conteúdo cadastrado.

**Cenários de aceitação**:

1. **Dado** que existem tópicos ativos, **quando** o estudante acessa a área de gramática, **então** vê os tópicos ordenados e cada item apresenta título, descrição, nível e quantidade de exercícios.
2. **Dado** que um tópico tem uma ou mais seções teóricas, **quando** o estudante o abre, **então** as seções são exibidas na ordem cadastrada, com regras, exemplos, traduções opcionais, observações e conteúdo formatado de forma segura.
3. **Dado** que não há tópicos ativos, **quando** o estudante acessa a área, **então** recebe uma mensagem clara de que não há conteúdo disponível.
4. **Dado** um tópico inexistente ou inativo, **quando** o estudante tenta abri-lo, **então** a aplicação apresenta um estado de não encontrado e oferece retorno à lista.

### História 2 — Responder e corrigir exercícios (Prioridade: P1)

Como estudante, quero praticar exercícios de lacunas ou de múltipla escolha vinculados ao tópico que estudei, para receber correção individual e entender meus erros.

**Por que esta prioridade**: A prática e o feedback constituem o resultado de aprendizagem principal da funcionalidade.

**Teste independente**: Selecionar um tópico com exercícios ativos, responder a um exercício de cada tipo e confirmar as respostas, a pontuação, as explicações e que as respostas corretas não foram fornecidas antes da correção.

**Cenários de aceitação**:

1. **Dado** um tópico com exercícios ativos, **quando** o estudante escolhe praticar, **então** vê todos os exercícios relacionados ao tópico em uma lista na mesma página.
2. **Dado** um exercício de lacunas com várias lacunas, **quando** o estudante responde, **então** cada campo aparece no seu marcador dentro do texto original, aceita digitação ou seleção conforme cadastrado e conserva pontuação e ordem próprias.
3. **Dado** um exercício com vários parágrafos, **quando** ele é apresentado, **então** o texto, a pontuação e as quebras de parágrafo permanecem na ordem original.
4. **Dado** respostas enviadas para um exercício de lacunas, **quando** a correção é solicitada, **então** cada lacuna é avaliada no servidor contra todas as respostas aceitas, respeitando a normalização configurada, e o resultado inclui resposta enviada, resposta esperada, acerto/erro e explicação aplicável.
5. **Dado** uma questão de múltipla escolha, **quando** o estudante seleciona uma alternativa e solicita correção, **então** recebe a identificação da alternativa correta e a explicação geral ou específica da alternativa.
6. **Dado** um exercício com várias questões relacionadas ao mesmo contexto, **quando** o estudante envia as respostas, **então** cada questão é corrigida individualmente e contribui para a pontuação.
7. **Dado** vários exercícios exibidos na mesma página, **quando** o estudante solicita a correção, **então** recebe o resultado individual de cada exercício e de cada lacuna/questão respondida.
8. **Dado** um exercício já apresentado para resolução, **quando** o estudante consulta os dados antes de enviar respostas, **então** os dados não revelam respostas corretas nem critérios internos de correção.
9. **Dado** um envio com lacuna/questão desconhecida, resposta duplicada ou referência pertencente a outro exercício, **quando** o servidor valida o envio, **então** rejeita a solicitação sem divulgar respostas de outro exercício.

### História 3 — Navegar, retomar e repetir a prática (Prioridade: P2)

Como estudante, quero avançar, voltar, tentar novamente e retornar à teoria sem perder as respostas da sessão, para controlar meu ritmo de estudo.

**Por que esta prioridade**: A navegação torna a prática utilizável, evita perda acidental de respostas e permite revisar um tópico sem reiniciar o fluxo.

**Teste independente**: Preencher respostas em exercícios consecutivos, navegar para frente e para trás, e verificar a retenção durante a sessão, a reinicialização explícita e os destinos de retorno.

**Cenários de aceitação**:

1. **Dado** mais de um exercício disponível na lista, **quando** o estudante preenche respostas, **então** cada exercício mantém suas respostas independentes durante a sessão.
2. **Dado** os exercícios respondidos, **quando** o estudante escolhe tentar novamente, **então** os campos e resultados de correção são limpos para uma nova tentativa.
3. **Dado** a tela de exercícios, **quando** o estudante escolhe voltar à teoria ou à lista de tópicos, **então** retorna ao destino correspondente.
4. **Dado** o primeiro ou o último exercício, **quando** o estudante chega ao limite da sequência, **então** os controles de navegação indicam corretamente que não há exercício anterior ou seguinte.
5. **Dado** um tópico sem exercícios ativos, **quando** o estudante tenta iniciar a prática, **então** recebe uma mensagem apropriada e pode voltar à teoria ou à lista.

### Casos de borda

- Tópico ativo sem seção teórica ou com conteúdo teórico vazio: apresentar o conteúdo disponível sem erro e indicar quando não houver teoria cadastrada.
- Exercício ativo com marcador sem lacuna correspondente, lacuna órfã, respostas aceitas ausentes, alternativas ausentes ou sem exatamente uma alternativa correta: impedir a ativação e não apresentar o exercício para resolução.
- Marcadores ausentes, duplicados ou fora de ordem: validar a consistência sem truncar ou reordenar texto; não disponibilizar um exercício inválido.
- Resposta vazia, espaços extras, diferenças entre maiúsculas/minúsculas e contrações: aplicar as regras cadastradas; não aceitar variantes não explicitamente válidas.
- Falha de comunicação ou erro do servidor durante consulta/envio: informar o problema sem exibir resultado de sucesso nem descartar respostas ainda mantidas na sessão.
- Conteúdo Markdown com HTML ou scripts: exibir como conteúdo inerte, sem executar marcação ativa.

## Requisitos

### Requisitos funcionais

- **RF-001**: A aplicação DEVE apresentar tópicos gramaticais ativos carregados do conteúdo persistido, sem exigir alterações na aplicação para que novos tópicos ativos sejam listados.
- **RF-002**: Cada tópico DEVE informar título, identificador amigável único, descrição, nível de proficiência no padrão CEFR (`A1`, `A2`, `B1`, `B2`, `C1` ou `C2`), ordem de apresentação e disponibilidade.
- **RF-003**: Cada tópico DEVE poder possuir várias seções de teoria ordenadas, com título, explicação, regras, exemplos, traduções opcionais, observações, listas e tabelas.
- **RF-004**: A aplicação DEVE apresentar a teoria do tópico selecionado e permitir ir ao tópico anterior/seguinte quando disponível.
- **RF-005**: A teoria e os exercícios DEVEM ser conteúdos independentes, relacionados ao tópico, e conteúdo novo DEVE poder ser disponibilizado por cadastro no banco de dados sem alteração de código da interface.
- **RF-006**: A aplicação DEVE apresentar a quantidade de exercícios ativos por tópico e iniciar a prática apenas com exercícios relacionados ao tópico escolhido; exercícios de revisão de vários tópicos DEVEM estar explicitamente associados a todos os tópicos abrangidos.
- **RF-007**: O sistema DEVE suportar exercícios de lacunas e exercícios de múltipla escolha, mantendo o modelo extensível para novos tipos sem alterar o significado dos tipos existentes.
- **RF-008**: Um exercício de lacunas DEVE armazenar o texto completo com marcadores `{{blank:chave}}`, preservando texto, pontuação e parágrafos, e relacionar cada marcador a uma lacuna individual.
- **RF-009**: Cada lacuna DEVE ter chave única no exercício, ordem, tipo de entrada, pontuação e uma ou mais respostas aceitas; a explicação individual da resposta DEVE ser opcional, com uso da explicação geral como alternativa.
- **RF-010**: Cada lacuna DEVE poder aceitar digitação livre ou seleção; opções de seleção DEVEM ser configuradas por exercício ou lacuna, incluir alternativas incorretas quando desejado e nunca revelar por sua ordenação qual alternativa é correta.
- **RF-011**: Exercícios de múltipla escolha DEVEM suportar uma ou mais questões associadas ao mesmo contexto textual; cada questão DEVE ter enunciado, alternativas ordenadas, exatamente uma alternativa correta na primeira versão e explicação geral ou por alternativa.
- **RF-012**: O sistema DEVE validar a integridade do conteúdo antes de ativá-lo: tópico associado, marcadores e lacunas correspondentes, chaves únicas, respostas aceitas presentes, opções suficientes e, para cada questão de múltipla escolha, exatamente uma alternativa correta.
- **RF-013**: A apresentação do exercício para resolução DEVE omitir respostas corretas e critérios de correção; a correção DEVE ocorrer no sistema confiável usando conteúdo persistido e validar que todas as respostas recebidas pertencem ao exercício solicitado.
- **RF-014**: A normalização DEVE remover espaços externos e permitir configurar a sensibilidade a maiúsculas/minúsculas por exercício; contrações e outras variantes textuais DEVEM ser aceitas somente se cadastradas ou cobertas por regra explícita.
- **RF-015**: Após a correção, a aplicação DEVE indicar cada resposta enviada, seu estado correto/incorreto, a resposta aceita/apresentada, a explicação pertinente e a pontuação obtida; o resultado não DEVE depender apenas de cor.
- **RF-016**: A pontuação DEVE ser calculada individualmente por lacuna ou questão, somada no exercício e acompanhada de pontuação máxima e percentual; itens DEVEM ter pesos configuráveis e peso igual por padrão.
- **RF-017**: A interface DEVE apresentar em uma lista todos os exercícios ativos do tópico na mesma página, com instruções, verificar respostas de todos os exercícios, tentar novamente, retorno à teoria e retorno à lista de tópicos.
- **RF-018**: As respostas preenchidas DEVEM permanecer durante a navegação entre exercícios na mesma sessão de estudo; histórico permanente e estatísticas por usuário não fazem parte desta entrega.
- **RF-019**: A apresentação de tópicos, seções teóricas, exercícios e alternativas DEVE respeitar as respectivas ordens cadastradas.
- **RF-020**: Conteúdo teórico formatado DEVE ser apresentado de forma segura, impedindo execução de HTML, scripts ou conteúdo ativo armazenado.
- **RF-021**: Dados recebidos para correção DEVEM ser validados, rejeitando estrutura inválida, itens desconhecidos, referências cruzadas entre exercícios e respostas duplicadas.
- **RF-022**: Se não houver conteúdo disponível, ou se um tópico ou exercício não for encontrado, a aplicação DEVE mostrar estado e orientação de navegação adequados, sem apresentar conteúdo incorreto.

### Entidades principais

- **Tópico gramatical**: Unidade navegável de estudo com título, slug, descrição, nível, ordem e estado ativo.
- **Seção teórica**: Parte ordenada e formatada da teoria de um tópico, incluindo regras, exemplos, traduções e observações.
- **Exercício**: Unidade de prática associada a um ou mais tópicos, com tipo, título, instrução, conteúdo textual, explicação, ordem e estado ativo.
- **Lacuna**: Campo de resposta incorporado ao texto de um exercício, com chave, ordem, tipo de interação e peso.
- **Resposta aceita**: Uma variante válida para uma lacuna, com possibilidade de resposta principal e explicação específica.
- **Questão e alternativa**: Pergunta associada ao contexto de um exercício de múltipla escolha, com alternativas ordenadas, exatamente uma correta e explicações opcionais.
- **Tentativa de resolução**: Respostas submetidas e resultado calculado para a sessão atual; não requer histórico permanente nesta versão.

## Critérios de sucesso

### Resultados mensuráveis

- **CS-001**: 100% dos tópicos ativos cadastrados são apresentados com título, descrição, nível e contagem de exercícios correta.
- **CS-002**: Para todo exercício válido de lacunas ou múltipla escolha do conjunto de testes de aceitação, cada campo/questão recebe resultado independente e a pontuação retornada corresponde aos itens corretos e aos pesos cadastrados.
- **CS-003**: 100% dos testes de consulta inicial confirmam que respostas corretas e explicações de correção não são reveladas antes do envio.
- **CS-004**: 100% dos testes de apresentação de exercícios com múltiplos parágrafos preservam o texto, a pontuação e a ordem das lacunas originais.
- **CS-005**: Em conteúdo válido, um estudante consegue ir da lista de tópicos a uma teoria e iniciar o primeiro exercício em até três ações de seleção.
- **CS-006**: Um novo tópico, seção teórica e exercício válido aparecem na aplicação após sua inclusão no conteúdo persistido, sem alteração de código da interface.
- **CS-007**: 100% dos cadastros inválidos do conjunto de testes de integridade são impedidos de ficar ativos e não podem ser resolvidos por estudantes.

## Premissas

- A primeira versão é uma experiência de estudo para usuários da aplicação, sem área administrativa para criar/editar conteúdo; o conteúdo pode ser incluído inicialmente pelo banco de dados ou por scripts/migrações de dados.
- O conteúdo de teoria é persistido em formato estruturado que permite texto formatado, listas e tabelas; a apresentação não executa conteúdo ativo fornecido pelo banco.
- Cada exercício pertence a pelo menos um tópico; a associação a mais de um tópico existe apenas para exercícios explicitamente configurados como revisão.
- A comparação de respostas ignora espaços externos e é insensível a maiúsculas/minúsculas por padrão, podendo ser configurada por exercício.
- Os níveis dos tópicos seguem os seis níveis comuns do CEFR, de `A1` a `C2`; classificações intermediárias como `Pre-A1` não fazem parte desta primeira versão.
- O peso padrão de cada lacuna ou questão é igual; o resultado de percentual é arredondado a uma casa decimal.
- As respostas de uma sessão são mantidas no cliente enquanto a navegação ocorre na própria sessão; recarga do navegador e armazenamento permanente não são garantidos.
- Navegação de tópicos e exercícios segue a ordem configurada no conteúdo; tópicos sem teoria ou sem exercícios continuam acessíveis com mensagens explícitas.
- Histórico de desempenho, estatísticas, repetição espaçada, dicas, áudio, importação em lote e administração visual estão fora do escopo desta primeira versão.
