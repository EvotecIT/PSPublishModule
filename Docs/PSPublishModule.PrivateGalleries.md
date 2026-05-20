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
3. Let `Initialize-ModuleRepository -InstallPrerequisites` install or refresh
   `Microsoft.PowerShell.PSResourceGet` at the version required by the selected
   bootstrap mode and install the Azure Artifacts Credential Provider on Windows
   workstations. On non-Windows systems, pre-install the credential provider
   with the official Microsoft installer before onboarding.
4. Create the local profile once with `Set-ModuleRepositoryProfile`. The profile
   contains feed identity only; it does not contain PATs, passwords, or tokens.
5. For managed rollout, export the non-secret profile with
   `Export-ModuleRepositoryProfile` and ask users or desktop support to run
   `Initialize-ModuleRepository -Path <profile.json> -ProfileName <name>
   -Overwrite -InstallPrerequisites` once. This imports or refreshes the
   profile, registers the repository, probes access, and triggers the normal
   Entra/MFA credential-provider flow when a token is not already cached.
6. If the profile is already deployed into the profile store, use
   `Initialize-ModuleRepository -ProfileName <name> -InstallPrerequisites`
   instead. Add `-SkipConnect` when you only want profile/readiness output
   without repository registration or probing.
7. Keep `Set-ModuleRepositoryProfile`, `Test-ModuleRepositoryProfile`, and
   `Connect-ModuleRepository` available as the advanced/manual flow for admins,
   diagnostics, and constrained rollouts.
8. Standardize install/update commands around `Install-PrivateModule
   -ProfileName <name>` and `Update-PrivateModule -ProfileName <name>`.
9. For publishers and CI operators, use the same profile with
   `New-ConfigurationPublish -ProfileName <name>` and `Publish-NugetPackage
   -ProfileName <name>` so package push and package consumption resolve the same
   feed.
10. Run the opt-in live Pester flow against at least one real feed/module before
   announcing the feed as production-ready.

The normal user command set should be short:

```powershell
Initialize-ModuleRepository -Path .\Company.profile.json -ProfileName Company -Overwrite -InstallPrerequisites
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

Connect and validate access on a workstation with the one-command onboarding
wrapper:

```powershell
Initialize-ModuleRepository -ProfileName Company -InstallPrerequisites
```

Use `Test-ModuleRepositoryProfile` and `Connect-ModuleRepository` directly when
you want separate readiness and connection/probe steps.

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

`-InstallPrerequisites` honors these Entra-first defaults by upgrading
PSResourceGet to the ExistingSession-capable line before it installs or refreshes
the Azure Artifacts Credential Provider.

## Managed Profile Deployment

Profile files are safe to treat as configuration, not credentials. An
administrator can create the profile on a staging machine, export it, and deploy
that JSON file with Intune, GPO, configuration management, or a bootstrap script:

```powershell
Set-ModuleRepositoryProfile -Name Company -Organization contoso -Project Platform -Feed Modules
Export-ModuleRepositoryProfile -Name Company -Path .\Company.profile.json -Force
```

On the target workstation, import or refresh the profile and connect in one
step:

```powershell
Initialize-ModuleRepository -Path .\Company.profile.json -ProfileName Company -Overwrite -InstallPrerequisites
```

The imported profile still does not contain secrets. If the user has not signed
in before, the first connect/install/update operation triggers the normal Azure
Artifacts Credential Provider Entra/MFA flow.

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
