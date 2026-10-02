# Quickstart: Validate Graphify Context Discovery

These checks validate tech-lead workflow guidance; they do not run application code.

## Prerequisites

- A repository with Graphify available and a generated project graph.
- A development request naming a component or feature.

## Scenarios

1. **Current graph**: Verify freshness is checked and a request-specific query summarizes affected projects and direct/indirect relationships before detailed source analysis or spec/planning.
2. **Stale graph**: Change relevant source after graph generation. Verify an incremental update (`graphify . --update`) or a full rebuild when needed occurs before relying on graph findings.
3. **Missing/unavailable graph**: In a disposable test copy, make the graph unavailable. Verify appropriate generation/recovery is attempted; if still unavailable, the tech-lead states the limitation and does not claim graph-derived results.
4. **Inferred relationship**: Return a critical `INFERRED` edge. Verify the tech-lead reads relevant source before using it for an architectural decision.
5. **Spec-First handoff**: Verify the tech-lead checks or creates the spec after discovery, then plans/tasks, selects specialists based on affected context, and includes regression validation.

## Expected Outcome

All five scenarios perform graph discovery before spec/planning/implementation. Source inspection remains required for implementation details, and graph limitations or confidence are explicit.
