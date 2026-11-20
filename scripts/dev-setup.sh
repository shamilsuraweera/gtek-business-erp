#!/usr/bin/env bash
# One-time local setup. Run the SQL part as a Postgres superuser, e.g.:
#   sudo -u postgres psql -f scripts/dev-db.sql
set -euo pipefail
cd "$(dirname "$0")/.."
CONN="${ERP_DATABASE_CONNECTION:-Host=localhost;Port=5432;Database=gtek_erp;Username=gtek;Password=gtek_dev}"
export ERP_DATABASE_CONNECTION="$CONN"
dotnet tool restore >/dev/null 2>&1 || true
dotnet ef database update \
  --project src/Modules/Platform/Erp.Modules.Platform.Infrastructure \
  --startup-project src/Host/Erp.Api
