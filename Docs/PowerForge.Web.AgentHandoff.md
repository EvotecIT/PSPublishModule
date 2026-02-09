# PowerForge.Web Agent Handoff (Websites Engine)

Last updated: 2026-02-09

This doc is a short, high-signal handoff for an agent working on the PowerForge-powered websites engine.
Scope for ongoing work (per maintainer request): **PowerForge/PSPublishModule**, **CodeGlyphX**, **HtmlForgeX.Website**, **IntelligenceX Website**.

Start here:
- `AGENTS.md` (repo + website paths and working agreements)
- `Docs\PowerForge.Web.Roadmap.md` (Have/Partial/Missing + milestones)
- `Docs\PowerForge.Web.QualityGates.md` (CI/dev contract pattern)

## Repos / Paths

- PowerForge engine (source): `C:\Support\GitHub\PSPublishModule`
  - Web CLI: `PowerForge.Web.Cli`
  - Web engine: `PowerForge.Web`
  - Docs: `Docs\PowerForge.Web.*.md`
- CodeGlyphX repo (website + library): `C:\Support\GitHub\CodeMatrix` (remote: `EvotecIT/CodeGlyphX`)
  - Website: `C:\Support\GitHub\CodeMatrix\Website`
- IntelligenceX repo (website): `C:\Support\GitHub\IntelligenceX\Website`
- HtmlForgeX website repo: `C:\Support\GitHub\HtmlForgeX.Website`

## Recent Changes (2026-02-09)

- Audit baselines can live under repo root (for example `./.powerforge/audit-baseline.json`) instead of under `_site`.
- Audit baselines use hashed issue keys to avoid huge baseline files that fail to load.
- Verify baselines support an "empty baseline" (0 keys) for `failOnNewWarnings` (baseline present = contract is enabled).
- Windows CSS contract check: root-relative hrefs like `/css/app.css` are treated as web-root paths (not disk-rooted paths).

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

### API docs

- API docs generator supports optional source links:
  - `sourceRoot` + `sourceUrl` pattern (requires PDB/source info).
  - Broken "Edit on GitHub" links typically mean `sourceRoot`/`sourceUrl` aren't aligned with repo layout (monorepo/subfolder cases).
- Pipeline `apidocs` step tries to keep navigation consistent by default:
  - if `headerHtml`/`footerHtml` are not set, it prefers theme `partials/api-header.html` + `api-footer.html`
  - if those are missing, it falls back to theme `partials/header.html` + `footer.html` (so API reference pages don't lose site nav)
Key doc: `Docs\PowerForge.Web.ApiDocs.md`.

### Verify / lint

- Verify includes: navigation lint, theme contract checks, not-found checks, markdown hygiene warnings.
- There is a markdown hygiene fixer step (`markdown-fix`) (dry-run and apply).
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

Symptom (HtmlForgeX.Website):
`audit: Path must resolve under site root: C:\Support\GitHub\HtmlForgeX.Website\.powerforge\audit.sarif`

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

1. HtmlForgeX: confirm audit + CI mode gates pass end-to-end with baselines and `--mode ci` (verify + audit).
2. Improve audit failure output in `PowerForge.Web.Cli` (print resolved absolute artifact paths + top issues with URL/path).
3. Add docs/examples for multi-product IA using `Navigation.Profiles` + `Regions` (goal: help Claude/agents create unique sites).
4. Fix/standardize API docs source links for monorepo/subfolder repos (IntelligenceX/CodeGlyphX).
5. Add output budgets (example: `audit.maxTotalFiles`) when site outputs start ballooning (keeps surprises down across sites).
