# Data Model: Local Distributed Observability

This feature adds no business database tables or event-body fields. The following are logical telemetry entities produced and transported by OpenTelemetry.

## Trace

- **Fields**: `trace_id` (32 lowercase hexadecimal characters, non-zero), service/resource attributes, start/end timestamps, status.
- **Relationships**: Contains one or more spans. TraceId is a technical identifier and is not the audio-extraction `requestId` or an optional business CorrelationId.
- **Validation**: A malformed or absent incoming W3C context is not reused; processing remains observable as a new root trace when sampled.

## Span

- **Fields**: `span_id` (16 lowercase hexadecimal characters, non-zero), `trace_id`, parent SpanId where present, operation name, start/end timestamps, status, allowlisted attributes.
- **Relationships**: HTTP request and API operations, outbox publication, RabbitMQ delivery, and worker processing are represented by spans associated with one trace when valid W3C context is propagated.
- **Validation**: Do not set secrets, full request bodies, credentials, signed URLs, or unrestricted user-provided values as attributes.

## W3C Trace Context

- **HTTP**: `traceparent` and optional `tracestate` headers on browser-to-API requests.
- **RabbitMQ**: `traceparent` and optional `tracestate` message headers, captured with the durable outbox record and injected at publication.
- **Validation**: Follow the W3C Trace Context format and OpenTelemetry propagator validation. Missing or invalid context starts a new trace; it must never connect to a previous operation accidentally.

## Correlated Log Record

- **Fields**: timestamp, severity, message, service name, `trace_id` and `span_id` when an active valid span exists, optional business `requestId`, and non-sensitive error/outcome fields.
- **Relationships**: A record is searchable by TraceId and associated with the active span; a business identifier remains a separate field.
- **Validation**: Enrichment is automatic across ordinary and error/retry log calls. Never include secrets, credentials, full URLs, raw message bodies, or sensitive request content.

## Metrics

| Measure | Instrument | Bounded attributes |
|---|---|---|
| HTTP request count | Count observations from ASP.NET Core server request instrumentation | Route template, method, status code, service |
| HTTP request duration | `http.server.request.duration` histogram | Route template, method, status code, service |
| HTTP errors | HTTP requests grouped by error status (5xx, and relevant failed outcomes where available) | Route template, method, status code, service |
| Worker processing count | Counter for handled audio extraction deliveries | Outcome and bounded retry/final-attempt classification |
| Worker processing duration | Histogram in seconds | Outcome and bounded retry/final-attempt classification |

Metric attributes must not contain TraceId, SpanId, RequestId, CorrelationId, video URL, or any other per-operation value. Metric names must follow OpenTelemetry conventions and be documented in the telemetry contract.
