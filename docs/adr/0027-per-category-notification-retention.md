# ADR 0027: Per-category notification retention

## Status

Accepted

## Context

ADR 0025 let a company choose how long it keeps notifications, but one rule applied to every message. Categories differ in value:
an approval request is noise a week after it is decided, while a decision (the record that a tolerance was approved) may be wanted
for months.

## Decision

- A **category** (`approval`, `decision`, … the `Category` a module gives its `NotificationMessage`) can have its own **read days** and
  **unread days**. Precedence, applied to each value separately: **category override → company override → system default**.
  A blank value inherits one level up. `0` unread days still means "keep forever" and is a real setting, not "unset".
- Storage: `platform.NotificationRetentionSettings` gains a `Category` column and the primary key becomes (`CompanyId`, `Category`).
  The **company-wide row has an empty category**, so every ADR 0025 row migrates unchanged. A row that overrides nothing is deleted,
  as before. Categories are lower-cased (notifications too, on creation) and limited to letters, digits, `-` and `_` (50 characters).
  The `Down` migration deletes category rows, since the old key allows one row per company.
- **Validation uses the values that would apply**: a category override is judged against the company's *effective* values, so "read 200"
  is refused when the company keeps unread for 120 days, and the message says the 120 is inherited from the company setting. This keeps the
  rule that an unread message outlives a read one. If the company value later changes so an inherited value drifts out of order, the purge
  lifts unread to read (`NotificationRetentionOptions.EffectiveFor`), as in ADR 0025.
- **The purge works most-specific-first.** For each company with any setting: one delete per category that has a row (by its effective
  values), then one for the rest of that company (by the company values, excluding those categories); then one for all companies without
  settings. Every statement is the same batched `ExecuteDelete`, and the scopes are disjoint, so nothing is deleted by the wrong rule.
  A company with only category rows keeps the system defaults for its other categories.
- API: `PUT /api/v1/notification-settings/retention/categories/{category}` (same body as the company-wide `PUT`; both null clears it),
  needing `platform.notification-retention.manage`. `GET /api/v1/notification-settings/retention` now also returns `categories`: the
  known categories (`approval`, `decision`), those the company has messages in, and those with a rule, each with its override and the
  effective values. Changes are audited (`notification-retention.category-updated`, with category, before and after).
- UI: Administration → Notifications has a **By category** card under the company form: a row per category with its two override fields,
  what is in effect, and Save / Inherit.
- `GET /api/v1/notifications/retention` (the bell panel) still reports the **company-wide** values.

## Consequences

- A category's rule can be longer than the company's, so a company cannot be capped by the system defaults (as in ADR 0025).
- A category that no module sends yet can be given a rule in advance through the API; the UI lists it only once messages exist or it has a rule.
  Clearing the rule of such a category removes it from the list.
- Rules take effect at the next scheduled purge; nothing is deleted when one is saved.
- The bell's retention line does not mention category rules, so it can be inaccurate for messages in a category with its own rule.
- Not covered: per-user retention, a category-specific purge interval, archiving before deletion, bulk copy between companies.
