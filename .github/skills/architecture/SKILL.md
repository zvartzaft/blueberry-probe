---
name: architecture
description: Use when adding projects, moving code between layers, or deciding where code belongs in Blueberry Probe (Web, Core, Import, Storage).
---
# Architecture

- `src/ProbeDesk.Web`: Blazor Server UI, DI, Kestrel config, `Services/` (guard middleware, echo endpoints, app state, help, seeding, error text).
- `src/ProbeDesk.Core`: HTTP engine (`Http/`), domain model (`Model/`), variables/extractors/assertions/flow runner (`Flow/`). No internal references, so the engine stays testable and UI-free.
- `src/ProbeDesk.Import`: Postman, .http, cURL importers, exporters, import report. References Core.
- `src/ProbeDesk.Storage`: EF Core/SQLite (`EnsureCreated`), stores, secret protection. References Core.
- Direction: Web -> Core/Import/Storage; Import -> Core; Storage -> Core.
- Collections live in the user profile, never in the repo.
