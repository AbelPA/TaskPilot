# Validação rápida: Estudo de gramática inglesa

## Pré-requisitos

- .NET 10 SDK e dependências restauráveis do projeto.
- Node.js compatível com o Angular 21 e `npm`.
- PostgreSQL da configuração local do TaskPilot para validação real de migrations e API.
- Para testes de aplicação, SQLite é fornecido pelo projeto xUnit e Vitest roda em ambiente local de testes.

## Restaurar e testar API

Na raiz do repositório:

```sh
dotnet test apps/api.tests/AudioApi.Tests.csproj
```

Resultado esperado: testes de domínio, validação de conteúdo, correção, serialização e endpoints terminam sem falhas. A suíte de gramática deve incluir testes para lacunas múltiplas, normalização, itens desconhecidos, alternativas e ausência de respostas corretas nos payloads de resolução.

## Construir e testar web

```sh
cd apps/web
npm ci
npm test -- --watch=false
npm run build
```

Resultado esperado: testes Angular passam e a compilação de produção conclui. Verificar navegação de tópicos, teoria, preenchimento, seleção, feedback acessível, navegação anterior/próximo e tentativa novamente.

## Validar o fluxo integrado

1. Iniciar a stack local seguindo o README e configurar a conexão PostgreSQL existente.
2. Iniciar a API; a migration de gramática deve criar seu esquema/tabelas e inserir conteúdo inicial sem alterar o histórico de áudio.
3. Consultar `GET /api/grammar/topics`; conferir tópicos ativos, ordem e contagem de exercícios.
4. Consultar um tópico e verificar blocos teóricos ordenados, incluindo exemplos e tabela.
5. Consultar um exercício de lacunas; confirmar que campos/opções são entregues, mas que nenhum atributo de resposta correta está presente.
6. Enviar respostas válidas em `POST /api/grammar/exercises/{id}/submit`; confirmar correção individual, explicações, pontuação máxima e percentual.
7. Enviar chave desconhecida, duplicada ou pertencente a outro exercício; confirmar resposta 400 e ausência de dados de correção indevidos.
8. Consultar um exercício de múltipla escolha, responder questão correta/incorreta e confirmar resultados individuais.
9. No navegador, percorrer o fluxo completo e confirmar que respostas persistem ao avançar/voltar e são apagadas apenas ao tentar novamente.

## Contratos e modelo

- [Contrato HTTP](./contracts/grammar.openapi.yaml)
- [Modelo de dados](./data-model.md)
- [Especificação](./spec.md)
