# ADR 0004: Multi-company architecture

**Status:** Accepted

## Context

One installation may serve multiple legally or operationally distinct
companies.

## Decision

Company scope is resolved from authenticated context and represented explicitly
by `CompanyId` on company-owned aggregates.

## Consequences

Queries and commands must enforce company scope. Cross-company access requires
an explicit authorized administrative workflow.
