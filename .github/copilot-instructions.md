# Repository instructions

- This is a modular-monolith ERP.
- Do not create microservices unless an ADR explicitly approves it.
- Modules own their persistence.
- No cross-module Infrastructure references.
- Financial posting must be transactional.
- Posted ledger entries are immutable.
- Corrections use reversal/correction transactions.
- Never use `float` or `double` for money.
- Never hard-code `CompanyId`.
- Preserve company isolation.
- Domain rules stay out of controllers and API endpoints.
- Avoid generic repositories over EF Core.
- Avoid speculative abstractions.
- Prefer explicit ERP business terminology.
- Every feature requires tests.
- Update ADRs when architectural decisions change.
- `dotnet build` and `dotnet test` must pass before completing tasks.
