**PSPublishModule C# Migration and GitHub Actions Adoption**

**Goals**
- Move PSPublishModule behavior into a reusable C# library and thin PowerShell wrappers.
- Make functionality reusable by GitHub Actions and other repos without scripts.
- Replace PlatyPS/HelpOut with a C# help/doc generator.
- Fix reliability: versioned installs, safe formatting, deterministic encodings, no “in use” self-build pain.
- Keep today’s UX (messages, defaults) while improving robustness/performance.
- Enable future tooling (VSCode extension) via stable machine-readable outputs and JSON configuration.

**Guardrails (non-negotiable)**
- All non-trivial logic lives in `PowerForge` (C#) and is reusable from cmdlets + CLI + future VSCode extension.
- `PSPublishModule` cmdlets are thin wrappers: parameter binding + `ShouldProcess` + call a service + emit typed result.
- Keep code typed: POCOs/records + enums + typed results; avoid `Hashtable`/`OrderedDictionary`/`IDictionary` in new public surfaces (allowed only in legacy adapters).
- Cmdlet size budget: keep each cmdlet file ~600–700 LOC max; if it grows, move logic to services and/or split into `partial` (parameters vs execution).
- Do not expose low-level PSResourceGet cmdlets (or “PSPublishResource” cmdlets); use PSResourceGet internally from configuration/publish workflows (like we do with PowerShellGet).
- Must remain `net472` compatible (Windows PowerShell 5.1), plus `net8.0`, and plan for `net10.0` (when available).
- CLI must be machine-friendly (stable JSON output + exit codes + no-color mode) and AOT/trim-friendly for VSCode scenarios.

**Current status (as of 2025-12-26)**
- PSPublishModule is now primarily a binary module: `Module/Public` and `Module/Private` contain no shipped `.ps1` functions (bootstrap `Module/PSPublishModule.psm1` remains, plus `Module/Build/Build-Module.ps1` for compatibility).
- `PowerForge` has typed build/install models + a staging-first build pipeline to avoid self-build file locking.
- `New-Configuration*` cmdlets now emit typed `PowerForge` configuration segment objects (no `OrderedDictionary`/`Hashtable` outputs); the legacy DSL parser accepts both typed segments and legacy dictionaries.
- `PowerForge.Cli` supports `build`/`install` via `--config <json>` and initial machine output via `--output json`, plus `pipeline`/`run` to execute a full typed pipeline spec from JSON (segment array + build/install options).
- `Invoke-ModuleBuild` routes both the simple build path and the legacy DSL (`-Settings {}` / `Build-Module {}`) through the PowerForge pipeline (C#); the legacy PowerShell `Start-*` build scripts were removed.
- `Module/Build/Build-Module.ps1` defaults to CLI staging build; `-Legacy` runs the DSL build path (still C#) for compatibility.
- PowerShell compatibility analysis no longer depends on PowerShell helper functions (moved to C# analyzer).
- PSResourceGet is used internally (out-of-proc wrapper for find/publish/install) and is not exposed as standalone cmdlets.

**Replacement roadmap (priority order)**
- (done) Configuration is typed end-to-end: configuration “segments” are typed models + enums in `PowerForge`, with legacy DSL supported via adapters/translation.
- Re-implement the legacy “one-shot build” features (docs/artefacts/publish) in C# services + CLI, now that the old PowerShell `Start-*` orchestration scripts are removed.
- Move remaining orchestration out of cmdlets into `PowerForge` services and slim cmdlets down to parameter mapping + `ShouldProcess` + typed output.
- Expand CLI contracts (stable JSON schema, `--no-color`, consistent exit codes) and add commands (`docs`, `pack`, `publish`) backed by the same `PowerForge` services.
- Add repository/publish support (NuGet v3 + PSGallery + Azure Artifacts + optional PSResourceGet wrapper) configured via typed models (no secrets logged).
- Add `net10.0` target + validate AOT/trim paths for the CLI (keep PowerShell module multi-targeting without blocking AOT).

**Architecture**
- One namespace per assembly; small set of assemblies:
  - Core library: `PowerForge` (net472 + net8.0 + net10.0) — reusable engine (keep AOT/trim friendly paths)
    - Namespace: `PowerForge`
    - Provides building blocks (IO, versioning, formatting, docs, packaging, repo clients)
  - PowerShell module: `PSPublishModule` (net472 + net8.0 + net10.0) — thin cmdlets (PowerShell UX surface)
    - Namespace: `PSPublishModule`
    - Wraps `PowerForge` services; no logic duplication
  - CLI tool: `PowerForge.Cli` (net8.0 + net10.0) — dotnet tool `powerforge` (AOT candidate)
    - Namespace: `PowerForge.Cli`
    - Calls `PowerForge` services for CI/GitHub Actions/VSCode
- Folders (in PowerForge): `Abstractions/`, `Services/`, `Models/`, `Diagnostics/`, `Repositories/`
- No nested namespaces beyond the root per assembly.

**Phase 0 — Prereqs**
- Add coding guidelines to `CONTRIBUTING.md` (nullability enabled, docs required, perf notes).
- Reduce nullable warnings (keep builds clean; warnings-as-errors where feasible).
- Baseline perf + memory measurements for key paths (build, format, docs).
- Define “machine output” contract for CLI (`--output json`, `--no-color`, stable exit codes, stable schema).

**Phase 0b — Make cmdlets truly thin (close the current gap)**
- Identify “fat cmdlets” and extract orchestration into `PowerForge` services:
  - `ModuleBuildPipeline` (exists; keep expanding only in `PowerForge`)
  - `ModuleTestService`
  - `ReleasePublisher` (GitHub assets) + `PackagePublisher` (NuGet/PSGallery/feeds)
  - `ProjectCleanupService`
- Keep cmdlets as: parameter mapping → call service → output typed result.
- Use `partial` cmdlet classes to split parameters vs execution (file size guardrail).

**Phase 1 — Core Services (no behavior change)**
- Interfaces: `IModuleVersionResolver`, `IModuleInstaller`, `IFormatter`, `ILineEndingsNormalizer`, `IFileSystem`, `IPowerShellRunner`, `ILogger`.
- Line endings + encoding:
  - Normalize `.ps1/.psm1/.psd1` deterministically (CRLF + UTF-8 BOM by default for PS 5.1 safety).
  - Detect mixed endings and fix; never fail build solely on formatting.
- Out-of-proc PSScriptAnalyzer:
  - Spawn `pwsh`/`powershell.exe` `-NoProfile -NonInteractive` and run `Invoke-Formatter`.
  - Timeout + graceful fallback (skip formatting but keep normalization).
- Logging adapter mapping to `[i]/[+]/[-]/[e]` style (no emojis).

**Phase 1a — PowerForge Core Packaging + Repo Abstractions**
- Abstractions:
  - `IPackager` (create module layout, nupkg/zip), `IRepositoryPublisher`, `IRepositoryClientFactory`
  - `RepositoryKind` enum: `PSGallery`, `NuGetV3`, `AzureArtifacts`, `FileShare` (extensible)
- Implement providers incrementally:
  - `NuGetV3Publisher` (NuGet.Protocol; supports PSGallery via API key)
  - `AzureArtifactsPublisher` (v3; PAT auth)
  - `FileSharePublisher` (copy artifacts)
  - `PSResourceGetPublisher` wrapper (out-of-proc) only when explicitly requested by config
- Credentials:
  - Read from env vars/PS credentials/TokenStore; no secrets in logs
- Private galleries supported via explicit repository URL + creds.

**Phase 2 — Versioned Install (fix locking)**
- Strategy `Exact` (release): install to `<Modules>\\Name\\<ModuleVersion>\\`.
- Strategy `AutoRevision` (dev): if version exists, install as `<ModuleVersion>.<rev>` without touching source manifest.
- Stage to temp, then atomic move; prune old versions (keep N=3 by default).
- PowerShell wrappers call C# installer; messages preserved.

**Phase 3 — Docs Engine (replace PlatyPS/HelpOut)**
- `DocumentationEngine` service:
  - Source: manifest + script files + XML doc from binary cmdlets.
  - Emit: MAML XML for external help, `about_*.help.txt`, and Markdown (README/CHANGELOG synthesis if desired).
  - Examples: support `<example>` in XML docs and comment-based help; honor ProseFirst/CodeFirst/CodeOnly modes.
- Drop PlatyPS/HelpOut once parity is validated on PSPublishModule itself.

**Phase 4 — CLI + GitHub Actions + VSCode**
- `PowerForge.Cli` tool commands (all backed by `PowerForge` services):
  - `powerforge build`: stage build (no in-place writes), compile/publish, normalize/format, produce artifacts.
  - `powerforge install`: versioned install from staging.
  - `powerforge docs`: generate external help and docs.
  - `powerforge pack`: create zip/nupkg artifacts for release.
  - `powerforge publish`: publish to PSGallery/private feeds (NuGet APIs) or via a PSResourceGet wrapper.
  - `--config <json>`: run build/publish/test from a typed JSON config (extension-friendly).
  - `--output json`: stable schema for every command (VSCode can parse results).
  - `--no-color`, `--quiet`, `--diagnostics`, consistent exit codes.
- GitHub Actions integration (`/mnt/c/Support/Github/github-actions`):
  - Add composite action `pspub-build`: setup-dotnet → cache → `dotnet tool restore` → `powerforge build` → `powerforge docs` → `powerforge pack`
  - Add composite action `pspub-install` for runner import sanity.
  - Optional publishing action `pspub-publish`.

**Phase 5 — Replace Script Functions with C#**
- Replace incrementally while keeping cmdlet names/parameters:
  - Formatting → `IFormatter` + `ILineEndingsNormalizer`
  - Versioning → `IModuleVersionResolver`
  - Copy/structure/artefacts → packaging + installer services
  - Docs commands → `DocumentationEngine`
  - Publishing → repository publishers / optional PSResourceGet wrapper
- Legacy compatibility must remain until parity is reached:
  - Existing `Build-Module` DSL and scripts still work, but translate into typed models internally.
  - Old PowerShell-only implementations are deleted once replaced.

**Phase 6 — AOT + Trimming (CLI)**
- Ensure `PowerForge` paths used by the CLI are trim-safe (avoid reflection-heavy/dynamic usage).
- Add `net10.0` target (when available) and validate `dotnet publish -p:PublishAot=true` for `PowerForge.Cli`.
- Keep PowerShell module multi-targeting (`net472` + modern) without blocking CLI AOT.

**Deprecations To Remove (when parity reached)**
- PlatyPS and HelpOut usage.
- In-proc PSScriptAnalyzer formatting.
- Unversioned installs to `...\\Modules\\Name\\`.
- Scripted `Publish-Module` flow; prefer `PowerForge` publishers or out-of-proc PSResourceGet wrapper only when requested.

**Validation**
- Unit tests for services (file ops, versioning, formatter, docs parser/emitters).
- Integration tests on Windows + Ubuntu:
  - `powerforge build`, `powerforge docs`, `powerforge pack`
  - `Import-Module` sanity + minimal cmdlet invocation
- Golden files for generated MAML/about_*.help.txt.

**Action Items (Checklist)**
- [x] Add typed build/install models + staging-first build pipeline (PowerForge).
- [x] Use staging build by default for self-build (`Module/Build/Build-Module.ps1`).
- [x] Add CLI `--config <json>` for `build`/`install` and initial `--output json`.
- [x] Add CLI `pipeline`/`run` command + polymorphic JSON segment deserialization.
- [x] Migrate configuration cmdlets away from `OrderedDictionary` to typed models + enums (legacy adapters allowed).
- [x] Replace legacy PowerShell build pipeline scripts with C# services (build/install) and delete the scripts (`Module/Private/New-PrepareStructure.ps1`, `Start-ModuleBuilding.ps1`, `Start-*`).
- [x] Remove remaining `Module/Private/*.ps1` helpers and port test-suite steps to C# (`PowerForge.ModuleDependencyInstaller`, `Invoke-ModuleTestSuite`).
- [ ] Restore parity for removed build-script features: docs, artefacts/pack, publish orchestration (PowerForge services + CLI commands).
- [ ] Refactor “fat cmdlets” into `PowerForge` services + `partial` cmdlets (enforce ~600–700 LOC budget).
- [ ] Define stable JSON output contract (schema/versioning, no-color/no-logs mixing, exit codes).
- [ ] Finish docs engine MVP and remove PlatyPS/HelpOut.
- [ ] Add GitHub composite actions calling the CLI.
- [ ] Plan `net10.0` + AOT publish for CLI (validate trim/AOT paths).
- [ ] Expand tests (service unit tests + CLI integration).
- [ ] PSResourceGet support: repository management + publish/find logic (internal; no standalone cmdlets).

**Sample GitHub Workflow (sketch)**
```
name: Build-Docs-Pack
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet tool restore
      - run: powerforge build --output json
      - run: powerforge docs --output json
      - run: powerforge pack --out artifacts --output json
      - uses: actions/upload-artifact@v4
        with: { name: module, path: artifacts }
```

**Repositories and Private Galleries**
- First-class support for Microsoft.PowerShell.PSResourceGet repositories (wrap out-of-proc for now) and direct NuGet V3 endpoints (private feeds/Azure Artifacts).
- Configuration must allow multiple named repositories with credentials and per-repo publish policies.

**Versioning & Dev Builds**
- Use `New-ConfigurationBuild -VersionedInstallStrategy AutoRevision -VersionedInstallKeep 3` during development.
- Keep PSD1 `ModuleVersion` stable while installer generates `<ver>.<rev>` on install.
- Do not introduce custom suffixes in `ModuleVersion`; document AutoRevision semantics instead.

**Notes**
- Formatting remains optional; when PSSA conflicts (e.g., VSCode), we log and continue.
- Secrets (PSGallery API key) flow via env vars/GitHub secrets; never logged.
