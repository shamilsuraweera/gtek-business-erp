# Architecture decision records

Each record states the context, the decision and its consequences. Where a later record changes an earlier one, the earlier one says so at the top.
They are grouped by topic here; the numbers are the order the decisions were made.

## Architecture and foundations

- [0001: Modular monolith](0001-modular-monolith.md)
- [0002: Module database ownership](0002-module-database-ownership.md)
- [0003: Ledger-based posting](0003-ledger-based-posting.md)
- [0004: Multi-company architecture](0004-multi-company-architecture.md)
- [0005: Domain events](0005-domain-events.md)
- [0006: Company context and data isolation](0006-company-data-isolation.md)
- [0012: EF baseline and migration authority](0012-database-migration-authority.md)

## Identity, access and audit

- [0007: Local authentication and user identity](0007-authentication-and-user-identity.md)
- [0008: Role and permission authorisation](0008-role-and-permission-authorisation.md)
- [0009: User-company access and active company authorization](0009-user-company-access.md)
- [0010: Append-only audit trail](0010-audit-trail.md)
- [0011: Company-scoped number sequences](0011-number-sequences.md)

## Sales, purchasing and finance

- [0013: Purchasing partial receipts and three-way match](0013-purchasing-three-way-match.md)
- [0014: Sales credit notes, returns and refunds](0014-sales-credit-notes-and-returns.md)
- [0015: Purchasing returns to vendor, debit notes and vendor refunds](0015-purchasing-returns-and-debit-notes.md)
- [0016: Configurable account mapping](0016-configurable-account-mapping.md)
- [0017: Tax support](0017-tax-support.md)

## Matching tolerances and approvals

- [0018: Price tolerances per vendor and per item](0018-price-tolerances.md)
- [0019: Over-delivery quantity tolerance](0019-over-delivery-tolerance.md)
- [0020: Under-delivery tolerance](0020-under-delivery-tolerance.md)
- [0021: Per-order tolerance overrides](0021-per-order-tolerance-overrides.md)
- [0022: Approval workflow for tolerance overrides](0022-tolerance-override-approval.md)

## Notifications and email

- [0023: In-app notifications](0023-in-app-notifications.md)
- [0024: Notification retention](0024-notification-retention.md)
- [0025: Per-company notification retention](0025-per-company-notification-retention.md)
- [0026: Email delivery of approval requests](0026-email-delivery.md)
- [0027: Per-category notification retention](0027-per-category-notification-retention.md)
- [0028: Per-category email opt-out](0028-per-category-email-opt-out.md)
- [0032: Account and access change notices (email)](0032-account-change-notices.md)
- [0034: Account notices are mandatory security notices](0034-mandatory-security-notices.md)

## Scheduled reminders

- [0029: Overdue invoice and bill reminders (scheduled email)](0029-overdue-reminders.md)
- [0030: Low-stock alerts (scheduled email)](0030-low-stock-alerts.md)
- [0031: Period-close reminders (scheduled email)](0031-period-close-reminders.md)
- [0035: A cross-instance lock for scheduled work](0035-job-lock.md)
- [0036: A time zone for the reminder schedule](0036-reminder-time-zone.md)
- [0037: Low-stock alerts net out what is on order; the dashboard stops counting never-stocked items](0037-on-order-netting-and-dashboard-low-stock.md)

## Quality, delivery and operations

- [0033: Smoke test of the live API, and CI](0033-smoke-test-and-ci.md)
- [0038: Secrets as files; dependency audit](0038-secret-files.md)

## Notes on superseded decisions

- ADR 0032 (account notices could be opted out of) was replaced by [ADR 0034](0034-mandatory-security-notices.md): they are now mandatory.
- ADR 0029 fixed the reminder schedule to UTC; [ADR 0036](0036-reminder-time-zone.md) made it follow a time zone.
- ADR 0030's low-stock rule was refined by [ADR 0037](0037-on-order-netting-and-dashboard-low-stock.md).
