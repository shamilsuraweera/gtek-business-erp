# ADR 0032: Account and access change notices (email)

## Status

Accepted

## Context

Until now email (ADRs 0026 to 0031) was about business events inside a company. The other events people most want to hear about are the
ones that change *their own access*: an account created for them, access to a company given or taken away, their roles changed, the
account deactivated. Today an administrator can do any of that and the person finds out when something stops working.

## Decision

- A new category, **`account`**, emailed to the **affected user**. Events (all done by an administrator):
  an account created for them; company access granted, removed or restored; a role given or taken away; the account deactivated or reactivated.
  Each email names the administrator who did it, and the company or role involved. Removal and deactivation add "if you did not expect this,
  contact an administrator". The new-account email gives the user name and **never the password**.
- **Email only, no in-app message.** These are not company events, and a person whose access to a company was just removed could not open it
  to read a message. The outbox row (ADR 0026) has no company (`CompanyId` null) and records the user.
- Sent by `AccountNotices` (Platform), called by the existing user, role and company-access services **after** their change is saved.
  It is **best effort**: a failure is logged and never undoes or fails the administrator's change, and queuing in the outbox never
  waits for the mail server, as in ADR 0026. The services take it as an optional dependency, so nothing else that builds them changes.
- **Not sent** when email is switched off for the installation, when the person turned email off (ADR 0026) or opted out of the `account`
  category (ADR 0028), when the administrator acts on themself, or for the very first (bootstrap) administrator, who has nobody to tell.
- The email status shown in Administration (ADR 0026) counts these company-less messages in every company's totals, so a failed account
  notice is visible there like any other.
- Being a category, `account` gets the per-category opt-out and per-category retention lists. It is shown as "Changes to my account and
  access" in the bell panel.

## Consequences

- Account notices can be turned off by the person they concern. That is a choice: a mandatory security notice would have to bypass the
  opt-outs, and this installation has no email verification to make that safe. Someone who needs mandatory notices should leave the master switch on.
- Roles are global, not per company, so a role email is not tied to a company.
- There is no password-change or reset flow in the product yet, so there are no such emails. When there is one, it should use this
  mechanism but not be opt-out-able.
- Changing several things at once sends one email per change.
- Not covered: notices to administrators about each other's actions, failed sign-in alerts, new-device sign-in notices, and role permission changes (changing what a role can do does not tell its holders).
