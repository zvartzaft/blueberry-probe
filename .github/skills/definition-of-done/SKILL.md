---
name: definition-of-done
description: Use before finishing any change in Blueberry Probe to check the completion checklist.
---
# Definition of done

- `dotnet build ProbeDesk.slnx` and `dotnet test ProbeDesk.slnx` pass; tests added/updated.
- Security rules (see security-rules skill) still hold; no secrets or real customer data anywhere.
- In-app help updated if user-visible behavior changed.
- Affected skills and `AGENTS.md` updated in the same change.
- Decision recorded in `docs/decisions/` if significant.
- UI changes follow ui-design-system.
