---
name: user-docs
description: Use when adding or editing in-app help pages in docs/user, including help links, titles and the What's new page.
---
# User docs

- Markdown in `docs/user/<lang>/<slug>.md` (English `en` first), embedded as resources, rendered with Markdig at `/help/{slug}`.
- Every page needs a title (H1); every internal link must resolve (tested).
- Required pages: getting started, variables/environments, cookie/CSRF login, file uploads, import, secrets, shortcuts, what's new.
- Few screenshots; they go stale.
- Pages are embedded via a wildcard in `ProbeDesk.Web.csproj`; `HelpService` loads them. Link internally as `[text](/help/slug)`. Tests: `tests/ProbeDesk.Web.Tests/WebTests.cs` (titles, links, required pages). Add new required slugs to that test and to `HelpService.Order`.
- Context help: place `<HelpLink Slug="..." />` in each view's heading.
- Update `whats-new.md` for user-visible changes.
