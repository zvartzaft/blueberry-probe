---
name: http-engine
description: Use when adding an HTTP verb, body type, auth type, redirect/cookie handling or header logic in ProbeDesk.Core.
---
# HTTP engine

- Build `HttpRequestMessage` with `new HttpMethod(string)`; Method is a string.
- Headers: `TryAddWithoutValidation`, fall back to `Content.Headers` (Content-Type etc.).
- Use `ResponseHeadersRead`, stream, cap rendered size, always accept `CancellationToken` and a timeout.
- One `HttpSession` (own `CookieContainer`, `SocketsHttpHandler`, `AllowAutoRedirect=false`) per user+environment. No `IHttpClientFactory` for sessions. The jar is in memory; pasted session cookies are stored encrypted with the environment and seeded into the jar (`SessionCookies`, `docs/decisions/0007-session-cookies.md`).
- `FlowRunner` adds `X-CSRF-TOKEN` (via `CsrfTokenProvider`) to mutating requests when `RequestEnvironment.AutoCsrf` is on, same origin as the Base URL only.
- Redirects followed manually per request, sharing the cookie container.
- Preview and "Copy as cURL" mask secrets (`CurlBuilder`, `src/ProbeDesk.Core/Http/`).
- Entry points: `HttpExecutor.SendAsync(RequestSpec)`, `RequestBuilder.Build`, `HttpSession`. Add a body type by extending `BodyType` and handling it in `RequestBuilder` and `CurlBuilder`.
