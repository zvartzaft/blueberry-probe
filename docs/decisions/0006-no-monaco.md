# 0006: Plain text areas instead of Monaco (deferred)

The brief suggests Monaco (BlazorMonaco) for body and response editing. v1 uses styled `textarea`/`pre` elements: no external JS bundle, offline-friendly, simpler accessibility and nothing extra to patch. Revisit when syntax highlighting or large-response editing becomes a real need; the composer and response view are the only places that would change.

Other v1 deviations from the brief: MudBlazor is not used (custom CSS on design tokens only), and the visual mock-up step was replaced by building the themed shell directly.
