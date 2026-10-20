# Tasks: Learning Statistics and Adaptive Review

**Input**: Design documents from `/specs/006-learning-statistics-adaptive-review/`

**Prerequisites**: `plan.md`, `spec.md`, `data-model.md`, `research.md`, `contracts/`, `quickstart.md`

**Tests**: Included because persistence, aggregation, selection, and UX acceptance criteria are measurable and the quickstart requires automated validation.

## Phase 1: Setup

**Purpose**: Use the existing Grammar API and Angular feature projects.

- [X] T001 Confirm the focused API and Angular validation commands in `specs/006-learning-statistics-adaptive-review/quickstart.md` and preserve project configuration in `apps/api.tests/AudioApi.Tests.csproj` and `apps/web/package.json`

## Phase 2: Foundational

**Purpose**: Persist evaluated response history; every story depends on this source of truth.

- [X] T002 [P] Add history persistence tests for immutable submission/answer rows, unique answer keys, and retained history in `apps/api.tests/Grammar/GrammarDbContextTests.cs`
- [X] T003 [P] Add submission idempotency, conflicting-payload, atomic-save, and server-evaluated-result tests in `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs`
- [X] T004 Add submission and evaluated-answer entities with unique IDs/keys and historical snapshots in `apps/api/Grammar/Persistence/Entities/GrammarSubmissionEntity.cs` and `apps/api/Grammar/Persistence/Entities/EvaluatedAnswerEntity.cs`
- [X] T005 Configure indexed, non-cascading history relationships and create the additive migration/model snapshot in `apps/api/Grammar/Persistence/GrammarDbContext.cs` and `apps/api/Grammar/Persistence/Migrations/`
- [X] T006 Extend exercise submission/result contracts with stable `submissionId`, validated topic, optional review session, and persisted attempt result in `apps/api/Grammar/Contracts/GrammarExerciseSubmission.cs` and `apps/api/Grammar/Contracts/GrammarExerciseResult.cs`
- [X] T007 Persist evaluator output and answer snapshots transactionally; identical submission retries return the original result, changed payloads conflict, and save failures remain errors in `apps/api/Grammar/Services/GrammarExerciseService.cs` and `apps/api/Grammar/Endpoints/GrammarEndpoints.cs`

**Checkpoint**: Existing practice records each evaluated logical submission once and surfaces persistence failure.

## Phase 3: User Story 1 - Review performance by topic (Priority: P1)

**Goal**: Present accurate, history-derived statistics for each grammar topic.

**Independent Test**: Submit answers across topics; verify counts, percentages, recent five answers and timestamp against history, zero-history topics are unassessed, and retries do not duplicate attempts.

### Tests

- [X] T008 [P] [US1] Test topic aggregation and empty-topic responses, including latest timestamp, recent five answers, and no divide-by-zero in `apps/api.tests/Grammar/GrammarStatisticsEndpointTests.cs`
- [X] T009 [P] [US1] Test exact aggregation, active topics, and insufficient-evidence behavior in `apps/api.tests/Grammar/GrammarStatisticsServiceTests.cs`
- [X] T010 [P] [US1] Test topic statistics API loading and HTTP errors in `apps/web/src/app/features/grammar/grammar-api.service.spec.ts`
- [X] T011 [P] [US1] Test assessed/unassessed display, percentages, recent progress, and accessible labels in `apps/web/src/app/features/grammar/statistics-page.spec.ts`

### Implementation

- [X] T012 [US1] Define topic statistics DTOs matching the OpenAPI response in `apps/api/Grammar/Contracts/GrammarTopicStatisticsResponse.cs`
- [X] T013 [US1] Aggregate answer history by active topic and compute totals, percentages, timestamp, recent five, and unassessed state in `apps/api/Grammar/Services/GrammarStatisticsService.cs`
- [X] T014 [US1] Add `GET /api/grammar/statistics/topics` and register its service in `apps/api/Grammar/Endpoints/GrammarEndpoints.cs` and `apps/api/Grammar/Configuration/GrammarServiceRegistration.cs`
- [X] T015 [US1] Add typed statistics models and API method in `apps/web/src/app/features/grammar/grammar-api.service.ts`
- [X] T016 [US1] Implement loading, error, assessed, and unassessed topic statistics UI in `apps/web/src/app/features/grammar/statistics-page.ts`, `apps/web/src/app/features/grammar/statistics-page.html`, and `apps/web/src/app/features/grammar/statistics-page.scss`
- [X] T017 [US1] Add lazy statistics route and accessible navigation without changing practice routes in `apps/web/src/app/app.routes.ts` and `apps/web/src/app/app.html`

**Checkpoint**: Learners can view correct topic statistics while existing practice still works.

## Phase 4: User Story 2 - Complete a personalized review (Priority: P1)

**Goal**: Generate a reproducible session of up to 10 unique active exercises balanced across priority, maintenance, and exploration.

**Independent Test**: With controlled history, verify evidence-shrunk 50/30/20 priorities, adaptable 70/20/10 targets, uniqueness, variety, deterministic selection, evidence-based reasons, and short-session behavior.

### Tests

- [X] T018 [P] [US2] Test weighted/shrunk priorities, distributions, topic-level fallback, alternatives, uniqueness, and session limit in `apps/api.tests/Grammar/GrammarReviewServiceTests.cs`
- [X] T019 [P] [US2] Test session creation, persisted snapshots, no history, short sessions, and session/topic attribution in `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs` and `apps/api.tests/Grammar/GrammarReviewSummaryEndpointTests.cs`
- [X] T020 [P] [US2] Test review session API methods and error states in `apps/web/src/app/features/grammar/grammar-api.service.spec.ts`
- [X] T021 [P] [US2] Test review start/practice/navigation, stable retry identity, and short-session messaging in `apps/web/src/app/features/grammar/review-page.spec.ts` and `apps/web/src/app/features/grammar/grammar-session.store.spec.ts`

### Implementation

- [X] T022 [US2] Add review session and selected exercise entities with unique `(sessionId, exerciseId)`, stable order, category, priority, reason, and title snapshots in `apps/api/Grammar/Persistence/Entities/GrammarReviewSessionEntity.cs` and `apps/api/Grammar/Persistence/Entities/GrammarReviewExerciseEntity.cs`
- [X] T023 [US2] Configure review relationships/indexes and update the additive migration/model snapshot in `apps/api/Grammar/Persistence/GrammarDbContext.cs` and `apps/api/Grammar/Persistence/Migrations/`
- [X] T024 [US2] Define bounded review response DTO and selection metadata matching the OpenAPI contract in `apps/api/Grammar/Contracts/GrammarReviewSessionResponse.cs`
- [X] T025 [US2] Implement deterministic review scoring (50% historical error, 30% recent error, 20% recency), evidence shrinkage, adaptable 70/20/10 category targets, topic diversity, and up-to-10 unique exercise selection in `apps/api/Grammar/Services/GrammarReviewService.cs`
- [X] T026 [US2] Add `POST /api/grammar/review/sessions`, persist selection snapshots, and submit through the regular evaluator with session/topic attribution in `apps/api/Grammar/Endpoints/GrammarEndpoints.cs` and `apps/api/Grammar/Services/GrammarExerciseService.cs`
- [X] T027 [US2] Add typed review API methods and review state in `apps/web/src/app/features/grammar/grammar-api.service.ts` and `apps/web/src/app/features/grammar/grammar-session.store.ts`
- [X] T028 [US2] Implement review start, regular exercise answering, evidence-backed reasons, and shorter-session messaging in `apps/web/src/app/features/grammar/review-page.ts`, `apps/web/src/app/features/grammar/review-page.html`, and `apps/web/src/app/features/grammar/review-page.scss`
- [X] T029 [US2] Add lazy review route and accessible navigation while preserving existing practice routes in `apps/web/src/app/app.routes.ts` and `apps/web/src/app/app.html`

**Checkpoint**: Review sessions are history-driven, reproducible, unique, bounded, and use the existing evaluator.

## Phase 5: User Story 3 - Complete and understand a session (Priority: P2)

**Goal**: Summarize evaluated review answers and compare prior history only when evidence is sufficient.

**Independent Test**: Partially answer a multi-topic session; verify counts, accuracy, attention topics, and prior comparison against persisted evaluated responses only.

### Tests

- [X] T030 [P] [US3] Test evaluated-only counts, topic totals, attention topics, zero answers, and prior-evidence thresholds in `apps/api.tests/Grammar/GrammarReviewSummaryServiceTests.cs`
- [X] T031 [P] [US3] Test partial and completed session summary responses in `apps/api.tests/Grammar/GrammarExerciseEndpointTests.cs` and `apps/api.tests/Grammar/GrammarReviewSummaryEndpointTests.cs`
- [X] T032 [P] [US3] Test conditional summary/prior comparison loading and display in `apps/web/src/app/features/grammar/grammar-api.service.spec.ts` and `apps/web/src/app/features/grammar/review-page.spec.ts`

### Implementation

- [X] T033 [US3] Define session summary and per-topic DTOs matching the OpenAPI contract in `apps/api/Grammar/Contracts/GrammarReviewSessionSummaryResponse.cs`
- [X] T034 [US3] Derive summary from persisted session-linked answers, excluding unanswered exercises and unsupported prior comparisons in `apps/api/Grammar/Services/GrammarReviewSummaryService.cs`
- [X] T035 [US3] Add `GET /api/grammar/review/sessions/{sessionId}/summary` with not-found/error handling and service registration in `apps/api/Grammar/Endpoints/GrammarEndpoints.cs` and `apps/api/Grammar/Configuration/GrammarServiceRegistration.cs`
- [X] T036 [US3] Add summary API/state and render counts, topic results, attention, and eligible comparison in `apps/web/src/app/features/grammar/grammar-api.service.ts`, `apps/web/src/app/features/grammar/review-page.ts`, `apps/web/src/app/features/grammar/review-page.html`, and `apps/web/src/app/features/grammar/review-page.scss`

**Checkpoint**: Summaries reconcile with actual evaluated answers; unanswered exercises never count as attempts.

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T037 [P] Verify route and response shapes against `specs/006-learning-statistics-adaptive-review/contracts/learning-statistics-review.openapi.yaml`
- [X] T038 [P] Run focused Grammar API tests and inspect PostgreSQL migration constraints/indexes using `apps/api.tests/AudioApi.Tests.csproj` and `apps/api/Grammar/Persistence/Migrations/`
- [X] T039 [P] Run Angular tests and production build, including keyboard-accessible statistics/review navigation, in `apps/web/package.json` and `apps/web/src/app/`
- [ ] T040 Run integrated acceptance scenarios and verify `/grammar/:slug/practice` remains functional in `specs/006-learning-statistics-adaptive-review/quickstart.md`

## Dependencies & Execution Order

- Phase 1 precedes Phase 2; foundational history (Phase 2) blocks every story.
