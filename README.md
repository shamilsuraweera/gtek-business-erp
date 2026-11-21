# GTEK Business ERP

[![CI](https://github.com/shamilsuraweera/gtek-business-erp/actions/workflows/ci.yml/badge.svg)](https://github.com/shamilsuraweera/gtek-business-erp/actions/workflows/ci.yml)

A multi-company business ERP built as a **.NET 10 modular monolith** with a **React 19 web client** and **PostgreSQL**. It runs two complete
business cycles end to end, with a ledger that always balances, and tells people what needs their attention in the app and by email.

> **Order to cash:** customer → sales order → invoice (stock issue and ledger posting) → payment, with credit notes, returns to stock and refunds
>
> **Procure to pay:** vendor → purchase order → goods receipts → vendor bill matched to the order and receipts (three-way match) → payment, with
> returns to vendor, debit notes and vendor refunds

**Status:** a working, tested local system (see [Project status](#project-status)). It has not been deployed to production and has no production
deployment configuration yet.

## Contents

[What it does](#what-it-does) · [Quick start](#quick-start) · [Demo data](#demo-data-and-what-to-try) · [Configuration](#configuration) ·
[Testing and CI](#testing-and-ci) · [Architecture](#architecture-and-repository-layout) · [API](#api) · [Project status](#project-status) · [Documentation](#documentation)

## What it does

| Area | Capabilities |
|---|---|
| **Platform** | Multiple companies; users with JWT sign-in; global roles and fine-grained permissions; per-user company access; append-only audit trail; company-scoped number sequences; accounting periods (open, close, lock) |
| **Finance** | Chart of accounts; balanced journals and reversals; trial balance; configurable account mapping; tax codes with output and input tax and a net-payable summary; an append-only ledger |
| **Inventory** | Items (with price, cost, tax code, matching tolerances and an optional reorder level); locations; append-only stock ledger; receipts and adjustments; oversell protection |
| **Sales** | Customers; orders (draft → confirmed → released → invoiced); invoices with tax; payments; credit notes and returns to stock at original cost; refunds; receivables ageing |
| **Purchasing** | Vendors; purchase orders; partial goods receipts; close short; three-way matched vendor bills with price tolerances; price-variance approval; payments; returns to vendor, debit notes, vendor refunds |
| **Web app** | Dashboard, sales, purchasing, inventory, ledger and administration screens that show only what the signed-in user's role allows |

Things that cut across the modules:

- **Matching tolerances.** Vendor bills are matched to orders and receipts within a price tolerance (2% by default, settable per vendor, per item
  and per order line), an over-delivery tolerance and an under-delivery tolerance. Changing them on an order needs the right permission, or a
  **request that a different approver approves or rejects** (four-eyes). ADRs 0013, 0018 to 0022.
- **Notifications.** An in-app bell tells people what needs them: tolerance requests and decisions, and scheduled summaries of overdue
  invoices, overdue bills, items at or below their reorder level (net of what is already on order) and accounting periods that ended but are still
  open. Messages are purged after a configurable retention, per company and per category. ADRs 0023 to 0025, 0027, 0029 to 0031, 0037.
- **Email** (off until configured). The same events can be emailed through an outbox with retries, so a mail-server outage never fails a business
  operation. Each user can switch email off overall or per category. Changes to *your own account or access* (account created, access or roles
  changed, deactivated) are **security notices** that cannot be switched off. ADRs 0026, 0028, 0032, 0034.
- **Safe to run on several instances.** Email delivery claims rows with a lease, and scheduled reminders take a database lock so nobody is told
  twice. ADRs 0026, 0035.
- **Audit and integrity.** Administrative and security actions are recorded in an append-only audit history. Posted financial history is never
  edited; corrections are reversals. ADRs 0003, 0010.

## Quick start

### Prerequisites

- .NET SDK 10 (`global.json` allows any 10.0.x feature band) and the `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`
- PostgreSQL on `localhost:5432` (CI and the compose file use 16; or `docker compose up -d postgres`, see [PostgreSQL notes](#postgresql-notes))
- Node 22+ and npm for the web app
- Optional: `psql`, Python 3 and a browser, for the smoke test and browser tests

The commands below are for a Unix shell; on Windows use the same commands in PowerShell with the obvious changes (environment variables, line
continuation).

### 1. Create the role and database (once, as a PostgreSQL superuser)

```bash
sudo -u postgres psql -f scripts/dev-db.sql        # creates role gtek (password gtek_dev) and database gtek_erp
```

### 2. Configure the API

Create `src/Host/Erp.Api/appsettings.Development.json` (git-ignored):

```json
{
  "ConnectionStrings": { "Erp": "Host=localhost;Port=5432;Database=gtek_erp;Username=gtek;Password=gtek_dev" },
  "Authentication": {
    "BootstrapSecret": "dev-bootstrap-secret",
    "Jwt": { "SigningKey": "<output of: openssl rand -base64 32>" }
  }
}
```

### 3. Apply the migrations and load demo data

```bash
bash scripts/dev-setup.sh --seed      # migrates every module's schema, then seeds the demo company (safe to re-run)
```

Migrations are never applied automatically when the API starts (ADR 0012); this script is how you apply them. To start over, drop and recreate
the `gtek_erp` database and run it again.

### 4. Start the API and the web app

```bash
# terminal 1: the API on http://localhost:5004
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Host/Erp.Api --no-launch-profile --urls http://localhost:5004

# terminal 2: the web app on http://localhost:5173 (proxies /api to the API)
cd web && npm install && npm run dev
```

Open **http://localhost:5173** and sign in as **`admin` / `Admin#123456`**, company **GTEK Demo (Pvt) Ltd**. Check the API with
`curl http://localhost:5004/api/v1/health`.

## Demo data and what to try

The seeder creates a company with a chart of accounts, tax codes (`VAT18`, `ZERO`), 15 items with opening stock, 8 customers, 5 vendors, sales
and purchase orders in every state, overdue invoices and bills, and a tolerance-approval history. Accounting months before September 2026 are closed;
September 2026 onwards stay open, so months that have ended and are still open are what the period-close reminder reports.

| User | Password | Can do |
|---|---|---|
| `admin` | `Admin#123456` | Everything, including administration and approving tolerance requests |
| `apclerk` | `Clerk#1234567` | Receive goods, enter and pay bills, raise tolerance requests; cannot open Finance or Administration or approve their own requests |

Things worth trying:

1. **Order to cash:** *Sales orders → New order*, confirm, release, create the invoice, record a payment, then open *Ledger and reports* and see "Balanced".
2. **Approvals:** as `apclerk` open a purchase order and choose *Request tolerance change*; sign in as `admin` and approve or reject it under *Tolerance requests*.
3. **The bell:** a couple of minutes after the API starts, the reminders appear (overdue invoices and bills, low stock, open periods). The panel also holds your email switches.
4. **Reorder levels:** *Items and stock → Edit* an item and set a reorder level; the printer starts below its seeded level.
5. **Administration:** accounting periods, number sequences, users (access and roles), the audit log, and notification retention per company and per category.

For emails, run any SMTP catcher (for example `docker run --rm -p 1025:1025 -p 8025:8025 axllent/mailpit`, inbox on :8025) and start the API with
`Email__Enabled=true Email__Host=127.0.0.1 Email__Port=1025 Email__Security=None Email__FromAddress=erp@gtek.local Email__BaseUrl=http://localhost:5173`.

## Configuration

Settings come from `appsettings*.json`, environment variables (`Section__Key`), and **secret files** (below). Later sources win.

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:Erp` | local `gtek_erp` | PostgreSQL connection |
| `Authentication:BootstrapSecret` | none | Allows creating the first administrator |
| `Authentication:Jwt:SigningKey` | ephemeral in Development | Base64 32-byte key for tokens; set a persistent one anywhere tokens must survive restarts |
| `Email:*` | `Enabled: false` | SMTP host, port, security (`None`/`StartTls`/`SslOnConnect`), credentials, `FromAddress`, `FromName`, `BaseUrl`; retry and retention tuning. An incomplete configuration stops start-up with every problem listed (ADR 0026) |
| `Reminders:*` | on, 08:00, UTC | `Enabled`, `TimeZone` (IANA id), `Hour`, `RepeatDays` (1), `IntervalMinutes` (60), `InitialDelaySeconds`, `PeriodCloseGraceDays` (3) (ADRs 0029, 0031, 0036) |
| `Notifications:Retention:*` | read 30 days, unread 180 | System default retention; each company can override it, per category (ADRs 0024, 0025, 0027) |
| `ERP_SECRETS_DIR` | `/run/secrets` | Folder of secret files (ADR 0038) |
| `ERP_DATABASE_CONNECTION` | — | Connection used by `dotnet ef` and `scripts/dev-setup.sh` |
| `ERP_TEST_DATABASE_CONNECTION` | — | A disposable database for the PostgreSQL integration tests |

**Secret files.** Any setting can be supplied as a file named like the setting with `__` for `:`, for example `/run/secrets/Email__Password`
(where Docker and Kubernetes mount secrets). Secret files are read last, so they win; a trailing newline is ignored and a missing folder is fine.
Keep the SMTP password, the signing key and the database password out of appsettings.

Posting accounts are configurable: sales, purchasing and inventory post to *roles* (receivable, revenue, inventory, payable, goods received not
invoiced, and so on) that you map to your own ledger accounts under *Finance → Account mapping* (ADR 0016). Unmapped roles use the defaults:
1000/1010 cash and bank, 1100 receivable, 1200 inventory, 2000 payable, 2100 goods received not invoiced, 2200 output tax, 1300 input tax,
4000 revenue, 4100 sales returns, 5000 cost of goods sold, 5100 purchase price variance.

## Testing and CI

| Suite | Command | What it covers |
|---|---|---|
| Unit | `dotnet test tests/Erp.UnitTests` | Domain rules, application services, reminders, email, notifications (417 tests) |
| Architecture | `dotnet test tests/Erp.ArchitectureTests` | Module boundaries and dependency rules (3) |
| Functional | `dotnet test tests/Erp.FunctionalTests` | The HTTP host on an in-memory database (10) |
| Integration | `dotnet test tests/Erp.IntegrationTests` | Real SQL on PostgreSQL: locks, outbox claims, purges, queries (27). Needs `ERP_TEST_DATABASE_CONNECTION`, migrated with `scripts/dev-setup.sh` |
| **Smoke + browser** | `scripts/smoke/run.sh` | A real API, PostgreSQL and SMTP conversation (84 checks), then Playwright browser flows (10 tests); about two and a half minutes (ADR 0033) |

`dotnet test` runs the first four (the integration tests need the environment variable). `scripts/smoke/run.sh` builds a throwaway database, seeds it,
starts the API with email and reminders switched on, runs `scripts/smoke/smoke.py` (standard library only, with a built-in SMTP sink) and then the
browser tests in `web/e2e`. Options: a section word to run only matching sections, `SMOKE_BROWSER=0`, `SMOKE_KEEP=1`. The browser flows need
`cd web && npm ci && npx playwright install chromium` once (or `E2E_CHROMIUM=/path/to/chrome`).

**GitHub Actions** (`.github/workflows/ci.yml`) runs on every push and pull request: build and test, the web build, and the smoke test with the browser
flows; failure artifacts (Playwright traces) are kept for a week.

## Architecture and repository layout

```text
src/
  BuildingBlocks/   SharedKernel (aggregate roots, domain events, ids) and Application.Abstractions (cross-module contracts)
  Host/Erp.Api      composition root: endpoints, authentication, hosted workers (email, reminders, retention), dev seeder
  Modules/          Platform, Finance, Inventory, Sales, Purchasing; each has Domain, Application, Infrastructure (and Contracts)
tests/              unit, architecture, functional, integration
web/                React 19 + Vite + TypeScript + TanStack Query; e2e/ holds the Playwright tests
scripts/            dev-setup.sh, dev-db.sql, smoke/ (smoke test and its runner)
docs/               user guide, status, architecture decision records (docs/adr)
```

Rules the code follows (enforced by the architecture tests where they can be):

- Modules talk only through contracts in `Erp.Application.Abstractions` or a module's `Contracts` project, never each other's tables.
- Each module owns its PostgreSQL schema and its migrations history (ADRs 0001, 0002). Domain projects do not reference EF Core or ASP.NET.
- Every company-scoped request carries `X-Company-Id`, and access is checked on the server (ADRs 0004, 0006, 0009).
- Money is `decimal`. Posted ledger and stock history is append-only; corrections are reversals (ADR 0003).
- Permissions are resolved on the server at request time, not copied into tokens (ADR 0008).

### PostgreSQL notes

`docker compose up -d postgres` starts a PostgreSQL 16 container with a separate `gtek` user (override with `POSTGRES_DB`, `POSTGRES_USER`,
`POSTGRES_PASSWORD`; it maps port 5432, so stop any local server first). Adopting a database created by the early SQL bootstrap needs the scripts
in `scripts/database` and a backup (ADR 0012).

## API

All endpoints are under `/api/v1`; the generated OpenAPI document is at `/openapi/v1.json`, and `src/Host/Erp.Api/Erp.Api.http` has requests to try.
Sign in with `POST /api/v1/auth/login` and send the token as `Authorization: Bearer …`; company-scoped calls also need `X-Company-Id`.

| Group | Examples |
|---|---|
| `auth`, `users`, `roles`, `companies` | sign-in, current user and preferences, user and role administration, company access |
| `accounting-periods`, `number-sequences`, `audit` | period close and lock, document numbering, audit history |
| `finance/…` | accounts, journals, trial balance, account mapping, tax codes and the tax summary |
| `inventory/…` | items, locations, stock levels, receipts and adjustments |
| `sales/…` | customers, orders, invoices and payments, credit notes and refunds |
| `purchasing/…` | vendors, orders, receipts, bills, payments, debit notes, tolerance requests |
| `notifications`, `notification-settings/…` | the bell, retention settings, email status |

The first user is created once with the bootstrap secret (`POST /api/v1/users` with the `X-Bootstrap-Secret` header); after that, users are
managed by administrators. The demo seeder does this for you.

## Project status

The full detail is in [docs/STATUS.md](docs/STATUS.md). In short:

- **Built and tested:** both business cycles, tax, matching tolerances with an approval workflow, notifications, email, scheduled reminders,
  multi-company administration, audit, and a web client covering all of it. 457 automated tests plus a live smoke and browser test, all run in CI.
- **Not built:** multi-currency, reporting beyond the trial balance, tax summary and ageing, integrations, per-location reorder levels,
  password change or reset, company-specific roles, bounce handling for email, and production deployment configuration.

## Documentation

- [docs/USER-GUIDE.md](docs/USER-GUIDE.md): how to run, use and administer the application
- [docs/STATUS.md](docs/STATUS.md): what is built, what is not, and known limitations
- [docs/adr/](docs/adr/README.md): the architecture decision records (0001 to 0038), indexed by topic
- [ERP-IMPLEMENTATION-SPECIFICATION.md](ERP-IMPLEMENTATION-SPECIFICATION.md): the long-term phase roadmap this project follows
- [CONTRIBUTING.md](CONTRIBUTING.md): what to run before a change is done
