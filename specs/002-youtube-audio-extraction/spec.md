# Feature Specification: YouTube Audio Extraction

**Feature Branch**: `release/extrator_audio_video`

**Created**: 2026-10-02

**Status**: Draft

**Input**: User description: Add asynchronous extraction of a requested audio interval from a YouTube video, with request validation, durable processing, secure result access, and real-time user notifications using the existing Angular and .NET applications and a new Python worker.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Request an audio extraction (Priority: P1)

As a user, I want to submit a YouTube video and a time interval so that I can receive only the audio for the portion I need without waiting for media processing to finish or signing in.

**Why this priority**: This is the core user value and creates the work item that all later processing and notification flows depend on.

**Independent Test**: Submit a valid, available YouTube URL and interval; verify that the request is accepted, a request identifier is returned, and the user can continue using the application while processing runs independently.

**Acceptance Scenarios**:

1. **Given** a user enters a supported YouTube URL and a valid interval without signing in, **When** they submit the form, **Then** the application accepts the request, displays confirmation, and returns a unique `requestId` without waiting for the audio file.
2. **Given** a malformed URL, invalid timestamp, reversed interval, interval outside the video, or interval beyond the configured maximum, **When** the user submits, **Then** the application explains the validation error and does not enqueue work.
3. **Given** a well-formed URL for a video that cannot be found or accessed, **When** the user submits, **Then** the API returns the established video-not-found or unavailable-video error and does not enqueue work.
4. **Given** the messaging service is unavailable or does not confirm durable acceptance, **When** the request is submitted, **Then** the API reports that it could not accept the request and does not return a success-shaped `202 Accepted`.

### User Story 2 - Receive progress outcomes (Priority: P1)

As a user, I want to be notified when my extraction succeeds or fails so that I know when the audio is ready without repeatedly checking the page.

**Why this priority**: Asynchronous processing is not useful unless its outcome is reliably returned to the requesting user.

**Independent Test**: Accept a request, complete or fail its processing, and verify that a notification associated with the same `requestId` appears in the user's notification center without polling.

**Acceptance Scenarios**:

1. **Given** an accepted request completes successfully, **When** the backend receives its completion event, **Then** the client tracking that `requestId` receives a real-time notification with a protected way to play or download the result.
2. **Given** an accepted request fails permanently, **When** the backend receives its failure event, **Then** the client tracking that `requestId` receives a non-sensitive failure notification associated with that request.
3. **Given** the user is offline when an outcome is produced, **When** the user next opens or reconnects to the application, **Then** the notification center displays the persisted outcome.
4. **Given** an event references an unknown `requestId`, **When** the backend handles it, **Then** it does not create a notification or disclose a result.

### User Story 3 - Review and retrieve completed audio (Priority: P2)

As a user, I want to review extraction notifications and access the resulting audio later so that I can use the output after the original request page is closed.

**Why this priority**: Results and notifications must remain useful across navigation and temporary disconnections.

**Independent Test**: Complete a request, navigate away, return with the request's `requestId`, and play or download the result.

**Acceptance Scenarios**:

1. **Given** the user has an unread extraction notification, **When** they open the notification bell, **Then** they can identify the request outcome and its read state.
2. **Given** an extraction has completed, **When** a client presents its `requestId` to play or download, **Then** the application provides the persisted audio through a protected result reference without exposing the storage location.
3. **Given** a client presents an unknown or invalid `requestId`, **When** it attempts to access a notification or result, **Then** access is denied without exposing whether unrelated requests exist.

### Edge Cases

- YouTube short links, unsupported hosts, malformed video identifiers, redirects, and URLs containing unexpected query parameters must be handled using an explicit allowlist and canonical video identity.
- A video may be private, removed, age-restricted, region-restricted, or unavailable to the processing service even if its URL is syntactically valid; these cases must produce a safe user-facing failure.
- Timestamps that are malformed, negative, equal, reversed, or beyond the video's duration must be rejected before enqueueing.
- The worker may receive the same message again after creating the artifact but before acknowledging the delivery; the repeated delivery must not create a second logical result.
- Transient broker, download, media-processing, or storage failures must be retried within a bounded policy; exhausted or permanent failures must reach a terminal failure outcome.
- The user may disconnect before a completion event arrives; notification persistence and reconnect behavior must prevent silent loss.
- A message may be malformed or refer to an unknown request; it must not be acknowledged as successful work or leak sensitive details.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The application MUST provide a dedicated “Extract Audio” page reachable from the existing navigation and containing fields for a YouTube URL, start time, and end time.
- **FR-002**: The form MUST validate required fields, supported YouTube URL formats, timestamp syntax, start-before-end ordering, video-duration bounds, and the configured maximum source and extraction durations before submission.
- **FR-003**: The application MUST allow extraction requests without requiring sign-in. It MUST issue a cryptographically unpredictable `requestId` and require that identifier to retrieve the corresponding status, notification, or result.
- **FR-004**: The API MUST expose `POST /api/audio-extractions` or the equivalent route required by existing API conventions. Its request MUST contain `url`, `start`, and `end`.
- **FR-005**: The API MUST validate the URL and interval, confirm that the referenced video exists and is accessible, and reject invalid requests before publishing work.
- **FR-006**: URL validation and media retrieval MUST be restricted to the supported YouTube hosts and canonical video identifiers. Redirects and resolved destinations MUST be checked to prevent server-side request forgery and access to private, local, or otherwise disallowed network destinations.
- **FR-007**: The API MUST create a unique, cryptographically unpredictable `requestId`, persist the request and its status for later lookup by that identifier, publish the request for asynchronous processing, and return `202 Accepted` only after durable message acceptance is confirmed.
- **FR-008**: The accepted response MUST include `requestId`, `status: "accepted"`, and a user-readable message. The HTTP request MUST NOT wait for audio extraction to finish.
- **FR-009**: The API MUST return the existing application error contract where available. At minimum, invalid request data MUST be a client error, unavailable videos MUST be reported distinctly, and broker unavailability MUST not be represented as an accepted request.
- **FR-010**: The request event MUST carry an event identifier, event type, schema version, UTC occurrence time, `requestId`, and the minimum validated data required by the worker. It MUST not expose credentials, internal diagnostics, or unnecessary user data.
- **FR-011**: Media work MUST be decoupled from request handling through RabbitMQ using a Topic Exchange for media events, with routing keys for `audio.extraction.requested`, `audio.extraction.completed`, and `audio.extraction.failed`, unless repository analysis identifies a compatible existing topology to reuse.
- **FR-012**: A dedicated Python worker MUST consume extraction requests, validate them again, retrieve the approved video, use FFmpeg to extract only the requested interval, and persist the resulting audio using storage compatible with the established deployment architecture.
- **FR-013**: The worker MUST publish a completion event only after the result is durably stored. It MUST publish a failure event for terminal failures, using stable error codes and safe user-facing messages rather than stack traces or infrastructure details.
- **FR-014**: Completion and failure events MUST carry an event identifier, event type, schema version, UTC occurrence time, the original `requestId`, and the result reference or safe failure details. Event contracts MUST be versioned and documented.
- **FR-015**: The backend MUST consume completion and failure events, verify that each `requestId` refers to a known request, persist notification outcomes, and deliver them in real time to clients tracking that identifier. If the application is .NET and has no compatible mechanism, SignalR MUST be evaluated for real-time delivery.
- **FR-016**: The Angular notification center MUST include a bell, unread indication, accepted/success/failure states, and play/download actions for completed results. It MUST receive live updates without polling and recover persisted notifications after reconnect or reload.
- **FR-017**: Result references MUST remain persistent for the configured retention period and MUST provide protected access using the corresponding `requestId` or a short-lived access reference, without exposing permanent public object-storage URLs.
- **FR-018**: Processing MUST be idempotent by `requestId`. If a delivery is repeated after an artifact was created but before acknowledgement, the worker MUST reuse or recognize the durable result and safely ensure the completion outcome is published without creating a second logical artifact.
- **FR-019**: The messaging workflow MUST define bounded retries for transient failures, terminal handling for permanent or exhausted failures, dead-letter handling for poison messages, and acknowledgement only after the corresponding successful outcome is durably recorded and published.
- **FR-020**: If request publication cannot be durably confirmed, the API MUST return an error and MUST NOT leave the user with a success response for unqueued work. The design MUST address broker recovery and avoid silently losing accepted requests.
- **FR-021**: Input values MUST be passed to media tooling without constructing shell commands from user-controlled strings. Media retrieval, duration, interval, output size, execution time, and request rate MUST be constrained according to the approved architecture.
- **FR-022**: Structured logs and operational signals MUST allow authorized operators to trace a request from API acceptance through broker, worker, storage, event consumption, and notification using `requestId`, without logging secrets, full sensitive URLs, or media contents.
- **FR-023**: The feature MUST include automated coverage for API validation and response contracts; publication, routing, consumption, acknowledgement and retry behavior; worker download, interval extraction, storage, completion/failure and idempotency; and Angular form, menu, accepted response, errors, live notifications, and result access.
- **FR-024**: The development environment and documentation MUST let a new developer reproduce the required broker and worker/media-processing services using the repository's existing container and setup conventions.
- **FR-025**: Before implementation, the technical plan MUST inventory the existing Angular app, API, service boundaries, persistence, storage, messaging, notification and real-time mechanisms, tests, configuration, containers, CI/CD, and Graphify context. Any proposed new service, dependency, topology, or abstraction MUST be justified against existing patterns.
- **FR-026**: The technical plan MUST define the API and event schemas, broker topology, worker responsibilities, notification strategy, storage access, validation limits, retry/idempotency behavior, security controls, testing approach, deployment changes, risks, and an incremental implementation sequence. Implementation MUST wait for approval of the plan.
- **FR-027**: The feature MUST follow the repository's Spec Kit workflow. After significant architectural changes, Graphify MUST be refreshed using the repository's established workflow.
- **FR-028**: The implementation MUST extend the existing Angular 21.2/TypeScript 5.9.2 application in `apps/web` and the existing ASP.NET Core API targeting .NET 10 in `apps/api`; it MUST NOT create replacement frontend or API applications for this feature.
- **FR-029**: Media extraction MUST run in a new, independently deployable Python worker. The worker MUST use FFmpeg for interval extraction and RabbitMQ for asynchronous request and outcome events; the Python runtime version and dependency versions MUST be pinned in the approved technical plan.
- **FR-030**: The API MUST own HTTP request validation, request identifier creation, durable request publication, and result/notification integration. The Python worker MUST own media retrieval, FFmpeg execution, durable result storage, and publication of completion or failure events. The worker MUST NOT implement HTTP endpoints or Angular/SignalR-specific behavior.

### Technology and Component Boundaries

- **User interface**: Extend the existing Angular 21.2 application in `apps/web`, written in TypeScript 5.9.2. Add the extraction page and navigation item to the existing routing/navigation structure, and use its existing form, HTTP, and application-state conventions. Do not create a second frontend.
- **HTTP API**: Extend the existing ASP.NET Core Minimal API in `apps/api`, targeting .NET 10 and written in C#. Add the extraction endpoint and backend event/notification integration there, following the established API conventions. Do not create a second API application.
- **Media worker**: Create a separate Python worker process, independently runnable/deployable from the API. It consumes extraction requests from RabbitMQ, retrieves the approved YouTube media, invokes FFmpeg to extract the requested interval, stores the result, and publishes completion or failure events. It does not call the browser or push notifications directly.
- **Messaging**: Use RabbitMQ for decoupling the .NET API, Python worker, and .NET event consumer. Preserve the topic exchange and routing-key proposal below unless repository discovery finds an existing compatible broker topology.
- **Media processing**: FFmpeg is the media-processing engine; invocation must use safe argument passing and bounded resources. The worker's Python version, package/dependency manager, image/runtime, and dependency pins are to be selected and documented in the approved plan.
- **Real-time updates**: The existing Angular client receives outcomes through the .NET API. Evaluate SignalR for this .NET API if repository analysis finds no compatible real-time transport; the Python worker communicates only through RabbitMQ.
- **Persistent data and files**: The .NET API owns request/status/notification integration and the worker persists extracted media using the storage selected after checking existing infrastructure. The exact persistence provider, retention, result retrieval contract, container composition, and test tooling must be confirmed in the plan rather than assumed here.
- **Specialist agents**: Use `dev-angular` for the Angular/TypeScript application, `dev-dotnet` for the .NET 10/C# API and real-time integration, and `dev-python` for the Python worker and FFmpeg workflow. The tech lead coordinates cross-service contracts; security, review, and testing specialists are selected as relevant.

### API Contract *(initial proposal; align with existing conventions during planning)*

**Request**:

```json
{
  "url": "https://www.youtube.com/watch?v=XXXXXXXX",
  "start": "00:05:30",
  "end": "00:08:45"
}
```

**Accepted response (`202 Accepted`)**:

```json
{
  "requestId": "01KXXXXXXXXXXXX",
  "status": "accepted",
  "message": "The audio extraction request has been accepted."
}
```

**Video-not-found response (illustrative; preserve an existing error contract if present)**:

```json
{
  "code": "VIDEO_NOT_FOUND",
  "message": "The specified YouTube video could not be found."
}
```

The application MUST provide a protected way to retrieve or stream the completed result using its `requestId` capability. The exact retrieval route and whether the result is represented by an identifier or a short-lived URL MUST follow existing API and storage conventions established during planning.

### Event Contracts *(initial proposal; preserve existing event conventions where available)*

All events MUST include `eventId`, `eventType`, `schemaVersion`, `requestId`, and `occurredAt` (UTC).

- **`audio.extraction.requested`**: carries the validated canonical video identity and normalized start/end offsets required for processing, plus the `requestId`.
- **`audio.extraction.completed`**: carries a durable result reference and extracted duration; it MUST be safe to deliver to clients tracking the corresponding `requestId`.
- **`audio.extraction.failed`**: carries a stable public error code and safe user-facing message; it MUST exclude internal exception details.

### Proposed Messaging and Processing Constraints

- Reuse existing messaging infrastructure if available; otherwise use a durable RabbitMQ Topic Exchange named `media.events`, with routing keys `audio.extraction.requested`, `audio.extraction.completed`, and `audio.extraction.failed`.
- Provide a worker queue bound to extraction requests and a backend notification-consumer queue bound to completion and failure events. Queue durability, dead-letter routing, publisher confirms, retry limits, and acknowledgement points MUST be finalized in the technical plan.
- Use an independent Python worker with clear responsibilities for consuming/validating requests, downloading approved media, FFmpeg extraction, durable storage, and event publication. The worker MUST NOT depend on Angular or the real-time notification transport.
- Reuse existing persistent object storage and file-access conventions where possible. Do not assume local filesystem storage is durable in production.
- Reuse an established real-time notification mechanism; if none exists in a .NET API, evaluate SignalR. Persist notifications so a disconnected user can recover them after reconnect.

### Key Entities *(include if feature involves data)*

- **Extraction Request**: `requestId`, canonical video identity, requested interval, creation time, and lifecycle status.
- **Extraction Result**: associated request, durable audio reference, duration, output format, creation time, and retention/access policy.
- **Processing Event**: unique event identity, event type and schema version, correlated `requestId`, occurrence time, and event-specific data.
- **Notification**: correlated `requestId`, outcome, safe message, read state, and creation time.

### Security, Resilience, and Operational Constraints

- Only approved YouTube URLs may be processed. Validate host, redirects, DNS/resolved destinations, video identity, and duration to prevent SSRF and arbitrary network or file access.
- FFmpeg invocation MUST use safe argument passing, controlled input/output paths, resource/time limits, and no shell interpolation of user-controlled values.
- Authentication is not required for this feature. Treat the unpredictable `requestId` as a capability: do not expose request identifiers in public listings, logs, or unrelated responses; rate-limit request creation and lookup; and ensure invalid identifiers reveal no information about other requests.
- Apply bounded retry behavior to transient failures, avoid retrying known permanent failures, and route exhausted or malformed work to dead-letter handling for diagnosis.
- Make persisted artifacts and completion notifications idempotent by `requestId`; tolerate worker restarts and broker redelivery.
- Keep logs structured and traceable by `requestId` while excluding secrets, full signed URLs, and media payloads.

### Testing Requirements

- **API**: supported and invalid URLs, inaccessible video, malformed/equal/reversed/out-of-bounds times, interval/source limits, accepted response, no-wait behavior, durable event publication, request ID unpredictability/correlation, protected result lookup, and broker failure.
- **Messaging**: exchange/routing behavior, queue consumption, publisher confirmation, acknowledgement, bounded retries, dead-letter behavior, duplicate delivery, and restart recovery.
- **Worker**: message validation, media retrieval, exact requested interval, successful storage and completion event, download/FFmpeg/storage failures, failure event, and redelivery idempotency.
- **Angular**: navigation item, required fields and validation, submission feedback, accepted/error responses, notification bell, real-time completion/failure, reconnect recovery, and protected play/download by request ID.
- Prefer integration tests for broker, worker, and notification boundaries where repository conventions support them; use deterministic fixtures or controlled media samples.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For valid requests whose video check and durable enqueue succeed, at least 95% receive a `202 Accepted` response with a unique `requestId` within 5 seconds, without waiting for extraction.
- **SC-002**: 100% of invalid or inaccessible-video requests in the acceptance test suite are rejected before work is queued and receive an actionable, non-sensitive error.
- **SC-003**: In duplicate-delivery and crash-recovery tests, each `requestId` produces at most one logical audio result, and the final outcome is eventually available or explicitly failed.
- **SC-004**: In connected-client integration tests, at least 95% of completion and failure events appear in the notification center tracking the corresponding request within 5 seconds of backend event consumption, without polling.
- **SC-005**: In reconnect tests, 100% of persisted extraction outcomes are visible after the client reconnects with the corresponding `requestId`, and requests with invalid identifiers cannot retrieve outcomes or audio.
- **SC-006**: A new developer following the feature documentation can start the required local services and complete a representative extraction workflow using the documented setup.

## Assumptions

- The feature is restricted to user-provided YouTube videos and does not support arbitrary media URLs or batch extraction.
- Sign-in and user authentication are not required for submitting or tracking an extraction. The unpredictable `requestId` serves as the correlation and access capability for its status, notifications, and result.
- Time input is `HH:MM:SS`; implementation planning will set and document finite source-video, requested-interval, output-size, and execution-time limits. Until refined against project infrastructure, the maximum extracted interval is assumed to be 30 minutes.
- MP3 is the default output format unless the existing product or storage conventions establish a different supported format.
- Durable object storage, request persistence, notification persistence, retention, and real-time delivery will reuse existing project services where possible; this specification does not presume which provider is deployed.
- The examples for status codes, event envelopes, and exchange names are initial contracts derived from the request and may be adapted during planning only to preserve established project conventions without changing the user-visible behavior.
- A request is reported as accepted only after the broker has confirmed durable acceptance; if durable acceptance cannot be established, the user receives an error and may retry.
