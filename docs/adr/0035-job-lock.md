# ADR 0035: A cross-instance lock for scheduled work

## Status

Accepted. Addresses the multi-instance limit noted in ADR 0029.

## Context

The reminders (ADR 0029 to 0031) run in a hosted worker inside the API process. With two instances, both wake at about the same time,
both see "not sent today", and both send: every recipient is told twice. Per-company "last sent" is a read-then-write, so it is not enough alone.

## Decision

- `IJobLock` (in `Erp.Application.Abstractions`) is a named, non-waiting lock shared by every instance. `TryAcquireAsync(name)` returns a handle to
  dispose, or null at once if another instance holds it.
- `PostgresJobLock` implements it with a **session-level PostgreSQL advisory lock on its own connection** (`pg_try_advisory_lock`), released by
  disposing the handle. If the process dies, its connection closes and the database frees the lock, so a crashed instance never leaves a job stuck.
- `ReminderWorker` takes `reminders:{company}` around all the jobs of one company. An instance that cannot get it skips that company this round
  (the other instance is doing it), and the "last sent" check inside the lock sees whatever the first instance just sent.
- Only the reminders use it today; the other workers are already safe across instances (email delivery claims rows with `FOR UPDATE SKIP LOCKED`
  and a lease; the retention purge is idempotent).

## Consequences

- Two instances no longer double-send a company's daily summary. Verified against PostgreSQL: a held lock refuses a second taker until released,
  different names are independent, and of eight simultaneous attempts exactly one wins.
- The lock holds one database connection per running company's reminders for the duration of that company's run (seconds), outside EF's pool.
- A skipped company is not retried until the next timer tick (up to `Reminders:IntervalMinutes`).
- Not covered: fairness or leader election (any instance may win each round), and a lock that survives a database failover.
