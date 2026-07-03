---
topic: about_PrivateGalleries
schema: 1.0.0
---
# about_PrivateGalleries

## Short Description

Explains how PSPublishModule consumes and publishes private PowerShell modules
from NuGet-compatible feeds, Azure Artifacts, JFrog Artifactory, GitHub
Packages, and Microsoft Artifact Registry.

## Long Description

PSPublishModule treats private PowerShell galleries as NuGet-compatible
repositories with a small set of reusable command shapes:

- generic NuGet-compatible feeds when you know the source/publish URLs
- Azure Artifacts profiles for Entra ID/MFA and the Azure Artifacts
Credential Provider
- JFrog Artifactory shortcuts that derive the NuGet URLs for you
- GitHub Packages profiles for GitHub-hosted NuGet feeds
- Microsoft Artifact Registry for read-only Microsoft package intake

Use PSResourceGet unless you have a specific reason to support an older
PowerShellGet-only feed. Profiles store repository shape and local behavior
only. They do not store PATs, passwords, Entra tokens, JFrog tokens, or
credential-provider session caches.

STANDARD NUGET-COMPATIBLE FEEDS

Use the generic NuGet-compatible path for feeds such as Nexus, ProGet,
GitHub Packages, internal NuGet servers, or JFrog when you prefer to provide
the URLs yourself.

If the feed expects a NuGet API key for push:

```powershell
New-ConfigurationPublish -Type PowerShellGallery -RepositoryName 'CompanyModules' -Tool PSResourceGet -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' -RepositorySourceUri 'https://packages.company.test/nuget/v3/index.json' -RepositoryPublishUri 'https://packages.company.test/nuget/v3/index.json' -FilePath "$env:USERPROFILE\.secrets\company-nuget-api-key.txt" -Enabled
```

If the feed uses basic/PAT credentials, create a non-secret profile and pass
the credential when publishing:

```powershell
Set-ManagedModuleRepository -Name 'CompanyNuGet' -Provider NuGet -RepositoryName 'CompanyModules' -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' -RepositorySourceUri 'https://packages.company.test/nuget/v3/index.json' -RepositoryPublishUri 'https://packages.company.test/nuget/v3/index.json' -Tool PSResourceGet
```

```powershell
New-ConfigurationPublish -ProfileName 'CompanyNuGet' -RepositoryCredentialUserName 'publisher' -RepositoryCredentialSecretEnvironmentVariable 'COMPANY_NUGET_TOKEN' -Enabled
```

AZURE ARTIFACTS

Azure Artifacts is the preferred enterprise flow when users should
authenticate through Entra ID/MFA instead of PATs. PSPublishModule stores
the organization, project, feed, repository name, and tool/bootstrap
preference. Authentication remains owned by PSResourceGet and the Azure
Artifacts Credential Provider.

Create a profile:

```powershell
Set-ManagedModuleRepository -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules'
```

Onboard a workstation:

```powershell
Initialize-ManagedModuleRepository -ProfileName 'Company' -InstallPrerequisites
```

Install and update modules:

```powershell
Install-ManagedModule -ProfileName 'Company' -Name 'Company.Tools'
Update-ManagedModule  -ProfileName 'Company' -Name 'Company.Tools'
```

Publish a module:

```powershell
New-ConfigurationPublish -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -RepositoryName 'CompanyModules' -Tool PSResourceGet -Enabled
```

Or use the saved profile:

```powershell
New-ConfigurationPublish -ProfileName 'Company' -Enabled
```

Push plain NuGet packages through the same profile:

```powershell
Publish-NugetPackage -Path .\artifacts -ProfileName 'Company' -InstallPrerequisites -SkipDuplicate
```

PAT/basic parameters remain available for constrained environments, but
they are a fallback. Prefer the Azure Artifacts Credential Provider when
Azure DevOps Services and workstation policy allow it.

JFROG ARTIFACTORY

JFrog can be configured as a generic NuGet feed, or with JFrog shortcut
parameters. Given:

```powershell
JFrogBaseUri    = https://company.jfrog.io/artifactory
JFrogRepository = powershell-virtual
```

PSPublishModule derives:

PSResourceGet v3:
https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json

PowerShellGet v2 source/publish:
https://company.jfrog.io/artifactory/api/nuget/powershell-virtual

For PAT/access-token publishing where the same token can read and push:

```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -Enabled
```

For local testing, inline clear text works but must not be committed:

```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecret 'temporary-pat' -Enabled
```

If Artifactory requires a separate NuGet API key for package push, add
FilePath or ApiKey:

```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -FilePath "$env:USERPROFILE\.secrets\jfrog-nuget-api-key.txt" -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt" -Enabled
```

For federated CI, use JFrog OIDC token exchange. PSPublishModule calls
JFrog CLI at publish time and passes the exchanged short-lived credential
to repository tooling:

```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -JFrogOidcProvider 'azure-oidc' -JFrogOidcProviderType Azure -JFrogOidcTokenIdEnvironmentVariable 'JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID' -Enabled
```

For interactive workstation proof, use JFrog CLI bootstrap mode:

```powershell
Initialize-ManagedModuleRepository -Provider JFrog -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -Name 'JFrogPS' -Tool PSResourceGet -BootstrapMode JFrogCli -InstallPrerequisites -Verbose
```

This runs jf login and then probes whether PowerShell repository tooling can
use that session. It is useful for troubleshooting, but it is not the default
CI publish shape.

PUBLISHING MISSING REQUIREDMODULES

Private feeds often start empty. If a module manifest contains
RequiredModules, publishing the main module can fail because the dependency
is not yet in the target feed.

Opt in to dependency mirroring:

```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -PublishRequiredModules -RequiredModuleSourceRepository PSGallery -Enabled
```

Dependency mirroring requires PSResourceGet. If a configuration uses
PublishRequiredModules with Tool PowerShellGet, the publish run fails early
with a clear error instead of silently skipping dependency mirroring.

The publish flow checks RequiredModules in the target feed, skips modules
listed in PSData.ExternalModuleDependencies, saves a compatible dependency
from RequiredModuleSourceRepository, publishes it to the target, verifies it
is present, and then publishes the main module.

This changes the target feed, so it is explicit. If the target repository
returns 401 Unauthorized while checking a dependency, fix repository
credentials first. Dependency mirroring only helps after the target feed can
be read and written.

MICROSOFT ARTIFACT REGISTRY

Microsoft Artifact Registry is a read-only PSResourceGet
container-registry repository for Microsoft-owned PowerShell packages.

```powershell
Initialize-ManagedModuleRepository -MicrosoftArtifactRegistry
Install-PSResource -Repository MAR -Name Microsoft.PowerShell.SecretManagement -TrustRepository
```

Do not use MAR as a publish target. For production estates, promote approved
Microsoft packages into your enterprise feed.

PROFILE STORAGE

User profiles:

```powershell
%LOCALAPPDATA%\PowerForge\PrivateGalleries\profiles.json
```

Machine profiles:

```powershell
%ProgramData%\PowerForge\PrivateGalleries\profiles.json
```

Commands read user profiles first, then machine profiles. This allows
desktop support to deploy non-secret feed metadata while each user still
authenticates as themselves.

Useful commands:

```powershell
Get-ManagedModuleRepository -ProfileName 'Company' -Test
Get-ManagedModuleRepository -Name 'Company' -ExportPath .\Company.profile.json -Force
Initialize-ManagedModuleRepository -Path .\Company.profile.json -Scope Machine -Overwrite
Initialize-ManagedModuleRepository -ProfileName 'Company' -BootstrapPath .\CompanyGalleryBootstrap -InstallModule 'Company.Tools' -BootstrapForce
```

VALIDATION

Before calling a feed ready:

1. Register/connect the repository from a clean shell.
2. Install a known module with Install-ManagedModule.
3. Update the module with Update-ManagedModule.
4. Generate publish configuration and confirm secrets are not stored.
5. Publish a disposable package or module version.
6. If PublishRequiredModules is enabled, prove a missing dependency is
promoted before the main module publish.

Azure Artifacts live validation:

```powershell
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 -Organization contoso -Project Platform -Feed Modules -ModuleName Company.Tools -GenerateDisposablePackage -EvidenceFile .\private-gallery-live.evidence.json -Output Detailed -PassThru
```

JFrog SSO/credential validation:

```powershell
.\Module\Tests\Invoke-PrivateGalleryJFrogSsoValidation.ps1 -JFrogBaseUri https://company.jfrog.io/artifactory -Repository powershell-virtual -ModuleName Company.Tools -RunJFrogCliLogin -EvidenceFile .\jfrog-sso.evidence.json -MarkdownFile .\jfrog-sso.evidence.md
```

## Examples


```powershell
PS> New-ConfigurationPublish -Type PowerShellGallery -RepositoryName CompanyModules -Tool PSResourceGet -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' -FilePath "$env:USERPROFILE\.secrets\company-nuget-api-key.txt" -Enabled
```

Configures a standard NuGet-compatible private feed that uses a NuGet API key for package push.

```powershell
PS> Initialize-ManagedModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites
```

Creates and connects an Azure Artifacts profile with Entra/MFA-capable credential-provider authentication.

```powershell
PS> New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -Enabled
```

Configures JFrog Artifactory publishing with PAT/access-token authentication provided from an environment variable.

```powershell
PS> New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -PublishRequiredModules -RequiredModuleSourceRepository PSGallery -Enabled
```

Configures JFrog publishing and opts in to pushing missing RequiredModules from PSGallery into the private target feed before publishing the main module.

## Notes

The longer maintainer guide is Docs\PSPublishModule.PrivateGalleries.md.
This file is source content for generated module documentation.
