# 0004: Storage and secret protection

- SQLite via EF Core in `%LOCALAPPDATA%/ProbeDesk` (override with `ProbeDesk:DataDir`).
- Request and environment payloads are stored as JSON protected by `ISecretProtector` (DPAPI, current user, Windows only in v1). Name, method and folder are plain columns for listing and search.
- Schema is created with `EnsureCreated`, not migrations. This deviates from the brief; adopt migrations before the first release that must upgrade existing databases.
- Secrets are write-only in the UI: saving an empty secret keeps the stored value.
- History stores metadata only (method, host, path without query, status, duration), capped at 500 rows.
- Linux/macOS secret stores (libsecret/Keychain) are a later `ISecretProtector` implementation; the app currently fails on non-Windows.
