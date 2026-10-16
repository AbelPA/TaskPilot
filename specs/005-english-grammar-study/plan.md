# Plano de implementação: Estudo de gramática inglesa

**Branch**: `agents/english-grammar-study` | **Data**: 2026-10-16 | **Spec**: [spec.md](./spec.md)

**Entrada**: Especificação de `specs/005-english-grammar-study/spec.md`.

## Resumo

Adicionar uma experiência de estudo com catálogo de tópicos, teoria estruturada e exercícios de lacunas/múltipla escolha, com correção confiável, pontuação e navegação que preserva as respostas da sessão. A funcionalidade será um módulo isolado no API ASP.NET Core existente e uma área Angular acessível pela navegação principal. O conteúdo inicial será incluído por migração de dados; o catálogo será lido dinamicamente para que conteúdos futuros possam ser adicionados ao armazenamento sem alterações no código da interface.

## Contexto técnico

**Linguagem/versão**: C# / .NET 10; TypeScript 5.9 e Angular 21.

**Dependências principais**: ASP.NET Core Minimal APIs, Entity Framework Core 10, Npgsql, PostgreSQL; Angular standalone, Router, HttpClient, signals e Reactive Forms.

**Armazenamento**: PostgreSQL 17.6 já usado pelo serviço; contexto e tabelas próprios para gramática, no mesmo banco.

**Testes**: xUnit + SQLite em `apps/api.tests`; Vitest/Angular CLI em `apps/web`.

**Plataforma alvo**: Web responsiva, API ASP.NET Core.

**Tipo de projeto**: Monorepo com SPA Angular e API .NET.

**Metas de desempenho**: Consulta de catálogo/teoria/exercícios deve carregar em uma única requisição por tela, evitando chamadas por lacuna; não introduzir limite artificial de tópicos ou itens.

**Restrições**: Não revelar respostas ou critérios antes da submissão; validar correção no API; conteúdo ativo inválido não pode ser apresentado; manter compatibilidade com as rotas e o fluxo de extração de áudio; sem área administrativa nem histórico permanente no MVP.

**Escala/escopo**: Um módulo de estudo, catálogo com quantidade variável de tópicos, teorias em seções e exercícios paginados/ordenados por tópico; sem limite de lacunas por exercício.

## Verificação de constituição

O arquivo `.specify/memory/constitution.md` contém somente marcadores do template (`[PRINCIPLE_*]`, `[SECTION_*]`), sem princípios ratificados. Portanto, não há gates constitucionais específicos a aplicar. A proposta segue as convenções verificadas no repositório: funcionalidades isoladas, Minimal APIs, EF Core, componentes Angular standalone, validação explícita e testes de unidade/integração. Nenhuma exceção arquitetural requer justificativa adicional.

## Estrutura do projeto

```text
apps/api/Grammar/
├── Contracts/
├── Endpoints/
├── Persistence/
│   ├── Entities/
│   └── Migrations/
├── Services/
└── Validation/
apps/api.tests/Grammar/
apps/web/src/app/features/grammar/
├── grammar-api.service.ts
├── grammar-session.store.ts
├── topics-page.*
├── theory-page.*
└── practice-page.*
specs/005-english-grammar-study/
├── contracts/grammar.openapi.yaml
├── data-model.md
├── quickstart.md
├── research.md
├── plan.md
└── tasks.md
```

**Decisão de estrutura**: manter lógica, contratos e persistência em `apps/api/Grammar`, com testes no projeto xUnit existente; compor as telas e o estado de sessão sob `apps/web/src/app/features/grammar`, adicionar rotas lazy e ligação de navegação no shell Angular existente. Não criar novo projeto, serviço ou banco.

## Decisões de arquitetura

1. **Persistência isolada do módulo**: criar `GrammarDbContext` e migrations próprios usando a conexão PostgreSQL já configurada, com uma tabela de histórico de migrations exclusiva para evitar colisão com o histórico do contexto de áudio. Aplicar a migration no arranque com configuração própria, em vez de acoplar tabelas de gramática ao `AudioExtractionDbContext`.
2. **Conteúdo teórico seguro e estruturado**: guardar secções em JSON com blocos tipados (texto, lista, exemplo, observação e tabela); renderizar os tipos em templates Angular, sem HTML arbitrário nem dependência de parser Markdown.
3. **Modelo de exercícios**: usar exercício como contexto comum, questões vinculadas para múltipla escolha, lacunas ordenadas para completar texto e opções reutilizáveis que referenciam uma questão ou lacuna do mesmo exercício. Respostas corretas ficam em relações separadas e só são consultadas pelo serviço de submissão.
4. **Validação de conteúdo ativo**: combinar constraints relacionais/índices e validação de domínio de marcadores, respostas e alternativas antes de apresentar o exercício; falhas são registradas e retornadas explicitamente, não transformadas em exercício utilizável.
5. **Estado do estudante**: manter respostas e resultados numa store Angular por sessão de estudo, indexada pelo exercício; não persistir dados pessoais nem adicionar dependência de autenticação.
6. **Conteúdo inicial**: migration versionada adiciona ao menos um tópico de exemplo, teoria e exercícios dos dois tipos. Outros conteúdos podem ser incluídos diretamente no armazenamento e são listados sem alterar componentes.

## Riscos e mitigação

- **Contexto de migration paralelo**: separar o histórico do contexto de gramática e testar modelo/migration para PostgreSQL e consultas de integração em SQLite; iniciar migração de ambos os contextos apenas quando a configuração correspondente estiver habilitada.
- **Conteúdo inválido inserido diretamente**: validar marcadores, ordem e dependências ao ler exercícios ativos; retornar erro explícito e cobrir os casos com testes.
- **Exposição acidental de respostas**: DTOs de resolução distintos dos modelos de correção; teste de contrato confirma que payloads GET não contêm campos de resposta correta.
- **Renderização de teoria não confiável**: aceitar apenas blocos tipados e renderizar texto por interpolação/elementos Angular, nunca por HTML dinâmico.
- **Acessibilidade**: usar elementos nativos, rótulos ligados aos campos, foco/feedback textual e testes de template para resultados acessíveis.

## Constituição após o desenho

**Resultado**: aprovado. A solução preserva o escopo do monorepo, usa os projetos existentes, não inclui princípios ratificados conflitantes, separa o conteúdo da UI e cobre validação e segurança no desenho. O estado e o desempenho persistentes foram excluídos conforme a especificação.
