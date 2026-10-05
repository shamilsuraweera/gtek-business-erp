# Phase 0 architecture

The solution is a modular monolith. `Erp.Api` is the composition root. Each
active module has Domain, Application, Infrastructure, and Contracts projects.
The domain layer depends only on `Erp.SharedKernel`; application coordinates
use cases; infrastructure owns EF Core and external concerns.

Future modules are represented by roadmap documentation only until their phase
is approved.
