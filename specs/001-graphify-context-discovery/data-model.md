# Workflow Data Model: Graphify Context Discovery

This feature adds no persisted or runtime data. These concepts describe information the tech-lead uses while handling a request.

## Project Graph

- **Purpose**: Navigable map of project components and relationships.
- **Attributes**: Availability, generation freshness, nodes, edges, and edge confidence (`EXTRACTED`, `INFERRED`, or ambiguous where provided).
- **Validation**: Do not rely on a stale graph or present missing graph evidence as a successful query.

## Context Discovery

- **Purpose**: Request-specific findings used to identify scope and impacts.
- **Attributes**: Affected projects/components, direct and indirect dependencies, APIs, services, data stores, events, contracts, and regression concerns.
- **Relationship**: Derived from a Development Request and graph queries; critical inferred relationships are checked against source.

## Development Request

- **Purpose**: User request that scopes Graphify queries and downstream work.
- **Relationship**: Informs Context Discovery, then the existing spec, plan, tasks, implementation, and validation workflow.
