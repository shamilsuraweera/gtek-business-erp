#!/usr/bin/env bash
# Local development setup: applies every module's EF migrations and (optionally) seeds demo data.
#
#   scripts/dev-setup.sh           # migrate only
#   scripts/dev-setup.sh --seed    # migrate + idempotent demo data
#
# Prerequisite (once): create the role/database, e.g.  sudo -u postgres psql -f scripts/dev-db.sql
# Override the target with ERP_DATABASE_CONNECTION.
set -euo pipefail
cd "$(dirname "$0")/.."

CONN="${ERP_DATABASE_CONNECTION:-Host=localhost;Port=5432;Database=gtek_erp;Username=gtek;Password=gtek_dev}"
export ERP_DATABASE_CONNECTION="$CONN"

for module in Platform Finance Inventory Sales; do
  echo "==> Migrating $module"
  dotnet ef database update \
    --project "src/Modules/$module/Erp.Modules.$module.Infrastructure" \
    --startup-project src/Host/Erp.Api >/dev/null
done

if [[ "${1:-}" == "--seed" ]]; then
  echo "==> Seeding demo data"
  ConnectionStrings__Erp="$CONN" ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project src/Host/Erp.Api --no-launch-profile -- --seed 2>&1 | grep -E "Seed complete|Unhandled|Exception" || true
fi
echo "Done. Start the API with: dotnet run --project src/Host/Erp.Api"
