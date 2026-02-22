# PSPublishModule DotNet Publish Engine Implementation Plan

Last updated: 2026-02-21

## Goal

Build the missing engine features in `PSPublishModule`/`PowerForge` first, then replace repo-local scripts with declarative config and helper cmdlets.

Execution rule:
- Do not replace `TestimoX` / `TierBridge` / `SectigoCertificateManagerService` scripts until required engine parity is complete and validated.

## Source Evidence (Why This Work Exists)

Cross-repo orchestration and duplication:
- `C:/Support/GitHub/TestimoX/Build/Deploy.ps1:66`
- `C:/Support/GitHub/TestimoX/Build/Deploy.ps1:74`
- `C:/Support/GitHub/TestimoX/Build/Deploy.ps1:107`
- `C:/Support/GitHub/TierBridge/Build/Deploy.ps1:344`
- `C:/Support/GitHub/TierBridge/Build/Deploy.ps1:368`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:107`

Service lifecycle + generated scripts:
- `C:/Support/GitHub/TestimoX/Build/Deploy-Service.ps1:96`
- `C:/Support/GitHub/TestimoX/Build/Deploy-Service.ps1:102`
- `C:/Support/GitHub/TestimoX/Build/Deploy-Service.ps1:109`
- `C:/Support/GitHub/TestimoX/Build/Deploy-TestimoX.Monitoring.ps1:229`
- `C:/Support/GitHub/TestimoX/Build/Deploy-TestimoX.Monitoring.ps1:237`
- `C:/Support/GitHub/TestimoX/Build/Deploy-TestimoX.Monitoring.ps1:256`
- `C:/Support/GitHub/TierBridge/Build/Deploy.ps1:512`
- `C:/Support/GitHub/TierBridge/Build/Deploy.ps1:551`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:241`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:259`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:279`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:302`

Signing logic duplication:
- `C:/Support/GitHub/TestimoX/Build/Deploy-TestimoX.Monitoring.ps1:41`
- `C:/Support/GitHub/TestimoX/Build/Deploy-TestimoX.Monitoring.ps1:268`
- `C:/Support/GitHub/TierBridge/Build/Deploy.ps1:123`
- `C:/Support/GitHub/TierBridge/Build/Deploy.ps1:165`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:37`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Deploy-SectigoCertificateManager.Service.ps1:395`

MSI prepare/build duplication:
- `C:/Support/GitHub/TestimoX/Build/Prepare-TestimoX.Monitoring-MSI.ps1:1`
- `C:/Support/GitHub/TestimoX/Build/Prepare-TestimoX.Monitoring-MSI.ps1:196`
- `C:/Support/GitHub/TestimoX/Build/Build-TestimoX.Monitoring-MSI.ps1:181`
- `C:/Support/GitHub/TestimoX/Build/Build-TestimoX.Monitoring-MSI.ps1:253`
- `C:/Support/GitHub/TestimoX/Build/Build-TestimoX.Monitoring-MSI.ps1:272`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Prepare-SectigoCertificateManager.Service-MSI.ps1:1`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Prepare-SectigoCertificateManager.Service-MSI.ps1:124`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Build-SectigoCertificateManager.Service-MSI.ps1:151`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Build-SectigoCertificateManager.Service-MSI.ps1:236`

Rebuild preserve/restore duplication:
- `C:/Support/GitHub/TestimoX/Build/Rebuild-TestimoX.Monitoring.ps1:28`
- `C:/Support/GitHub/TestimoX/Build/Rebuild-TestimoX.Monitoring.ps1:97`
- `C:/Support/GitHub/TestimoX/Build/Rebuild-TestimoX.Monitoring.ps1:403`
- `C:/Support/GitHub/TierBridge/Build/Rebuild.ps1:146`
- `C:/Support/GitHub/TierBridge/Build/Rebuild.ps1:394`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Rebuild-SectigoCertificateManager.Service.ps1:22`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Rebuild-SectigoCertificateManager.Service.ps1:168`
- `C:/Support/GitHub/SectigoCertificateManagerService/Build/Rebuild-SectigoCertificateManager.Service.ps1:310`

Benchmark and baseline gate behavior:
- `C:/Support/GitHub/TestimoX/Build/Helpers/Test-DashboardStorageBenchmarkGate.ps1:13`
- `C:/Support/GitHub/TestimoX/Build/Helpers/Test-DashboardStorageBenchmarkGate.ps1:274`
- `C:/Support/GitHub/TestimoX/Build/Helpers/Test-DashboardEnterpriseBenchmarkGate.ps1:20`
- `C:/Support/GitHub/TestimoX/Build/Helpers/Test-DashboardEnterpriseBenchmarkGate.ps1:179`
- `C:/Support/GitHub/TestimoX/Build/Benchmark-TestimoX.Monitoring-Dashboard.ps1:388`

## Current Engine Baseline (PSPublishModule)

Already available in `PowerForge`:
- Dotnet publish plan/run pipeline: `PowerForge/Services/DotNetPublishPipelineRunner.Plan.cs:18`, `PowerForge/Services/DotNetPublishPipelineRunner.Run.cs:8`
- Publish output staging/cleanup/zip/sign/manifests: `PowerForge/Services/DotNetPublishPipelineRunner.Steps.cs:101`, `PowerForge/Services/DotNetPublishPipelineRunner.PublishLayout.cs:48`, `PowerForge/Services/DotNetPublishPipelineRunner.SigningAndProcess.cs:10`, `PowerForge/Services/DotNetPublishPipelineRunner.FailureAndManifests.cs:129`
- Typed publish models and schema: `PowerForge/Models/DotNetPublish/DotNetPublishSpec.cs:7`, `PowerForge/Models/DotNetPublish/DotNetPublishTarget.cs:24`, `Schemas/powerforge.dotnetpublish.schema.json:42`
- CLI entrypoint for dotnet publish + plan/validate: `PowerForge.Cli/Program.Command.DotNet.cs:9`

Gaps to close before replacement:
- Profiles/matrix/project catalog
- Service packaging/lifecycle primitives
- MSI prepare/build primitives
- Preserve/restore rebuild primitives
- Baseline-driven perf gates
- Checksums and richer release manifest/reporting
- Path safety policy and strict signing policies

## Master TODO Backlog (Implementation Order)

P0: Hardening and contracts (do first)
- [x] Add output path safety policy to DotNetPublish (`deny outside ProjectRoot` by default).
- [x] Add manifest path safety policy (`ManifestJsonPath`/`ManifestTextPath` cannot escape root unless explicitly allowed).
- [x] Add explicit signing policy (`OnMissingTool`, `OnSignFailure`) with `Warn|Fail|Skip`.
- [x] Add checksum output (`SHA256SUMS.txt`) finalizer.
- [x] Add deterministic manifest ordering for stable CI diffs.
- [x] Add tests for path traversal attempts and signing policy behavior.

P1: DSL foundation
- [x] Add profile model (`Profiles` + default profile + profile merge).
- [x] Add matrix model (`runtime`, `framework`, `style`, custom dimensions).
- [x] Add project catalog (`Projects[]` with IDs/groups) and target references by ID.
- [x] Add include/exclude filters for matrix combinations.
- [x] Add CLI `--profile` override.
- [x] Add CLI `--matrix` overrides.
- [x] Add `plan` output expansion showing resolved matrix jobs.

P2: Service packaging primitives
- [x] Add service packaging options in publish target (generate scripts + metadata).
- [x] Add service lifecycle execution step (`stop/delete/install/start/verify`).
- [x] Add service recovery configuration support.
- [x] Add script template renderer with token validation.
- [x] Add config bootstrap copy rules (`example -> runtime config when missing`).

P3: MSI primitives
- [x] Add `msi.prepare` step to produce staging + manifest.
- [x] Add built-in harvest generation for payload tree.
- [x] Add `msi.build` step for wixproj with mapped metadata.
- [x] Add MSI version policy helper (date floor / monotonic bump).
- [x] Add MSI signing as first-class step reusing sign policy.
- [x] Add optional client-license payload injection.

P4: Rebuild/preserve primitives
- [x] Add preserve rules for config/data/log/database/report/license paths.
- [x] Add restore rules with overwrite and directory-create semantics.
- [x] Add locked-output guard with actionable diagnostics.
- [x] Add service-aware rebuild execution flow (`stop -> deploy -> restore -> start`).

P5: Quality gates and CI outputs
- [x] Add benchmark gate step consuming baseline JSON + tolerances.
- [x] Add regex/parser metrics extraction step for benchmark logs.
- [x] Add baseline update/verify modes with fail-on-new behavior.
- [x] Add run report JSON with timings, artifacts, signing counts, gate outcomes.

P6: Migration and replacement
- [x] Add migration helpers (config scaffold + helper cmdlets) for existing Build scripts.
- [x] Add `powerforge dotnet scaffold` starter config generation for JSON-first migration.
- [x] Add helper cmdlets for scripted DotNetPublish config composition.
- [ ] Migrate `Deploy-*.ps1` first.
- [ ] Migrate MSI scripts second.
- [ ] Migrate rebuild scripts third.
- [ ] Migrate top-level orchestrators (`Deploy.ps1`) last.
- [ ] Remove duplicate script logic only after parity checklist passes.

## PR Wave Plan (Concrete)

PR1: Engine hardening baseline
- Scope:
  - Path safety + signing policy + checksum + deterministic manifests.
  - Unit tests for hardening.
- Primary files:
  - `PowerForge/Models/DotNetPublish/DotNetPublishTarget.cs`
  - `PowerForge/Models/DotNetPublish/DotNetPublishSpec.cs`
  - `PowerForge/Services/DotNetPublishPipelineRunner.Plan.cs`
  - `PowerForge/Services/DotNetPublishPipelineRunner.Steps.cs`
  - `PowerForge/Services/DotNetPublishPipelineRunner.SigningAndProcess.cs`
  - `PowerForge/Services/DotNetPublishPipelineRunner.FailureAndManifests.cs`
  - `Schemas/powerforge.dotnetpublish.schema.json`
  - `PowerForge.Cli/Program.Command.DotNet.cs`
- Done when:
  - Path escape attempts are blocked in validate/run.
  - CI can fail on missing signtool/sign errors when configured.
  - Checksums file can be emitted from config.

PR2: Profiles/matrix/project catalog
- Scope:
  - Add typed config for profiles/matrix/project IDs + CLI overrides.
  - Expand plan output with resolved jobs.
- Done when:
  - A single config can express `runtime x framework x style` matrix without duplicated target blocks.

PR3: Service package DSL
- Scope:
  - Script generation + service lifecycle options.
  - Template tokens for install/uninstall/run-once.
- Done when:
  - One target can produce service package scripts equivalent to TestimoX/Sectigo/TierBridge patterns.

PR4: MSI prepare/build DSL
- Scope:
  - `msi.prepare` and `msi.build` steps with harvest + signing.
- Done when:
  - TestimoX/Sectigo MSI flow can be described in one config without custom MSI script logic.

PR5: Rebuild preserve/restore
- Scope:
  - Preserve/restore rules and rebuild orchestration.
- Done when:
  - Rebuild behavior (config/data/logs/license/report) is declarative and tested.

PR6: Perf gates and reporting
- Scope:
  - Baseline-driven benchmark gates and structured run report.
- Done when:
  - TestimoX baseline gate behavior can run from engine step(s).

PR7: Config/cmdlet migration (no behavior drift)
- Scope:
  - Convert existing scripts in external repos to DSL/pipeline config and helper cmdlets.
- Done when:
  - External repo scripts remain UX-compatible, but logic lives in PowerForge.

## Ownership and Effort

- PR1: Owner `PowerForge.Core`, Effort `M`
- PR2: Owner `PowerForge.Core`, Effort `L`
- PR3: Owner `PowerForge.Core + ServicePackaging`, Effort `L`
- PR4: Owner `PowerForge.Core + Installer`, Effort `L`
- PR5: Owner `PowerForge.Core`, Effort `M`
- PR6: Owner `PowerForge.Core + QualityGates`, Effort `M`
- PR7: Owner `RepoMaintainer + PowerForge.Core`, Effort `M`

## Proposed DSL Shape (JSON)

```json
{
  "$schema": "./Schemas/powerforge.dotnetpublish.schema.json",
  "SchemaVersion": 2,
  "DotNet": {
    "ProjectRoot": ".",
    "Configuration": "Release"
  },
  "Profiles": [
    { "Name": "release", "Default": true },
    { "Name": "local" }
  ],
  "Matrices": {
    "runtime": ["win-x64", "win-arm64"],
    "style": ["PortableCompat", "AotSpeed"]
  },
  "Projects": [
    { "Id": "monitoring", "Path": "TestimoX.Monitoring/TestimoX.Monitoring.csproj", "Group": "apps" },
    { "Id": "installer.monitoring", "Path": "Installer/TestimoX.Monitoring/TestimoX.Monitoring.Installer.wixproj", "Group": "installer" }
  ],
  "Targets": [
    {
      "Name": "monitoring",
      "ProjectId": "monitoring",
      "Publish": {
        "Framework": "net10.0-windows",
        "Runtimes": ["<runtime>"],
        "Style": "<style>",
        "UseStaging": true,
        "Zip": true,
        "PruneReferences": true,
        "Sign": {
          "Enabled": true,
          "OnMissingTool": "Fail",
          "OnSignFailure": "Fail"
        },
        "Service": {
          "Name": "TestimoX.Monitoring",
          "GenerateInstallScript": true,
          "GenerateUninstallScript": true,
          "GenerateRunOnceScript": true
        }
      }
    }
  ],
  "Installers": [
    {
      "Id": "monitoring.msi",
      "Type": "Wix",
      "PrepareFromTarget": "monitoring",
      "InstallerProjectId": "installer.monitoring",
      "Harvest": "Auto",
      "Sign": true
    }
  ],
  "Gates": [
    {
      "Type": "benchmark",
      "BaselinePath": "Build/Baselines/DashboardStorageBenchmark.baseline.json",
      "FailOnNewRegressions": true
    }
  ],
  "Outputs": {
    "ManifestJsonPath": "Artifacts/Release/manifest.json",
    "ManifestTextPath": "Artifacts/Release/manifest.txt",
    "ChecksumsPath": "Artifacts/Release/SHA256SUMS.txt"
  }
}
```

## Proposed DSL Shape (PowerShell)

```powershell
New-ConfigurationDotNetPublish -Name 'TestimoX' {
    New-ConfigurationProfile -Name 'release' -Default
    New-ConfigurationMatrix -Name 'runtime' -Values 'win-x64','win-arm64'
    New-ConfigurationMatrix -Name 'style' -Values 'PortableCompat','AotSpeed'

    New-ConfigurationProject -Id 'monitoring' -Path 'TestimoX.Monitoring/TestimoX.Monitoring.csproj'
    New-ConfigurationProject -Id 'installer.monitoring' -Path 'Installer/TestimoX.Monitoring/TestimoX.Monitoring.Installer.wixproj'

    New-ConfigurationPublish -Name 'monitoring' -ProjectId 'monitoring' -Framework 'net10.0-windows' -Runtime '<runtime>' -Style '<style>' -UseStaging -Zip
    New-ConfigurationSign -Target 'monitoring' -Enabled -OnMissingTool Fail -OnFailure Fail
    New-ConfigurationServicePackage -Target 'monitoring' -Name 'TestimoX.Monitoring' -GenerateInstallScript -GenerateUninstallScript -GenerateRunOnceScript

    New-ConfigurationMsiPrepare -Id 'monitoring.msi' -FromTarget 'monitoring'
    New-ConfigurationMsiBuild -Id 'monitoring.msi' -InstallerProjectId 'installer.monitoring' -Harvest Auto -Sign

    New-ConfigurationGate -Type Benchmark -BaselinePath 'Build/Baselines/DashboardStorageBenchmark.baseline.json' -FailOnNewRegressions
    New-ConfigurationOutput -ManifestJsonPath 'Artifacts/Release/manifest.json' -ChecksumsPath 'Artifacts/Release/SHA256SUMS.txt'
}
```

## Canonical Pipeline Step Order

1. Resolve profile/matrix/variables/secrets.
2. Validate prerequisites and path safety.
3. Stage workspace.
4. Restore/build/test.
5. Publish outputs.
6. Prune/normalize outputs.
7. Inject config/templates/scripts.
8. Preserve state (rebuild mode only).
9. Sign files.
10. Package zip/artifacts.
11. Build MSI artifacts.
12. Run quality gates.
13. Emit manifests/checksums/reports.
14. Apply install/release targets.
15. Cleanup and final summary.

## Replacement Gate (Must Pass Before Script Deletion)

- Parity checklist approved for each repo:
  - `TestimoX`
  - `TierBridge`
  - `SectigoCertificateManagerService`
- CI run using engine config has no regression in output shape/signing/install scripts.
- Baseline and benchmark gates pass under engine.
- Existing scripts are converted to config/cmdlet entrypoints before removing old logic.
