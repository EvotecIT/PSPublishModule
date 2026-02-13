# PowerForge.Web Library Ecosystem Plan

Last updated: 2026-02-13

This plan focuses on making PowerForge.Web a first-class docs engine for:

- C# libraries (single and multi-package),
- PowerShell modules (single and module suites),
- mixed ecosystems that need one coherent website.

It complements `Docs/PowerForge.Web.Roadmap.md` by turning parity goals into implementation slices with direct value for:

- HtmlForgeX.Website
- IntelligenceX Website
- CodeGlyphX Website

## Review Findings (Current Gaps)

1. Unified xref now exists at page/type/cmdlet level, but member-level symbol linking is still partial.
2. Extensibility still requires pipeline workarounds (no stable hook/plugin contract).
3. API docs are strong for C# XML + PowerShell help, but package-level metadata and release context are not first-class.
4. Multi-version docs conventions exist partially, but there is no turnkey "version switcher + compatibility matrix" contract.
5. Search is generated, but ranking and filtering across docs/blog/api/package dimensions are not standardized.

## Recently Landed (2026-02-13)

1. Added site-level xref support:
   - `SiteSpec.Xref` with external map loading, unresolved-link verification, and optional emitted map.
2. Added API xref map emission in `apidocs`:
   - C# and PowerShell API runs now emit `xrefmap.json` payloads by default.
3. Added merge tooling:
   - New CLI/pipeline task `xref-merge` to combine multiple xref maps with duplicate handling and CI warning control.
4. Wired website pipelines:
   - HtmlForgeX, IntelligenceX, and CodeGlyphX now merge API maps into `data/xrefmap.json` and rebuild before LLMS export.
5. Added package/module metadata generation:
   - New pipeline task `package-hub` emits unified library/module JSON from `.csproj` + `.psd1` inputs (frameworks, dependencies, exported commands, module requirements).

## Immediate Next Features (Highest ROI)

### 1) Member-Level Symbol UIDs + Deep Links

- Expand xref map generation from type/cmdlet level to:
  - .NET: methods/properties/events/fields and generic overload signatures.
  - PowerShell: parameters and about-topic sections.
- Add deterministic UID normalization so links stay stable across versions.

Site impact:
- HtmlForgeX and CodeGlyphX can deep-link guides to exact API members.
- IntelligenceX can deep-link command parameter docs in onboarding flows.

### 2) Package/Module Hub Generator (V2)

- Extend `package-hub` beyond raw metadata JSON:
  - emit turnkey markdown/HTML-ready data blocks for release pages.
  - include changelog/release assets and richer compatibility narratives.
  - add version-lifecycle signals (stable/prerelease/deprecated) and migration notes.

Site impact:
- All three websites get consistent release hubs without manual markdown churn.

### 3) Multi-Version Contract + Switcher Data

- Standardize version metadata model and generated data file consumed by theme switcher.
- Verify cross-version xref integrity and canonical URL behavior in CI.

Site impact:
- Safer major-version rollouts for HtmlForgeX and CodeGlyphX.
- Cleaner docs lifecycle and onboarding paths for IntelligenceX.

### 4) Search Facets + Ranking Profiles

- Extend search index with type facets: `docs`, `api.csharp`, `api.powershell`, `blog`, `package`.
- Add site-level ranking profiles and CI snapshots for relevance drift.

Site impact:
- Better discoverability in larger outputs, especially mixed docs/API sites.

## Priority Backlog

## P0 - Foundation (high impact, low regret)

### 1) Unified Symbol Graph + XRef

- Add engine-level symbol identity model for:
  - .NET: assembly/type/member
  - PowerShell: module/command/parameter/about topic
- Emit `xrefmap.json` per build.
- Add link resolver in markdown and templates (for conceptual docs and API pages).

Acceptance:
- Broken xref links are detectable in verify/audit.
- Same xref syntax works across C# and PowerShell docs.

Why it helps sites:
- HtmlForgeX: reliable links from guides to API members.
- IntelligenceX: links from onboarding docs to command/API references.
- CodeGlyphX: deep cross-linking between analyzers, rules, and API types.

### 2) API Provider Contract (Adapter Model)

- Introduce provider interface for API ingestion:
  - `dotnet-xml` (existing)
  - `powershell-help` (existing)
  - future: `openapi`, `typescript`, custom
- Normalize output into shared page/view model so themes do not special-case each provider.

Acceptance:
- Multiple providers can run in one site build.
- Common layouts/partials render provider data without per-site hacks.

Why it helps sites:
- Shared API chrome and templates across all three websites.

### 3) Local CI Parity Command Surface

- Standardize `build.ps1 -CI` across website starters (explicit strict mode locally).
- Keep environment-based CI detection as fallback.

Status:
- Implemented for HtmlForgeX, IntelligenceX, and CodeGlyphX build scripts.

## P1 - Productized Docs Experience

### 4) Package and Module Intelligence Pages

- Add generators for:
  - package/module overview (name, summary, owners, tags)
  - version table (stable/prerelease/deprecated)
  - dependency matrix
  - target framework / PowerShell edition compatibility
- Inputs:
  - `.csproj`, NuGet metadata, changelog/release notes
  - `.psd1`, help metadata, exported commands

Acceptance:
- One command generates a release-oriented "package hub" section.

Why it helps sites:
- HtmlForgeX/CodeGlyphX: clear package lifecycle and upgrade guidance.
- IntelligenceX: module/feature compatibility made visible for onboarding.

### 5) Versioned Docs Contract

- Define canonical structure for multi-version docs:
  - routing
  - canonical links
  - version switcher data
  - "latest/LTS" aliases
- Add verifier rules for version consistency and broken cross-version links.

Acceptance:
- New version cut requires no theme rewrite.

Why it helps sites:
- All three sites can document breaking changes without navigation drift.

### 6) Search Ranking Profiles

- Extend search output with typed facets and weights:
  - docs, api, blog, package/module pages
- Allow site-configured ranking profiles.

Acceptance:
- Search relevance improvements are measurable and testable in CI snapshots.

Why it helps sites:
- Faster discovery in large outputs (especially HtmlForgeX and CodeGlyphX).

## P2 - Ecosystem Differentiators

### 7) Hook/Plugin Contract

- Stable hooks:
  - pre-discovery
  - post-content-parse
  - pre-render page transform
  - post-build artifact transform
- Isolation model for custom plugins.

Acceptance:
- Common customization scenarios work without forking the engine.

### 8) Starter Presets (One-command project bootstrap)

- Presets:
  - `library-dotnet`
  - `module-powershell`
  - `hybrid-dotnet-powershell`
- Generate site/pipeline/theme contract defaults + baseline files.

Acceptance:
- New repo can ship with strict CI gate in under 10 minutes.

## Near-term Execution Order

1. Expand xref from type/cmdlet to member/parameter granularity.
2. Introduce API provider contract while preserving existing dotnet/powershell generators.
3. Add package/module intelligence pages from project/manifests.
4. Standardize versioned docs contract and search ranking profiles.
5. Land plugin contract after core models are stable.

## Guardrails

- Keep verify/audit in CI as fail-on-new to avoid regressions.
- Prefer engine capabilities over site-specific template hacks.
- Add tests for every new contract surface (paths, routing, outputs, and warnings).
