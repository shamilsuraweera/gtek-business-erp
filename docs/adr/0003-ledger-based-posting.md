# ADR 0003: Ledger-based posting

**Status:** Accepted

## Context

Financial and inventory history must be auditable and reproducible.

## Decision

Postings create immutable ledger history. Corrections use reversal or
adjustment transactions rather than editing posted records.

## Consequences

Balances are derived from controlled transactions and audit history is
preserved, at the cost of more explicit correction workflows.
