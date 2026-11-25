# Blueberry Probe: agent guide

Local, single-user Blazor Server tool (.NET 10) for test-running HTTP requests against customer systems. Security is priority 1. Read by Copilot CLI and VS Code Copilot (also see `.github/copilot-instructions.md`).

## Commands
- Build: `dotnet build ProbeDesk.slnx`
- Test: `dotnet test ProbeDesk.slnx`
- Run: `dotnet run --project src/ProbeDesk.Web`, then open http://127.0.0.1:5187

## Layout
`src/ProbeDesk.Web` (UI) -> `Core`, `Import`, `Storage`; `Import -> Core`; `Storage -> Core`; `Core` references nothing internal. Details: `.github/skills/architecture/SKILL.md`.

## Hard rules (details: `.github/skills/security-rules/SKILL.md`)
1. Kestrel binds loopback only (`ListenLocalhost`); `AllowedHosts` is localhost.
2. Secrets are write-only in the UI, resolved server-side at send time, stored with OS protection, never in logs/history/exports.
3. Never log or persist `Authorization`, `Cookie`, `Set-Cookie`. Only exception: pasted session cookies stored encrypted per environment (`docs/decisions/0007-session-cookies.md`). Response bodies are not stored by default.
4. TLS validation on by default; ignoring it is explicit per environment.
5. Production environments need confirmation before POST/PUT/PATCH/DELETE.
6. No real customer URLs, tokens or data in code, docs, tests or fixtures.

## Conventions
- Code, identifiers, commits, repo docs: English. In-app user docs (`docs/user/en`): English; the UI is English too (more languages can be added as `docs/user/<lang>`).
- Decisions go in `docs/decisions/`. Update affected skills/docs in the same change.
- Definition of done: `.github/skills/definition-of-done/SKILL.md`.
- Known v1 deviations (see `docs/decisions/0004`, `0006`): `EnsureCreated` instead of migrations, DPAPI (Windows) only, no Monaco/MudBlazor.
