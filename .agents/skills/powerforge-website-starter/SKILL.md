---
name: powerforge-website-starter
description: Create, refactor, or standardize PowerForge.Web websites. Use when setting up or fixing site.json/pipeline.json, dev vs CI modes, baselines and budgets, theme.manifest.json contracts (features/requiredPartials/slots/CSS selectors), navigation consistency, API docs (header/footer + multi-CSS), and agent handoff docs to prevent half-cooked websites and regressions across multiple sites.
---

# PowerForge Website Starter

Follow a deterministic "golden path" so agents can build sites without guessing.

## Golden Path (Do This In Order)

1. Identify the repo root and build outputs.
   - Repo root contains `site.json` and `pipeline.json`.
   - Output is usually `./_site`, temp is `./_temp`.
2. Make the site explicit (avoid inference).
   - In `site.json` set `defaultTheme`, `features`, and navigation.
3. Standardize pipeline modes (dev vs ci).
   - Dev: fast iteration; skip heavy steps.
   - CI/release: strict; fail on new issues; enforce budgets.
4. Make the theme portable and contract-driven.
   - Prefer `theme.manifest.json` with `schemaVersion: 2`.
   - Declare `features` and `featureContracts` (required layouts/partials/slots + CSS selectors).
5. Ensure page layouts include required hooks.
   - Scriban: `{{ extra_css_html }}`, `{{ extra_scripts_html }}`, `{{ assets.css_html }}`, `{{ assets.js_html }}`.
6. Make API docs use site chrome and correct CSS.
   - Provide `api-header`/`api-footer` partials that match the site header/footer structure/classes.
   - Use multi-CSS: `"/css/app.css,/css/api.css"` (or the equivalent for the site).
   - Emit API coverage report (`coverageReport`) so CI can track documentation completeness.
   - For PowerShell APIs, wire `psExamplesPath` (or rely on `Examples/` discovery) to enable fallback examples when help XML is sparse.
7. Add quality gates with an escape hatch.
   - Use baselines for legacy noise; fail only on new issues in CI.
8. Run locally in dev and ci modes and verify output.
   - Dev should warn, not spam, and stay fast.
   - CI should be deterministic and block regressions.
9. Update agent handoff docs so the next agent knows where everything is.
   - Paths to sibling repos, theme structure, and the site’s “rules”.

## Reference Files (Read As Needed)

- `references/blueprint.md`: minimal recommended `site.json`/`pipeline.json`/theme contract patterns.
- `references/theme-contracts.md`: feature contract checklist (docs/apiDocs/blog/search/404).
- `references/agent-prompts.md`: copy/paste prompts for Claude/agents (new site, refactor theme, fix api docs).
