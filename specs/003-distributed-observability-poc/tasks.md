# Tasks: Local Distributed Observability

**Input**: Design documents from `/specs/003-distributed-observability-poc/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/telemetry.md](./contracts/telemetry.md), [quickstart.md](./quickstart.md)

**Tests**: Included because the feature's independent acceptance criteria and plan's design gates require automated propagation, privacy, metrics, and cross-service integration coverage.

**Organization**: Tasks are grouped by user story; complete setup and the shared OTLP foundation before story work. US2 uses the trace context delivered by US1. US3 metric instrumentation can proceed after the foundation, while its full demonstration validates all stories together.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the browser OpenTelemetry dependencies needed by the new instrumentation; API and worker already have OpenTelemetry dependencies.

- [X] T001 Add pinned OpenTelemetry browser SDK, W3C propagation, and OTLP/HTTP exporter dependencies to `apps/web/package.json` and update `apps/web/package-lock.json`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Make the local OTLP receiver reachable from a browser and define the local endpoint/CORS boundary before implementing client telemetry.

- [X] T002 Verify browser access to the local OTLP/HTTP receiver and configure the narrowest local UI CORS supported by the bundled collector in `docker-compose.yml` (add `docker/otel-collector-config.yaml` and wire it into Compose only if the bundled receiver cannot be configured); record the browser endpoint for `apps/web/src/app/app.config.ts` without exposing a permissive production CORS policy.

**Checkpoint**: The browser can reach the local OTLP receiver; all user-story work can proceed.

---

## Phase 3: User Story 1 - Follow an operation end to end (Priority: P1) 🎯 MVP

**Goal**: A request started in the Angular UI produces a real W3C/OpenTelemetry trace continued through the API outbox, RabbitMQ, and worker processing.

**Independent Test**: Submit a valid audio-extraction request and locate a trace with browser, API, publication/delivery, and worker spans sharing one TraceId; verify absent or invalid context starts an unrelated root rather than reusing a previous trace.

### Tests for User Story 1

- [X] T003 [P] [US1] Verify submission propagates the context of an SDK-generated OpenTelemetry span and does not synthesize a random `traceparent` in `apps/web/src/app/features/audio-extraction/audio-extraction-api.service.spec.ts`.
- [X] T004 [P] [US1] Verify outbox publication preserves valid `traceparent`/`tracestate` across retries and handles missing context in `apps/api.tests/AudioExtractions/OutboxPublisherTests.cs`.
- [X] T005 [P] [US1] Verify worker extraction of valid, missing, and malformed message context creates the expected parent/root spans in `apps/workers/audio/tests/test_observability.py`.

### Implementation for User Story 1

- [X] T006 [P] [US1] Initialize browser OpenTelemetry tracing with stable `taskpilot-web` resource identity and OTLP/HTTP export in `apps/web/src/app/app.config.ts`.
- [X] T007 [P] [US1] Instrument the audio-extraction HTTP operation with W3C context propagation and remove the manually generated random `traceparent` from `apps/web/src/app/features/audio-extraction/audio-extraction-api.service.ts`.
- [X] T008 [P] [US1] Ensure API request and outbox publication spans preserve the valid W3C context in message metadata without changing event bodies in `apps/api/AudioExtractions/Configuration/AudioExtractionServiceRegistration.cs`, `apps/api/AudioExtractions/Observability/TraceContext.cs`, and `apps/api/AudioExtractions/Persistence/OutboxPublisher.cs`.
- [X] T009 [P] [US1] Ensure worker consumer spans extract W3C context, record processing status/errors, and never attach invalid or absent context to another operation in `apps/workers/audio/src/audio_worker/observability.py` and `apps/workers/audio/src/audio_worker/messaging/consumer.py`.

**Checkpoint**: The browser-to-worker trace is independently testable without the log-search and metrics deliverables.

---

## Phase 4: User Story 2 - Find logs by operation (Priority: P2)

**Goal**: API and worker logs are searchable by active TraceId/SpanId while keeping business RequestId and optional CorrelationId separate and excluding sensitive values.

**Independent Test**: Complete a traced operation, query Grafana/Loki logs using its TraceId, and locate ordinary and error/retry API and worker records with distinct business identifiers and no secret or request-content attributes.

### Tests for User Story 2

- [X] T010 [P] [US2] Verify API log records carry active TraceId/SpanId and keep business identifiers separate in `apps/api.tests/AudioExtractions/OutboxPublisherTests.cs`.
- [X] T011 [P] [US2] Verify worker success, error, and retry logs are correlated with the active span and omit sensitive message content in `apps/workers/audio/tests/test_observability.py`.

### Implementation for User Story 2

- [X] T012 [P] [US2] Enrich API OpenTelemetry log records with active TraceId/SpanId for normal and error paths while retaining separate business identifiers in `apps/api/AudioExtractions/Configuration/AudioExtractionServiceRegistration.cs`.
- [X] T013 [P] [US2] Add automatic structured TraceId/SpanId context to worker log records across processing, error, and retry paths without logging raw URLs or message bodies in `apps/workers/audio/src/audio_worker/observability.py` and `apps/workers/audio/src/audio_worker/messaging/consumer.py`.
- [X] T014 [US2] Add Grafana/Loki TraceId search instructions and an example of distinguishing TraceId from `requestId` in `specs/003-distributed-observability-poc/quickstart.md`.

**Checkpoint**: API and worker records for a completed or failed operation can be found by its TraceId.

---

## Phase 5: User Story 3 - Reproduce and observe the PoC locally (Priority: P3)

**Goal**: Docker Compose starts a repeatable local stack where Grafana can inspect HTTP and worker counts, durations, and errors through the OTLP layer.

**Independent Test**: Start the documented Compose stack, process an operation, query the five required measure classes in Grafana, stop and recreate the stack, and repeat without manual observability recovery.

### Tests for User Story 3

- [X] T015 [P] [US3] Verify worker processing counter and duration histogram record success, failure, and bounded retry/outcome attributes without per-operation labels in `apps/workers/audio/tests/test_observability.py`.
- [X] T016 [P] [US3] Verify API HTTP request count, duration, and error measurements are exported with route-template and bounded attributes in `apps/api.tests/AudioExtractions/AudioExtractionEndpointTests.cs`.

### Implementation for User Story 3

- [X] T017 [P] [US3] Record worker processing count and duration using OpenTelemetry instruments with bounded outcome/retry attributes and no TraceId, SpanId, RequestId, CorrelationId, or URL labels in `apps/workers/audio/src/audio_worker/observability.py` and `apps/workers/audio/src/audio_worker/messaging/consumer.py`.
- [X] T018 [US3] Configure and verify API request count, duration, and error metrics with route-template, method, status, and service attributes in `apps/api/AudioExtractions/Configuration/AudioExtractionServiceRegistration.cs`.
- [X] T019 [US3] Verify Compose OTLP routing for web, API, and worker and ensure unavailable telemetry export does not block business operations in `docker-compose.yml`, `apps/api/AudioExtractions/Configuration/AudioExtractionServiceRegistration.cs`, and `apps/workers/audio/src/audio_worker/observability.py`.
- [X] T020 [US3] Document prerequisites, local startup/restart, demonstration steps, Grafana URL, trace/log/metric queries, and troubleshooting in `specs/003-distributed-observability-poc/quickstart.md` and `README.md`.
- [ ] T021 [US3] Execute the documented end-to-end flow, verify 10 consecutive operations each produce one searchable cross-service TraceId and related logs, inspect all five required measures, then recreate the stack in `specs/003-distributed-observability-poc/quickstart.md`.

**Checkpoint**: A developer can reproduce the demonstration and inspect traces, correlated logs, and required metrics after a clean restart.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Run repository quality gates and ensure the delivered implementation matches the contract and privacy constraints.

- [X] T022 [P] Run Angular tests and production build for the OpenTelemetry browser changes in `apps/web/package.json` and `apps/web/src/app/`.
- [X] T023 [P] Run API tests and verify the telemetry changes preserve the existing HTTP and outbox contracts in `apps/api.tests/AudioApi.Tests.csproj` and `apps/api/AudioExtractions/`.
- [X] T024 [P] Run worker tests and verify the telemetry changes preserve retry and event behavior in `apps/workers/audio/tests/` and `apps/workers/audio/src/audio_worker/`.
- [X] T025 Review `apps/web/src/app/`, `apps/api/AudioExtractions/`, `apps/workers/audio/src/audio_worker/`, and `docker-compose.yml` to confirm no credentials, signed URLs, full request bodies, or unbounded per-operation metric labels are exported.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; provides the browser SDK packages.
- **Foundational (Phase 2)**: Depends on Setup; makes the local receiver reachable before browser instrumentation work.
- **User Stories (Phases 3-5)**: Start after the foundation. US2 depends on US1's active W3C trace context. US3's metric implementation can proceed after the foundation, but its Grafana validation and final quickstart acceptance depend on the completed trace/log integrations.
- **Polish (Phase 6)**: Depends on all selected stories being complete.

### User Story Dependencies

- **US1 (P1)**: Depends on Setup and Foundational phases only; MVP story.
- **US2 (P2)**: Depends on US1 for valid active context across API and worker logs.
- **US3 (P3)**: Metric instrumentation is independent after Foundational; full restart/demo validation depends on US1 and US2 being complete.

### Dependency Graph

```text
T001 -> T002
T002 -> US1 (T003-T009)
T002 -> US3 metrics (T015-T019)
US1 -> US2 (T010-T014)
US1 + US2 + US3 implementation -> T020 -> T021
US1 + US2 + US3 -> Polish (T022-T025)
```

### Parallel Execution Examples

- **Setup/foundation**: T001, then T002.
- **US1 tests**: T003, T004, and T005 target separate test files and can be prepared in parallel; after those tests, T006-T009 affect separate frontend/API/worker code areas and can be implemented in parallel.
- **US2**: T010 and T011 can be written in parallel; after US1 context is available, T012 and T013 can proceed in parallel. T014 follows to document the resulting query path.
- **US3**: T015 and T016 can be prepared in parallel; T017 and T018 can proceed in parallel after the tests. Compose validation T019 and documentation T020 follow the verified exporters/configuration.
- **Polish**: T022, T023, and T024 run independently; T025 follows the test runs to review the integrated result.

## Implementation Strategy

1. Deliver the MVP as Setup + Foundation + US1: browser-originated real spans, API/outbox W3C propagation, and worker consumer spans with missing/invalid context coverage.
2. Add US2 log correlation as a separate increment using the trace context established in the MVP; verify ordinary, error, and retry logs remain searchable without confusing business identifiers.
3. Add US3 processing metrics, Compose/local OTLP verification, and reproducibility documentation; finish with ten-operation end-to-end and restart checks.
4. Run the three application test suites and privacy review before calling the feature complete.
