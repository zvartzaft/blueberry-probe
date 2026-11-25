# 0009: Product name "Blueberry Probe"

Status: accepted.

## Decision
The user-facing name is **Blueberry Probe** (UI, page titles, help, exports, console output, docs).

Technical identifiers keep the `ProbeDesk` name on purpose: solution/project/namespace names, `ProbeDeskDbContext`, the data folder `%LOCALAPPDATA%/ProbeDesk`, `probedesk.db`, the `ProbeDesk:*` configuration keys and the DPAPI entropy string `ProbeDesk.v1`. Renaming the data folder, database or entropy would orphan existing data and make stored secrets and session cookies undecryptable.

## Consequences
Export file names are now `blueberry-probe.http` and `blueberry-probe.postman_collection.json`. A full technical rename needs a data migration and is a separate decision.
