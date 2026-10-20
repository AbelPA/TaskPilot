# Feature Specification: Learning Statistics and Adaptive Review

**Feature Branch**: `agents/create-spec-for-attachment-1`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "Implement performance statistics by grammar topic, adaptive review, and session summaries based on actual response history."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review performance by topic (Priority: P1)

As a learner, I want to review my correct and incorrect answers and progress by grammar topic so I can identify which content I need to practice.

**Why this priority**: Consistent response tracking and performance visibility are the foundation for reviews that adapt to the learner's actual needs.

**Independent Test**: Answer exercises from different topics and verify that the dashboard displays the corresponding counts, percentages, most recent attempt, and recent performance.

**Acceptance Scenarios**:

1. **Given** the learner has answered exercises for a topic, **When** they review their statistics, **Then** the total attempts, correct and incorrect answers, corresponding percentages, date of the most recent attempt, and recent performance are displayed.
2. **Given** a topic has no recorded answers, **When** the learner reviews the statistics, **Then** the topic is identified as not yet assessed and no invalid percentage is displayed.
3. **Given** a response has been evaluated as correct or incorrect, **When** it is recorded, **Then** the statistics reflect the evaluated result, not merely the answer the learner selected.
4. **Given** a response has already been recorded, **When** the same logical submission is retried after a communication failure, **Then** it is not counted twice; a deliberately submitted new attempt remains a new record.

### User Story 2 - Complete a personalized review (Priority: P1)

As a learner, I want to receive an exercise session that focuses more on topics I find difficult while continuing to practice familiar content and explore new topics.

**Why this priority**: Adaptive review turns response history into a useful next study activity and is the main new benefit of this feature.

**Independent Test**: Prepare controlled history for topics with high, low, recent, and no performance data; generate a review and verify its priorities, variety, size, and explanations.

**Acceptance Scenarios**:

1. **Given** there is sufficient history and different performance levels, **When** a review is started, **Then** topics with demonstrated difficulty receive higher priority, based on historical errors, recent errors, and time since last practice.
2. **Given** a topic has only a few attempts, **When** its priority is calculated, **Then** one incorrect answer is not treated as conclusive evidence, and the topic remains eligible for exploration.
3. **Given** the learner has no history, **When** a review is started, **Then** the session offers available exercises for exploration without claiming that any difficulty has been observed.
4. **Given** at least 10 eligible exercises are available, **When** a review is generated, **Then** the session contains 10 exercises without repeating an exercise.
5. **Given** fewer than 10 eligible exercises are available, **When** a review is generated, **Then** the session contains the available unique exercises and informs the learner that it is shorter than the standard length.
6. **Given** a topic has been prioritized, **When** the learner views the reason, **Then** the explanation uses only counts and results that have actually been recorded.

### User Story 3 - Complete and understand a session (Priority: P2)

As a learner, I want to see a summary when I finish a review so I can understand my results and decide what to study next.

**Why this priority**: The summary completes the study cycle and helps learners interpret their progress without turning a small sample into a definitive diagnosis.

**Independent Test**: Complete a session with correct and incorrect answers across different topics and compare the summary with the responses that were actually evaluated.

**Acceptance Scenarios**:

1. **Given** the learner has completed exercises in a review, **When** the session ends, **Then** the summary displays exercises answered, correct and incorrect answers, accuracy percentage, and results by topic.
2. **Given** a topic still has incorrect answers, **When** the summary is displayed, **Then** it is identified for future attention without being classified as mastered based on a short session.
3. **Given** comparable prior history exists, **When** the summary is displayed, **Then** progress is compared with that history; without sufficient data, the comparison is not presented as conclusive.
4. **Given** the learner interrupts the session, **When** not all exercises have been answered, **Then** only responses that were actually evaluated count toward history, and unanswered exercises are not included as attempts in the summary.

### Edge Cases

- The learner has no history, or a topic has only one or two attempts.
- A topic has no active exercises, or the available exercise set changes between sessions.
- An exercise has been answered many times; prior attempts remain in history and are considered during selection without causing excessive repetition.
- A response cannot be persisted: the learner receives a clear failure notice and the result is not presented as saved.
- A submission is retried; the same logical submission is not duplicated, but a deliberate new attempt is preserved.
- The session is interrupted, has fewer than 10 eligible exercises, or cannot fill one of the priority, maintenance, or exploration portions.
- A topic has a strong overall history but recent performance has worsened, or has a high error rate based on few attempts.
- An exercise is removed or deactivated after prior responses have been recorded; its history remains valid, but the unavailable exercise is not selected.
- The session cannot be filled without repeating exercises or exceeding the actual availability for a topic.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST record each response that is actually evaluated, including the exercise, topic, correct/incorrect result, and date and time; it MUST associate the learner's identifier when the application provides one and record the attempt number when that concept applies.
- **FR-002**: The system MUST preserve prior attempts. Retrying the same logical submission MUST NOT create a duplicate record, while a deliberately submitted new attempt MUST be recorded as a new attempt.
- **FR-003**: The system MUST calculate, for each topic, total attempts, correct and incorrect answers, accuracy and error percentages, the date of the most recent attempt, and recent performance based on recorded results.
- **FR-004**: The system MUST keep statistics consistent with the attempt history and MUST identify topics without attempts as not yet assessed, without dividing by zero.
- **FR-005**: The system MUST allow the learner to review statistics by topic in a clear presentation that highlights difficulties, strong performance, topics without sufficient evidence, and recent progress.
- **FR-006**: The system MUST make Review available through the existing navigation and generate sessions dynamically from eligible exercises and available history.
- **FR-007**: Initial priority MUST combine historical error rate (50%), recent error rate (30%), and time since last practice (20%), with each component normalized to a value between 0 and 1. The time factor MUST increase gradually until it reaches the configured limit.
- **FR-008**: When there is insufficient history to calculate a priority component, the system MUST avoid assigning excessive confidence to the result and MUST preserve opportunities to explore topics with no history or few attempts.
- **FR-009**: The system MUST use an initial session distribution of 70% of exercises from priority topics, 20% from topics selected for maintenance based on strong performance, and 10% for exploring topics with little or no history. These proportions are adjustable targets and MUST adapt to the number of topics and eligible exercises without discarding valid exercises.
- **FR-010**: A session MUST contain up to 10 exercises, using 10 when that many unique eligible exercises are available; when fewer are available, it MUST use all available unique exercises and inform the learner that the session is shorter.
- **FR-011**: Exercise selection MUST consider exercise-specific performance, how often the exercise has been presented, time since it was last answered, and the availability of alternatives. When exercise-specific history is unavailable, the system MUST use topic-level performance as a reference and MUST NOT invent individual results.
- **FR-012**: The system MUST avoid repeating exercises within a session and avoid concentrating the entire session in one topic when sufficient alternatives exist. Selection MUST be reproducible under controlled test conditions.
- **FR-013**: The system MUST provide an understandable explanation for a topic's priority, based exclusively on recorded data, such as the number of incorrect answers among recent attempts.
- **FR-014**: Responses given during a review MUST use the regular exercise evaluation flow and update the same history used by statistics and future reviews.
- **FR-015**: When a review is completed, the system MUST show exercises answered, correct and incorrect answers, accuracy percentage, performance by topic, topics that still need attention, and a comparison with prior history when sufficient data is available.
- **FR-016**: An interrupted session MUST count only responses that have already been evaluated; blank or unsubmitted responses MUST NOT be recorded as attempts.
- **FR-017**: When the application provides user identification, the system MUST restrict statistics, history, selection, and summaries to the corresponding learner's data. Without user identity in the MVP, the feature MUST use the existing single-learner context and MUST NOT claim to provide multi-user isolation.
- **FR-018**: Failures to record a response MUST be clearly communicated and MUST NOT be presented as a successful save or as persisted statistics.
- **FR-019**: If valid prior responses are available, they may contribute to statistics as long as they represent actual evaluations; the system MUST NOT invent results or remove valid history.
- **FR-020**: Adding statistics and Review MUST preserve the existing exercise browsing and answering flow.

### Key Entities *(include if feature involves data)*

- **Evaluated attempt**: The result of an actual exercise submission, associated with its topic and, when available, the learner; includes correctness, date and time, and attempt order when applicable.
- **Topic statistics**: An aggregate view of a learner's attempts for a topic, including totals, percentages, most recent date, and recent performance.
- **Review session**: A dynamically generated, limited set of exercises offered to the learner, with a selection category and priority explanation where applicable.
- **Session result**: A summary of evaluated responses during a session, aggregated by session and topic, excluding unanswered exercises.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of successfully submitted and evaluated responses appear exactly once in persisted attempts, including after a retry of the same logical submission.
- **SC-002**: For every topic with attempts, displayed totals match evaluated records and accuracy and error percentages sum to 100%, with a maximum difference of 1 percentage point due to rounding.
- **SC-003**: In 100% of reviews generated when at least 10 exercises are eligible, the learner receives 10 unique exercises; when fewer than 10 are eligible, the learner receives all available unique exercises and is notified that the session is shorter.
- **SC-004**: In controlled scenarios with sufficient history, topics with greater calculated difficulty are prioritized over topics with lower difficulty, while topics with no history remain eligible for exploration.
- **SC-005**: 100% of summaries for completed sessions show counts that match the responses actually evaluated in that session and exclude unsubmitted responses.
- **SC-006**: The existing exercise browsing and answering flow remains completable without using Review.
- **SC-007**: In usability tests with five learners, at least four can identify which topics they should practice and explain the reason shown in the statistics or review.

## Assumptions

- The current application provides exercises associated with grammar topics and evaluates responses in its existing practice flow.
- The application currently has no authentication or learner identity. The first version uses the existing single-learner context; isolation between learners applies only when a real identity is available.
- Recent performance is calculated from the five most recent evaluated attempts for each topic. The window may be adjusted based on actual usage data.
- The time factor measures the period since the topic's most recent evaluated practice and reaches its limit after 30 days; the weights, recent-attempt window, time limits, and default session size are adjustable parameters.
- Review uses only exercises that are active and available when the session is generated.
- An interrupted session does not need to classify unanswered exercises; evaluated responses already recorded remain in history.
- The MVP may start without prior history if no earlier evaluated responses have been persisted.
