# Contributing

Keep changes aligned with the modular-monolith boundaries and the permanent
rules in `.github/copilot-instructions.md`.

Before completing a change, run:

```powershell
dotnet restore
dotnet build
dotnet test
```

Add tests for every new domain rule or use case. Do not edit posted accounting
or inventory history; use explicit correction transactions.
