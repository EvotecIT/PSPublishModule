# PSPublishModule Private Galleries

This page describes the supported enterprise flow for consuming private modules
from Azure Artifacts with PSPublishModule.

## Recommended Azure Artifacts Flow

Use Microsoft Entra ID/MFA through Microsoft.PowerShell.PSResourceGet and the
Azure Artifacts Credential Provider. PSPublishModule stores only non-secret feed
profile settings such as organization, project, feed, repository name, and tool
preferences. The Entra token/session cache remains owned by the Azure Artifacts
Credential Provider.

When registering Azure Artifacts feeds through PSResourceGet, PSPublishModule
sets the repository credential provider to `AzArtifacts` when the installed
PSResourceGet version exposes that parameter, and falls back to PSResourceGet's
Azure Artifacts URL detection for older versions.

## Enterprise Rollout Checklist

For a managed workstation estate, treat PSPublishModule as the operator-facing
wrapper and leave identity/session ownership with Microsoft tooling:

1. Publish PSPublishModule to the approved bootstrap channel users already trust
   (PSGallery mirror, Azure Artifacts, Intune package, or internal software
   distribution).
2. Ensure users have Azure DevOps access to the target feed through Entra ID
   groups. Do not create PATs by default.
3. Let `Connect-ModuleRepository -InstallPrerequisites` install or refresh
   `Microsoft.PowerShell.PSResourceGet` and the Azure Artifacts Credential
   Provider on Windows workstations. On non-Windows systems, pre-install the
   credential provider with the official Microsoft installer before onboarding.
4. Create the local profile once with `Set-ModuleRepositoryProfile`. The profile
   contains feed identity only; it does not contain PATs, passwords, or tokens.
5. Ask users to run `Connect-ModuleRepository -ProfileName <name>
   -InstallPrerequisites` once. This registers the repository and triggers the
   normal Entra/MFA credential-provider flow when a token is not already cached.
6. Standardize install/update commands around `Install-PrivateModule
   -ProfileName <name>` and `Update-PrivateModule -ProfileName <name>`.
7. For publishers and CI operators, use the same profile with
   `New-ConfigurationPublish -ProfileName <name>` and `Publish-NugetPackage
   -ProfileName <name>` so package push and package consumption resolve the same
   feed.
8. Run the opt-in live Pester flow against at least one real feed/module before
   announcing the feed as production-ready.

The normal user command set should be short:

```powershell
Set-ModuleRepositoryProfile -Name Company -Organization contoso -Project Platform -Feed Modules
Connect-ModuleRepository -ProfileName Company -InstallPrerequisites
Install-PrivateModule -ProfileName Company -Name ModuleA
Update-PrivateModule -ProfileName Company -Name ModuleA
```

If you distribute a pre-created profile file, redirect the profile store with
`POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH` or place `profiles.json` in the
default profile store. Keep that file non-secret and user-writable only when
users should be allowed to edit profile definitions.

Create a profile once:

```powershell
Set-ModuleRepositoryProfile `
    -Name Company `
    -AzureDevOpsOrganization contoso `
    -AzureDevOpsProject Platform `
    -AzureArtifactsFeed Modules
```

Connect and validate access on a workstation:

```powershell
Connect-ModuleRepository -ProfileName Company -InstallPrerequisites
```

Install or update modules by profile:

```powershell
Install-PrivateModule -ProfileName Company -Name ModuleA, ModuleB -InstallPrerequisites
Update-PrivateModule -ProfileName Company -Name ModuleA, ModuleB
```

Use the same profile in module build/publish configuration:

```powershell
New-ConfigurationPublish -ProfileName Company -Enabled
```

Direct NuGet package pushes can also resolve the feed from the profile:

```powershell
Publish-NugetPackage -Path .\artifacts -ProfileName Company -SkipDuplicate
```

## Profile Storage

Profiles are stored under the current user's local application data folder:

```text
%LOCALAPPDATA%\PowerForge\PrivateGalleries\profiles.json
```

Set `POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH` to redirect profile storage in
tests, automation, or managed desktop rollouts.

Profiles intentionally do not store PATs, passwords, or Entra tokens. For Azure
Artifacts, prefer the default profile settings:

- `Tool = PSResourceGet`
- `BootstrapMode = ExistingSession`
- `AuthenticationMode = AzureArtifactsCredentialProvider`

## PAT Fallback

PAT/basic credential parameters remain available for legacy or constrained
environments:

```powershell
Install-PrivateModule `
    -Name ModuleA `
    -AzureDevOpsOrganization contoso `
    -AzureDevOpsProject Platform `
    -AzureArtifactsFeed Modules `
    -CredentialUserName user@contoso.com `
    -CredentialSecretFilePath "$env:USERPROFILE\.secrets\azdo.pat"
```

Treat this as a fallback, not the default enterprise path. Rotate tokens outside
PSPublishModule and prefer Microsoft Entra-backed credential-provider login when
Azure DevOps Services and client tooling support it.

## Other Private Feeds

The profile model is intentionally provider-shaped, but the only implemented
managed provider is currently Azure Artifacts. JFrog and generic NuGet v3 feeds
should be added as explicit providers/adapters with their own credential policy
instead of overloading Azure Artifacts behavior.

## Live Azure Artifacts Validation

The local test suite includes an opt-in live smoke test that is skipped unless
explicitly enabled. Use it against a real Azure Artifacts feed that contains at
least one module the current user may install/update:

```powershell
$env:PSPUBLISHMODULE_AZDO_LIVE = '1'
$env:PSPUBLISHMODULE_AZDO_ORGANIZATION = 'contoso'
$env:PSPUBLISHMODULE_AZDO_PROJECT = 'Platform'
$env:PSPUBLISHMODULE_AZDO_FEED = 'Modules'
$env:PSPUBLISHMODULE_AZDO_MODULE_NAME = 'ModuleA'

Invoke-Pester -Path .\Module\Tests\PrivateGallery.AzureArtifacts.Live.Tests.ps1 -Output Detailed
```

The live test creates a temporary profile store, saves an Entra-first profile,
runs `Connect-ModuleRepository`, verifies profile-backed publish configuration,
then calls `Install-PrivateModule` and `Update-PrivateModule` through the saved
profile.

Recommended production-readiness evidence:

- `Connect-ModuleRepository -ProfileName <name> -InstallPrerequisites` succeeds
  on a clean user workstation and reports `AccessProbeSucceeded = True`.
- `Install-PrivateModule -ProfileName <name> -Name <known module>` succeeds with
  no PAT or explicit credential parameters.
- `Update-PrivateModule -ProfileName <name> -Name <known module>` succeeds for
  an installed private module.
- `New-ConfigurationPublish -ProfileName <name> -Enabled` produces a repository
  configuration with an Azure Artifacts v3 URI and no stored credential.
- `Publish-NugetPackage -ProfileName <name>` succeeds for a disposable package
  when `PSPUBLISHMODULE_AZDO_PUBLISH_LIVE=1` is intentionally enabled.

To prove a real package push as well, opt in separately with a package path.
This mutates the target feed, so it is intentionally not part of the default
live smoke:

```powershell
$env:PSPUBLISHMODULE_AZDO_PUBLISH_LIVE = '1'
$env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH = 'C:\Temp\Company.Tools.1.0.0.nupkg'

Invoke-Pester -Path .\Module\Tests\PrivateGallery.AzureArtifacts.Live.Tests.ps1 -Tag Live -Output Detailed
```
