# Telemetry Interface Contract

This document defines the observability contract at existing application boundaries. It does not change HTTP request/response bodies or RabbitMQ event schemas.

## HTTP context

- The browser uses W3C Trace Context (`traceparent`, optional `tracestate`) on the audio-extraction HTTP operation.
- The API accepts and continues valid W3C context. When the context is absent or invalid, it starts an independent trace where sampling allows.
- Browser instrumentation exports OTLP/HTTP to the local OTLP receiver. The receiver must permit only the local UI origin required by the development setup and must not expose a permissive production CORS policy.

## RabbitMQ context

- `traceparent` and optional `tracestate` are message metadata headers, not JSON event fields.
- The API captures the request context in the existing outbox and injects it when publishing, including delayed/retried publication.
- The worker extracts valid headers and creates a processing span in that context. Missing/malformed context is handled without claiming continuity with an unrelated trace.
- Business identifiers remain separate from W3C context; retries and terminal failures retain their product semantics.

## OTLP export and resource identity

- Applications export traces, logs, and metrics using OTLP/HTTP to the local Compose OTLP receiver; applications do not address Grafana, Loki, or Tempo directly.
- Stable service resource names: `taskpilot-web`, `taskpilot-api`, and `taskpilot-audio-worker`; use a common `service.namespace=taskpilot` where supported.
- Telemetry export is asynchronous and best-effort. Backend unavailability may reduce observability but must not change HTTP or worker business results.

## Logs

- API and worker records include active TraceId and SpanId when valid context is available.
- Include `requestId` only where useful and keep it distinct from TraceId and any CorrelationId.
- Errors and retry attempts are searchable without logging credentials, secrets, full request bodies, raw message contents, or full user-supplied URLs.

## Metrics

- Required measures: HTTP request count, duration, and error counts; worker processing count and duration.
- HTTP measures use ASP.NET Core server instrumentation, grouped by low-cardinality method, route template, status, and service attributes.
- Worker measures use a counter and duration histogram with bounded outcome/retry attributes. Do not use trace/span/request/correlation identifiers or URLs as metric attributes.
- Metrics must be queryable from the local Grafana/OTel LGTM UI alongside traces and logs.
