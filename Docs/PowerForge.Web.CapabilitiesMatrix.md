# PowerForge.Web Capabilities Matrix (vs DocFX/Hugo/Gatsby/Ghost/Jekyll/Astro)

Last updated: 2026-02-09

This is a quick, agent-friendly checklist of what the engine supports today,
what is partial/fragile, and what is missing, framed against common expectations
set by popular doc/SSG systems.

Legend:
- Have: implemented and used successfully today.
- Partial: implemented but not standardized enough to be "turnkey" across sites/themes.
- Missing: not implemented in the engine.
- N/A: not the goal of PowerForge.Web (or belongs to a different class of product).

Notes:
- PowerForge.Web is pipeline-driven (audit/optimize/budgets) and aims for deterministic
  CI behavior. It is not trying to replicate full "plugin ecosystems" by default.
- For the evidence-based inventory, see `Docs/PowerForge.Web.Roadmap.md`.

## Matrix (Engine vs Ecosystems)

| Capability | PowerForge.Web | DocFX | Hugo | Gatsby | Ghost | Jekyll | Astro |
|---|---|---|---|---|---|---|---|
| Markdown pages + layouts | Have | Have | Have | Have | Have | Have | Have |
| Collections/sections | Have | Have | Have | Have | Partial | Have | Have |
| Taxonomies (tags/categories) | Have | Partial | Have | Have | Have | Have | Have |
| RSS feeds | Have | Have | Have | Have | Have | Have | Have |
| Atom feeds | Missing | Have | Have | Partial | Have | Partial | Have |
| JSON Feed | Missing | Missing | Partial | Partial | Partial | Partial | Partial |
| Blog UX defaults (archives, pagination, series) | Partial | Partial | Have | Have | Have | Have | Have |
| Search index generation | Have | Have | Have | Have | Have | Have | Have |
| Search UI/UX | Partial (theme-driven) | Have | Partial (theme-driven) | Have | Have | Partial | Have |
| API reference generation | Have | Have | N/A | N/A | N/A | N/A | N/A |
| API docs xref graph (docs <-> API) | Partial | Have | N/A | N/A | N/A | N/A | N/A |
| Multi-version docs conventions | Partial | Have | Partial | Partial | N/A | Partial | Partial |
| Theme contract / feature flags | Partial | Have | Partial | Partial | Have | Partial | Partial |
| Navigation surfaces (main/docs/api/products) | Partial | Have | Have | Have | Have | Have | Have |
| Shortcodes/components | Have | Partial | Have | Have | Have | Partial | Have |
| i18n/multilingual | Have | Have | Have | Partial | Partial | Have | Have |
| Asset pipeline + bundling | Have | Partial | Have | Have | Have | Partial | Have |
| Image optimization | Have | Partial | Have | Have | Partial | Partial | Have |
| Deterministic CI quality gates (audit, budgets) | Have | Partial | Partial | Partial | Partial | Partial | Partial |
| Extensibility (plugin ecosystem) | Missing (planned hooks) | Partial | Have | Have | Have | Have | Have |
| CMS authoring UI | N/A | N/A | N/A | N/A | Have | N/A | N/A |

## Practical Takeaways (for PowerForge.Web)

- If you want "DocFX tier docs": the highest leverage missing pieces are xref/cross-ref,
  versioned docs conventions, and include/overwrite workflows for conceptual content.
- If you want "Hugo tier SSG": feed parity (Atom/JSON Feed), pagination/blog defaults,
  and real incremental rebuilds are the practical gap-closers.
- If you want "Astro tier dev ergonomics": fast rebuild + clear diagnostics + integration
  points are the wins, without adopting islands/MDX as core requirements.
