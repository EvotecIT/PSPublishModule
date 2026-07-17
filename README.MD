# PSPublishModule and PowerForge

PSPublishModule started as the Evotec module builder. It has grown into the PowerShell-facing surface for **PowerForge**: a build, packaging, documentation, release, private-gallery, and static-site automation toolkit used across EvotecIT projects.

Use it when you want repeatable PowerShell module builds, .NET publish matrices, MSI/MSIX/Store packaging, NuGet and PSGallery releases, generated PowerShell help, private gallery onboarding, GitHub housekeeping, or PowerForge.Web static site pipelines without rewriting the same build logic in every repository.

## Packages

### PowerShell Module

[![PowerShell Gallery version](https://img.shields.io/powershellgallery/v/PSPublishModule.svg)](https://www.powershellgallery.com/packages/PSPublishModule)
[![PowerShell Gallery preview](https://img.shields.io/powershellgallery/v/PSPublishModule.svg?label=powershell%20gallery%20preview&colorB=yellow&include_prereleases)](https://www.powershellgallery.com/packages/PSPublishModule)
[![PowerShell Gallery platforms](https://img.shields.io/powershellgallery/p/PSPublishModule.svg)](https://www.powershellgallery.com/packages/PSPublishModule)
[![PowerShell Gallery downloads](https://img.shields.io/powershellgallery/dt/PSPublishModule.svg)](https://www.powershellgallery.com/packages/PSPublishModule)

### NuGet Packages and Tools

[![PowerForge](https://img.shields.io/nuget/v/PowerForge?label=PowerForge)](https://www.nuget.org/packages/PowerForge)
[![PowerForge downloads](https://img.shields.io/nuget/dt/PowerForge?label=PowerForge%20downloads)](https://www.nuget.org/packages/PowerForge)
[![PowerForge.PowerShell](https://img.shields.io/nuget/v/PowerForge.PowerShell?label=PowerForge.PowerShell)](https://www.nuget.org/packages/PowerForge.PowerShell)
[![PowerForge.Build](https://img.shields.io/nuget/v/PowerForge.Build?label=powerforge%20tool)](https://www.nuget.org/packages/PowerForge.Build)
[![PowerForge.Web](https://img.shields.io/nuget/v/PowerForge.Web?label=PowerForge.Web)](https://www.nuget.org/packages/PowerForge.Web)
[![PowerForge.Web.Build](https://img.shields.io/nuget/v/PowerForge.Web.Build?label=powerforge-web%20tool)](https://www.nuget.org/packages/PowerForge.Web.Build)

### Project Information

[![Build Module](https://github.com/EvotecIT/PSPublishModule/actions/workflows/BuildModule.yml/badge.svg)](https://github.com/EvotecIT/PSPublishModule/actions/workflows/BuildModule.yml)
[![DotNet publish tests](https://github.com/EvotecIT/PSPublishModule/actions/workflows/dotnetpublish-tests.yml/badge.svg)](https://github.com/EvotecIT/PSPublishModule/actions/workflows/dotnetpublish-tests.yml)
[![license](https://img.shields.io/github/license/EvotecIT/PSPublishModule.svg)](https://github.com/EvotecIT/PSPublishModule)
[![top language](https://img.shields.io/github/languages/top/EvotecIT/PSPublishModule.svg)](https://github.com/EvotecIT/PSPublishModule)

### Author and Social

[![Twitter follow](https://img.shields.io/twitter/follow/PrzemyslawKlys.svg?label=Twitter%20%40PrzemyslawKlys&style=social)](https://twitter.com/PrzemyslawKlys)
[![Blog](https://img.shields.io/badge/Blog-evotec.xyz-2A6496.svg)](https://evotec.xyz/hub)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-pklys-0077B5.svg?logo=LinkedIn)](https://www.linkedin.com/in/pklys)
[![Discord](https://img.shields.io/discord/508328927853281280?style=flat-square&label=discord%20chat)](https://evo.yt/discord)

## What It Is

PSPublishModule is the PowerShell module you install and import. PowerForge is the reusable engine underneath it. The repo also ships CLI tools:

- `powerforge` from the `PowerForge.Build` .NET tool package.
- `powerforge-web` from the `PowerForge.Web.Build` .NET tool package.

The same contracts are available from PowerShell cmdlets, JSON files, and CLI commands, so a project can start with a simple build script and later move the repeatable rules into checked-in JSON.

## Main Capabilities

| Area | What it does | Main entry points |
| --- | --- | --- |
| PowerShell module build | Builds script and binary modules, merges public/private functions, updates manifests, signs outputs, creates packed/unpacked artifacts, validates imports, runs tests, and generates help. | `Invoke-ModuleBuild`, `Build-Module`, `New-ConfigurationBuild`, `New-ConfigurationManifest`, `New-ConfigurationArtefact`, `New-ConfigurationValidation` |
| Module dependencies and isolation | Resolves required/external/approved modules, supports online dependency install, packages required modules, detects missing helpers, and imports selected modules through AssemblyLoadContext isolation profiles. | `New-ConfigurationModule`, `Get-MissingFunctions`, `Import-IsolatedModule`, `Test-IsolatedModuleProfile` |
| Documentation and delivery | Generates Markdown command docs, MAML external help, about topics, and bundled module documentation. It can also copy or render installed module docs from local files, GitHub, or Azure DevOps. | `New-ConfigurationDocumentation`, `New-ModuleAboutTopic`, `Show-ModuleDocumentation`, `Install-ModuleDocumentation`, `Install-ModuleScript` |
| Private galleries | Stores non-secret repository profiles, bootstraps feed access, and lets the managed module cmdlets use the same profile for find/save/install/update/publish. | `Set-ManagedModuleRepository`, `Get-ManagedModuleRepository`, `Initialize-ManagedModuleRepository`, `Remove-ManagedModuleRepository`, `Find-ManagedModule`, `Save-ManagedModule`, `Install-ManagedModule`, `Update-ManagedModule`, `Publish-ManagedModule` |
| Managed module lifecycle | Finds, saves, installs, updates, publishes, and maintains PowerShell modules through the managed C# engine with typed results and receipts. Benchmark tooling lives under Benchmarks for contributor evidence. | `Find-ManagedModule`, `Save-ManagedModule`, `Install-ManagedModule`, `Update-ManagedModule`, `Repair-ManagedModule`, `Publish-ManagedModule` |
| .NET publish and packaging | Builds publish matrices for apps, services, tools, bundles, plugins, MSI, MSIX, Microsoft Store packages, appinstaller files, signing, checksums, and manifests. | `New-DotNetPublishConfig`, `Invoke-DotNetPublish`, `Invoke-PowerForgeBundlePostProcess`, `Invoke-PowerForgePluginExport`, `Invoke-PowerForgePluginPack` |
| Unified release | Coordinates module artifacts, NuGet packages, tool binaries, installers, GitHub releases, staging folders, checksums, Winget manifests/submission, and release manifests from one release config. | `New-PowerForgeReleaseConfig`, `Invoke-PowerForgeRelease`, `Invoke-ProjectRelease`, `Invoke-ProjectBuild`, `powerforge release` |
| GitHub housekeeping | Prunes Actions artifacts and caches, performs runner cleanup, and provides reusable workflows/actions for cross-repo maintenance. | `powerforge github housekeeping`, `.github/workflows/powerforge-github-housekeeping.yml` |
| Home Assistant and HACS releases | Applies PR-driven semantic version policy, keeps integration/plugin metadata aligned, builds HACS assets, publishes verified GitHub releases, and resumes failed runs. | `powerforge homeassistant release`, `.github/workflows/powerforge-homeassistant-release.yml` |
| Static websites and API docs | Builds PowerForge.Web sites, docs, blogs, search indexes, API docs, project hubs, SEO assets, sitemaps, quality gates, audits, and deployment artifacts. | `powerforge-web build`, `powerforge-web pipeline`, `powerforge-web verify`, `powerforge-web audit`, `powerforge-web scaffold` |
| Project hygiene | Checks or fixes line endings/encoding, removes comments or project files, reads versions, and summarizes test failures. | `Get-ProjectConsistency`, `Convert-ProjectConsistency`, `Get-ProjectVersion`, `Set-ProjectVersion`, `Get-ModuleTestFailures` |

## Supported Runtimes

| Surface | Targets |
| --- | --- |
| PSPublishModule | Windows PowerShell 5.1 and PowerShell 7+ |
| PowerForge | .NET Framework 4.7.2, .NET 8, .NET 10 |
| PowerForge.PowerShell | .NET Framework 4.7.2, .NET 8, .NET 10 |
| PowerForge.Cli / `powerforge` | .NET 8 and .NET 10 |
| PowerForge.Web / `powerforge-web` | .NET 8 and .NET 10 |

## Install

Install the PowerShell module from PSGallery:

```powershell
Install-Module -Name PSPublishModule -Scope CurrentUser
```

If you are updating an older install or replacing a prerelease build, this is the usual maintainer-friendly command:

```powershell
Install-Module -Name PSPublishModule -AllowClobber -Force -SkipPublisherCheck
```

Install the CLI tools when you prefer JSON-first automation from shell scripts or CI:

```powershell
dotnet tool install --global PowerForge.Build
dotnet tool install --global PowerForge.Web.Build
```

Update later with:

```powershell
Update-Module -Name PSPublishModule
dotnet tool update --global PowerForge.Build
dotnet tool update --global PowerForge.Web.Build
```

If a production build depends on a known-good version, pin and test upgrades intentionally. PSPublishModule and PowerForge expose build/release contracts, so small parameter or manifest changes can affect automation.

## Quick Start: Build a PowerShell Module

Scaffold a starter module:

```powershell
Import-Module PSPublishModule

Build-Module -ModuleName 'MyModule' -Path 'C:\Git'
```

Build an existing module with a PowerShell DSL:

```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git' -Settings {
    New-ConfigurationBuild -Enable
    New-ConfigurationManifest -Description 'My module' -PowerShellVersion '5.1'
    New-ConfigurationDocumentation -Enable -Path 'Docs' -PathReadme 'Docs\Readme.md' -AboutTopicsSourcePath 'Help\About'
    New-ConfigurationArtefact -Type Packed -Enable
    New-ConfigurationValidation -Enable
}
```

Common module build outputs include:

- a refreshed module manifest,
- packed and unpacked artifacts,
- generated Markdown command docs,
- generated external help XML,
- about topic docs,
- validation/test summaries,
- signed module files when signing is configured.

See [Docs/PSPublishModule.ModuleDocumentation.md](Docs/PSPublishModule.ModuleDocumentation.md) and [Module/Docs/Invoke-ModuleBuild.md](Module/Docs/Invoke-ModuleBuild.md).

## Full Module Builder Example

The quick path creates the initial module structure, manifest, and build script:

```powershell
Import-Module PSPublishModule

Build-Module -ModuleName 'MyGreatModule' -Path 'C:\Support\GitHub'
```

The scaffold creates the standard folders and starting files needed to build and publish the module.

![New module scaffold](Assets/Images/NewPrepareModule.png)

The generated structure is intentionally boring: public/private functions, manifest, docs, tests, build script, and artifact locations are laid out in the same way across modules.

![Generated module structure](Assets/Images/NewPrepareModule1.png)

After the scaffold exists, the module can be built repeatedly with a DSL script. This is the pattern used by Evotec modules: the build script owns project-specific values, while PSPublishModule/PowerForge owns the reusable build, validation, packaging, signing, and publishing behavior.

```powershell
Build-Module -ModuleName 'MyGreatModule' -Path 'C:\Support\GitHub' {
    $Manifest = [ordered] @{
        ModuleVersion        = '1.0.0'
        CompatiblePSEditions = @('Desktop', 'Core')
        GUID                 = '330e259e-799f-415d-8247-4843127620a1'
        Author               = 'Author'
        CompanyName          = 'CompanyName'
        Copyright            = "(c) 2011 - $((Get-Date).Year) Author @ CompanyName. All rights reserved."
        Description          = 'Simple project MyGreatModule'
        PowerShellVersion    = '5.1'
        Tags                 = @('Windows', 'MacOS', 'Linux')
        ProjectUri           = 'https://github.com/CompanyName/MyGreatModule'
    }
    New-ConfigurationManifest @Manifest

    New-ConfigurationModule -Type RequiredModule -Name 'PSSharedGoods' -Guid 'Auto' -Version 'Latest'

    New-ConfigurationModule -Type ApprovedModule -Name @(
        'PSSharedGoods'
        'PSWriteColor'
        'Connectimo'
        'PSUnifi'
        'PSWebToolbox'
        'PSMyPassword'
    )

    New-ConfigurationModuleSkip -IgnoreFunctionName 'Invoke-Formatter', 'Find-Module'

    $format = [ordered] @{
        RemoveComments                              = $false
        PlaceOpenBraceEnable                        = $true
        PlaceOpenBraceOnSameLine                    = $true
        PlaceOpenBraceNewLineAfter                  = $true
        PlaceOpenBraceIgnoreOneLineBlock            = $false
        PlaceCloseBraceEnable                       = $true
        PlaceCloseBraceNewLineAfter                 = $true
        PlaceCloseBraceIgnoreOneLineBlock           = $false
        PlaceCloseBraceNoEmptyLineBefore            = $true
        UseConsistentIndentationEnable              = $true
        UseConsistentIndentationKind                = 'space'
        UseConsistentIndentationPipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
        UseConsistentIndentationIndentationSize     = 4
        UseConsistentWhitespaceEnable               = $true
        UseConsistentWhitespaceCheckInnerBrace      = $true
        UseConsistentWhitespaceCheckOpenBrace       = $true
        UseConsistentWhitespaceCheckOpenParen       = $true
        UseConsistentWhitespaceCheckOperator        = $true
        UseConsistentWhitespaceCheckPipe            = $true
        UseConsistentWhitespaceCheckSeparator       = $true
        AlignAssignmentStatementEnable              = $true
        AlignAssignmentStatementCheckHashtable      = $true
        UseCorrectCasingEnable                      = $true
    }

    New-ConfigurationFormat -ApplyTo 'OnMergePSM1', 'OnMergePSD1' -Sort None @format
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'DefaultPSM1' -EnableFormatting -Sort None
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'OnMergePSD1' -PSD1Style 'Minimal'

    New-ConfigurationDocumentation `
        -Enable `
        -PathReadme 'Docs\Readme.md' `
        -Path 'Docs' `
        -AboutTopicsSourcePath 'Help\About'

    New-ConfigurationImportModule -ImportSelf -ImportRequiredModules

    New-ConfigurationBuild `
        -Enable `
        -DeleteTargetModuleBeforeBuild `
        -MergeModuleOnBuild `
        -SignModule:$false

    New-ConfigurationArtefact `
        -Type Unpacked `
        -Enable `
        -Path "$PSScriptRoot\..\Artefacts" `
        -RequiredModulesPath "$PSScriptRoot\..\Artefacts\Modules"

    New-ConfigurationArtefact `
        -Type Packed `
        -Enable `
        -Path "$PSScriptRoot\..\Releases" `
        -IncludeTagName

    New-ConfigurationPublish `
        -Type PowerShellGallery `
        -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' `
        -Enabled:$false

    New-ConfigurationPublish `
        -Type GitHub `
        -FilePath 'C:\Support\Important\GitHubAPI.txt' `
        -UserName 'CompanyName' `
        -Enabled:$false
}
```

The older hashtable-style configuration is still supported for compatibility, but the `New-Configuration*` DSL is the preferred style for new and actively maintained modules.

## Quick Start: Private PowerShell Galleries

Create a non-secret profile for a private NuGet-compatible module feed:

```powershell
Set-ManagedModuleRepository `
    -Name Company `
    -Provider NuGet `
    -RepositoryName CompanyModules `
    -RepositoryUri 'https://packages.company.test/nuget/v3/index.json' `
    -RepositoryPublishUri 'https://packages.company.test/nuget/v3/index.json' `
    -Trusted
```

Export/import the same settings for other users or build machines:

```powershell
Get-ManagedModuleRepository -Name Company -ExportPath .\Company.profile.json -Force
Initialize-ManagedModuleRepository -Path .\Company.profile.json -Overwrite
Initialize-ManagedModuleRepository -ProfileName Company -InstallPrerequisites
```

Use the profile with the managed module lifecycle commands:

```powershell
Find-ManagedModule -ProfileName Company -Name Company.Tools
Save-ManagedModule -ProfileName Company -Name Company.Tools -Path C:\OfflineModules
Install-ManagedModule -ProfileName Company -Name Company.Tools -Scope CurrentUser
Update-ManagedModule -ProfileName Company -Name Company.Tools
```

Publish PowerShell modules through the same profile:

```powershell
Publish-ManagedModule -Path C:\Source\Company.Tools -ProfileName Company -ApiKeyFilePath C:\Secrets\company-feed-key.txt
```

`Publish-NugetPackage` remains available for raw `.nupkg` publishing. For
module lifecycle work, prefer `Publish-ManagedModule` so package creation,
metadata validation, repository resolution, and publish behavior stay in the
managed module flow.

See [Docs/PSPublishModule.PrivateGalleries.md](Docs/PSPublishModule.PrivateGalleries.md),
[Docs/PSPublishModule.ManagedModules.Compatibility.md](Docs/PSPublishModule.ManagedModules.Compatibility.md),
[Docs/PSPublishModule.PSResourceGetParity.md](Docs/PSPublishModule.PSResourceGetParity.md),
and `Get-Help about_PrivateGalleries`.

## Quick Start: Managed Module Lifecycle

The managed module commands are the C# engine path for module discovery, save,
install, update, publish, and estate maintenance. They are designed to cover
common `Find-Module`, `Save-Module`, `Install-Module`, `Update-Module`, and
`Publish-Module` workflows without depending on PowerShellGet, PSResourceGet,
PackageManagement, external executables, or embedded PowerShell scripts in the
managed delivery path.

```powershell
Find-ManagedModule -Name Company.Tools -Repository PSGallery
Save-ManagedModule -Name Company.Tools -Path C:\OfflineModules -Repository PSGallery
Install-ManagedModule -Name Company.Tools -Scope CurrentUser -Repository PSGallery
Update-ManagedModule -Name Company.Tools -Repository PSGallery
Publish-ManagedModule -Path C:\Source\Company.Tools -Repository C:\Packages
```

Managed module repository profiles can be used anywhere the lifecycle commands
accept `-ProfileName`, so private/internal feeds do not need a parallel install
or update command family.

For machine-wide maintenance, use `Repair-ManagedModule` as the operator
entrypoint. `-Plan` previews the work with typed objects and the same summary
view; removing `-Plan` applies the managed install/update/save actions.

```powershell
Repair-ManagedModule -Latest -Repository PSGallery -Plan -ShowSummary
Repair-ManagedModule -Latest -Repository PSGallery -ShowSummary
```

### Benchmarks

PSPublishModule can run reusable benchmark suites, import BenchmarkDotNet
artifacts, update marker-delimited Markdown blocks, and verify summary metrics
against a baseline. Benchmark specs live in `.benchmark.ps1` files and declare
cases, matrix axes, engines, operations, validation, comparison rules, and
requested artifacts.

The managed module benchmark measures normal module lifecycle commands against
representative module graphs. The generated table below is a one-iteration
matrix smoke over `Find`, `Install`, and `Save`; use the command below with
warmup and multiple iterations for stable local timing. Managed is the baseline
for each completed row. Other values show the relative timing and the measured
duration in parentheses. Failed rows mean the tool did not produce a valid
comparable result for that lane.

The managed install path uses PSPublishModule repository profiles, dependency
planning, package download, extraction, caching, SHA256 delivery evidence,
short-path staging, and atomic promotion into the normal versioned module
layout. Native provider install rows that target `CurrentUser` can be measured
under the `TemporaryLocalUser` benchmark profile so the real user module folder
is not changed.

<!-- managed-module-benchmark-table:start -->
| Scenario | Variables | Host | Operation | Managed | ModuleFast | PowerShellGet | PSResourceGet | Result |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| Az | AcceptLicense=True, ModuleName=Az, Version=14.0.0 | Core-7.5.5 | Find | 1.00x (210ms) | Skipped | 3.77x (791ms) | 2.57x (538ms) | Managed fastest |
| Az | AcceptLicense=True, ModuleName=Az, Version=14.0.0 | Desktop-5.1.26100.8655 | Find | 1.00x (207ms) | Skipped | 3.56x (738ms) | Skipped | Managed fastest |
| Az | AcceptLicense=True, ModuleName=Az, Version=14.0.0 | Core-7.5.5 | Install | 1.00x (3.73s) | 2.55x (9.52s) | Skipped | Skipped | Managed fastest |
| Az | AcceptLicense=True, ModuleName=Az, Version=14.0.0 | Desktop-5.1.26100.8655 | Install | 1.00x (5.59s) | Skipped | Skipped | Skipped | Managed only successful |
| Az | AcceptLicense=True, ModuleName=Az, Version=14.0.0 | Core-7.5.5 | Save | 1.00x (3.58s) | Skipped | 113.61x (406.91s) | 41.01x (146.90s) | Managed fastest |
| Az | AcceptLicense=True, ModuleName=Az, Version=14.0.0 | Desktop-5.1.26100.8655 | Save | 1.00x (5.78s) | Skipped | 29.25x (169.12s) | Skipped | Managed fastest |
| AzAccounts | AcceptLicense=True, ModuleName=Az.Accounts, Version=5.1.0 | Core-7.5.5 | Find | 1.00x (2.90s) | Skipped | 0.42x (1.21s) | 0.11x (329ms) | Managed slower than PSResourceGet |
| AzAccounts | AcceptLicense=True, ModuleName=Az.Accounts, Version=5.1.0 | Desktop-5.1.26100.8655 | Find | 1.00x (175ms) | Skipped | 4.45x (777ms) | Skipped | Managed fastest |
| AzAccounts | AcceptLicense=True, ModuleName=Az.Accounts, Version=5.1.0 | Core-7.5.5 | Install | 1.00x (588ms) | 3.00x (1.76s) | Skipped | Skipped | Managed fastest |
| AzAccounts | AcceptLicense=True, ModuleName=Az.Accounts, Version=5.1.0 | Desktop-5.1.26100.8655 | Install | 1.00x (755ms) | Skipped | Skipped | Skipped | Managed only successful |
| AzAccounts | AcceptLicense=True, ModuleName=Az.Accounts, Version=5.1.0 | Core-7.5.5 | Save | 1.00x (747ms) | Skipped | 19.92x (14.89s) | 2.33x (1.74s) | Managed fastest |
| AzAccounts | AcceptLicense=True, ModuleName=Az.Accounts, Version=5.1.0 | Desktop-5.1.26100.8655 | Save | 1.00x (694ms) | Skipped | 4.21x (2.92s) | Skipped | Managed fastest |
| Graph | AcceptLicense=True, ModuleName=Microsoft.Graph, Version=2.29.1 | Core-7.5.5 | Find | 1.00x (309ms) | Skipped | 4.40x (1.36s) | 5.18x (1.60s) | Managed fastest |
| Graph | AcceptLicense=True, ModuleName=Microsoft.Graph, Version=2.29.1 | Desktop-5.1.26100.8655 | Find | 1.00x (241ms) | Skipped | 2.92x (703ms) | Skipped | Managed fastest |
| Graph | AcceptLicense=True, ModuleName=Microsoft.Graph, Version=2.29.1 | Core-7.5.5 | Install | 1.00x (7.35s) | 0.76x (5.55s) | Skipped | Skipped | Managed slower than ModuleFast |
| Graph | AcceptLicense=True, ModuleName=Microsoft.Graph, Version=2.29.1 | Desktop-5.1.26100.8655 | Install | 1.00x (4.19s) | Skipped | Skipped | Skipped | Managed only successful |
| Graph | AcceptLicense=True, ModuleName=Microsoft.Graph, Version=2.29.1 | Core-7.5.5 | Save | 1.00x (6.88s) | Skipped | 26.92x (185.28s) | 18.19x (125.21s) | Managed fastest |
| Graph | AcceptLicense=True, ModuleName=Microsoft.Graph, Version=2.29.1 | Desktop-5.1.26100.8655 | Save | 1.00x (4.85s) | Skipped | 13.91x (67.41s) | Skipped | Managed fastest |
| GraphAuthentication | AcceptLicense=True, ModuleName=Microsoft.Graph.Authentication, Version=2.29.1 | Core-7.5.5 | Find | 1.00x (187ms) | Skipped | 2.95x (551ms) | 1.75x (327ms) | Managed fastest |
| GraphAuthentication | AcceptLicense=True, ModuleName=Microsoft.Graph.Authentication, Version=2.29.1 | Desktop-5.1.26100.8655 | Find | 1.00x (481ms) | Skipped | 2.05x (986ms) | Skipped | Managed fastest |
| GraphAuthentication | AcceptLicense=True, ModuleName=Microsoft.Graph.Authentication, Version=2.29.1 | Core-7.5.5 | Install | 1.00x (457ms) | 3.02x (1.38s) | Skipped | Skipped | Managed fastest |
| GraphAuthentication | AcceptLicense=True, ModuleName=Microsoft.Graph.Authentication, Version=2.29.1 | Desktop-5.1.26100.8655 | Install | 1.00x (620ms) | Skipped | Skipped | Skipped | Managed only successful |
| GraphAuthentication | AcceptLicense=True, ModuleName=Microsoft.Graph.Authentication, Version=2.29.1 | Core-7.5.5 | Save | 1.00x (479ms) | Skipped | 18.89x (9.05s) | 7.45x (3.57s) | Managed fastest |
| GraphAuthentication | AcceptLicense=True, ModuleName=Microsoft.Graph.Authentication, Version=2.29.1 | Desktop-5.1.26100.8655 | Save | 1.00x (713ms) | Skipped | 3.65x (2.61s) | Skipped | Managed fastest |
| SingleModule | AcceptLicense=False, ModuleName=PSScriptAnalyzer, Version=1.25.0 | Core-7.5.5 | Find | 1.00x (348ms) | Skipped | 4.99x (1.73s) | 1.42x (493ms) | Managed fastest |
| SingleModule | AcceptLicense=False, ModuleName=PSScriptAnalyzer, Version=1.25.0 | Desktop-5.1.26100.8655 | Find | 1.00x (267ms) | Skipped | 12.61x (3.36s) | Skipped | Managed fastest |
| SingleModule | AcceptLicense=False, ModuleName=PSScriptAnalyzer, Version=1.25.0 | Core-7.5.5 | Install | 1.00x (999ms) | 2.07x (2.07s) | Skipped | Skipped | Managed fastest |
| SingleModule | AcceptLicense=False, ModuleName=PSScriptAnalyzer, Version=1.25.0 | Desktop-5.1.26100.8655 | Install | 1.00x (1.18s) | Skipped | Skipped | Skipped | Managed only successful |
| SingleModule | AcceptLicense=False, ModuleName=PSScriptAnalyzer, Version=1.25.0 | Core-7.5.5 | Save | 1.00x (756ms) | Skipped | 14.83x (11.21s) | 6.53x (4.93s) | Managed fastest |
| SingleModule | AcceptLicense=False, ModuleName=PSScriptAnalyzer, Version=1.25.0 | Desktop-5.1.26100.8655 | Save | 1.00x (1.15s) | Skipped | 2.60x (2.99s) | Skipped | Managed fastest |
<!-- managed-module-benchmark-table:end -->

For the controlled identical-feed results, the default-source race between the
official PowerShell Gallery and pwsh.gallery, exact product versions/hashes, and
the Windows PowerShell 5.1 support lane, see
[Managed Module Benchmarks](Benchmarks/ManagedModules/README.md#modulefast-100-beta1-results).

Run the managed module benchmark:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Scenario SingleModule, GraphAuthentication, Graph, AzAccounts, Az `
    -Operation Find, Install, Save `
    -Engine Managed, ModuleFast, PSResourceGet, PowerShellGet `
    -Host Core, Desktop `
    -WarmupCount 1 `
    -IterationCount 3 `
    -Variable @{ UpdateReadme = $true }
```

Import existing artifacts or BenchmarkDotNet output:

```powershell
Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -OutputPath .\Build\Benchmarks\normalized.json
```

Update a README block from generated benchmark output:

```powershell
Update-BenchmarkDocument `
    -Path .\README.MD `
    -BlockId managed-module-benchmark-table `
    -ComparisonPath .\Build\Benchmarks\comparison.json `
    -Renderer ComparisonTable
```

Verify benchmark metrics in CI:

```powershell
Test-BenchmarkGate `
    -SummaryPath .\Build\Benchmarks\summary.json `
    -BaselinePath .\Build\Benchmarks\baseline.json `
    -Metric MedianMs
```

See [Docs/PSPublishModule.Benchmarking.md](Docs/PSPublishModule.Benchmarking.md)
for the benchmark command guide and [Benchmarks/ManagedModules/README.md](Benchmarks/ManagedModules/README.md)
for the managed module benchmark suite.

## Quick Start: .NET Publish and Packaging

Scaffold a JSON config:

```powershell
New-DotNetPublishConfig -ProjectRoot '.' -PassThru
```

Plan and run:

```powershell
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -Validate
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -Plan
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -ExitCode
```

The same flow is available through the CLI:

```powershell
powerforge dotnet scaffold --project-root . --output json
powerforge dotnet publish --config .\powerforge.dotnetpublish.json --plan
powerforge dotnet publish --config .\powerforge.dotnetpublish.json --output json
```

The DotNet publish engine can produce publish folders, zip files, service scripts, bundle layouts, plugin folders/packages, generated MSI authoring, MSI outputs, MSIX/Store packages, appinstaller files, manifests, checksums, and signing reports.

See [Docs/PSPublishModule.DotNetPublish.Quickstart.md](Docs/PSPublishModule.DotNetPublish.Quickstart.md).

## Quick Start: Unified Release

Generate a starter release config:

```powershell
New-PowerForgeReleaseConfig -ProjectRoot . -PassThru
```

Plan or run the release:

```powershell
Invoke-PowerForgeRelease -ConfigPath .\Build\release.json -Plan
Invoke-PowerForgeRelease -ConfigPath .\Build\release.json
```

CLI equivalent:

```powershell
powerforge release --config .\Build\release.json --plan
```

The unified release engine is designed for repositories that need several outputs at once: module packages, NuGet packages, portable tools, installers, GitHub release uploads, Winget manifests/submission, checksums, and categorized upload-ready staging folders.

See [Docs/PSPublishModule.ProjectBuild.md](Docs/PSPublishModule.ProjectBuild.md).

## Quick Start: PowerForge.Web

PowerForge.Web is the static-site engine in this repository. It is used for documentation sites, API reference portals, project hubs, blog/news content, search, SEO assets, sitemaps, quality gates, and CI-friendly audits.

Create or standardize a site:

```powershell
powerforge-web scaffold --out .\Website --name "My Site" --base-url "https://example.com" --engine scriban
```

Run a local pipeline:

```powershell
powerforge-web pipeline --config .\pipeline.json --mode dev
```

Run CI-style gates locally:

```powershell
powerforge-web pipeline --config .\pipeline.json --mode ci
```

Recommended site contracts include:

- explicit `Features` in `site.json`,
- `pipeline.json` with dev and CI modes,
- committed `.powerforge/verify-baseline.json`,
- committed `.powerforge/audit-baseline.json`,
- strict CI gates with `failOnNewWarnings` and `failOnNewIssues`,
- a theme manifest with feature contracts.

See [Docs/PowerForge.Web.WebsiteStarter.md](Docs/PowerForge.Web.WebsiteStarter.md), [Docs/PowerForge.Web.QualityGates.md](Docs/PowerForge.Web.QualityGates.md), and [Docs/PowerForge.Web.Roadmap.md](Docs/PowerForge.Web.Roadmap.md).

## GitHub Housekeeping

PowerForge can clean GitHub Actions artifacts, caches, and runner workspace pressure with safe dry-run defaults:

```powershell
powerforge github artifacts prune --name "test-results*,coverage*,github-pages"
powerforge github caches prune --key "ubuntu-*,windows-*" --keep 1 --max-age-days 14
powerforge github runner cleanup --apply --min-free-gb 20
powerforge github housekeeping --config .\.powerforge\github-housekeeping.json --apply
```

Reusable workflow example:

```yaml
permissions:
  contents: read
  actions: write

jobs:
  housekeeping:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-github-housekeeping.yml@main
    with:
      config-path: ./.powerforge/github-housekeeping.json
    secrets: inherit
```

## Command Families

PSPublishModule exports commands in these families:

- `Invoke-*`: run build, test, publish, release, plugin, and repository workflows.
- `New-Configuration*`: create DSL objects for module, .NET publish, release, installer, signing, documentation, validation, and compatibility configuration.
- `New-*Config`: scaffold JSON configuration files.
- `Get-*` / `Test-*`: inspect module metadata, repository profiles, compatibility, project consistency, versions, isolated module profiles, and test failures.
- `Find-*` / `Save-*` / `Install-*` / `Update-*` / `Repair-*`: manage module discovery, materialization, installation, update, repair, docs/scripts, and managed repository profiles.
- `Publish-*` / `Send-*`: publish NuGet packages and GitHub release assets.
- `powerforge`: JSON-first CLI for release, .NET publish, GitHub housekeeping, plugin export/pack, bundle post-process, and related automation.
- `powerforge-web`: static-site and API-docs CLI.

Generated command reference lives in [Module/Docs/Readme.md](Module/Docs/Readme.md).

## Repository Layout

| Path | Purpose |
| --- | --- |
| `PSPublishModule/` | Binary PowerShell module cmdlets. |
| `PowerForge/` | Host-neutral build, release, packaging, and workflow engine. |
| `PowerForge.PowerShell/` | PowerShell runtime adapters, module build execution, help extraction, signing, Pester, and repository tooling. |
| `PowerForge.Cli/` | `powerforge` command-line tool. |
| `PowerForge.Web/` | Static-site, API-docs, search, SEO, audit, and verification engine. |
| `PowerForge.Web.Cli/` | `powerforge-web` command-line tool. |
| `PowerForgeStudio.*` | Current PowerForge Studio experiments and hosts. |
| `Module/` | Packaged PSPublishModule output, generated command docs, external help, tests, and module build script. |
| `Docs/` | Product guides, quickstarts, configuration reference, and workflow documentation. |
| `Schemas/` | JSON schemas for PowerForge configs. |
| `Build/` | Repo release/build entrypoints and release configuration. |

## Build This Repository

Run the full solution tests:

```powershell
dotnet test .\PSPublishModule.sln -c Release
```

Run the module build:

```powershell
.\Module\Build\Build-Module.ps1
```

Run the unified release plan:

```powershell
.\Build\Build-Project.ps1 -Plan
```

Build release artifacts:

```powershell
.\Build\Build-Project.ps1
```

Build only the CLI tools:

```powershell
.\Build\Build-Project.ps1 -ToolsOnly
```

## Documentation

Start here:

- [Docs/PSPublishModule.DotNetPublish.Quickstart.md](Docs/PSPublishModule.DotNetPublish.Quickstart.md)
- [Docs/PSPublishModule.ProjectBuild.md](Docs/PSPublishModule.ProjectBuild.md)
- [Docs/PSPublishModule.ModuleDocumentation.md](Docs/PSPublishModule.ModuleDocumentation.md)
- [Docs/PSPublishModule.PrivateGalleries.md](Docs/PSPublishModule.PrivateGalleries.md)
- [Docs/PSPublishModule.ManagedModules.Compatibility.md](Docs/PSPublishModule.ManagedModules.Compatibility.md)
- [Docs/PSPublishModule.PSResourceGetParity.md](Docs/PSPublishModule.PSResourceGetParity.md)
- [Docs/PSPublishModule.ManagedModules.ReleaseReadiness.md](Docs/PSPublishModule.ManagedModules.ReleaseReadiness.md)
- [Benchmarks/ManagedModules/README.md](Benchmarks/ManagedModules/README.md)
- [Docs/PSPublishModule.ModuleIsolation.md](Docs/PSPublishModule.ModuleIsolation.md)
- [Docs/PSPublishModule.DevelopmentBootstrapper.md](Docs/PSPublishModule.DevelopmentBootstrapper.md)
- [Docs/PowerForge.Web.WebsiteStarter.md](Docs/PowerForge.Web.WebsiteStarter.md)
- [Docs/PowerForge.Web.QualityGates.md](Docs/PowerForge.Web.QualityGates.md)
- [Docs/PowerForge.Web.Roadmap.md](Docs/PowerForge.Web.Roadmap.md)
- [Docs/PowerForgeStudio.Runbook.md](Docs/PowerForgeStudio.Runbook.md)
- [Module/Docs/Readme.md](Module/Docs/Readme.md)

## Support This Project

If PSPublishModule or PowerForge saves you time, sponsorship helps keep the project maintained and improving:

- [Become a sponsor via GitHub Sponsors](https://github.com/sponsors/PrzemyslawKlys)
- [Support via PayPal](https://paypal.me/PrzemyslawKlys)

Sponsorship is optional. The project remains open-source and available for everyone.

## License

PSPublishModule and PowerForge are released under the license in [LICENSE](LICENSE).
