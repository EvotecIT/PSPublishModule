# PSResourceGet Parity Plan

This document is the source-of-truth plan for making the managed resource
surface behave like Microsoft.PowerShell.PSResourceGet where users reasonably
expect the same shape, while keeping the managed engine faster, more repairable,
and easier to automate.

The goal is not to alias `Install-PSResource` or surprise users by hijacking
another module's commands. The goal is to provide the same functionality,
parameters, and operational behavior through PSPublishModule-owned commands and
shared PowerForge services.

Baseline references:

- [Microsoft.PowerShell.PSResourceGet command reference](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/?view=powershellget-3.x)
- [Install-PSResource](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/install-psresource?view=powershellget-3.x)
- [Find-PSResource](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/find-psresource?view=powershellget-3.x)
- [Save-PSResource](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/save-psresource?view=powershellget-3.x)
- [Register-PSResourceRepository](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/register-psresourcerepository?view=powershellget-3.x)

Local comparison baseline:

- Installed Microsoft.PowerShell.PSResourceGet: `1.2.0`
- Current managed commands on `origin/main`: `Find-ManagedModule`,
  `Get-ManagedModule`, `Install-ManagedModule`, `Save-ManagedModule`,
  `Update-ManagedModule`, `Publish-ManagedModule`, `Repair-ManagedModule`,
  `Get-ManagedModuleRepository`, `Set-ManagedModuleRepository`,
  `Initialize-ManagedModuleRepository`, and
  `Remove-ManagedModuleRepository`
- Ready prerequisite slice: PR #497 adds latest-first install semantics,
  selected-version dependency repair, and repair-aware installed-module checks.

## Product Rules

1. Managed module behavior should feel familiar to PSResourceGet users.
   Broad `Install` requests should select the latest matching version before
   deciding whether local state is already sufficient.
2. Automatic repair is valid when the selected latest/exact installed version is
   present but incomplete. Reinstall/force semantics should still exist for full
   replacement.
3. Module hot paths must stay fast. Script resources and script metadata support
   can be added, but they must be isolated from normal module install, save,
   update, and repair planning unless explicitly requested.
4. Parity means behavior, not just parameter names. Every public compatibility
   switch needs tests that prove the observable result.
5. Intentional improvements are allowed when documented. Examples include
   dependency repair, richer planning, package hash validation, and deterministic
   custom module roots.
6. Cmdlets stay thin. Shared behavior belongs in PowerForge/PSPublishModule
   services and models, with cmdlets mapping parameters and output.

## Compatibility Status

| PSResourceGet command | Current managed surface | Status | Required parity work |
| --- | --- | --- | --- |
| `Find-PSResource` | `Find-ManagedModule` | Partial | Add command-name, DSC resource-name, tag, resource type, and include-dependency search behavior. Decide whether this becomes `Find-ManagedResource` or stays module-first with explicit resource-kind parameters. |
| `Get-InstalledPSResource` | `Get-ManagedModule` | Partial | Add resource-shaped installed output, script discovery, `-Scope`, `-Version`, and path behavior. Consider `Get-ManagedResource` for non-module resources. |
| `Install-PSResource` | `Install-ManagedModule` | Partial, improved by PR #497 | Add `-RequiredResource`, `-RequiredResourceFile`, `-InputObject`, `-NoClobber` compatibility, `-Reinstall` spelling, `-Quiet`, and script resource support. Preserve repair-aware selected-version behavior. |
| `Save-PSResource` | `Save-ManagedModule` | Partial | Add `-InputObject`, `-AsNupkg`, `-IncludeXml`, `-RequiredResource`/file if shared with install, and script resource save. |
| `Update-PSResource` | `Update-ManagedModule` | Partial | Add resource-shaped behavior, script updates, `-Quiet`, `-Reinstall`/force alignment, and `-Scope` parity. |
| `Uninstall-PSResource` | Missing | Missing | Add `Uninstall-ManagedModule` first, then shared `Uninstall-ManagedResource` if script resources land. Include dependency safety, side-by-side version targeting, scope/path targeting, `-WhatIf`, and `-Confirm`. |
| `Publish-PSResource` | `Publish-ManagedModule` | Partial | Add script path publishing, `-NupkgPath`, `-ModulePrefix`, and `-SkipModuleManifestValidate` parity where meaningful. |
| `Compress-PSResource` | Missing | Missing | Add packaging-to-nupkg support for module and script folders, likely as `Compress-ManagedResource`, reusing existing pack services. |
| `Get-PSResourceRepository` | `Get-ManagedModuleRepository` | Partial | Align output shape, default repository behavior, and priority/trusted fields. |
| `Register-PSResourceRepository` | `Set-ManagedModuleRepository` / `Initialize-ManagedModuleRepository` | Partial | Add register-shaped behavior or aliases that do not surprise: `-Name`, `-Uri`, `-Trusted`, `-Priority`, `-ApiVersion`, `-CredentialInfo`, `-CredentialProvider`, `-PSGallery`, repository hashtable input, and `-PassThru`. |
| `Set-PSResourceRepository` | `Set-ManagedModuleRepository` | Partial | Add priority, credential provider, CredentialInfo persistence shape, and repository-object input. |
| `Reset-PSResourceRepository` | `Initialize-ManagedModuleRepository` | Partial | Add reset-to-default profile/store behavior, including PSGallery and Microsoft Artifact Registry defaults where supported. |
| `Unregister-PSResourceRepository` | `Remove-ManagedModuleRepository` | Partial | Align `-PassThru`, not-found behavior, and repository store shape. |
| `Import-PSGetRepository` | `Initialize-ManagedModuleRepository -Import...` | Partial | Add explicit import from PowerShellGet v2 repositories with `-Force` overwrite semantics. |
| `New-PSScriptFileInfo` | Missing | Missing | Add script metadata authoring if scripts are in scope. Keep isolated from module hot path. |
| `Get-PSScriptFileInfo` | Missing | Missing | Add script metadata reader and validation model. |
| `Test-PSScriptFileInfo` | Missing | Missing | Add script metadata validation used by publish/compress. |
| `Update-PSScriptFileInfo` | Missing | Missing | Add safe metadata update, including signature removal behavior if supported. |
| `Update-PSModuleManifest` | Existing PowerShell cmdlet in PSResourceGet, not managed | Decision needed | Decide whether to wrap, delegate, or leave to Microsoft command. This is adjacent manifest editing, not repository resource lifecycle. |

## Parameter Parity Matrix

| Parameter or behavior | PSResourceGet surface | Managed status | Plan |
| --- | --- | --- | --- |
| `-Name` | Find/install/save/update/uninstall/get | Supported for modules | Extend to scripts/resources when those land. |
| `-Version` | Exact or NuGet range | Supported through version policy and aliases | Verify exact PSResourceGet range semantics, including single-version-as-required behavior. |
| `-Prerelease` | Includes prerelease | Supported for modules | Keep aliases such as `-AllowPrerelease`; add to script/resource paths. |
| `-Repository` | Registered names, priority order, wildcard rules | Partial | Implement repository priority/name ordering and wildcard validation compatibility. |
| `-Credential` | Repository access | Supported in managed module flows | Add CredentialInfo persistence and SecretManagement-backed repository credentials if chosen. |
| `-Scope` | CurrentUser/AllUsers | Supported for install/update/repair modules | Add to get/uninstall/resource paths and verify Windows PowerShell 5.1 paths. |
| `-TemporaryPath` | Staging path | Supported in managed internals, partially exposed | Normalize public exposure across install/save/update. |
| `-TrustRepository` | Suppresses untrusted prompt | Intentional difference | Keep managed explicit trust policies, but provide a compatibility decision before declaring parity. |
| `-Reinstall` | Overwrite selected/latest installed version | Partial via `-Force`, PR #497 repair | Add explicit `-Reinstall` spelling and document difference from repair-only fast path. |
| `-Force` | Repository overwrite/update force in some commands | Supported with managed semantics | Keep exact behavior documented; add repository command parity. |
| `-Quiet` | Suppresses progress | Missing/partial | Add as host-output/progress policy without changing engine behavior. |
| `-AcceptLicense` | Suppresses license prompts | Supported for modules | Verify dependency and batch resource behavior. |
| `-NoClobber` | Prevent command conflicts | Managed currently uses `-AllowClobber` | Add compatibility spelling or explicit mapping so PSResourceGet users are not surprised. |
| `-SkipDependencyCheck` | Install/save/update/uninstall dependency behavior | Supported for modules | Extend to batch/script resource paths; define uninstall safety. |
| `-AuthenticodeCheck` | Validates signatures/catalogs on Windows | Partial | Finish catalog/timestamp/certificate-chain parity. |
| `-PassThru` | Emits resource/repository objects | Partial | Normalize object shapes and no-output defaults. |
| `-InputObject` | Pipeline resource object input | Missing | Add typed managed resource info input and adapters from find results. |
| `-RequiredResource` | Hashtable or JSON resource specification | Missing | Add parser, model, tests, and shared install/save plan expansion. |
| `-RequiredResourceFile` | `.psd1` or `.json` resource specification | Missing | Add PSD1/JSON parser and batch semantics. |
| `-AsNupkg` | Save package as nupkg | Missing | Reuse pack/download cache services and verify package integrity. |
| `-IncludeXml` | Save package with PSGet XML metadata | Missing | Decide support level; add only if a real compatibility consumer needs it. |
| `-Path` | Save destination or installed path | Supported in module commands | Align output/input semantics per command. |
| `-Priority` | Repository order | Partial | Make repository priority first-class in managed repository store. |
| `-Trusted` | Repository trust | Supported conceptually | Align register/set/get/reset output. |
| `-CredentialInfo` | SecretManagement reference | Missing | Add only with a clear persistence and secret lookup policy. |
| `-CredentialProvider` | Azure Artifacts dynamic provider | Missing/partial | Decide whether managed credential-provider bootstrapping is in scope or explicit gap. |
| `-PSGallery` | Default PSGallery registration | Partial | Align register/reset behavior. |
| `-MicrosoftArtifactRegistry` / `-MAR` | MAR default repository | Partial | Current managed repository init has MAR concepts; align behavior and version-gate docs. |

## Milestones

### M0: Install/Repair Latest Semantics

Status: ready in PR #497, not part of this matrix branch.

- Latest-first install selection when no exact/bounding policy forces an older installed version.
- Selected installed version dependency repair without forcing a full reinstall.
- Repair-ManagedModule awareness of missing dependencies and broken installed manifests.
- Force/reinstall behavior stays available for full replacement.

### M1: Source-of-Truth Matrix And Contract Harness

Status: this document.

- Record command, parameter, and behavior parity expectations.
- Add metadata-driven tests that fail when managed cmdlet surfaces drift away from the matrix.
- Keep intentional differences explicitly named.

### M2: Installed And Uninstall Parity

- Add `Uninstall-ManagedModule`.
- Align `Get-ManagedModule` with `Get-InstalledPSResource` for module names, paths, versions, and scopes.
- Prove side-by-side version targeting, loaded-module safety, dependency safety, `-WhatIf`, and `-Confirm`.

### M3: Required Resource Batch Installs

- Add `-RequiredResource` and `-RequiredResourceFile`.
- Support PSD1 and JSON input.
- Share the expansion model across install and save where practical.
- Keep batch parsing outside the hot single-module path.

### M4: Save/Pack Parity

- Add `-AsNupkg` and `Compress-ManagedResource` or equivalent.
- Decide `-IncludeXml` support from real compatibility value.
- Verify module package hashes, dependency closure, and script package layout.

### M5: Find/Search Parity

- Add search by command name, DSC resource name, tag, type, and include dependencies.
- Keep repository search ordering compatible with priority then name.
- Validate against PSGallery and local feed fixtures.

### M6: Repository Model Parity

- Add register/reset/import-shaped behavior.
- Make priority, trust, API version, PSGallery, MAR, and hashtable registration first-class.
- Decide CredentialInfo and credential-provider scope.

### M7: Script Resource Lane

- Add script metadata commands: new/get/test/update.
- Add script install/save/publish/compress support.
- Verify this lane does not slow module-only find/install/save/update/repair benchmarks.

### M8: Cross-Version Proof And Release Readiness

- Validate each slice on Windows PowerShell 5.1 and PowerShell 7.
- Run focused contract tests plus module build gates.
- Keep benchmark rows for module hot paths and script-resource opt-in paths.
- Follow PRs through CI/review before merge.

## Done Criteria

End-to-end parity is complete only when:

- Every command in the PSResourceGet command reference is either implemented by a
  managed PSPublishModule command or documented as an intentional non-goal with
  a user-facing reason.
- Core parameters have behavior tests, not just properties in a cmdlet class.
- `Microsoft.Graph`, `Az`, and at least one script resource install/save/update
  path are validated on Windows PowerShell 5.1 and PowerShell 7.
- Module hot-path benchmarks remain competitive after script/resource support is
  added.
- Repository reset/import/register/set/unregister behavior has contract tests.
- PRs are green, delayed reviewer feedback is settled, and temporary worktrees
  are cleaned after merge or closure.
