# PowerForge.Web Agent Handoff (Websites Engine)

Last updated: 2026-03-03

This doc is a short, high-signal handoff for an agent working on the PowerForge-powered websites engine.
Scope for ongoing work (per maintainer request): **PowerForge/PSPublishModule**, **CodeGlyphX**, **HtmlForgeX Website**, **IntelligenceX Website**.

Start here:
- `AGENTS.md` (repo + website paths and working agreements)
- `Docs\PowerForge.Web.Roadmap.md` (Have/Partial/Missing + milestones)
- `Docs\PowerForge.Web.QualityGates.md` (CI/dev contract pattern)
- `Docs\PowerForge.Web.WarningCodes.md` (verify/apidocs warning code catalog)
- `Docs\PowerForge.Web.ReleaseChannels.md` (stable/candidate/nightly rollout + canary model)
- `Docs\PowerForge.Web.ReleaseHub.md` (downloads/changelog/release asset model + helper contract)
- `C:\Support\GitHub\Website\Docs\AgentHandoff.md` (Evotec website implementation status and next steps)

## Repos / Paths

- PowerForge engine (source): `C:\Support\GitHub\PSPublishModule`
  - Web CLI: `PowerForge.Web.Cli`
  - Web engine: `PowerForge.Web`
  - Docs: `Docs\PowerForge.Web.*.md`
- CodeGlyphX repo (website + library): `C:\Support\GitHub\CodeMatrix` (remote: `EvotecIT/CodeGlyphX`)
  - Website: `C:\Support\GitHub\CodeMatrix\Website`
- IntelligenceX repo (website): `C:\Support\GitHub\IntelligenceX\Website`
- HtmlForgeX repo (website path): `C:\Support\GitHub\HtmlForgeX\Website`
  - Use this location for all HtmlForgeX website work.
- Evotec main website repo: `C:\Support\GitHub\Website` (remote: `EvotecIT/Website`)

## Recent Changes (2026-02-09)

- Audit baselines can live under repo root (for example `./.powerforge/audit-baseline.json`) instead of under `_site`.
- Audit baselines use hashed issue keys to avoid huge baseline files that fail to load.
- Verify baselines support an "empty baseline" (0 keys) for `failOnNewWarnings` (baseline present = contract is enabled).
- Windows CSS contract check: root-relative hrefs like `/css/app.css` are treated as web-root paths (not disk-rooted paths).

## Recent Changes (2026-03-03)

- Added localization fallback materialization:
  - `localization.materializeFallbackPages: true` now allows serving default-language content under localized routes when translation is missing.
  - This is intended for phased rollouts (for example EN content under `/pl/`, `/de/`, `/fr/`, `/es/` until translation is available).
- Canonical/feed/output absolute URL generation is now language-aware:
  - URLs resolve against per-language `localization.languages[].baseUrl` when configured.
- Taxonomy and term list resolution now respects page language, preventing cross-language leakage on localized taxonomy pages.
- Pipeline runner task filtering (`--only` / `--skip`) now supports both `task` and step `id` matching.
- Added native pipeline tasks:
  - `ecosystem-stats`
  - `search-index-export` (exports compatibility search index payload from built `/search/index.json`, with optional truncation and summary output).
- `project-docs-sync` (catalog/surfaces-driven docs copy into `content/docs/<slug>` plus optional API artifact sync into `data/apidocs/<slug>`, with optional TOC generation and run summary).
  - `project-catalog` (manifest import, optional curation CSV apply, validation, project/section markdown generation, static catalog publish).
  - `apache-redirects` (CSV-based legacy URL map to Apache rewrite include generation for migration/SEO continuity).
  - `wordpress-normalize` (imported WordPress markdown cleanup and canonical slug filename normalization, with run summary output).
  - `wordpress-media-sync` (imported markdown media URL rewrite/local sync + image/iframe hint enrichment, with optional no-download mode).
  - `wordpress-export-snapshot` (public WordPress REST export to language-scoped raw snapshot JSON + manifest/summary artifacts; supports auth modes `auto|none|bearer|basic|header` and query/per-collection filters).
  - `wordpress-import-snapshot` (snapshot-to-markdown import contract with taxonomy fallback, alias/redirect CSV emission, and generated-content protection).
- `project-catalog` telemetry merge:
  - optional stats merge from `statsPath` (`./data/ecosystem/stats.json` by default)
  - writes reusable `metrics` into catalog JSON (`github`, `nuget`, `powerShellGallery`, `downloads`)
  - projects/pages now get richer generated `meta.project_*` fields (links/surfaces/telemetry) for theme rendering.
- `project-catalog` release telemetry merge:
  - optional GitHub latest release fetch per `githubRepo` (`mergeReleaseTelemetry`)
  - supports token and base URL overrides (`githubToken`, `githubTokenEnv`, `githubApiBaseUrl`)
  - writes reusable release metrics (`metrics.release.latestTag/latestPublishedAt/...`) and generated page metadata (`meta.project_release_*`).
- `project-catalog` bootstrap from ecosystem stats:
  - optional creation of missing catalog entries from `statsPath` GitHub repositories (`bootstrapFromStats`)
  - supports bootstrap shaping: `bootstrapTop`, `bootstrapMinimumStars`, `bootstrapIncludeArchived`, `bootstrapExcludeRepos`
  - intended for bootstrap/migration runs while preserving curated/manual fields.
- `project-catalog` contract validation now covers project surfaces/links quality:
  - unknown `surfaces.*` / `links.*` keys
  - unsupported link target format
  - dedicated-external docs/api surfaces missing required links
  - missing source/release/changelog fallbacks for enabled surfaces
  - CI can enforce this via `failOnWarnings: true` while dev runs keep warnings non-blocking.
- Pipeline schema now includes first-class step contracts for:
  - `ecosystem-stats`
  - `project-catalog`
  - `project-catalog` release + bootstrap options (merge/token/base-url/timeout/bootstrap filters).
- Localization route fallback resolver now preserves requested language routes when fallback materialization is enabled:
  - when a translation is missing, alternates/switch links stay on localized URLs (for example `/pl/blog/`, `/de/projects/`) instead of collapsing to default-language paths.
  - this stabilizes hreflang alternates and language switch behavior for mixed translated/fallback content.
- Content model now includes first-class `Categories` alongside `Tags`:
  - parser maps front matter `categories`/`category` into typed fields (while preserving `meta.categories` compatibility),
  - `ContentItem`/pagination/fallback clones carry categories,
  - search index entries now emit `categories[]`,
  - JSON outputs now include categories payloads.
- Evotec website quality preset now includes a CI `seo-doctor` smoke gate (`seo-smoke-ci`) focused on key EN/PL routes, requiring canonical + hreflang (+ x-default) and failing on warnings.
- `ecosystem-stats` fallback hardening:
  - default behavior now preserves existing stats JSON when a refresh returns warnings and empty totals (for example transient API rate limit/offline runs), avoiding accidental overwrite of homepage metrics with zeros.
  - behavior can be disabled per step with `preserveOnWarnings: false`.
- WordPress translation-group hardening:
  - `wordpress-import-snapshot` now resolves translation groups from WordPress `translations` metadata when present, so cross-language variants share one `translation_key` even when IDs/slugs differ.
  - `wordpress-import-snapshot` and `wordpress-normalize` now support translation key override maps via:
    - `translationKeyMapPath` / `translation-key-map-path`
    - `translationMapPath` / `translation-map-path`
    - `translationOverridesPath` / `translation-overrides-path`
  - this closes the language-switch/hreflang gap for legacy multilingual posts where source systems used different IDs per language.
- Native localization routing/alias contract hardening:
  - Front matter now supports additional translation key aliases (`translationKey`, `i18n.translation_key`, `i18n.translationKey`, `i18n.group`).
  - Front matter parser now supports both dot-notation and nested YAML blocks for localization metadata:
    - `i18n: { group/language/aliases.<lang> }`
    - `translations: { <lang>: { route|url|path, aliases } }`
  - Language switch/hreflang can be explicitly pinned per language with:
    - `i18n.routes.<lang>` / `i18n.route.<lang>`
    - aliases: `i18n.urls.<lang>`, `i18n.url.<lang>`, `translations.<lang>.route|url|path`
  - Per-language legacy aliases are supported for redirects:
    - `i18n.aliases.<lang>`, `aliases.<lang>`, `translations.<lang>.aliases`
    - plus shared aliases `i18n.aliases.default|all`, `aliases.default|all`.

## Current Capabilities (What Exists)

### Pipeline / build UX

- CLI supports a pipeline model with steps like `build`, `verify`, `apidocs`, `llms`, `sitemap`, `optimize`, `audit`.
- There is support for running a lighter dev loop by skipping heavy steps via pipeline configuration (for example `skipModes: ["dev"]` on steps).
- Web pipeline supports watch mode (`--watch`) to re-run a subset of steps when inputs change.

### Navigation model (important for multi-product sites)

PowerForge navigation is more capable than most themes currently render:

- Nested menus: `Navigation.Menus[].Items[].Items[]...`
- Mega menus: menu items can define `Sections`/`Columns` (see `Docs\PowerForge.Web.ContentSpec.md`).
- Regions: `Navigation.Regions` provides named slots like `header.left`, `header.right`, `mobile.drawer`.
- Profiles: `Navigation.Profiles` allows route/collection/layout/project-scoped navigation variants (critical for multi-product sites).
- Auto menus: `Navigation.Auto[]` can generate menus from folder structure with `MaxDepth`.

Key doc: `Docs\PowerForge.Web.ContentSpec.md` (`## Navigation` section).

Theme best practice (Scriban):
- Prefer stable, engine-owned helpers over index-based loops:
  - `{{ pf.nav_links "main" }}` for top nav links
  - `{{ pf.nav_actions }}` for header actions
  - `{{ pf.menu_tree "docs" 4 }}` for nested sidebar trees

### API docs

- API docs generator supports optional source links:
  - `sourceRoot` + `sourceUrl` pattern (requires PDB/source info).
  - Broken "Edit on GitHub" links typically mean `sourceRoot`/`sourceUrl` aren't aligned with repo layout (monorepo/subfolder cases).
- Pipeline `apidocs` step tries to keep navigation consistent by default:
  - if `config` is not set, it will use `./site.json` when present at the pipeline root (prevents missing nav/theme drift)
  - if `headerHtml`/`footerHtml` are not set, it prefers theme `partials/api-header.html` + `api-footer.html`
  - if those are missing, it falls back to theme `partials/header.html` + `footer.html` (so API reference pages don't lose site nav)
Key doc: `Docs\PowerForge.Web.ApiDocs.md`.

### Verify / lint

- Verify includes: navigation lint, theme contract checks, not-found checks, markdown hygiene warnings.
- There is a markdown hygiene fixer step (`markdown-fix`) (dry-run/apply, optional `failOnChanges`, JSON report, markdown summary).
- Verify supports baselines to keep CI stable while fixing legacy warnings:
  - `powerforge-web verify --baseline-generate` writes `./.powerforge/verify-baseline.json` by default
  - `--fail-on-new` / `failOnNewWarnings` fails only on newly introduced verify warnings
- Verify baselines normalize keys by stripping any leading `[CODE]` prefix (so adding/changing warning codes does not break baselines).

### Audit / budgets

- Audit supports:
  - baselines (`.powerforge/audit-baseline.json`) + `failOnNewIssues`
  - output budgets (example: `maxTotalFiles` + `failOnCategories: "budget"`)
  - issue suppression via `suppressIssues` (patterns/codes like `PFAUDIT.BUDGET`)
- Audit output is designed to be high-signal on failures:
  - compact context line (files/pages/nav/warnings/new)
  - error samples + warning samples
  - category summary (top issue categories)
- Missing navigation is aggregated into a single warning with count + sample pages (prevents log spam on large sites).

### Audit artifacts path bug (fixed upstream)

Symptom (HtmlForgeX Website):
`audit: Path must resolve under site root: C:\Support\GitHub\HtmlForgeX\Website\.powerforge\audit.sarif`

Fix:
- PSPublishModule PR #89 merged to `origin/main` (commit `60bf13a`).
- It changes default fallback paths for audit artifacts to **relative** paths:
  - `.powerforge/audit-summary.json`
  - `.powerforge/audit.sarif.json`

Action:
- Ensure the website build uses a PSPublishModule checkout that includes that commit (or newer).

## Known Gaps / Pain Points (As Observed)

### 1) Theme rendering makes everything feel the same

Even with `Profiles`/`Regions`/mega-menu support, themes tend to render:
- flat top nav
- flat left sidebar
- similar footer

This is primarily a theme-side limitation, not an engine limitation.

### 2) Audit/verify failure output is hard to diagnose

Pipeline currently summarizes failures, but often does not surface enough context (which file/URL, which rule) without opening artifacts.

Status:
- Mostly addressed (compact failure summaries + samples + category summaries).
Remaining:
- Print resolved absolute paths for summary/SARIF on failures (helpful for CI log users).

### 3) Website build performance in dev loops

`optimize` and `audit` are expensive on large sites.

Current recommended approach:
- In `pipeline.json`, mark heavy steps with `skipModes: ["dev"]` and run `-Dev` / dev mode.

Target improvement:
- Better incremental caching and a well-defined "dev contract" (what is skipped, what is always run).

### 4) Source links in API pages can be wrong in monorepo/subfolder layouts

Example pattern:
- repo contains project under a subfolder (e.g. `IntelligenceX/...`)
- source link generated as if repo root == project root

Target improvement:
- better defaults + docs + explicit config knobs in pipeline/site.json.

### 5) Image optimization

Optimize currently focuses on HTML/CSS/JS + critical CSS; it does not (yet) resize/recompress images.

Target improvement:
- optional pipeline step for image optimization (e.g. Magick.NET) with a report of bytes saved.

## Repro / Commands (Typical)

From a website repo:

- Serve + watch (fast dev loop):
  - `.\build.ps1 -Serve -Watch -Dev`
- Full production pipeline:
  - `.\build.ps1`

## Next Tasks (If You Pick Up Work)

1. Improve `wordpress-normalize` parity (strict HTML conversion/table/list/link handling) and add focused regression tests from real migrated pages.
2. Add optional maintenance preset examples using `wordpress-export-snapshot` + `wordpress-import-snapshot` contracts (auth env + query filters) so website repos do not re-invent export workflows.
3. HtmlForgeX: confirm audit + CI mode gates pass end-to-end with baselines and `--mode ci` (verify + audit).
4. Improve audit failure output in `PowerForge.Web.Cli` (print resolved absolute artifact paths + top issues with URL/path).
5. Add docs/examples for multi-product IA using `Navigation.Profiles` + `Regions` (goal: help Claude/agents create unique sites).

## Latest Update (2026-03-03): Scriban Editorial Localization

Engine changes in `PowerForge.Web/Services/ScribanThemeHelpers.cs`:
- `pf.editorial_post_nav` now filters collection candidates to the current language before computing back/newer/older/related links.
- `ResolveCollectionHref` now applies current language routing so back-links are localized.
- Taxonomy chip links now route through language-aware base path resolution (`BuildTaxonomyTermHref` + `ResolveTaxonomyBasePath`).
- Added language-aware route handling for both prefixed routes (`/pl/...`) and language-as-root contexts.

Regression coverage in `PowerForge.Tests/ScribanPfNavigationHelpersTests.cs`:
- `Build_RendersPfEditorialCards_WithLocalizedTaxonomyLinks`
- `Build_RendersPfEditorialPostNav_WithinCurrentLanguageOnly`

Verification:
- `dotnet test .\PowerForge.Tests\PowerForge.Tests.csproj -c Release --filter "FullyQualifiedName~ScribanPfNavigationHelpersTests"`
- Result: Passed (`7` tests).
