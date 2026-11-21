# ADR 0028: Per-category email opt-out

## Status

Accepted

## Context

ADR 0026 gave each user one switch for all email. Someone who approves tolerance requests may want the "needs a decision" mail
but not the mail about outcomes of their own requests, or the reverse.

## Decision

- A user can opt out of email for a single **notification category** (`approval`, `decision`, …). The master switch
  (`User.EmailNotifications`) stays: **email is sent only if the master switch is on and the category is not opted out.**
- Storage: `platform.UserEmailOptOuts (UserId, Category)`, one row per opted-out category, primary key on both.
  **Absence of a row means email is wanted**, so new categories are on by default and existing users see no change.
  Categories use the same normalisation as retention (ADR 0027): lower case, letters, digits, `-`, `_`, up to 50.
- The notifier looks up the opt-outs of the candidate recipients for the message's category in one query, after applying the master
  switch and the address check, and queues email only for the rest. The **in-app notification is unaffected**: an opt-out never hides a message.
- API: `GET /api/v1/auth/me/preferences` now returns `categories` (`category`, `emailEnabled`) listing the known categories
  (`approval`, `decision`) plus any the user has opted out of. `PUT /api/v1/auth/me/preferences/email-categories/{category}` with
  `{ "enabled": bool }` turns one on or off; it is idempotent and works while the master switch is off, so the choices survive it.
  Any signed-in user can change their own; no permission is involved.
- UI: the bell panel shows a checkbox per category (friendly names for the known ones) under the master checkbox, only while
  email is configured and the master switch is on.

## Consequences

- Preferences are per user across companies, like the master switch; there is no per-company or per-event choice within a category.
- A message already waiting in the outbox is still sent after the user opts out; the choice applies to messages created afterwards.
- The list shows only known categories and those already opted out of; a category added by a new module appears once that module's
  name is added to `NotificationCategories.Known` (it still defaults to on without that).
- Not covered: digests, quiet hours, per-sender muting.
