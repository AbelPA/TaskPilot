# Quickstart: Learning Statistics and Adaptive Review

## Prerequisites

- .NET 10 SDK and Node.js/npm compatible with repository configuration.
- Restore dependencies using existing lockfiles.
- For manual API execution, PostgreSQL and the existing `ConnectionStrings:AudioExtraction` configuration are required. Grammar migrations use the existing separate `__GrammarMigrationsHistory` table.

## Automated validation

From the repository root, run focused grammar API tests:

```bash
dotnet test apps/api.tests/AudioApi.Tests.csproj --filter FullyQualifiedName~Grammar
```

From `apps/web`, run Angular tests in non-watch mode and build:

```bash
npm test -- --watch=false
npm run build
```

The API suite uses the existing SQLite test setup. Review the generated production migration for PostgreSQL-compatible indexes, constraints, and model snapshot updates.

## End-to-end acceptance scenarios

1. Start the API with configured PostgreSQL and the Angular application using existing run configuration.
2. Submit evaluated answers for at least two grammar topics from existing practice. Refresh statistics and verify counts, percentages, recent performance, and timestamps against evaluated history.
3. Open a topic with no attempts; verify “not yet assessed” and no misleading percentage.
4. Submit with one stable `submissionId`, retry the exact request, and verify one logical submission. Submit with a new ID and verify a new attempt is retained.
5. Start review with at least 10 active eligible exercises; verify 10 unique exercises and priority/maintenance/exploration metadata. Repeat with fewer than 10 and verify all unique exercises plus the shorter-session flag.
6. Complete only some review exercises and request the summary. Verify only evaluated answers count, topic totals reconcile, priority explanations are evidence-based, and prior comparison appears only with sufficient history.
7. Force persistence failure during submission; verify an API error, answers retained for retry, and no success/persisted indication.
8. Confirm `/grammar/:slug/practice` still works without Review and that navigation is keyboard accessible and passes axe/WCAG AA checks.

Contract shapes and status behavior are defined in [contracts/learning-statistics-review.openapi.yaml](./contracts/learning-statistics-review.openapi.yaml); entity and aggregation semantics are in [data-model.md](./data-model.md).
