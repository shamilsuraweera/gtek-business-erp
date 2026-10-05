---
applyTo: "src/**/Infrastructure/**/*.cs"
---

Infrastructure owns EF Core mappings, database access, and external adapters
for its module. Use Fluent API mappings and never update another module's tables
directly.

EF Core migrations are the authoritative schema-evolution mechanism. Generate
and commit migrations together with the model snapshot, validate them against a
clean PostgreSQL database, and never manually fabricate migration history.
Legacy SQL-bootstrap databases require explicit schema equivalence validation
before baseline adoption. Do not rewrite, reorder, or delete historical
migrations that may already be deployed.
