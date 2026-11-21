# GTEK Business ERP

GTEK Business ERP is a .NET 10 modular-monolith ERP platform with a React web
client. The local MVP demonstrates two complete workflows:

> **Order-to-cash:** customer → sales order → invoice (stock issue + ledger posting) → payment, with credit notes, returns to stock and refunds
>
> **Procure-to-pay:** vendor → purchase order → goods receipts (partial deliveries, stock in, GRNI accrual) → vendor bills matched to the order and receipts → payment, with returns to vendor, debit notes and vendor refunds

| Module | What works today |
|---|---|
| Platform | Companies, users, JWT sign-in, roles/permissions, company access, audit trail, number sequences, accounting periods |
| Finance | Chart of accounts, balanced journals, reversals, trial balance (append-only ledger) |
| Inventory | Items, locations, append-only stock ledger, receipts/adjustments, oversell protection |
| Sales | Customers, orders (draft → confirmed → released → invoiced), invoices, payments, credit notes and returns (stock and COGS reversed at original cost), refunds |
| Purchasing | Vendors, purchase orders, partial goods receipts (stock at PO cost), close short, three-way matched vendor bills, price-variance approval, payments, returns to vendor, debit notes, vendor refunds |
| Web (`web/`) | Dashboard, sales, purchasing, inventory, ledger and administration screens |

## Run the MVP locally

Prerequisites: .NET SDK 10, Node 22+, and PostgreSQL on `localhost:5432`.

```bash
# 1. Create the role and database once (needs a Postgres superuser)
sudo -u postgres psql -f scripts/dev-db.sql

# 2. Create src/Host/Erp.Api/appsettings.Development.json (git-ignored)
#    { "ConnectionStrings": { "Erp": "Host=localhost;Port=5432;Database=gtek_erp;Username=gtek;Password=gtek_dev" },
#      "Authentication": { "BootstrapSecret": "dev-bootstrap-secret",
#                          "Jwt": { "SigningKey": "<run: openssl rand -base64 32>" } } }

# 3. Apply every module's migrations and load demo data (idempotent)
scripts/dev-setup.sh --seed

# 4. Start the API (http://localhost:5004) and the web app (http://localhost:5173)
dotnet run --project src/Host/Erp.Api
cd web && npm install && npm run dev
```

Sign in as **`admin` / `Admin#123456`** and pick the **GTEK Demo (Pvt) Ltd** company.
A restricted demo user, **`apclerk` / `Clerk#1234567`**, can receive goods and enter and pay bills
but cannot approve price variances beyond the tolerance (2% by default) or open Finance and Administration.
The demo data contains 15 items with opening stock, 8 customers, 5 vendors, and
sales and purchase orders in every state (drafts through paid and overdue documents).
It also shows the matching tolerances and the approval workflow: the importer VEN-004 allows 5% over or under delivery, VEN-003 allows a 5% price
difference, the laptop LAP-002 is held to a 1% price and cable accepts 10% over and 5% under. Three tolerance requests from the AP clerk are
seeded: one approved, one rejected and one pending, so the administrator's bell shows 1 unread and the clerk's shows 2.

Notes:

- Migrations are never applied automatically at API start (ADR 0012); use
  `scripts/dev-setup.sh`.
- Re-running `--seed` is safe. To start over, drop and recreate the `gtek_erp` database.
- Integration tests need a disposable database: `ERP_TEST_DATABASE_CONNECTION="Host=localhost;Database=gtek_erp_test;Username=gtek;Password=gtek_dev"`.
- Posting accounts are configurable: sales, purchasing and inventory post to *roles* (receivable, revenue, inventory, payable, GRNI, …)
  that you map to your own ledger accounts under **Finance → Account mapping**; unmapped roles use the default codes below (ADR 0016).
  The API also grants newly introduced permissions to the system administrator role at startup.
- MVP limitations: default account codes are 1000/1010 cash and bank, 1100 receivable, 1200 inventory, 2000 payable, 2100 goods received
  not invoiced, 4000 revenue, 4100 sales returns, 5000 COGS, 5100 purchase price variance, 2200 output tax payable, 1300 input tax recoverable; manual stock receipts do not post to the ledger;
  the three-way match price tolerance defaults to 2% and can be set per vendor and per item (ADR 0018); over-delivery is refused unless a per-vendor or per-item quantity tolerance allows it (ADR 0019), and a short delivery completes the order automatically only within an under-delivery tolerance (ADR 0020), otherwise use *Close short*; any of the three tolerances can be overridden per order line directly by users with the *Approve order tolerance overrides* permission (ADR 0021), or by request: anyone who manages orders can raise a request with a reason, and an approver other than the requester approves or rejects it (ADR 0022); approvers and requesters are told through the notification bell in the top bar (ADR 0023) and, when email is configured, by email (ADR 0026), which each user can switch off overall or per category from the bell panel (ADR 0028); open invoices and bills past their due date are summarised for the people who record payments, in the app and by email, once a day from 08:00 UTC (`Reminders` configuration, ADR 0029), and items at or below their reorder level are summarised the same way for the people who manage stock (ADR 0030), and ended accounting periods that are still open (after `Reminders:PeriodCloseGraceDays`, default 3) for the people who manage periods (ADR 0031); people are also emailed when an administrator creates their account, changes their company access or roles, or deactivates them (ADR 0032), and read messages are purged after 30 days, unread ones after 180, configurable under `Notifications:Retention` (ADR 0024), and each company can set its own under Administration → Notifications (ADR 0025), optionally per notification category (ADR 0027); credit notes are at the invoiced
  price (no partial price adjustments); there is no multi-currency yet.
- Tax: prices are tax-exclusive. Create tax codes under **Finance → Tax** (the demo has `VAT18` 18% and `ZERO`) and assign one to each item;
  invoices then charge output tax (posted to 2200) and vendor bills record input tax (1300). Credit notes and debit notes return the tax
  that was charged, and **Finance → Tax** shows the net payable for any period (ADR 0017). Items with no tax code carry no tax.

## Quick start

### 1. Install prerequisites

- .NET SDK 10.0.400
- PostgreSQL running on `localhost:5432`
- Optional: Docker Desktop, if you prefer the included PostgreSQL container

### 2. Create the development database

Create the database once if it does not already exist:

```powershell
psql -U postgres -h localhost -c "CREATE DATABASE gtek_erp;"
```

If PostgreSQL asks for a password, enter the password for your local
`postgres` user.

Phase 1.6 includes company-scoped number sequences. Configure them through
`/api/v1/number-sequences` with `platform.number-sequences.manage`, then reserve
numbers through `POST /api/v1/number-sequences/{code}/next` with
`platform.number-sequences.read`. PostgreSQL row locking makes issuance safe
across concurrent API instances. Gaps are allowed and reserved values are never
reused.

The development connection is configured in the ignored
`src/Host/Erp.Api/appsettings.Development.json` file. Do not commit real
credentials. You can also override it for one PowerShell session:

```powershell
$env:ConnectionStrings__Erp = "Host=localhost;Port=5432;Database=gtek_erp;Username=postgres;Password=<your-password>"
```

### 3. Restore, build, and test

Run these commands from the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
```

### 4. Start the API

```powershell
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj
```

The development launch profile uses:

- HTTP: `http://localhost:5004`
- HTTPS: `https://localhost:7104`

Keep the terminal running while you use the API. Stop the application with
`Ctrl+C`.

### 5. Verify that it is running

In a second PowerShell terminal:

```powershell
Invoke-WebRequest http://localhost:5004/api/v1/health
```

A successful response has HTTP status `200`.

## Available API functionality

The current API exposes the Phase 1.6 platform endpoints and representative
company-scoped endpoints:

| Method | Endpoint | Current behavior |
|---|---|---|
| `GET` | `/api/v1/health` | Returns `200` when the API host is running |
| `POST` | `/api/v1/companies` | Creates an active company |
| `GET` | `/api/v1/companies` | Lists persisted companies |
| `GET` | `/api/v1/companies/{id}` | Gets a company by ID |
| `GET` | `/api/v1/companies/by-code/{code}` | Gets a company by normalized code |
| `PUT` | `/api/v1/companies/{id}/name` | Renames a company |
| `POST` | `/api/v1/companies/{id}/activate` | Activates a company |
| `POST` | `/api/v1/companies/{id}/deactivate` | Deactivates a company |
| `GET` | `/api/v1/finance/accounts` | Representative company-scoped endpoint |
| `POST` | `/api/v1/auth/login` | Authenticates a user and returns a JWT |
| `GET` | `/api/v1/auth/me` | Returns the authenticated user |
| `GET` | `/api/v1/auth/me/companies` | Lists companies available to the authenticated user |
| `GET` | `/api/v1/users` | Lists users for a bootstrap administrator |
| `GET` | `/api/v1/users/{userId}/companies` | Lists companies assigned to a user |
| `POST` | `/api/v1/users/{userId}/companies/{companyId}` | Grants or restores company access |
| `DELETE` | `/api/v1/users/{userId}/companies/{companyId}` | Revokes company access |
| `GET` | `/api/v1/companies/{companyId}/users` | Lists users with company access |
| `GET` | `/api/v1/audit` | Queries append-only audit history with bounded pagination and filters |
| `GET` | `/api/v1/number-sequences` | Lists number sequences for the active company |
| `POST` | `/api/v1/number-sequences` | Creates a company-scoped number sequence |
| `PUT` | `/api/v1/number-sequences/{id}/configuration` | Changes safe formatting configuration without resetting `NextValue` |
| `POST` | `/api/v1/number-sequences/{code}/next` | Atomically reserves the next number |
| `GET` | `/openapi/v1.json` | Returns the generated OpenAPI document |

Examples:

```powershell
Invoke-WebRequest http://localhost:5004/api/v1/health
Invoke-RestMethod http://localhost:5004/api/v1/companies
Invoke-RestMethod http://localhost:5004/api/v1/finance/accounts -Headers @{ "X-Company-Id" = "<company-id>" }
Invoke-RestMethod http://localhost:5004/openapi/v1.json
```

The Sales, Inventory and Finance endpoints live under `/api/v1/sales`, `/api/v1/inventory`
and `/api/v1/finance`; browse them in the OpenAPI document at `/openapi/v1.json`.

## Domain functionality available in Phase 1.6

Although the API is intentionally small, the solution includes the Phase 1.6
company-management slice and representative domain foundations:

- Platform: `Company`, `User`, `Role`, and `Permission`
- Finance: accounts, currencies, accounting periods, journals, and immutable
  general-ledger entries
- Sales: customers and controlled sales-order lifecycle transitions
- Purchasing: vendors and controlled purchase-order lifecycle transitions
- Inventory: items, locations, units of measure, and immutable item-ledger
  entries
- Shared kernel: aggregate roots, domain events, domain exceptions, company
  identifiers, clock abstraction, and company-context abstraction
- Audit: append-only Platform audit entries for security and administrative
  operations, queried with `platform.audit.read`

Audit history is not application logging and is not a financial ledger. The
audit API is system-scoped, so it does not require `X-Company-Id`; use
`companyId` to filter history for a company. Supported filters include
`from`, `to`, `actorUserId`, `companyId`, `category`, `action`, `entityType`,
`entityId`, and `outcome`. Results default to 50 entries and are capped at 200,
ordered newest first.

The domain tests currently verify balanced journals, rejected unbalanced
journals, controlled sales-order transitions, immutable posted ledger records,
and persistence schema ownership.

## PostgreSQL options

### Use local PostgreSQL

This is the recommended setup for the current development environment:

```powershell
$env:PGPASSWORD = "postgres"
psql -U postgres -h localhost -c "CREATE DATABASE gtek_erp;"
psql -U postgres -h localhost -d gtek_erp -f .\scripts\database\apply-platform-migration.sql
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj
```

The dependency-safe `20261005164810_PlatformBaseline` migration is authoritative
for new databases and future schema changes.

```powershell
$env:ConnectionStrings__Erp = "Host=localhost;Port=5432;Database=gtek_erp;Username=postgres;Password=postgres"
dotnet ef database update --connection $env:ConnectionStrings__Erp --project .\src\Modules\Platform\Erp.Modules.Platform.Infrastructure\Erp.Modules.Platform.Infrastructure.csproj --startup-project .\src\Host\Erp.Api\Erp.Api.csproj
```

Existing SQL-bootstrap databases require a backup and explicit schema validation
before adoption:

```powershell
.\scripts\database\adopt-platform-bootstrap.ps1 -ConnectionString $env:ConnectionStrings__Erp
.\scripts\database\adopt-platform-bootstrap.ps1 -ConnectionString $env:ConnectionStrings__Erp -Apply
```

The first command is read-only. The second records the baseline only after
validation succeeds. See [ADR 0012](docs/adr/0012-database-migration-authority.md).

### Use Docker PostgreSQL

Docker is optional and is retained for other contributors:

```powershell
docker compose up -d postgres
docker compose ps
docker compose logs -f postgres
```

The Docker defaults use a separate `gtek` database user. Docker maps port `5432`
on the host, so stop or reconfigure it if a local PostgreSQL service already
uses that port:

```powershell
docker compose down
```

Environment variables can override the Docker defaults:

```powershell
$env:POSTGRES_DB = "gtek_erp"
$env:POSTGRES_USER = "gtek"
$env:POSTGRES_PASSWORD = "development-only-password"
docker compose up -d postgres
```

## Repository commands

```powershell
# Restore NuGet packages
dotnet restore

# Compile the complete solution
dotnet build

# Run all tests
dotnet test

# Run only unit tests
dotnet test .\tests\Erp.UnitTests\Erp.UnitTests.csproj

# Run only architecture tests
dotnet test .\tests\Erp.ArchitectureTests\Erp.ArchitectureTests.csproj

# Start the API
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj
```

## Smoke test and CI

`scripts/smoke/run.sh` runs the business flows against a **real API, real PostgreSQL and a real SMTP conversation** (ADR 0033): it creates
a throwaway database, migrates and seeds it, starts the API with email and reminders switched on, and runs `scripts/smoke/smoke.py` (Python
standard library only, with a built-in SMTP sink). It covers sign-in and permissions, order to cash, three-way match, the tolerance approval
workflow and its emails, email preferences, the scheduled reminders, account-change notices, a rejected address not failing an operation,
retention settings, and that the ledger still balances.

```bash
scripts/smoke/run.sh               # all sections (about two minutes)
scripts/smoke/run.sh reminders     # only sections whose title contains the word
SMOKE_KEEP=1 scripts/smoke/run.sh  # keep the database and the API log (/tmp/gtek-smoke-api.log) afterwards
```

It needs the .NET SDK, the `dotnet-ef` tool, `psql`, `python3` and a PostgreSQL server you can create databases on
(`SMOKE_PG_ADMIN`, default `postgresql://gtek:gtek_dev@localhost:5432/postgres`). `.github/workflows/ci.yml` runs it, the unit,
architecture, integration and functional tests, and the web build on every push and pull request.

## Architecture and boundaries

This is a modular monolith:

- `Erp.Api` is the composition root.
- Each active module owns its Domain, Application, Infrastructure, and
  Contracts projects.
- Each module owns its persistence schema.
- Domain projects do not reference EF Core or ASP.NET Core.
- Financial values use `decimal`, never `float` or `double`.
- Posted financial history is immutable and corrections will use explicit
  reversal or correction transactions.

See [ERP-IMPLEMENTATION-SPECIFICATION.md](ERP-IMPLEMENTATION-SPECIFICATION.md)
for the complete Phases 1–12 roadmap and
[docs/USER-GUIDE.md](docs/USER-GUIDE.md) for the detailed operator/developer
guide.

## Authentication and user management

Phase 1.6 includes local JWT authentication, database-backed user management,
and policy-based roles and permissions.
Set secrets through environment variables rather than committed configuration:

```powershell
$env:Authentication__BootstrapSecret = "operator-supplied-bootstrap-secret"
$env:Authentication__Jwt__SigningKey = "<base64-encoded-32-byte-key>"
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj
```

In Development, the API can generate an ephemeral signing key when one is not
configured. Configure a persistent key for any environment where tokens must
survive restarts. Create the first user once:

```powershell
$body = '{"userName":"admin","email":"admin@example.com","password":"use-a-long-unique-password"}'
curl.exe -X POST http://localhost:5004/api/v1/users `
  -H "Content-Type: application/json" `
  -H "X-Bootstrap-Secret: $env:Authentication__BootstrapSecret" `
  -d $body
```

Then log in and call the authenticated user endpoint:

```powershell
$login = curl.exe -s -X POST http://localhost:5004/api/v1/auth/login `
  -H "Content-Type: application/json" `
  -d '{"userName":"admin","password":"use-a-long-unique-password"}' | ConvertFrom-Json
curl.exe http://localhost:5004/api/v1/auth/me -H "Authorization: Bearer $($login.accessToken)"
```

`GET /api/v1/users`, user lifecycle endpoints, and company-management
endpoints require the appropriate current server-side permission. The
authentication identity does not select a company; company-scoped requests
require `X-Company-Id` and active `UserCompanyAccess`.

## Current limitations

Phase 1.6 does not yet include:

- A web frontend
- Company-specific roles or permissions
- Database-backed finance account queries
- Journal-posting API endpoints
- Sales, purchasing, inventory, invoicing, or payment workflows
- Reporting, integrations, or production deployment configuration

Do not treat the representative endpoints as a completed ERP feature set.
### Roles and permissions

Phase 1.6 uses the server-side permission model and explicit company scope.
Permissions use stable
`module.resource.action` codes, roles contain explicit permission assignments,
and users have global role assignments. The initial catalogue includes
`platform.companies.*`, `platform.users.*`, `platform.roles.*`,
`platform.permissions.read`, `platform.audit.read`, and
`finance.accounts.read`.

Protected endpoints return `401` for an unauthenticated request and `403` for
an authenticated user without the declared permission. Permissions are
resolved from PostgreSQL at request time and are not copied into JWTs.
Company access is not copied into JWTs. It is resolved server-side before
CompanyContext is established, so revocation affects an existing token.
`SYSTEM_ADMIN` receives company-access permissions through normal role
assignments but has no implicit access to every company. Grant access with
`POST /api/v1/users/{userId}/companies/{companyId}` and discover selectable
companies with `GET /api/v1/auth/me/companies`; neither endpoint requires an
active company header.

## Email

Approval requests and their decisions can be emailed as well as shown in the app. Email is **off by default**; turn it on in the API's
configuration (appsettings or environment variables, e.g. `Email__Host`):

```json
"Email": {
  "Enabled": true,
  "Host": "smtp.example.com",
  "Port": 587,
  "Security": "StartTls",          // None | StartTls | SslOnConnect
  "Username": "mailer",
  "Password": "…",                 // keep secrets out of source control: use an environment variable or secret store
  "FromAddress": "erp@example.com",
  "FromName": "GTEK ERP",
  "BaseUrl": "https://erp.example.com"   // the web app's address, used for the links in the emails
}
```

The API refuses to start with a clear list of problems if email is enabled but incomplete. Optional tuning: `PollSeconds` (20), `MaxAttempts` (5),
`RetryMinutes` (`[1, 5, 30, 120, 720]`), `KeepSentDays` (14) and `KeepFailedDays` (90). Each user can turn emails off from the notification bell, and
Administration → Notifications shows what is waiting or has failed. See ADR 0026. For local testing, point `Host` at any SMTP catcher
(for example `127.0.0.1:1025` with `Security` set to `None`).
