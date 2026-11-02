# ADR 0012: EF baseline and migration authority

## Context

The original Platform EF migration IDs do not respect their dependencies.
`20261005154728_CreateUserCompanyAccessPhase14` creates foreign keys to
`Companies` and `Users`, although those tables are created by later migrations.
The repository also has SQL-bootstrap databases with no
`__EFMigrationsHistory`.

Historical migration files are retained under
`Persistence/LegacyMigrations` for reference and are no longer compiled as the
active migration set. They are not renamed, deleted, or reordered.

## Decision

EF Core migrations are authoritative for new databases and future schema
evolution. The active migration set starts with the generated
`20261005164810_PlatformBaseline` migration, which creates the complete current
Platform schema, including NumberSequences, in dependency-safe order.

The SQL script under `scripts/database/apply-platform-migration.sql` is a
legacy bootstrap helper for databases that predate the EF baseline. It is not a
second long-term schema authority.

## New databases

Create an empty PostgreSQL database and run:

```powershell
dotnet ef database update `
  --project .\src\Modules\Platform\Erp.Modules.Platform.Infrastructure\Erp.Modules.Platform.Infrastructure.csproj `
  --startup-project .\src\Host\Erp.Api\Erp.Api.csproj
```

This creates the schema and records the baseline in
`__EFMigrationsHistory`. Re-running the command is a no-op.

## Existing SQL-bootstrap databases

Operators must take a backup, run the bootstrap script if necessary, and run
`scripts/database/adopt-platform-bootstrap.ps1` without `-Apply`. The script
checks the schema, required columns, foreign key, unique index, and rejects any
database with existing EF history. Only after reviewing successful output may
the operator rerun it with `-Apply`. The script then records only the verified
baseline migration; it never runs during API startup and never drops data.

An incompatible schema fails validation and is not baselined. The operator must
repair it with a reviewed forward migration or restore from backup.

## Existing EF-managed databases

Databases already using the legacy migration chain are not automatically
converted. They require a reviewed backup, schema comparison, and an explicit
forward transition plan before adopting the new baseline. Historical migration
rows are never fabricated or silently removed.

## Future migrations and testing

Future changes must be generated with EF tooling from the active model and
validated against a clean PostgreSQL database. The model snapshot must be
committed with every migration. Migration tests must cover clean creation,
repeat update, legacy adoption, mismatch rejection, and representative data
preservation.

## Consequences

Fresh deployments have one coherent EF-controlled history. Legacy SQL-bootstrap
databases have an explicit operator-controlled adoption path with schema
verification. The historical migrations remain available for audit but cannot
accidentally break fresh deployments.
