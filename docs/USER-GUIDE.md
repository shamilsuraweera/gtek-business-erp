# GTEK Business ERP User and Developer Guide

## 1. What this application is

The current release is **Phase 0** of the GTEK Business ERP. It provides the
technical foundation for a modular monolith:

- A .NET 10 ASP.NET Core API host
- PostgreSQL connection and module database-context registration
- Platform, Finance, Sales, Purchasing, and Inventory module boundaries
- Initial domain rules and immutable ledger concepts
- OpenAPI metadata
- Health checks, problem-details responses, and structured logging
- Unit, integration, architecture, and functional tests

It is not yet a complete end-user ERP. There is no browser UI or login flow,
and the current read endpoints intentionally return representative empty
collections until Phase 1 persistence and application use cases are built.

## 2. Start the application with local PostgreSQL

### Prerequisites

Install:

1. .NET SDK 10.0.400
2. PostgreSQL with a server listening on `localhost:5432`
3. `psql` on your PATH, or another PostgreSQL client

### Create the database

Using the local `postgres` administrator:

```powershell
psql -U postgres -h localhost -c "CREATE DATABASE gtek_erp;"
```

If the database already exists, PostgreSQL will report that fact; no data is
deleted by this command.

### Configure the connection

Development settings are loaded from:

```text
src/Host/Erp.Api/appsettings.Development.json
```

Keep the local password in that ignored file or set the standard ASP.NET Core
environment variable:

```powershell
$env:ConnectionStrings__Erp = "Host=localhost;Port=5432;Database=gtek_erp;Username=postgres;Password=<your-password>"
```

The environment-variable value takes precedence over the JSON configuration.
Never commit a production password or credential to the repository.

### Build and run

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj
```

The API uses the Development environment from its launch profile:

```text
HTTP:  http://localhost:5004
HTTPS: https://localhost:7104
```

If you run without the launch profile, specify the URL explicitly:

```powershell
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj --urls http://localhost:5004
```

Stop the process with `Ctrl+C`.

## 3. Verify the running service

Open another PowerShell terminal and call the health endpoint:

```powershell
$response = Invoke-WebRequest http://localhost:5004/api/v1/health
$response.StatusCode
$response.Content
```

Expected result:

```text
200
Healthy
```

If the API is not running, PowerShell reports that the connection was refused.
Check the terminal running `dotnet run` for startup errors.

## 4. Use the current endpoints

### Health

```powershell
Invoke-WebRequest http://localhost:5004/api/v1/health
```

This checks that the web host is accepting requests. It is not yet a complete
database-readiness check.

### Companies

```powershell
Invoke-RestMethod http://localhost:5004/api/v1/companies
```

Current response:

```json
[]
```

The endpoint is a placeholder for the Phase 1 company-management use case.

### Finance accounts

```powershell
Invoke-RestMethod http://localhost:5004/api/v1/finance/accounts
```

Current response:

```json
[]
```

The endpoint is a placeholder for the Phase 2 account-management use case.

### OpenAPI

Open the generated API description in a browser:

```text
http://localhost:5004/openapi/v1.json
```

This is the machine-readable API document. A Swagger UI is not included in the
current host configuration.

## 5. Optional Docker database

Local PostgreSQL is the recommended setup for this workspace. Docker remains
available for another developer or CI-like local environment:

```powershell
docker compose up -d postgres
docker compose ps
docker compose logs -f postgres
```

The Docker container uses the values defined in
[docker-compose.yml](../docker-compose.yml), not the local `postgres` account
configuration. Do not start it on port `5432` while another PostgreSQL server
is already using that port unless you change the port mapping.

Stop the optional container with:

```powershell
docker compose down
```

## 6. Run validation

Run the complete validation before submitting a change:

```powershell
dotnet restore
dotnet build
dotnet test
```

Target a specific test project when iterating:

```powershell
dotnet test .\tests\Erp.UnitTests\Erp.UnitTests.csproj
dotnet test .\tests\Erp.IntegrationTests\Erp.IntegrationTests.csproj
dotnet test .\tests\Erp.ArchitectureTests\Erp.ArchitectureTests.csproj
dotnet test .\tests\Erp.FunctionalTests\Erp.FunctionalTests.csproj
```

## 7. What is currently implemented

### Platform foundations

- `CompanyId` and company-scoped aggregate foundations
- `ICompanyContext`
- `IClock` and `SystemClock`
- Representative `Company`, `User`, `Role`, and `Permission` concepts

### Finance foundations

- Accounts and account types
- Currencies and accounting periods
- General journals with debit/credit lines
- Rejection of unbalanced journals
- Immutable `GeneralLedgerEntry` records
- Protection against changing a posted journal

### Sales and purchasing foundations

- Customer and vendor concepts
- Sales-order and purchase-order lines
- Controlled document status transitions
- Validation that draft documents contain valid lines before opening/releasing

### Inventory foundations

- Item and location concepts
- Units of measure
- Immutable item-ledger movement concept

## 8. What is not implemented yet

The following are intentionally deferred to later phases:

- Authentication, login, and user administration
- Frontend screens
- CRUD or database-backed read models
- EF Core migrations
- Financial posting application services and APIs
- Sales quotes, shipments, invoices, and customer payments
- Purchase receipts, vendor invoices, and vendor payments
- Inventory reservations, costing, warehouse execution, and stock balances
- Fixed assets, projects, manufacturing, service, CRM, HR, and reporting

The implementation roadmap and acceptance criteria are documented in
[ERP-IMPLEMENTATION-SPECIFICATION.md](../ERP-IMPLEMENTATION-SPECIFICATION.md).

## 9. Common problems

### PostgreSQL connection refused

Confirm that PostgreSQL is running and listening on port 5432:

```powershell
Test-NetConnection localhost -Port 5432
```

Then verify the connection settings and database name.

### Database does not exist

Create it with:

```powershell
psql -U postgres -h localhost -c "CREATE DATABASE gtek_erp;"
```

### Port 5004 is already in use

Run the API on another port:

```powershell
dotnet run --project .\src\Host\Erp.Api\Erp.Api.csproj --urls http://localhost:5050
```

Use `http://localhost:5050` for the endpoint examples.

### HTTPS development certificate warning

Use the HTTP URL while developing:

```text
http://localhost:5004
```

HTTPS is optional for local Phase 0 development.
