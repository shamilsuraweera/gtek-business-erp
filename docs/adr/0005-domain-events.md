# ADR 0005: Domain events

**Status:** Accepted

## Context

Modules need a future-safe way to communicate business facts without direct
database coupling.

## Decision

Aggregates may raise lightweight in-process domain events. Dispatching and
external messaging are deferred until a concrete use case is approved.

## Consequences

Domain code remains infrastructure-free, while event handling must be made
transactional before events drive financial side effects.
