# PowerForge.Web Website Starter (Golden Path)

Last updated: 2026-02-09

This is the canonical, short "how we build sites" document meant to prevent:
- half-cooked themes,
- silent fallbacks,
- regressions across multiple websites,
- and agent guesswork.

This is compatible with both "standalone themes" and "themes that extend a vendored base theme".

## Non-Negotiables (Best Practices)

- Always set `features` explicitly in `site.json`.
- Always use a theme manifest (`theme.manifest.json` preferred; legacy `theme.json` allowed).
- Always run with two modes:
  - dev: warn, summarize, stay fast
  - ci/release: fail on new issues, enforce budgets
- Always keep escape hatches scoped:
  - baselines for legacy noise
  - do not blanket-ignore whole categories without a written reason

## Step-by-Step

1. Create/confirm these files exist at repo root:
   - `site.json`
   - `pipeline.json`
   - `.powerforge/verify-baseline.json`
   - `.powerforge/audit-baseline.json`
2. `site.json`:
   - set `defaultTheme`
   - set `features` explicitly (for example `["docs","apiDocs","search"]`)
   - define navigation menus + actions
   - configure quality gates:
     - verify: `baseline`, `failOnNewWarnings`
     - audit: `baseline`, `failOnNewIssues`, budgets (`maxTotalFiles`, etc.)
3. `pipeline.json`:
   - run `build` + `verify` in all modes
   - run heavy steps only in CI (`modes:["ci"]`):
     - `audit` (and rendered checks if enabled)
     - `optimize`
4. Theme manifest (`theme.manifest.json` recommended):
   - set `schemaVersion: 2`
   - declare `features`
   - define `featureContracts` for drift-prone features:
     - `apiDocs`: required `api-header/api-footer` and required CSS selectors
     - `docs`: required `docs` layout and required slots/partials
5. Layout hooks:
   - layouts must include:
     - `{{ assets.critical_css_html }}`
     - `{{ include "theme-tokens" }}`
     - `{{ extra_css_html }}`
     - `{{ assets.css_html }}`
     - `{{ assets.js_html }}`
     - `{{ extra_scripts_html }}`
6. API docs rule:
   - API pages must use the same global CSS as normal pages plus API-specific CSS.
   - Use multi-css in apidocs: `"/css/app.css,/css/api.css"`.

## Theme Inheritance (extends)

If a theme declares `extends`, the base theme must be present on disk (vendored into the repo).
`powerforge-web verify` warns when `extends` points to a missing base folder to avoid silent fallback.

Recommendation:
- Treat `extends` as a repo-local dependency (vendor base theme under `themes/<base>`).
- Prefer a stable CSS variable namespace (`--pf-*`) rather than coupling variables to a base theme name.

## Agent Handoff Checklist (Required)

In `AGENTS.md` (site repo):
- repo purpose + build commands
- list of enabled `features`
- theme structure + whether it uses `extends`
- where API docs config lives (apidocs steps + css list)
- where baselines/budgets live and how to update them

