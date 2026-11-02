# ADR 0009: User-company access and active company authorization

## Context

Phase 1.3 established authentication and global capability authorization.
Selecting an `X-Company-Id` previously validated only that a company existed
and was active. A valid company identifier is untrusted request input and does
not prove that the authenticated user may operate in that company.

## Decision

Persist explicit `UserCompanyAccess` relationships in the Platform module.
Each relationship is keyed by `(UserId, CompanyId)` and has an active/inactive
lifecycle plus actor and timestamp metadata. Database foreign keys and the
composite primary key prevent orphaned or duplicate relationships.

Company capability and company scope remain separate:

- `RolePermission` answers what a user may do.
- `UserCompanyAccess` answers which companies the user may enter.

The request pipeline checks authentication, company existence and status, and
current server-side company access before setting `CompanyContext`. Permission
authorization remains a separate dynamic policy check. Access and permission
changes therefore take effect immediately for existing JWTs.

`GET /api/v1/auth/me/companies` is system-scoped and lists active companies
available to the authenticated user without requiring `X-Company-Id`.
Administrative access-management endpoints require
`platform.company-access.read` or `platform.company-access.manage` and are also
system-scoped.

`SYSTEM_ADMIN` receives these capabilities through ordinary role-permission
rows. It does not receive implicit access to every company; an explicit
relationship is required for company-scoped operations.

## Alternatives considered

- **Put company IDs in JWTs:** rejected because grants and revocations would
  remain stale until token expiry.
- **Infer access from roles:** rejected because capability and data scope are
  different concerns and would not be auditable.
- **Implicit all-company access for `SYSTEM_ADMIN`:** rejected because it
  creates an undocumented data-scope bypass.
- **A separate service or messaging subsystem:** rejected as premature for
  this modular monolith.

## Consequences

Company-scoped requests perform a current access lookup before establishing
trusted context. Persistence-level company isolation remains required after
context establishment; access checks do not replace query filters or ownership
rules. Access lifecycle operations are structured for future audit observation
without introducing a complete audit subsystem in Phase 1.4.

## Future considerations

Phase 1.5 may observe `CompanyAccessGranted`, `CompanyAccessRevoked`, and
`CompanyAccessRestored` in a dedicated audit capability. Company-specific
roles, delegated administration, and broader record-level security remain out
of scope.
