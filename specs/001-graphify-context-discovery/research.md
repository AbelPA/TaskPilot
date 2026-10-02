# Research: Graphify Context Discovery

## Decisions

### Use Graphify as the first dependency-discovery step

- **Decision**: Check graph availability and freshness and run a request-specific Graphify query before deep source inspection, spec work, or implementation.
- **Rationale**: This is the requested order and surfaces direct and indirect dependencies early.
- **Alternatives considered**: Broad source searches first were rejected because they reverse the required order and may miss graph relationships.

### Treat graph findings as navigation evidence, not implementation truth

- **Decision**: Refresh stale graphs, disclose unavailable graphs, distinguish `EXTRACTED` from `INFERRED`, and verify critical inferred claims against source.
- **Rationale**: Graph freshness and provenance affect confidence; implementation decisions still require source inspection.
- **Alternatives considered**: Treating every edge as authoritative was rejected because inferred or stale edges can mislead.

### Keep implementation within the tech-lead agent boundary

- **Decision**: Change the root tech-lead instructions only; retain the existing Spec Kit workflow and specialist routing model.
- **Rationale**: The requested behavior is orchestration guidance, not application functionality.
- **Alternatives considered**: Application services, mandatory repository hooks, or specialist-specific instructions were rejected as unrelated scope.

## Unresolved Questions

None. The request provides the Graphify command forms and required discovery fields.
