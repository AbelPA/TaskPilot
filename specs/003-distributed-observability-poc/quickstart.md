# Quickstart: Local Distributed Observability

Use the existing audio-extraction flow to validate the end-to-end trace, correlated logs, and local metrics.

## Prerequisites

- Docker Engine with the Compose plugin and the ports used by the local stack available.
- A YouTube Data API v3 key and a rights-cleared, publicly accessible short test video.
- For native application tests: Node/npm matching `apps/web/package-lock.json`, .NET 10 SDK, Python 3.12, and `uv`.
- Do not commit the API key, `.env`, credentials, signed URLs, or media artifacts.

## Start the local environment

1. From the repository root, create `.env` from `.env.example` if needed and set local-only values. The startup script creates the file when absent.
2. Run (the YouTube API key is configured later on the Settings screen, `/settings`):

   ```sh
   ./scripts/start-local.sh
   ```

   Alternatively, start the stack with `docker compose up --build` after setting the required environment.
3. Open the UI at <http://localhost:4200> and the Grafana/OTel LGTM interface at <http://localhost:3000>. The browser-facing OTLP/HTTP collector listens locally on port 4319.
4. The browser sends OTLP/HTTP to `http://localhost:4319/v1/traces`; the local collector forwards application traces, logs, and metrics to LGTM. In Grafana, use Explore with the configured Tempo, Loki, and Prometheus-compatible data sources; allow a short export interval after the operation completes.

## Run and inspect one demonstration

1. Submit a valid short extraction request in the audio-extraction UI and wait until the worker completes it or reports a terminal failure.
2. In Grafana Explore, select Tempo and locate the trace by the operation's time window and service (`taskpilot-web`, `taskpilot-api`, or `taskpilot-audio-worker`). Copy its TraceId and inspect spans for the browser request, API handling/publication, and worker delivery/processing. The worker processing span must be a consumer span parented by the RabbitMQ publication span.
3. In Grafana Explore, select Loki, filter for `trace_id` equal to the copied TraceId, and verify matching API and worker log records. Confirm they show SpanId when applicable and keep the business `requestId` separate.
4. In Grafana Explore, select Prometheus and inspect `http.server.request.duration` (count and duration, grouped by route/method/status) and `audio_worker.processed` plus `audio_worker.processing.duration` (worker count and duration, grouped by bounded outcome/attempt). These provide request counts, durations and errors, and worker processing counts and durations. Use the instruments and bounded attributes defined in [the telemetry contract](./contracts/telemetry.md); no per-request identifier should appear as a metric label.
5. Repeat ten valid demonstration operations and verify one end-to-end trace and searchable correlated logs per operation.

## Verify recovery and telemetry independence

- Stop and recreate the Compose stack using the repository's documented shutdown/startup commands, then run another demonstration and confirm telemetry collection works without manual backend repair.
- Exercise a request with no valid trace context and a worker delivery without valid message context in automated tests; neither may attach to an unrelated operation.
- Temporarily make the OTLP receiver unavailable in a controlled local test. HTTP requests and worker processing must retain their ordinary business outcomes while exporter diagnostics make the telemetry failure visible.

## Automated checks

```sh
(cd apps/web && npm test -- --watch=false)
dotnet test apps/api.tests/AudioApi.Tests.csproj
(cd apps/workers/audio && uv run pytest)
```

Use the focused propagation/log/metric tests added alongside each application's existing tests. Use the Compose flow above for browser CORS, receiver routing, Grafana search, and cross-service integration; unit tests alone do not validate those boundaries.

## Troubleshooting

- **No browser span**: Check browser developer tools for OTLP network/CORS failures and confirm `http://localhost:4319/v1/traces` is reachable from the browser (container DNS names are not browser hostnames). The collector allows the local UI origins on port 4200 only.
- **Trace ends at the API**: Inspect outbox trace context and RabbitMQ `traceparent`/`tracestate` headers, then check the worker extraction logs.
- **Logs do not match a trace**: Confirm the log exporter/handler is enabled and query by the actual TraceId, not the business `requestId`.
- **No metrics**: Wait for the export interval, check the OTLP receiver and service resource names, and verify the metric instrument names/units from the telemetry contract.
- **Sensitive data appears**: Stop the local demonstration, remove or restrict the offending attribute/log source, and do not share or commit the telemetry output.
