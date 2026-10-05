# GTEK Business ERP User and Developer Guide

## 1. What this application is

The current release is **Phase 1.5** of the GTEK Business ERP. It provides the
technical foundation for a modular monolith:

- A .NET 10 ASP.NET Core API host
- PostgreSQL connection, platform persistence, and the initial platform schema
- Platform, Finance, Sales, Purchasing, and Inventory module boundaries
- Initial domain rules and immutable ledger concepts
- OpenAPI metadata
- Health checks, problem-details responses, and structured logging
- Unit, integration, architecture, and functional tests

It is not yet a complete end-user ERP. There is no browser UI; authentication
is provided through the documented API login endpoint, and current read endpoints intentionally return representative empty
collections. Phase 1.5 now includes database-backed company management, local
authentication, policy-based permissions, and explicit user-company access.
It also records intentional security and administrative audit history.

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

Create a company, then use the returned ID for lifecycle operations:

```powershell
$company = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5004/api/v1/companies `
  -ContentType "application/json" `
  -Body '{"code":"DEMO","name":"Demo Company"}'
$company
Invoke-RestMethod http://localhost:5004/api/v1/companies
Invoke-RestMethod "http://localhost:5004/api/v1/companies/$($company.id)"
```

Company codes are trimmed and normalized to uppercase. New companies are
active. Rename, activate, and deactivate operations are available at
`/api/v1/companies/{id}/name`, `/activate`, and `/deactivate`.

Company-management endpoints are system endpoints and do not require a header.
Company-scoped endpoints require `X-Company-Id`:

```powershell
Invoke-RestMethod http://localhost:5004/api/v1/finance/accounts `
  -Headers @{ "X-Company-Id" = $company.id }
```

Missing or malformed headers return `400`, an unknown company returns `404`,
an inactive company returns `409`, and a user without active company access
returns `403`.

### Finance accounts

```powershell
Invoke-RestMethod http://localhost:5004/api/v1/finance/accounts
```

This is currently a representative company-scoped endpoint and returns an empty
array. Finance account management is planned for a later phase.

### OpenAPI

Open the generated API description in a browser:

```text
http://localhost:5004/openapi/v1.json
```

This is the machine-readable API document. A Swagger UI is not included in the
current host configuration.

### Audit history

Phase 1.5 records intentional administrative and security actions in the
append-only Platform audit table. It covers company, user, role,
role-permission, user-role, user-company-access, and successful or failed
authentication actions. Audit history is separate from operational logs and
financial ledgers.

Users with `platform.audit.read` can query the system-scoped endpoint:

```powershell
Invoke-RestMethod "http://localhost:5004/api/v1/audit?page=1&pageSize=50" `
  -Headers @{ Authorization = "Bearer $token" }
```

Supported filters are `from`, `to`, `actorUserId`, `companyId`, `category`,
`action`, `entityType`, `entityId`, and `outcome`. Results are newest first,
default to 50 entries, and accept at most 200 entries per page. The endpoint
does not require `X-Company-Id`; `companyId` is an explicit filter for
platform auditors.

Metadata is deliberately limited to safe identifiers and before/after values.
Passwords, hashes, JWTs, signing keys, connection strings, tokens, and full
request bodies are never stored. Audit entries have no update or delete use
case, and no automatic retention purge is enabled.

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
- `Company` and database-backed `User` aggregate
- JWT authentication and HTTP-independent `ICurrentUser`

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

## 8. Authentication and first-user bootstrap

Set the bootstrap secret and, outside Development, a persistent base64 JWT
signing key as environment variables:

```powershell
$env:Authentication__BootstrapSecret = "operator-supplied-bootstrap-secret"
$env:Authentication__Jwt__SigningKey = "<base64-encoded-32-byte-key>"
```

Create the first user once:

```powershell
$body = '{"userName":"admin","email":"admin@example.com","password":"use-a-long-unique-password"}'
curl.exe -X POST http://localhost:5004/api/v1/users `
  -H "Content-Type: application/json" `
  -H "X-Bootstrap-Secret: $env:Authentication__BootstrapSecret" `
  -d $body
```

Login is anonymous and returns a one-hour JWT:

```powershell
$login = curl.exe -s -X POST http://localhost:5004/api/v1/auth/login `
  -H "Content-Type: application/json" `
  -d '{"userName":"admin","password":"use-a-long-unique-password"}' | ConvertFrom-Json
curl.exe http://localhost:5004/api/v1/auth/me `
  -H "Authorization: Bearer $($login.accessToken)"
```

Invalid credentials and inactive users return the same unauthorized result.
Password hashes and passwords are never returned. Deactivation blocks future
logins; already-issued tokens remain valid until their one-hour expiry.

User-management and company-management endpoints require the appropriate
server-side permission.
Authentication and active-company selection remain separate: authenticated
company-scoped requests still require `X-Company-Id`.

## 9. What is not implemented yet

The following are intentionally deferred to later phases:

- Frontend screens
- Generic CRUD architecture and broad database-backed read models
- Finance account persistence and database-backed account queries
- Company-specific roles or permissions
- Financial posting application services and APIs
- Sales quotes, shipments, invoices, and customer payments
- Purchase receipts, vendor invoices, and vendor payments
- Inventory reservations, costing, warehouse execution, and stock balances
- Fixed assets, projects, manufacturing, service, CRM, HR, and reporting

The implementation roadmap and acceptance criteria are documented in
[ERP-IMPLEMENTATION-SPECIFICATION.md](../ERP-IMPLEMENTATION-SPECIFICATION.md).

## 10. Common problems

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

HTTPS is optional for local Phase 1.4 development.
## Roles and permissions

Users receive global roles, and roles receive explicit permissions. Permission
codes follow `module.resource.action`, for example
`finance.accounts.read`. Use the role and permission endpoints to inspect and
manage assignments:

- `GET /api/v1/permissions`
- `GET|POST /api/v1/roles`
- `GET /api/v1/roles/{id}/permissions`
- `POST|DELETE /api/v1/roles/{id}/permissions/{permissionCode}`
- `GET /api/v1/users/{userId}/roles`
- `POST|DELETE /api/v1/users/{userId}/roles/{roleId}`

`SYSTEM_ADMIN` is created with explicit rows for the current permission
catalogue and is assigned to the first bootstrap administrator. The JWT
contains identity, not permissions; changing a role or permission takes effect
without issuing a new token. Inactive users and roles do not authorize access.

Authorization is separate from company selection. Global roles grant
capability, while `UserCompanyAccess` grants company scope. `SYSTEM_ADMIN`
has no implicit access to every company; access must be explicitly granted.
`GET /api/v1/auth/me/companies` does not require `X-Company-Id`, while
company-scoped operations require both the header and active access.

Grant and revoke access with the system-scoped administrative endpoints:

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5004/api/v1/users/$userId/companies/$companyId" `
  -Headers @{ Authorization = "Bearer $token" }

Invoke-RestMethod -Method Delete `
  -Uri "http://localhost:5004/api/v1/users/$userId/companies/$companyId" `
  -Headers @{ Authorization = "Bearer $token" }
```

The permission catalogue includes `platform.company-access.read` and
`platform.company-access.manage`. Permission and company-access changes are
resolved server-side and therefore affect an already-issued JWT immediately.
