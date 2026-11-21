# Project status

Where the project stands, what is built and tested, and what is deliberately not built. Updated at the end of the current development round.
For how things work see the [user guide](USER-GUIDE.md); for why, the [decision records](adr/README.md).

## Summary

A working modular-monolith ERP with a web client that covers order to cash and procure to pay, tax, three-way matching with an approval workflow,
multi-company administration, notifications, email and scheduled reminders. It is tested at every level (457 automated tests, a live smoke test
of 84 checks, and 10 browser tests) and all of it runs in GitHub Actions on every push. It runs locally; it has **not been deployed** and has
**no production deployment configuration** (no container image, no hosting or secrets setup beyond the secret-file support).

## Built

### Platform

- Companies, users, JWT sign-in, global roles and permissions, per-user company access (revocation takes effect on existing tokens).
- Audit trail (append-only) for administrative and security actions; company-scoped number sequences; accounting periods with close, reopen and lock.
- In-app notifications, an email outbox with retries, per-user and per-category email preferences, **mandatory security notices** for changes to a
  person's own account and access, retention (system default, per company, per category), and a cross-instance job lock.
- Scheduled reminders in a hosted worker: overdue invoices, overdue bills, low stock, accounting periods still open after they ended, on a
  configurable time zone and hour.

### Finance

- Chart of accounts, balanced journals, reversals, trial balance, configurable account mapping, tax codes with output and input tax and a net-payable summary.

### Inventory

- Items with cost, price, tax code, matching tolerances and an optional reorder level; locations; an append-only stock ledger; receipts and adjustments
  with oversell protection; stock levels with an "ever stocked" flag.

### Sales

- Customers, orders, invoices with tax, payments, credit notes with returns to stock at original cost, refunds, receivables ageing.

### Purchasing

- Vendors, purchase orders, partial receipts and close short, three-way matched vendor bills (price, over-delivery and under-delivery tolerances at
  item, vendor and order-line level), price-variance approval, payments, returns, debit notes, vendor refunds.
- A **tolerance request workflow**: a request with a reason, one open request per order, a different approver decides, every step audited.

### Web client

- Dashboard (KPIs, order pipeline, ageing, low stock), customers, items and stock, sales orders and invoices, vendors, purchase orders, bills,
  tolerance requests, ledger and reports, administration (periods, number sequences, users, audit log, notifications and email status), the
  notification bell with email preferences. Navigation and actions follow the signed-in user's permissions.

## Quality

| What | Where | Count |
|---|---|---|
| Unit tests | `tests/Erp.UnitTests` | 417 |
| Architecture tests | `tests/Erp.ArchitectureTests` | 3 |
| Functional tests (HTTP host, in-memory) | `tests/Erp.FunctionalTests` | 10 |
| PostgreSQL integration tests | `tests/Erp.IntegrationTests` | 27 |
| Live smoke test (API, PostgreSQL, SMTP) | `scripts/smoke/smoke.py` | 84 checks |
| Browser flows (Playwright) | `web/e2e` | 10 tests |
| CI | `.github/workflows/ci.yml` | build and test, web build, smoke and browser flows |

Dependency audit of the web client is clean (`npm audit`: 0 vulnerabilities at the time of writing). There is no automated dependency update or audit in CI.

## Known limitations

These are things the code does not do, not bugs.

**Finance and documents**
- Single currency; no multi-currency or revaluation.
- Reporting is the trial balance, tax summary and receivables ageing; there is no balance sheet, income statement or cash-flow report, and no exports.
- Manual stock receipts do not post to the ledger (receipts against purchase orders do).
- Credit notes are at the invoiced price: no partial price adjustments.
- Business dates (document dates, what counts as overdue) are UTC; there are no per-company time zones.

**Access and accounts**
- Roles are global, not company-specific; company access is the only company-level scoping.
- No password change or reset flow, no email verification, no multi-factor authentication.

**Notifications and email**
- Delivery is at least once: a crash between the mail server accepting a message and the database recording it could send it twice (each email has a
  stable `Message-ID` so a copy can be recognised). Bounces are not processed.
- One sender and one set of templates for the installation; English only; one email per event (no digests).
- Reminders follow one time zone and one send hour for the installation. With default settings a person with something overdue is emailed daily
  until it is resolved; they can opt out per category (not for security notices).
- Only the events listed in the user guide send email.

**Inventory**
- A reorder level is per item, not per location; there are no expected delivery dates or suggested order quantities. Alerts net out released
  purchase orders but do not create them.
- On the dashboard an item without a reorder level counts as low only if it has been stocked and has 8 or fewer.

**Operations**
- No production deployment configuration, container image or infrastructure code; no application metrics or alerting beyond logs.
- Migrations are applied by script, never automatically (a deliberate choice, ADR 0012); there is no upgrade-path testing from older databases.
- The browser tests cover the main screens in Chromium only, with no visual-regression or cross-browser testing.

## Possible next steps

Roughly in the order they would pay off:

1. **Deployment:** a container image, a production configuration profile, and secrets and database setup for a real environment.
2. **Reporting:** balance sheet and income statement, exports (CSV, PDF), saved report periods.
3. **Accounts:** password change and reset (using the mandatory-notice mechanism), email verification.
4. **Multi-currency** with exchange rates and revaluation.
5. **Inventory:** per-location reorder levels, expected delivery dates, suggested order quantities.
6. **Email:** per-company sender and templates, digests, bounce handling, translations.
7. **Process:** automated dependency updates and audit in CI; upgrade-path tests against a previous release's database.

The long-term phase roadmap (HR, manufacturing, and so on) is in [ERP-IMPLEMENTATION-SPECIFICATION.md](../ERP-IMPLEMENTATION-SPECIFICATION.md).
