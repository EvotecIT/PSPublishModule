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

- `Get-ManagedModule` is not planned now. Use `Get-ModuleState` for installed inventory and `Find-ManagedModule` for repository discovery.
- `Register-ManagedModuleRepository` is not planned now. Use `Set-ModuleRepositoryProfile`, `Get-ModuleRepositoryProfile`, `Connect-ModuleRepository`, and `Register-ModuleRepository` for repository profiles and compatibility registration. Managed commands can also use direct `-Repository` values.
- `Install-PrivateModule` and `Update-PrivateModule` stay as convenience wrappers. The reusable path is `Install-ManagedModule`, `Save-ManagedModule`, `Update-ManagedModule`, `Publish-ManagedModule`, and `Invoke-ModuleState`.
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
Measure-ManagedModule -Name Company.Tools -Operation Find -ProfileName CompanyModules -Engine Managed
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
Update-ManagedModule -Name Company.Tools -Repository PSGallery
Update-ManagedModule -Name Company.Tools -VersionPolicy '>=1.2.0 <2.0.0' -ProfileName CompanyModules
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

## Model Contracts

The managed engine owns typed domain models for:

- Repositories: source, name, kind, trust, priority, and profile-derived evidence.
- Packages: identity, nuspec metadata, manifest metadata, dependency metadata, hashes, file counts, and byte counts.
- Versions: semantic comparison, prerelease labels, PowerShellGet-style bounds, and NuGet/PSResourceGet-style ranges.
- Plans and actions: install, save, update, publish, ModuleState delivery, repair, and skip reasons.
- Receipts: successful delivery evidence under the installed module version directory.
- Benchmarks: engine, operation, timing, package counts, bytes, import validation, publish status, and report paths.

Cmdlets should map parameters into these models and write result objects. They should not own repository protocol logic, archive extraction, dependency solving, package creation, or ModuleState repair decisions.

## Provider Support

| Provider/source | Support level | Notes |
| --- | --- | --- |
| PowerShell Gallery | Supported | NuGet v3 service index and flat-container package flow. |
| Generic NuGet v3 feed | Supported | Find, save, install, update, and publish with API key or basic credential where the feed supports it. |
| Local folder feed | Supported | Used for deterministic tests, offline bundles, local publish, and benchmark smoke proof. |
| Azure Artifacts | Partial | Profiles and direct v3 feed URLs work. Credential-provider bootstrapping remains a compatibility/profile concern. |
| JFrog/Artifactory | Partial | NuGet v3 endpoints and static credentials work. Runtime OIDC exchange remains outside the managed publish path. |
| ProGet/Nexus-compatible NuGet feeds | Expected | Treat as generic NuGet v3 until live validation proves feed-specific behavior. |
| GitHub Packages NuGet feeds | Expected | Treat as generic NuGet v3 until live validation proves authentication and publish behavior. |

## Behavior Rules

- Repository trust is explicit. A trusted profile or caller policy can allow unattended install/update; `RequireTrustedRepository` blocks untrusted sources.
- Credentials are resolved before managed repository access. The core engine receives credential values or no credential; it does not call external credential helpers.
- Retries, timeouts, cancellation, proxy behavior, and HTTP error messages belong in the managed repository client.
- Side-by-side versions are valid when version policies ask for exact or compatible copies. Forced replacement stages first and rolls back on promotion failure.
- Downgrades require explicit policy through the selected version/range and force behavior.
- Loaded modules can block unsafe updates unless the caller explicitly allows that risk.
- Cross-scope repairs must target the requested scope and must not treat a satisfying copy in another scope as equivalent.
- Prerelease labels are semantic-version labels. Stable versions do not satisfy prerelease-only requests unless the policy allows that outcome.

## Transition Gates

Compatibility transport stays available until all of these are true:

- Managed install/save/update/publish passes local-folder and public-feed proof on Windows PowerShell 5.1 and PowerShell 7+.
- Benchmarks cover cold cache, warm cache, heavy extraction, dependency closure, no-op update, private feed metadata, and publish comparison.
- Common PowerShellGet and PSResourceGet module workflows have documented managed equivalents.
- ModuleState can maintain the same estate through managed transport with receipts and inspectable repair plans.
- Provider gaps are documented as explicit partial support instead of hidden fallbacks.
