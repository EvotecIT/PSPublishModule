# PowerForge.Web Roadmap (Inventory + Milestones)

Last updated: 2026-02-10

This document is the single source of truth for:

- what PowerForge.Web already supports (engine-level),
- what is partial/fragile (works but regresses across themes/sites),
- what is missing,
- and the next milestones to prevent “going in circles”.

## How This Roadmap Is Verified (Avoid "We Think")

This file is intended to be evidence-based:

- **Schemas**: check `schemas/powerforge.web.*.schema.json` for the config surface area.
- **Engine code**: verify key features exist by locating their implementation under `PowerForge.Web/Services` and related models under `PowerForge.Web/Models`.
- **CLI wiring**: verify tasks/policies exist under `PowerForge.Web.Cli/`.

When adding items to **Missing**, sanity-check with a quick search so we don't invent gaps:

- `rg -n "application/atom\\+xml|jsonfeed|application/feed\\+json" PowerForge.Web Docs schemas`
- `rg -n "xref|cross[- ]ref" PowerForge.Web Docs`
- `rg -n "plugin|hook|transform" PowerForge.Web Docs schemas`

If a "Missing" item is implemented later, move it to **Have** or **Partial** and link the implementing files.

## Guiding Principles

- Determinism over cleverness: specs + contracts + verification.
- CI strict, dev friendly: fail in CI/release, warn+summarize locally.
- Prevent regressions across sites: prefer contracts/baselines/budgets over one-off fixes.
- “Agentable” defaults: stable structures, schemas, and reference renderers.

## Current Inventory (Have vs Partial vs Missing)

Legend:
- **Have**: supported and used today.
- **Partial**: supported but not yet “turnkey” across themes/sites (drift/regressions likely).
- **Missing**: not implemented in engine.

### Content + Routing

- **Have**: Collections (`pages/docs/blog/...`), section pages (`_index.md`), bundles (`index.md` + resources).
  - Docs: `Docs/PowerForge.Web.ContentSpec.md`
  - Code: `PowerForge.Web/Services/WebSiteBuilder.ContentDiscovery.cs`
- **Have**: Front matter: `title/description/date/tags/categories/aliases/layout/template/outputs/meta.*`.
  - Code: `PowerForge.Web/Services/FrontMatterParser.cs`
- **Partial**: DocFX-style TOC (`toc.json/yml`) is supported, but many sites still prefer `UseToc:false` and hardcoded sidebars.
  - Docs: `Docs/PowerForge.Web.ContentSpec.md`

### Blog + Taxonomies + Feeds

- **Have**: Taxonomies (`tags/categories/custom`) with generated taxonomy list + term pages.
  - Schema: `schemas/powerforge.web.sitespec.schema.json` (`Taxonomies`)
  - Code: `PowerForge.Web/Services/WebSiteBuilder.ContentDiscovery.cs` + `PowerForge.Web/Services/WebSiteBuilder.OutputRendering.cs`
- **Have**: RSS output (`rss.xml`) for section and taxonomy pages; feed settings via `FeedSpec`.
  - Code: `PowerForge.Web/Services/WebSiteBuilder.OutputRendering.cs` (`RenderRssOutput`)
  - Model: `PowerForge.Web/Models/FeedSpec.cs`
- **Partial**: “Blog UX defaults” (archives pages, featured posts, series) are theme-driven, not standardized by engine.
- **Missing**: Atom feed and JSON Feed formats (RSS only today; RSS uses Atom namespace only for self-link metadata).

### Search

- **Have**: Search index generation at `/search/index.json` (for client-side search).
  - Code: `PowerForge.Web/Services/WebSiteBuilder.DataAndDiagnostics.cs` (`WriteSearchIndex`)
- **Partial**: Search UI/UX is theme responsibility; no canonical “search surface” renderer contract yet.

### Themes + Contracts

- **Have**: Theme inheritance + manifest loader (`theme.manifest.json`, legacy `theme.json`).
  - Code: `PowerForge.Web/Services/ThemeLoader.cs`
- **Have**: Feature contracts (required layouts/partials/slots/surfaces + CSS selector checks).
  - Code: `PowerForge.Web/Services/WebSiteVerifier.ThemeRules.cs`, `PowerForge.Web/Services/WebSiteVerifier.ThemeFeatureContracts.cs`
- **Partial**: Themes often don’t declare contracts (schema v2) or ship all required partials, so behavior still drifts.
- **Partial**: “Nav surfaces” exist conceptually, but aren’t yet enforced as a stable cross-theme structure with a reference renderer.

### Navigation

- **Have**: Menus + actions + regions + profiles + auto menus (rich model).
  - Docs: `Docs/PowerForge.Web.ContentSpec.md` (Navigation section)
- **Have**: Navigation surfaces: `Navigation.Surfaces` projects common patterns into stable runtime surfaces (`navigation.surfaces`).
  - Code: `PowerForge.Web/Models/NavigationSpec.cs`, `PowerForge.Web/Services/WebSiteBuilder.Navigation.cs`
- **Have**: Scriban reference renderer helpers exposed as `pf`:
  - `pf.nav_links`, `pf.nav_actions`, `pf.menu_tree`
  - Code: `PowerForge.Web/Services/ScribanThemeHelpers.cs`, `PowerForge.Web/Services/ScribanTemplateEngine.cs`
- **Partial**: Many existing themes still hardcode sidebars and/or index-based menu rendering (e.g. `navigation.menus[0]`), causing drift/regressions across sites.
- **Partial**: One canonical “reference renderer” output (JSON) for navigation surfaces for non-Scriban consumers (scripts, external renderers) is not yet standardized end-to-end.

### Shortcodes + Data-driven Blocks

- **Have**: Shortcodes (built-in registry + theme overrides via `partials/shortcodes/<name>.html`).
  - Code: `PowerForge.Web/Services/ShortcodeRegistry.cs`, `PowerForge.Web/Services/ShortcodeProcessor.cs`
  - Docs: `Docs/PowerForge.Web.Theme.md`

### Localization + Versioning

- **Have**: Localization routing/runtime.
  - Models: `PowerForge.Web/Models/LocalizationSpec.cs`
  - Code: `PowerForge.Web/Services/WebSiteBuilder.Navigation.LocalizationAndVersioning.cs`
- **Have**: Versioning runtime (basic).
  - Models: `PowerForge.Web/Models/QualitySpec.cs` (`VersioningSpec`), `PowerForge.Web/Models/VersioningRuntime.cs`
- **Partial**: Multi-version docs conventions and canonicalization patterns (DocFX-like) are not standardized end-to-end.

### API Docs

- **Have**: C# XML API docs generator + pipeline integration + source links.
  - Docs: `Docs/PowerForge.Web.ApiDocs.md`
- **Partial**: Theme/layout contract for API pages (ensuring site nav renders) requires consistent `api-header/api-footer` partials across themes.

### Quality Gates (Verify/Audit/Doctor) + Budgets

- **Have**: Verify (theme/nav lint, best practices, baselines, fail-on-new).
  - Code: `PowerForge.Web/Services/WebSiteVerifier.*`, CLI: `PowerForge.Web.Cli/*`
- **Have**: Audit (links/assets/nav coverage/required routes/rendered checks) with baselines + budgets.
  - Code: `PowerForge.Web/Services/WebSiteAuditor*.cs`
- **Have**: CI strict/dev fast pattern via pipeline modes (site-level adoption varies).
- **Partial**: Warning bucketing/threshold UX can be improved (signal vs spam).

### Performance / Optimize

- **Have**: HTML/CSS/JS minify, critical CSS, hashing, headers, image optimization + variants + budgets.
  - CLI: `powerforge-web optimize --help`
- **Partial**: Incremental builds beyond “pipeline cache + skip heavy steps” (Hugo-tier invalidation) are not there yet.

### Extensibility

- **Partial**: Pipeline steps cover many needs, but there is no first-class plugin/hook system for custom transforms without forking.
- **Missing**: “Hook points” API (pre/post build, per-page HTML transform, pre-render data transforms) as a stable contract.

## Milestones (Stop Regressions First)

### M0: Quality Gate Unification (Completed)

- CI/dev contract pattern for sites: `--mode ci` + `modes:["ci"]` steps, baselines in repo root.
- Audit baselines: hashed keys to keep baseline files small and loadable.
- CSS contract validation fixed on Windows for `/css/*.css` hrefs.
- Empty verify baseline supported for fail-on-new (0 keys baseline is valid).

### M1: Navigation Surfaces + Reference Renderer (Next)

Goal: themes/agents stop guessing and API/docs nav stops drifting.

- Completed (engine-side building blocks):
  - Surfaces runtime projection: `Navigation.Surfaces` -> `navigation.surfaces`
  - Scriban `pf.*` reference renderer helpers
- Remaining:
  - Standardize surface names (minimum: `main`, `docs`, `api`, `products`) in starter docs.
  - Generate an engine-owned JSON payload suitable as a stable reference renderer input for non-Scriban consumers.
  - Verify:
    - warn in dev, fail in CI when `features` require a surface but site/theme doesn’t provide it.

### M2: Blog UX Defaults + Feed Parity (Next)

- Implement Atom output (in addition to RSS).
- Implement JSON Feed output (optional).
- Standardize blog list/term layouts in scaffold themes, so blog becomes turnkey.

### M3: DocFX-class Docs Conveniences (Later)

- XRef/cross-reference system (docs <-> API).
- Multi-version docs conventions with canonical rules.
- Include/overwrite mechanisms for conceptual docs (DocFX-like).

### M4: Extensibility + Incremental Build (Later)

- Stable hook/plugin model for transforms (no forks).
- True incremental build graph (content hash invalidation; partial rebuilds).

## Site Adoption Checklist (Best Practice)

- `site.json`:
  - Set `Features` explicitly (`docs`, `apiDocs`, `blog`, `search`, `notFound`).
  - For collections that don’t use DocFX-style TOC, set `UseToc:false`.
- Theme:
  - Use `theme.manifest.json` (schemaVersion 2).
  - Declare `features` + `featureContracts` and required CSS selectors for high-drift pages.
  - Provide `api-header`/`api-footer` partials when `apiDocs` is enabled.
- Pipeline:
  - Add a `verify-ci` step (`modes:["ci"]`) with `failOnNewWarnings:true` + baseline.
  - Add audit/doctor baselines and a `maxTotalFiles` budget.
