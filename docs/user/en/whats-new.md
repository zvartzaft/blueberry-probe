# What's new

## Version 0.3

- Paste your browser's `Cookie` header once on the **Cookies** page. Blueberry Probe keeps the auth and antiforgery cookies, stores them encrypted per environment and reuses them after a restart.
- Optional automatic CSRF token for POST, PUT, PATCH and DELETE (set per environment).
- The Cookies page is simpler: status, Test session and Clear. The manual cookie table is gone.

- The **Environment** selector now refreshes when you save, delete or import environments.
- The **Cookies** page no longer needs a click outside the box before **Save cookies** becomes active.
- Help pages updated to match.
- The application is now called **Blueberry Probe**.
- Import accepts Postman files as **YAML** and **zip archives** with several files.
- A complete redesign: a left navigation rail (collapsible) with the **Environment** selector on top, a compact title bar on every page instead of the big banner, a sharper light theme (dark theme still available) and a single blue accent. Search with <kbd>Ctrl</kbd>+<kbd>K</kbd>, send with <kbd>Ctrl</kbd>+<kbd>Enter</kbd>.

## Version 0.2

- The whole interface and the help are now in English.
- A redesigned workspace: saved requests on the left, a tabbed request editor and a tabbed response viewer.
- Headers and query parameters are edited as simple rows.
- A visual flow editor for login and CSRF steps.
- A new look, with light and dark themes.

## Version 0.1

- Requests with all common methods, preview and copy as cURL.
- Collections, environments with secret variables and production protection.
- Multipart, binary files and file uploads.
- Cookie session, extractors, flow steps and checks.
- Import from Postman, .http and cURL, plus export.
- History (metadata only) and comparison with the previous response.
- Built-in help and a local echo endpoint.

Start with [Getting started](/help/getting-started).
