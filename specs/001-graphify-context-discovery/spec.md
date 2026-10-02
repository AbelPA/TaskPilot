# Feature Specification: Graphify Context Discovery

**Feature Branch**: `agents/graphify-context-discovery-process`

**Created**: 2026-10-02

**Status**: Draft

**Input**: User description: Require the tech-lead to consult the project Graphify graph at the start of every new development context, use targeted queries to map affected components and dependencies before spec/planning/implementation, check graph freshness, verify inferred critical relationships against source, and use discoveries to scope work, select specialists, and assess regression risks.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover technical context before planning (Priority: P1)

As a user asking for a repository change, I need the tech-lead to consult the project graph before deep code analysis so that the plan starts from the affected projects, modules, dependencies, APIs, services, data stores, events, and contracts.

**Why this priority**: Early dependency discovery prevents incomplete scope and missed indirect impacts.

**Independent Test**: Give the tech-lead a change request in a repository with a current graph and verify that it makes a query specific to the request and records relevant context before spec, planning, or implementation.

**Acceptance Scenarios**:

1. **Given** a new development request and an available graph, **When** the tech-lead begins work, **Then** it makes a request-specific Graphify query before detailed source analysis or implementation.
2. **Given** graph results with direct and indirect dependencies, **When** the tech-lead defines scope, **Then** it uses those results to identify affected projects, components, related APIs/services/contracts, and regression risks.

### User Story 2 - Handle graph freshness and evidence correctly (Priority: P1)

As a user relying on the technical assessment, I need the tech-lead to distinguish current graph evidence from stale or inferred relationships so that critical decisions are confirmed against source code.

**Why this priority**: Stale or inferred edges can lead to incorrect architecture and implementation decisions.

**Independent Test**: Exercise stale-graph, missing-graph, and inferred-edge cases and verify that the tech-lead refreshes or reports the limitation and checks critical claims in source.

**Acceptance Scenarios**:

1. **Given** relevant code changes since graph generation, **When** the tech-lead starts discovery, **Then** it updates the graph incrementally or rebuilds it when needed before relying on it.
2. **Given** a missing or unusable graph, **When** discovery begins, **Then** the tech-lead attempts appropriate graph generation or clearly reports the blocker and does not claim graph-derived findings.
3. **Given** an inferred relationship supporting an architectural decision, **When** the tech-lead evaluates that decision, **Then** it verifies the relationship in the original source before relying on it.

### User Story 3 - Carry graph findings into Spec-First workflow (Priority: P2)

As a user, I need graph findings to inform the relevant spec, plan, task breakdown, specialist selection, and regression validation so that the implementation follows the actual dependency context.

**Why this priority**: Discovery is only useful when it changes the work definition and validation appropriately.

**Independent Test**: Provide a request crossing component boundaries and verify that the resulting spec or plan names affected context and that tasks or specialist involvement cover impacted areas.

**Acceptance Scenarios**:

1. **Given** graph discovery is complete, **When** the tech-lead checks for an existing spec, **Then** it validates or updates that spec with the affected context, or creates one if needed.
2. **Given** multiple affected technologies or subsystems, **When** the tech-lead assigns work, **Then** it selects only relevant specialists based on discovered dependency paths.

### Edge Cases

- The graph is absent, corrupt, or Graphify is unavailable; limitations must be explicit and source inspection must not be presented as graph evidence.
- The graph has not changed, so no refresh is required; discovery still uses a query tailored to the request.
- The query returns no relevant nodes or only inferred/ambiguous relationships; the tech-lead uses targeted source searches and marks the graph limitation.
- The request is documentation-only or otherwise has no meaningful code dependencies; the tech-lead still checks the graph first and explains when no relevant code relationships apply.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: At the beginning of each new development context, the tech-lead MUST check the project graph before deep source analysis or implementation.
- **FR-002**: The graph query MUST be directed at the current request and MUST identify relevant affected projects and direct or indirect relationships where available.
- **FR-003**: The tech-lead MUST use relevant graph findings to establish scope, dependencies, specialist involvement, spec/plan content, and regression risks.
- **FR-004**: The tech-lead MUST verify graph freshness and update incrementally or rebuild when relevant source changes make the graph stale.
- **FR-005**: When the graph is missing, unusable, or unavailable, the tech-lead MUST attempt recovery when feasible and MUST disclose remaining limitations rather than imply graph discovery succeeded.
- **FR-006**: The tech-lead MUST distinguish extracted relationships from inferred relationships and MUST verify critical architectural decisions supported by inferred relationships against source code.
- **FR-007**: Graphify MUST support discovery and navigation, not replace reading relevant source files when implementation details affect a decision.
- **FR-008**: Graph discovery MUST precede checking, creating, or updating the relevant spec, followed by planning, task definition, implementation, and validation in the repository's Spec-First workflow.

### Key Entities *(include if data involved)*

- **Project Graph**: A generated representation of project components and relationships, including freshness and relationship confidence.
- **Context Discovery**: The request-specific findings about affected components and their direct or indirect dependencies.
- **Development Request**: The user's requested change that scopes graph queries and subsequent Spec-First work.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of reviewed new development contexts, a request-specific graph check occurs before deep source analysis or implementation, or an explicit graph unavailability limitation is recorded first.
- **SC-002**: In 100% of reviewed cases with a stale graph, the tech-lead refreshes or rebuilds it before relying on its relationships.
- **SC-003**: In 100% of reviewed architectural decisions relying on inferred graph relationships, the supporting source is checked.
- **SC-004**: Every completed technical plan identifies relevant affected context and regression concerns discovered from the graph, or explicitly records that the graph yielded no relevant relationships.

## Assumptions

- The repository's Graphify CLI and project graph are available in the expected development environment; if not, the tech-lead reports the limitation and uses source inspection without representing it as Graphify output.
- Graph findings are preliminary navigation evidence; source files remain authoritative for implementation-specific decisions.
- The existing Spec Kit workflow remains the process for creating/updating specs, plans, and tasks.
- The change is limited to repository guidance for the tech-lead and does not alter application runtime behavior.

