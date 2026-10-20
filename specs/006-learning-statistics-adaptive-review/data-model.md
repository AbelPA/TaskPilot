# Data Model: Learning Statistics and Adaptive Review

Append-only evaluated answer history is the source of truth. Topic statistics and session summaries are projections, not separately mutable totals.

## Grammar submission

One logical grading request and its idempotency boundary.

| Field | Type | Rules |
|---|---|---|
| `id` | UUID | Client-generated stable submission ID; unique and reused only for retries |
| `learnerId` | UUID/string, nullable | Null in the single-learner MVP; scope data when identity exists |
| `exerciseId` | UUID | Original catalog identifier |
| `exerciseTitleSnapshot` | string | Historical display context |
| `topicId` | UUID | Explicit context validated against the exercise-topic relation |
| `topicTitleSnapshot` | string | Historical display context |
| `reviewSessionId` | UUID, nullable | Set only for a review submission |
| `attemptNumber` | integer | Monotonic per learner/exercise/topic; assigned transactionally |
| `evaluatedAt` | UTC timestamp | Set by server when evaluation is persisted |
| `score`, `maxScore` | decimal | Snapshot of the existing evaluator result |

### Relationships and invariants

- One submission groups the answer items evaluated together.
- The same submission ID and same request returns the original result without additional rows. Reuse of an ID with different input is a conflict. An intentional new attempt uses a new ID.
- Submission and answer rows commit atomically with the result; persistence failure is an API failure, not success.
- Deactivation does not delete history. Preserve IDs and title snapshots if a catalog record is removed; do not cascade-delete attempts.

## Evaluated answer attempt

One row for each answer item actually evaluated by the existing grading flow.

| Field | Type | Rules |
|---|---|---|
| `submissionId` | UUID | Parent submission |
| `itemType` | enum/string | `blank` or `question` |
| `itemKey` | string | Unique with `submissionId` and `itemType` |
| `submittedAnswer` | string | Evaluated response snapshot |
| `correctAnswer` | string | Evaluator result snapshot for answer review |
| `isCorrect` | boolean | Server evaluator output only |
| `explanation` | string | Evaluator explanation snapshot |
| `points`, `maxPoints` | decimal | Evaluator result snapshot |

Topic statistics count these answer rows: `totalAttempts = correctAnswers + incorrectAnswers`; percentages use `totalAttempts` as denominator. Zero rows means “not yet assessed” and null percentages, not zero. Recent performance uses the five most recent evaluated answers for the topic, ordered by server timestamp and stable ID.

## Review session

| Field | Type | Rules |
|---|---|---|
| `id` | UUID | Server-generated session ID |
| `learnerId` | UUID/string, nullable | Same current single-learner rule as submissions |
| `createdAt` | UTC timestamp | Server-generated |
| `selectionSeed` | integer/string | Persisted seed/tie-break for the selection |
| `requestedExerciseCount` | integer | Default 10; never exceeds configured session size |
| `availableExerciseCount` | integer | Actual selected count, 0–10 |

## Review session exercise

| Field | Type | Rules |
|---|---|---|
| `sessionId` | UUID | Parent session |
| `exerciseId` | UUID | Active exercise selected at generation |
| `topicId` | UUID | Explicit topic attribution for the selection |
| `selectionCategory` | enum/string | `priority`, `maintenance`, or `exploration` |
| `priorityScore` | decimal, nullable | Normalized composite for priority selections |
| `reason` | string | Evidence-backed explanation; no unsupported diagnosis |
| `displayOrder` | integer | Stable session order |
| `titleSnapshot`, `topicTitleSnapshot` | string | Preserve session context after catalog changes |

Unique `(sessionId, exerciseId)` prevents within-session repetition. Unanswered entries remain planned session exercises but are not answer attempts or summary counts.

## Derived views

### Topic statistics

Per learner/topic: total/correct/incorrect answer attempts, accuracy/error percentage, most recent evaluated timestamp, recent five-answer performance, assessment state, and insufficient-evidence flags. Compute from answer history; clamp percentages to `[0,100]` and avoid division when total is zero.

### Review priority

`0.50 * historicalError + 0.30 * recentError + 0.20 * recency`, where error components are evidence-shrunk toward `0.5` using `n / (n + 5)` and `recency = min(daysSinceLastPractice / 30, 1)`. No history is neutral evidence and remains eligible for exploration, not proof of difficulty. Weights, the recent-answer window, time limit, and default session size follow the feature assumptions and should be constants/configuration, not per-request controls.

### Session result

Derived from evaluated answers whose parent submission references the session. Include exercises answered, correct/incorrect answer counts, accuracy, per-topic performance, attention topics, and a prior-history comparison only when sufficient prior evidence exists. Unanswered exercises are excluded.
