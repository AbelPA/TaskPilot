# Research: YouTube Audio Extraction

**Branch**: `release/extrator_audio_video`
**Date**: 2026-10-02

## Repository findings

- The repository contains an Angular application at `apps/web` and an ASP.NET Core API at `apps/api`.
- The Angular app is a starter shell with no routes, feature pages, HTTP integration, notification UI, or real-time client. Its `package.json` declares Angular 21.2, TypeScript 5.9, RxJS, and Vitest. `ng test` is the existing test entry point.
- The API is a .NET 10 ASP.NET Core Minimal API with only the sample weather forecast route and the OpenAPI package. There is no current authentication, persistence, broker integration, SignalR hub, API test project, or established error contract.
- There is no tracked Python runtime/manifest, worker, RabbitMQ configuration, FFmpeg installation, database, object-storage provider, Docker/Compose setup, or GitHub Actions workflow to reuse.
- The constitution file contains unfilled template placeholders; the applicable repository guidance is `docs/engineering-standards.md` and application-specific standards. These require simple maintainable boundaries, explicit contracts, protection of sensitive information, automated tests, and CI validation before merge.
- Graphify was generated for the current branch and queried for the requested workflow. Its useful extracted relationships confirm the Angular entry point and app configuration, but it contains no existing worker, broker, database, storage, or notification path. Source inspection confirms the same. Graphify is navigation evidence only; the source files remain authoritative.
- Read-only specialist research confirmed the frontend and backend findings above and identified no pre-existing patterns that should be extended for messaging, persistence, storage, or real-time notifications.

## Decisions

### D1. Extend the existing applications and add one worker

**Decision**: Keep the existing Angular 21.2 / TypeScript 5.9 app and .NET 10 API. Add one independent Python worker under `apps/workers/audio/`.

**Rationale**: This matches the explicit product requirement and the repository's existing `apps/` boundary without replacing either current application. The worker owns media retrieval and processing; the API owns HTTP, request lifecycle, publication, and notification integration.

**Alternatives considered**: A new API or frontend would duplicate existing applications. Running FFmpeg inside the API would couple long-running CPU/network work to request handling and violate the asynchronous worker requirement.

### D2. Use PostgreSQL as the durable workflow store

**Decision**: Add PostgreSQL for extraction-request state, transactional outbox publication records, and terminal notification outcomes. Use EF Core with the Npgsql provider in the existing API; the Python worker does not connect directly to the application database.

**Rationale**: The unauthenticated `requestId` lookup, request state, durable outbox, and missed-notification recovery require durable shared state. PostgreSQL supports atomic request/outbox commits and unique constraints for event/request deduplication. Keeping database writes in the .NET API avoids a shared-schema coupling between C# and Python.

**Alternatives considered**: In-memory state is lost on restart and cannot support reconnect recovery. Local SQLite/files do not provide a suitable shared production store for an API plus background publisher. A managed PostgreSQL service can replace the local container without changing the data contract.

### D3. Make the PostgreSQL transaction the acceptance boundary with a transactional outbox

**Decision**: In one database transaction, create the request and its `audio.extraction.requested` outbox record. Return `202 Accepted` after the commit succeeds. A .NET hosted outbox publisher delivers pending records to RabbitMQ using persistent messages and publisher confirms, with bounded backoff; broker downtime does not erase an accepted request. After the retry threshold, alert and retain the event for slower retries/operator recovery. Mark an outbox record published only after confirmation.

**Rationale**: A database and RabbitMQ cannot be committed atomically. Publishing directly and then failing to persist request state can create an untraceable worker job; returning an error while leaving a broker-accepted event can create a ghost job. The outbox gives a durable meaning to "accepted" and enables safe eventual delivery. The feature spec's acceptance language has been aligned to this durable acceptance model.

**Alternatives considered**: Waiting for RabbitMQ confirmation before returning `202` leaves a request/queue dual-write gap and cannot safely recover ambiguous confirms without an outbox. Rejecting all requests during broker outages unnecessarily makes the API unavailable even though the application can durably retain work.

### D4. Use RabbitMQ Topic Exchange and explicit durable queues

**Decision**:

- Durable topic exchange: `media.events`.
- Routing keys: `audio.extraction.requested`, `audio.extraction.completed`, and `audio.extraction.failed`.
- Durable queues: `audio.extraction.worker`, `audio.extraction.notifications`, and `audio.extraction.dead-letter`.
- The API outbox publisher sends request events; the Python worker consumes them and publishes outcomes; a .NET hosted consumer processes outcome events.
- Use persistent messages, publisher confirms, manual acknowledgements, finite delayed retries, and dead-letter routing after the retry limit.

**Rationale**: This uses RabbitMQ's exchange/routing-key/queue model and keeps the worker independent of browser and SignalR details. Separate notification consumption from work consumption allows each side to retry independently.

**Alternatives considered**: An in-process queue cannot decouple API and worker restarts. Kafka-style topics are not part of the requested RabbitMQ model. Exact retry delays and broker image versions must be pinned in implementation configuration.

### D5. Add a bounded-retry and idempotent worker protocol

**Decision**: Use at most three automatic retries for transient failures with increasing delays; malformed messages and permanent video/processing errors are not retried indefinitely and are dead-lettered or converted to terminal failure events as appropriate. Acknowledge an extraction message only after a terminal outcome has been durably stored and the corresponding outcome event is publisher-confirmed. Use a deterministic object key and deterministic terminal `eventId` derived from `requestId` plus the terminal event type; upload atomically from a temporary file. If a redelivered message finds a valid completed object, skip re-extraction and republish the same completion event.

**Rationale**: RabbitMQ provides at-least-once delivery, not exactly-once processing. Deterministic result identity and a durable object make redelivery safe across a crash after storage and before acknowledgement.

**Alternatives considered**: A shared worker/API database would give additional lease tracking but creates cross-language schema and deployment coupling. This first version accepts possible repeated CPU work if a worker dies before a complete object is persisted; it must not create duplicate logical results.

### D6. Use an S3-compatible object store for extracted audio

**Decision**: Store files using an S3-compatible interface, with MinIO for reproducible local development and a production S3-compatible provider supplied through deployment configuration. Use a private bucket, deterministic object keys, short-lived signed retrieval links, and a seven-day initial artifact retention policy measured from completion/upload.

**Rationale**: Object storage is durable and appropriate for media output; the repository has no existing provider. S3 compatibility lets local and production storage share a boundary without assuming a production cloud vendor.

**Alternatives considered**: Worker-local files disappear on restart or redeployment and cannot be safely retrieved by the frontend. Public permanent URLs expose the generated media and are not acceptable for an unauthenticated request-ID capability.

### D7. Validate YouTube identity and duration through the official Data API

**Decision**: Canonicalize an allowlisted YouTube URL to a video ID. The .NET API calls the fixed YouTube Data API v3 `videos.list` endpoint to verify existence/accessibility and read duration before creating the request. Store only the canonical video ID, not the submitted URL. Configure the API key as a secret. The worker receives the ID and constructs the permitted YouTube source itself; it does not fetch arbitrary user-provided URLs.

**Rationale**: The API is required to reject nonexistent videos synchronously. A fixed-host official metadata request avoids accepting arbitrary URLs and exposes duration for interval validation.

**Alternatives considered**: A generic URL probe or following arbitrary redirects creates SSRF risk and does not reliably establish video accessibility. Asking the Python downloader to validate after queueing would violate the synchronous request-validation contract.

**Constraints and risk**: YouTube Data API quota/key provisioning is a deployment prerequisite. Metadata availability does not guarantee that media can be retrieved by the worker; private, restricted, removed, or region-limited media can still fail asynchronously. Media retrieval and extraction must comply with YouTube terms, content rights, and applicable law; this feature does not bypass access restrictions.

### D8. Use `yt-dlp` and FFmpeg in the Python worker

**Decision**: Pin Python 3.12 and dependencies with `uv`/`uv.lock`; use `aio-pika` for asynchronous AMQP, `yt-dlp` for retrieval of the approved canonical video, FFmpeg as an executable invoked without a shell, and `pytest` for tests. Package the worker in a Linux container that includes a pinned FFmpeg build.

**Rationale**: These choices provide an independently runnable, lockable async worker while keeping media retrieval separate from time-interval conversion. FFmpeg remains the required extraction engine.

**Alternatives considered**: `pika` is a viable synchronous AMQP client but would require a different concurrency approach. Python-native audio processing would duplicate FFmpeg capabilities. Dependency and image patch versions must be selected and locked when implementation manifests are added.

### D9. Use anonymous SignalR subscriptions guarded by `requestId`

**Decision**: Extend the .NET API with an ASP.NET Core SignalR hub at `/hubs/audio-extractions`; add `@microsoft/signalr` to Angular. Clients subscribe to a known request using a hub method that validates the request ID before joining its server-side group. The high-entropy `requestId` acts as the bearer capability because sign-in is explicitly out of scope. Persist terminal state before publishing a real-time update. On page reload/reconnect, Angular calls the status endpoint for its locally retained request IDs to recover missed outcomes; do not use periodic polling.

**Rationale**: The API already owns outcome consumption and is the only service that should know about browser notification transport. SignalR provides the requested real-time path; persistent state covers offline clients and race conditions between completion and subscription.

**Alternatives considered**: The Python worker must not connect directly to browsers. Polling is less efficient and explicitly not preferred. SSE is viable for server-to-client updates but would require implementing a separate connection and subscription protocol.

**Operational constraint**: Start with a single API instance. If the API is scaled horizontally, add a supported SignalR scale-out/backplane or shared delivery mechanism before doing so; no such infrastructure currently exists.

### D10. Define identifiers, access, rate limits, and retention

**Decision**: Generate a 256-bit cryptographically random, base64url-encoded `requestId`; treat it as a secret capability for status and artifact access. Do not list requests globally. Enforce an initial per-IP API rate limit (5 submissions per minute), use only trusted forwarded IP headers, and do not persist raw client IPs. Keep completed audio for seven days after upload and remove it through a scheduled cleanup/lifecycle policy. Limit requested extraction to 30 minutes as specified; reject sources longer than six hours, downloads larger than 2 GiB, outputs larger than 100 MiB, and jobs exceeding 15 minutes.

**Rationale**: Authentication is not required, so a strong unguessable request token, minimal response exposure, abuse limits, and bounded media resource usage are important. Limits are conservative starting defaults and should be measured and configurable.

**Alternatives considered**: Sequential IDs and publicly listed notifications expose unrelated requests. Unlimited source or extraction duration creates avoidable resource-exhaustion and cost risks. The plan does not add a full user account system.

### D11. Test each service at its established boundary

**Decision**:

- Angular: continue with the configured Angular CLI unit-test builder/Vitest, plus `ng build`.
- .NET API: add xUnit unit tests and ASP.NET Core integration tests for validation, request/outbox transactions, API contracts, event consumers, and SignalR authorization by capability.
- Python worker: use `pytest` and `pytest-asyncio`; fake the broker, downloader, and storage for unit tests, with RabbitMQ/MinIO/FFmpeg integration tests in the local Compose stack using a small lawful fixture.
- Cross-service: validate JSON event schemas and run a Compose-based end-to-end scenario.

**Rationale**: Angular has an existing test runner. The API and worker have no test patterns, so these are new explicit, standard choices.

**Alternatives considered**: Manual-only testing cannot reliably cover redelivery, storage failure, or disconnected notifications. A full browser E2E framework is not currently installed and is deferred until the cross-service integration path is stable.

### D12. Add a development Compose environment and document native commands

**Decision**: Add Dockerfiles for API/worker/web and a root Compose configuration for PostgreSQL, RabbitMQ, MinIO, API, worker, and Angular web application. FFmpeg is installed in the worker image. Document both the Compose path and targeted native commands for running the apps/tests.

**Rationale**: No existing container conventions exist; one explicit reproducible local path satisfies the new infrastructure requirement and keeps the media binary isolated in the worker image.

**Alternatives considered**: Installing RabbitMQ, Postgres, MinIO, and FFmpeg manually on each developer machine is not reproducible. Production deployment manifests are deferred because no deployment target or pipeline exists in the repository.

## Remaining implementation-time inputs

The plan resolves architectural choices, but deployment credentials and exact patched package/image versions must be supplied or locked when implementation begins:

- YouTube Data API key and quota/project ownership.
- Production PostgreSQL and S3-compatible storage endpoints/credentials.
- Exact dependency and container image patch versions, pinned in lockfiles/manifests.
- Confirmation that the seven-day retention and initial six-hour source, 30-minute interval, and per-IP rate limits meet product policy.
- Confirmation that planned YouTube metadata and media retrieval are permitted for the intended content and usage.
