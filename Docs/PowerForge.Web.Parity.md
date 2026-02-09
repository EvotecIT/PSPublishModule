# PowerForge.Web Parity Notes (DocFX/Hugo/Gatsby/Ghost/Jekyll/Astro)

Last updated: 2026-02-09

This document is a pragmatic comparison of what PowerForge.Web does today, what users typically expect from popular static site generators / doc engines, and the highest-leverage gaps to close (to reduce regressions across multiple sites).

## What PowerForge.Web Already Has (Core Strengths)

- Deterministic pipeline model (`pipeline.json`) with composable tasks (build/verify/apidocs/llms/sitemap/optimize/audit/doctor).
- Theme system with inheritance + manifest (`theme.manifest.json` preferred, legacy `theme.json` supported).
- Strong navigation model:
  - multiple menus, actions
  - regions/slots
  - profiles (route/layout scoped variants)
  - auto menu generation
- API reference generation (C# XML doc based) with optional source links and hybrid JSON output.
- Audit + budgets:
  - broken links/assets/nav coverage
  - required routes
  - rendered checks (Playwright) for “it works in a browser”
  - baselines + “fail only on new issues”
  - output budgets like `maxTotalFiles`
- Optimization step:
  - HTML/CSS/JS minification, critical CSS, hashing, headers
  - image optimization + responsive variants (site-controlled)
- Search index generation (`/search/index.json`) for simple client-side search.
- “Agent-friendly” behavior: JSON specs, schemas, consistent output paths, and stable failure summaries.

## What “Parity” Usually Means (User Expectations)

Most users (and agents) implicitly expect:

- A theme contract that makes “docs/apiDocs/blog/search/404” support explicit and verifiable.
- A stable navigation structure that themes can render without guessing (top bar vs docs sidebar vs product switcher).
- A fast local dev loop by default, without sacrificing CI strictness.
- A built-in blog (tags/categories + list pages + RSS/Atom).
- A search UX (not just an index file).
- Plugin-like extensibility so sites can add custom transforms without forking the engine.

## Comparisons

### DocFX (docs + API)

DocFX is strongest at documentation-scale features:

- Managed reference model, xref/cross-linking, conceptual + reference separation.
- Include/overwrite mechanisms, monorepo support patterns, versioned docs patterns.
- Built-in search UX and docs navigation patterns.

PowerForge.Web today:

- Has a usable API generator and a stronger “site pipeline” story (budgets, optimize, audit).
- Lacks DocFX-style xref graph, include/overwrite, multi-version docs, and a mature reference model.

High-value DocFX-like gaps:

- Cross-reference / xref system (IDs + resolvers across docs and API).
- Versioning model (`/v1/`, `/v2/`) with stable canonical handling.
- Include/overwrite pipeline (like “partials/includes” for docs content, not only layouts).

### Hugo (SSG)

Hugo is strongest at:

- Taxonomies (tags/categories), list pages, RSS, pagination.
- Shortcodes, content types, multilingual, large theme ecosystem.
- Fast incremental builds.

PowerForge.Web today:

- Has collections, data, and good audit/verify/optimize.
- Has taxonomies and RSS output support (section + taxonomy pages can emit `rss.xml`).
- Lacks Atom/JSON feed formats, richer blog primitives (archives/series), i18n polish, and a plugin ecosystem.

High-value Hugo-like gaps:

- Feed parity: Atom and JSON Feed (keep RSS as-is).
- Better blog UX defaults: archives, featured posts, series, and pagination conventions.
- Shortcodes (safe, deterministic) for common patterns.
- Persistent build cache keyed by content hash (real incremental builds).

### Gatsby (React SSG)

Gatsby is strongest at:

- Plugin ecosystem + data sourcing (GraphQL).
- Component-driven pages, “app-like” ecosystems.

PowerForge.Web today:

- Is intentionally simpler and more deterministic.
- Doesn’t try to compete on React/plugin ecosystems.

High-value Gatsby-like gaps (pragmatic subset):

- “Plugin hooks” for transforms (pre/post build, HTML post-processing, per-page transforms).
- External data sourcing adapters (GitHub, NuGet, RSS imports) as optional steps.

### Ghost (CMS + publishing)

Ghost is strongest at:

- Authoring workflow, CMS admin UI, memberships, newsletters.

PowerForge.Web today:

- Is a static engine; it does not ship a CMS.

High-value Ghost-like gaps (only if needed):

- Optional “headless CMS ingest” step (pull content at build time).

### Jekyll (GitHub Pages SSG)

Jekyll is strongest at:

- Markdown + layouts + collections + blog + GitHub Pages friendliness.

PowerForge.Web today:

- Matches the basic “markdown -> themed HTML” workflow and has better audits/budgets.
- Lacks Jekyll’s built-in blog/RSS conventions and broad plugin ecosystem.

### Astro (modern SSG)

Astro is strongest at:

- Component islands, MDX, modern dev server ergonomics, integration ecosystem.

PowerForge.Web today:

- Focuses on static output + deterministic pipelines (better for CI gates and budgets).
- Does not provide component islands or MDX.

High-value Astro-like gaps (without adopting the whole model):

- Better dev server experience (fast rebuild + partial invalidation + clear diagnostics).
- Optional integration points for modern frontends (generate a “static assets manifest” that other tools can consume).

## Gaps We Should Close Next (Engine-Level, High Leverage)

These are the biggest “regression preventers” across multiple sites:

1. Theme contract + feature flags
   - Theme declares supported features and required partials/slots.
   - Verify should fail (CI) when a site enables `apiDocs` but theme lacks `api-header/api-footer`, required CSS selectors, etc.
2. Stable navigation surfaces (reference renderer)
   - Standardize “surfaces” (main/docs/api/products) and renderers so themes/agents don’t guess.
3. CI strict, dev friendly defaults
   - `--dev` skips optimize/audit/rendered by default; CI runs them (and gates) via `--mode ci`.
4. Audit signal improvements
   - Bucketing + thresholds + “fail only on new” everywhere.
   - Required routes + nav coverage + heading order + CSS contracts as first-class checks.
5. Blog/tags/RSS
   - Blog collection + taxonomy pages + RSS already exist; add Atom + JSON feed, and make the default theme scaffolding cover blog/taxonomy layouts.

## Notes On “Ignoring” Issues (Best Practice)

- Prefer baselines for legacy noise:
  - verify baseline: `.powerforge/verify-baseline.json`
  - audit baseline: `.powerforge/audit-baseline.json`
- Prefer explicit suppressions for known false positives (scoped, code-based) rather than blanket ignores.
- Recommended policy:
  - Dev: warn, summarize, don’t block iteration.
  - CI/release: fail on new warnings/issues and on configured categories/budgets.
