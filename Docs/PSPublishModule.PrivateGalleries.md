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
   -ProfileName <name> -InstallPrerequisites` so package push and package
   consumption resolve the same feed and can bootstrap the same Azure Artifacts
   credential-provider path.
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
you want separate readiness and connection/probe steps. The readiness object
includes both `RecommendedOnboardingCommand` for the managed one-command path
and `RecommendedConnectCommand` for the lower-level connection step.

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
Publish-NugetPackage -Path .\artifacts -ProfileName Company -InstallPrerequisites -SkipDuplicate
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
Initialize-ModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -SkipConnect
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
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName ModuleA `
    -OutputFile .\private-gallery-live.xml `
    -EvidenceFile .\private-gallery-live.evidence.json
```

The helper sets the required environment variables for the current process,
runs the Pester harness, optionally writes an NUnit/JUnit result file, and then
restores the caller's environment, including any existing
`POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH` override. The helper throws when
Pester reports failed tests so release operators get a non-zero script outcome
instead of a false-green live validation. `-EvidenceFile` writes a non-secret
JSON evidence summary with the provider, organization, project, feed, module,
profile name, publish opt-in state, Pester counts, optional result-file
metadata, and sanitized live assertion details such as access probe success,
credential-free publish configuration, install/update execution, and optional
package-push evidence. When `-EvidenceFile` is used, the helper also treats
missing or incomplete required assertion details as a validation failure so the
JSON artifact cannot look successful while omitting the evidence operators need.
For publish proof, use either `-PublishPackagePath` with a prebuilt disposable
package or `-GenerateDisposablePackage` to create a unique non-secret validation
package for the run.

The repository also includes a manual GitHub Actions workflow named
`Private Gallery Live Validation`. Use it when you want the same proof captured
as a workflow run with downloadable evidence artifacts. Dispatch it with:

- `organization`, `project`, `feed`, and `moduleName` for the Azure Artifacts
  feed and an existing module to install/update.
- `generateDisposablePackage = true` when the run should also push a generated
  disposable package to the feed.
- `runnerLabels` set to a JSON array for the runner that owns the enterprise
  authentication policy, for example `["self-hosted","windows"]`.

Prefer a self-hosted Windows runner that is allowed to use the Azure Artifacts
Credential Provider and complete the Entra-backed sign-in/session flow for the
target feed. Hosted runners generally do not have the interactive or cached
enterprise identity context needed to prove the Entra-first path. The workflow
adds a non-secret run summary from the evidence JSON and uploads
`private-gallery-live.xml` and `private-gallery-live.evidence.json` as the
`private-gallery-live-validation` artifact.

To run the harness directly, set:

```powershell
$env:PSPUBLISHMODULE_AZDO_LIVE = '1'
$env:PSPUBLISHMODULE_AZDO_ORGANIZATION = 'contoso'
$env:PSPUBLISHMODULE_AZDO_PROJECT = 'Platform'
$env:PSPUBLISHMODULE_AZDO_FEED = 'Modules'
$env:PSPUBLISHMODULE_AZDO_MODULE_NAME = 'ModuleA'

Invoke-Pester -Path .\Module\Tests\PrivateGallery.AzureArtifacts.Live.Tests.ps1 -Output Detailed
```

The live test creates a temporary profile store, exports a non-secret managed
profile file, imports/connects it with `Initialize-ModuleRepository`, verifies
profile-backed publish configuration, then calls `Install-PrivateModule` and
`Update-PrivateModule` through the saved profile.

Recommended production-readiness evidence:

- `Initialize-ModuleRepository -Path <profile.json> -ProfileName <name>
  -Overwrite -InstallPrerequisites` succeeds on a clean user workstation and
  reports `Connection.AccessProbeSucceeded = True`.
- `Install-PrivateModule -ProfileName <name> -Name <known module>` succeeds with
  no PAT or explicit credential parameters.
- `Update-PrivateModule -ProfileName <name> -Name <known module>` succeeds for
  an installed private module.
- `New-ConfigurationPublish -ProfileName <name> -Enabled` produces a repository
  configuration with an Azure Artifacts v3 URI and no stored credential.
- `Publish-NugetPackage -ProfileName <name>` succeeds for a disposable package
  when package-push validation is intentionally enabled with
  `-PublishPackagePath` or `-GenerateDisposablePackage`.

To prove a real package push as well, opt in separately. This mutates the target
feed, so it is intentionally not part of the default live smoke. The simplest
operator path is to let the helper create a disposable package with a timestamp
version:

```powershell
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName ModuleA `
    -GenerateDisposablePackage `
    -OutputFile .\private-gallery-live-publish.xml `
    -EvidenceFile .\private-gallery-live-publish.evidence.json
```

You can also supply a prebuilt package:

```powershell
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName ModuleA `
    -PublishPackagePath 'C:\Temp\Company.Tools.1.0.0.nupkg' `
    -OutputFile .\private-gallery-live-publish.xml `
    -EvidenceFile .\private-gallery-live-publish.evidence.json
```
