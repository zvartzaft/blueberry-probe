# 0007: Pasted session cookies are stored encrypted per environment

Status: accepted. Amends hard rule 3 ("never persist `Cookie`/`Set-Cookie`") for one narrow case.

## Context
Blueberry Probe is mainly used for secure APIs, where a login needs an interactive browser sign-in. Rebuilding that as a request flow is fragile, and cookies were memory-only, so every restart meant starting over. `` solved this by pasting the browser `Cookie` header once.

## Decision
- The Cookies page takes a pasted `Cookie` header. Only `.AspNetCore.Cookies` (auth) and `.AspNetCore.Antiforgery.*` are kept (`SessionCookies.Parse`); everything else is dropped.
- They are stored in `RequestEnvironment.SessionCookies`, inside the DPAPI-protected environment payload (same as secret variables, `0004-storage.md`). Values are write-only in the UI: only status is shown.
- On session start they are seeded into the in-memory cookie jar for the Base URL's host. Cookies refreshed by the server are saved back after a run, but only if cookies were stored before.
- `RequestEnvironment.AutoCsrf` fetches `/api/authentication/antiforgery/token` before POST/PUT/PATCH/DELETE to the Base URL's origin and sends it as `X-CSRF-TOKEN`. It is never sent to another origin.
- "Clear cookies" removes them from storage and resets the session.

## Unchanged
Only these pasted/refreshed profile cookies are persisted. `Authorization`, other cookies and all `Set-Cookie` headers are still never logged, put in history, cURL output or exports. Production confirmation still applies (the token fetch is a GET).
