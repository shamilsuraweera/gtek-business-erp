# ADR 0002: Module database ownership

**Status:** Accepted

## Context

Cross-module table access creates hidden coupling and weakens invariants.

## Decision

Each module owns its persistence schema and exposes application contracts.
Other modules must not update its tables directly.

## Consequences

Integration requires explicit contracts or projections, but migrations and
invariants remain attributable to one module.
