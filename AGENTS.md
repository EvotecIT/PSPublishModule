# Agent Guide (PSPublishModule / PowerForge.Web + Websites)

Last updated: 2026-02-09

This file is the "start here" context for any agent working on the PowerForge.Web engine and the three websites that use it.

## Repos / Local Paths

- Engine (this repo): `C:\Support\GitHub\PSPublishModule`
  - Web engine: `PowerForge.Web\`
  - Web CLI: `PowerForge.Web.Cli\`
  - Web docs: `Docs\PowerForge.Web.*.md`

- HtmlForgeX website: `C:\Support\GitHub\HtmlForgeX.Website`
  - Remote: `https://github.com/EvotecIT/HtmlForgeX.Website.git` (currently `origin`)

- IntelligenceX website: `C:\Support\GitHub\IntelligenceX\Website`
  - Remote: `https://github.com/EvotecIT/IntelligenceX.git`

- CodeGlyphX website: `C:\Support\GitHub\CodeMatrix\Website`
  - Remote: `https://github.com/EvotecIT/CodeGlyphX.git`

## What To Read First (Canonical)

1. `Docs\PowerForge.Web.Roadmap.md` (inventory: Have/Partial/Missing + milestones)
2. `Docs\PowerForge.Web.AgentHandoff.md` (high-signal handoff + commands)
3. `Docs\PowerForge.Web.QualityGates.md` (CI/dev contract, baselines, budgets)

Reference docs (as needed):
- `Docs\PowerForge.Web.ContentSpec.md` (content model + navigation)
- `Docs\PowerForge.Web.Theme.md` (theme anatomy + shortcodes)
- `Docs\PowerForge.Web.Pipeline.md` (pipeline tasks)
- `Docs\PowerForge.Web.ApiDocs.md` (API generator)

## Working Agreements (Best Practices)

- Prefer engine fixes over theme hacks when the same issue can recur across sites.
- CI/release should fail on regressions; dev should warn and summarize:
  - Verify: use baselines + `failOnNewWarnings:true` in CI.
  - Audit: use baselines + `failOnNewIssues:true` in CI.
- Commit frequently. Avoid "big bang" diffs that mix unrelated changes.

## Quality Gates (Pattern)

Each website should have:
- `site.json` with explicit `Features` (docs/apiDocs/blog/search/notFound as applicable)
- `pipeline.json` with:
  - `verify-ci` step (`modes:["ci"]`) with baseline + `failOnNewWarnings:true`
  - `doctor` or `audit` step with baseline + `failOnNewIssues:true`
  - a simple budget like `maxTotalFiles` to catch output explosions early
- `.powerforge/verify-baseline.json` committed
- `.powerforge/audit-baseline.json` committed
- `.gitignore` ignoring `_site/`, `_temp/`, `_reports/` (keep baselines committed)

## Commands (Engine)

- Tests:
  - `dotnet test .\PSPublishModule.sln -c Release`
- File size discipline (line limit):
  - `node .\Build\linecount.js . 800`

## Commands (Websites)

From a website repo folder:

- Full build:
  - `.\build.ps1`
- Fast dev loop:
  - `.\build.ps1 -Serve -Watch -Dev`
- Run CI-gated steps locally:
  - `powerforge-web pipeline --config .\pipeline.json --mode ci`

Baselines:
- Verify baseline:
  - `powerforge-web verify --config .\site.json --baseline .\.powerforge\verify-baseline.json --baseline-generate`
- Audit baseline:
  - `powerforge-web audit --site-root .\_site --baseline .\.powerforge\audit-baseline.json --baseline-generate`

## Current State (As Of 2026-02-09)

- Engine branch: `feature/web-engine-contracts` contains recent quality-gate and contract hardening work.
- Site branches were created for CI quality gates:
  - IntelligenceX: `chore/quality-gates`
  - CodeGlyphX: `chore/quality-gates`
  - HtmlForgeX: `chore/quality-gates`

