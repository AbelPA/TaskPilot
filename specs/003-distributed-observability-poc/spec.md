# Feature Specification: Local Distributed Observability

**Feature Branch**: `release/observability`

**Created**: 2026-10-02

**Status**: Draft

**Input**: User description: Pasted text #1 — a local PoC for distributed tracing, correlated logs, and metrics across the frontend, API, RabbitMQ, and worker.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Follow an operation end to end (Priority: P1)

As a developer, I want to start an operation from the web interface and follow its asynchronous processing in a single trace so that I can locate delays and failures across services.

**Why this priority**: Trace continuity across synchronous and asynchronous boundaries is the central goal of the PoC.

**Independent Test**: Start a valid request from the interface, wait for the work to complete, and locate a single trace containing activities from the interface, API, and worker message processing, including publication and delivery through RabbitMQ.

**Acceptance Scenarios**:

1. **Given** the local environment is running and services are available, **When** a user starts an operation from the interface, **Then** the HTTP request and asynchronous processing are associated with the same TraceId.
2. **Given** the API publishes a message with trace context, **When** the worker consumes it, **Then** its processing activity continues the original trace instead of starting an unrelated trace.
3. **Given** an operation has completed successfully or failed, **When** a developer opens its trace, **Then** they can identify the sequence and duration of activities across the participating components.

### User Story 2 - Find logs by operation (Priority: P2)

As a developer, I want to search service logs by TraceId so that I can understand what happened without confusing technical identifiers with business identifiers.

**Why this priority**: Traces show the sequence of activities, while logs provide the detail needed to diagnose the outcome of each step.

**Independent Test**: Complete a test operation, copy its TraceId, and find the related API and worker logs, verifying that each record keeps any applicable business identifier separate.

**Acceptance Scenarios**:

1. **Given** logs were emitted during a traced operation, **When** a developer searches by TraceId, **Then** they find the related API and worker records.
2. **Given** an operation also has a business identifier or CorrelationId, **When** its data is displayed, **Then** those identifiers remain distinct from the TraceId and do not replace it.
3. **Given** an error occurred during processing, **When** a developer searches by TraceId, **Then** they can find the error logs related to the same operation.

### User Story 3 - Reproduce and observe the PoC locally (Priority: P3)

As a developer, I want the local environment to be reproducible and visible in a single observability dashboard so that I can run the scenario, inspect traces, and confirm the local stack is healthy.

**Why this priority**: The PoC must be easy to reproduce and inspect locally without extra configuration beyond the repository instructions.

**Independent Test**: Follow the local startup instructions, submit a request, and confirm the traces and metrics appear in the local observability stack without manually reconfiguring endpoints or data sources.

**Acceptance Scenarios**:

1. **Given** the project is started using the documented local process, **When** a user runs the demonstrating scenario, **Then** the observability interface is already configured to receive the telemetry without manual destination setup.
2. **Given** a trace has been emitted, **When** a developer opens the local interface, **Then** they can view the trace, inspect related spans, and confirm request/activity timings.
3. **Given** the environment is stopped and recreated, **When** the scenario is run again, **Then** it can be replayed without lone manual repairs to the observability backend.

### Edge Cases

- A broker restart or temporary transient failure should not create a separate root trace; activity should remain tied to the original operation when message redelivery occurs.
- A request that fails early should still emit a trace and logs with the same request context.
- Missing or partial trace metadata should not crash the service; it should degrade gracefully to a new trace when no valid W3C context exists.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operation started from the interface MUST produce a distributed trace that identifies its origin and activities in the frontend, API, and worker.
- **FR-002**: Trace context in HTTP calls MUST follow W3C Trace Context, including propagation of `traceparent` where applicable.
- **FR-003**: Asynchronous publication and consumption MUST carry W3C context in message metadata; the worker MUST extract it and associate its processing activity with the original trace.
- **FR-004**: The trace MUST make it possible to identify request, message publication/consumption, and worker-processing activities, including their relationships and durations.
- **FR-005**: Services MUST emit structured logs containing the active TraceId and SpanId when available, without requiring each log call to reconstruct that context manually.
- **FR-006**: Logs MUST be searchable by TraceId and keep TraceId, optional CorrelationId, and business identifier as distinct values.
- **FR-007**: The solution MUST provide metrics for request counts, durations, and errors, and worker-processing counts and durations.
- **FR-008**: Application telemetry MUST be routed through an intermediate OTLP-compatible layer; applications MUST NOT depend directly on Grafana, Loki, or Tempo.
- **FR-009**: The local observability interface MUST allow users to view traces, search logs by TraceId, and inspect the minimum metrics defined in this specification.
- **FR-010**: The local environment MUST start and be recreatable using Docker Compose, including the existing applications needed for the demonstration flow, RabbitMQ, and the observability platform.
- **FR-011**: Documentation MUST explain prerequisites, startup, how to run the demonstration scenario, how to access the observability interface, find a TraceId, query related logs, and troubleshoot common problems.
- **FR-012**: Instrumentation MUST use OpenTelemetry as the telemetry standard and MUST NOT introduce a proprietary correlation or context-propagation protocol.
- **FR-013**: Instrumentation and telemetry export MUST NOT record secrets or sensitive request content.

### Key Entities *(include if feature involves data)*

- **Trace**: A set of activities related to a distributed operation, identified technically by a TraceId.
- **Span**: A timed step in a trace, identified by a SpanId and associated with the trace context.
- **Trace context**: W3C metadata used to propagate activity relationships over HTTP and asynchronous messages.
- **Correlated log**: A structured record containing the active TraceId and SpanId when available, as well as non-sensitive operational data.
- **Business identifier**: An identifier for a product operation or entity (for example, RequestId), kept separate from the technical TraceId.
- **Metric**: An aggregated measure of request or worker-processing volume, duration, or errors.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 10 out of 10 consecutive valid demonstration operations, a developer can find a single TraceId covering the request started in the interface, the API, and the corresponding worker processing.
- **SC-002**: For 10 out of 10 demonstration operations, searching by TraceId finds correlated API and worker logs without confusing the TraceId with a business identifier.
- **SC-003**: After a demonstration operation completes, a developer can locate its trace and related logs within 1 minute.
- **SC-004**: The observability interface provides at least five verifiable measures: request counts, durations, and errors; and worker-processing counts and durations.
- **SC-005**: A developer following the prerequisites and instructions can start the environment, run the scenario, and access the results without individually configuring telemetry destinations.
- **SC-006**: After stopping and recreating the local environment according to the instructions, the demonstration scenario can be run again without manually repairing the observability platform.

## Assumptions

- The PoC will integrate with TaskPilot's existing asynchronous audio-extraction flow (request from the interface, API, RabbitMQ, and Python worker) rather than introducing a separate order domain solely for the reference text's `POST /orders` example.
- The stack is intended for local development and demonstration; high availability, cloud deployment, and production requirements are outside this version's scope.
- The business identifier already available in the existing flow will remain separate from TraceId; an additional CorrelationId will be used only if explicitly needed for business purposes.
- Grafana's `otel-lgtm` distribution is preferred to simplify the local backend, provided it supports the visualization and query requirements defined here.
- Developers will have Docker Compose and the documented local prerequisites and configuration; credentials and external service keys will not be included in logs or this specification.

## Out of Scope

- Grafana authentication, high availability, Kubernetes, TLS, and long-term retention.
- Complex alerting, production dashboards, autoscaling, cloud configuration, or a service mesh.
- Advanced sampling or integration with external observability vendors.
- Creating a separate order-related business feature solely for the demonstration.
