# ADR 0031: Period-close reminders (scheduled email)

## Status

Accepted

## Context

An accounting period that has ended but is still open lets late postings slip into a month nobody is looking at any more, and
delays reporting. Closing is a manual step (ADR 0003 and the accounting period rules), and nothing prompted it.

## Decision

- `PeriodCloseReminder` (an `IReminderJob`, in Platform because periods belong to Platform) uses the machinery of ADR 0029 unchanged:
  one summary per company per reminder period, `Reminders` configuration (`Hour`, `RepeatDays`, ...), a newer summary
  replaces the older one, and it clears itself when nothing needs closing.
- A period **needs closing** when it is **open** and its end date plus `Reminders:PeriodCloseGraceDays` (default 3, 0 to 60) is before today.
  The grace is the time to finish the month's postings; closed and locked periods never count, nor do periods that have not ended.
- The category and subject are `period-close`; the message is **emailed**, goes to holders of `platform.accounting-periods.manage`
  (the people who can close them), and links to `/admin` (Administration, where periods are managed). It gives the count and the oldest
  period with how many days ago it ended.
- Being a category, `period-close` gets the per-category email opt-out (ADR 0028) and per-category retention (ADR 0027) without extra work.
- The demo seeder now **closes the months before September 2026** at the end of seeding (the opening balances post into January, so it
  cannot be earlier). Without it the demo would report ten open periods at once; with it, the two most recent ended months.

## Consequences

- With the default `RepeatDays = 1`, someone with the permission is emailed daily until every ended period is closed or the category is opted out.
- Reopening a closed period makes it count again at the next run once it is open and past its grace.
- The reminder does not know whether a period is ready to close (no check for unposted or unreconciled documents), only that it has ended.
- Periods are per company, so are the reminders; a company with no periods defined is never reminded.
- Not covered: reminders before a period ends, a checklist of what must be finished, locking reminders, and escalation.
