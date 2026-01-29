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
- Cmdlets must be small: no orchestration, no business logic. If a cmdlet grows past “parameter mapping + ShouldProcess + call service”, move the logic into `PowerForge` and keep the cmdlet as a thin wrapper (partials for parameters vs execution are allowed).
- `PowerForge` file size budget: if a service grows past ~600–700 LOC, split into smaller services and/or `partial` to keep it maintainable.
- Do not expose low-level PSResourceGet cmdlets (or “PSPublishResource” cmdlets); use PSResourceGet internally from configuration/publish workflows (like we do with PowerShellGet).
- Must remain `net472` compatible (Windows PowerShell 5.1), plus `net8.0` and `net10.0`.
- CLI must be machine-friendly (stable JSON output + exit codes + no-color mode) and AOT/trim-friendly for VSCode scenarios.

**Current status (as of 2025-12-29)**
- PSPublishModule is now primarily a binary module: `Module/Public` and `Module/Private` contain no shipped `.ps1` functions (bootstrap `Module/PSPublishModule.psm1` remains, plus `Module/Build/Build-Module.ps1` for legacy DSL compatibility, and `Module/Build/Build-ModuleSelf.ps1` for self-build via CLI pipeline).
- `PowerForge` has typed build/install models + a staging-first build pipeline to avoid self-build file locking.
- `PowerForge` build/export detection supports explicit binary assembly names (`ExportAssemblies`) and will not clobber existing manifest exports when binaries are missing (`DisableBinaryCmdletScan` + safe fallback).
- `New-Configuration*` cmdlets now emit typed `PowerForge` configuration segment objects (no `OrderedDictionary`/`Hashtable` outputs); the legacy DSL parser accepts both typed segments and legacy dictionaries.
- `PowerForge.Cli` supports `build`/`install` via `--config <json>` and machine output via `--output json` (stable envelope with `schemaVersion` + `command` + `success` + `exitCode` + payload; source-generated System.Text.Json for AOT/trim), plus `pipeline`/`run` to execute a full typed pipeline spec from JSON (segment array + build/install options), and `plan` to preview the pipeline without running it.
- `ModulePipelineRunner` now executes `ConfigurationDocumentationSegment`/`ConfigurationBuildDocumentationSegment` (PowerForge docs generator; no PlatyPS/HelpOut), `ConfigurationArtefactSegment` (Packed/Unpacked) including optional required-module bundling via PSResourceGet `Save-PSResource` (out-of-proc) with PowerShellGet `Save-Module` fallback, and `ConfigurationPublishSegment` (PSResourceGet `Publish-PSResource` + GitHub releases), producing typed results.
- `Invoke-ModuleBuild` routes both the simple build path and the legacy DSL (`-Settings {}` / `Build-Module {}`) through the PowerForge pipeline (C#); the legacy PowerShell `Start-*` build scripts were removed.
- `Module/Build/Build-ModuleSelf.ps1` self-builds by building `PowerForge.Cli` and running the JSON pipeline (`powerforge.json`) so PSPublishModule can self-build without file locking.
- PowerShell compatibility analysis no longer depends on PowerShell helper functions (moved to C# analyzer).
- PSResourceGet is used internally (out-of-proc wrapper for find/publish/install) and is not exposed as standalone cmdlets.
- Repository-wide .NET package release flow is now available in C# (discover projects, resolve version, pack, publish).

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
- Logging adapter mapping to emoji-first (fallback to `[i]/[+]/[-]/[e]` when Unicode is not available).

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
- Exact installs sync the target directory to staging (remove stale files that are not present in the new build).
- PowerShell wrappers call C# installer; messages preserved.

**Phase 3 — Docs Engine (replace PlatyPS/HelpOut)**
- `DocumentationEngine` service:
  - Source (MVP): out-of-proc `Get-Command` + `Get-Help` (no PlatyPS/HelpOut) for script + cmdlet parity.
  - Emit (MVP): PlatyPS-style Markdown help files + module page (Readme.md) + external help MAML (`<culture>\<ModuleName>-help.xml`).
  - Keep legacy knobs (`Tool`, `UpdateWhenNew`) as no-ops for compatibility.
- PlatyPS/HelpOut usage removed (legacy enum values remain for compatibility).

**Phase 4 — CLI + GitHub Actions + VSCode**
- `PowerForge.Cli` tool commands (all backed by `PowerForge` services):
  - `powerforge build`: stage build (no in-place writes), compile/publish, normalize/format, produce artifacts.
  - `powerforge install`: versioned install from staging.
  - `powerforge docs`: generate external help and docs.
  - `powerforge pack`: create zip/nupkg artifacts for release.
  - `powerforge test`: run out-of-proc Pester test suite (typed results + exit codes).
  - `powerforge publish`: publish to PSGallery/private feeds (NuGet APIs) or via a PSResourceGet wrapper.
  - `--config <json>`: run build/publish/test from a typed JSON config (extension-friendly).
  - `--output json`: stable schema for every command (VSCode can parse results).
  - `--no-color`, `--quiet`, `--diagnostics`, `--view auto|standard|ansi`, consistent exit codes.
- GitHub Actions integration (`C:\\Support\\GitHub\\github-actions`):
  - (done) Add composite action `.github/actions/powerforge-run` to install `PowerForge.Cli` (dotnet tool) and run `powerforge` commands.
  - (optional) Add higher-level wrappers once the tool is published broadly (e.g., `pspub-build`, `pspub-publish`).

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
- (done) Add `net10.0` target for `PowerForge`, `PSPublishModule`, and `PowerForge.Cli`.
- Validate `dotnet publish -p:PublishAot=true` for `PowerForge.Cli`.
- Keep PowerShell module multi-targeting (`net472` + modern) without blocking CLI AOT.

**Deprecations To Remove (when parity reached)**
- PlatyPS and HelpOut usage (done: replaced by PowerForge docs generator; legacy enum values remain for compatibility).
- In-proc PSScriptAnalyzer formatting.
- Unversioned installs to `...\\Modules\\Name\\`.
- Scripted `Publish-Module` flow; prefer `PowerForge` publishers or out-of-proc PSResourceGet wrapper only when requested.

**Validation**
- Unit tests for services (file ops, versioning, formatter, docs parser/emitters).
- Integration tests on Windows + Ubuntu:
  - `powerforge build`, `powerforge docs`, `powerforge pack`
  - `Import-Module` sanity + minimal cmdlet invocation
- Golden files for generated MAML/about_*.help.txt.

**Real-module beta testing (start here)**
- Status: ready to start testing builds on real modules as of 2025-12-27 (staging build + pipeline + test suite are C#-backed).
- Safest workflow (no install): use staging + keep it for inspection.
  - Prefer `--config <BuildSpec.json>` so you can set `ExcludeDirectories`/`ExcludeFiles` for a clean staged module.
  - `dotnet run -f net10.0 --project .\PowerForge.Cli\PowerForge.Cli.csproj -c Release -- build --config <BuildSpec.json> --output json`
- For binary modules where the cmdlet assembly name differs from the module name, set `ExportAssemblies` (JSON) or `-NETBinaryModule` (PowerShell DSL) so exports are detected from the right `.dll`.
- Legacy workflow (existing module `Build\Build-Module.ps1`): should continue to work; it routes through `Invoke-ModuleBuild` (C# pipeline).
- When using JSON pipelines: run `powerforge plan --config <Pipeline.json> --output json` first, then `powerforge pipeline --config <Pipeline.json> --output json`.

**Action Items (Checklist)**
- [x] Add typed build/install models + staging-first build pipeline (PowerForge).
- [x] Use staging build by default for self-build (`Module/Build/Build-ModuleSelf.ps1` + `powerforge.json`).
- [x] Add CLI `--config <json>` for `build`/`install` and initial `--output json`.
- [x] Add CLI `pipeline`/`run` command + polymorphic JSON segment deserialization.
- [x] Add CLI `plan` command to preview `pipeline` without running it.
- [x] Migrate configuration cmdlets away from `OrderedDictionary` to typed models + enums (legacy adapters allowed).
  - [x] Collapse `New-ConfigurationBuild` to emit one segment per config group (Build/Options/BuildLibraries/PlaceHolderOption) instead of many tiny segments.
- [x] Move project text analysis into `PowerForge` (typed reports for encoding/line-endings/consistency; cmdlets no longer emit `OrderedDictionary`).
- [x] Replace legacy PowerShell build pipeline scripts with C# services (build/install) and delete the scripts (`Module/Private/New-PrepareStructure.ps1`, `Start-ModuleBuilding.ps1`, `Start-*`).
- [x] Remove remaining `Module/Private/*.ps1` helpers and port test-suite steps to C# (`PowerForge.ModuleDependencyInstaller`, `Invoke-ModuleTestSuite`).
- [x] Restore parity for removed build-script features (PowerForge services + CLI commands):
  - [x] Artefacts/pack (C# `ArtefactBuilder` + pipeline `ConfigurationArtefactSegment` support)
  - [x] Docs orchestration
  - [x] Publish orchestration
- [x] Refactor “fat cmdlets” into `PowerForge` services and keep cmdlets truly thin (parameter binding + `ShouldProcess` + call service + typed output; `partial` is OK for parameters vs execution).
  - [x] `Remove-ProjectFiles` → `PowerForge.ProjectCleanupService` (typed spec/results; cmdlet is a thin wrapper)
  - [x] `Get-ModuleTestFailures` → `PowerForge.ModuleTestFailureAnalyzer` (typed `PowerForge.ModuleTestFailureAnalysis` on `-PassThru`)
  - [x] `Invoke-ModuleTestSuite` → `PowerForge` test runner service (for future CLI + VSCode usage)
  - [x] `Invoke-ModuleBuild` → `PowerForge.ModuleScaffoldService` + `PowerForge.LegacySegmentAdapter` (cmdlet only maps params and invokes the PowerForge pipeline)
  - [x] `Invoke-DotNetReleaseBuild` → `PowerForge.DotNetReleaseBuildService` (cmdlet only handles `ShouldProcess` + optional `Register-Certificate` hook)
  - [x] `Get-PowerShellCompatibility` → `PowerForge.PowerShellCompatibilityAnalyzer` (typed report + C# CSV export; cmdlet only handles host/progress output)
- [x] Add repository-wide .NET package release workflow (discover, X-pattern versioning via NuGet, pack, publish).
- **Deliberate exclusions (do not reintroduce)**
  - Legacy public helper functions are intentionally removed from PSPublishModule (moved to PSMaintenance or deprecated):
    - `Convert-ProjectEncoding`, `Convert-ProjectLineEnding`
    - `Get-ProjectEncoding`, `Get-ProjectLineEnding`
    - `Initialize-PortableModule`, `Initialize-PortableScript`, `Initialize-ProjectManager`
    - `Install-ProjectDocumentation`, `Show-ProjectDocumentation`
  - Hashtable-based DSL configuration is intentionally not supported; use typed cmdlets / JSON segments only.
- [x] Close remaining DSL parity gaps in C# pipeline:
  - [x] Wire `ImportModules` segment (self/required module import).
  - [x] Wire `Command` segment (manifest `CommandModuleDependencies`).
  - [x] Apply `PlaceHolder` + `PlaceHolderOption` segments during build.
  - [x] Execute `TestsAfterMerge` segment (`New-ConfigurationTest`).
- [x] Add opt-in auto-install of missing modules during build (PSResourceGet/PowerShellGet fallback).
- [x] Add regression test for `Remove-Comments` script-level param block behavior (comment-based help preservation).
- [x] Define stable JSON output contract (schema/versioning, no-color/no-logs mixing, exit codes).
  - [x] Include `schemaVersion` in all CLI JSON outputs.
  - [x] Serialize enums as strings in CLI JSON output.
  - [x] Use a stable JSON envelope (no anonymous-type serialization; AOT/trim-friendly).
  - [x] Add `--quiet` and `--diagnostics` (keep stdout pure when `--output json` is used).
  - [x] Add `--view auto|standard|ansi` (auto disables live UI in CI).
  - [x] Add interactive Spectre.Console progress for `docs`/`pack`/`pipeline` in Standard view (auto disables in CI and when `--output json`/`--no-color`/`--quiet`).
  - [x] Document the JSON schema (VSCode extension baseline): `JSON_SCHEMA.md` + `Schemas/`.
- [x] Finish docs engine MVP and remove PlatyPS/HelpOut.
- [x] Add GitHub composite actions calling the CLI.
- [ ] Validate AOT publish for CLI (code is AOT/trim-friendly; verify end-to-end publish in CI with a native toolchain on Windows runners).
  - [ ] Current blocker: `dotnet publish -p:PublishAot=true` fails due to AOT analysis errors in `Microsoft.PowerShell.SDK` (System.Management.Automation), `Newtonsoft.Json`, and related dependencies (single-file + reflection-heavy APIs).
  - [ ] Next: split AOT-safe core from PowerShell-SDK-dependent services (or conditionally exclude PowerShell SDK when `PublishAot=true`) so `PowerForge.Cli` can publish NativeAOT for the build/pack/docs paths that only require out-of-proc PowerShell.
- [ ] Expand tests (service unit tests + CLI integration).
  - [x] Add `PowerForge.Tests` (xUnit) with starter coverage for `PowerShellCompatibilityAnalyzer` and `ModuleBuilder` TFM routing.
  - [x] Add unit tests for pipeline step planning (`ModulePipelineStep.Create` build/docs substeps).
- [x] Repository publishing: PSResourceGet + PowerShellGet support (tool selection + repo registration + publish/find/version check logic; internal; no standalone cmdlets).

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
