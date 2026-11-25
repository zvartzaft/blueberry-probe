# 0008: Visual redesign, "Precision light"

Status: accepted. Replaces the inspired look (white header, navy hero bands, pill buttons).

## Context
The Workspace was a dense tool without a banner, while every other page opened with a tall navy hero. The app felt like two products, and the generic card/pill styling looked less professional than the task deserves.

## Decision
- Design language "Precision light": white and graphite surfaces, 1px borders, almost no shadow, 6px radius, tight headings, one electric-blue accent. A dark theme exists; light is the default.
- Navigation is a collapsible dark left rail (brand, environment switcher, icon nav, search, theme). Under 860px it becomes a top strip. Production shows a red rail edge, a red environment box and a banner.
- `PageHero` is replaced by a compact `PageHeader` on all pages. The Workspace uses the same shell.
- All colors, including HTTP method colors and diff highlights, are tokens in `tokens.css`; no hard-coded colors. Fonts remain local only.
- Motion is limited to short fades and hover transitions and is disabled under `prefers-reduced-motion`.
- The brand is independent of colors.

## Consequences
Class names (`pd-*`) are kept, so Razor behavior is unchanged. The rail collapse state is stored in `localStorage` (`pd-rail`); the theme default is now light instead of following the OS. The `ui-design-system` skill and `whats-new.md` are updated.
