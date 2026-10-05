# ADR 0006: Company context and data isolation

**Status:** Accepted

## Context

ERP operations must not read or write another company's data because a caller
forgot to add a company predicate or supplied a forged company identifier.

## Decision

System/platform endpoints are marked with `SystemEndpointMetadata`. Future
company-scoped endpoints are marked with `CompanyScopedEndpointMetadata`.
`CompanyContextMiddleware` handles the latter by reading `X-Company-Id`,
resolving the company through the Platform application service, requiring the
company to exist and be active, and setting the scoped `ICompanyContext`.

Company-owned application commands must use `ICompanyContext` rather than a
company identifier from request payloads. Platform company-management
endpoints are system endpoints and therefore do not require the header.

## Consequences

Endpoint classification is explicit rather than based on URL string matching.
Missing, malformed, unknown, or inactive contexts fail before the handler runs.
The current phase establishes the request boundary; each future module must
apply the context to its own queries, write validation, and persistence
configuration.

Global EF query filters remain a per-module decision because the current Phase
1 slice does not yet contain a persisted company-owned business aggregate.
