# Contributing

Keep changes aligned with the modular-monolith boundaries and the permanent rules in `.github/copilot-instructions.md`. Start with the
[README](README.md), the [user guide](docs/USER-GUIDE.md) and the [decision records](docs/adr/README.md); a change that alters a decision
gets a new record (or an amendment) in `docs/adr`.

Before completing a change, run:

```bash
dotnet build
dotnet test                       # with ERP_TEST_DATABASE_CONNECTION set for the PostgreSQL integration tests (see the README)
scripts/smoke/run.sh              # the live API and browser flows, on a throwaway database (about two and a half minutes)
cd web && npm run build           # type-check and build the web app
```

GitHub Actions runs the same on every push and pull request.

- Add tests for every new domain rule or use case: unit tests for rules, PostgreSQL integration tests for anything that depends on real SQL, locks or
  concurrency, and a smoke or browser check for a user-visible flow.
- Do not edit posted accounting or inventory history; use explicit correction transactions.
- Add a database migration with `dotnet ef migrations add` for the module that owns the change, and never apply migrations from application code (ADR 0012).
- Keep secrets out of source control: use environment variables or secret files (ADR 0038).
