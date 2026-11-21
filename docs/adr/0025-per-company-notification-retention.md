# ADR 0025: Per-company notification retention

## Status

Accepted

## Context

Notification retention (ADR 0024) was one setting for the whole installation. Companies sharing an installation have different needs:
one wants approval messages gone after a week, another must keep unread messages indefinitely for its own process.

## Decision

- The system configuration (`Notifications:Retention`) stays as the **default**. A company may override **read days** and **unread days**
  independently; a blank value inherits the system default. Interval, batch size and the on/off switch remain installation-level.
- Overrides are stored in `platform.NotificationRetentionSettings`, one row per company that overrides anything (primary key = company).
  Inheriting everything is the **absence of a row**: saving both values blank deletes it. Database check constraints repeat the bounds
  (read 1–3650, unread 0–3650).
- **Validation uses the values that would apply**: an override is rejected if the resulting unread days (its own, or the inherited default)
  would be shorter than the resulting read days (unless unread is 0). So "read 200" is refused while the default unread is 180, and the
  message says the 180 is the system default. This keeps ADR 0024's rule that an unread message outlives a read one.
- If the system default later changes so an inherited value drifts out of order, the **purge lifts unread to read** for that company rather
  than act on the contradiction (`NotificationRetentionOptions.Effective`).
- The purge now runs per scope: **each company with an override is purged by its own values** and **all other companies by the defaults** in one
  statement that excludes the overriding companies. Same batched deletes, same worker, same schedule. The log line reports the totals.
- Permissions: `platform.notification-retention.read` and `.manage`. A company's settings are changed only by someone with the manage
  permission for the active company; each change is audited (`notification-retention.updated`, before and after).
- API: `GET`/`PUT /api/v1/notification-settings/retention` (active company; PUT with both fields null clears the override).
  `GET /api/v1/notifications/retention` now returns the **effective** policy for the active company to any signed-in user, so the bell
  panel tells each user what applies to them.
- UI: Administration → **Notifications** shows the system defaults, the company's overrides, what is in effect now, who changed it and when,
  with a "Use system defaults" action; it warns when the installation has switched the purge off.

## Consequences

- If the installation disables the purge (`Enabled=false`), company settings have no effect; the tab says so.
- Overrides can be longer than the default, so an installation cannot cap how long a company keeps messages; the bounds (3650 days) are the only limit.
- A setting takes effect at the next scheduled purge, not immediately; nothing is deleted when it is saved.
- Retention for new companies starts from the defaults. There is no copy-from-company or bulk edit.
- Not covered: per-category or per-user retention, different intervals per company, archiving before deletion.
