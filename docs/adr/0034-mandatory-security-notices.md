# ADR 0034: Account notices are mandatory security notices

## Status

Accepted. Changes ADR 0032 and 0028 for the `account` category.

## Context

ADR 0032 let people turn off the emails about changes to their own access, like any other category. That is wrong for these particular
emails: their value is that someone whose access was changed (or taken over by someone who granted themselves a role) finds out, and a
person who switched email off, or a compromised account whose email preferences were changed, would silently never be told.

## Decision

- The **`account` category is mandatory** (`NotificationCategories.Mandatory`). Its email is sent regardless of the recipient's master email
  switch and any per-category setting. `NotificationMessage` has a `Mandatory` flag; the email says so ("a security notice about your account,
  sent whatever your email settings are") instead of offering to turn it off.
- It is **not offered as a choice**: the preferences API does not list it, and `PUT .../email-categories/account` is refused with a clear
  message. A stale opt-out row from before this change is ignored. `account` also leaves the retention category list, since these notices
  create no in-app messages.
- Unchanged: the notices go only when the installation has email configured, only to a person with an address, never to the administrator
  about their own action, and never fail the administrator's change (best effort, through the outbox).

## Consequences

- The only way to stop these emails is to switch email off for the installation or not give the user an address.
- Any future security email (a password change or reset, a new-device sign-in) should use the same flag.
- The master switch still governs everything else; a person with it off still gets security notices and nothing else.
