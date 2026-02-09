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
  "$schema": "./schemas/powerforge.web.pipelinespec.schema.json",
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
      "maxTotalFiles": 800,
      "failOnCategories": "budget"
    }
  ]
}
```

Notes:
- `verify` baselines are stored under `./.powerforge/verify-baseline.json` by default when generated from the CLI.
- `audit` already supports baselines (`failOnNewIssues`) and can gate on categories (useful for budgets).
- Use `suppressWarnings` as a scalpel; prefer baselines for "existing debt".
- Use `suppressIssues` (audit) as a scalpel too (e.g. `PFAUDIT.BUDGET`), but prefer baselines for existing debt and `failOnCategories` for enforceable budgets.
- Verify baseline keys strip any leading `[CODE]` prefix for stability (so adding/changing warning codes does not break baselines).
- An empty verify baseline (0 keys) is valid and enables `failOnNewWarnings` semantics ("any warning is new").

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
- Add budgets only when you can defend them:
  - `maxTotalFiles` is a simple early warning for accidental output explosion.
