# GTEK Business ERP

GTEK Business ERP is a .NET 10 modular-monolith ERP platform. The repository
currently contains the **Phase 1.6 platform foundation**. It is an executable API
and domain foundation, not yet a complete ERP application with a user interface
or full business workflows.

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

The current API exposes the Phase 1.5 platform endpoints and representative
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
| `GET` | `/openapi/v1.json` | Returns the generated OpenAPI document |

Examples:

```powershell
Invoke-WebRequest http://localhost:5004/api/v1/health
Invoke-RestMethod http://localhost:5004/api/v1/companies
Invoke-RestMethod http://localhost:5004/api/v1/finance/accounts -Headers @{ "X-Company-Id" = "<company-id>" }
Invoke-RestMethod http://localhost:5004/openapi/v1.json
```

There is currently no frontend, finance transaction workflow, or full ERP
business workflow.

## Domain functionality available in Phase 1.5

Although the API is intentionally small, the solution includes the Phase 1.5
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

The SQL script is idempotent for the initial platform schema. Keep the password
out of committed files; the environment variable is only a local example.
It also creates the Phase 1.5 `AuditEntries` table, indexes, and
`platform.audit.read` permission. The equivalent incremental EF migration is
`20261005160402_Phase15AuditTrail`.

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

Phase 1.5 includes local JWT authentication, database-backed user management,
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

Phase 1.5 does not yet include:

- A web frontend
- Company-specific roles or permissions
- Database-backed finance account queries
- Journal-posting API endpoints
- Sales, purchasing, inventory, invoicing, or payment workflows
- Reporting, integrations, or production deployment configuration

Do not treat the representative endpoints as a completed ERP feature set.
### Roles and permissions

Phase 1.5 uses the server-side permission model and explicit company scope.
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
