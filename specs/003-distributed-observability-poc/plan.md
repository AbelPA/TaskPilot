# Implementation Plan: Local Distributed Observability

**Branch**: `003-distributed-observability-poc` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-distributed-observability-poc/spec.md`

## Summary

Instrument the existing Angular audio-extraction flow, ASP.NET Core API, RabbitMQ outbox/consumer boundary, and Python worker as one observable operation. Replace the browser's synthetic `traceparent` with real OpenTelemetry spans, preserve W3C context through the existing outbox headers, correlate API and worker logs, add worker processing metrics, and expose OTLP telemetry through the local Grafana/OTel LGTM stack. Document a repeatable local demonstration without changing business payloads or introducing a separate domain.

## Technical Context

**Language/Version**: Angular 21.2 / TypeScript 5.9; ASP.NET Core / .NET 10; Python 3.12 (worker requirement is `>=3.12,<3.13`).

**Primary Dependencies**: Angular `HttpClient`; OpenTelemetry JS browser SDK and OTLP exporter/instrumentation (to be added); existing .NET OpenTelemetry 1.12 packages and RabbitMQ.Client 7.1; existing Python OpenTelemetry API/SDK and OTLP HTTP exporter plus aio-pika; Docker Compose with `grafana/otel-lgtm:0.8.0`.

**Storage**: No new persistence. PostgreSQL's existing outbox already preserves W3C trace context for asynchronous publication; Grafana/LGTM stores local telemetry.

**Testing**: Angular `npm test`; API `dotnet test apps/api.tests/AudioApi.Tests.csproj`; worker `uv run pytest` in `apps/workers/audio`; Docker Compose end-to-end verification using the documented demonstration flow.

**Target Platform**: Local browser and Docker Compose services on a developer workstation.

**Project Type**: Existing multi-project web application: Angular SPA, ASP.NET Core API, Python background worker, RabbitMQ, and local observability services.

**Performance Goals**: No numeric latency or throughput target is defined by the PoC specification. Record request and worker durations and keep telemetry export non-blocking to business processing.

**Constraints**: W3C Trace Context and OpenTelemetry only; keep TraceId, SpanId, CorrelationId, and business RequestId distinct; do not log credentials, secrets, full request payloads, or sensitive video URLs; telemetry export failure must not cause a request or worker operation to be reported as failed; local PoC only.

**Scale/Scope**: One local developer environment and the existing audio-extraction request path; no production HA, cloud deployment, or retention requirements.

## Constitution Check

The repository constitution is still the unfilled template (`.specify/memory/constitution.md`), so it defines no ratified principles or enforceable gates. No constitution violation can be established from that template. This design follows the populated engineering standards: explicit boundaries, backward-compatible contracts, automated tests, documented decisions, and sensitive-data protection. Integration coverage is required for the HTTP-to-RabbitMQ-to-worker path.

**Gate before Phase 0**: PASS with no ratified constitution constraints; use the feature requirements and `docs/engineering-standards.md` as the applicable project guidance.

## Project Structure

### Documentation (this feature)

```text
specs/003-distributed-observability-poc/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── telemetry.md
```

### Source Code (repository root)

```text
apps/web/src/app/features/audio-extraction/  # Replace synthetic trace header with SDK-backed HTTP spans
apps/web/src/app/                            # Browser OpenTelemetry initialization
apps/api/AudioExtractions/                   # API spans, correlated logs, request metrics
apps/api.tests/AudioExtractions/             # API propagation and telemetry behavior tests
apps/workers/audio/src/audio_worker/         # Consumer spans, correlated logs, processing metrics
apps/workers/audio/tests/                    # Worker propagation, retry, and metric tests
docker-compose.yml                           # Browser-reachable OTLP receiver and LGTM service wiring
scripts/start-local.sh                       # Retain/document reproducible local startup
README.md                                    # Link or update the observability run instructions
```

**Structure Decision**: Keep instrumentation in the existing application boundaries and local Compose stack. Do not create a new service or persistence model unless implementation proves the bundled OTLP receiver cannot satisfy browser CORS and routing requirements; any such change must remain local and preserve the spec's intermediate-OTLP-layer requirement.

## Design Gates

- **Trace continuity**: W3C context starts in a real browser span, reaches ASP.NET Core, is captured by the transactional outbox, and is extracted by the worker. A missing or invalid context starts an independent operation rather than reusing stale context.
- **Privacy and business behavior**: No sensitive values are added to span attributes or logs. Telemetry export is best-effort and does not gate business processing.
- **Contracts and compatibility**: Preserve existing HTTP payloads and RabbitMQ message bodies; propagate context using standard HTTP headers and message metadata only.
- **Verification**: Cover valid/missing/invalid context, retry/failure behavior, correlated logs, metric recording, Compose routing, and the 10-operation end-to-end acceptance scenario.

**Gate after Phase 1**: PASS. The design uses the existing boundaries and standard protocols; it introduces no new persisted business entities, breaking payload changes, or unjustified architectural layers. Browser-to-collector CORS and telemetry fail-open behavior are explicit implementation verification points.

## Complexity Tracking

No constitution violations or additional architectural layers are proposed.
