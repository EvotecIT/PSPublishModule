# Blueprint (Minimal, Deterministic)

This file is intentionally small. Use it as a checklist and a source of known-good patterns.

## site.json (recommended ideas)

- Set `features` explicitly to avoid inference drift.
- Keep `defaultTheme` explicit.
- Prefer absolute URLs in nav items.

Example (shape only, adapt per site):

```json
{
  "defaultTheme": "mytheme",
  "features": ["docs", "apiDocs", "search"],
  "trailingSlash": true,
  "navigation": {
    "menus": [{ "name": "main", "items": [{ "title": "Home", "url": "/" }] }],
    "actions": []
  },
  "quality": {
    "verify": { "failOnNewWarnings": true, "baseline": ".powerforge/verify-baseline.json" },
    "audit": { "failOnNewIssues": true, "baseline": ".powerforge/audit-baseline.json", "budgets": { "maxTotalFiles": 3000 } }
  }
}
```

## pipeline.json (dev vs ci)

Pattern:

- `-Dev` / `mode=dev`: skip heavy steps by default (`optimize`, `audit`, rendered checks).
- `mode=ci`: run strict checks and budgets.

Example (shape only):

```json
{
  "cache": true,
  "profile": true,
  "steps": [
    { "task": "build", "id": "build-site", "config": "./site.json", "out": "./_site", "clean": true },
    { "task": "verify", "id": "verify-site", "dependsOn": "build-site", "config": "./site.json" },
    { "task": "audit", "id": "audit-site", "dependsOn": "build-site", "config": "./site.json", "modes": ["ci"] },
    { "task": "optimize", "id": "optimize-site", "dependsOn": "build-site", "config": "./site.json", "out": "./_site", "modes": ["ci"] }
  ]
}
```

## Theme manifest (portable contract)

Prefer `theme.manifest.json` with `schemaVersion: 2` and explicit contracts:

- `features`: declare what the theme supports.
- `featureContracts`: required layouts/partials/slots + CSS contracts for drift-prone features.

Example (shape only):

```json
{
  "$schema": "../../Schemas/powerforge.web.themespec.schema.json",
  "schemaVersion": 2,
  "name": "mytheme",
  "engine": "scriban",
  "defaultLayout": "base",
  "features": ["docs", "apiDocs", "search"],
  "featureContracts": {
    "apiDocs": {
      "requiredPartials": ["api-header", "api-footer"],
      "cssHrefs": ["/css/app.css", "/css/api.css"],
      "requiredCssSelectors": [".pf-api-sidebar", ".pf-api-content"]
    },
    "docs": { "requiredLayouts": ["docs"] }
  }
}
```

## API docs configuration (avoid site chrome drift)

Rule:

- API pages must use the same global CSS as the rest of the site, plus API CSS.
- Set `css` to a list: `"/css/app.css,/css/api.css"`.
- Ensure `api-header`/`api-footer` match the site’s header/footer structure/classes.
- Set `coverageReport` (for example `./_reports/apidocs-coverage.json`) so CI can track doc completeness drift.
- For PowerShell API steps, set `psExamplesPath` (for example `../MyModule/Examples`) to improve examples when help XML is incomplete.
- Add at least one coverage threshold + gate in CI-focused pipelines (for example `minTypeSummaryPercent`, `minPowerShellCodeExamplesPercent`, `failOnCoverage: true`).
