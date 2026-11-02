# ADR 0007: Local authentication and user identity

## Context

Phase 1.2 needs a secure identity foundation without coupling ERP modules to
HTTP, JWT, or password storage. Roles, permissions, and user-company access
are intentionally deferred.

## Decision

- Platform owns the `User` aggregate and its lifecycle.
- Password hashes are stored separately in `platform."UserCredentials"` and
  are produced and verified by ASP.NET Core's `PasswordHasher`.
- The API uses standard JWT bearer authentication with issuer, audience,
  lifetime, and signature validation.
- ERP code consumes `ICurrentUser`; the API provides an HTTP adapter.
- Login identifies the user; `X-Company-Id` independently selects the active
  company.
- The first user may be created once with an operator-provided
  `Authentication__BootstrapSecret`. That user receives the temporary
  `bootstrap_admin` claim. Until Phase 1.3, that claim is the narrow
  administrative boundary for user and company management.

## Consequences

JWTs last one hour. Deactivating a user prevents future logins, but already
issued tokens remain valid until expiry; a revocation store is deferred.
The development signing key is generated per process when no key is configured,
so operators must configure a persistent base64 key for non-development use.
No user-company access rules are enforced until Phase 1.4.

## Alternatives considered

- ASP.NET Core Identity was not adopted as the aggregate model because the
  ERP needs an explicit Platform user boundary.
- External identity providers and Microsoft Entra ID remain future adapters.
- Cookies, custom token formats, and custom password cryptography were
  rejected for this API.
