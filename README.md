# GTEK Business ERP

GTEK Business ERP is a .NET 10 modular-monolith ERP platform. The repository
currently contains the **Phase 0 platform foundation**. It is an executable API
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

The current API exposes only representative Phase 0 endpoints:

| Method | Endpoint | Current behavior |
|---|---|---|
| `GET` | `/api/v1/health` | Returns `200` when the API host is running |
| `GET` | `/api/v1/companies` | Returns an empty JSON array; company persistence is not implemented yet |
| `GET` | `/api/v1/finance/accounts` | Returns an empty JSON array; account persistence is not implemented yet |
| `GET` | `/openapi/v1.json` | Returns the generated OpenAPI document |

Examples:

```powershell
Invoke-WebRequest http://localhost:5004/api/v1/health
Invoke-RestMethod http://localhost:5004/api/v1/companies
Invoke-RestMethod http://localhost:5004/api/v1/finance/accounts
Invoke-RestMethod http://localhost:5004/openapi/v1.json
```

There is currently no frontend, login screen, user-management endpoint, or
write endpoint. The API is intended to validate the host, module registration,
database configuration, and initial architecture.

## Domain functionality available in Phase 0

Although the API is intentionally small, the solution includes representative
domain foundations:

- Platform: `Company`, `User`, `Role`, and `Permission`
- Finance: accounts, currencies, accounting periods, journals, and immutable
  general-ledger entries
- Sales: customers and controlled sales-order lifecycle transitions
- Purchasing: vendors and controlled purchase-order lifecycle transitions
- Inventory: items, locations, units of measure, and immutable item-ledger
  entries
- Shared kernel: aggregate roots, domain events, domain exceptions, company
  identifiers, clock abstraction, and company-context abstraction

The domain tests currently verify balanced journals, rejected unbalanced
journals, controlled sales-order transitions, immutable posted ledger records,
and persistence schema ownership.

## PostgreSQL options

### Use local PostgreSQL

This is the recommended setup for the current development environment:

```powershell
psql -U postgres -h localhost -c "CREATE DATABASE gtek_erp;"
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj
```

The Phase 0 contexts are registered for PostgreSQL, but no application
migrations or full database-backed queries have been implemented yet.

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

## Current limitations

Phase 0 does not yet include:

- A web frontend
- Authentication or authorization
- User/company administration screens
- Database migrations
- Database-backed company or account queries
- Journal-posting API endpoints
- Sales, purchasing, inventory, invoicing, or payment workflows
- Reporting, integrations, or production deployment configuration

Do not treat the representative endpoints as a completed ERP feature set.
