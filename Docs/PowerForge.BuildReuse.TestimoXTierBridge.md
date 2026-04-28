# PowerForge Reusable Build Coverage: TestimoX and TierBridge

This note maps the PowerShell-heavy build/deploy/catalog logic in TestimoX and TierBridge to the reusable capabilities already present in PSPublishModule/PowerForge, then proposes the smallest engine changes needed to make future repo scripts thin wrappers.

## Consumer Inventory

### TestimoX

Relevant scripts and patterns:

- `Build/Sync-PowerShellCatalog.ps1`
  - Runs `dotnet run --project TestimoX.CLI ... ps-catalog --base-dir ... --output-dir ...`.
  - Generates compiled PowerShell rule catalog source under `TestimoX/RulesPowerShell/RulesGenerated`.
- `Build/Publish-AOT.ps1`
  - Owns restore/build, target selection (`CLI`, `Audit`, `Tools`, `Service`, `Agent`, `Both`, `All`), runtime matrix, style selection (`Portable`, `PortableCompat`, `PortableSize`, `AotSpeed`, `AotSize`), slim cleanup, optional catalog refresh, service install/start, and summary output.
- `Build/Deploy-CLI.ps1`, `Build/Deploy-Service.ps1`, `Build/Deploy-TestimoX.Monitoring.ps1`, `Build/Deploy-TestimoX.Agent.ps1`
  - Publish, copy to distributable folders, zip outputs, and generate simple service scripts.
- `Build/Deploy.ps1`
  - Composes CLI/service/monitoring outputs into a release root and writes `manifest.json` plus `manifest.txt`.
- `Build/Prepare-TestimoX.*-MSI.ps1` and `Build/Build-TestimoX.*-MSI.ps1`
  - Stage payloads, write MSI manifests, harvest/build installer projects.
- `Build/*DashboardBenchmark*.ps1` and `Build/Helpers/Test-Dashboard*BenchmarkGate.ps1`
  - Generate scenario data and enforce benchmark gates.
- `Module-TestimoX/Build/Build-Module.ps1`, `Module-ADPlayground/Build/Build-Module.ps1`, `Module-ComputerX/Build/Build-Module.ps1`
  - Already use `Build-Module` / `New-ConfigurationBuild` for binary module merge/sign/package flows.

### TierBridge

Relevant scripts and patterns:

- `Build/Deploy.ps1`
  - Restores/builds solution, optionally builds/copies the PowerShell module, publishes service and CLI, signs binaries/scripts, creates package layout, generates wrapper scripts from templates, creates data folders from `appsettings.json`, copies README template, and zips the deployment.
- `Build/Rebuild.ps1`
  - Stops service, builds/publishes service, preserves or clears local state, patches config from machine environment variables, configures audit policy, reinstalls service if needed, and starts/verifies service.
- `Build/Templates/*.wrapper.ps1`
  - Thin user-facing wrappers that import the shipped module and call repo-specific module functions.
- `Module/Build/Build-Module.ps1`
  - Already uses `Build-Module` / `New-ConfigurationBuild` for the PowerTierBridge module.

## Existing PowerForge Coverage

PowerForge already covers a large part of both repos' custom PowerShell:

- Module builds:
  - `Invoke-ModuleBuild` / `Build-Module`
  - binary module merge/copy/sign/install
  - `New-ConfigurationManifest`, `New-ConfigurationBuild`, `New-ConfigurationArtefact`, validation, docs, formatting, module dependency handling
- DotNet publish:
  - `Invoke-DotNetPublish`, `New-DotNetPublishConfig`, `New-ConfigurationDotNet*`
  - project catalog (`Projects[]` + `ProjectId`)
  - runtime/framework/style matrix
  - restore/build/publish separation
  - `Portable`, `PortableCompat`, `PortableSize`, `AotSpeed`, `AotSize`, `FrameworkDependent`
  - target-level and style-level MSBuild properties
  - output path templates, zip output, slim cleanup, symbol/docs/ref pruning
  - signing profiles and per-target/installer signing
  - service script/metadata generation (`Install-Service.ps1`, `Uninstall-Service.ps1`, `Run-Once.ps1`)
  - service recovery and service lifecycle execution
  - config bootstrap rules
  - state preservation rules for rebuild scenarios
  - bundles with includes, bundle scripts, metadata, delete patterns, nested archive rules, and zip output
  - MSI prepare/build and installer-from-bundle flow
  - benchmark gates
  - output manifests/checksums/run reports
- Plugin/catalog lane:
  - `powerforge plugin export`
  - `powerforge plugin pack`
  - `PowerForgePluginCatalogService`
  - PowerShell cmdlet source exists for `Invoke-PowerForgePluginExport` and `Invoke-PowerForgePluginPack`
- Standalone bundle post-process:
  - `powerforge dotnet bundle-postprocess`
  - PowerShell cmdlet source exists for `Invoke-PowerForgeBundlePostProcess`

## Immediate Gaps

### 1. Plugin and bundle-postprocess cmdlets are not exported by the generated module

The cmdlet source and generated docs exist:

- `PSPublishModule/Cmdlets/InvokePowerForgePluginExportCommand.cs`
- `PSPublishModule/Cmdlets/InvokePowerForgePluginPackCommand.cs`
- `PSPublishModule/Cmdlets/InvokePowerForgeBundlePostProcessCommand.cs`
- `Module/Docs/Invoke-PowerForgePluginExport.md`
- `Module/Docs/Invoke-PowerForgePluginPack.md`
- `Module/Docs/Invoke-PowerForgeBundlePostProcess.md`

But `Module/PSPublishModule.psd1` and `Module/PSPublishModule.psm1` do not include those cmdlets in `CmdletsToExport`.

Next step: regenerate/fix the module build output so these cmdlets are exported, and add a focused test that binary-detected cmdlets and manifest exports agree. Do not hand-edit the generated bootstrapper as the primary fix.

### 2. No first-class pre-publish generated-source/catalog step

TestimoX needs `ps-catalog` before publish. Today this can stay as a thin script before `Invoke-DotNetPublish`, but PowerForge lacks a declarative "run this command before build/publish" contract inside `DotNetPublishSpec`.

Recommended engine feature:

- `Hooks` or `Commands` in DotNet publish specs with phases such as `BeforeRestore`, `BeforeBuild`, `BeforeTargetPublish`, `AfterTargetPublish`, `BeforeBundle`, `AfterBundle`.
- command path, args with tokens, working directory, timeout, required/fail policy, environment variables, and output capture.
- plan-mode visibility without execution.

This would make TestimoX's `Sync-PowerShellCatalog.ps1` a one-line wrapper around config instead of a prerequisite hidden in bespoke publish scripts.

### 3. Generic wrapper-script generation is narrower than TierBridge needs

PowerForge can generate service install/uninstall/run-once scripts, and it has an internal `ScriptTemplateRenderer`. TierBridge needs package scripts that import the shipped module and call arbitrary module functions (`Install-TierBridgeService`, `Set-TierBridgeConfig`, `Get-TierBridgePermission`, etc.).

Recommended engine feature:

- Bundle/service `ScriptsToGenerate[]` or `Templates[]`.
- Inputs: template path or embedded template, output path, token map, overwrite policy, optional signing profile.
- Built-in tokens for package paths, module names, service names, runtime/framework/style, bundle output, and artifact paths.

Keep function names and parameters repo-owned. Move only the template rendering/copy/sign mechanics to PowerForge.

### 4. No declarative "include built module artefact in app bundle"

TierBridge deploy detects the module name from `Build-Module.ps1`, optionally builds it, finds an installed module path, and copies it into `Modules/<ModuleName>`.

Recommended engine feature:

- A module artefact include in DotNet bundles:
  - `ModuleBuild` reference or command hook
  - expected module name
  - source path from known PowerForge module artefact output, not user profile install fallback
  - bundle destination such as `Modules/{moduleName}`

This would let app bundles depend on PowerForge module build outputs without scraping scripts or reading user profile module installs.

### 5. Product policy should remain repo-local

Do not move these into PowerForge as generic features yet:

- TestimoX rule semantics and the `ps-catalog` CLI implementation.
- TierBridge config mutation from `VirusTotalApi` / `TierBridgeGraphSecret`.
- TierBridge audit policy bootstrap and folder ACL policy.
- Product README text, branding, default install paths, support instructions.
- Local sibling dependency policy such as `UseLocalFileInspectorX`, except as normal MSBuild properties in config.

PowerForge should expose hooks and reusable mechanics; repos should keep product choices.

## Proposed Migration Plan

### Phase 0: Unblock existing reusable surfaces

1. Regenerate/fix module exports for:
   - `Invoke-PowerForgePluginExport`
   - `Invoke-PowerForgePluginPack`
   - `Invoke-PowerForgeBundlePostProcess`
2. Add `Schemas/powerforge.plugins.schema.json` and a small `Module/Examples/PluginCatalog` example.
3. Add tests for default plugin config discovery and manifest export parity.
4. Add a short section to `Docs/PSPublishModule.DotNetPublish.Quickstart.md` linking plugin catalogs and bundle post-process to app bundles.

### Phase 1: Engine primitives before consumer rewrites

Implement the reusable pieces before replacing repo scripts. Scope them to the patterns already proven by TierBridge and TestimoX, not a broad "everything build-related" abstraction.

1. DotNet publish hooks/commands with plan-mode output. Implemented in this branch as `Hooks[]`:
   - `BeforeRestore`
   - `BeforeBuild`
   - `BeforeTargetPublish`
   - `AfterTargetPublish`
   - `BeforeBundle`
   - `AfterBundle`
2. Generic generated script/template outputs for bundle/package scripts. Implemented in this branch as `Bundles[].GeneratedScripts[]`:
   - template path or embedded template
   - output path
   - token map
   - overwrite policy
   - optional signing profile
3. Module artefact bundle includes. Implemented in this branch as `Bundles[].ModuleIncludes[]`:
   - reference a PowerForge module build output
   - copy into `Modules/{moduleName}` or another configured destination
   - avoid user-profile module install discovery as the primary source
4. MSI/package hardening. Implemented in this branch with `Bundles[].PrimarySubdirectory`, `Bundles[].CopyItems[]`, module includes, generated scripts, and the existing `PrepareFromBundleId` installer flow:
   - ensure installer prepare/build can consume composed bundle outputs
   - keep service package metadata, wrapper scripts, README, module payload, and MSI payload in one declarative release layout
5. Manifest/export parity tests for generated module files so new binary cmdlets do not exist in docs/source but disappear from the shipped module.

### Phase 2: TierBridge package and MSI pilot

1. Add `Build/powerforge.dotnetpublish.json` with targets for `TierBridge.Service` and `TierBridge.CLI`.
2. Move `UseLocalFileInspectorX` into `DotNet.MsBuildProperties` or target `MsBuildProperties`.
3. Use service package + lifecycle for install/reinstall/start/verify mechanics.
4. Use state preservation for data/config/log paths that must survive rebuilds.
5. Use bundles for `Service`, `CLI`, `Scripts`, `Modules`, `README.md`, and deployment zip.
6. Generate TierBridge wrapper scripts through the engine template output feature.
7. Include the built PowerTierBridge module through the module artefact bundle include.
8. Build the MSI from the composed bundle/package layout.
9. Keep config mutation/audit bootstrap repo-local behind explicit hooks.

TierBridge is the better first consumer because it exercises the package, service, module-include, generated-wrapper, signing, zip, and MSI path without TestimoX's additional generated catalog and AOT matrix complexity.

### Phase 3: TestimoX publish/catalog/MSI pilot

1. Add `Build/powerforge.dotnetpublish.json` with project catalog entries for:
   - `TestimoX.CLI`
   - `TestimoX.Audit`
   - `TestimoX.Service`
   - `TestimoX.Agent`
   - `TestimoX.Monitoring`
2. Model publish targets with style/rid/framework matrices instead of script loops.
3. Move `Build/Sync-PowerShellCatalog.ps1` into a declarative `BeforeBuild` or `BeforeTargetPublish` hook.
4. Replace `Deploy-CLI.ps1` and `Deploy-Service.ps1` mechanics with `Invoke-DotNetPublish -ConfigPath ... -Target ...`.
5. Use bundles for the unified `CLI/Service/Monitoring` release layout and `Outputs` for manifest/checksum/report generation.
6. Migrate MSI staging/build scripts to `Installers[]` after publish parity is verified.

### Phase 4: Generalize only after both pilots

After TierBridge and TestimoX both build through PowerForge, promote only duplicated repo patterns into engine contracts:

1. Optional package layout manifest that can replace repo-specific `manifest.json` / `manifest.txt` builders.
2. Additional hook phases only if both repos need them.
3. Additional script-template tokens only when they replace duplicated wrapper logic.
4. Higher-level release profiles only if the existing target/bundle/installer model becomes repetitive.

## Target End State

Each repo should converge to:

```powershell
param(
    [ValidateSet('Release','Debug')]
    [string] $Configuration = 'Release',
    [string[]] $Runtime = @('win-x64'),
    [switch] $Plan
)

$config = Join-Path $PSScriptRoot 'powerforge.dotnetpublish.json'
Invoke-DotNetPublish -ConfigPath $config -Configuration $Configuration -Runtimes $Runtime -Plan:$Plan -ExitCode
```

Repo-specific scripts should only:

- choose profile/target/runtime defaults
- pass secrets or product-specific paths
- run product-specific catalog/config/audit hooks until those are intentionally generalized
