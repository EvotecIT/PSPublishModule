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
   - For PowerShell APIs, wire `psExamplesPath` when you want fallback examples during API generation; do not assume raw `Examples/` should automatically become a public website examples section.
7. Add quality gates with an escape hatch.
   - Use baselines for legacy noise; fail only on new issues in CI.
8. Run locally in dev and ci modes and verify output.
   - Dev should warn, not spam, and stay fast.
   - CI should be deterministic and block regressions.
9. Update agent handoff docs so the next agent knows where everything is.
   - Paths to sibling repos, theme structure, and the site’s “rules”.

## Regression Hotspots

Check these explicitly when reviewing or refactoring an existing site:

- Localization routing:
  - Verify `localization.languages[].baseUrl`, `renderAtRoot`, and menu URLs agree.
  - For languages rendered at their domain root (for example `evotec.pl/kontakt/`), do not keep public menu links under `/<lang>/...`.
  - For localhost preview, check both the deployed public route intent and the generated local route/alias behavior.
- Navigation consistency:
  - Compare header, footer, docs, and API surfaces; do not assume `build -serve` validated them.
  - Prefer running `verify` plus a rendered smoke pass or CI-mode pipeline, not just a fast preview build.
  - When a sync/config change removes routes, do not trust a fast incremental `_site`; run a clean build (CI mode or site helper `-CleanOutput`) before checking rendered output.
- API docs shell parity:
  - Compare `/projects/<slug>/` and `/projects/<slug>/api/` for shared width, spacing, nav alignment, and action rendering.
  - If API header uses `{{NAV_ACTIONS}}`, confirm actions have the right icon/text contract for that theme.
  - Keep `project-docs-sync.apiRoot` and `project-apidocs.apiRoot` aligned so local project tabs do not advertise API routes that the pipeline never generated.
- Markdown hygiene:
  - Watch for `meta.raw_html: true` on pages that still contain Markdown headings/lists.
  - When pages mix components and Markdown, verify the rendered output instead of trusting front matter alone.
- Curated examples:
  - Treat public project examples as an editorial surface, not a raw repo mirror.
  - Prefer `Website/content/examples` or `content/examples` in project repos.
  - Only use raw `Examples/` fallback when the site explicitly wants it and the source repo is tidy enough for public ingestion.
  - Keep `surfaces.examples` off until the examples are intentionally authored and reviewed.
  - After switching from raw examples to curated examples, use a clean build so stale raw example pages are removed from `_site`.
- Localized smoke paths:
  - Add CI audit coverage for key language routes and at least one API page.
  - Use required routes for generated files and rendered smoke pages for user-facing navigation paths.

## Localization Guidance

When editing localized websites:

- localize for meaning, not word-for-word equivalence
- preserve product and platform names such as `PowerShell`, `.NET`, `Entra Connect`, `Active Directory`, `Group Policy`, `GitHub`, and `PowerShell Gallery`
- prefer local-language equivalents for business/process words like `delivery`, `engagement`, `workflow`, `cleanup`, `handoff`, `supportable`, and `output` when the English term is only convenience jargon
- do not over-translate technical labels if the result becomes less natural for native technical readers
- keep visible copy and any `.head.html` FAQ/schema snippets aligned; do not improve one and leave the other stale
- remove SEO-internal phrasing from body copy (`buyer intent`, `search intent`, `shortest summary`, and similar wording)

## Reference Files (Read As Needed)

- `references/blueprint.md`: minimal recommended `site.json`/`pipeline.json`/theme contract patterns.
- `references/theme-contracts.md`: feature contract checklist (docs/apiDocs/blog/search/404).
- `references/agent-prompts.md`: copy/paste prompts for Claude/agents (new site, refactor theme, fix api docs).
