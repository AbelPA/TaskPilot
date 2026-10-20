# Implementation Plan: Learning Statistics and Adaptive Review

**Branch**: `006-learning-statistics-adaptive-review` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-learning-statistics-adaptive-review/spec.md`.

## Summary

Persist immutable records of each evaluated answer and the submission that groups them, then derive per-topic statistics, review priorities, and session summaries from that history. Extend the existing ASP.NET Core grammar API and PostgreSQL/EF Core module; add statistics, review, and session-result flows to the existing Angular grammar feature. Reuse the current server-side evaluator and exercise-browsing flow. A stable client submission ID makes retries idempotent, while a new ID represents a deliberate attempt.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5.9 / Angular 21

**Primary Dependencies**: ASP.NET Core minimal APIs, Entity Framework Core 10, Npgsql, Angular standalone components, RxJS

**Storage**: PostgreSQL via `GrammarDbContext`; SQLite-backed API tests

**Testing**: xUnit API tests (`apps/api.tests`); Angular tests via the configured Angular runner (`apps/web`); API integration tests exercise grammar endpoints and persistence

**Target Platform**: Existing TaskPilot web application and ASP.NET Core API

**Project Type**: Web application with backend API

**Performance Goals**: Review generation returns at most 10 unique exercises; statistics and summaries use indexed history projections rather than loading answer payloads into memory

**Constraints**: Preserve existing practice behavior; save evaluated results transactionally before reporting success; support a single-learner context until identity is available; maintain history when catalog exercises are deactivated; make selection reproducible in tests

**Scale/Scope**: Grammar feature only; the topic catalog and each review session are bounded by active content, while append-only response history may grow over time. The specification sets no numeric production scale or latency SLA.

## Constitution Check

The checked-in constitution is still the unfilled Spec Kit template (placeholder principle names and descriptions), so it defines no enforceable project-specific gates. No constitution violation can be established from that file. This design follows the repository patterns found in source: grammar API and EF Core module, additive migrations, focused services/contracts, Angular feature components with signals and accessible navigation, and tests in existing project layouts.

**Gate result**: PASS - no actionable constitution rules are ratified; repository conventions are followed.

## Project Structure

### Documentation (this feature)

```text
specs/006-learning-statistics-adaptive-review/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
`-- contracts/
    `-- learning-statistics-review.openapi.yaml
```

### Source Code (repository root)

```text
apps/api/Grammar/
|-- Contracts/                  # Statistics, review-session, attempt, and summary DTOs
|-- Endpoints/                  # Statistics and review-session routes
|-- Persistence/
|   |-- Entities/               # Submission/answer history and review sessions
|   `-- Migrations/             # Additive grammar-history migration
`-- Services/                   # Persistence, aggregation, and adaptive selection

apps/api.tests/Grammar/         # Service, endpoint, idempotency, and persistence tests

apps/web/src/app/
|-- app.routes.ts                # Lazy statistics and review routes
|-- app.html                     # Statistics/review navigation
`-- features/grammar/
    |-- grammar-api.service.ts   # New API contracts and calls
    |-- grammar-session.store.ts # Submission identity and review state
    |-- statistics-page.*        # Per-topic statistics
    `-- review-page.*            # Session generation, practice, and summary
```

**Structure Decision**: Extend the existing `apps/api/Grammar` bounded feature and `apps/web/src/app/features/grammar`; no new deployable project or shared library is warranted. Exact filenames can follow existing module naming during implementation.

## Complexity Tracking

No constitution violations or additional architectural projects are proposed. Append-only answer history, a submission idempotency key, and persisted review-session selection are directly required to preserve trustworthy statistics, avoid duplicate attempts, and compare session results with prior history.

## Post-Design Constitution Check

PASS - the design remains within existing API, database, web application, and test projects. It introduces no external service or runtime dependency. The repository constitution remains a placeholder and supplies no additional gates.
