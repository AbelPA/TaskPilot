# Tasks: YouTube Audio Extraction

**Input**: Design documents in `specs/002-youtube-audio-extraction/`

**Prerequisites**: Approved `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, and `quickstart.md`.

**Tests**: Included because the specification explicitly requires automated Angular, API, RabbitMQ, worker, event-contract, and end-to-end coverage.

**Organization**: Tasks are grouped by user story. All story work depends on shared setup and foundational contracts/infrastructure.

## Format

Every task uses the required format `- [ ] T### [P?] [US#?] Description with concrete file path`. `[P]` appears only when files are independent and no prerequisite task is incomplete. Story labels appear only in user-story phases.

## Phase 1: Setup

**Purpose**: Establish the planned app/service structure and reproducible project configuration. These tasks do not yet implement a complete user story.

- [X] T001 Create the root `.env.example` with documented placeholder variables for the YouTube API key, PostgreSQL, RabbitMQ, MinIO/S3-compatible storage, and Angular API base URL; include no real credentials.
- [X] T002 [P] Add `apps/api/Dockerfile` for the existing .NET 10 API using a non-root runtime image, health-check endpoint convention, and build/runtime stages.
- [X] T003 [P] Add `apps/web/Dockerfile` to build the existing Angular app and serve its production browser bundle for the local Compose stack.
- [X] T004 [P] Create the Python 3.12 worker package in `apps/workers/audio/pyproject.toml` with runtime dependencies `aio-pika`, `yt-dlp`, `jsonschema`, and `openapi-spec-validator` plus development dependencies `pytest` and `pytest-asyncio`; pin dependencies using `apps/workers/audio/uv.lock`, and add the worker image at `apps/workers/audio/Dockerfile` with FFmpeg installed.
- [X] T005 Add root `docker-compose.yml` definitions for PostgreSQL, RabbitMQ, MinIO, the existing API, Angular web app, and Python worker, wiring configuration through `.env.example` and persistent local volumes.
- [X] T006 [P] Add `.github/workflows/ci.yml` jobs for Angular build/tests, .NET tests, Python tests, and API/event contract validation using the repository paths and documented SDK versions.

**Checkpoint**: Project manifests and local infrastructure describe the three apps and shared services; no production secrets are committed.

## Phase 2: Foundational

**Purpose**: Complete shared contracts, persistence, broker conventions, and test scaffolding that block all user stories.

- [X] T007 Create `apps/api.tests/AudioApi.Tests.csproj` targeting .NET 10 with xUnit, ASP.NET Core integration-test support, and a project reference to `apps/api/api.csproj`.
- [X] T008 [P] Create `apps/workers/audio/tests/conftest.py` and `apps/workers/audio/tests/test_event_contracts.py` to validate all event fixtures against `specs/002-youtube-audio-extraction/contracts/events/*.schema.json`.
- [X] T009 [P] Add `apps/api/AudioExtractions/Contracts/AudioExtractionRequested.cs`, `AudioExtractionCompleted.cs`, `AudioExtractionFailed.cs`, and schema-versioned JSON event fixtures matching the required fields and names in `specs/002-youtube-audio-extraction/contracts/events/`.
- [X] T010 Configure EF Core/Npgsql in `apps/api/api.csproj`, `apps/api/Program.cs`, and `apps/api/appsettings.json` with connection strings supplied through environment variables and no committed credentials.
- [X] T011 Implement the request, outbox, event-deduplication, and terminal-notification schema plus its first migration in `apps/api/AudioExtractions/Persistence/AudioExtractionDbContext.cs`, `apps/api/AudioExtractions/Persistence/Entities/`, and `apps/api/AudioExtractions/Persistence/Migrations/`, preserving these exact model rules from `specs/002-youtube-audio-extraction/data-model.md`: `requestId` is “32 cryptographically random bytes encoded as 43-character base64url without padding”; `videoId` is a “Canonical 11-character YouTube video identifier”; `startSeconds` is “>= 0”; `endSeconds` is “> startSeconds” and no greater than source duration; output format is “Initially mp3”; status is “accepted, completed, or failed; terminal states do not transition back”; source duration must not exceed the “configured six-hour source limit”; result object key is nullable and private; result size is “Positive and bounded by configured output-size limit”; checksum is a “SHA-256 hex digest”; result duration must match the requested interval “within media-container precision”; failure data has “no exception or infrastructure detail”; `expiresAt` is “Set to terminal outcome time plus seven days” and nullable before a terminal outcome; idempotency key is “Unique per supported client retry scope; store a hash rather than the raw optional header value”; `eventId` is “Primary key; stable across publisher retries”; event `schemaVersion` “Starts at 1”; outbox state is `pending|published`; `attemptCount` is “Non-negative”; `publishedAt` is set “only after RabbitMQ publisher confirmation”; notification status is `completed|failed` and has a “User-facing and non-sensitive” message.
- [X] T012 [P] Add `apps/api/AudioExtractions/Configuration/AudioExtractionOptions.cs` and environment binding for limits: 30-minute interval, six-hour source, five requests/minute/IP, 2 GiB input download, 100 MiB output, 15-minute worker timeout, and seven-day post-completion retention.
- [X] T013 Implement Problem Details and privacy-safe structured logging in `apps/api/AudioExtractions/Configuration/AudioExtractionServiceRegistration.cs` and `apps/api/Program.cs` after T012; redact raw `requestId`, URL query values, API keys, signed object URLs, and media payloads from logs.
- [X] T014 Configure RabbitMQ infrastructure in `apps/api/AudioExtractions/Messaging/RabbitMqTopology.cs` and `apps/workers/audio/src/audio_worker/messaging/topology.py` with durable topic exchange `media.events`, worker queue `audio.extraction.worker`, notification queue `audio.extraction.notifications`, dead-letter exchange `media.events.dlx`, and `audio.extraction.dead-letter` queue bound to the three documented routing keys and retry/dead-letter routes.
- [X] T015 Add root `docker-compose.yml` health checks, dependency readiness, private MinIO bucket initialization, RabbitMQ durable service configuration, and PostgreSQL readiness; document startup environment variable names in `.env.example`.
- [X] T016 Add `apps/api/AudioExtractions/Persistence/OutboxPublisher.cs` and `apps/api.tests/AudioExtractions/OutboxPublisherTests.cs` scaffolding for persistent publication, RabbitMQ publisher confirms, retry scheduling, and retaining pending outbox records during prolonged broker downtime.
- [X] T017 Add the worker configuration and startup/readiness boundary in `apps/workers/audio/src/audio_worker/config.py` and `apps/workers/audio/src/audio_worker/main.py`, validating required broker/storage settings at startup and failing visibly when invalid.

**Checkpoint**: API, worker, and contract tests can start against the same versioned contracts and local dependencies; all user stories can now begin.

## Phase 3: User Story 1 - Request an audio extraction (Priority: P1) 🎯 MVP

**Goal**: A user submits a valid YouTube video/time interval without signing in and gets a durable `202` response and request capability without waiting for media processing.

**Independent Test**: With database and fake YouTube metadata client available, submit a valid request; assert `202`, a unique 43-character base64url `requestId`, one accepted request plus one pending outbox row in the same transaction, no synchronous extraction, and idempotent retry behavior. Invalid URL/time/video and durable-write failure must reject without creating work.

### Tests for User Story 1

- [X] T018 [P] [US1] Add OpenAPI contract tests in `apps/api.tests/AudioExtractions/AudioExtractionOpenApiTests.cs` for `POST /api/audio-extractions`, the required request fields, `202` response, Problem Details errors, and idempotency conflict response defined in `specs/002-youtube-audio-extraction/contracts/audio-extractions.openapi.yaml`.
- [X] T019 [P] [US1] Add API validation and transaction integration tests in `apps/api.tests/AudioExtractions/CreateAudioExtractionTests.cs` for malformed/unsupported YouTube URLs, malformed/equal/reversed/out-of-duration times, 30-minute interval and six-hour source limits, missing video, upstream timeout, database failure, and atomic request/outbox persistence.
- [X] T020 [P] [US1] Add Angular form tests in `apps/web/src/app/features/audio-extraction/audio-extraction-page.spec.ts` for required URL/start/end, supported YouTube formats, `HH:MM:SS`, ordering, interval bounds, and duplicate-submit disabling.
- [X] T021 [P] [US1] Add request-flow tests in `apps/web/src/app/features/audio-extraction/audio-extraction-api.service.spec.ts` proving the frontend sends `url`, `start`, and `end`, includes the retry idempotency key, handles `202`, and displays API validation/rate-limit/unavailable errors.

### Implementation for User Story 1

- [X] T022 [P] [US1] Implement YouTube URL allowlisting, canonical 11-character video ID extraction, and timestamp-to-seconds conversion in `apps/api/AudioExtractions/Validation/YouTubeVideoUrlParser.cs` and `apps/api/AudioExtractions/Validation/ExtractionIntervalValidator.cs`, rejecting unsupported hosts, protocols, redirects as arbitrary URLs, timestamps outside `HH:MM:SS`, reversed/equal intervals, and configured source/interval limits.
- [X] T023 [P] [US1] Implement the fixed-host YouTube Data API v3 `videos.list` metadata client in `apps/api/AudioExtractions/YouTube/YouTubeVideoMetadataClient.cs` and bind the API key through `apps/api/AudioExtractions/Configuration/AudioExtractionOptions.cs`; map missing/inaccessible videos to stable `VIDEO_NOT_FOUND` and avoid sending any URL or key to the worker.
- [X] T024 [US1] Implement idempotency-key hashing and request/outbox creation in `apps/api/AudioExtractions/Services/AudioExtractionRequestService.cs`, storing a 256-bit random base64url capability and creating both records in one EF transaction; require exact repeat body to return the original ID and return `409` when the same key has a different normalized body.
- [X] T025 [US1] Implement `POST /api/audio-extractions` in `apps/api/AudioExtractions/Endpoints/AudioExtractionEndpoints.cs` and map it from `apps/api/Program.cs`; perform synchronous format/video-duration validation, enforce per-IP rate limit, return `202` only after durable request/outbox commit, and never wait for RabbitMQ consumption or FFmpeg.
- [X] T026 [US1] Implement `apps/api/AudioExtractions/Persistence/OutboxPublisher.cs` to publish `audio.extraction.requested` from pending outbox rows with stable `eventId`, durable persistent messages, publisher confirms, concurrent-safe claiming, bounded backoff, and alert/retention rather than deletion during broker outage.
- [X] T027 [US1] Implement the extraction form and API client in `apps/web/src/app/features/audio-extraction/audio-extraction-page.ts`, `audio-extraction-page.html`, `audio-extraction-page.scss`, and `audio-extraction-api.service.ts`, showing validation, submission state, accepted confirmation, and the returned request ID without requiring sign-in.
- [X] T028 [US1] Add the “Extract Audio” navigation entry, route, and initial app shell in `apps/web/src/app/app.routes.ts`, `apps/web/src/app/app.html`, and `apps/web/src/app/app.ts`, replacing the generated welcome screen without introducing another Angular app.
- [X] T029 [US1] Add `apps/api.tests/AudioExtractions/OutboxPublisherTests.cs` for publish confirmation, broker reconnect after outage, stable event ID across retries, and no message loss after committed acceptance.

**Checkpoint**: User Story 1 can be demonstrated end-to-end through durable `202` acceptance and eventual RabbitMQ publication; extraction need not be complete for this MVP checkpoint.

## Phase 4: User Story 2 - Receive progress outcomes (Priority: P1)

**Goal**: Accepted work runs in the Python worker; users tracking its request ID receive persisted success/failure notifications in real time and can recover them after reconnect.

**Independent Test**: Publish valid request events and exercise success, terminal failure, duplicate delivery, broker failure, and disconnected-client cases. Assert one logical terminal outcome per request, a durable notification, and real-time/reconnect delivery to the client tracking that ID.

### Tests for User Story 2

- [ ] T030 [P] [US2] Add worker unit tests in `apps/workers/audio/tests/test_audio_extraction.py` for request validation, approved canonical video ID only, exact start/end interval, FFmpeg failure, download failure, storage failure, safe failure payloads, and no shell command interpolation.
- [ ] T031 [P] [US2] Add worker redelivery tests in `apps/workers/audio/tests/test_idempotency.py` for crash after object upload before ACK, deterministic event IDs, one logical output, valid existing result reuse, bounded transient retries, permanent failure, and malformed-message dead-letter behavior.
- [ ] T032 [P] [US2] Add API outcome consumer and SignalR integration tests in `apps/api.tests/AudioExtractions/AudioExtractionOutcomeTests.cs` for known/unknown request IDs, event deduplication, terminal-state persistence before notification, completion/failure group delivery, and duplicate terminal events.
- [ ] T033 [P] [US2] Add Angular real-time tests in `apps/web/src/app/features/audio-extraction/notifications/audio-extraction-notification.service.spec.ts` for connection lifecycle, subscription validation, completion/failure updates, reconnect, and status recovery without periodic polling.

### Implementation for User Story 2

- [ ] T034 [P] [US2] Implement the independently runnable worker consumer and event validation in `apps/workers/audio/src/audio_worker/messaging/consumer.py`, validating schema version, 43-character request ID, 11-character video ID, integer offsets, and `endSeconds > startSeconds` before dispatch.
- [ ] T035 [P] [US2] Implement canonical YouTube media retrieval in `apps/workers/audio/src/audio_worker/media/video_downloader.py` using pinned `yt-dlp`, generated canonical source URL, controlled temporary paths, download-size limits, timeouts, and no arbitrary URL support.
- [ ] T036 [P] [US2] Implement FFmpeg interval extraction in `apps/workers/audio/src/audio_worker/media/ffmpeg_processor.py` using a fixed executable and argument vector (never a shell), validated integer offsets, `-vn`, an audio stream map, MP3 output, generated paths, and the 15-minute job timeout.
- [ ] T037 [P] [US2] Implement private S3-compatible storage in `apps/workers/audio/src/audio_worker/storage/audio_storage.py` and `apps/workers/audio/src/audio_worker/storage/minio.py`, uploading to the deterministic request key and validating size/checksum before treating an existing object as complete.
- [ ] T038 [US2] Implement completion/failure event publication in `apps/workers/audio/src/audio_worker/messaging/publisher.py` matching `contracts/events/audio-extraction-completed.schema.json` and `audio-extraction-failed.schema.json`; use deterministic terminal event IDs, publisher confirms, bounded retries, and expose no stack traces, signed URLs, or source URL.
- [ ] T039 [US2] Implement consumer acknowledgement/retry orchestration in `apps/workers/audio/src/audio_worker/processing/audio_extraction_service.py`; ACK only after output/failure is durable and outcome publication is confirmed, reuse verified output after redelivery, retry transient errors at most three times, and dead-letter poison/exhausted messages.
- [ ] T040 [US2] Implement .NET completion/failure queue consumption and conditional request state/notification persistence in `apps/api/AudioExtractions/Messaging/AudioExtractionOutcomeConsumer.cs` and `apps/api/AudioExtractions/Services/AudioExtractionOutcomeService.cs`; deduplicate by event ID and terminal request ID, reject unknown IDs safely, commit before notification, then ACK.
- [ ] T041 [US2] Add `apps/api/AudioExtractions/Notifications/AudioExtractionsHub.cs` and SignalR registration in `apps/api/Program.cs`; validate that `requestId` exists before allowing a connection into that request's group, implement subscribe/unsubscribe, and redact capability values from connection logs.
- [ ] T042 [US2] Implement Angular SignalR connection and event handling in `apps/web/src/app/features/audio-extraction/notifications/audio-extraction-notification.service.ts`, using `@microsoft/signalr` reconnect behavior and status lookup for locally saved request IDs after reconnection, without polling.
- [ ] T043 [US2] Add success/failure/unread notification presentation to `apps/web/src/app/features/audio-extraction/notifications/notification-center.ts`, `notification-center.html`, and `notification-center.scss`, showing safe failure details and completion action without requiring authentication.
- [ ] T044 [P] [US2] Add `GET /api/audio-extractions/{requestId}` and reconnect-recovery tests in `apps/api.tests/AudioExtractions/AudioExtractionStatusTests.cs` for accepted/completed/failed requests, valid and malformed capability IDs, unknown/expired IDs returning the same generic not-found shape, and status persistence across API restart.
- [ ] T045 [US2] Implement `GET /api/audio-extractions/{requestId}` in `apps/api/AudioExtractions/Endpoints/AudioExtractionEndpoints.cs` and `apps/api/AudioExtractions/Contracts/AudioExtractionStatusResponse.cs`, returning capability-scoped persisted outcome state so offline clients can recover without polling or a global listing.

**Checkpoint**: Accepted extraction requests reach a terminal persisted outcome; a client tracking the request sees the result live or recovers it after reconnect.

## Phase 5: User Story 3 - Review and retrieve completed audio (Priority: P2)

**Goal**: Users can return with their saved request capability, review unread outcomes, and play/download the persisted private result without exposing storage URLs.

**Independent Test**: Complete a request, close/reload the page, reopen its notification using the locally saved request ID, play/download the audio through the API, and confirm unknown/expired IDs cannot retrieve request status or artifacts.

### Tests for User Story 3

- [ ] T046 [P] [US3] Add artifact access tests in `apps/api.tests/AudioExtractions/AudioExtractionResultTests.cs` for completed, unknown, malformed, uncompleted, failed, and expired IDs; confirm identical not-found behavior and no public object URL disclosure.
- [ ] T047 [P] [US3] Add Angular persistence/recovery tests in `apps/web/src/app/features/audio-extraction/notifications/notification-center.spec.ts` for retaining only request capabilities in browser-local state, restoring multiple outcomes, marking notifications read locally, and clearing expired entries.
- [ ] T048 [P] [US3] Add worker retention/storage integration tests in `apps/workers/audio/tests/test_audio_storage.py` for private bucket access, 100 MiB output limit, checksum verification, deterministic object key, and seven-day artifact expiration metadata.

### Implementation for User Story 3

- [ ] T049 [US3] Implement `GET /api/audio-extractions/{requestId}/audio` in `apps/api/AudioExtractions/Endpoints/AudioExtractionEndpoints.cs` and `apps/api/AudioExtractions/Services/AudioResultAccessService.cs`, checking completion/expiry before creating a short-lived signed object URL and returning no-store/no-referrer headers without revealing bucket or key.
- [ ] T050 [US3] Implement terminal expiry and cleanup in `apps/api/AudioExtractions/Persistence/ExpiredExtractionCleanupService.cs` and `apps/workers/audio/src/audio_worker/storage/retention.py`, retaining outcomes for seven days after completion, deleting expired objects and records safely, and never expiring pending accepted work.
- [ ] T051 [US3] Implement browser request-ID and local read-state persistence in `apps/web/src/app/features/audio-extraction/notifications/request-capability-store.ts`, keeping IDs out of shareable URLs and avoiding storage of signed media URLs.
- [ ] T052 [US3] Add notification actions in `apps/web/src/app/features/audio-extraction/notifications/notification-center.html` and `notification-center.ts` for play/download through `/api/audio-extractions/{requestId}/audio`, and display expired/failed state without leaking object-storage details.

**Checkpoint**: The owner-less capability flow supports persistent recovery and protected audio retrieval for completed, unexpired requests.

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify the complete workflow, document operations, and close security/observability gaps.

- [ ] T053 [P] Write `docs/architecture/audio-extraction.md` documenting service boundaries, PostgreSQL/outbox acceptance semantics, RabbitMQ topology/retries/DLQ, event/API contracts, MinIO and production S3 configuration, SignalR capability subscriptions, security limits, failure recovery, and retention.
- [ ] T054 [P] Update `README.md` and `.env.example` with the documented developer prerequisites and link to `specs/002-youtube-audio-extraction/quickstart.md`; make sure local secrets remain ignored.
- [ ] T055 Add structured trace events and operational metrics in `apps/api/AudioExtractions/Observability/AudioExtractionTelemetry.cs` and `apps/workers/audio/src/audio_worker/observability.py`, correlating with a non-reversible request-ID digest and recording queue age, retry count, processing duration, result bytes, and terminal status without logging capability values or sensitive URLs.
- [ ] T056 Review YouTube Data API quota/error handling, arbitrary URL/redirect protections, FFmpeg process limits, per-IP rate limiting, CORS, request-ID log redaction, signed-link TTL, YouTube policy/legal constraints, and privacy behavior in `apps/api.tests/AudioExtractions/AudioExtractionSecurityTests.cs` and `apps/workers/audio/tests/test_security_limits.py`.
- [ ] T057 [P] Validate CI workflow and contract consumers in `.github/workflows/ci.yml`, including schema validation by .NET/Python, Angular build and tests, .NET test suite, Python test suite, and no network-dependent media access in unit tests.
- [ ] T058 Run every scenario in `specs/002-youtube-audio-extraction/quickstart.md`, record the local Compose/API/worker outcomes in `specs/002-youtube-audio-extraction/quickstart.md`, and fix discrepancies between executable commands and the implemented service names/ports.
- [ ] T059 Refresh the Graphify graph with `graphify . --update` after the service, event, storage, and notification architecture is implemented; verify the generated graph includes the new Angular, .NET, Python, RabbitMQ, and storage relationships.

**Checkpoint**: Complete feature has reproducible local setup, documented operational procedures, passing CI, safe failure recovery, and refreshed architecture graph.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No prerequisite; establishes project manifests, local service stack, and CI entry points.
- **Foundational (Phase 2)**: Depends on Phase 1; creates shared persistence, event, configuration, broker, and test foundations. Blocks every user story.
- **User Story 1 (Phase 3)**: Depends on Foundation; independently delivers request validation and durable acceptance. This is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundation and the request/event contract from US1. Worker and notification unit work can be tested with fixtures, but full-flow acceptance uses US1.
- **User Story 3 (Phase 5)**: Depends on Foundation, US1 request persistence, and US2 terminal outcomes/object storage.
- **Polish (Phase 6)**: Depends on every story selected for release; Graphify refresh follows implementation of the architecture.

### User Story Dependencies

- **US1 (P1)**: Starts after Foundation; no dependency on other story implementation.
- **US2 (P1)**: Reuses the US1 request event and `requestId` contract. Can develop component tests with event fixtures after Foundation, but integration requires US1 publication. Implements persisted request status needed for disconnect/reconnect recovery.
- **US3 (P2)**: Requires US1 request records and US2 terminal result/notification/status persistence. Its artifact API and UI integration follows both.

### Within Each User Story

- Write the story-specific tests and contracts first; verify the tests fail before implementing behavior.
- Complete the data/persistence contract before service logic, service logic before endpoints/Angular integration, and end-to-end integration before marking a checkpoint complete.
- Keep API, Angular, and Python changes in separate files so specialists can work in parallel after their contract prerequisites.
- Do not parallelize tasks that edit the same component/endpoint file, depend on an unfinished task, or require unapproved changes to a shared contract.

### Parallel Opportunities

- **Setup**: T002, T003, T004, and T006 edit independent image, worker, and CI files; T001 and T005 are sequential because Compose depends on environment variable names.
- **Foundation**: T008, T009, T012, and T004's completed project scaffolding can proceed independently; T013 follows T012 because it edits `Program.cs` for service configuration; T010 precedes T011 and T016; T014 precedes T016 and worker messaging; T015 completes the runnable local service stack.
- **US1**: T018-T021 tests can be authored in parallel after contracts exist. T022 and T023 can proceed independently; T024 needs T011/T022; T025 needs T024 and T023; T026 needs T016/T014; T027 can be developed independently of backend tests against the OpenAPI contract; T028 and T027 both edit app shell/routing and should be coordinated as one Angular workstream.
- **US2**: T030-T033 tests can be authored in parallel. T034-T037 worker files can be implemented in parallel after T009/T014/T017; T038-T039 depend on these pieces. T040-T041 are API-side work; T042-T043 are Angular-side work and require US1's request ID storage/route.
- **US3**: T046-T048 tests can proceed in parallel after their service contracts. T049 precedes T052; T050 can run in parallel with UI work T051-T052 after US2; T051 and T052 edit distinct files.
- **Polish**: T053, T054, T055, and T057 edit separate documentation/telemetry/CI files after story APIs are stable; T056 and T058 validate the integrated system; T059 runs after architectural changes settle.

## Parallel Example: User Story 1

```text
After contract and setup tasks complete, start together:
Task T018: Add API OpenAPI contract tests in apps/api.tests/AudioExtractions/AudioExtractionOpenApiTests.cs
Task T019: Add API validation/transaction tests in apps/api.tests/AudioExtractions/CreateAudioExtractionTests.cs
Task T020: Add Angular form tests in apps/web/src/app/features/audio-extraction/audio-extraction-page.spec.ts
Task T021: Add Angular API service tests in apps/web/src/app/features/audio-extraction/audio-extraction-api.service.spec.ts

Then implement API validation/metadata client concurrently:
Task T022: URL/time validation in apps/api/AudioExtractions/Validation/
Task T023: YouTube metadata lookup in apps/api/AudioExtractions/YouTube/

Then integrate request persistence, endpoint, publisher, and Angular UI in dependency order:
T024 -> T025; T026 after T016/T014; T027/T028 coordinated as one Angular workstream.
```

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 setup and Phase 2 shared foundations.
2. Implement Phase 3 request validation, YouTube existence/duration lookup, atomic request/outbox write, idempotency key, and `202` response.
3. Validate a valid/invalid request and broker recovery with the tests listed in US1.
4. Stop for review before expanding to processing and notifications; the MVP does not claim that extraction audio is available yet.

### Incremental Delivery

1. Setup + Foundation → reliable service contracts and local infrastructure.
2. US1 → users can submit requests and receive durable acceptance.
3. US2 → worker processes accepted jobs and sends recoverable real-time terminal outcomes.
4. US3 → users can recover notifications and retrieve private output.
5. Polish → security review, docs, full Compose validation, CI, and Graphify refresh.

### Specialist Workstreams

After shared contracts are approved, the `dev-angular`, `dev-dotnet`, and `dev-python` specialists can work within their listed app/file boundaries. The tech lead owns cross-service API/event topology and resolves any contract mismatch before integration; reviewer, tester, and security agents validate completed slices.

## Notes

- `[P]` means the task edits files independent from other unfinished tasks; `[US1]`, `[US2]`, and `[US3]` map directly to spec stories.
- Test tasks are included because they are explicitly required by the feature spec; external YouTube access is reserved for the configured end-to-end test.
- Any task that changes API/event schemas must update the corresponding OpenAPI/JSON Schema document and consumer tests in the same change.
- The plan's operational limits and dependency choices are defaults from approved research; confirm policy and pin exact patched dependency/image versions before public deployment.
