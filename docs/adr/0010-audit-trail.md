# ADR 0010: Append-only audit trail

## Context

Phase 1 operations need durable business and security history. Application
logs are operational diagnostics and are not a reliable audit source of truth.
The audit trail must remain useful when users or companies are later
deactivated.

## Decision

The Platform module owns an append-only `AuditEntry` table in the `platform`
schema. An entry records the actor snapshot, timestamp, optional trusted
company context, category, explicit action, target, outcome, correlation ID,
IP address, and deliberately constructed safe metadata.

Application services are the authoritative emitters. They add a success entry
to the same Platform EF unit of work before saving the business change. Login
events explicitly save through the audit store because login has no business
write transaction. Audit failures therefore fail the operation rather than
silently succeeding without required history.

Actor identity comes from `ICurrentUser`; request bodies cannot select an
actor. Company scope comes only from `ICompanyContext`. System operations may
have no actor or company. Actor names are snapshots and the audit table does
not use foreign keys to mutable master records.

The query API is system-scoped and requires `platform.audit.read`. It supports
bounded offset pagination, newest-first ordering, and indexed filters. There
is no update, delete, export, retention job, event-sourcing pipeline, or
automatic database change capture. Audit history is distinct from domain
events, application logs, and financial ledgers.

## Consequences

The audit table grows until an explicit retention policy is approved. Safe
metadata must be constructed at each audit point; passwords, hashes, tokens,
keys, connection strings, and request bodies are never captured.
