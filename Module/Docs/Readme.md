---
Module Name: PSPublishModule
Module Guid: eb76426a-1992-40a5-82cd-6480f883ef4d
Download Help Link: https://github.com/EvotecIT/PSPublishModule
Help Version: 3.0.0
Locale: en-US
---
# PSPublishModule Module
## Description
Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery

## PSPublishModule Cmdlets
### [Convert-ProjectConsistency](Convert-ProjectConsistency.md)
Converts a project to a consistent encoding/line ending policy and reports the results.

### [Export-CertificateForNuGet](Export-CertificateForNuGet.md)
Exports a code-signing certificate to DER format for NuGet.org registration.

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

### [Invoke-DotNetReleaseBuild](Invoke-DotNetReleaseBuild.md)
Builds a .NET project in Release configuration and prepares release artefacts.

### [Invoke-DotNetRepositoryRelease](Invoke-DotNetRepositoryRelease.md)
Repository-wide .NET package release workflow (discover, version, pack, publish).

### [Invoke-ModuleBuild](Invoke-ModuleBuild.md)
Creates/updates a module structure and triggers the build pipeline (legacy DSL compatible).

### [Invoke-ModuleTestSuite](Invoke-ModuleTestSuite.md)
Complete module testing suite that handles dependencies, imports, and test execution.

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

### [New-ConfigurationPublish](New-ConfigurationPublish.md)
Provides a way to configure publishing to PowerShell Gallery or GitHub.

### [New-ConfigurationTest](New-ConfigurationTest.md)
Configures running Pester tests as part of the build.

### [New-ConfigurationValidation](New-ConfigurationValidation.md)
Creates configuration for module validation checks during build.

### [Publish-GitHubReleaseAsset](Publish-GitHubReleaseAsset.md)
Publishes a release asset to GitHub (creates a release and uploads a zip).

### [Publish-NugetPackage](Publish-NugetPackage.md)
Pushes NuGet packages to a feed using dotnet nuget push.

### [Register-Certificate](Register-Certificate.md)
Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).

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
