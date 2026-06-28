# Managed Module Compatibility Contract

This document defines the compatibility target for the managed C# module engine. It is not a promise to clone every historical behavior from PowerShellGet or PSResourceGet. The goal is a clean managed implementation that covers the common module lifecycle workflows and keeps escape hatches where provider support is incomplete.

Baseline references:

- [PowerShellGet v2 command reference](https://learn.microsoft.com/en-us/powershell/module/powershellget/?view=powershellget-2.x)
- [PSResourceGet command reference](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/?view=powershellget-3.x)
- [Install-Module](https://learn.microsoft.com/en-us/powershell/module/powershellget/install-module?view=powershellget-2.x)
- [Find-Module](https://learn.microsoft.com/en-us/powershell/module/powershellget/find-module?view=powershellget-2.x)
- [Publish-Module](https://learn.microsoft.com/en-us/powershell/module/powershellget/publish-module?view=powershellget-2.x)
- [Install-PSResource](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/install-psresource?view=powershellget-3.x)

## Command Mapping

| Existing workflow | Managed command | Status | Notes |
| --- | --- | --- | --- |
| `Find-Module` | `Find-ManagedModule` | Supported | Searches direct repository sources or saved profiles. Wildcard names are supported. |
| `Save-Module` | `Save-ManagedModule` | Supported | Saves module package content and dependency closure to a path. |
| `Install-Module` | `Install-ManagedModule` | Supported | Installs side-by-side versions into CurrentUser, AllUsers, or a custom module root. |
| `Update-Module` | `Update-ManagedModule` | Supported | Plans updates from installed inventory, receipt evidence, and repository metadata. |
| `Publish-Module` | `Publish-ManagedModule` | Supported | Packs and publishes modules to local folder or NuGet v3-compatible feeds. |
| `Find-PSResource` | `Find-ManagedModule` | Partial | Module packages are supported. Script/resource-kind specific behavior is out of scope for the managed module engine. |
| `Save-PSResource` | `Save-ManagedModule` | Partial | Module packages are supported; non-module resource kinds remain compatibility-path work. |
| `Install-PSResource` | `Install-ManagedModule` | Partial | Module packages, version ranges, prerelease, repository priority, and scope are supported. |
| `Update-PSResource` | `Update-ManagedModule` | Partial | Module update workflows are supported. Non-module resources remain compatibility-path work. |
| `Publish-PSResource` | `Publish-ManagedModule` | Partial | Module package publishing is supported. Script/resource publishing remains compatibility-path work. |

## Public Surface Decisions

- `Get-ManagedModule` is the PowerShell-native installed inventory surface. Use `-AsInventory` when an advanced ModuleState inventory object is needed for planning or support bundles.
- `Repair-ManagedModule` is the day-to-day stale/drift/family/source maintenance surface. Use `-Plan` for preview. The lower-level ModuleState cmdlets remain available when inventory, plan, test, and apply need to be inspected independently.
- `Register-ManagedModuleRepository` is not planned now. Use `Set-ModuleRepositoryProfile`, `Get-ModuleRepositoryProfile`, `Connect-ModuleRepository`, and `Register-ModuleRepository` for repository profiles and compatibility registration. Managed commands can also use direct `-Repository` values.
- `Install-PrivateModule` and `Update-PrivateModule` stay as convenience wrappers. The reusable path is `Install-ManagedModule`, `Save-ManagedModule`, `Update-ManagedModule`, `Repair-ManagedModule`, `Publish-ManagedModule`, and advanced ModuleState cmdlets.
- Public and private command aliases are allowed only when they point to the same managed command implementation. New command families need a distinct operator purpose.

## Switching Examples

The managed commands keep the common module lifecycle shape familiar while moving repository access, dependency resolution, package extraction, receipts, and publish packing into the C# engine.

### Find

```powershell
Find-ManagedModule -Name Company.Tools -Repository 'https://packages.company.test/nuget/v3/index.json'
Find-ManagedModule -Name Company.* -ProfileName CompanyModules -AllVersions
```

### Benchmark Repository Metadata

```powershell
.\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName Company.Tools -Operation Find -Engine Managed
```

### Save

```powershell
Save-ManagedModule -Name Company.Tools -Path C:\OfflineModules -Repository PSGallery
Save-ManagedModule -Name Company.Tools -RequiredVersion 1.2.0 -Path C:\OfflineModules -ProfileName CompanyModules
```

### Install

```powershell
Install-ManagedModule -Name Company.Tools -Scope CurrentUser -Repository PSGallery
Install-ManagedModule -Name Company.Tools -RequiredVersion 1.2.0 -ProfileName CompanyModules -AcceptLicense
```

### Update

```powershell
Update-ManagedModule -Repository PSGallery
Update-ManagedModule -Name Company.Tools -Repository PSGallery
Update-ManagedModule -Name Company.Tools -VersionPolicy '>=1.2.0 <2.0.0' -ProfileName CompanyModules
```

### Repair

```powershell
Repair-ManagedModule -Latest -Repository PSGallery -Plan -ShowSummary
Repair-ManagedModule -Family Graph -Repository PSGallery -Plan -ShowSummary
Repair-ManagedModule -MaintenanceReceiptPath .\module-maintenance.json -Repository CompanyModules -Plan
```

### Installed Inventory

```powershell
Get-ManagedModule -Name Microsoft.Graph.* -IncludeLoaded -ShowSummary
Get-ManagedModule -Path C:\OfflineModules -AsInventory
```

### Publish

```powershell
Publish-ManagedModule -Path C:\Source\Company.Tools -Repository C:\Packages
Publish-ManagedModule -Path C:\Source\Company.Tools -ProfileName CompanyModules -ApiKeyFilePath C:\Secrets\company-feed-key.txt
```

### Private Gallery Wrapper Opt-In

Existing private-gallery wrappers remain available while parity is proven. Use `-Transport ManagedModule` when a profile should use the managed engine for module install/update delivery:

```powershell
Install-PrivateModule -ProfileName CompanyModules -Name Company.Tools -Transport ManagedModule
Update-PrivateModule  -ProfileName CompanyModules -Name Company.Tools -Transport ManagedModule
```

### Estate Maintenance

Use `Invoke-ModuleState` as the operator entrypoint when the question is not just "install this module", but "keep this machine's module estate under control":

```powershell
Invoke-ModuleState -Installed -Latest -Repository PSGallery -Transport ManagedModule -ShowSummary
```

For automation and support bundles, keep the steps inspectable:

```powershell
$inventory = Get-ModuleState -IncludeLoaded -ShowSummary
$plan = $inventory | Get-ModuleStatePlan -DesiredState @{
    Modules = @(
        @{ Name = 'Company.Tools'; Version = '=1.2.0'; Repository = 'CompanyModules'; Scope = 'CurrentUser' }
    )
} -Repair -ShowSummary
$plan | Test-ModuleState -PassThru -ShowSummary
$plan | Invoke-ModuleStatePlan -Repository CompanyModules -Transport ManagedModule -Execute -ShowSummary
```

## Parameter Matrix

| Capability | Managed support | Compatible inputs |
| --- | --- | --- |
| Module identity | Supported | `-Name`, wildcard names where search semantics apply |
| Repository source | Supported | `-Repository`, `-RepositoryName`, `-ProfileName` |
| Credentials | Supported | `-Credential`, credential user/secret/file inputs where exposed |
| Version selection | Supported | `-RequiredVersion`, `-Version`, `-MinimumVersion`, `-MaximumVersion`, `-VersionPolicy` |
| Prerelease | Supported | `-Prerelease`, `-AllowPrerelease` alias where useful |
| Scope | Supported | CurrentUser, AllUsers, Custom module root |
| Dependency handling | Supported | Dependency closure, skip dependency check, package dependency mirroring during managed publish |
| Trust/integrity | Supported | Trusted repository requirement, allowed author policy, expected package SHA256 |
| WhatIf/Confirm | Supported | Mutating cmdlets use PowerShell `ShouldProcess` |
| Summaries | Supported | Spectre.Console summaries remain host-side; result objects are still pipeline-friendly |

## Compatibility Checklist

This checklist is the guardrail for replacing common PowerShellGet and PSResourceGet usage without surprises. Matching a parameter name is not enough; each item needs behavior proof, tests, and benchmark evidence when it can affect speed.

- [x] `Install-ManagedModule` supports common `Install-Module` flows: `-Name`, `-Repository`, `-RequiredVersion`, `-MinimumVersion`, `-MaximumVersion`, `-Scope`, `-Force`, `-AllowClobber`, `-AcceptLicense`, `-Credential`, `-WhatIf`, and `-Confirm`.
- [x] `Update-ManagedModule` supports named updates and no-name estate updates from selected module roots, matching the common `Update-Module` operator habit.
- [x] `Save-ManagedModule` supports dependency closure, explicit path, version policies, license acceptance, forced replacement, and import-validation benchmark proof.
- [x] `Find-ManagedModule` supports repository/profile lookup, wildcard module names, all versions, prerelease inclusion, and fast latest-version lookup.
- [x] `Publish-ManagedModule` supports module package publishing with API key or credential paths through the managed publisher.
- [x] PSResourceGet-style semantic version ranges are supported through `-VersionPolicy`.
- [x] PSResourceGet-style `-TrustRepository` behavior maps to trusted repository profiles and `-RequireTrustedRepository` policy instead of hidden prompts.
- [ ] Decide whether public proxy parameters should be added to managed cmdlets or kept as repository/profile policy only. The managed repository client has proxy behavior, but `-Proxy` and `-ProxyCredential` are not currently public compatibility parameters.
- [x] Define exact public semantics for `-Force` on install/save/update, including exact-version reinstall, no-op plans, rollback-protected replacement, downgrade blocking, and no implied cleanup.
- [ ] Define exact public semantics for `-Force` in repair and maintenance flows, including receipt repair and cleanup interactions.
- [x] Define exact public semantics for `-AllowClobber` versus PSResourceGet `-NoClobber`, including exported command conflicts in the selected target root.
- [x] Define exact public semantics for `-AcceptLicense`, including dependency packages and unattended estate updates.
- [ ] Define exact public semantics for `-SkipPublisherCheck` compatibility. Managed install/update currently uses trust, author, source, and package hash policies instead of cloning PowerShellGet's publisher-check switch.
- [ ] Complete managed Authenticode/catalog validation parity with PSResourceGet `-AuthenticodeCheck`, including explicit catalog policy evidence and timestamped short-lived certificate chains.
- [x] Add initial managed `-AuthenticodeCheck` support for install/save/update on Windows using native WinTrust validation of extracted signable files before promotion.
- [x] Expose semantically equivalent migration aliases such as `-RequiredVersion`, `-AllowPrerelease`, `-Source`, `-RepositoryUri`, `-Path`, `-DestinationPath`, `-SkipDependenciesCheck`, `-ModulePath`, and `-NuGetApiKey` where they map cleanly to managed cmdlet behavior.
- [ ] Decide whether to add explicit `-TrustRepository` and `-SkipPublisherCheck` compatibility parameters. They should not be aliases unless their behavior is intentionally defined because repository trust and publisher checks are different safety concepts in the managed engine.
- [ ] Document unsupported non-module resource use cases explicitly: scripts, DSC resources as resource kinds, role capability search, command-name search, and provider-specific bootstrap behavior.
- [x] Add repair/maintenance benchmark lanes for stale versions, source drift, scope drift, and family coherence.
- [ ] Add repair/maintenance benchmark lanes for loaded-module safety and cleanup planning.

## Model Contracts

The managed engine owns typed domain models for:

- Repositories: source, name, kind, trust, priority, and profile-derived evidence.
- Packages: identity, nuspec metadata, manifest metadata, dependency metadata, hashes, file counts, and byte counts.
- Versions: semantic comparison, prerelease labels, PowerShellGet-style bounds, and NuGet/PSResourceGet-style ranges.
- Plans and actions: install, save, update, publish, ModuleState delivery, repair, and skip reasons.
- Receipts: successful delivery evidence under the installed module version directory.
- Benchmark evidence: engine, operation, timing, package counts, bytes, import validation, publish status, and report paths. Benchmark tooling lives under `Benchmarks`, not in the shipped cmdlet surface.

Cmdlets should map parameters into these models and write result objects. They should not own repository protocol logic, archive extraction, dependency solving, package creation, or ModuleState repair decisions.

## Provider Support

| Provider/source | Support level | Notes |
| --- | --- | --- |
| PowerShell Gallery | Supported | Canonical PSGallery read operations use the NuGet v2 API for find/save/install/update because that endpoint is the reliable public module feed surface; managed publish and generic NuGet feeds still use NuGet v3 service metadata where required. |
| Generic NuGet v3 feed | Supported | Find, save, install, update, and publish with API key or basic credential where the feed supports it. |
| Local folder feed | Supported | Used for deterministic tests, offline bundles, local publish, and benchmark smoke proof. |
| Azure Artifacts | Partial | Profiles and direct v3 feed URLs work. Credential-provider bootstrapping remains a compatibility/profile concern. |
| JFrog/Artifactory | Partial | NuGet v3 endpoints and static credentials work. Runtime OIDC exchange remains outside the managed publish path. |
| ProGet/Nexus-compatible NuGet feeds | Expected | Treat as generic NuGet v3 until live validation proves feed-specific behavior. |
| GitHub Packages NuGet feeds | Expected | Treat as generic NuGet v3 until live validation proves authentication and publish behavior. |

## Behavior Rules

- Repository trust is explicit. A trusted profile or caller policy can allow unattended install/update; `RequireTrustedRepository` blocks untrusted sources.
- Credentials are resolved before managed repository access. The core engine receives credential values or no credential; it does not call external credential helpers.
- Retries, timeouts, cancellation, proxy behavior, endpoint selection, and HTTP error messages belong in the managed repository client. The public PowerShell Gallery default resolves read operations through its NuGet v2 endpoint to avoid slow or blocked v3 service-index probes.
- Side-by-side versions are valid when version policies ask for exact or compatible copies. Forced replacement stages first and rolls back on promotion failure.
- Downgrades require a dedicated explicit downgrade policy; `-Force` alone is not a downgrade request.
- Loaded modules can block unsafe updates unless the caller explicitly allows that risk.
- Cross-scope repairs must target the requested scope and must not treat a satisfying copy in another scope as equivalent.
- Prerelease labels are semantic-version labels. Stable versions do not satisfy prerelease-only requests unless the policy allows that outcome.

### Force Semantics

`-Force` is intentionally narrow in the managed engine:

- `Install-ManagedModule -Force` and `Save-ManagedModule -Force` replace the exact selected module version when that version already exists in the target root.
- Without `-Force`, installing or saving an exact version that already exists is a no-op plan and does not write files.
- `Update-ManagedModule -Force` reinstalls the selected target version when the selected target is already the installed version.
- `Update-ManagedModule -Force` does not silently downgrade a newer installed version to an older selected version. Downgrade behavior needs a separate explicit policy so stale-version repair and operator mistakes do not collapse into the same switch.
- `-Force` does not imply destructive old-version cleanup. Cleanup remains a separate maintenance/repair decision.
- `Publish-ManagedModule -Force` bypasses managed preflight duplicate/version guards where the target repository supports replacement or accepts the pushed package. It does not guarantee that a remote feed will overwrite an existing package.

### Clobber Semantics

Managed install, save, and update are no-clobber by default. Before a staged package is promoted, the managed engine reads manifest-declared `FunctionsToExport`, `CmdletsToExport`, and `AliasesToExport` values and checks them against other installed modules in the selected target root. If another module exports the same function, cmdlet, or alias, the operation fails before writing the final module version.

`-AllowClobber` is the explicit opt-in that permits those exported command conflicts. This maps to the PSResourceGet `-NoClobber` safety idea by making no-clobber the default in the managed engine, rather than adding a separate `-NoClobber` switch whose absence could imply less safe behavior.

Same-name modules are skipped during conflict checks so side-by-side versions and exact-version reinstalls do not self-conflict. Wildcard exports such as `FunctionsToExport = '*'` are not treated as concrete conflicts because they cannot be reliably enumerated from the manifest alone. The check is target-root based; cross-scope conflicts remain a repair/maintenance policy concern rather than an install-time block across every module path on the machine.

### License Acceptance Semantics

Managed install, save, update, and ModuleState managed delivery never prompt for license acceptance. If the selected package or any dependency package declares `requireLicenseAcceptance=true`, the operation fails unless the caller passes `-AcceptLicense`.

`-AcceptLicense` applies to the whole dependency closure for that operation. This is intentional for unattended estate maintenance: either the operator or automation policy accepts the package licenses up front, or no package that requires acceptance is promoted. A license-required dependency blocks the parent package before the parent is promoted.

Plan operations do not accept licenses or write license receipts. They only describe the intended action. The caller must pass `-AcceptLicense` again when invoking the mutating install, save, update, or ModuleState apply operation.

### Authenticode Semantics

`-AuthenticodeCheck` is an explicit opt-in on managed install, save, and update. When supplied, the managed engine extracts the package to a staging directory and validates signable files with native Windows WinTrust before dependency delivery and before the staged module is promoted to the target root. Unsigned or invalid signable files block the operation before the final module version is written.

The initial managed gate covers common signable module artifacts such as `.ps1`, `.psm1`, `.psd1`, `.ps1xml`, `.pssc`, `.psrc`, `.dll`, `.exe`, and `.cat`. Dependency installs inherit the same check. Plan output records that Authenticode validation would be required, but plan mode does not download or validate signatures.

This check is currently Windows-only. Calling it on non-Windows hosts fails clearly instead of silently skipping the safety check. Full PSResourceGet parity still needs catalog-specific evidence and timestamp/short-lived-certificate behavior documented with live signed fixtures.

## Transition Gates

Compatibility transport stays available as a legacy fallback. The managed engine is the preferred path when source evidence and provider support allow it; compatibility is still selected when a provider gap or missing repository-source signal would make the managed decision unsafe.

The managed path can be treated as the default for supported module workflows after all of these are true:

- Managed install/save/update/publish passes local-folder and public-feed proof on Windows PowerShell 5.1 and PowerShell 7+.
- Benchmarks cover cold cache, warm cache, heavy extraction, dependency closure, no-op update, private feed metadata, and publish comparison.
- Common PowerShellGet and PSResourceGet module workflows have documented managed equivalents.
- ModuleState can maintain the same estate through managed transport with receipts and inspectable repair plans.
- Provider gaps are documented as explicit partial support instead of hidden fallbacks.

Current status: these gates have local evidence on both Windows PowerShell 5.1 and PowerShell 7+. `Auto` transport prefers managed delivery for local paths, direct repository URIs, and registered/profile repositories that resolve to source endpoints. Compatibility transport remains available for provider-specific bootstrap gaps, non-module resource kinds, and unresolved repository names; those decisions are surfaced in typed result objects and summaries instead of being hidden.
