# ADR 0008: Role and permission authorisation

## Context

Phase 1.2 authenticated users with a temporary bootstrap-administrator claim.
Future ERP modules need stable, auditable capabilities without coupling their
application code to JWT parsing or Platform infrastructure.

## Decision

Platform owns global `Roles`, `Permissions`, `UserRoles`, and
`RolePermissions` tables. Roles and permissions use explicit relational
assignments and database uniqueness constraints. Role codes are normalized
uppercase identifiers; permission codes are stable lowercase
`module.resource.action` identifiers.

ASP.NET Core uses a dynamic permission policy provider and authorization
handler. JWTs prove identity only. The handler resolves the current user,
active roles, and permissions from Platform persistence on each authorization
check, so deactivation and assignment changes take effect without token
rotation.

The initial catalogue is deliberately small and includes platform
administration permissions plus `finance.accounts.read`. `SYSTEM_ADMIN` is a
system role with explicit rows for every known permission. Existing bootstrap
administrators are associated with that role during the idempotent migration.
The bootstrap secret remains only for first-user installation.

Company context remains separate from authorization. A permission says what a
user may do; `X-Company-Id` selects the active company. User-company access is
deferred to Phase 1.4, so role assignments are currently global.

## Consequences

- Permission changes are immediately effective and are auditable through
  assignment rows.
- Inactive users and inactive roles cannot authorize requests.
- Platform application code depends on abstractions, while EF Core remains in
  Platform infrastructure.
- A future Phase 1.4 model can add company access without changing permission
  codes.

## Alternatives considered

- Storing permissions in JWTs was rejected because changes would be stale until
  token expiry.
- Comma-separated role/permission values were rejected because they are not
  relational, auditable, or database-constrained.
- A hidden `bootstrap_admin` allow-all bypass was rejected as a permanent model.
