---
topic: about_PrivateGalleries
schema: 1.0.0
---
# about_PrivateGalleries

## Short Description

Explains the enterprise private gallery profile flow for Azure Artifacts and PSPublishModule.

## Long Description

PSPublishModule supports an Entra-first private gallery workflow for Azure Artifacts feeds.
The intended enterprise flow is:

1. Save non-secret feed settings in a local PSPublishModule profile.
2. Let Microsoft.PowerShell.PSResourceGet and the Azure Artifacts Credential Provider own Entra ID, MFA,
device login, and token/session caching.
3. Use the saved profile for connect, install, update, and publish configuration commands.

Profiles are created with Set-ModuleRepositoryProfile. A profile stores feed identity and local behavior:

- Azure DevOps organization
- optional Azure DevOps project
- Azure Artifacts feed
- local repository name
- preferred repository tool
- bootstrap mode
- repository trust and priority settings

Profiles intentionally do not store PATs, passwords, or Entra tokens. The default Azure Artifacts profile uses:

Tool           = PSResourceGet
BootstrapMode  = ExistingSession
Authentication = AzureArtifactsCredentialProvider

When PSResourceGet registration supports it, PSPublishModule configures Azure Artifacts repositories with
CredentialProvider = AzArtifacts. Older PSResourceGet versions fall back to their built-in Azure Artifacts URL
detection.
Install-prerequisite flows honor the selected bootstrap mode. For the default ExistingSession profile, they
upgrade PSResourceGet to the ExistingSession-capable line before installing or refreshing the Azure Artifacts
Credential Provider.

PROFILE STORAGE

Profile data is stored under the current user's local application data folder:

%LOCALAPPDATA%\PowerForge\PrivateGalleries\profiles.json

Set POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH when tests, managed desktop rollout tooling, or ephemeral build
agents need to redirect profile storage.

ENTERPRISE ROLLOUT CHECKLIST

For a managed workstation estate, keep PSPublishModule as the wrapper and leave identity/session ownership with
Microsoft tooling:

1. Publish PSPublishModule through the approved bootstrap channel users already trust.
2. Grant Azure DevOps feed access through Entra ID groups. Do not create PATs by default.
3. Use Connect-ModuleRepository -InstallPrerequisites to install or refresh PSResourceGet at the version
required by the selected bootstrap mode and install the Azure Artifacts Credential Provider on Windows
workstations. Pre-install the credential provider on non-Windows systems with Microsoft's installer.
4. Create the local feed profile once with Set-ModuleRepositoryProfile.
5. For managed rollout, export the non-secret profile with Export-ModuleRepositoryProfile and import it on
workstations with Import-ModuleRepositoryProfile.
6. Run Test-ModuleRepositoryProfile -ProfileName <name> to verify the saved profile and local prerequisite
readiness without registering or probing the repository.
7. Ask users to run Connect-ModuleRepository -ProfileName <name> -InstallPrerequisites once. This registers
the repository and triggers the normal Entra/MFA credential-provider flow when no token is cached.
8. Standardize user commands around Install-PrivateModule -ProfileName <name> and
Update-PrivateModule -ProfileName <name>.
9. Use the same profile for New-ConfigurationPublish -ProfileName <name> and
Publish-NugetPackage -ProfileName <name> so publishing and consuming resolve the same feed.
10. Run the opt-in live Pester flow against a real feed/module before announcing the feed as production-ready.

RECOMMENDED WORKSTATION FLOW

Create the profile once:

Set-ModuleRepositoryProfile `
-Name Company `
-AzureDevOpsOrganization contoso `
-AzureDevOpsProject Platform `
-AzureArtifactsFeed Modules

Connect and validate the workstation:

Test-ModuleRepositoryProfile -ProfileName Company
Connect-ModuleRepository -ProfileName Company -InstallPrerequisites

Install or update modules:

Install-PrivateModule -ProfileName Company -Name ModuleA, ModuleB -InstallPrerequisites
Update-PrivateModule -ProfileName Company -Name ModuleA, ModuleB

Use the same profile in publish configuration:

New-ConfigurationPublish -ProfileName Company -Enabled

Direct NuGet package pushes can also resolve the feed from the profile:

Publish-NugetPackage -Path .\artifacts -ProfileName Company -SkipDuplicate

MANAGED PROFILE DEPLOYMENT

Profile files are configuration, not credentials. An administrator can create the profile once, export it, and
deploy the JSON file with Intune, GPO, configuration management, or a bootstrap script:

Export-ModuleRepositoryProfile -Name Company -Path .\Company.profile.json -Force

On the target workstation, import or refresh the profile:

Import-ModuleRepositoryProfile -Path .\Company.profile.json -Overwrite
Test-ModuleRepositoryProfile -ProfileName Company
Connect-ModuleRepository -ProfileName Company -InstallPrerequisites

The imported profile still does not contain PATs, passwords, or tokens. If the user has not signed in before,
the first connect/install/update operation triggers the normal Azure Artifacts Credential Provider Entra/MFA flow.

PRODUCTION READINESS EVIDENCE

Before calling a feed ready for users, prove:

- Connect-ModuleRepository -ProfileName <name> -InstallPrerequisites succeeds and reports
AccessProbeSucceeded = True.
- Install-PrivateModule -ProfileName <name> -Name <known module> succeeds with no PAT or explicit
credential parameters.
- Update-PrivateModule -ProfileName <name> -Name <known module> succeeds for an installed private module.
- New-ConfigurationPublish -ProfileName <name> -Enabled produces an Azure Artifacts v3 URI and no stored
credential.
- Publish-NugetPackage -ProfileName <name> succeeds for a disposable package when publish validation is
intentionally enabled.

PAT FALLBACK

PAT/basic credential parameters remain available for legacy or constrained environments, but they are not the
preferred enterprise path. Prefer the Azure Artifacts Credential Provider whenever Azure DevOps Services and
workstation policy allow it.

OTHER PRIVATE FEEDS

The profile model is provider-shaped, but the only managed provider implemented today is Azure Artifacts.
JFrog and generic NuGet v3 feeds should be added as explicit providers/adapters with their own credential
policy instead of overloading Azure Artifacts behavior.

## Examples

```text
PS> Set-ModuleRepositoryProfile -Name Company -AzureDevOpsOrganization contoso -AzureDevOpsProject Platform -AzureArtifactsFeed Modules

Saves an Entra-first Azure Artifacts profile named Company.

PS> Connect-ModuleRepository -ProfileName Company -InstallPrerequisites

Installs missing prerequisites if needed, registers the repository, and validates authenticated feed access.

PS> Test-ModuleRepositoryProfile -ProfileName Company

Checks saved profile and local prerequisite readiness without changing repository registrations.

PS> Export-ModuleRepositoryProfile -Name Company -Path .\Company.profile.json -Force

Exports the non-secret Company profile for managed rollout.

PS> Import-ModuleRepositoryProfile -Path .\Company.profile.json -Overwrite

Imports or refreshes managed profile settings on a workstation.

PS> Install-PrivateModule -ProfileName Company -Name ModuleA -InstallPrerequisites

Installs ModuleA from the saved private gallery profile.

PS> Publish-NugetPackage -Path .\artifacts -ProfileName Company -SkipDuplicate

Pushes local NuGet packages to the saved Azure Artifacts feed using credential-provider authentication.
```

## Notes

This file is source content for generated module documentation.
