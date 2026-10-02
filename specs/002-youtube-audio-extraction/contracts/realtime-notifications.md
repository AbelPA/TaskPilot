# Real-Time Notification Contract

**Transport**: ASP.NET Core SignalR at `/hubs/audio-extractions`; Angular uses the official `@microsoft/signalr` client. Python communicates only through RabbitMQ.

## Client-to-server methods

### `Subscribe(requestId)`

- Requires the 43-character base64url request capability.
- The API verifies that the request exists and is not expired before adding the connection to a server-side group for that request.
- Unknown/expired IDs return a generic not-found/denied hub error.
- Request IDs and hub arguments must be redacted from routine request/connection logs.

### `Unsubscribe(requestId)`

Removes the connection from that request's group. Disconnect also removes the connection from all groups.

## Server-to-client event

### `extractionUpdated(notification)`

```json
{
  "requestId": "43-character-random-base64url-capability",
  "status": "completed",
  "message": "The requested audio extraction has completed.",
  "createdAt": "2026-10-02T16:15:23Z",
  "result": {
    "audioPath": "/api/audio-extractions/{requestId}/audio",
    "durationSeconds": 195,
    "contentType": "audio/mpeg"
  }
}
```

For a failed request, `status` is `failed`, `message` is non-sensitive, and `result` is absent.

## Recovery behavior

The backend commits the terminal request state/notification before raising `extractionUpdated`. On first load and after reconnect, Angular queries `GET /api/audio-extractions/{requestId}` for every saved request ID, then subscribes again. This closes the race where completion happens before the SignalR subscription and supports offline clients without periodic polling.

The request ID is a bearer capability: only keep it in the requesting browser's private local storage, do not put it in shareable URLs, do not enumerate requests, and expire result access after the configured retention. If API replicas are introduced, the deployment must add a supported SignalR scale-out mechanism before routing clients across replicas.
