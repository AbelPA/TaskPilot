# Modelo de dados: Estudo de gramática inglesa

Os identificadores primários são UUIDs. Todo conteúdo tem timestamps quando apropriado e chaves estrangeiras explícitas. Remoções de conteúdo publicado são restritas; conteúdo deixa de ser visível por desativação, evitando referências órfãs.

## `grammar_topics`

| Campo | Tipo conceitual | Regras |
|---|---|---|
| `id` | UUID | Chave primária |
| `slug` | texto curto | Obrigatório, único, adequado a URL |
| `title` | texto curto | Obrigatório |
| `description` | texto | Obrigatório |
| `level` | enum/texto | Nível CEFR obrigatório: `A1`, `A2`, `B1`, `B2`, `C1` ou `C2` |
| `display_order` | inteiro | Obrigatório, padrão 0 |
| `is_active` | booleano | Obrigatório, padrão falso ao criar |
| timestamps | instante | criação e última atualização |

## `grammar_theory_sections`

| Campo | Tipo conceitual | Regras |
|---|---|---|
| `id` | UUID | Chave primária |
| `topic_id` | UUID | FK para tópico; exclusão restrita |
| `title` | texto curto | Obrigatório |
| `content_json` | JSON | Lista validada de blocos tipados; não permite HTML arbitrário |
| `display_order` | inteiro | Obrigatório; único por tópico |

Blocos suportados: `paragraph`, `list`, `example` (texto e tradução opcional), `note` e `table` (cabeçalho e linhas). Blocos desconhecidos ou inválidos impedem a seção de ser apresentada.

## `exercises` e `exercise_topics`

`exercises` contém `id`, `title`, `instruction`, `exercise_type` (`fill_blank` ou `multiple_choice`), `content_template` (texto completo para lacunas ou contexto comum para questões), `explanation`, `case_sensitive`, `display_order`, `is_active` e timestamps. Um exercício ativo requer pelo menos um tópico.

`exercise_topics` é a relação muitos-para-muitos, com PK composta `(exercise_id, topic_id)`, FKs restritas e índice reverso por tópico/ordem. Múltiplos tópicos significam revisão explícita.

## `exercise_blanks`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | UUID | Chave primária |
| `exercise_id` | UUID | FK e componente de chave alternativa |
| `blank_key` | texto curto | Obrigatório, único por exercício, corresponde a um marcador |
| `display_order` | inteiro | Obrigatório, único por exercício |
| `input_type` | enum | `text` ou `select` |
| `points` | decimal | Maior que zero; padrão 1 |

A combinação `(exercise_id, id)` é chave alternativa para FKs compostas das opções.

## `exercise_blank_answers`

Campos `id`, `blank_id`, `answer`, `is_primary` e `explanation` opcional. Uma lacuna tem uma ou mais respostas não vazias, valores normalizados únicos segundo a configuração do exercício e, no máximo, uma resposta principal. FK para lacuna com exclusão restrita.

## `exercise_questions`

Campos `id`, `exercise_id`, `question_key`, `prompt`, `display_order` e `points`. `question_key` e `display_order` são únicos no exercício; pontos maiores que zero. Um exercício de múltipla escolha ativo tem uma ou mais questões.

## `exercise_options`

Campos `id`, `exercise_id`, `question_id` anulável, `blank_id` anulável, `option_key`, `option_text`, `is_correct`, `explanation` opcional e `display_order`.

- Exatamente um entre `question_id` e `blank_id` deve estar preenchido.
- FK composta `(exercise_id, question_id)` garante questão do mesmo exercício; FK composta equivalente garante lacuna do mesmo exercício.
- `option_key` e `display_order` são únicos dentro da questão/lacuna.
- Cada questão de múltipla escolha tem pelo menos duas opções e exatamente uma correta; opção correta de lacuna deve estar entre as respostas válidas cadastradas.
- O DTO entregue para resolução exclui `is_correct`, respostas aceitas e explicações de correção.

## Regras de consistência e validação

1. Um exercício ativo deve estar relacionado a ao menos um tópico ativo.
2. O nível de cada tópico deve ser um dos seis níveis CEFR: `A1`, `A2`, `B1`, `B2`, `C1` ou `C2`.
3. Exercício `fill_blank` requer template não vazio, lacunas ordenadas, um marcador único para cada lacuna e nenhuma referência órfã; cada lacuna requer resposta aceita.
4. Lacuna `select` requer duas ou mais opções; lacuna `text` não retorna opções.
5. Exercício `multiple_choice` requer uma ou mais questões, cada uma com pelo menos duas opções e exatamente uma correta.
6. Um conteúdo marcado ativo que viole regras de estrutura não é servido ao estudante; erro é registrado e comunicado sem sucesso disfarçado.
7. Correção aceita respostas vazias como incorretas, compara após `trim`, e usa comparação ordinal com ou sem `ToUpperInvariant` de acordo com `case_sensitive`.
8. As chaves de resposta submetidas devem ser únicas e existir dentro do exercício solicitado; referência desconhecida ou duplicada é erro de validação.
9. Peso total maior que zero; pontuação é a soma dos itens corretos e percentual = `round(score / max_score * 100, 1)`.

## Relações

```text
grammar_topics 1 ── N grammar_theory_sections
grammar_topics N ── N exercises (exercise_topics)
exercises 1 ── N exercise_blanks 1 ── N exercise_blank_answers
exercises 1 ── N exercise_questions 1 ── N exercise_options
exercises 1 ── N exercise_options (via blank_id para opções de lacuna)
```
