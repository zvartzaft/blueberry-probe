# Copilot instructions

Blueberry Probe: local, single-user Blazor Server tool (.NET 10) for test-running HTTP requests against customer systems. Security is priority 1. `AGENTS.md` holds the hard security rules (loopback-only Kestrel, write-only secrets, never log/persist `Authorization`/`Cookie`/`Set-Cookie` (sole exception: pasted session cookies stored encrypted per environment, `docs/decisions/0007-session-cookies.md`), TLS validation on by default, production confirmation for mutating verbs, no real customer data); they apply to every change.

## Commands
- Build: `dotnet build ProbeDesk.slnx`
- Test all: `dotnet test ProbeDesk.slnx`
- Single project: `dotnet test tests/ProbeDesk.Core.Tests`
- Single test: `dotnet test tests/ProbeDesk.Core.Tests --filter "FullyQualifiedName~FlowTests.SomeTestName"`
- Run: `dotnet run --project src/ProbeDesk.Web`, then open http://127.0.0.1:5187. No linter is configured.

## Architecture
- Layers: `Web` (Blazor UI, DI, Kestrel, guard middleware) -> `Core`, `Import`, `Storage`; `Import -> Core`; `Storage -> Core`; `Core` references nothing internal (HTTP engine in `Http/`, model in `Model/`, variables/extractors/assertions/flow runner in `Flow/`). Keep Core UI- and storage-free.
- `Import` holds Postman/.http/cURL importers, exporters and the import report; `Storage` is EF Core/SQLite plus secret protection (DPAPI, Windows only).
- Collections live in the user profile, never in the repo.
- Known v1 deviations: `EnsureCreated` instead of migrations, no Monaco/MudBlazor (`docs/decisions/0004`, `0006`).

## Conventions
- Code, identifiers, commits, repo docs and UI/in-app help (`docs/user/en`) are English.
- Importers never execute scripts and must report everything not translated; fixtures use invented data only (`example.test` hosts, fake tokens).
- HTTP method is a string; use `HttpCompletionOption.ResponseHeadersRead` and honor `CancellationToken`.
- UI: CSS variables from `wwwroot/tokens.css`, no hard-coded colors, visible focus states, light and dark themes.
- Significant decisions go in `docs/decisions/`; update affected skills, `AGENTS.md` and in-app help in the same change. Checklist: `.github/skills/definition-of-done/SKILL.md`.

- Work in the milestone order in `docs/decisions/0001-plan.md`; keep steps small, tested, documented.
- Do not deviate from binding decisions silently; ask first.
- Prefer rules and reasons in docs over code copies; link to source files.
- Path-specific rules live in `.github/instructions/`; task knowledge in `.github/skills/`.
