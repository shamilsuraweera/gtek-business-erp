# ADR 0011: Company-scoped number sequences

## Status

Accepted

## Decision

Platform owns reusable number sequences. Each sequence belongs to one company and
is uniquely identified by `(CompanyId, Code)`. Codes are normalized to uppercase.
Formatting is deterministic: prefix, zero-padded numeric value, and optional
suffix. Values are monotonic and may contain gaps; a reserved value is never
reused, including after a transaction failure.

Issuance locks the matching PostgreSQL row with `SELECT ... FOR UPDATE` inside a
database transaction, increments the aggregate, and commits the result. This is
safe across concurrent requests and API instances without process-local locks,
`MAX()+1`, or another service.

Administration is permission-protected and company-scoped. Configuration changes
and lifecycle changes use the existing append-only audit trail. Business modules
will store issued document numbers separately when they consume this service.

## Consequences

Sequences remain independent between companies, and database uniqueness protects
duplicate configuration. Gaps are an explicit trade-off for concurrency safety
and no-reuse semantics. The bootstrap SQL and EF model must remain synchronized.
