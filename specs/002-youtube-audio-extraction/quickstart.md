# Quickstart: YouTube Audio Extraction

This guide describes the local developer path for the current User Story 1 slice and the target path for the complete feature. The API accepts and durably queues requests; extraction consumption, SignalR notifications, status recovery, and protected audio retrieval are implemented in later user-story phases.

## Prerequisites

- Docker Engine with Compose.
- Node.js and npm versions compatible with the existing `apps/web` lockfile (npm 11.4.2).
- .NET 10 SDK.
- Python 3.12 and `uv` for native worker development.
- A YouTube Data API v3 key with quota, supplied through an environment variable; do not commit it.
- A rights-cleared, publicly accessible test video and a short interval for end-to-end validation.

## Start the local stack

1. Copy `.env.example` to `.env`, replace the local-only password placeholders, and supply a YouTube Data API v3 key with quota. Keep `.env` out of version control.
2. Start the complete local stack:

   ```sh
   docker compose up --build
   ```

   This starts Angular, the .NET API, PostgreSQL, RabbitMQ, MinIO, and the Python worker. FFmpeg is included in the worker image.
3. Open `http://localhost:4200`. Verify that the “Extrair áudio” page is available from the navigation.

## Validate User Story 1 acceptance

1. Submit a rights-cleared, publicly available test video with a valid `HH:MM:SS` interval.
2. Confirm the page displays an accepted message and an unpredictable 43-character `requestId` without waiting for extraction.
3. Inspect the API database and verify the request and one pending outbox event were committed together.
4. Confirm the outbox publisher sends the persistent `audio.extraction.requested` event after RabbitMQ is available. The current worker only declares durable queues; it does not consume extraction work yet.
5. Submit invalid URLs/timestamps and confirm no request or outbox row is created.

## Validate failure and recovery paths

- Use an unavailable video and verify the API returns the stable `VIDEO_NOT_FOUND` problem response.
- Stop RabbitMQ before a pending outbox publish; verify the request remains durably accepted and the event is retained for retry.
- Retry an identical request with the same `Idempotency-Key` and verify the original `requestId` is returned; reuse the key with different values and verify `409 Conflict`.
- Submit more than five requests in one minute from one IP and verify the remaining request receives `429 Too Many Requests`.

## Run targeted tests

```sh
(cd apps/web && npm ci && npm test && npm run build)
dotnet test apps/api.tests/AudioApi.Tests.csproj
(cd apps/workers/audio && uv sync --locked && uv run pytest)
```

The contract test job validates the OpenAPI document and the JSON Schemas under `contracts/`. External YouTube API calls and downloads are mocked in unit tests; a manual acceptance test uses only the configured rights-cleared test video.

## Expected outcomes

- A valid submission is durably accepted with an ID within five seconds after metadata validation.
- A broker interruption does not lose a committed request.
- Each accepted request has one durable pending outbox event and returns the same capability for an identical idempotent retry.
- A broker outage does not discard an accepted request or its pending event.
- The current slice does not yet produce terminal extraction outcomes; those checks become available after User Story 2.

## Safety

Never use private, paid, age-restricted, or otherwise unauthorized media as a test fixture. Do not commit API keys, signed URLs, `.env` files, or media artifacts. Apply the configured source-duration, interval, byte, time, and request-rate limits in all environments.
