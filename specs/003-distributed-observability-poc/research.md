# Research: Local Distributed Observability

**Branch**: `003-distributed-observability-poc`
**Date**: 2026-10-14

## Repository findings

- The request flow already spans `apps/web`, `apps/api`, RabbitMQ, and `apps/workers/audio`; `docker-compose.yml` includes Grafana/OTel LGTM and publishes OTLP HTTP on port 4318.
- The API already uses OpenTelemetry ASP.NET Core, HTTP client, runtime, log, and OTLP exporters. `TraceContext` and the transactional outbox preserve `traceparent`/`tracestate` in message metadata.
- The browser currently creates a random W3C-shaped `traceparent` on submission, but it does not create or export a corresponding OpenTelemetry span. This synthetic parent is not proof of browser-side tracing.
- The Python worker already configures an OTLP trace exporter and extracts W3C headers for a consumer span. Trace context is logged explicitly at the start of processing, but other log records do not receive automatic trace fields and worker counters/duration histograms are not configured.
- Existing API and worker metrics cover framework/runtime instrumentation, but worker-processing measures and a clear, searchable end-to-end demonstration need explicit coverage.
- The project has established Angular, .NET, and pytest runners; `scripts/test.sh` runs all three application suites.
- The constitution file remains an unfilled template. The populated engineering standards require automated tests, explicit integration contracts, backward-compatible interfaces, and sensitive-data protection.

## Graph context and limits

The checked-in graph did not initially resolve observability terms. After an incremental refresh, an AST query exposed the API outbox publisher, its trace-header extraction, RabbitMQ topology, and associated outbox tests. The semantic refresh was partial, so no architectural decision relies on semantic graph results; source files and the feature specification were checked directly. The graph query supports the existing outbox path, not a claim that every telemetry edge is already implemented.

## Decisions

### 1. Use real OpenTelemetry browser spans instead of synthetic trace headers

- **Decision**: Initialize the OpenTelemetry JS browser SDK for the audio-extraction UI and instrument its HTTP request path. Export spans over OTLP/HTTP to the local OTLP receiver. Remove the manually generated random `traceparent` once the SDK owns propagation.
- **Rationale**: The current header has valid-looking W3C formatting but does not represent an exported browser span, leaving the originating request absent from the trace. A real SDK span lets standard propagation continue into ASP.NET Core and gives the browser activity a discoverable parent-child relationship.
- **Alternatives considered**: Keep the generated header (rejected: no matching span); implement a custom tracing protocol (rejected by FR-012); emit spans directly to Grafana/Tempo (rejected by FR-008 and violates backend independence).
- **Implementation check**: Browser requests use W3C propagation, and the local receiver accepts only the intended local UI origin as needed for CORS. Verify CORS/preflight and that no credentials or request bodies are exported.

### 2. Preserve W3C context through the API outbox and RabbitMQ metadata

- **Decision**: Keep the existing transactional outbox trace fields and `traceparent`/`tracestate` message headers. Instrument producer/consumer operations as OpenTelemetry spans and create a fresh root when no valid remote context exists.
- **Rationale**: The outbox provides durable, retry-safe context propagation and avoids changing event payload contracts. W3C is already supported by the .NET runtime and the Python OpenTelemetry propagators.
- **Alternatives considered**: Put trace identifiers in the JSON event body (rejected: protocol metadata belongs in headers and would couple business schemas to telemetry); add a separate CorrelationId protocol (rejected: not needed and must remain distinct from TraceId).
- **Implementation check**: Test accepted HTTP context, absent/malformed context, delayed outbox publication, retries, and worker consumption without falsely joining unrelated traces.

### 3. Export application telemetry through the existing local OTLP/LGTM stack

- **Decision**: Keep application exporters pointed at the Compose `otel-lgtm` OTLP endpoints and use the bundled OTLP-compatible collection path for local traces, logs, and metrics. Do not configure application services with direct Grafana, Loki, or Tempo destinations.
- **Rationale**: This reuses the existing local service, minimizes PoC infrastructure, and follows the OTLP intermediary requirement. The UI and collector routing must be checked together because browser telemetry has different network and CORS requirements from containers.
- **Alternatives considered**: Direct backend exporters (rejected: vendor coupling); separate collector container (deferred unless the bundled receiver cannot meet the CORS/routing contract); cloud vendor exporter (out of scope).
- **Implementation check**: Verify traces, logs, and metrics reach Grafana via the local OTLP receiver and that stopping telemetry storage/export does not block business operations.

### 4. Correlate logs and add low-cardinality worker metrics

- **Decision**: Add active TraceId/SpanId to API and worker log records through OpenTelemetry logging integration/context enrichment, keep business RequestId separate, and record worker processed count and duration with bounded outcome attributes. Retain ASP.NET Core HTTP request duration/status metrics.
- **Rationale**: Automatic enrichment covers error and retry paths, not only the first worker log. Bounded metric attributes make the PoC queryable without creating one time series per request.
- **Alternatives considered**: Add identifiers manually at individual log call sites (rejected: misses future and failure-path logs); use RequestId as a metric label (rejected: unbounded cardinality); infer worker outcomes from logs (rejected: does not satisfy measurable metrics).

## Risks and mitigations

- Browser-to-collector OTLP can fail due to CORS or incorrect host addressing. Validate a browser-origin request through the published Compose port; if required, configure the collector with the narrowest local-origin allowlist.
- Outbox retries can happen after the originating request span ends. Preserve the captured trace context durably and represent publication/consumption as linked/continued spans according to the existing context model; tests must assert identity and avoid requiring an ambient request activity during retry.
- Telemetry exporters can fail during local startup or shutdown. Keep export asynchronous/best-effort, surface exporter diagnostics, and assert that product request/worker outcomes are unchanged.
- Request URLs and credentials may contain sensitive data. Avoid capturing bodies, full URLs, headers, or secrets; use route templates and allowlisted identifiers only.

## Unresolved clarifications

None. The spec sets the local PoC boundary and explicitly names the three application components, transport, and telemetry outcomes. It supplies no numeric performance threshold; the plan records that absence rather than inventing one.
