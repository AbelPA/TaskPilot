# Data Model: YouTube Audio Extraction

**Branch**: `release/extrator_audio_video`
**Plan**: [plan.md](plan.md)

## AudioExtractionRequest

Represents one user-submitted extraction and its durable lifecycle.

| Field | Type | Rules |
|---|---|---|
| `requestId` | string | Primary lookup capability; 32 cryptographically random bytes encoded as 43-character base64url without padding |
| `videoId` | string | Canonical 11-character YouTube video identifier; do not persist the submitted URL |
| `startSeconds` | integer | `>= 0`; normalized from `HH:MM:SS` |
| `endSeconds` | integer | `> startSeconds`; no greater than the verified source duration |
| `outputFormat` | enum | Initially `mp3` |
| `status` | enum | `accepted`, `completed`, or `failed`; terminal states do not transition back |
| `createdAt` | UTC timestamp | Set when request and outbox record commit |
| `updatedAt` | UTC timestamp | Updated when terminal outcome is recorded |
| `sourceDurationSeconds` | integer | Verified duration; must not exceed the configured six-hour source limit |
| `resultObjectKey` | string, nullable | Set only after successful completion; private deterministic object key |
| `resultSizeBytes` | integer, nullable | Positive and bounded by configured output-size limit |
| `resultChecksumSha256` | string, nullable | SHA-256 hex digest of the persisted result |
| `resultDurationSeconds` | integer, nullable | Must match the requested interval within media-container precision |
| `failureCode` | string, nullable | Stable public code; no exception or infrastructure detail |
| `failureMessage` | string, nullable | Safe message for the requesting client |
| `expiresAt` | UTC timestamp, nullable | Set to terminal outcome time plus seven days; drives request/result cleanup without expiring queued work |
| `idempotencyKeyHash` | string, nullable | Unique per supported client retry scope; store a hash rather than the raw optional header value |

### Validation and transitions

- Validate URL host and format, then store only the canonical video ID.
- Require `startSeconds >= 0`, `endSeconds > startSeconds`, `endSeconds <= sourceDurationSeconds`, `endSeconds - startSeconds <= 1800`, and `sourceDurationSeconds <= 21600`.
- Create request and request-event outbox record in the same database transaction.
- New requests enter `accepted`. Only a valid completion or terminal failure event may transition them to `completed` or `failed`.
- Terminal transition is conditional on the request not already being terminal. Duplicate or stale terminal events do not create a second outcome.
- A missing or invalid capability receives the same not-found response; no global request-list operation is exposed.

## OutboxMessage

Stores an event that must eventually be delivered to RabbitMQ.

| Field | Type | Rules |
|---|---|---|
| `eventId` | UUID | Primary key; stable across publisher retries |
| `requestId` | string | Foreign key to `AudioExtractionRequest` |
| `eventType` | string | Initially `audio.extraction.requested` |
| `schemaVersion` | integer | Starts at `1` |
| `occurredAt` | UTC timestamp | Event creation time |
| `payload` | JSON | Validated event body; contains canonical ID and offsets, not user URL or secrets |
| `state` | enum | `pending`, `published` |
| `attemptCount` | integer | Non-negative; used for bounded publication retry and operations visibility |
| `nextAttemptAt` | UTC timestamp | Retry backoff schedule |
| `publishedAt` | UTC timestamp, nullable | Set only after RabbitMQ publisher confirmation |

## NotificationOutcome

Represents the durable terminal notification recoverable by a client holding the request capability.

| Field | Type | Rules |
|---|---|---|
| `requestId` | string | Primary key and foreign key to `AudioExtractionRequest`; permits one terminal outcome |
| `eventId` | UUID | Unique event identity; supports duplicate-event detection |
| `status` | enum | `completed` or `failed` |
| `safeMessage` | string | User-facing and non-sensitive |
| `createdAt` | UTC timestamp | Event consumption time |

The frontend keeps a private local list of request IDs so the bell can aggregate outcomes without requesting a global endpoint. Read/unread state is local to that browser because no user identity exists.

## AudioObject

An object-store artifact referenced by a completed request.

| Property | Type | Rules |
|---|---|---|
| `objectKey` | string | Deterministic, private key derived from `requestId`, for example `audio-extractions/{requestId}/audio.mp3` |
| `contentType` | string | Initially `audio/mpeg` |
| `durationSeconds` | integer | Extracted interval duration |
| `sizeBytes` | integer | Positive and within configured limit |
| `checksumSha256` | string | Confirms that an existing object found on redelivery is complete and reusable |
| `expiresAt` | UTC timestamp | Seven days after completion; lifecycle cleanup must remove the object |

Write to a private temporary path, verify the media, then upload the complete object to its deterministic key. Do not expose permanent public object URLs. The API issues only a short-lived signed retrieval reference after checking the request is completed and unexpired. MP3 duration may differ from the requested whole-second interval by codec/container frame precision.

## State and event flow

```text
HTTP request
  -> transaction: AudioExtractionRequest(accepted) + OutboxMessage(pending)
  -> 202 Accepted
  -> OutboxMessage(published) after RabbitMQ publisher confirm
  -> worker processes/extracts/stores artifact
  -> completion or failure event
  -> API conditional terminal update + NotificationOutcome commit
  -> SignalR notification to requestId group
```

RabbitMQ redelivery does not create another request. A worker that finds a valid artifact at the deterministic key reuses it and publishes the same completion event, using a deterministic terminal `eventId` derived from `requestId` and event type. Duplicate outcome events are deduplicated by `eventId` and the unique terminal `requestId`.
