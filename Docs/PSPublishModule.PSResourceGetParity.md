# PSResourceGet Parity

This document records how the managed PowerForge resource surface maps to
`Microsoft.PowerShell.PSResourceGet`. It distinguishes complete module lifecycle
coverage from non-module and provider-specific gaps so a missing resource kind
does not make a supported module command look partial.

Comparison baseline: stable `Microsoft.PowerShell.PSResourceGet` 1.2.0,
reviewed 2026-07-17.

References:

- [PSResourceGet command reference](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/?view=powershellget-3.x)
- [PSResourceGet 1.2.0 release notes](https://learn.microsoft.com/en-us/powershell/gallery/powershellget/psresourceget-release-notes?view=powershellget-3.x)
- [Managed module compatibility contract](PSPublishModule.ManagedModules.Compatibility.md)
- [Managed module release readiness](PSPublishModule.ManagedModules.ReleaseReadiness.md)

## Status Vocabulary

- **Supported for modules** means the PSResourceGet module workflow has a
  managed end-to-end equivalent, including typed output and pipeline behavior.
- **Managed difference** means the user outcome is supported through a safer or
  more explicit managed contract rather than an identical switch.
- **Explicit gap** means the behavior is not silently emulated or delegated.
- **Managed extension** means PowerForge provides functionality beyond the
  PSResourceGet module lifecycle.

## Module Lifecycle

| PSResourceGet 1.2.0 | Managed command | Status | Notes |
| --- | --- | --- | --- |
| `Find-PSResource` | `Find-ManagedModule` | Supported for modules | Name and wildcard search, exact/wildcard/range `-Version`, tags, resource type `Module`, prerelease, all versions, dependency expansion, credentials, proxy, direct sources, and saved profiles. Command-name and DSC-resource-name content search are explicit gaps. |
| `Get-InstalledPSResource` | `Get-ManagedModule` | Supported for modules | Name/wildcard, version, prerelease, scope, explicit module paths, and typed installed rows. The rows pipe directly to uninstall. |
| `Install-PSResource` | `Install-ManagedModule` | Supported for modules | Name, typed find output, exact/bounded/range versions, required-resource maps/files, dependency closure, scope/custom root, prerelease, reinstall, license, clobber policy, signature checks, credentials, proxy, planning, `WhatIf`, and typed results. |
| `Save-PSResource` | `Save-ManagedModule` | Supported for modules | Name or typed find output, current-directory default, unpacked or `-AsNupkg`, `-IncludeXml`, dependencies, exact/bounded/range versions, credentials, proxy, planning, `WhatIf`, and typed results. |
| `Update-PSResource` | `Update-ManagedModule` | Supported for modules | Named or discovered installed modules, version policies, scope/custom root, prerelease, reinstall, license, clobber and signature policy, credentials, proxy, planning, and `WhatIf`. |
| `Uninstall-PSResource` | `Uninstall-ManagedModule` | Supported for modules | Name/wildcard or typed installed input, exact/range version targeting, prerelease, scope/custom root, dependency and loaded-module safety, planning, `WhatIf`, and `Confirm`. |
| `Publish-PSResource` | `Publish-ManagedModule` | Supported for modules | Packs a module folder or publishes an existing `-NupkgPath`; supports local and NuGet-compatible publish targets, credentials, proxy, manifest validation policy, dependency checks, overwrite policy, and typed results. `-ModulePrefix` for Microsoft Artifact Registry is an explicit transport gap. |
| `Compress-PSResource` | `Compress-ManagedResource` | Supported for modules | Creates a module `.nupkg` without publishing it. Script compression remains an explicit non-module gap. |

The supported module commands use the shared C# repository, package,
dependency, integrity, receipt, and promotion services. They do not invoke
PowerShellGet, PSResourceGet, PackageManagement, or another PowerShell process.

## Repository Lifecycle

| PSResourceGet 1.2.0 | Managed command | Status | Notes |
| --- | --- | --- | --- |
| `Get-PSResourceRepository` | `Get-ManagedModuleRepository` | Supported | Returns saved managed profiles with source, trust, priority, API version, and readiness information. |
| `Register-PSResourceRepository` | `Register-ManagedModuleRepository` | Managed difference | Registers named or hashtable-defined profiles and provides a PSGallery preset. Microsoft Artifact Registry onboarding is separate through `Initialize-ManagedModuleRepository`. |
| `Set-PSResourceRepository` | `Set-ManagedModuleRepository` | Supported | Updates managed profile settings and can validate readiness. |
| `Reset-PSResourceRepository` | `Reset-ManagedModuleRepository` | Supported | Restores managed defaults without changing PSResourceGet's separate repository store. |
| `Unregister-PSResourceRepository` | `Unregister-ManagedModuleRepository` | Supported | Removes managed profiles. `Remove-ManagedModuleRepository` remains the managed removal surface. |
| `Import-PSGetRepository` | `Import-ManagedModuleRepository` | Explicit gap | Imports an explicit managed profile file. Direct discovery and conversion from the PowerShellGet v2 repository store is not implemented. |

Managed repository profiles intentionally live in their own store. Registered
name resolution is deterministic; when no profile is selected, module commands
use one explicit source instead of querying every repository by priority. This
keeps the hot path predictable and fast. Azure Artifacts credential-provider
bootstrap and Microsoft Artifact Registry package transport remain explicit
provider gaps; direct credentials and supported NuGet endpoints remain usable.

## Important Parameter Mappings

| PSResourceGet behavior | Managed behavior | Status |
| --- | --- | --- |
| `-Name` and pipeline names | By-value names are supported where PSResourceGet accepts them. Typed find/install/save and get/uninstall pipelines are also supported. | Supported for modules |
| `-Version` | Exact versions, wildcard expressions, and NuGet ranges are supported. Install/save/update also expose bounded and policy-based selection. | Supported for modules |
| `-Prerelease` | `-Prerelease` with `-AllowPrerelease` as a compatibility alias where appropriate. | Supported for modules |
| `-InputObject` | Typed `Find-ManagedModule` output pipes to install/save; typed `Get-ManagedModule` output pipes to uninstall. | Supported for modules |
| `-RequiredResource` / `-RequiredResourceFile` | Module batch installs support hashtable, JSON, and PowerShell data-file specifications. | Supported for modules |
| `-AsNupkg` / `-IncludeXml` | Both save forms are supported. They are mutually exclusive because PSGet XML metadata belongs beside unpacked module content. | Supported for modules |
| `-NupkgPath` | Existing packages can be published without repacking. | Supported for modules |
| `-NoClobber` | No-clobber is the managed default; `-NoClobber` is accepted and `-AllowClobber` is the explicit opt-in. | Managed difference |
| `-Reinstall` | Accepted for install; it maps to the managed selected-version replacement behavior also exposed by `-Force`. | Supported for modules |
| `-Quiet` | Suppresses optional host summaries while typed pipeline output remains available. | Managed difference |
| `-TrustRepository` | The reusable engine never prompts. Use trusted profiles and `-RequireTrustedRepository` rather than a prompt-suppression switch whose meaning would be inverted. | Managed difference |
| `-PassThru` | Managed lifecycle commands always return typed result or plan objects; an extra output-enabling switch is unnecessary. | Managed difference |
| `-TemporaryPath` | Staging is owned and cleaned by the engine. `-PackageCacheDirectory` is available for durable package-cache control. | Managed difference |
| `-AuthenticodeCheck` | Extracted signable files are checked before promotion on Windows. Complete catalog policy, timestamp, and short-lived certificate-chain evidence remains open. | Explicit gap |
| Repository priority fanout | Profiles store priority, but lifecycle commands do not silently fan out across all profiles. Select a profile or source explicitly. | Managed difference |
| `-CredentialProvider` | Static credentials and API keys are supported; automatic provider bootstrap is not. | Explicit gap |
| `-ModulePrefix` | Microsoft Artifact Registry prefix application is not supported by the current package transport. | Explicit gap |

## Script And Adjacent Resource Scope

Script metadata authoring and validation are supported through
`New-ManagedScriptFileInfo`, `Get-ManagedScriptFileInfo`,
`Test-ManagedScriptFileInfo`, and `Update-ManagedScriptFileInfo`. Managed script
save and install are available through `Save-ManagedScript` and
`Install-ManagedScript`.

The remaining non-module lifecycle gaps are script find, update, uninstall,
publish, and compression. Command-name, DSC-resource-name, and role-capability
content search are also not part of the fast module metadata path. These gaps
must not be described as missing module lifecycle parity.

`Update-PSModuleManifest` is an adjacent manifest-authoring command rather than
a repository lifecycle operation. PSPublishModule's existing build and manifest
services remain the owner for that functionality; no duplicate managed wrapper
is planned solely for command-name parity.

## Managed Extensions

- `Repair-ManagedModule` plans and applies estate repair for missing modules,
  dependency drift, source/scope drift, family coherence, loaded-module safety,
  receipt repair, and exact-path old-version cleanup. It keeps PowerShell
  editions, scopes, custom roots, and local user profiles independent, blocks
  ambiguous missing-module destinations, and verifies post-apply convergence.
- Install, save, update, uninstall, and repair expose inspectable plans.
- Package SHA256, allowed-author, repository-trust, signature, receipt, cache,
  and transactional promotion evidence are first-class managed contracts.
- Direct repository sources and saved profiles coexist without requiring global
  registration in another module's repository store.

## Remaining Work Before A Broader Resource-Parity Claim

- [x] Complete stable PSResourceGet 1.2.0 module lifecycle parity and typed
  pipeline interoperability.
- [x] Keep module XML help and generated command docs aligned with the actual
  public surface.
- [ ] Complete catalog, timestamp, and short-lived certificate-chain proof for
  `-AuthenticodeCheck`.
- [ ] Add non-module lifecycle commands only behind a shared resource model that
  does not slow module find/install/save/update paths.
- [ ] Add provider-specific credential bootstrap or MAR transport only when the
  shared repository client can own it end to end.
