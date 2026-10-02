# Tasks: Graphify Context Discovery

**Input**: Design documents from `specs/001-graphify-context-discovery/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `quickstart.md`

**Tests**: No automated tests are requested for this documentation-only agent workflow. Validate with the manual scenarios in `quickstart.md` and a checklist review of the final agent instructions.

## Phase 1: Setup

No project initialization or runtime setup is required for this internal documentation change.

## Phase 2: Foundational

No shared application infrastructure is required. User-story tasks edit the existing root tech-lead agent instructions.

## Phase 3: User Story 1 - Discover technical context before planning (Priority: P1) - MVP

**Goal**: Make request-specific Graphify discovery the first step in each new development context.

**Independent Test**: With a current graph, verify a request-specific query runs before deep source analysis, spec work, planning, or implementation and identifies relevant affected components and relationships.

- [x] T001 [US1] Add the mandatory graph-first startup sequence and request-specific query guidance in `.github/agents/tech-lead.agent.md`.

## Phase 4: User Story 2 - Handle graph freshness and evidence correctly (Priority: P1)

**Goal**: Prevent reliance on stale, missing, or uncertain graph evidence.

**Independent Test**: Verify stale graphs trigger an update/rebuild, unavailable graphs are explicitly disclosed if recovery fails, and critical `INFERRED` relationships are checked against source.

- [x] T002 [US2] Add graph freshness checks, missing/unusable graph recovery and disclosure, edge-confidence handling, and source-verification rules in `.github/agents/tech-lead.agent.md`.

## Phase 5: User Story 3 - Carry graph findings into Spec-First workflow (Priority: P2)

**Goal**: Use discovery results to define scope, relevant specialists, implementation work, and regression validation.

**Independent Test**: For a request crossing component boundaries, verify findings inform the affected-project map, spec/plan/tasks, specialist selection, and regression risks after graph discovery.

- [x] T003 [US3] Connect Graphify findings to component/dependency scope, APIs, services, data stores, events, contracts, indirect impacts, Spec-First artifacts, specialist selection, and regression risks in `.github/agents/tech-lead.agent.md`.

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T004 Review `.github/agents/tech-lead.agent.md` against every scenario in `specs/001-graphify-context-discovery/quickstart.md` and preserve source inspection as the authority for implementation details.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No tasks; no project/runtime setup is required.
- **Foundational (Phase 2)**: No tasks; there is no shared runtime prerequisite.
- **User Stories (Phases 3-5)**: Execute in order because all three update the same `.github/agents/tech-lead.agent.md` file and later stories refine the Graphify guidance established earlier.
- **Polish (Phase 6)**: Depends on T001-T003.

### User Story Dependencies

- **User Story 1 (P1)**: Independent MVP; establishes graph-first discovery.
- **User Story 2 (P1)**: Follows User Story 1 to add graph reliability rules to the same guidance file.
- **User Story 3 (P2)**: Follows User Stories 1-2 to wire verified findings into Spec-First work and impact assessment.

### Parallel Opportunities

No implementation tasks are marked parallelizable: all edit the same agent instruction file, so parallel editing would conflict. Independent validation scenarios can be checked separately during T004.

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete T001 and validate User Story 1 independently using `quickstart.md` scenario 1.
2. Continue with T002 and T003 to deliver reliable evidence handling and complete Spec-First integration.
3. Complete T004 to verify all scenarios and consistency with the existing Spec-First instructions.

### Incremental Delivery

1. Add Graphify-first discovery (US1).
2. Add freshness, fallback, and confidence controls (US2).
3. Carry findings into scope, planning, delegation, and regression validation (US3).
4. Run the full quickstart review.
