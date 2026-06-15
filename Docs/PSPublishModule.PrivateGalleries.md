# PSPublishModule Private Galleries

This guide explains how to consume and publish private PowerShell modules with
PSPublishModule and PowerForge. It is written for the common operator decision:
"I have a private NuGet-compatible feed. Which command shape should I use?"

The short answer:

- Use the generic NuGet-compatible flow when you already know the feed URLs and
  the feed behaves like a normal NuGet v2/v3 repository.
- Use the Azure Artifacts preset when the feed is in Azure DevOps and users
  should authenticate with Entra ID/MFA through the Azure Artifacts Credential
  Provider.
- Use the JFrog preset when the feed is Artifactory and you want PSPublishModule
  to derive the NuGet URLs from `JFrogBaseUri` and `JFrogRepository`.
- Use profiles for workstation onboarding and repeated install/update commands.
- Use publish configuration for build/release pipelines.

For the Evotec Azure Artifacts feed runbook, see
`Docs/PSPublishModule.PrivateGalleryProcess.md`.

## Mental Model

PowerShell module publishing still uses NuGet underneath:

- `PSResourceGet` usually talks to a NuGet v3 endpoint.
- `PowerShellGet` usually talks to a NuGet v2 endpoint.
- A repository can require a push API key, a basic/PAT credential, an external
  credential provider, or some combination of those.
- Profiles store repository shape and local behavior only. They must not store
  PATs, passwords, Entra tokens, or JFrog access tokens.

Use `PSResourceGet` unless you have a specific reason to support older
PowerShellGet-only clients.

## Which Flow Should I Pick?

| Feed or scenario | Recommended path | Why |
| --- | --- | --- |
| Any NuGet v3/v2-compatible feed with known URLs | Generic NuGet-compatible configuration | It is explicit and works for most vendors. |
| Azure DevOps Artifacts | Azure Artifacts preset/profile | It derives endpoints and uses the Azure Artifacts Credential Provider. |
| JFrog Artifactory | JFrog preset | It derives Artifactory NuGet URLs and supports PAT/basic, API key, OIDC, and CLI bootstrap options. |
| GitHub Packages | GitHub Packages profile | It resolves `https://nuget.pkg.github.com/<owner>/index.json`; see `Docs/PSPublishModule.GitHubPackages.md`. |
| Microsoft-owned modules from MAR | Microsoft Artifact Registry | MAR is read-only discovery/install only, not a publish target. |
| Missing RequiredModules in a private feed | `-PublishRequiredModules` | Mirrors required dependencies into the target feed before publishing the main module. |

## Standard NuGet-Compatible Feed

Use this when the private gallery gives you standard NuGet feed URLs.

Typical examples:

- Sonatype Nexus NuGet repository
- ProGet NuGet feed
- GitHub Packages NuGet feed
- JFrog Artifactory when you prefer to provide the URLs yourself
- any internal NuGet v2/v3 service

### Publish With A NuGet API Key

Use this when the feed expects a NuGet API key for package push:

```powershell
New-ConfigurationPublish `
    -Type PowerShellGallery `
    -RepositoryName 'CompanyModules' `
    -Tool PSResourceGet `
    -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' `
    -RepositorySourceUri 'https://packages.company.test/nuget/v3/index.json' `
    -RepositoryPublishUri 'https://packages.company.test/nuget/v3/index.json' `
    -FilePath "$env:USERPROFILE\.secrets\company-nuget-api-key.txt" `
    -Enabled
```

Use `-ApiKey` only for local tests. Prefer `-FilePath` or a CI secret expanded
at runtime.

### Publish With Basic/PAT Credentials

Some feeds use the same username/token for read probes and package push. For a
generic feed, create a non-secret profile once, then pass the credential at
publish time:

```powershell
Set-ModuleRepositoryProfile `
    -Name 'CompanyNuGet' `
    -Provider NuGet `
    -RepositoryName 'CompanyModules' `
    -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' `
    -RepositorySourceUri 'https://packages.company.test/nuget/v3/index.json' `
    -RepositoryPublishUri 'https://packages.company.test/nuget/v3/index.json' `
    -Tool PSResourceGet

New-ConfigurationPublish `
    -ProfileName 'CompanyNuGet' `
    -RepositoryCredentialUserName 'publisher' `
    -RepositoryCredentialSecretEnvironmentVariable 'COMPANY_NUGET_TOKEN' `
    -Enabled
```

That keeps the URL/profile reusable while keeping the token outside committed
configuration.

### Connect And Install From A Generic Feed

For users or support engineers:

```powershell
Connect-ModuleRepository `
    -Provider NuGet `
    -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' `
    -Name 'CompanyModules' `
    -Tool PSResourceGet `
    -CredentialUserName 'reader' `
    -CredentialSecretFilePath "$env:USERPROFILE\.secrets\company-feed-token.txt" `
    -InstallPrerequisites

Install-PrivateModule `
    -Name 'Company.Tools' `
    -Repository 'CompanyModules' `
    -CredentialUserName 'reader' `
    -CredentialSecretFilePath "$env:USERPROFILE\.secrets\company-feed-token.txt"
```

For repeated workstation usage, prefer a profile and `-ProfileName`.

## Azure Artifacts

Use this for Azure DevOps feeds. This is the preferred enterprise flow when
users should authenticate with Entra ID/MFA instead of PATs.

PSPublishModule stores:

- organization
- optional project
- feed
- local repository name
- preferred tool/bootstrap settings

It does not store Entra tokens, PATs, or passwords. Authentication stays with
`Microsoft.PowerShell.PSResourceGet` and the Azure Artifacts Credential Provider.

### Create Or Import A Profile

Create a profile directly:

```powershell
Set-ModuleRepositoryProfile `
    -Name 'Company' `
    -AzureDevOpsOrganization 'contoso' `
    -AzureDevOpsProject 'Platform' `
    -AzureArtifactsFeed 'Modules'
```

One-command workstation onboarding:

```powershell
Initialize-ModuleRepository `
    -ProfileName 'Company' `
    -InstallPrerequisites
```

Import a managed profile file:

```powershell
Initialize-ModuleRepository `
    -Path .\Company.profile.json `
    -ProfileName 'Company' `
    -Overwrite `
    -InstallPrerequisites
```

### Install And Update

```powershell
Install-PrivateModule -ProfileName 'Company' -Name 'Company.Tools' -InstallPrerequisites
Update-PrivateModule  -ProfileName 'Company' -Name 'Company.Tools'
```

The wrappers refresh repository registration and run the access
probe/session-prime step before install or update. If the cached credential
provider session expired, the normal command can prompt the user again.

### Publish A PowerShell Module

Direct preset:

```powershell
New-ConfigurationPublish `
    -AzureDevOpsOrganization 'contoso' `
    -AzureDevOpsProject 'Platform' `
    -AzureArtifactsFeed 'Modules' `
    -RepositoryName 'CompanyModules' `
    -Tool PSResourceGet `
    -Enabled
```

Profile-backed:

```powershell
New-ConfigurationPublish -ProfileName 'Company' -Enabled
```

The generated configuration resolves the Azure Artifacts v3 endpoint and does
not store a credential object. The user or automation identity still needs feed
push permission.

### Publish Plain NuGet Packages

```powershell
Publish-NugetPackage `
    -Path .\artifacts `
    -ProfileName 'Company' `
    -InstallPrerequisites `
    -SkipDuplicate
```

Use this for `.nupkg` files that are not PowerShell module publishing outputs.

### When To Use PATs

PAT/basic credential parameters still exist for legacy or constrained
environments:

```powershell
Install-PrivateModule `
    -Name 'Company.Tools' `
    -AzureDevOpsOrganization 'contoso' `
    -AzureDevOpsProject 'Platform' `
    -AzureArtifactsFeed 'Modules' `
    -CredentialUserName 'user@contoso.com' `
    -CredentialSecretFilePath "$env:USERPROFILE\.secrets\azdo.pat"
```

Treat this as a fallback. Prefer the Azure Artifacts Credential Provider when
Azure DevOps Services and workstation policy allow it.

## JFrog Artifactory

JFrog Artifactory is a NuGet-compatible private PowerShell module repository.
You can use it two ways:

- generic NuGet URLs, as shown in the standard NuGet section;
- JFrog shortcut parameters, which derive URLs for you.

For a SaaS Artifactory instance:

```text
JFrogBaseUri:    https://company.jfrog.io/artifactory
JFrogRepository: powershell-virtual

PSResourceGet v3:
https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json

PowerShellGet v2 source/publish:
https://company.jfrog.io/artifactory/api/nuget/powershell-virtual
```

### Option 1: PAT Or Access Token As Repository Credential

Use this when the JFrog PAT/access token is accepted for both read/probe and
publish operations. This is the simplest local and CI shape.

```powershell
New-ConfigurationPublish `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -RepositoryCredentialUserName 'name@company.com' `
    -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' `
    -Enabled
```

For a quick local test, inline clear text works but should not be committed:

```powershell
New-ConfigurationPublish `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -RepositoryCredentialUserName 'name@company.com' `
    -RepositoryCredentialSecret 'temporary-pat' `
    -Enabled
```

### Option 2: Separate NuGet API Key Plus Repository Credential

Use this only when Artifactory requires a push API key in addition to a
repository credential for authenticated read/probe operations:

```powershell
New-ConfigurationPublish `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -FilePath "$env:USERPROFILE\.secrets\jfrog-nuget-api-key.txt" `
    -RepositoryCredentialUserName 'name@company.com' `
    -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt" `
    -Enabled
```

If the feed does not require a separate NuGet API key, do not pass `-FilePath`
or `-ApiKey`; use repository credentials only.

### Option 3: JFrog OIDC In CI

Use this when the CI runner can obtain a short-lived OIDC token and JFrog is
configured to exchange it. PSPublishModule calls JFrog CLI at publish time,
parses the returned username/access token, and passes that credential to the
repository tooling. The exchanged access token is not written into the publish
configuration.

```powershell
New-ConfigurationPublish `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -JFrogOidcProvider 'azure-oidc' `
    -JFrogOidcProviderType Azure `
    -JFrogOidcTokenIdEnvironmentVariable 'JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID' `
    -Enabled
```

Provider type choices:

- `GitHub` for GitHub Actions OIDC.
- `Azure` for Azure DevOps or Entra-backed JFrog OIDC mappings.
- `GenericOidc` for other OIDC-compatible CI providers.

Use `-JFrogPlatformUri` when the platform URL cannot be derived from
`JFrogBaseUri`.

### Option 4: JFrog CLI Browser Login For Workstations

Use this as an interactive bootstrap/proof path, not as the default CI publish
configuration:

```powershell
Connect-ModuleRepository `
    -Provider JFrog `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -Name 'JFrogPS' `
    -Tool PSResourceGet `
    -BootstrapMode JFrogCli `
    -InstallPrerequisites `
    -Verbose
```

This runs `jf login` and then probes whether PowerShell repository tooling can
use the resulting session. If the CLI login succeeds but PSResourceGet still
cannot read the NuGet feed, the result reports that boundary instead of hiding
it.

### JFrog Profile For Users

Profiles are useful for workstation onboarding because they store the feed
shape once:

```powershell
Set-ModuleRepositoryProfile `
    -Name 'JFrogPS' `
    -Provider JFrog `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -Trusted $true

Install-PrivateModule `
    -Name 'Company.Tools' `
    -ProfileName 'JFrogPS' `
    -CredentialUserName 'name@company.com' `
    -CredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt"
```

The profile is non-secret. The credential stays in a prompt, environment
variable, CI secret, or local secret file.

## Publishing Missing RequiredModules

Private feeds often start empty. If your module manifest contains
`RequiredModules`, the main module publish can fail because the target feed does
not yet contain those dependencies.

Use `-PublishRequiredModules` to opt in to mirroring missing dependencies before
publishing the main module:

```powershell
New-ConfigurationPublish `
    -JFrogBaseUri 'https://company.jfrog.io/artifactory' `
    -JFrogRepository 'powershell-virtual' `
    -RepositoryName 'JFrogPS' `
    -Tool PSResourceGet `
    -RepositoryCredentialUserName 'name@company.com' `
    -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' `
    -PublishRequiredModules `
    -RequiredModuleSourceRepository PSGallery `
    -Enabled
```

Behavior:

1. PSPublishModule reads the built module manifest.
2. It checks each `RequiredModules` entry in the target repository.
3. It skips modules listed in `PSData.ExternalModuleDependencies`.
4. For missing entries, it finds a compatible version in
   `RequiredModuleSourceRepository`.
5. It saves that dependency locally and publishes it to the target repository
   using the target publish credential.
6. It verifies the dependency is now present, then publishes the main module.

This is intentionally opt-in. It changes the target feed by adding dependency
packages. Use it only when your release process is allowed to promote those
dependencies.

If the target repository returns `401 Unauthorized` while checking a dependency,
that is still a repository credential/read-access problem. Dependency mirroring
only helps after the target feed can be read and written.

## Microsoft Artifact Registry

Microsoft Artifact Registry (MAR) is the Microsoft-controlled source for
Microsoft-owned PowerShell packages. It is read-only for consumers and is a
PSResourceGet container-registry repository.

Use it for discovery/install, not publishing:

```powershell
Register-ModuleRepository -MicrosoftArtifactRegistry
Connect-ModuleRepository -MicrosoftArtifactRegistry

Install-PrivateModule `
    -MicrosoftArtifactRegistry `
    -Name Microsoft.PowerShell.SecretManagement
```

For production estates, promote approved Microsoft packages into your enterprise
feed and point production automation at that enterprise feed.

## Profiles And Deployment

Profiles live here by default:

```text
User:    %LOCALAPPDATA%\PowerForge\PrivateGalleries\profiles.json
Machine: %ProgramData%\PowerForge\PrivateGalleries\profiles.json
```

Commands resolve user profiles first, then machine profiles. This lets desktop
support deploy a non-secret feed definition once while each user still
authenticates as themselves.

Useful profile commands:

```powershell
Test-ModuleRepositoryProfile -ProfileName 'Company'
Export-ModuleRepositoryProfile -Name 'Company' -Path .\Company.profile.json -Force
Import-ModuleRepositoryProfile -Path .\Company.profile.json -Scope Machine -Overwrite
Remove-ModuleRepositoryProfile -Name 'Company'
```

Generate a one-folder onboarding package:

```powershell
New-ModuleRepositoryBootstrap `
    -ProfileName 'Company' `
    -OutputDirectory .\CompanyGalleryBootstrap `
    -InstallModule 'Company.Tools' `
    -Force
```

The package contains feed metadata and bootstrap scripts only. It must not
contain PATs, passwords, Entra tokens, JFrog tokens, or credential-provider
session caches.

## Validation Checklist

Before announcing a feed as ready:

1. Register/connect the repository from a clean shell.
2. Install a known module through `Install-PrivateModule`.
3. Update the same module through `Update-PrivateModule`.
4. Generate publish configuration and confirm secrets are not stored.
5. Publish a disposable package or module version.
6. If dependency mirroring is enabled, prove a missing dependency is promoted
   to the target feed before the main module publish.

Azure Artifacts live validation helper:

```powershell
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName Company.Tools `
    -GenerateDisposablePackage `
    -EvidenceFile .\private-gallery-live.evidence.json `
    -Output Detailed `
    -PassThru
```

JFrog SSO/credential evidence helper:

```powershell
.\Module\Tests\Invoke-PrivateGalleryJFrogSsoValidation.ps1 `
    -JFrogBaseUri https://company.jfrog.io/artifactory `
    -Repository powershell-virtual `
    -ModuleName Company.Tools `
    -RunJFrogCliLogin `
    -EvidenceFile .\jfrog-sso.evidence.json `
    -MarkdownFile .\jfrog-sso.evidence.md
```

Keep real organization, feed, repository, and module names in secure run notes
or test tickets when they are not safe for committed docs.
