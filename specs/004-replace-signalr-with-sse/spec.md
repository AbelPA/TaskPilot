# Feature Specification: Replace SignalR with SSE for Audio Notifications

**Feature Branch**: `agents/replace-signalr-with-sse-spec`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "crie uma spec para substituir o signalR por SSE"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Receive extraction updates in real time (Priority: P1)

As a user following one or more audio extraction requests, I want to receive their status outcomes in the notification center without refreshing the page, so I can see when each extraction completes or fails.

**Why this priority**: Timely outcomes are the primary value of the existing real-time notification experience and must remain available after the transport change.

**Independent Test**: Start a tracked extraction while the notification center is open and verify its terminal outcome appears for that request without a manual page refresh.

**Acceptance Scenarios**:

1. **Given** the user is tracking an active extraction and the notification connection is available, **When** that extraction reaches a terminal outcome, **Then** the notification center displays the matching outcome without a manual refresh.
2. **Given** the user tracks multiple extractions, **When** one extraction changes state, **Then** the notification is associated only with that extraction and other requests remain unchanged.
3. **Given** the user is not using a browser, **When** the application starts, **Then** no browser-only notification connection is attempted.
4. **Given** the user opens the notification experience in a browser, **When** a live connection is established, **Then** updates are delivered through the requested one-way SSE transport and the audio notification path does not establish a SignalR connection.

### User Story 2 - Recover notifications after disconnection (Priority: P1)

As a user whose network connection is interrupted, I want the application to reconnect and reconcile saved requests with their latest available status, so temporary disconnections do not make me miss an extraction outcome.

**Why this priority**: A live stream alone cannot guarantee delivery while a client is offline; the existing status-recovery behavior must continue to provide a reliable result.

**Independent Test**: Interrupt the client connection while an extraction completes, restore connectivity, and verify the saved request converges to the latest available status without duplicate outcomes.

**Acceptance Scenarios**:

1. **Given** a tracked extraction changes state while the client is disconnected, **When** the client reconnects, **Then** the latest available status is recovered for that request.
2. **Given** the client reconnects more than once, **When** the same saved requests are restored, **Then** each request remains subscribed once and its outcome is not duplicated in the notification center.
3. **Given** the notification stream cannot be established, **When** recovery is attempted, **Then** the user receives a clear connection or recovery error and can still obtain the current status through the existing status-recovery flow.

### User Story 3 - Keep request notifications private (Priority: P1)

As a user, I want updates limited to requests I am authorized to track, so another request identifier cannot expose my extraction status or results.

**Why this priority**: Request identifiers grant access to request status; transport migration must not weaken the current request-level access boundary.

**Independent Test**: Subscribe to two distinct valid requests and verify each client receives only the outcomes for its authorized request; also verify malformed, missing, or expired identifiers reveal no request data.

**Acceptance Scenarios**:

1. **Given** a user is tracking one request, **When** another request receives an update, **Then** the user receives no status or result data for the unrelated request.
2. **Given** a subscription uses a malformed, unavailable, or expired request identifier, **When** access is attempted, **Then** the request is rejected consistently with the existing status access behavior and no notification data is disclosed.
3. **Given** an update is received, **When** the client processes it, **Then** it is accepted only if it belongs to a locally tracked request and has a supported status shape.

### Edge Cases

- A terminal outcome is recorded while the client is offline or reconnecting; recovery must return the latest available state.
- The same update is delivered more than once or after the client has already recovered that state; the notification center must not show duplicate outcomes.
- A tracked request expires or becomes unavailable while a stream is open; its data must stop being exposed and the user must receive the existing unavailable or expired-state handling.
- Several tracked requests are updated concurrently; each outcome must remain associated with its own request.
- The browser closes or navigates away; notification resources must be released and must not keep a stale subscription alive.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The audio extraction notification experience MUST replace its SignalR transport with one-way Server-Sent Events (SSE).
- **FR-002**: The notification experience MUST continue to deliver the existing supported audio extraction outcomes to the notification center without requiring a manual page refresh.
- **FR-003**: Each notification MUST be scoped to its associated request; a client MUST NOT receive data for an unrelated request.
- **FR-004**: The service MUST validate request availability and expiry before providing a request subscription, consistently with the existing status-access behavior.
- **FR-005**: The application MUST restore notifications for saved request identifiers after a successful connection or reconnection and reconcile each request against its latest available status.
- **FR-006**: Reconnection and repeated delivery MUST NOT create duplicate visible outcomes or multiple effective subscriptions for the same request.
- **FR-007**: If the stream is disconnected or unavailable, the interface MUST communicate the connection or recovery failure and continue to expose the existing status-recovery path.
- **FR-008**: The client MUST release notification resources when a request is no longer tracked or the browser session ends.
- **FR-009**: Notification status meaning and user-visible outcomes MUST remain compatible with the current audio extraction workflow; changing extraction processing, persistence, or result access is outside this feature.
- **FR-010**: SignalR MUST no longer be required by the audio extraction notification path after migration; unrelated features, if any, are outside this scope.

### Key Entities *(include if data involved)*

- **Tracked extraction request**: An audio extraction identified by an opaque request identifier that the current client stores to recover status and receive request-scoped updates.
- **Notification outcome**: The supported status and associated user-facing information for one tracked extraction, including its request identity and terminal result or failure information.
- **Notification stream**: A temporary, one-way connection through which the client receives outcomes for authorized tracked requests and which can be re-established after interruption.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a stable connection test, 100% of generated extraction terminal outcomes appear in the notification center within 2 seconds, without a manual page refresh.
- **SC-002**: After a successful reconnection, 100% of saved requests converge to their latest available status within 5 seconds.
- **SC-003**: Across request-isolation tests, 0 notification payloads for unrelated requests are delivered to a client.
- **SC-004**: Repeated delivery and repeated reconnection produce 0 duplicate visible outcomes for the same request state.
- **SC-005**: Users can distinguish a connected notification experience from a disconnected or failed recovery state and can still retrieve current status using the existing recovery flow.

## Assumptions

- The scope is the existing audio extraction notification experience; source inspection found its SignalR hub, publisher, and Angular connection service, with no other SignalR consumer identified in the relevant application paths.
- Current request identifiers and status-access rules remain the authorization boundary; this feature does not introduce user accounts or change the capability model.
- Persisted/current status remains the source of truth when the client reconnects; live delivery is an optimization for timely updates, not the only record of an outcome.
- Existing extraction request, processing, result retrieval, and notification-center behavior remain unchanged except where necessary to replace the notification transport and preserve recovery.
