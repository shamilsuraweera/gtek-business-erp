#!/usr/bin/env bash
# Smoke test of the live API: builds a throwaway database, seeds it, starts the API configured for email and reminders, runs
# scripts/smoke/smoke.py against it, and cleans up.
#
#   scripts/smoke/run.sh               # everything
#   scripts/smoke/run.sh reminders     # only sections whose title contains the word(s)
#
# After the API checks it also runs the browser flows (web/e2e, Playwright) against the same API through the Vite dev server.
# Needs: .NET SDK, the dotnet-ef tool, psql, python3, Node 20+ with web/ dependencies installed, and a PostgreSQL server you can create databases on.
# Environment (all optional):
#   SMOKE_PG_ADMIN   connection URL used to create/drop the database  (default postgresql://gtek:gtek_dev@localhost:5432/postgres)
#   SMOKE_DB         database name                                   (default gtek_erp_smoke; dropped first and again at the end)
#   SMOKE_PORT       API port (default 5004)      SMOKE_SMTP_PORT  SMTP sink port (default 2525)
#   SMOKE_KEEP=1     keep the database and the API log afterwards
#   SMOKE_NO_BUILD=1 skip the build (CI builds first)
#   SMOKE_BROWSER=0  skip the browser flows (they need `npm ci` in web/ and a Playwright browser: `npx playwright install chromium`,
#                    or E2E_CHROMIUM=/path/to/chrome); SMOKE_WEB_PORT is the dev server port (default 5173)
set -euo pipefail
cd "$(dirname "$0")/../.."

PG_ADMIN="${SMOKE_PG_ADMIN:-postgresql://gtek:gtek_dev@localhost:5432/postgres}"
DB="${SMOKE_DB:-gtek_erp_smoke}"
PORT="${SMOKE_PORT:-5004}"
SMTP_PORT="${SMOKE_SMTP_PORT:-2525}"
WEB_PORT="${SMOKE_WEB_PORT:-5173}"
BROWSER="${SMOKE_BROWSER:-1}"
# Same user, password, host and port as the admin URL, other database.
DB_URL="${PG_ADMIN%/*}/$DB"
CONN="$(python3 - "$DB_URL" "$DB" <<'PY'
import sys, urllib.parse
u = urllib.parse.urlparse(sys.argv[1])
print(f"Host={u.hostname};Port={u.port or 5432};Database={sys.argv[2]};Username={urllib.parse.unquote(u.username or '')};Password={urllib.parse.unquote(u.password or '')}")
PY
)"
LOG="${TMPDIR:-/tmp}/gtek-smoke-api.log"
API_PID=""
WEB_PID=""

cleanup() {
  [[ -n "${SMOKE_PID:-}" ]] && kill "$SMOKE_PID" 2>/dev/null || true
  [[ -n "$WEB_PID" ]] && kill "$WEB_PID" 2>/dev/null || true
  [[ -n "$API_PID" ]] && kill "$API_PID" 2>/dev/null || true
  [[ -n "$API_PID" ]] && wait "$API_PID" 2>/dev/null || true
  if [[ -z "${SMOKE_KEEP:-}" ]]; then psql "$PG_ADMIN" -qc "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE)" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT

if ss -ltn 2>/dev/null | grep -q ":$PORT "; then echo "Port $PORT is already in use; stop that API or set SMOKE_PORT." >&2; exit 2; fi
if [[ "$BROWSER" != "0" ]]; then
  if ss -ltn 2>/dev/null | grep -q ":$WEB_PORT "; then echo "Port $WEB_PORT is already in use; stop that dev server or set SMOKE_WEB_PORT." >&2; exit 2; fi
  [[ -x web/node_modules/.bin/playwright ]] || { echo "The browser flows need the web dependencies: (cd web && npm ci && npx playwright install chromium). Or set SMOKE_BROWSER=0." >&2; exit 2; }
fi

echo "==> Fresh database $DB"
psql "$PG_ADMIN" -qc "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE)" -c "CREATE DATABASE \"$DB\"" >/dev/null

if [[ -z "${SMOKE_NO_BUILD:-}" ]]; then
  echo "==> Build"
  dotnet build src/Host/Erp.Api -v q --nologo >/dev/null
fi

echo "==> Migrate and seed"
ERP_DATABASE_CONNECTION="$CONN" bash scripts/dev-setup.sh --seed | tail -n 3

# The test runner goes first: it hosts the SMTP sink, which must be listening before the API starts sending email, and it waits for the API itself.
echo "==> Start the smoke test runner (SMTP sink on :$SMTP_PORT)"
SMOKE_API="http://127.0.0.1:$PORT" SMOKE_SMTP_PORT="$SMTP_PORT" SMOKE_WAIT_SECONDS=120 python3 scripts/smoke/smoke.py "$@" &
SMOKE_PID=$!
sleep 1

echo "==> Start the API on :$PORT (log: $LOG)"
env ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Erp="$CONN" \
    Authentication__BootstrapSecret="smoke-bootstrap-secret" \
    Authentication__Jwt__SigningKey="c21va2Utc2lnbmluZy1rZXktMzItYnl0ZXMtbG9uZy0hISE=" \
    Email__Enabled=true Email__Host=127.0.0.1 Email__Port="$SMTP_PORT" Email__Security=None \
    Email__FromAddress=erp@gtek.local Email__BaseUrl=http://localhost:5173 Email__PollSeconds=2 \
    Reminders__InitialDelaySeconds=1 Reminders__HourUtc=0 \
    dotnet run --project src/Host/Erp.Api --no-build --no-launch-profile --urls "http://127.0.0.1:$PORT" >"$LOG" 2>&1 &
API_PID=$!

# Wait for the runner; if the API dies first, stop waiting and say why.
status=0
while kill -0 "$SMOKE_PID" 2>/dev/null; do
  if ! kill -0 "$API_PID" 2>/dev/null; then
    echo "The API exited while the smoke test was running:" >&2; kill "$SMOKE_PID" 2>/dev/null || true; status=1; break
  fi
  sleep 1
done
if [[ $status -eq 0 ]]; then wait "$SMOKE_PID" || status=$?; fi

# The browser flows run against the same API (and database) through the Vite dev server, which proxies /api to it.
if [[ "$BROWSER" != "0" ]] && kill -0 "$API_PID" 2>/dev/null; then
  echo
  echo "==> Browser flows (Playwright) via the dev server on :$WEB_PORT"
  (cd web && VITE_API_TARGET="http://127.0.0.1:$PORT" VITE_PORT="$WEB_PORT" exec ./node_modules/.bin/vite --host 127.0.0.1 --strictPort) >"${TMPDIR:-/tmp}/gtek-smoke-web.log" 2>&1 &
  WEB_PID=$!
  for _ in $(seq 1 40); do
    curl -sf -o /dev/null "http://127.0.0.1:$WEB_PORT/api/v1/health" && break
    kill -0 "$WEB_PID" 2>/dev/null || break
    sleep 1
  done
  if curl -sf -o /dev/null "http://127.0.0.1:$WEB_PORT/api/v1/health"; then
    (cd web && E2E_BASE_URL="http://127.0.0.1:$WEB_PORT" npx playwright test) || status=$?
  else
    echo "The dev server did not come up:" >&2; tail -n 20 "${TMPDIR:-/tmp}/gtek-smoke-web.log" >&2; status=1
  fi
fi

if [[ $status -ne 0 ]]; then echo "--- API log (last 40 lines) ---" >&2; tail -n 40 "$LOG" >&2; fi
exit $status
