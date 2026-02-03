# Backlog

## PowerForge.Web (Engine)

### API Docs parity (DocFX-style)
- Render XML tags beyond summary/remarks: `example`, `exception`, `value`, `typeparam`, `seealso`
- Add access modifiers + async/readonly/static/virtual info in signatures
- Group overloads and surface constructors separately
- Optional source links per type/member (GitHub line mapping)
- Optional per‑type mini‑TOC (“In this article”)
- Type hierarchy tree (base chain + derived types when available)

### Engine quality
- Validate layout hooks (`extra_css_html` / `extra_scripts_html`) so per-page assets load.
- Warn when Prism local assets are missing (prevent silent 404s).
- Prism theme overrides (light/dark) for reusable site theming.
- Pipeline defaults: include audit step (links/assets/nav/rendered).

## Websites
