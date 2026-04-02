---
Module Name: PSPublishModule
Module Guid: eb76426a-1992-40a5-82cd-6480f883ef4d
Download Help Link: https://github.com/EvotecIT/PSPublishModule
Help Version: 3.0.2
Locale: en-US
---
# PSPublishModule Module
## Description
Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery

## PSPublishModule Cmdlets
### [Connect-ModuleRepository](Connect-ModuleRepository.md)
Registers an Azure Artifacts repository if needed and validates authenticated access for the selected bootstrap mode.

### [Convert-ProjectConsistency](Convert-ProjectConsistency.md)
Converts a project to a consistent encoding/line ending policy and reports the results.

### [Export-CertificateForNuGet](Export-CertificateForNuGet.md)
Exports a code-signing certificate to DER format for NuGet.org registration.

### [Export-ConfigurationProject](Export-ConfigurationProject.md)
Exports a PowerShell-authored project release object to JSON.

### [Get-MissingFunctions](Get-MissingFunctions.md)
Analyzes a script or scriptblock and reports functions/commands it calls that are not present.

### [Get-ModuleInformation](Get-ModuleInformation.md)
Gets module manifest information from a project directory.

### [Get-ModuleTestFailures](Get-ModuleTestFailures.md)
Analyzes and summarizes failed Pester tests from either a Pester results object or an NUnit XML result file.

### [Get-PowerShellAssemblyMetadata](Get-PowerShellAssemblyMetadata.md)
Gets the cmdlets and aliases in a .NET assembly by scanning for cmdlet-related attributes.

### [Get-PowerShellCompatibility](Get-PowerShellCompatibility.md)
Analyzes PowerShell files and folders to determine compatibility with Windows PowerShell 5.1 and PowerShell 7+.

### [Get-ProjectConsistency](Get-ProjectConsistency.md)
Provides comprehensive analysis of encoding and line ending consistency across a project.

### [Get-ProjectVersion](Get-ProjectVersion.md)
Retrieves project version information from .csproj, .psd1, and build scripts.

### [Import-ConfigurationProject](Import-ConfigurationProject.md)
Imports a PowerShell-authored project release object from JSON.

### [Install-PrivateModule](Install-PrivateModule.md)
Installs one or more modules from a private repository, optionally bootstrapping Azure Artifacts registration first.

### [Invoke-DotNetPublish](Invoke-DotNetPublish.md)
Executes DotNet publish engine from DSL settings or an existing JSON config.

### [Invoke-DotNetReleaseBuild](Invoke-DotNetReleaseBuild.md)
Builds a .NET project in Release configuration and prepares release artefacts.

### [Invoke-DotNetRepositoryRelease](Invoke-DotNetRepositoryRelease.md)
Repository-wide .NET package release workflow (discover, version, pack, publish).

### [Invoke-ModuleBuild](Invoke-ModuleBuild.md)
Creates/updates a module structure and triggers the build pipeline (legacy DSL compatible).

### [Invoke-ModuleTestSuite](Invoke-ModuleTestSuite.md)
Complete module testing suite that handles dependencies, imports, and test execution.

### [Invoke-PowerForgeBundlePostProcess](Invoke-PowerForgeBundlePostProcess.md)
Applies reusable bundle post-process rules from a dotnet publish config to an existing bundle directory.

### [Invoke-PowerForgePluginExport](Invoke-PowerForgePluginExport.md)
Exports plugin folders from a reusable PowerForge plugin catalog configuration.

### [Invoke-PowerForgePluginPack](Invoke-PowerForgePluginPack.md)
Packs plugin-related NuGet packages from a reusable PowerForge plugin catalog configuration.

### [Invoke-PowerForgeRelease](Invoke-PowerForgeRelease.md)
Executes the unified repository release workflow from a JSON configuration.

### [Invoke-ProjectBuild](Invoke-ProjectBuild.md)
Executes a repository-wide .NET build/release pipeline from a JSON configuration.

### [Invoke-ProjectRelease](Invoke-ProjectRelease.md)
Executes a PowerShell-authored project release object through the unified PowerForge release engine.

### [New-ConfigurationArtefact](New-ConfigurationArtefact.md)
Tells the module to create an artefact of a specified type.

### [New-ConfigurationBuild](New-ConfigurationBuild.md)
Allows configuring the build process for a module.

### [New-ConfigurationCommand](New-ConfigurationCommand.md)
Defines a command import configuration for the build pipeline.

### [New-ConfigurationCompatibility](New-ConfigurationCompatibility.md)
Creates configuration for PowerShell compatibility checking during module build.

### [New-ConfigurationDelivery](New-ConfigurationDelivery.md)
Configures delivery metadata for bundling and installing internal docs/examples.

### [New-ConfigurationDocumentation](New-ConfigurationDocumentation.md)
Enables or disables creation of documentation from the module using PowerForge.

### [New-ConfigurationDotNetBenchmarkGate](New-ConfigurationDotNetBenchmarkGate.md)
Creates a benchmark gate definition for DotNet publish DSL.

### [New-ConfigurationDotNetBenchmarkMetric](New-ConfigurationDotNetBenchmarkMetric.md)
Creates a benchmark metric extraction rule for DotNet publish gates.

### [New-ConfigurationDotNetConfigBootstrapRule](New-ConfigurationDotNetConfigBootstrapRule.md)
Creates config bootstrap copy rules for DotNet publish service packages.

### [New-ConfigurationDotNetInstaller](New-ConfigurationDotNetInstaller.md)
Creates installer configuration (MSI prepare/build) for DotNet publish DSL.

### [New-ConfigurationDotNetMatrix](New-ConfigurationDotNetMatrix.md)
Creates matrix defaults and include/exclude filters for DotNet publish DSL.

### [New-ConfigurationDotNetMatrixRule](New-ConfigurationDotNetMatrixRule.md)
Creates a matrix include/exclude rule for DotNet publish DSL.

### [New-ConfigurationDotNetProfile](New-ConfigurationDotNetProfile.md)
Creates a named profile for DotNet publish DSL.

### [New-ConfigurationDotNetProject](New-ConfigurationDotNetProject.md)
Creates a project catalog entry for DotNet publish DSL.

### [New-ConfigurationDotNetPublish](New-ConfigurationDotNetPublish.md)
Creates DotNet publish configuration using DSL objects from a settings script block.

### [New-ConfigurationDotNetService](New-ConfigurationDotNetService.md)
Creates service packaging options for DotNet publish targets.

### [New-ConfigurationDotNetServiceLifecycle](New-ConfigurationDotNetServiceLifecycle.md)
Creates service lifecycle execution options for DotNet publish service targets.

### [New-ConfigurationDotNetServiceRecovery](New-ConfigurationDotNetServiceRecovery.md)
Creates service recovery options for DotNet publish service targets.

### [New-ConfigurationDotNetSign](New-ConfigurationDotNetSign.md)
Creates signing options for DotNet publish targets and installers.

### [New-ConfigurationDotNetState](New-ConfigurationDotNetState.md)
Creates preserve/restore state options for DotNet publish targets.

### [New-ConfigurationDotNetStateRule](New-ConfigurationDotNetStateRule.md)
Creates a preserve/restore rule for DotNet publish state handling.

### [New-ConfigurationDotNetTarget](New-ConfigurationDotNetTarget.md)
Creates a DotNet publish target entry for DotNet publish DSL.

### [New-ConfigurationExecute](New-ConfigurationExecute.md)
Reserved placeholder for future execution-time configuration.

### [New-ConfigurationFileConsistency](New-ConfigurationFileConsistency.md)
Creates configuration for file consistency checking (encoding and line endings) during module build.

### [New-ConfigurationFormat](New-ConfigurationFormat.md)
Builds formatting options for code and manifest generation during the build.

### [New-ConfigurationImportModule](New-ConfigurationImportModule.md)
Creates a configuration for importing PowerShell modules.

### [New-ConfigurationInformation](New-ConfigurationInformation.md)
Describes what to include/exclude in the module build and how libraries are organized.

### [New-ConfigurationManifest](New-ConfigurationManifest.md)
Creates a configuration manifest for a PowerShell module.

### [New-ConfigurationModule](New-ConfigurationModule.md)
Provides a way to configure required, external, or approved modules used in the project.

### [New-ConfigurationModuleSkip](New-ConfigurationModuleSkip.md)
Provides a way to ignore certain commands or modules during build process and continue module building on errors.

### [New-ConfigurationPlaceHolder](New-ConfigurationPlaceHolder.md)
Helps define custom placeholders replacing content within a script or module during the build process.

### [New-ConfigurationProject](New-ConfigurationProject.md)
Creates a PowerShell-first project/release object for the unified PowerForge release engine.

### [New-ConfigurationProjectInstaller](New-ConfigurationProjectInstaller.md)
Creates an installer entry for a PowerShell-authored project build.

### [New-ConfigurationProjectOutput](New-ConfigurationProjectOutput.md)
Creates output-root and staging defaults for a PowerShell-authored project build.

### [New-ConfigurationProjectRelease](New-ConfigurationProjectRelease.md)
Creates release-level defaults for a PowerShell-authored project build.

### [New-ConfigurationProjectSigning](New-ConfigurationProjectSigning.md)
Creates signing defaults for a PowerShell-authored project build.

### [New-ConfigurationProjectTarget](New-ConfigurationProjectTarget.md)
Creates a high-level target entry for a PowerShell-authored project build.

### [New-ConfigurationProjectWorkspace](New-ConfigurationProjectWorkspace.md)
Creates workspace-validation defaults for a PowerShell-authored project build.

### [New-ConfigurationPublish](New-ConfigurationPublish.md)
Provides a way to configure publishing to PowerShell Gallery, GitHub, or private galleries such as Azure Artifacts.

### [New-ConfigurationTest](New-ConfigurationTest.md)
Configures running Pester tests as part of the build.

### [New-ConfigurationValidation](New-ConfigurationValidation.md)
Creates configuration for module validation checks during build.

### [New-DotNetPublishConfig](New-DotNetPublishConfig.md)
Scaffolds a starter powerforge.dotnetpublish.json configuration file.

### [New-ModuleAboutTopic](New-ModuleAboutTopic.md)
Creates an about_*.help.txt template source file for module documentation.

### [New-PowerForgeReleaseConfig](New-PowerForgeReleaseConfig.md)
Scaffolds a starter unified release.json configuration file.

### [New-ProjectReleaseConfig](New-ProjectReleaseConfig.md)
Scaffolds a starter project release configuration file for PowerShell-authored project builds.

### [Publish-GitHubReleaseAsset](Publish-GitHubReleaseAsset.md)
Publishes a release asset to GitHub (creates a release and uploads a zip).

### [Publish-NugetPackage](Publish-NugetPackage.md)
Pushes NuGet packages to a feed using dotnet nuget push.

### [Register-Certificate](Register-Certificate.md)
Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).

### [Register-ModuleRepository](Register-ModuleRepository.md)
Registers an Azure Artifacts feed as a private PowerShell module repository for PowerShellGet and/or PSResourceGet.

### [Remove-Comments](Remove-Comments.md)
Removes PowerShell comments from a script file or provided content, with optional empty-line normalization.

### [Remove-ProjectFiles](Remove-ProjectFiles.md)
Removes specific files and folders from a project directory with safety features.

### [Send-GitHubRelease](Send-GitHubRelease.md)
Creates a new release for the given GitHub repository and optionally uploads assets.

### [Set-ProjectVersion](Set-ProjectVersion.md)
Updates version numbers across multiple project files.

### [Step-Version](Step-Version.md)
Steps a version based on an expected version pattern (supports the legacy X placeholder).

### [Update-ModuleRepository](Update-ModuleRepository.md)
Refreshes or repairs an Azure Artifacts private PowerShell module repository registration.

### [Update-PrivateModule](Update-PrivateModule.md)
Updates one or more modules from a private repository, optionally refreshing Azure Artifacts registration first.

## About Topics

### [about_ModuleDependencies](About/about_ModuleDependencies.md)

