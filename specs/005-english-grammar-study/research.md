# Pesquisa: Estudo de gramática inglesa

## Decisão: módulo de API com Minimal APIs

**Decisão**: adicionar `Grammar` como módulo do API existente, com endpoints agrupados e serviços de domínio injetados.

**Justificativa**: a API atual já organiza recursos por módulo e declara endpoints em extensões `Map...Endpoints`; evita um novo serviço e preserva o contrato de implantação.

**Alternativas consideradas**: criar um serviço separado (desnecessário para o escopo) ou implementar a correção somente no navegador (contraria a integridade e confidencialidade exigidas).

## Decisão: DbContext específico no PostgreSQL existente

**Decisão**: criar `GrammarDbContext`, migrations e histórico de migrations próprios na mesma conexão usada atualmente. O carregamento de migrations de gramática recebe configuração própria, com padrão habilitado.

**Justificativa**: mantém as entidades de estudo separadas das entidades de áudio e reaproveita o banco e a infraestrutura operacional existente. Histórico próprio evita que contextos EF concorrentes compartilhem indevidamente `__EFMigrationsHistory`.

**Alternativas consideradas**: colocar as entidades no `AudioExtractionDbContext` (mistura domínios e migrations) ou criar banco/contexto operacional separado (duplicação de configuração e implantação).

## Decisão: blocos de conteúdo teórico tipados

**Decisão**: persistir cada seção como documento JSON validado contendo blocos de parágrafo, lista, exemplo, nota e tabela; usar DTOs discriminados e templates Angular para exibição.

**Justificativa**: satisfaz os formatos pedidos e permite renderização de tabelas e listas sem executar HTML/JavaScript ou introduzir um parser de Markdown inexistente no projeto.

**Alternativas consideradas**: Markdown armazenado no banco (precisaria de parser e sanitização em outra camada) ou HTML livre (risco de conteúdo ativo e risco de XSS).

## Decisão: texto-modelo com marcador por lacuna

**Decisão**: persistir o texto integral e marcadores `{{blank:key}}`; conferir que o conjunto ordenado de marcadores seja idêntico ao conjunto das lacunas do exercício, rejeitando marcador repetido, ausente, órfão ou inválido.

**Justificativa**: preserva parágrafos e pontuação e permite quantas lacunas forem necessárias sem colunas específicas por frase.

**Alternativas consideradas**: campos por frase/parágrafo (limita o tamanho e replica conteúdo) ou sublinhados sem identificador (ambíguos para associar respostas).

## Decisão: correção determinística no servidor

**Decisão**: comparar respostas aceitas após remoção de espaços externos e normalização de maiúsculas/minúsculas configurável; retornar resultados por chave e calcular pontuação por item com pesos.

**Justificativa**: mantém regras de correção consistentes e impede que o navegador determine a própria pontuação.

**Alternativas consideradas**: comparação por substring/fuzzy (aceita respostas não cadastradas) ou correção local (expõe regras e respostas).

## Decisão: opções com referência tipada

**Decisão**: compartilhar alternativas por tabela, com referência exclusiva a uma lacuna ou questão e chave estrangeira composta que garanta pertencimento ao mesmo exercício.

**Justificativa**: permite lista de seleção por lacuna e múltipla escolha tradicional sem duplicar o modelo, preservando integridade relacional.

**Alternativas consideradas**: arrays JSON sem integridade referencial ou tabelas distintas para as mesmas alternativas e validações.

## Decisão: estado de navegação só na sessão

**Decisão**: store Angular por sessão mantém respostas enquanto a pessoa avança/volta, limpando resultado e respostas apenas em “tentar novamente”.

**Justificativa**: cumpre preservação de progresso sem adicionar conta, retenção de dados ou persistência além do MVP.

**Alternativas consideradas**: armazenamento permanente local ou remoto, explicitamente fora do escopo; estado local isolado por componente, que perde respostas ao trocar de rota.
