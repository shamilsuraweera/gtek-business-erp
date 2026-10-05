---
applyTo: "src/**/Application/**/*.cs"
---

Application code orchestrates use cases and authorization through explicit
interfaces. It may depend on domain and contracts, but not infrastructure
implementations. Keep transaction boundaries visible.
