---
applyTo: "src/**/Infrastructure/**/*.cs"
---

Infrastructure owns EF Core mappings, database access, and external adapters
for its module. Use Fluent API mappings and never update another module's tables
directly.
