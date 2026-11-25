---
applyTo: "src/ProbeDesk.Core/**"
---
Core must stay free of UI and storage dependencies and reference no other project. Method is a string. Never log secrets or Authorization/Cookie/Set-Cookie values. Use `HttpCompletionOption.ResponseHeadersRead` and honor `CancellationToken`.
