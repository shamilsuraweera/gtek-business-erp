# ADR 0029: Overdue invoice and bill reminders (scheduled email)

## Status

Accepted

## Context

Until now notifications (and their email, ADRs 0023 and 0026) came only from the tolerance approval workflow: events that happen inside a
request. Overdue invoices and bills are events that happen with the passing of time, and they are the ones people most need to be told about.

## Decision

- Two new notification categories, both **emailed**: `overdue-invoice` (to holders of `sales.payments.record`, the people who collect)
  and `overdue-bill` (to holders of `purchasing.payments.record`, the people who pay). Being categories, they get the per-category email
  opt-out (ADR 0028) and per-category retention (ADR 0027) with no extra work, and appear in both lists.
- Each module owns its check, as an `IReminderJob` (in `Erp.Application.Abstractions`) implemented by `OverdueInvoiceReminder` (Sales) and
  `OverdueBillReminder` (Purchasing). It asks its own store for **open documents whose due date is before today**, and sends **one summary per
  company**: the count, the outstanding total, and the oldest document with how many days late it is. It does not list every document.
- `ReminderWorker` (Host, a hosted service) runs the jobs for every **active company**, one scope per company with that company set as the
  active one, after `InitialDelaySeconds` and then every `IntervalMinutes`. A failure in one job or company is logged and does not stop the others.
- Configuration `Reminders` (validated at start-up, every problem listed): `Enabled` (true), `Hour` (8, the earliest hour a reminder is sent), `TimeZone` (UTC),
  `RepeatDays` (1), `IntervalMinutes` (60), `InitialDelaySeconds` (120).
- **Once per period, not once per run.** The summary has a fixed subject (`overdue-invoices` / `overdue-bills`); a job sends only if the last
  notification with that subject was **before today minus `RepeatDays` calendar days** (comparing dates, not hours, so a late send one day
  does not push the next). Restarts and the hourly timer therefore never duplicate it.
- **A new summary replaces the old.** Before sending, the previous one is marked read for everyone, so the bell holds one summary per kind.
  When nothing is overdue any more, the old summary is marked read too and nothing is sent.

## Consequences

- With the default `RepeatDays = 1`, people with overdue items are emailed every day until they are paid; a user can opt out of the category
  or an installation can set `RepeatDays` to 7. A new overdue invoice is reported at the next send, not the moment it falls due.
- The schedule (`Hour`, and which calendar day counts as "today" for once-per-day) follows `Reminders:TimeZone` (ADR 0036); there is no per-company time zone or send time.
- Only open documents count. Partly paid ones count with their remaining balance; credited or debited ones do not.
- The recipients are decided by permission, as everywhere else; there is no per-customer or per-vendor assignment, so everyone who can
  record payments hears about every overdue document. The summary names no customers or vendors.
- Reminders run inside the API process. With several API instances each runs the worker; the "last sent" check makes a duplicate
  unlikely, but two instances ticking at the same moment could both send once.
- Not covered: reminders before the due date, escalation to managers, SMS, per-user schedules, other events (low stock, period close).
