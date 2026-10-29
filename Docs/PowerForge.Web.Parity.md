# PowerForge.Web Parity Notes (DocFX, Hugo, Gatsby, Ghost, Jekyll, Astro)

Last updated: 2026-02-14

This is a pragmatic parity cheat-sheet for maintainers and agents.
It is not intended to be a perfect feature-by-feature comparison of every ecosystem plugin.

## How To Read This

- **PowerForge.Web** uses: `Have` / `Partial` / `Missing` (validated in this repo).
- Other tools use broad labels:
  - `Built-in` means it is a first-class, expected capability.
  - `Ecosystem` means it is commonly done via plugins/integrations.
  - `N/A` means the tool is not meant for that use-case.

If you want to verify a PowerForge item, start with:
- `Docs/PowerForge.Web.Roadmap.md`
- `Schemas/powerforge.web.*.schema.json`
- `PowerForge.Web/Services/*`

## High-Level Matrix

| Capability | PowerForge.Web | DocFX | Hugo | Gatsby | Ghost | Jekyll | Astro |
|---|---|---|---|---|---|---|---|
| Static pages + collections | Have | Built-in | Built-in | Built-in | N/A | Built-in | Built-in |
| Front matter | Have | Built-in | Built-in | Built-in | Built-in | Built-in | Built-in |
| Docs TOC (toc.yml/json) | Partial | Built-in | Ecosystem | Ecosystem | N/A | Ecosystem | Ecosystem |
| Nested navigation model | Have | Built-in | Built-in | Ecosystem | Built-in | Ecosystem | Ecosystem |
| Navigation profiles/surfaces | Have/Partial | Partial | Ecosystem | Ecosystem | N/A | Ecosystem | Ecosystem |
| Theme contract enforcement | Have | Partial | N/A | N/A | N/A | N/A | N/A |
| Search index generation | Have | Ecosystem | Ecosystem | Ecosystem | Built-in | Ecosystem | Ecosystem |
| Blog posts | Have | Partial | Built-in | Built-in | Built-in | Built-in | Built-in |
| Tags/categories | Have | Ecosystem | Built-in | Built-in | Built-in | Built-in | Built-in |
| RSS feeds | Have | Ecosystem | Built-in | Ecosystem | Built-in | Built-in | Ecosystem |
| Atom feed | Have | Ecosystem | Built-in | Ecosystem | Built-in | Ecosystem | Ecosystem |
| JSON Feed | Have | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem |
| Taxonomy pages (list + term) | Have | Ecosystem | Built-in | Built-in | Built-in | Built-in | Built-in |
| API reference from .NET XML docs | Have | Built-in | N/A | N/A | N/A | N/A | N/A |
| Package/module metadata hub (csproj + psd1) | Have/Partial | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem |
| XRef/cross refs (docs <-> api) | Partial | Built-in | Ecosystem | Ecosystem | N/A | Ecosystem | Ecosystem |
| Multi-version docs conventions | Partial | Built-in | Ecosystem | Ecosystem | N/A | Ecosystem | Ecosystem |
| i18n routing | Have | Ecosystem | Built-in | Ecosystem | Ecosystem | Ecosystem | Ecosystem |
| Output formats (html/rss/atom/jsonfeed/json) | Have | Built-in | Built-in | Built-in | Built-in | Built-in | Built-in |
| Pipeline source sync from Git repos (public/private) | Have | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem |
| Host redirect artifacts (Netlify/Azure/Vercel/Apache/Nginx/IIS) | Have | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem |
| Incremental build graph | Partial | Partial | Built-in | Ecosystem | Built-in | Partial | Built-in |
| Plugin/hook system | Partial | Ecosystem | Built-in | Built-in | Built-in | Ecosystem | Built-in |
| Asset pipeline (minify/hash) | Have | Ecosystem | Built-in | Built-in | Built-in | Ecosystem | Built-in |
| Critical CSS | Have | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem |
| Image optimization | Partial | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem | Ecosystem |

## What PowerForge.Web Has That Helps Avoid “Half-Cooked Sites”

- **Feature flags + contracts**:
  - Themes can declare required layouts/partials/surfaces per feature and required CSS selectors.
  - CI can fail when a site enables a feature but theme support is incomplete.
- **Quality gates with baselines**:
  - Warn locally, fail only on new regressions in CI (`failOnNewWarnings`, `failOnNewIssues`).
- **API docs that can share site navigation**:
  - API header/footer fragments support token injection (brand + nav links + actions).

## Biggest Gaps To Close (Engine-Level)

These are the gaps that most often cause surprises when compared to DocFX/Hugo/Astro-class experiences:

1. **Extensibility hooks**
   - Pipeline hook, per-page HTML transform, file-based data transform, and typed JSON model-transform steps are available (including wildcard path transforms for JSON models).
   - Direct collection/page model transforms (without JSON file boundary) are still future work.
2. **DocFX-class cross references**
   - `xref:` link resolution exists and API docs now emit DocFX-style `xrefmap.json` for C# + PowerShell symbols.
   - Full symbol graph parity and richer UID coverage are still in progress.
3. **Incremental rebuild**
   - Hugo-tier invalidation (content-hash dependency graph) so big sites rebuild fast and deterministically.

