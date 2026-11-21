# ADR 0024: Notification retention

## Status

Accepted

## Context

Notifications (ADR 0023) were never deleted. Every request and decision adds rows for several users, so the table grows without
bound, mostly with messages nobody will read again. The panel only shows the newest 20, so old rows cost storage and index size
and give nothing back.

## Decision

- A **system-wide retention policy**, configured under `Notifications:Retention` (appsettings or environment variables), not per company or user:

  | Setting | Default | Meaning |
  |---|---|---|
  | `ReadDays` | 30 | Days a message is kept **after it was read**. |
  | `UnreadDays` | 180 | Days an unread message is kept **after it was sent**; `0` keeps unread messages forever. |
  | `IntervalMinutes` | 1440 | Minutes between purges. |
  | `InitialDelaySeconds` | 60 | Wait after start-up before the first purge. |
  | `BatchSize` | 1000 | Rows deleted per statement. |
  | `Enabled` | true | `false` turns the purge off; messages then accumulate. |

- **Read messages are timed from when they were read**, not when they were sent, so a message someone opens late still stays visible for the
  full period. Messages that were resolved by a decision (ADR 0023) count as read at that moment, so they leave after `ReadDays` too.
- **Unread messages outlive read ones:** `UnreadDays` may not be shorter than `ReadDays` (0 excepted). Something nobody has seen is not
  discarded sooner than something they have.
- The configuration is **validated at start-up**: every problem is reported together and the API refuses to start, rather than purging
  with a value the operator did not intend. Bounds: `ReadDays` 1–3650, `UnreadDays` 0–3650, `IntervalMinutes` 1–10080, `BatchSize` 100–10000.
- A hosted service in the API (`NotificationRetentionWorker`) runs the purge once after the initial delay and then every interval. It calls
  `INotificationRetention.PurgeAsync`, which computes the cutoffs from the clock and asks the store to delete. Deletes run **in batches**
  (`DELETE … WHERE Id IN (SELECT Id … LIMIT n)`, repeated until a batch comes back short), so a large backlog never holds one long lock.
- A failed run is logged as a warning and retried at the next interval; it never stops the API. A run that removed anything logs the counts.
- The purge is idempotent and takes no locks beyond the deletes, so several API instances running it at once is harmless.
- `GET /api/v1/notifications/retention` returns the policy and the bell panel states it ("Messages are removed 30 days after you read
  them, or after 180 days if you never do."), so users know what to expect.

## Consequences

- Deletion is permanent. The audit trail (not notifications) is the record of who requested and decided what; it is unaffected.
- The policy is one setting for the whole installation. Different retention per company or per category is not supported.
- Nothing is archived or exported before deletion.
- With `UnreadDays` at 0, a user who never opens the bell keeps every unread message indefinitely.
- The functional-test host turns the worker off, because the purge needs a real database; it is covered by integration tests against PostgreSQL.
