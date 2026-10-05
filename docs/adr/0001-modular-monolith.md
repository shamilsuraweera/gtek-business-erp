# ADR 0001: Modular monolith

**Status:** Accepted

## Context

The ERP needs strong module boundaries without premature distributed-system
complexity.

## Decision

Use a modular monolith with explicit domain, application, contracts, and
infrastructure projects per module.

## Consequences

Deployment and transactions remain simple while ownership boundaries are
enforced in code. A future service split requires a deliberate ADR.
