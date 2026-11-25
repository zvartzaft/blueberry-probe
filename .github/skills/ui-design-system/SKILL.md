---
name: ui-design-system
description: Use when building or changing any Blueberry Probe screen, component, CSS, theme or microcopy.
---
# UI design system

- Tokens in `src/ProbeDesk.Web/wwwroot/tokens.css` (colors, type, spacing, radius, motion); light (default) and dark via `data-theme`. Design language "Precision light" (ADR 0008): white/graphite surfaces, 1px borders, flat (almost no shadow), 6px radius controls, ONE electric-blue accent, semantic ok/caution/danger tokens, method colors as `--pd-m-*`. Fonts: Segoe UI Variable / Inter with local fallbacks, Cascadia Mono for code; never load external fonts or CSS (offline, security). No hard-coded colors in CSS or Razor.
- Layout: `MainLayout.razor` = dark left rail (brand + collapse, environment switcher, nav with icons, Ctrl+K search, theme toggle) + content (production banner, page, footer). The rail collapses to icons (state in localStorage) and becomes a top strip under 860px. Production = red rail edge + banner + red env box.
- Pages open with the compact `PageHeader` (title, one-line subtitle, actions, help link); no hero bands. Workspace is the same shell: request sidebar + tabbed editor (URL bar with method + Send) + tabbed response.
- Static assets need `UseStaticWebAssets()` in `Program.cs` so `blazor.web.js` loads outside Development.
- Identity surfaces (request composer, step flow, response viewer) are custom components on CSS variables; MudBlazor (if added) is themed to tokens.
- Env context bar always visible; production uses warning treatment (`.pd-envbar--production`).
- Step-flow view is the main differentiator; simple mode by default, advanced behind "Visa mer".
- Ctrl+K palette, Ctrl+Enter send, visible focus, WCAG AA contrast.
- UI text English. Motion is subtle (140ms) and disabled under `prefers-reduced-motion`. Buttons: secondary by default, `pd-btn--primary` for the single main action per view, `pd-btn--danger` for destructive confirms. Use `pd-empty-state` for empty views.
