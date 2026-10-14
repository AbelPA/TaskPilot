# Implementation Plan: YouTube Audio Extraction

**Branch**: `release/extrator_audio_video` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-youtube-audio-extraction/spec.md`

## Summary

Extend the existing Angular 21.2 / TypeScript 5.9 application and .NET 10 Minimal API, and add one independent Python 3.12 worker. Use PostgreSQL as the durable acceptance/state store, a transactional outbox to publish to RabbitMQ, an S3-compatible private object store for audio, FFmpeg for interval extraction, and SignalR for real-time outcome notifications. The API returns `202 Accepted` after the request and outbox record commit atomically; the outbox publisher retries RabbitMQ delivery independently. The complete design and alternatives are in [research.md](research.md).

## Technical Context

**Language/Version**: Angular 21.2 / TypeScript 5.9.2 (existing); C# / .NET 10 (existing); Python 3.12 (new, pin patch in runtime file and image).

**Primary Dependencies**: Existing Angular CLI, RxJS, and Vitest; `@microsoft/signalr` for the new Angular real-time client; ASP.NET Core SignalR, EF Core/Npgsql, and RabbitMQ.Client in the existing API; `aio-pika`, `yt-dlp`, and FFmpeg in the Python worker; PostgreSQL, RabbitMQ, and S3-compatible object storage (MinIO locally).

**Storage**: PostgreSQL for request lifecycle, outbox, and notification outcomes. Private S3-compatible object storage for audio results; MinIO in development. Initial result retention: seven days.

**Testing**: Angular CLI `ng test`/Vitest and `ng build`; .NET xUnit unit/integration tests; Python `pytest`/`pytest-asyncio`; JSON Schema contract tests and Compose-based cross-service integration tests.

**Target Platform**: Linux containers for API, worker, and local supporting services; browser client via the existing Angular development/build workflow.

**Project Type**: Existing Angular web application plus existing .NET HTTP API and a new Python background worker; a root Docker Compose development stack.

**Performance Goals**: At least 95% of valid submissions receive a `202` and `requestId` within 5 seconds after YouTube metadata verification and durable request/outbox commit. Connected clients receive a terminal real-time notification within 5 seconds of backend event consumption. Audio extraction is never performed in the HTTP request.

**Constraints**: No sign-in; a 256-bit random `requestId` is a bearer capability. Validate and canonicalize YouTube IDs before network access; metadata requests use the fixed YouTube Data API host. Initial limits: 30-minute extraction, six-hour source duration, five submissions/minute/IP, 2 GiB source download, 100 MiB output, 15-minute worker job timeout, and seven-day artifact retention after completion. RabbitMQ is at-least-once; use outbox, publisher confirms, bounded retries, deterministic result object keys, and duplicate-safe outcome handling. Start with a single API replica unless SignalR scale-out is added.

**Scale/Scope**: One feature in the existing Angular and .NET applications, one new Python worker, three broker event types, request-specific status/result access, and one local Compose environment. Multi-tenant accounts, extraction history without a saved request identifier, cancellation, arbitrary URL support, browser E2E infrastructure, and production deployment manifests are out of scope.

## Constitution Check

The project constitution file is still an unfilled template, so it defines no ratified principles or enforceable gates. The plan follows the active [engineering standards](../../docs/engineering-standards.md): simple maintainable service boundaries, explicit integration contracts, sensitive-data protection, automated tests, and CI validation before merge. Extending the existing frontend/API and adding only the Python worker required by the feature passes the architecture gate. The proposed broker, database, and object store are necessary infrastructure for durable asynchronous processing, status recovery, and media persistence; each is isolated behind service boundaries.

**Gate before research**: PASS — no active constitutional violation; confirm the proposed infra and provider choices in the plan review.

**Gate after design**: PASS with operational prerequisites — provision a YouTube Data API key/quota and production database/object-store configuration; lock exact patch versions before implementation. These are deployment inputs, not unresolved architecture choices.

## Architecture and Contracts

### Responsibilities

1. Angular replaces the generated starter welcome screen with the application's initial navigation shell and extraction page. It validates user-entered fields, calls the .NET endpoint, saves the returned `requestId` and retry idempotency key, and renders submission/outcome notifications. It reconnects through SignalR and reloads status for saved IDs after refresh/reconnect; it does not poll periodically.
2. The .NET API validates the request, canonicalizes a supported YouTube URL, verifies existence/accessibility and duration using the YouTube Data API, enforces limits, creates an unpredictable request ID, and atomically persists the request plus outbox event. It returns `202` only after durable commit.
3. A .NET hosted outbox publisher sends persistent `audio.extraction.requested` events to RabbitMQ with publisher confirms and bounded backoff. If the attempt threshold is reached, it alerts and retains the outbox row for continued slower retries/operator recovery; it never silently discards accepted work.
4. The Python worker validates event schema and bounds, retrieves only the canonical YouTube video, and writes it to a controlled temporary path. FFmpeg is invoked as an argument array without a shell, with accurate seeking, for example `ffmpeg -nostdin -hide_banner -v error -ss 330 -i input -t 195 -vn -map 0:a:0 -c:a libmp3lame -q:a 2 output.mp3`, where offsets and paths are validated/generated internally. The completed file is uploaded to a deterministic private object key.
5. The worker publishes `audio.extraction.completed` or a safe `audio.extraction.failed` event with the same request ID. It acknowledges input only after the stored result and outcome publication are durable/confirmed. On redelivery, a valid existing object is reused and its terminal outcome is re-published idempotently.
6. A .NET hosted consumer validates/deduplicates outcome events, commits terminal state and notification before signaling the SignalR group for that request. Angular recovers missed state via the request-specific status endpoint.

### RabbitMQ Topology

| Entity | Name | Binding / purpose |
|---|---|---|
| Topic exchange | `media.events` (durable) | All versioned media lifecycle events |
| Worker queue | `audio.extraction.worker` (durable) | Bound with `audio.extraction.requested` |
| Notification queue | `audio.extraction.notifications` (durable) | Bound with `audio.extraction.completed` and `audio.extraction.failed` |
| Dead-letter exchange | `media.events.dlx` (durable topic) | Receives exhausted or poison work |
| Dead-letter queue | `audio.extraction.dead-letter` (durable) | Bound to exhausted extraction messages and malformed outcomes for operations review |

Use persistent messages, manual acknowledgement, publisher confirms, finite retry queues with TTL and dead-letter routing, and stable event IDs. Worker processing has three automatic retries with increasing delays before a terminal failure/dead-letter decision. Outbox publication attempts use bounded backoff per cycle but retain accepted rows and alert if the broker remains unavailable. Exact delays and broker image patch version are configuration locked during implementation. Do not acknowledge work on a failed storage write or unconfirmed outcome publish.

### API Contracts

- `POST /api/audio-extractions`: JSON `{ "url": string, "start": "HH:MM:SS", "end": "HH:MM:SS" }`; optional `Idempotency-Key` for safe client retries. Success is `202` with `{ "requestId": string, "status": "accepted", "message": string }`.
- `GET /api/audio-extractions/{requestId}`: retrieve only that capability's persisted request status and terminal notification; invalid/unknown IDs return the same not-found shape.
- `GET /api/audio-extractions/{requestId}/audio`: return a short-lived signed object reference or redirect only when complete; never reveal permanent public storage URLs.
- Use RFC 9457 Problem Details with stable `code` extensions for invalid URL/interval, `VIDEO_NOT_FOUND`, upstream unavailable, rate limit, and service unavailable.
- The official YouTube Data API key is server-side configuration and must never be sent to Angular.

### Event Contracts

All events use JSON with `eventId` (UUID), `eventType`, `schemaVersion`, `requestId`, and UTC `occurredAt`. JSON Schemas under `contracts/events/` are authoritative.

- `audio.extraction.requested`: normalized `videoId`, integer `startSeconds` / `endSeconds`, output format, and request ID. Never include the raw user URL or API key.
- `audio.extraction.completed`: object key/reference, content type, duration, size, and checksum.
- `audio.extraction.failed`: stable public `code`, safe user-facing `message`, and retryable/terminal classification; no stack traces or raw source URLs.
- SignalR hub `/hubs/audio-extractions`: client invokes `Subscribe(requestId)` / `Unsubscribe(requestId)`; server emits `extractionUpdated` with request ID, terminal status, safe message, and available result action. The server verifies request ID existence before group subscription. The request ID is a capability and must not be written to access logs or analytics.

## Security and Resilience

- Accept only explicit YouTube host/URL forms and canonical video IDs. The API calls only the fixed YouTube Data API host with an identifier; the worker constructs its own canonical source URL. Reject arbitrary URLs, private/IP-literal hosts, unsafe redirects, and unexpected protocols.
- Treat `requestId` as a secret. Generate 256 random bits, avoid sequential IDs and global list endpoints, and log only a non-reversible digest plus non-capability event IDs. Redact the request ID path segment from application, reverse-proxy, and analytics logs. Use a short-lived signed result link with no-store/no-referrer handling. Configure CORS to the known Angular origin and rate-limit requests.
- Keep API keys and storage/broker credentials in environment/secret configuration. Do not expose exception details, signed URLs, credentials, or media in logs or failure notifications.
- Run FFmpeg with an argument vector and fixed executable, controlled temp paths, time/memory/disk limits, and no shell. Enforce source duration, selected interval, downloaded bytes, output bytes, and execution timeout.
- Retry transient broker, network, and storage errors a finite number of times. Permanent source errors become terminal failures. Dead-letter poison events. Persist state before notifying.
- Use deterministic object keys derived from the request ID and atomic final writes. Redelivered completed work validates the existing artifact and republishes the same logical completion; notification processing is deduplicated by request/event identity.
- Local rate limiting is per client IP and initially supports the single API instance. Before horizontal API scaling, provide distributed rate limiting and a SignalR scale-out/backplane.
- YouTube metadata checks do not guarantee worker retrieval. Production use must comply with YouTube terms, content rights, and applicable law; the worker does not bypass video access restrictions.

## Testing and Validation Strategy

- **Angular**: validators for supported URL/timestamps and bounds; accepted/error UI; persisted request ID recovery; SignalR connect/reconnect/subscription and result action. Run `npm test` and `npm run build` in `apps/web`.
- **API**: unit tests for normalization, YouTube metadata response mapping, IDs, limits, and error contracts; integration tests for request/outbox atomicity, durable acceptance, retry-safe idempotency, status/content access, broker outage recovery, and outcome deduplication. Run `dotnet test` for the API test project.
- **Worker**: pytest for message/schema validation, section boundaries, FFmpeg arguments, deterministic object keys, redelivery after artifact write, publisher-confirm failure, retries, poison messages, and safe failure data. Integration tests use Compose RabbitMQ/MinIO and a small controlled media fixture.
- **Contracts**: validate request/completion/failure examples and producer/consumer payloads against JSON Schema in both .NET and Python tests.
- **End-to-end**: Compose starts PostgreSQL, RabbitMQ, MinIO, API, Angular, and worker. Submit a controlled valid request, observe `202`, confirm one stored output and terminal status, disconnect/reconnect the browser, and verify notification recovery. Also exercise broker interruption, worker restart, and invalid request paths.
- No current GitHub Actions workflow exists. Add or define a feature CI workflow to run Angular build/tests, .NET tests, Python tests, and contract validation; keep external YouTube calls and media fixtures out of unit tests.

## Technical Risks and Open Operational Inputs

1. **YouTube API key/quota and media retrieval policy**: product owner must supply key/quota and confirm use is permitted. Metadata checks can pass while download later fails; communicate that as an asynchronous terminal outcome.
2. **Production services**: provider endpoints/credentials for PostgreSQL and S3-compatible storage are not in the repository; local Compose uses Postgres and MinIO. Provider-neutral interfaces preserve portability.
3. **Unauthenticated access**: possession of a request ID grants access. High entropy, no listing, no routine logs, CORS, rate limiting, and short-lived artifact links are mandatory.
4. **SignalR scale-out**: no real-time infrastructure exists. Initial design supports one API replica; horizontal scale requires a supported backplane/shared delivery topology.
5. **Resource abuse/cost**: YouTube requests, downloads, and FFmpeg can consume network/CPU/storage. Configure and monitor request rates, maximum durations/bytes, concurrency, deadlines, and retention.
6. **Retention and limits**: seven-day artifact retention, six-hour source limit, and rate limit are proposed starting values and need product confirmation before public launch.
7. **No existing CI/container practice**: compose files, Dockerfiles, test projects, and CI workflow will establish new conventions and should be reviewed together before adoption.

## Project Structure

### Documentation (this feature)

```text
specs/002-youtube-audio-extraction/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    ├── audio-extractions.openapi.yaml
    ├── events/
    │   ├── audio-extraction-requested.schema.json
    │   ├── audio-extraction-completed.schema.json
    │   └── audio-extraction-failed.schema.json
    └── realtime-notifications.md
```

### Source Code (repository root)

```text
apps/
├── web/
│   └── src/app/features/audio-extraction/
│       ├── audio-extraction-page.*
│       ├── audio-extraction-api.service.ts
│       └── notifications/
├── api/
│   ├── AudioExtractions/
│   │   ├── Endpoints/
│   │   ├── Contracts/
│   │   ├── Persistence/
│   │   ├── Messaging/
│   │   └── Notifications/
├── api.tests/
│   └── AudioApi.Tests.csproj
└── workers/
    └── audio/
        ├── pyproject.toml
        ├── uv.lock
        ├── Dockerfile
        ├── src/audio_worker/
        └── tests/

apps/api/Dockerfile
apps/web/Dockerfile
docker-compose.yml
.env.example
.github/workflows/ci.yml
docs/architecture/audio-extraction.md
```

**Structure Decision**: Keep the existing Angular app and .NET API, add the requested independent Python worker as the third app, put .NET feature-specific code under a focused `AudioExtractions` boundary, and add one root Compose/CI integration surface plus the feature architecture document. The tree describes intended files; it is not claiming they already exist.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| New Python app and worker image | Explicit user requirement and media workload must not block HTTP requests | Running media work in the API would violate the independent worker boundary and tie CPU/network processing to API availability |
| PostgreSQL plus transactional outbox | Needed for durable request acceptance, status/reconnect recovery, and no-loss broker retries | In-memory state or direct DB+Rabbit dual writes cannot safely survive restarts or ambiguous publish confirmations |
| RabbitMQ, object storage, and Compose/CI | Required asynchronous broker, persistent media result, and reproducible local workflow have no existing infrastructure | Local-only files or manually installed services are not durable/reproducible; no existing platform service is available to reuse |
| SignalR and Angular SignalR client | Required real-time browser notification path with no existing mechanism | Polling is explicitly dispreferred; worker-to-browser coupling would violate service boundaries |
