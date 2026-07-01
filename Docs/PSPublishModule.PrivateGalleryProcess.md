# PSPublishModule Private Gallery Process

This note is the short maintainer and end-user process for using the Evotec
Azure Artifacts feed as a private PowerShell module gallery.

Feed:

```text
Organization: evotecpl
Project:      PowerShellGallery
Feed:         PowerShellGalleryFeed
Repository:   EvotecPowerShellGallery
```

## Goal

Maintainers publish approved modules, including PSPublishModule itself, to the
private feed. Zone users install or update from that feed without PATs, manual
NuGet configuration, or special per-command credentials.

Authentication is handled by Microsoft.PowerShell.PSResourceGet and the Azure
Artifacts Credential Provider. If the user has a valid cached Entra/Azure
DevOps session, commands use it silently. If the session is missing or expired,
the credential provider prompts once and caches the refreshed session.

## Maintainer Publish Flow

Add one publish target to the module build configuration. In this repo it lives
beside the PSGallery and GitHub publish lines in
`Module/Build/Build-Module.ps1`:

```powershell
New-ConfigurationPublish -AzureDevOpsOrganization 'evotecpl' -AzureDevOpsProject 'PowerShellGallery' -AzureArtifactsFeed 'PowerShellGalleryFeed' -RepositoryName 'EvotecPowerShellGallery' -Tool PSResourceGet -Enabled:$true
```

Optional maintainer preflight:

```powershell
Initialize-ManagedModuleRepository -ProfileName EvotecPowerShellGallery -Organization evotecpl -Project PowerShellGallery -Feed PowerShellGalleryFeed -InstallPrerequisites
```

The preflight installs or refreshes prerequisites, registers/probes the feed,
and primes the cached Entra/Azure DevOps session when needed. It is useful for
first setup, but the publish target also registers or refreshes the repository
internally before version checks and publishing.

Then run the normal build/publish flow:

```powershell
.\Build\Build-Module.ps1
```

Repeat publishing is the same process. Azure Artifacts will reject the same
package ID/version if it is already present, so publish a new module version for
each release.

## End-User Bootstrap

Recommended first-time user setup, using PSGallery only to obtain
PSPublishModule:

```powershell
Install-Module PSPublishModule -Scope CurrentUser

Initialize-ManagedModuleRepository -ProfileName EvotecPowerShellGallery -Organization evotecpl -Project PowerShellGallery -Feed PowerShellGalleryFeed -InstallPrerequisites
```

After that, normal install/update commands use the private feed profile:

```powershell
Install-ManagedModule -ProfileName EvotecPowerShellGallery -Name PSPublishModule
Update-ManagedModule -ProfileName EvotecPowerShellGallery -Name PSPublishModule
```

For other approved private modules, replace `PSPublishModule` with the module
name.

## Direct PSResourceGet Option

When PSPublishModule is already available and the repository profile has been
initialized, native PSResourceGet commands also work:

```powershell
Install-PSResource -Name PSPublishModule -Repository EvotecPowerShellGallery -TrustRepository
Update-PSResource -Name PSPublishModule
```

The PSPublishModule managed commands remain preferred for users because they use
the saved profile and the managed install/update engine. Run
`Initialize-ManagedModuleRepository -ProfileName EvotecPowerShellGallery` again
when a workstation needs repository registration, prerequisites, or a refreshed
credential-provider session.

## What Users Should Expect

- No PAT, username, or password is required for normal interactive users.
- If the credential provider cache is valid, install/update runs without a
  prompt.
- If the cache is empty or expired, the Azure Artifacts Credential Provider
  prompts through the normal Entra-capable browser or device-code flow.
- After successful login, later commands use the cached session.
- A synthetic probe package warning is expected when verbose output is enabled;
  it means the feed was reached but the fake probe package does not exist.

## Proof From Current Validation

The process has been validated with PSPublishModule published to the private
feed and then installed locally through:

```powershell
Install-ManagedModule -ProfileName EvotecPowerShellGallery -Name PSPublishModule
```

The verbose output showed the repository was refreshed, the access probe reached
the feed through PSResourceGet, ExistingSession auth was used, and the installed
version satisfied the requirement.
