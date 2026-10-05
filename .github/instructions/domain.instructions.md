---
applyTo: "src/**/Domain/**/*.cs"
---

Keep domain invariants and lifecycle transitions inside aggregates or domain
services. Do not reference EF Core, ASP.NET Core, or infrastructure projects.
Use decimal for monetary values and immutable records for posted history.
