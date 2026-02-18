# PowerForge.Web Roadmap (Inventory + Milestones)

Last updated: 2026-02-18

This document is the single source of truth for:

- what PowerForge.Web already supports (engine-level),
- what is partial/fragile (works but regresses across themes/sites),
- what is missing,
- and the next milestones to prevent “going in circles”.

Companion execution plan for C# libraries + PowerShell modules:
- `Docs/PowerForge.Web.LibraryEcosystemPlan.md`
Companion SEO parity plan (Yoast-informed capability mapping):
- `Docs/PowerForge.Web.SeoParityPlan.md`

## How This Roadmap Is Verified (Avoid "We Think")

This file is intended to be evidence-based:

- **Schemas**: check `Schemas/powerforge.web.*.schema.json` for the config surface area.
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
  - Schema: `Schemas/powerforge.web.sitespec.schema.json` (`Taxonomies`)
  - Code: `PowerForge.Web/Services/WebSiteBuilder.ContentDiscovery.cs` + `PowerForge.Web/Services/WebSiteBuilder.OutputRendering.cs`
- **Have**: RSS output (`rss.xml`) for section and taxonomy pages; feed settings via `FeedSpec`.
  - Code: `PowerForge.Web/Services/WebSiteBuilder.OutputRendering.cs` (`RenderRssOutput`)
  - Model: `PowerForge.Web/Models/FeedSpec.cs`
- **Have**: Atom (`index.atom.xml`) and JSON Feed (`index.feed.json`) outputs via explicit output rules, or implicitly via `Feed.IncludeAtom` / `Feed.IncludeJsonFeed`.
  - Code: `PowerForge.Web/Services/WebSiteBuilder.OutputRendering.cs` (`RenderAtomOutput`, `RenderJsonFeedOutput`)
  - Schema: `Schemas/powerforge.web.sitespec.schema.json` (`Feed.IncludeAtom`, `Feed.IncludeJsonFeed`)
- **Partial**: “Blog UX defaults” (archives pages, featured posts, series) are theme-driven, not standardized by engine.

### Search

- **Have**: Search index generation at `/search/index.json` with optional per-language shards (`/search/<lang>/index.json`) for localized sites.
  - Code: `PowerForge.Web/Services/WebSiteBuilder.DataAndDiagnostics.cs` (`WriteSearchIndex`)
- **Partial**: Search UI/UX is theme responsibility; no canonical “search surface” renderer contract yet.

### SEO + Discovery

- **Have**: Canonical, OG/Twitter metadata, baseline structured data (`WebSite`, `Organization`, `Article`, `BreadcrumbList`), social card generation, and sitemap noindex-safe defaults.
  - Code: `PowerForge.Web/Services/WebSiteBuilder.RenderAssetsAndRouting.cs`, `PowerForge.Web/Services/WebSitemapGenerator.cs`
- **Partial**: `seo-doctor` pipeline step provides deterministic editorial+technical checks (title/meta length, H1, image alt, duplicate title intent, orphan candidates, optional focus keyphrase, canonical/hreflang validation, JSON-LD validation), but search-appearance templating and richer schema/sitemap families are still pending.
  - Code: `PowerForge.Web/Services/WebSeoDoctor.cs`, `PowerForge.Web.Cli/WebPipelineRunner.Tasks.SeoDoctor.cs`
- **Have**: IndexNow submission pipeline step (`indexnow`) for canonical URL push (batch/retry/dry-run/report + changed-file scoping).
  - Code: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.IndexNow.cs`, `PowerForge.Web.Cli/IndexNowSubmitter.cs`
- **Missing**: search appearance token templates/preview, expanded schema profiles (`FAQ/HowTo/Product/SoftwareApplication/NewsArticle`), and dedicated news/image/video sitemaps.
  - Plan: `Docs/PowerForge.Web.SeoParityPlan.md`

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
- **Have**: PowerShell help API docs (command help XML + parameter sets + examples + `about_*` topic import + fallback examples + coverage report).
- **Have**: API docs xref map emission (`xrefmap.json`, DocFX-style `references`) for both C# and PowerShell outputs.
  - Supports aliases like `T:Namespace.Type`, short type names (when unique), `ps:Command-Name`, `command:Command-Name`, module-qualified command aliases, and `about:` aliases.
  - Docs: `Docs/PowerForge.Web.ApiDocs.md`
- **Partial**: XRef link resolver + verifier checks (`xref:` in markdown links) with optional external maps (`Xref.MapFiles`) and optional build map emission (`_powerforge/xrefmap.json`).
  - Code: `PowerForge.Web/Services/WebSiteBuilder.Xref.cs`, `PowerForge.Web/Services/WebSiteVerifier.Xref.cs`, `PowerForge.Web/Services/WebXrefSupport.cs`
- **Partial**: Theme/layout contract for API pages (ensuring site nav renders) requires consistent `api-header/api-footer` partials across themes.

### Library + Module Release Hub

- **Have**: `package-hub` pipeline task that emits unified package/module metadata from `.csproj` and `.psd1` inputs.
  - Code: `PowerForge.Web/Services/WebPackageHubGenerator.cs`, `PowerForge.Web/Models/WebPackageHub.cs`
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.Content.cs`, `PowerForge.Web.Cli/WebPipelineRunner.Tasks.cs`
- **Partial**: Package hub currently emits metadata JSON only; turnkey rendered pages/layout contracts remain theme-driven.

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

### Source Sync + Hosting Targets

- **Have**: `git-sync` pipeline task can clone/fetch public or private Git repos (via token env) into deterministic local folders before downstream steps run (single or batch `repos`, optional submodule sync, optional manifest output, configurable shorthand base URL for enterprise/mirror hosts including nested group paths, explicit auth mode `auto|token|ssh|none`, retry controls, and lock file verify/update mode for commit pinning).
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.GitSync.cs`
  - Schema: `Schemas/powerforge.web.pipelinespec.schema.json` (`GitSyncStep`)
- **Have**: `hosting` pipeline task can keep/remove host artifacts for selected targets (`netlify`, `azure`, `vercel`, `apache`/`apache2`, `nginx`, `iis`) after site build.
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.Hosting.cs`
  - Schema: `Schemas/powerforge.web.pipelinespec.schema.json` (`HostingStep`)
- **Have**: Redirect build emits host-specific artifacts for Netlify, Azure SWA, Vercel, Apache (`.htaccess`), Nginx (`nginx.redirects.conf`), and IIS (`web.config`).
  - Code: `PowerForge.Web/Services/WebSiteBuilder.Redirects.cs`

### Extensibility

- **Have**: Pipeline includes a first-class `hook` step for named plugin hooks with command execution, deterministic context/env injection, and optional stdout/stderr/context artifacts.
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.Hook.cs`
  - Schema: `Schemas/powerforge.web.pipelinespec.schema.json` (`HookStep`)
- **Have**: Pipeline includes a first-class `html-transform` step for per-page HTML transforms with include/exclude globs, tokenized command arguments, and in-place/stdout write modes.
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.HtmlTransform.cs`
  - Schema: `Schemas/powerforge.web.pipelinespec.schema.json` (`HtmlTransformStep`)
- **Have**: Pipeline includes a first-class `data-transform` step for pre-render or pre-publish data shaping with explicit input/output contracts (stdin/file + stdout/passthrough modes).
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.DataTransform.cs`
  - Schema: `Schemas/powerforge.web.pipelinespec.schema.json` (`DataTransformStep`)
- **Have**: Pipeline includes a first-class `model-transform` step with typed JSON operations (`set`, `replace`, `insert`, `remove`, `append`, `merge`, `copy`, `move`) for engine-native structural transforms, including wildcard selectors (`[*]`, `*`, `**`) with deterministic ordering, optional per-operation target-count guards, and optional conditional filters (`when`/`where` for targets plus `fromWhen`/`sourceWhen` for copy/move sources).
  - CLI wiring: `PowerForge.Web.Cli/WebPipelineRunner.Tasks.ModelTransform.cs`
  - Schema: `Schemas/powerforge.web.pipelinespec.schema.json` (`ModelTransformStep`)
- **Partial**: Direct collection/page model transforms (without JSON file boundary) are still future work.

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
  - Standardize surface names (canonical: `main`, `docs`, `apidocs`, `products`; treat `api` as alias for `apidocs`) in starter docs.
  - Generate an engine-owned JSON payload suitable as a stable reference renderer input for non-Scriban consumers.
  - Verify:
    - warn in dev, fail in CI when `features` require a surface but site/theme doesn’t provide it.

### M2: Blog UX Defaults (Next)

- Standardize blog list/term layouts in scaffold themes, so blog becomes turnkey.

### M2.5: SEO Parity Foundation (Next)

- Expand `seo-doctor` with richer fix hints and snippet-style scoring ergonomics.
- Add SEO title/description template token resolution and preview artifacts. (completed)
- Expand structured data profiles for docs/product/news use cases.
- Add specialized sitemap family (news/images/videos) and sitemap index output support.
- Add crawl-policy model for explicit discovery controls. (completed)

### M3: DocFX-class Docs Conveniences (Later)

- XRef/cross-reference graph hardening (symbol graph parity, richer API UID generation, and template/runtime exposure).
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

