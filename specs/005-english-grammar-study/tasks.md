# Tarefas: Estudo de gramática inglesa

**Entrada**: Documentos de desenho em `specs/005-english-grammar-study/`.

**Pré-requisitos**: `plan.md`, `spec.md`, `research.md`, `data-model.md` e `contracts/grammar.openapi.yaml`.

**Testes**: Incluídos porque os cenários e critérios de aceitação da especificação exigem validação automatizada de contratos, integridade, correção e navegação.

## Fase 1: Preparação

**Objetivo**: Preparar configuração e pontos de entrada sem alterar o fluxo existente de áudio.

- [X] T001 Adicionar opções de inicialização do módulo de gramática em `apps/api/Grammar/Configuration/GrammarOptions.cs` e seus valores padrão em `apps/api/appsettings.json`.
- [X] T002 Criar os limites de módulo e convenções de pastas sob `apps/api/Grammar/`, `apps/api.tests/Grammar/` e `apps/web/src/app/features/grammar/` conforme `specs/005-english-grammar-study/plan.md`.

## Fase 2: Fundação (bloqueia todas as histórias)

**Objetivo**: Modelar conteúdo e habilitar persistência/migrações independentes antes das telas e contratos.

- [X] T003 Criar entidades de tópico, seção teórica, exercício, relação exercício-tópico, lacuna, resposta aceita, questão e opção em `apps/api/Grammar/Persistence/Entities/`; respeitar literalmente as regras de `specs/005-english-grammar-study/data-model.md`, incluindo `slug` obrigatório e único, nível CEFR obrigatório restrito a `A1`, `A2`, `B1`, `B2`, `C1` ou `C2`, chaves únicas por exercício, pesos maiores que zero, respostas não vazias e exatamente um vínculo entre opção e questão/lacuna.
- [X] T004 Configurar `GrammarDbContext` em `apps/api/Grammar/Persistence/GrammarDbContext.cs` com FKs restritas, índices de ordenação e unicidade, chaves compostas que garantam opção e lacuna/questão no mesmo exercício e constraints para os valores permitidos.
- [X] T005 Registrar conexão, contexto, serviços e aplicação de migration do módulo em `apps/api/Grammar/Configuration/GrammarServiceRegistration.cs`, `apps/api/Grammar/Persistence/GrammarDesignTimeDbContextFactory.cs` e `apps/api/Program.cs`; manter o histórico de migrations separado do contexto de áudio.
- [X] T006 Criar migration PostgreSQL e conteúdo inicial em `apps/api/Grammar/Persistence/Migrations/`; incluir um tópico de exemplo, seções teóricas com blocos tipados e exercícios válidos de lacunas e múltipla escolha sem tornar o conteúdo dependente de constantes na UI.
- [X] T007 Adicionar testes de mapeamento e integridade da persistência em `apps/api.tests/Grammar/GrammarDbContextTests.cs`, cobrindo unicidade/relacionamentos, conteúdo JSON tipado e separação dos dados do contexto de áudio.

**Checkpoint**: Modelo e persistência prontos; US1 e US2 podem avançar em paralelo sem compartilhar arquivos de lógica de domínio.

## Fase 3: História de usuário 1 — Encontrar um tópico e estudar a teoria (P1)

**Objetivo**: Listar conteúdo ativo e apresentar a teoria em sua ordem cadastrada.

**Teste independente**: Consultar tópicos ativos e abrir um tópico seeded; verificar metadados, contagem de exercícios, teoria ordenada e navegação de tópico sem respostas de exercícios.

### Testes da US1

- [X] T008 [P] [US1] Escrever testes de contrato focados nos endpoints de listagem e detalhe em `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs`, cobrindo tópicos inativos, ordenação, contagem e seções tipadas; reservar navegação 404 e contrato de conteúdo recém-cadastrado para T016.
- [X] T009 [P] [US1] Criar testes unitários Angular focados nos estados vazio/erro da lista e na renderização segura dos blocos teóricos em `apps/web/src/app/features/grammar/topics-page.spec.ts` e `apps/web/src/app/features/grammar/theory-page.spec.ts`; reservar o fluxo completo de seleção e número de ações para T016.

### Implementação da US1

- [X] T010 [P] [US1] Criar DTOs públicos e serviço de catálogo em `apps/api/Grammar/Contracts/GrammarTopicResponse.cs`, `apps/api/Grammar/Contracts/GrammarTheoryBlock.cs` e `apps/api/Grammar/Services/GrammarCatalogService.cs`; retornar apenas tópicos/seções ativos, ordenados, e contar exercícios ativos associados.
- [X] T011 [US1] Implementar endpoints `GET /api/grammar/topics` e `GET /api/grammar/topics/{slug}` em `apps/api/Grammar/Endpoints/GrammarEndpoints.cs`, incluindo estados 404 explícitos e tópicos anterior/seguinte, e mapear o grupo em `apps/api/Program.cs`.
- [X] T012 [P] [US1] Criar contratos TypeScript e `GrammarApiService` para consulta de catálogo e teoria em `apps/web/src/app/features/grammar/grammar-api.service.ts`; usar HttpClient tipado e as rotas definidas em `specs/005-english-grammar-study/contracts/grammar.openapi.yaml`.
- [X] T013 [US1] Implementar a lista de tópicos em `apps/web/src/app/features/grammar/topics-page.ts`, `topics-page.html` e `topics-page.scss`, exibindo título, descrição, nível e contagem e tratando estados de carregamento, vazio e falha.
- [X] T014 [US1] Implementar teoria e blocos tipados em `apps/web/src/app/features/grammar/theory-page.ts`, `theory-page.html` e `theory-page.scss`; suportar parágrafos, listas, exemplos/traduções, notas e tabelas usando bindings seguros, sem HTML dinâmico.
- [X] T015 [US1] Adicionar rotas lazy `/grammar`, `/grammar/:slug` e ligação de navegação no shell em `apps/web/src/app/app.routes.ts` e `apps/web/src/app/app.html`, mantendo o redirecionamento e a experiência existentes de extração de áudio.
- [X] T016 [US1] Executar a suíte criada em T008-T009 e adicionar somente cenários de integração ainda não cobertos: navegação com tópico inexistente/inativo e inclusão de tópico/seção/exercício no banco sem alteração da UI em `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs`, e percurso lista-teoria-início da prática em no máximo três seleções em `apps/web/src/app/app.spec.ts`.

**Checkpoint**: Estudante consegue encontrar um tópico e ler a teoria sem precisar da prática implementada.

## Fase 4: História de usuário 2 — Responder e corrigir exercícios (P1)

**Objetivo**: Resolver exercícios de lacunas e múltipla escolha, com correção e pontuação confiáveis.

**Teste independente**: Consultar exercício sem respostas corretas, enviar acertos/erros em lacunas e questões, conferir respostas esperadas, explicações e percentuais; rejeitar chaves desconhecidas ou duplicadas.

### Testes da US2

- [X] T017 [P] [US2] Escrever testes de contrato da API em `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs` para GET sem gabarito/explicações, payloads válidos de lacunas e múltipla escolha, chaves desconhecidas/duplicadas e erros 400; reservar os casos de pontuação detalhada e respostas alternativas para T023.
- [X] T018 [P] [US2] Criar testes unitários de apresentação Angular em `apps/web/src/app/features/grammar/practice-page.spec.ts` para interpolação de múltiplas lacunas em texto com parágrafos e campos text/select; reservar feedback integrado após submissão, falhas de rede e estado de progresso para T023.

### Implementação da US2

- [X] T019 [P] [US2] Implementar validação de conteúdo ativo e parsing exato dos marcadores em `apps/api/Grammar/Validation/ExerciseContentValidator.cs`; exigir correspondência um-para-um entre marcadores e lacunas e reportar explicitamente marcador órfão/repetido, resposta ausente ou alternativas inválidas.
- [X] T020 [US2] Implementar contratos de resolução e correção em `apps/api/Grammar/Contracts/GrammarExerciseResponse.cs`, `GrammarExerciseSubmission.cs`, `GrammarExerciseResult.cs` e `apps/api/Grammar/Services/GrammarExerciseService.cs`; remover espaços externos, aplicar sensibilidade configurada, pontuar cada item, usar explicação específica ou geral e nunca expor gabarito no DTO de resolução.
- [X] T021 [US2] Completar `GET /api/grammar/topics/{slug}/exercises`, `GET /api/grammar/exercises/{exerciseId}` e `POST /api/grammar/exercises/{exerciseId}/submit` em `apps/api/Grammar/Endpoints/GrammarEndpoints.cs`; validar todos os identificadores no exercício solicitado e retornar erros explícitos para conteúdo inválido e submissões inválidas.
- [X] T022 [US2] Criar a tela de prática e renderização inline de marcadores em `apps/web/src/app/features/grammar/practice-page.ts`, `practice-page.html` e `practice-page.scss`; suportar campos de texto/seleção, perguntas, verificar respostas, gabarito/explicação após envio e feedback não dependente apenas de cores.
- [X] T023 [US2] Executar os testes de T017-T018 e acrescentar cenários de integração distintos em `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs` e `apps/web/src/app/features/grammar/practice-page.spec.ts`: correção e pontuação por item, respostas alternativas e normalização no backend; feedback após envio, falha de rede e progresso da prática no frontend.

**Checkpoint**: Prática validada ponta a ponta, com correção feita no servidor e sem vazamento prévio das respostas.

## Fase 5: História de usuário 3 — Navegar, retomar e repetir a prática (P2)

**Objetivo**: Navegar pelo conjunto de exercícios sem perder respostas durante a sessão e recomeçar uma tentativa explicitamente.

**Teste independente**: Preencher exercícios alternados, navegar para frente/atrás, confirmar estado por exercício e então repetir um item para conferir sua limpeza sem afetar os demais.

### Testes da US3

- [X] T024 [P] [US3] Escrever testes unitários da store em `apps/web/src/app/features/grammar/grammar-session.store.spec.ts` para retenção de respostas por ID, isolamento entre exercícios e limpeza apenas do exercício reiniciado; reservar controles de navegação e acessibilidade para T027.

### Implementação da US3

- [X] T025 [US3] Implementar store de sessão em `apps/web/src/app/features/grammar/grammar-session.store.ts`, indexando respostas/resultado pelo identificador do exercício e limpando apenas o exercício ao tentar novamente.
- [X] T026 [US3] Integrar store e controles anterior/próximo/progresso/retorno à teoria/lista em `apps/web/src/app/features/grammar/practice-page.ts` e `practice-page.html`; desabilitar limites da sequência sem descartar respostas.
- [X] T027 [US3] Executar T024 e adicionar testes de integração da tela em `apps/web/src/app/features/grammar/practice-page.spec.ts` para anterior/próximo, limites, rótulos acessíveis, feedback textual correto/incorreto e preservação ao voltar; não repetir testes unitários da store.

**Checkpoint**: Todas as histórias completas; prática e navegação preservam respostas até o fim da sessão.

## Fase 6: Acabamento e validação transversal

**Objetivo**: Confirmar contrato, segurança, build e guia de validação.

- [X] T028 [P] Atualizar e conferir o contrato HTTP em `specs/005-english-grammar-study/contracts/grammar.openapi.yaml` contra os DTOs reais; respostas corretas, explicações gerais/específicas e critérios internos só podem constar no resultado da submissão, nunca na resposta inicial de resolução.
- [X] T029 [P] Atualizar o guia de teste integrado em `specs/005-english-grammar-study/quickstart.md` com os comandos e fluxo efetivamente suportados.
- [X] T030 Executar `dotnet test apps/api.tests/AudioApi.Tests.csproj`, `dotnet build apps/api/api.csproj`, `npm test -- --watch=false` em `apps/web` e `npm run build` em `apps/web`; corrigir falhas desta funcionalidade sem alterar áreas não relacionadas.
- [X] T031 Revisar transversalmente acessibilidade, segurança de conteúdo tipado e respostas de erro em `apps/web/src/app/features/grammar/` e `apps/api/Grammar/`; adicionar em `apps/web/src/app/features/grammar/*.spec.ts` e `apps/api.tests/Grammar/` somente regressões ainda não cobertas por T008-T027, registrando explicitamente qualquer lacuna restante.

**Resultado da validação final**:

- Os 12 testes da API de gramática, os 33 testes web e os builds da API e web passaram.
- A suíte completa da API teve uma falha em `Http_request_duration_metric_uses_route_and_status_attributes` (atributo duplicado `url.scheme`); esse teste passou isoladamente e os outros 87 testes passaram ao excluí-lo. A falha é preexistente e não foi alterada por esta funcionalidade.
- O script PostgreSQL idempotente da migration foi gerado com sucesso. A migration ainda não foi aplicada a uma instância PostgreSQL real.
- Os avisos de vulnerabilidade NuGet existentes não foram alterados por estarem fora do escopo desta feature.

## Dependências e ordem de execução

```text
T001 ─┐
      ├─> T003 -> T004 -> T005 -> T006 -> T007
T002 ─┘                         ├─> US1 (T008-T016)
                                └─> US2 (T017-T023)
US2 -> US3 (T024-T027)
US1 + US2 + US3 -> acabamento (T028-T031)
```

- A fundação é bloqueante para todas as histórias.
- US1 e US2 podem ser implementadas em paralelo depois de T007; os pontos de integração comuns devem ser coordenados antes de alterar `apps/api/Program.cs` e `apps/web/src/app/app.routes.ts`.
- US3 depende da prática de US2 e da teoria/lista de US1.
- T008/T009, T017/T018, T024 e tarefas de documentação/contrato usam arquivos distintos e podem ser preparadas em paralelo com a implementação da respectiva história.

## Oportunidades de paralelização

- Após T007, dividir backend da US1 (`T010-T011`) e frontend da US1 (`T012-T015`) entre especialistas; compartilhar DTO/contrato antes da integração.
- Após T007, os testes de US2 (`T017-T018`) podem ser escritos enquanto a API de catálogo da US1 é implementada.
- Na US2, validação/correção no backend (`T019-T021`) e tela Angular (`T022`) são trabalhos separáveis após fechar o contrato OpenAPI.
- T028 e T029 podem ocorrer em paralelo; T030 e T031 são validação final sequencial após integração.

## Estratégia de implementação

1. Implementar migrations e modelo compartilhado primeiro.
2. Entregar catálogo e teoria como primeiro incremento verificável (MVP de descoberta/estudo).
3. Entregar prática e correção server-side como segundo incremento, com testes de privacidade do gabarito.
4. Entregar sessão/navegação e só então executar validação transversal e atualizar o guia.

**MVP recomendado**: História 1 fornece acesso ao catálogo e teoria; para valor de aprendizagem completo, publicar junto com História 2, prática/correção.
