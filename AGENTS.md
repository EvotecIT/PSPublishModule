# Agent Guide (PSPublishModule / PowerForge.Web + Websites)

Last updated: 2026-02-10

This file is the "start here" context for any agent working on the PowerForge.Web engine and the three websites that use it.

## Repos / Local Paths

These are the maintainer's default Windows paths (safe to assume in this workspace):

- Engine (this repo): `C:\Support\GitHub\PSPublishModule`
  - Web engine: `PowerForge.Web\`
  - Web CLI: `PowerForge.Web.Cli\`
  - Web docs: `Docs\PowerForge.Web.*.md`
  - PowerShell module: `PSPublishModule\` (+ packaging under `Module\`)
  - Core .NET libs/CLI: `PowerForge\`, `PowerForge.Cli\`

- HtmlForgeX website: `C:\Support\GitHub\HtmlForgeX.Website`
  - Remote: `https://github.com/EvotecIT/HtmlForgeX.Website.git` (currently `origin`)

- IntelligenceX website: `C:\Support\GitHub\IntelligenceX\Website`
  - Remote: `https://github.com/EvotecIT/IntelligenceX.git`

- CodeGlyphX website: `C:\Support\GitHub\CodeMatrix\Website`
  - Remote: `https://github.com/EvotecIT/CodeGlyphX.git`

## Portable Path Discovery (WSL/macOS/Linux)

If you're not on Windows or you don't have `C:\Support\GitHub`, use this layout heuristic:

- Common layout: the repos are siblings under one parent folder:
  - `<root>/PSPublishModule`
  - `<root>/HtmlForgeX.Website`
  - `<root>/IntelligenceX/Website`
  - `<root>/CodeMatrix/Website`

Practical search strategy:

1. Start at the current repo root (where this `AGENTS.md` lives).
2. Check `..` (parent) and `../..` (grandparent) for sibling repo folders above.
3. If you're in WSL, Windows drives usually live under `/mnt/c`:
   - Example: `/mnt/c/Support/GitHub/PSPublishModule`

Recommended environment variable (makes site scripts deterministic):

- Set `POWERFORGE_ROOT` to the engine repo root (path to `PSPublishModule`).
  - Website `build.ps1` scripts prefer `POWERFORGE_ROOT` when resolving `PowerForge.Web.Cli`.

## What To Read First (Canonical)

1. `Docs\PowerForge.Web.Roadmap.md` (inventory: Have/Partial/Missing + milestones)
2. `Docs\PowerForge.Web.AgentHandoff.md` (high-signal handoff + commands)
3. `Docs\PowerForge.Web.QualityGates.md` (CI/dev contract, baselines, budgets)
4. `Docs\PowerForge.Web.WebsiteStarter.md` (golden path for building new sites without surprises)
5. `Docs\PowerForge.Web.Parity.md` (rough parity notes vs DocFX/Hugo/Astro/etc.)

Reference docs (as needed):
- `Docs\PowerForge.Web.ContentSpec.md` (content model + navigation)
- `Docs\PowerForge.Web.Theme.md` (theme anatomy + shortcodes)
- `Docs\PowerForge.Web.Pipeline.md` (pipeline tasks)
- `Docs\PowerForge.Web.ApiDocs.md` (API generator)
- `Docs\PSPublishModule.ProjectBuild.md` (PowerShell module build/publish pipeline)

## Repo Skills (.agents/skills)

This repo ships agent skills under `.agents/skills` so new contributors/agents don't
need per-user global skill installs.

- Website scaffolding skill: `.agents/skills/powerforge-website-starter`

## Working Agreements (Best Practices)

- Prefer engine fixes over theme hacks when the same issue can recur across sites.
- CI/release should fail on regressions; dev should warn and summarize:
  - Verify: use baselines + `failOnNewWarnings:true` in CI.
  - Audit: use baselines + `failOnNewIssues:true` in CI.
- Prefer stable theme helpers over ad-hoc rendering:
  - Scriban: use `pf.nav_links` / `pf.nav_actions` / `pf.menu_tree` (avoid `navigation.menus[0]`).
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

## Commands (PowerShell Module)

- Module lives under `PSPublishModule\` (plus packaging helpers under `Module\`).
- The PowerForge.Web engine is .NET; the PSPublishModule is the PowerShell-facing surface.

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
- Website quality gates (baselines/budgets/CI-vs-dev pattern) were merged to the default branches
  of the three site repos. Avoid long-lived "quality-gates" branches; prefer small PR branches
  that get merged and deleted.
