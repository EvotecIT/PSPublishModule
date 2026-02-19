# PowerForge.Web Quality Gates (Recommended Defaults)

This document captures recommended, low-surprise defaults for running PowerForge.Web across multiple sites with consistent CI behavior.

## Goals

- Dev loops should be fast and informative (warnings allowed, limited previews).
- CI should be strict but stable (fail only on *new* regressions).
- Themes should be contract-driven (feature flags + required fragments + CSS selector checks).
- Output should not silently bloat (simple budgets like total file count).

## Recommended Pipeline Pattern (Dev + CI In One File)

Use a single `pipeline.json` and drive strictness via `--mode`.

Run locally:
- `powerforge-web pipeline --mode dev`

Run in CI:
- `powerforge-web pipeline --mode ci`

Example `pipeline.json`:
```json
{
  "$schema": "./Schemas/powerforge.web.pipelinespec.schema.json",
  "steps": [
    { "task": "build", "config": "./site.json", "out": "./_site", "clean": true },

    {
      "task": "verify",
      "id": "verify-dev",
      "config": "./site.json",
      "skipModes": ["ci"],
      "warningPreviewCount": 5
    },
    {
      "task": "verify",
      "id": "verify-ci",
      "config": "./site.json",
      "modes": ["ci"],
      "baseline": "./.powerforge/verify-baseline.json",
      "failOnNewWarnings": true,
      "failOnNavLint": true,
      "failOnThemeContract": true,
      "warningPreviewCount": 10,
      "errorPreviewCount": 10
    },

    {
      "task": "audit",
      "id": "audit-ci",
      "modes": ["ci"],
      "siteRoot": "./_site",
      "summary": true,
      "sarif": true,
      "baseline": "./.powerforge/audit-baseline.json",
      "failOnNewIssues": true,
      "maxTotalFiles": 2000,
      "failOnCategories": "budget",
      "failOnIssueCodes": "media-img-dimensions,heading-order,head-render-blocking"
    }
  ]
}
```

Notes:
- `verify` baselines are stored under `./.powerforge/verify-baseline.json` by default when generated from the CLI.
- `audit` already supports baselines (`failOnNewIssues`) and can gate on categories (useful for budgets).
- Use `failOnIssueCodes` for surgical CI blocking when only specific warnings should fail the build.
- Use `suppressWarnings` as a scalpel; prefer baselines for "existing debt".
- Use `suppressIssues` (audit) as a scalpel too (e.g. `PFAUDIT.BUDGET`), but prefer baselines for existing debt and `failOnCategories` for enforceable budgets.
- Verify baseline keys strip any leading `[CODE]` prefix for stability (so adding/changing warning codes does not break baselines).
- An empty verify baseline (0 keys) is valid and enables `failOnNewWarnings` semantics ("any warning is new").
- Verify markdown hygiene now flags multiline HTML media tags (`img`/`iframe`/etc.) because they can render as escaped text in output; keep media tags single-line or use markdown/shortcodes.

## Creating/Updating Baselines

Verify baseline (creates `./.powerforge/verify-baseline.json`):
```powershell
powerforge-web verify --config .\site.json --baseline-generate
```

Audit baseline (creates `./.powerforge/audit-baseline.json` by default):
```powershell
powerforge-web audit --site-root .\_site --baseline .\.powerforge\audit-baseline.json --baseline-generate
```

## Common Rules Of Thumb

- Prefer `failOnNewWarnings` (verify) and `failOnNewIssues` (audit) in CI.
- Prefer strict theme contracts over "it renders ok on one site":
  - `theme.manifest.json` should declare `features` and `featureContracts`.
  - For `apiDocs`, include required fragments and a CSS selector contract.
- With `failOnNavLint:true`, keep `Navigation.Surfaces` explicit for feature-heavy sites (`docs`, `apiDocs`) so nav contracts fail early instead of drifting across page types.
- Add budgets only when you can defend them:
  - `maxTotalFiles` is a simple early warning for accidental output explosion.
  - Sites with API references can legitimately exceed 800 files (per-type pages add up quickly). Set a budget that reflects the site's expected scale (example: 2000-5000) or exclude known large outputs from budgets via `budgetExclude` (for example `api/**`).

## Compatibility Lock (Prevent Silent Default Drift)

If you want CI behavior to stay frozen across engine updates, enable:

- `requireExplicitChecks: true` on `audit`/`doctor` steps

When enabled, the step fails fast unless these checks are explicitly set in the step:

- `checkSeoMeta`
- `checkNetworkHints`
- `checkRenderBlockingResources`
- `checkHeadingOrder`
- `checkLinkPurposeConsistency`
- `checkMediaEmbeds`

Example:
```json
{
  "task": "audit",
  "siteRoot": "./_site",
  "requireExplicitChecks": true,
  "checkSeoMeta": false,
  "checkNetworkHints": true,
  "checkRenderBlockingResources": true,
  "checkHeadingOrder": true,
  "checkLinkPurposeConsistency": true,
  "checkMediaEmbeds": true
}
```
