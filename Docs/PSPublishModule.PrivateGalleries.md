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
4. Create the profile once with `Set-ModuleRepositoryProfile`. Use the default
   user scope for one-user machines, or `-Scope Machine` from an elevated/admin
   deployment when the same non-secret feed definition should be visible to all
   users on the workstation. The profile contains feed identity only; it does
   not contain PATs, passwords, or tokens.
5. For managed rollout, export the non-secret profile with
   `Export-ModuleRepositoryProfile` and ask users or desktop support to run
   `Initialize-ModuleRepository -Path <profile.json> -ProfileName <name>
   -Overwrite -InstallPrerequisites` once. This imports or refreshes the
   profile, registers the repository, probes access, and, when the first
   ExistingSession probe cannot use a cached token, invokes the Azure Artifacts
   Credential Provider interactively so the user can complete Entra/MFA and
   cache a session token for later install/update/publish commands.
6. If the profile is already deployed into the profile store, use
   `Initialize-ModuleRepository -ProfileName <name> -InstallPrerequisites`
   instead. Add `-SkipConnect` when you only want profile/readiness output
   without repository registration or probing.
7. Keep `Set-ModuleRepositoryProfile`, `Test-ModuleRepositoryProfile`, and
   `Connect-ModuleRepository` available as the advanced/manual flow for admins,
   diagnostics, and constrained rollouts.
8. Standardize install/update commands around `Install-PrivateModule
   -ProfileName <name>` and `Update-PrivateModule -ProfileName <name>`.
   These commands refresh the repository registration and run the same access
   probe/session-prime step before installing or updating, so an expired Azure
   Artifacts Credential Provider session can be renewed from the normal
   day-to-day command in an interactive shell.
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

If you distribute a pre-created profile file, redirect the user profile store
with `POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH`, redirect the machine profile
store with `POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH`, or place
`profiles.json` in the default user or machine profile store. Keep that file
non-secret and user-writable only when users should be allowed to edit profile
definitions.

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

Machine-wide profiles are stored under the machine common application data
folder:

```text
%ProgramData%\PowerForge\PrivateGalleries\profiles.json
```

Commands that consume a profile, such as `Install-PrivateModule -ProfileName
Company`, `Update-PrivateModule`, `Connect-ModuleRepository`,
`Initialize-ModuleRepository`, `New-ConfigurationPublish`, and
`Publish-NugetPackage`, resolve user profiles first and then machine-wide
profiles. This lets desktop support register feed metadata once for everyone
without sharing anyone's token cache. Each user still authenticates as
themselves through the Azure Artifacts Credential Provider and Entra/MFA.

Set `POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH` to redirect user profile
storage in tests or automation. Set
`POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH` to redirect the machine-wide
store for tests or managed desktop deployment tooling.

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

For a machine-wide profile installed once by desktop support or software
distribution, import or create it with `-Scope Machine`:

```powershell
Import-ModuleRepositoryProfile -Path .\Company.profile.json -Scope Machine -Overwrite
# or
Set-ModuleRepositoryProfile -Name Company -Organization contoso -Project Platform -Feed Modules -Scope Machine
```

After that, any user on the workstation can run `Install-PrivateModule
-ProfileName Company -Name ModuleA`. PSPublishModule reads the shared profile,
but Azure Artifacts authentication remains per-user. If that user has no cached
session yet, or the session expired, the normal install/update access probe can
invoke the Azure Artifacts Credential Provider so the user signs in with their
own Entra ID/MFA.

On the target workstation, import or refresh the profile and connect in one
step:

```powershell
Initialize-ModuleRepository -Path .\Company.profile.json -ProfileName Company -Overwrite -InstallPrerequisites
```

When desktop support, Intune, GPO, or another software distribution system
needs a single folder to deploy, generate a bootstrap package from the saved
profile:

```powershell
New-ModuleRepositoryBootstrap `
    -ProfileName Company `
    -OutputDirectory .\CompanyGalleryBootstrap `
    -InstallModule ModuleA, ModuleB `
    -Force
```

The package contains `profiles.json` and `Initialize-PrivateGallery.ps1`. The
script imports PSPublishModule when needed, imports the bundled profile,
installs missing private-gallery prerequisites unless `-SkipInstallPrerequisites`
is used, connects with `Initialize-ModuleRepository`, and can install approved
modules through `Install-PrivateModule -ProfileName <name>`. The package remains
non-secret: it contains feed identity and bootstrap commands only, not PATs,
passwords, Entra tokens, or Azure Artifacts Credential Provider session caches.

The imported profile still does not contain secrets. If the user has not signed
in before, `Initialize-ModuleRepository` and `Connect-ModuleRepository` can
prime the Azure Artifacts Credential Provider for the feed URI so the user can
complete Entra/MFA and cache a session token. PSResourceGet itself calls the
provider in non-interactive mode after that, so the explicit priming step is
what gives managed workstation onboarding a real prompt/cache path.

`Install-PrivateModule -ProfileName <name>` and `Update-PrivateModule
-ProfileName <name>` also perform this access probe/session-prime step before
installing or updating modules. If the cached session expired after onboarding,
or a different user receives the same non-secret profile, the first normal
install/update command can invoke the Azure Artifacts Credential Provider again
and then continue once Entra/MFA succeeds.

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
metadata, and sanitized live assertion details such as bootstrap package
generation, non-secret package contents, bootstrap script execution, access
probe success, credential-free publish configuration, install/update execution,
and optional package-push evidence. When `-EvidenceFile` is used, the helper
also treats missing or incomplete required assertion details as a validation
failure so the JSON artifact cannot look successful while omitting the evidence
operators need. For publish proof, use either `-PublishPackagePath` with a
prebuilt disposable package or `-GenerateDisposablePackage` to create a unique
non-secret validation package for the run.

The repository also includes a manual GitHub Actions workflow named
`Private Gallery Live Validation`. Use it when you want the same proof captured
as a workflow run with downloadable evidence artifacts. Dispatch it with:

- `organization`, `project`, `feed`, and `moduleName` for the Azure Artifacts
  feed and an existing module to install/update.
- `generateDisposablePackage = true` when the run should also push a generated
  disposable package to the feed.
- `runnerLabels` set to a JSON array for the runner that owns the enterprise
  authentication policy, for example `["self-hosted","windows"]`.

For a managed repository, define GitHub variables once and leave the matching
manual inputs blank during dispatch:

- `PSPUBLISHMODULE_AZDO_ORGANIZATION`
- `PSPUBLISHMODULE_AZDO_PROJECT` for project-scoped feeds
- `PSPUBLISHMODULE_AZDO_FEED`
- `PSPUBLISHMODULE_AZDO_MODULE_NAME`
- `PSPUBLISHMODULE_AZDO_PROFILE_NAME`
- `PSPUBLISHMODULE_AZDO_RUNNER_LABELS`
- `PSPUBLISHMODULE_AZDO_DISPOSABLE_PACKAGE_NAME`
- `PSPUBLISHMODULE_AZDO_DISPOSABLE_PACKAGE_VERSION`

Workflow inputs override variables when both are provided. The pre-merge
`Test & Build Module` workflow uses the same variable names for its opt-in
`PrivateGalleryLiveValidation` job.

Before dispatching a live run, check the repository configuration without
printing any secret values:

```powershell
.\Module\Tests\Test-PrivateGalleryGitHubLiveValidationConfiguration.ps1 `
    -Repository EvotecIT/PSPublishModule `
    -RequireUnattendedCredentialProviderSecret `
    -Markdown
```

Use `-NoFail` for a report-only inventory. Without `-NoFail`, the helper fails
when required variables are missing or, when
`-RequireUnattendedCredentialProviderSecret` is used, when none of the supported
Azure Artifacts Credential Provider secret names is present. The Markdown output
includes concrete `gh variable set`, `gh secret set`, and `gh workflow run`
command shapes with placeholders so operators can move from inventory to a live
validation run without putting secret material in profiles, logs, or docs.

Prefer a self-hosted Windows runner that is allowed to use the Azure Artifacts
Credential Provider and already has a cached or policy-provided identity for
the target feed. Hosted runners generally do not have the interactive or cached
enterprise identity context needed to prove the Entra-first path. In unattended
validation outside a signed-in workstation, configure the Azure Artifacts
Credential Provider using its supported endpoint environment variables, such as
`ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS` for access-token based
automation or `ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS` for managed
identity/service-principal based automation. PSPublishModule does not write
those secrets into profiles. The included workflows pass through the matching
GitHub secrets when they exist:

- `PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS` to
  `ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS`
- `PSPUBLISHMODULE_AZDO_ARTIFACTS_FEED_ENDPOINTS` to
  `ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS`
- `PSPUBLISHMODULE_AZDO_VSS_NUGET_EXTERNAL_FEED_ENDPOINTS` to the legacy
  `VSS_NUGET_EXTERNAL_FEED_ENDPOINTS`

The evidence JSON and step summary report only whether each unattended
credential-provider endpoint variable was configured, never the secret value.
The workflow adds a non-secret run summary from the evidence JSON and uploads
`private-gallery-live.xml` and `private-gallery-live.evidence.json` as the
`private-gallery-live-validation` artifact.

Because GitHub only exposes brand-new `workflow_dispatch` workflows after the
workflow file exists on the default branch, pre-merge validation can also run
through the existing `Test & Build Module` workflow (`BuildModule.yml`). Dispatch
that workflow against the feature branch and set:

- `privateGalleryLiveValidation = true`
- `privateGalleryOrganization`, `privateGalleryProject`, `privateGalleryFeed`,
  and `privateGalleryModuleName`
- `privateGalleryGenerateDisposablePackage = true` when package-push proof is
  required
- `privateGalleryRunnerLabels` to the approved runner JSON array

That gated job uses the same validation helper, writes the same step summary,
and uploads the same evidence artifact. Normal push and pull request runs do not
run the live Azure Artifacts job.

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
  reports `Connection.AccessProbeSucceeded = True`. If no cached token existed
  before the first probe, `Connection.CredentialProviderSessionPrimeAttempted`
  shows whether PSPublishModule invoked the provider to prime the Entra-backed
  session before retrying.
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
