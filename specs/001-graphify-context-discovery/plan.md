# Implementation Plan: Graphify Context Discovery

**Branch**: `agents/graphify-context-discovery-process` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: `specs/001-graphify-context-discovery/spec.md`

## Summary

Require the root tech-lead agent to start each new development context with request-specific Graphify discovery, then use verified findings in the existing Spec-First workflow. Update `.github/agents/tech-lead.agent.md` only; do not change application code or runtime behavior.

## Technical Context

**Language/Version**: Markdown agent configuration; no runtime language changes
**Primary Dependencies**: Graphify CLI and generated project graph, as specified by the user
**Storage**: N/A; no runtime data
**Testing**: Manual acceptance scenarios for ordering, freshness, fallback, confidence, and Spec-First handoff; diff review
**Target Platform**: Copilot SDK in VS Code
**Project Type**: Internal developer workflow guidance
**Performance Goals**: No runtime performance impact
**Constraints**: Graphify is for discovery; source remains authoritative; discoveries inform scope and regression risk
**Scale/Scope**: Root `.github/agents/tech-lead.agent.md` only; application and specialist agent behavior are unchanged

## Constitution Check

The checked-in constitution has template placeholders, not ratified principles, and defines no enforceable gates. Engineering standards require simplicity, bounded contexts, and automated validation for critical behavior. This is a documentation-only workflow change within the tech-lead boundary, with no application behavior or dependencies. **Gate: PASS** against the available engineering standards.

## Project Structure

```text
.github/agents/tech-lead.agent.md
specs/001-graphify-context-discovery/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md
```

**Structure Decision**: Update only the root tech-lead agent instructions. This internal workflow has no external or runtime interface, so no `contracts/` artifacts are needed.

## Complexity Tracking

None; no constitution violations or unnecessary project boundaries are introduced.

## Phase 0: Research

See [research.md](research.md). No unresolved technical choices remain: the user supplied Graphify command forms and relationship confidence labels, and the repository provides the root tech-lead agent and Spec Kit workflow.

## Phase 1: Design

- [data-model.md](data-model.md) describes graph and discovery concepts; this change persists no runtime data.
- [quickstart.md](quickstart.md) defines manual acceptance checks for graph-first ordering, freshness, recovery, source verification, and Spec-First handoff.
- No external contracts or application interfaces change.

## Constitution Check (Post-Design)

PASS. The design remains a focused update to existing agent guidance and adds no application coupling or runtime dependency.
