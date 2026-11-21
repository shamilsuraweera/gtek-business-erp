# ADR 0026: Email delivery of approval requests

## Status

Accepted

## Context

In-app notifications (ADR 0023) only reach people who are signed in and looking at the app. An approver who is away from their desk
does not learn that a tolerance request (ADR 0022) is waiting, and the requester does not learn the outcome.

## Decision

- Messages flagged `Email` on `NotificationMessage` are **also sent by email**. Purchasing flags only what needs action or reports a
  decision: a new tolerance request (to the approvers) and an approval or rejection (to the requester). Withdrawals, resolutions and
  everything else stay in-app only. Nobody is emailed about their own action.
- **Outbox, not inline sending.** The notifier writes an `EmailMessage` row to `platform.EmailMessages` in the **same save** as the in-app
  notification, so both exist or neither does, and a slow or unavailable mail server never delays or fails the approval itself.
- **A worker delivers the outbox** (`EmailDeliveryWorker`, registered only when email is on) every `PollSeconds`. It claims due rows with a
  single `UPDATE … WHERE Id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT n)` that stamps a **lease**, so several API instances never send the same
  message; a lease that lapses (a crashed worker) can be taken again. Each message is saved right after its attempt, so a crash never causes a
  resend of one that went out.
- **Retries with back-off:** a failed attempt is retried after `RetryMinutes` (default 1, 5, 30, 120, 720 minutes) until `MaxAttempts` (5), then the
  message is `Failed`. A **permanent** refusal (the server rejects the recipient or sender, 5xx) is not retried at all. Temporary refusals (4xx),
  connection problems and timeouts are retried. The in-app notification is unaffected either way.
- **Sending** uses MailKit over SMTP with `None`, `StartTls` or `SslOnConnect`, and optional authentication. Each message opens a short connection.
- **Content:** a subject `[GTEK ERP] …`, a plain-text part and an HTML part. In-app paths become absolute links from `Email:BaseUrl`; anything that is
  not a plain in-app path (`https://…`, `//host`, `javascript:`) is dropped rather than put in an email. Message text is HTML-encoded, and line breaks are
  stripped from the subject so a notification can never inject headers.
- **Opt-in by installation, opt-out by user.** Email is off unless `Email:Enabled` is true. When it is, each user has **Email me approval requests and
  decisions** (on by default) in the notification bell, stored on the user, and users without an address are skipped.
- **Validated at start-up.** With email enabled, a missing host, an invalid from address or base URL, a username without a password, or an
  out-of-range number stops the API with every problem listed. With email off, no email settings are needed.
- **Housekeeping:** the worker also deletes sent messages after `KeepSentDays` (14) and failed ones after `KeepFailedDays` (90); messages still
  waiting are never discarded by age.
- **Visibility:** Administration → Notifications shows, for the active company, what is waiting, sent in the last 24 hours and failed, the last error
  and when the oldest waiting message was queued. It needs the notification-retention read permission.
- Configuration note: `RetryMinutes` has no built-in default list, because the .NET configuration binder appends configured items to a list that
  already has some; the defaults are applied when nothing is configured.

## Consequences

- Email is best effort and at-least-once from the outbox's point of view: a failure between the server accepting a message and the row being saved
  could, rarely, send it twice. Receipt is not tracked (no bounces or read receipts).
- The SMTP password lives in configuration; keep it in an environment variable or secret store, not source control. It is never returned by the API.
- There is no per-company sender, no templates to edit, no digest, and no localization.
- Not covered: email for other kinds of notification, inbound mail (replying to approve), and per-user choice of which events to email.
