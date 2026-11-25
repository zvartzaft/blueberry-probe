---
name: security-rules
description: Use for any change touching networking, secrets, cookies, logging, history, storage, TLS, hosts/origins, exports or production-environment behavior in Blueberry Probe.
---
# Security rules (non-negotiable)

1. Bind to 127.0.0.1 only (`ListenLocalhost`); AllowedHosts = localhost. `LocalGuardMiddleware` validates Host/Origin (`docs/decisions/0005-local-guard.md`). Never weaken it.
2. Secrets: write-only in UI (`Environments.razor`), resolved server-side at send time, protected with `ISecretProtector` (DPAPI, `docs/decisions/0004-storage.md`; Windows only in v1).
3. Never log/persist/export secrets, `Authorization`, `Cookie`, `Set-Cookie`. Only exception: the pasted session cookies (`.AspNetCore.Cookies`, `.AspNetCore.Antiforgery.*`) stored encrypted per environment, write-only in the UI (`docs/decisions/0007-session-cookies.md`).
4. Response bodies not stored by default; history is metadata only.
5. TLS validation on; ignoring is explicit per environment, warned and logged.
6. Production environments: prominent indicator and confirmation before POST/PUT/PATCH/DELETE.
7. Do not block private/loopback targets; optional per-environment allowed-hosts.
8. Uploaded files: app-data folder, size limits, cleanup.
9. No detailed exceptions in the UI.
10. Never use real customer data in docs/tests/fixtures.

Reason: the tool touches many customers' systems, so a leak or misfire is costly.
