[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [switch]$Apply
)

$ErrorActionPreference = "Stop"
$baselineMigration = "20261005164810_PlatformBaseline"

function Get-PsqlArguments {
    $arguments = @()
    foreach ($part in ($ConnectionString -split ';')) {
        if ([string]::IsNullOrWhiteSpace($part)) {
            continue
        }
        $key, $value = $part.Split('=', 2)
        switch ($key.Trim().ToLowerInvariant()) {
            "host" { $arguments += @("--host", $value.Trim()) }
            "port" { $arguments += @("--port", $value.Trim()) }
            "database" { $arguments += @("--dbname", $value.Trim()) }
            "username" { $arguments += @("--username", $value.Trim()) }
            "password" { $env:PGPASSWORD = $value.Trim() }
            default { throw "Unsupported connection-string option '$key'." }
        }
    }
    return $arguments
}

$psqlArguments = Get-PsqlArguments

function Invoke-Scalar([string]$Sql) {
    $result = & psql @psqlArguments --tuples-only --no-align --command $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL validation failed."
    }
    return ($result | Out-String).Trim()
}

function Assert-Scalar([string]$Description, [string]$Sql, [string]$Expected = "1") {
    $actual = Invoke-Scalar $Sql
    if ($actual -ne $Expected) {
        throw "Schema equivalence check failed: $Description. Expected $Expected, got '$actual'."
    }
}

Write-Host "Validating the SQL-bootstrap schema before EF baseline adoption..."

Assert-Scalar "platform schema exists" "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'platform';"

$tables = @(
    "Companies", "Users", "UserCredentials", "Roles", "Permissions",
    "UserRoles", "RolePermissions", "UserCompanyAccess", "AuditEntries",
    "NumberSequences"
)

foreach ($table in $tables) {
    Assert-Scalar "platform.$table exists" "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'platform' AND table_name = '$table';"
}

Assert-Scalar "number-sequence company/code uniqueness" "SELECT count(*) FROM pg_indexes WHERE schemaname = 'platform' AND indexname = 'IX_NumberSequences_CompanyId_Code';"
Assert-Scalar "number-sequence company foreign key" "SELECT count(*) FROM information_schema.table_constraints WHERE constraint_schema = 'platform' AND table_name = 'NumberSequences' AND constraint_name = 'FK_NumberSequences_Companies_CompanyId';"

$columns = @{
    "NumberSequences.NextValue" = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'NumberSequences' AND column_name = 'NextValue' AND data_type = 'bigint' AND is_nullable = 'NO';"
    "NumberSequences.Padding" = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'NumberSequences' AND column_name = 'Padding' AND data_type = 'integer' AND is_nullable = 'NO';"
    "NumberSequences.Increment" = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'NumberSequences' AND column_name = 'Increment' AND data_type = 'bigint' AND is_nullable = 'NO';"
    "AuditEntries.MetadataJson" = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'AuditEntries' AND column_name = 'MetadataJson';"
}

foreach ($entry in $columns.GetEnumerator()) {
    Assert-Scalar $entry.Key $entry.Value
}

$historyCount = Invoke-Scalar "SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname IN ('public', 'platform') AND lower(tablename) = lower('__EFMigrationsHistory');"
if ($historyCount -ne "0") {
    throw "The database already has EF migration history. Refusing to adopt it as an unverified SQL-bootstrap baseline."
}

if (-not $Apply) {
    Write-Host "Schema checks passed. No changes were made. Re-run with -Apply to record the verified EF baseline."
    exit 0
}

if ($PSCmdlet.ShouldProcess("platform.__EFMigrationsHistory", "Record verified Platform baseline")) {
    $createHistorySql = 'CREATE TABLE IF NOT EXISTS platform."__EFMigrationsHistory" ("MigrationId" character varying(150) NOT NULL, "ProductVersion" character varying(32) NOT NULL, CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"));'
    $createHistorySql | & psql @psqlArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create EF migration history."
    }

    $insertHistorySql = "INSERT INTO platform.""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('$baselineMigration', '10.0.0');"
    $insertHistorySql | & psql @psqlArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Could not record the verified EF baseline."
    }
    Write-Host "Verified baseline recorded: $baselineMigration"
}
