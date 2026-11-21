# ADR 0038: Secrets as files; dependency audit

## Status

Accepted

## Context

The SMTP password (and the JWT signing key) lived in configuration or environment variables, both easy to leak through logs, process listings
and checked-in files. Container platforms mount secrets as files instead.

## Decision

- The API adds a **key-per-file configuration source** last, so it overrides every other source: a file named like the setting, with `__` for
  `:`, whose content is the value (`/run/secrets/Email__Password`). The folder defaults to `/run/secrets` and is moved with `ERP_SECRETS_DIR`;
  it is optional. A trailing newline is ignored. It is part of the ASP.NET shared framework, so there is no extra dependency.
- The smoke test supplies the sender's display name this way, through the real start-up path, and checks that it reached the emails.
- **Web dependency audit.** `npm audit` reported high-severity findings in `react-router-dom` (a production dependency; several concern server
  rendering this SPA does not use, but open-redirect ones apply to client routing) and in the Vite dev server. Both were moved to patched
  versions (7.18.6 and 7.3.8, pinned exactly like the other dependencies); the audit is now clean.

## Consequences

- Production deployments should put the SMTP password, the JWT signing key and the database password in secret files or a secret store, not in
  appsettings.
- Rotating a secret still needs a restart: configuration is read at start-up.
- The audit is a point-in-time check; there is no automated dependency update or audit step in CI yet (a possible next step).
