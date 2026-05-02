# PowerForge.Web Website Starter (Golden Path)

Last updated: 2026-02-19

This is the canonical, short "how we build sites" document meant to prevent:

- half-cooked themes,
- silent fallbacks,
- regressions across multiple websites,
- and agent guesswork.

This is compatible with both "standalone themes" and "themes that extend a vendored base theme".

## Non-Negotiables (Best Practices)

- Always set `Features` explicitly in `site.json`.
- Always use a theme manifest (`theme.manifest.json` preferred; legacy `theme.json` allowed).
- Always run with two modes:
  - dev: warn, summarize, stay fast
  - ci/release: fail on new issues, enforce budgets
- For Linux-hosted static sites, follow `Docs\PowerForge.Web.LinuxDeployment.md` so hourly deploy checks are commit-driven and reusable across sites.
- Always make the performance path explicit:
  - add `optimize` to CI/release pipelines with explicit `minifyHtml`, `minifyCss`, and `minifyJs`
  - choose `assetRegistry.cssStrategy` intentionally:
    - `blocking` when the shell must never flash unstyled during hard navigations or reloads
    - `preload` when critical CSS is solid and you want a softer non-blocking path
    - `async` only when the theme's critical CSS truly covers the first paint
  - prefer `Head.Links` for fonts/preconnects instead of hiding `@import` font loads inside inline theme CSS
  - when a site needs first-party copies of remote fonts/CSS, prefer `AssetPolicy.Rewrites` with HTTPS `SourceUrl`, `SourceUrlAllowedHosts`, and `DownloadDependencies:true`
- Always keep escape hatches scoped:
  - baselines for legacy noise
  - do not blanket-ignore whole categories without a written reason
- Always prefer stable, engine-owned theme helpers over ad-hoc rendering:
  - Scriban: use `pf.nav_links` / `pf.nav_actions` / `pf.menu_tree` (avoid `navigation.menus[0]`).

## Step-by-Step

1. Create/confirm these files exist at repo root:
   - `site.json`
   - `pipeline.json`
   - `pipeline.maintenance.json`
   - `.github/workflows/website-ci.yml`
   - `.github/workflows/website-maintenance.yml`
   - `config/presets/pipeline.web-quality.json`
   - `config/presets/pipeline.web-maintenance.json`
   - `.powerforge/verify-baseline.json`
   - `.powerforge/audit-baseline.json`
2. `site.json`:
   - set `DefaultTheme`

- set `Features` explicitly (for example `["docs","blog","news","apiDocs","search"]`)
- define navigation menus + actions
- define `Navigation.Surfaces` explicitly (canonical: `main`, `docs`, `apidocs`, optional `products`; `api` is treated as an alias of `apidocs`) to keep docs/API navigation deterministic
- configure quality gates:
  - verify: `baseline`, `failOnNewWarnings`
  - audit: `baseline`, `failOnNewIssues`, budgets (`maxTotalFiles`, etc.)

3. `pipeline.json`:
   - keep root `pipeline.json` small and inherit shared defaults via `extends`
   - put shared build/verify/audit/cache/profile defaults in `config/presets/pipeline.web-quality.json`
   - for CI `audit`/`doctor` steps, lock compatibility-sensitive defaults:
     - `requireExplicitChecks: true`
     - explicitly set:
       - `checkSeoMeta`
       - `checkNetworkHints`
       - `checkRenderBlockingResources`
       - `checkHeadingOrder`
       - `checkLinkPurposeConsistency`
       - `checkMediaEmbeds`
   - if your site depends on content/projects sourced from other repos, declare them under `Sources` in `site.json`
     and run `sources-sync` before `build` (or use `powerforge-web build --sync-sources` locally)
   - run `build` + `verify` in all modes
   - run `sitemap` after `build` so XML/JSON sitemap artifacts stay in sync with generated routes
   - run `engine-lock` verify in CI so missing/invalid `.powerforge/engine-lock.json` fails fast (prefer `requireImmutableRef:true`)
   - run heavy steps only in CI (`modes:["ci"]`):
     - `audit` (and rendered checks if enabled)
     - `optimize`
     - `indexnow` (recommended with `optionalKey:true` so missing secrets skip safely in forks/dev)
     - `github-artifacts-prune` (recommended `dryRun:true` + `optional:true`; flip to `apply:true` in scheduled maintenance runs)
   - keep audit media tuning in a reusable `./config/media-profiles.json` file and reference it via `mediaProfiles` on `audit`/`doctor`
4. CI workflow:
   - keep workflow YAML thin; normal binary consumers should usually pin only `powerforge_web_tag`
   - keep `.powerforge/*` for committed site policy and baselines, not workflow implementation logic
   - keep `.powerforge/engine-lock.json` committed only when the site intentionally uses source mode (prefer immutable SHA in `ref`)
   - default scaffolder workflow is a thin wrapper around `EvotecIT/PSPublishModule/.github/workflows/powerforge-website-ci.yml`
   - default scaffolder maintenance workflow is a thin wrapper around `EvotecIT/PSPublishModule/.github/workflows/powerforge-website-maintenance.yml`
   - reusable workflow resolves engine checkout from `POWERFORGE_LOCK_PATH` or explicit repo/ref inputs
   - scaffolded workflow fails early when lock/override `ref` is not a full commit SHA (40/64 hex)
   - optional canary override: set GitHub vars `POWERFORGE_REPOSITORY` / `POWERFORGE_REF` without editing lock file
   - upload `_reports` artifacts on every run (`if: always()`) to make regressions debuggable.
   - optional scheduled workflow: run `pipeline.maintenance.json` weekly for storage hygiene (`github-artifacts-prune` with `apply:true` + safe caps)
   - scaffold maintenance caps intentionally with `powerforge-web scaffold --maintenance-profile conservative|balanced|aggressive` (default: `balanced`)
5. Theme manifest (`theme.manifest.json` recommended):
   - set `schemaVersion: 2`
   - declare `features`
   - define `featureContracts` for drift-prone features:
     - `apiDocs`: required `api-header/api-footer/theme-tokens` and required CSS selectors
     - `docs`: required `docs` layout and required slots/partials
   - for Scriban editorial layouts (`blog`/`news`), prefer `{{ pf.editorial_cards ... }}` + `{{ pf.editorial_pager ... }}` in list layouts so verify can detect regressions early
   - `pf.editorial_cards` supports variants (`default`, `compact`, `hero`, `featured`) plus optional `grid_class`/`card_class` overrides so themes can evolve layout style without duplicating list rendering loops
   - if you define `featureContracts.blog` / `featureContracts.news`, include selectors for the variants/override classes used by `pf.editorial_cards` in `requiredCssSelectors`
6. Layout hooks:
   - layouts must include:
     - `{{ assets.critical_css_html }}`
     - `{{ include "theme-tokens" }}`
     - `{{ extra_css_html }}`
     - `{{ assets.css_html }}`
     - `{{ assets.js_html }}`
     - `{{ extra_scripts_html }}`
7. API docs rule:
   - API pages must use the same global CSS as normal pages plus API-specific CSS.
   - If the theme defines tokens, API docs must receive the same token partial/context as normal layouts so width, color, and spacing variables stay aligned.
   - Use multi-css in apidocs: `"/css/app.css,/css/api.css"`.
   - If your site uses `Navigation.Profiles` and you want API pages to select an `/api/` profile override for nav token injection, set `navContextPath: "/api/"` on the apidocs pipeline step.
8. Project examples rule:
   - Treat examples as a curated project surface, not an automatic dump of every repo script.
   - For public project hubs, prefer examples authored in:
     - `Website/content/examples`
     - `content/examples`
   - Only ingest raw `Examples/` folders when the site explicitly wants fallback harvesting and the repo is known to be tidy enough for public display.
   - Keep `surfaces.examples` disabled until a project has intentional example pages ready for the site.
   - After changing/removing public example routes, run a clean build or CI-mode pipeline before inspecting `_site`; fast incremental builds may keep stale generated pages.
   - If examples are enabled, verify the generated routes and the project metadata (`project_surface_examples`, `project_link_examples`, `project_local_examples_available`) after sync.

## Multi-Project API Suite Starter

If a site publishes APIs for multiple related projects or modules, prefer one engine-owned suite portal over several disconnected API roots.

PowerForge now scaffolds this shape directly:

```powershell
powerforge-web scaffold --out .\Website --engine scriban --starter-profile multi-project-api-suite
```

To promote the starter into a real first project immediately:

```powershell
powerforge-web scaffold --out .\Website --engine scriban --starter-profile multi-project-api-suite --suite-project-slug testimox --suite-project-name "TestimoX" --suite-project-surface powershell
```

That seeded project also gets source staging that matches `project-apidocs` discovery:

- PowerShell seed: `projects-sources/<slug>/powershell/` plus `examples/`
- .NET seed: `projects-sources/<slug>/dotnet/`

To avoid false-positive discovery, scaffolded starter files live under `templates/` and use non-discoverable names until you replace them with real inputs.

The scaffold also adds promotion helpers inside the seeded source folder:

- PowerShell: `promote-from-templates.ps1`
- .NET: `promote-from-build.ps1`

That starter creates:

- `data/projects/catalog.json`
- `data/projects/catalog.project-template.json`
- `data/projects/api-suite-narrative.json`
- `data/projects/sample-project-api-guides.json`
- `content/docs/projects/api-guide-template.md`
- `projects-sources/README.md`
- `themes/<theme>/partials/api-header.html`
- `themes/<theme>/partials/api-footer.html`
- `themes/<theme>/assets/api.css`
- a placeholder `/projects/api-suite/` page that `project-apidocs` can replace once real APIs are generated

Starter adoption note:

- `project-apidocs` now emits `[PFWEB.APIDOCS.SUITE]` recommendations when the suite scaffold is still untouched or when sample placeholders like `sample-project` / `sample-project-api-guides.json` leak into the real catalog.

Recommended contract:

- In `pipeline.json`, use `project-apidocs`.
- Add `suiteTitle`.
- Add `suiteNarrativeManifest` (or `suiteNarrativeManifests`) so the generated `api-suite/` portal has a `Start Here` section.
- In project catalog entries, add `apiDocs.quickStartTypes` for the main entry points of each project.
- In project catalog entries, add `apiDocs.relatedContentManifest` / `relatedContentManifests` so curated guides/samples can be attached per project.
- In CI, add:
  - `suiteCoverage` + `suiteFailOnCoverage`
  - `suiteNarrative` + `suiteFailOnNarrative`

Minimal pipeline example:

```json
{
  "steps": [
    {
      "task": "project-apidocs",
      "catalog": "./data/projects/catalog.json",
      "sourcesRoot": "./projects-sources",
      "outRoot": "./_site/projects",
      "template": "docs",
      "format": "both",
      "suiteTitle": "Project APIs",
      "suiteNarrativeManifest": "./data/projects/api-suite-narrative.json",
      "suiteCoverage": {
        "minQuickStartRelatedContentPercent": 80,
        "maxQuickStartMissingRelatedContentCount": 1
      },
      "suiteFailOnCoverage": true,
      "suiteNarrative": {
        "requireSummary": true,
        "minSectionCount": 1,
        "minSuiteEntryCoveragePercent": 100
      },
      "suiteFailOnNarrative": true
    }
  ]
}
```

Minimal catalog example:

```json
{
  "projects": [
    {
      "slug": "testimox",
      "name": "TestimoX",
      "hubPath": "/projects/testimox/",
      "surfaces": {
        "apiPowerShell": true
      },
      "apiDocs": {
        "quickStartTypes": "Invoke-TestimoXAction",
        "relatedContentManifest": "./data/projects/testimox-api-guides.json"
      }
    }
  ]
}
```

Minimal suite narrative example:

```json
{
  "summary": "Use this suite portal to choose the right API and follow the main onboarding flow.",
  "sections": [
    {
      "title": "Start Here",
      "summary": "Begin with the primary automation project, then move into supporting APIs.",
      "items": [
        {
          "title": "Open the TestimoX quick start",
          "url": "/projects/testimox/docs/quick-start/",
          "kind": "workflow",
          "audience": "New maintainers",
          "estimatedTime": "10 min",
          "projects": [ "testimox" ]
        }
      ]
    }
  ]
}
```

## Repo Sources (Optional, Recommended When You Depend On Other Repos)

If your site needs content/projects from other repositories (public or private), declare them in `site.json` under `Sources` and sync them as part of your build.

Best practices:

- Prefer `TokenEnv` (defaults to `GITHUB_TOKEN`) over inline `Token` for private repos.
- In CI, use a lock file: `lockMode: "verify"` and commit `.powerforge/git-sync-lock.json`.
- In dev, you can refresh locks intentionally with `lockMode: "update"`.
- Prefer repo-relative `Destination` values so committed `git-sync-lock.json` entries stay portable across machines and CI runners. Absolute destinations make the lock file machine-specific.
- To migrate older absolute-path locks, run one intentional `lockMode: "update"` pass after switching the source config to repo-relative destinations.
- Keep `build` deterministic:
  - Use `sources-sync` (or `build --sync-sources`) explicitly, rather than auto-downloading inside normal builds.

How destinations work:

- If `Destination` is omitted, PowerForge.Web clones each repo to:
  - `<ProjectsRoot>/<Slug>` (or `./projects/<Slug>` when `ProjectsRoot` is not set).
- If `Slug` is omitted, it is inferred from the repo URL/name.
- If you need a different folder layout, set `Destination`.

Example `site.json` snippet:

```json
{
  "ProjectsRoot": "projects",
  "Sources": [
    {
      "Repo": "EvotecIT/IntelligenceX",
      "Slug": "intelligencex",
      "Ref": "main",
      "AuthType": "token",
      "TokenEnv": "GITHUB_TOKEN"
    }
  ]
}
```

Dev bootstrap (refresh lock intentionally):

```powershell
powerforge-web sources-sync --config ./site.json --lock-mode update --lock-path ./.powerforge/git-sync-lock.json
```

Example `pipeline.json` snippet:

```json
{
  "extends": "./config/presets/pipeline.web-quality.json",
  "steps": [
    {
      "task": "sources-sync",
      "config": "./site.json",
      "lockMode": "verify",
      "lockPath": "./.powerforge/git-sync-lock.json"
    }
  ]
}
```

## Optional: CDN Cache Purge (Cloudflare)

If your site is behind Cloudflare and caches HTML, consider purging key HTML URLs after deploy
(or keeping HTML TTL low). PowerForge.Web provides a small purge command:

- `Docs/PowerForge.Web.Cloudflare.md`

Recommended deploy pattern:

- purge:
  - `powerforge-web cloudflare purge --zone-id <ZONE_ID> --token-env CLOUDFLARE_API_TOKEN --site-config ./site.json`
- verify:
  - `powerforge-web cloudflare verify --site-config ./site.json --warmup 1`

Prefer `--site-config` over hardcoded route lists so route coverage stays aligned with
`Features` and `Navigation` as the site evolves.

## Engine Lock Updates

Upgrade pinned engine ref intentionally:

```powershell
powerforge-web engine-lock --mode update --path .\.powerforge\engine-lock.json --ref <new-sha> --channel stable
```

Verify lock drift (for local checks/scripts):

```powershell
powerforge-web engine-lock --mode verify --path .\.powerforge\engine-lock.json --ref <expected-sha> --require-immutable-ref
```

## Theme Inheritance (extends)

If a theme declares `extends`, the base theme must be present on disk (vendored into the repo).
`powerforge-web verify` warns when `extends` points to a missing base folder to avoid silent fallback.

Recommendation:

- Treat `extends` as a repo-local dependency (vendor base theme under `themes/<base>`).
- Prefer a stable CSS variable namespace (`--pf-*`) rather than coupling variables to a base theme name.

## Agent Handoff Checklist (Required)

In `AGENTS.md` (site repo):

- repo purpose + build commands
- list of enabled `Features`
- theme structure + whether it uses `extends`
- where API docs config lives (apidocs steps + css list)
- where baselines/budgets live and how to update them
