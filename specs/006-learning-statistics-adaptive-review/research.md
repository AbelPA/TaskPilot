# Research: Learning Statistics and Adaptive Review

## Decision: Store evaluated responses as immutable history, not editable totals

**Rationale**: Totals, accuracy, recency, and session summaries must agree with actual grading results and remain correct after retries or catalog changes. Append-only answer records are the source of truth; aggregates are read projections. Keep a submission envelope separately so a request can be idempotent and linked to a review session.

**Alternatives considered**: Updating per-topic counters on every submit was rejected because retries and correction logic can drift from history. Persisting only session totals was rejected because it cannot support recent-performance windows or exercise-level selection.

## Decision: Use a stable submission ID as the idempotency key

**Rationale**: The existing POST endpoint has no identity for a logical request. The Angular client can create one ID and retain it across transport retries; a deliberate resubmission gets a new ID. Enforce uniqueness in PostgreSQL and return the originally evaluated result for the same key and payload. Reuse of a key with a different exercise, topic, or answer payload returns a conflict. Persist evaluation and answer history in one transaction, and do not return success if persistence fails.

**Alternatives considered**: Hashing answers was rejected because identical answers can be a deliberate later attempt. A server-generated key cannot recognize a retry after a lost response.

## Decision: Attribute each submission to one explicit study topic

**Rationale**: Exercises have a many-to-many topic relation, so inferring attribution from the exercise can double-count one response. Existing practice has topic-route context, and review selects each exercise for a topic. Pass that context with the submission, validate the association, and snapshot it. Resolve an omitted topic only when the exercise has exactly one topic; reject ambiguous attribution rather than inventing a topic.

**Alternatives considered**: Copying every answer to every linked topic duplicates a response. Choosing the first linked topic treats display order as learner intent.

## Decision: Persist attempts and review selection; derive statistics and summaries

**Rationale**: Persisting the generated session and its category/reason preserves it across reloads and links answers to the correct session. Derive topic statistics and completed-session summaries from answer history to avoid a second mutable source of truth. Retain IDs and label snapshots for historical context when catalog items are deactivated.

**Alternatives considered**: Browser-only session state loses associations on reload. Persisted aggregate counters can drift from source history.

## Decision: Use conservative, deterministic priorities and target distributions

**Rationale**: Apply the specified 50% historical error, 30% recent error (five most recent evaluated answers), and 20% recency components. Normalize each to `[0,1]`; use `min(daysSinceLastPractice / 30, 1)` for recency. Shrink observed error rates toward neutral `0.5` according to evidence (`n / (n + 5)`), with no-history rates at `0.5`; preserve a distinct exploration pool for topics with little/no evidence. Treat 70/20/10 as target quotas, round for session size, redistribute unavailable quota without dropping valid exercises, and use a stable injected seed/tie-break for reproducible tests.

**Alternatives considered**: Treating one wrong answer as full confidence violates the low-sample requirement. Strict percentages when a category has too few eligible exercises conflicts with using all available unique exercises.

## Decision: Reuse the grammar API, evaluator, database context, and Angular feature

**Rationale**: Source inspection found grammar endpoints and grading in `apps/api/Grammar`, PostgreSQL/EF Core registration and migrations in the same module, and an Angular practice route, API service, and session store. The API tests use xUnit and SQLite; the web project uses the configured Angular test runner. This is an extension to the existing feature, not a new bounded context.

**Alternatives considered**: A separate statistics service/deployable or client-side grading/aggregation would split the current source of truth without a stated need.

## Resolved questions and assumptions

- **Learner identity**: The current grammar flow has no authentication/learner ID. Use one logical learner for the MVP; allow a nullable owner field for future identity integration and do not claim multi-user isolation.
- **Inactive content**: Retain attempt facts and snapshots; select only currently active exercises for new review sessions.
- **Session completion**: Derive summaries from evaluated responses linked to the session. Unanswered entries do not become answer attempts.
- **External interface**: The existing JSON REST API is consumed by Angular; describe new and changed endpoints in OpenAPI.
- **Accessibility**: Existing Angular guidance requires signal state, lazy routes, and WCAG AA/axe checks.
- **Scale and latency**: No numeric production scale or SLO is stated; bound generated sessions to 10 and index history queries.
