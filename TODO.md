**PSPublishModule C# Migration and GitHub Actions Adoption**

**Goals**
- Move PSPublishModule behavior into a single C# library and thin PS wrappers.
- Make functionality reusable by GitHub Actions and other repos without scripts.
- Replace PlatyPS/HelpOut with a C# help/doc generator.
- Fix reliability: versioned installs, safe formatting, deterministic encodings.
- Keep today’s UX (messages, defaults) while improving robustness/performance.

**Architecture**
- One namespace per assembly; small set of assemblies:
  - Core library: `PowerForge` (net472 + net8.0) — reusable engine
    - Namespace: `PowerForge`
    - Provides all building blocks (IO, versioning, formatting, docs, packaging, gallery clients)
  - PowerShell module: `PSPublishModule` (net472 + net8.0) — thin cmdlets
    - Namespace: `PSPublishModule`
    - Wraps `PowerForge` services; no logic duplication
  - CLI tool: `PSPublishModule.Cli` (net8.0) — dotnet global tool `pspub`
    - Namespace: `PSPublishModule.Cli`
    - Calls `PowerForge` services for CI/GitHub Actions
- Folders (in PowerForge): `Abstractions/`, `Services/`, `Models/`, `Diagnostics/`, `Repositories/`
- No nested namespaces beyond the root per assembly.

**Phase 0 — Prereqs**
- Add coding guidelines to `CONTRIBUTING.md` (nullability enabled, docs required, perf notes).
- Enable nullable in csproj; clean remaining warnings.
- Baseline perf + memory measurements for key paths (build, format, docs).

**Phase 1 — Core Services (no behavior change)**
- Interfaces: `IModuleVersionResolver`, `IModuleInstaller`, `IFormatter`, `ILineEndingsNormalizer`, `IFileSystem`, `IPowerShellRunner`, `ILogger`.
- Line endings + encoding:
  - Normalize `.ps1/.psm1/.psd1` to CRLF, UTF‑8 (BOM optional) deterministically.
  - Detect mixed endings and fix; never fail build solely on formatting.
- Out‑of‑proc PSScriptAnalyzer:
  - Spawn `pwsh`/`powershell.exe` `-NoProfile -NonInteractive` and run `Invoke-Formatter`.
  - Timeout + graceful fallback (skip formatting but keep normalization).
- Logging adapter mapping to `[i]/[+]/[-]/[e]` style.

**Phase 1a — PowerForge Core Packaging + Repo Abstractions**
- Abstractions:
  - `IPackager` (create module layout, nupkg/zip), `IRepositoryPublisher`, `IRepositoryClientFactory`
  - `RepositoryKind` enum: `PSGallery`, `NuGetV3`, `AzureArtifacts`, `FileShare` (extensible)
- Implement providers incrementally:
  - `NuGetV3Publisher` (NuGet.Protocol; supports PSGallery via API key)
  - `AzureArtifactsPublisher` (v3; PAT auth)
  - `FileSharePublisher` (copy artifacts)
  - Optional `PSResourceGetPublisher` wrapper (out-of-proc) for environments preferring PowerShell repos
- Credentials:
  - Read from env vars/PS credentials/TokenStore; no secrets in logs
- Private galleries supported via explicit repository URL + creds.

**Phase 2 — Versioned Install (fix locking)**
- Strategy `Exact` (release): install to `<Modules>\Name\<ModuleVersion>\`.
- Strategy `AutoRevision` (dev): if version exists, install as `<ModuleVersion>.<rev>` without touching source manifest.
- Stage to temp, then atomic move; prune old versions (keep N=3 by default).
- PowerShell wrappers call C# installer; messages preserved.

**Phase 3 — Docs Engine (replace PlatyPS/HelpOut)**
- `DocumentationEngine` service:
  - Source: manifest + script files (AST via PowerShell SDK) + XML doc from binary cmdlets.
  - Emit: MAML XML for external help, `about_*.help.txt`, and Markdown (README/CHANGELOG synthesis if desired).
  - Examples: support `<example>` in XML docs, and comment-based help; honor ProseFirst/CodeFirst/CodeOnly modes.
- Drop PlatyPS/HelpOut once parity validated on PSPublishModule itself.

- **Phase 4 — CLI + GitHub Actions**
- `PowerForge.Cli` dotnet tool `powerforge` commands:
  - `powerforge build`: compile, copy binaries, normalize/format, stage output.
  - `powerforge install`: versioned install to user Modules path.
  - `powerforge docs`: run docs engine (MAML, about_*, Markdown).
  - `powerforge pack`: create NuGet-like artifact or zip for release.
  - `powerforge publish`: publish to PSGallery/private (NuGet APIs) or via PSResourceGet.
  - Optional alias package: `PSPublishModule.Cli` with command `pspub` calling into the same library (for backward compat).
- GitHub Actions integration (`/mnt/c/Support/Github/github-actions`):
  - Add composite action `pspub-build`:
    - Steps: setup-dotnet → cache → `dotnet tool restore` → `powerforge build` → `powerforge docs` → `powerforge pack`.
  - Add composite action `pspub-install` (for self-hosted runners testing module import).
  - Optionally add container action for consistent toolchain; for Windows runners, use composite.
  - Caching: NuGet, PSScriptAnalyzer module, dotnet tool cache.
  - Optional publishing action `pspub-publish` using `PowerForge` repository providers (PSGallery/Private)

**Phase 5 — Replace Script Functions with C#**
- Replace incrementally while keeping cmdlet names/parameters:
  - `Format-Code` → `IFormatter` (out‑of‑proc PSSA) + `ILineEndingsNormalizer`.
  - `Get-ProjectVersion` → `IModuleVersionResolver`.
  - `Copy-Binaries`/structure → `IPackagingService` + `IModuleInstaller`.
  - Docs commands → `DocumentationEngine`.
- Keep thin PS functions that call the C# implementations until fully removed.

**Phase 6 — Telemetry and Perf**
- Optional minimal telemetry (opt-in env var) to measure durations and error rates.
- Parallelize safe operations (file copy, hash, parse) with TPL; bound concurrency.

**Deprecations To Remove (when parity reached)**
- PlatyPS and HelpOut usage.
- In-proc PSScriptAnalyzer formatting.
- Unversioned installs to `...\Modules\Name\`.
 - Scripted `Publish-Module` flow; prefer `PowerForge` publishers (NuGet APIs) or out-of-proc PSResourceGet wrapper only when requested.

**Validation**
- Unit tests for services (file ops, versioning, formatter, docs parser/emitters).
- Integration tests pipeline on Windows + Ubuntu: `pspub build`, `pspub docs`, `Import-Module` sanity.
- Golden files for generated MAML/about_*.help.txt.

**Action Items (Checklist)**
- [ ] Create interfaces and base services (Phase 1)
- [ ] Wire PS wrappers to call C# for formatting + normalization
- [ ] Implement versioned installer + dev AutoRevision (Phase 2)
- [ ] Port docs generator MVP (external help + about_*)
- [ ] Ship `PSPublishModule.Cli` dotnet tool with `build/docs/install`
- [ ] Publish GitHub composite actions that call the CLI
- [ ] Migrate PSPublishModule pipeline to use the action; remove PlatyPS/HelpOut
- [ ] Add tests and golden files; enable CI matrix (win/ubuntu)
- [ ] Introduce `PowerForge` core project and move services under it
- [ ] Add PSGallery/Private gallery publishers via NuGet APIs
- [ ] Add optional PSResourceGet out-of-proc publisher and repository management

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
      - run: pspub build --verbosity detailed
      - run: pspub docs --mode ProseFirst
      - run: pspub pack --out artifacts
      - uses: actions/upload-artifact@v4
        with: { name: module, path: artifacts }
```

**Notes**
- Formatting remains optional; when PSSA conflicts (e.g., VSCode), we log and continue.
- Secrets (PSGallery API key) should flow via GitHub secrets; never logged.
- About topics (`about_<ModuleName>.help.txt`) can be generated from Delivery docs.
