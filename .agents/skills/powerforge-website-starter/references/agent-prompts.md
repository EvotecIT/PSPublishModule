# Agent Prompts (Copy/Paste)

## Refactor HtmlForgeX to "portable contracts + deterministic gates"

You are working in `C:\\Support\\GitHub\\HtmlForgeX.Website`.

Goals:
- No surprises: deterministic dev vs CI behavior.
- Theme portability: contract-driven `theme.manifest.json` schemaVersion 2.
- API docs match site chrome and load global + api CSS.

Tasks:
1. Convert `themes/htmlforgex/theme.json` to `themes/htmlforgex/theme.manifest.json` (keep legacy file only if required).
2. Set `schemaVersion: 2`, `engine: scriban`, `defaultLayout`.
3. Add `features` explicitly and add `featureContracts`:
   - `apiDocs`: requiredPartials `api-header`/`api-footer`, cssHrefs `["/css/app.css","/css/api.css"]`, requiredCssSelectors for the API layout.
   - `docs`: requiredLayouts `["docs"]` and any required slots.
4. Ensure `pipeline.json` apidocs steps use multi-css:
   - `css: "/css/app.css,/css/api.css"`
5. Add API docs quality outputs:
   - `coverageReport: "./_reports/apidocs-<project>-coverage.json"` on each apidocs step.
6. For PowerShell apidocs steps, wire examples fallback source:
   - `psExamplesPath: "../<ModuleRepo>/Module/Examples"`
   - Keep `generatePowerShellFallbackExamples: true` (default) unless explicitly disabled.
7. Ensure `themes/htmlforgex/partials/api-header.html` and `api-footer.html` use the same structural markup/classes as the normal header/footer.
8. Run `./build.ps1 -Dev` and `./build.ps1 -Mode ci` (or the repo's CI equivalent) and fix verify/audit issues without adding new drift.
9. Update docs:
   - add a short "Theme contract + features + apiDocs css" section to the repo’s docs or `AGENTS.md`.

Output:
- commits that are PR-ready (small, scoped commits).
- no inline CSS hacks in api header/footer; rely on shared global CSS.

## Create a new PowerForge.Web site (greenfield)

1. Create `site.json` with:
   - `defaultTheme`
   - `features`
   - navigation menus + actions
   - quality spec: verify/audit baselines and budgets
2. Create `pipeline.json` with modes:
   - dev: build + verify (+ fast tasks)
   - ci: build + verify + audit + optimize (+ budgets)
3. Create `themes/<theme>/theme.manifest.json` schemaVersion 2 with featureContracts.
4. Implement minimal layouts:
   - `base.html` with required hooks (`assets.css_html`, `assets.js_html`, `extra_css_html`, `extra_scripts_html`, `include theme-tokens`)
5. Add `.powerforge/verify-baseline.json` and `.powerforge/audit-baseline.json` (start empty).
6. Run build in dev, then ci mode, and fix until clean.
