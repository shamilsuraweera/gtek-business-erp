# ADR 0033: Smoke test of the live API, and CI

## Status

Accepted

## Context

The unit, architecture and functional tests (the latter on an in-memory database) and the PostgreSQL integration tests do not exercise what
most of the recent features are made of: HTTP, several hosted workers, real SQL, and email that actually leaves the process. Those were
checked by hand-run scripts that were ordered, partly stale, and not in the repository. The repository also had no CI at all.

## Decision

- `scripts/smoke/smoke.py` is a **dependency-free Python** script (standard library only) that drives the running API over HTTP and hosts a
  small **SMTP sink** in-process, so it asserts on the emails the API really sends. It is a set of independent sections; a section that raises
  is reported as a failure and the others still run; the exit code is non-zero if any check failed. Sections can be selected by title word.
- `scripts/smoke/run.sh` builds the environment: a **fresh database** (dropped first and last), migrate and seed with `scripts/dev-setup.sh --seed`,
  then start the runner (so the SMTP sink is listening) **before** the API (so the e-mails the reminders send at start-up never meet a closed port),
  then the API with email and reminders configured through environment variables. It stops the API and drops the database on exit.
- The smoke test depends on the seeder's demo data (admin and clerk users, the demo company, overdue and low-stock items, ended open periods)
  rather than creating all of its own, and on a fresh database for the once-per-day reminders. Everything else it creates uses unique names, and
  it creates an open accounting period for today if the seeded periods have run out.
- `.github/workflows/ci.yml` runs three jobs on push and pull request: **build and test** (all test projects, with a PostgreSQL service migrated for
  the integration tests), **web** (`npm ci`, type-check and build), and **smoke** (the script above against a PostgreSQL service).
- **Browser flows** (added after the first version): `web/e2e` holds ten Playwright tests (`@playwright/test`, pinned, one worker, in file order).
  `run.sh` starts the Vite dev server against the smoke API (`VITE_API_TARGET`, `VITE_PORT`) once the API checks have run, then `npx playwright test`; both
  results count towards the exit code. They use the seeded data and create their own purchase orders (unique names) through the API, so they can be
  re-run on the same database. A fixture fails every test on an uncaught page error or a 5xx from the API. The CI job installs Chromium and uploads
  the Playwright report and traces when something fails. `SMOKE_BROWSER=0` skips them.
- It found two real problems when first run, and both were fixed: the email status shown to administrators did not count the company-less
  account notices (ADR 0032 now says so and it does), and the runner had to start before the API.

## Consequences

- A change that breaks a flow across modules, the email path or a scheduled reminder fails CI instead of being found by hand.
  It was checked to fail by deliberately breaking a reminder's recipient permission.
- It takes about two minutes and needs `psql`, `dotnet-ef` and Python, which the workflow installs (`psql`, `dotnet-ef`) or the runner provides (Python).
- It is a smoke test: it checks that the main paths work and the numbers add up, not every rule (those are unit tests). The browser flows cover the
  main screens and what is new in the UI, not every page; there is no visual-regression or cross-browser testing (Chromium only).
- The browser tests assert on structure and the flows, not exact seeded counts, and avoid views that other tests change: for example the dashboard's
  Low stock card shows only the six lowest items, so the tests check the same rule on the Items page, where it is deterministic.
- The seeded dates are in 2026, so the checks use "at least"/"is reported" style conditions that stay true as time passes, not exact counts.
- Not covered: load or concurrency beyond the unit and integration tests, upgrades from an older database, and the retention purge
  (covered by the PostgreSQL integration tests), because it waits for a daily schedule.
