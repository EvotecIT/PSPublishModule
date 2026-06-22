---
Module Name: PSPublishModule
Module Guid: eb76426a-1992-40a5-82cd-6480f883ef4d
Download Help Link: https://github.com/EvotecIT/PSPublishModule
Help Version: 3.0.33
Locale: en-US
---
# PSPublishModule Module
## Description
Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery

## PSPublishModule Cmdlets
### [Add-AppStoreConnectBetaTesterToGroup](Add-AppStoreConnectBetaTesterToGroup.md)
Adds App Store Connect beta testers to a TestFlight beta group.

### [Add-AppStoreConnectBuildToBetaGroup](Add-AppStoreConnectBuildToBetaGroup.md)
Adds App Store Connect builds to a TestFlight beta group.

### [Connect-ModuleRepository](Connect-ModuleRepository.md)
Registers an Azure Artifacts repository if needed and validates authenticated access for the selected bootstrap mode.

### [Convert-ProjectConsistency](Convert-ProjectConsistency.md)
Converts a project to a consistent encoding/line ending policy and reports the results.

### [Export-CertificateForNuGet](Export-CertificateForNuGet.md)
Exports a code-signing certificate to DER format for NuGet.org registration.

### [Export-ConfigurationProject](Export-ConfigurationProject.md)
Exports a PowerShell-authored project release object to JSON.

### [Export-ModuleRepositoryProfile](Export-ModuleRepositoryProfile.md)
Exports saved private module repository profiles to a non-secret JSON file.

### [Get-AppleDevice](Get-AppleDevice.md)
Lists Apple devices available through xcrun devicectl.

### [Get-AppStoreConnectApp](Get-AppStoreConnectApp.md)
Reads app information from App Store Connect.

### [Get-AppStoreConnectBetaGroup](Get-AppStoreConnectBetaGroup.md)
Reads App Store Connect TestFlight beta groups for an app.

### [Get-AppStoreConnectBetaTester](Get-AppStoreConnectBetaTester.md)
Reads App Store Connect TestFlight beta testers.

### [Get-AppStoreConnectBuild](Get-AppStoreConnectBuild.md)
Reads build information from App Store Connect.

### [Get-AppStoreConnectScreenshot](Get-AppStoreConnectScreenshot.md)
Reads screenshots in an App Store Connect screenshot set.

### [Get-AppStoreConnectScreenshotSet](Get-AppStoreConnectScreenshotSet.md)
Reads App Store Connect screenshot sets for an App Store version localization.

### [Get-AppStoreConnectSubscription](Get-AppStoreConnectSubscription.md)
Reads App Store Connect auto-renewable subscription products for an app.

### [Get-AppStoreConnectVersion](Get-AppStoreConnectVersion.md)
Reads App Store version information from App Store Connect.

### [Get-AppStoreConnectVersionLocalization](Get-AppStoreConnectVersionLocalization.md)
Reads App Store version localizations from App Store Connect.

### [Get-ConfigurationBoolean](Get-ConfigurationBoolean.md)
Resolves a boolean configuration value from an environment variable with a script-defined default.

### [Get-MissingFunctions](Get-MissingFunctions.md)
Analyzes a script or scriptblock and reports functions/commands it calls that are not present.

### [Get-ModuleDocumentation](Get-ModuleDocumentation.md)
Gets module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) and renders it in the console.

Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent, it will backfill from the module's repository specified by PrivateData.PSData.ProjectUri (GitHub or Azure DevOps), using a token when necessary.

### [Get-ModuleInformation](Get-ModuleInformation.md)
Gets module manifest information from a project directory.

### [Get-ModuleRepositoryProfile](Get-ModuleRepositoryProfile.md)
Gets saved private module repository profiles.

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

### [Get-XcodeProjectVersion](Get-XcodeProjectVersion.md)
Reads version information from an Xcode project.

### [Import-ConfigurationProject](Import-ConfigurationProject.md)
Imports a PowerShell-authored project release object from JSON.

### [Import-IsolatedModule](Import-IsolatedModule.md)
Imports a known PowerShell module through a curated AssemblyLoadContext isolation profile.

### [Import-ModuleDependency](Import-ModuleDependency.md)
Imports a module runtime by exact paths, with dependencies loaded before the root module.

### [Import-ModuleRepositoryProfile](Import-ModuleRepositoryProfile.md)
Imports private module repository profiles from a non-secret JSON file.

### [Initialize-ModuleRepository](Initialize-ModuleRepository.md)
Performs one-command enterprise onboarding for a private module repository profile.

### [Install-AppleApp](Install-AppleApp.md)
Installs a built Apple .app bundle on a physical device.

### [Install-ModuleDependency](Install-ModuleDependency.md)
Installs a module and its embedded dependencies to an explicit private runtime folder.

### [Install-ModuleDocumentation](Install-ModuleDocumentation.md)
Copies a module's bundled documentation (Internals, README/CHANGELOG/LICENSE) to a chosen location.

Resolves the module and copies its documentation payload into a destination folder arranged by DocumentationLayout. The payload is the module's delivery Internals folder (or the default Internals folder) plus selected root documentation files such as README, CHANGELOG and LICENSE. Repeat runs can merge, refresh, overwrite, skip or stop based on OnExistsOption. The default Merge mode adds missing files and keeps existing files unless -Force is used. The Refresh mode overwrites package files while keeping local files that are not part of the package. When successful, returns the destination path.

### [Install-ModuleScript](Install-ModuleScript.md)
Copies only PowerShell scripts from a module's Internals\Scripts folder to a destination path.
The destination is flattened (no Module/Version subfolders).

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

### [New-AppleAppArchive](New-AppleAppArchive.md)
Creates an Apple app .xcarchive using xcodebuild.

### [New-AppleAppBuild](New-AppleAppBuild.md)
Builds an Apple app for local installation using xcodebuild.

### [New-AppStoreConnectBetaTester](New-AppStoreConnectBetaTester.md)
Creates an App Store Connect TestFlight beta tester.

### [New-AppStoreConnectScreenshotSet](New-AppStoreConnectScreenshotSet.md)
Creates an App Store Connect screenshot set for an App Store version localization.

### [New-ConfigurationAppleApp](New-ConfigurationAppleApp.md)
Creates configuration for preparing an Apple app target in a release pipeline.

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

### [New-ConfigurationDotNetServiceHealthCheck](New-ConfigurationDotNetServiceHealthCheck.md)
Creates an HTTP readiness check for DotNet publish service lifecycle verification.

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
Creates a module pipeline lifecycle action.

### [New-ConfigurationFileConsistency](New-ConfigurationFileConsistency.md)
Creates configuration for file consistency checking (encoding and line endings) during module build.

### [New-ConfigurationFormat](New-ConfigurationFormat.md)
Builds formatting options for code and manifest generation during the build.

### [New-ConfigurationGate](New-ConfigurationGate.md)
Sets the high-level module pipeline mode for an F5-friendly build DSL.

### [New-ConfigurationImportModule](New-ConfigurationImportModule.md)
Creates a configuration for importing PowerShell modules.

### [New-ConfigurationInformation](New-ConfigurationInformation.md)
Describes what to include/exclude in the module build and how libraries are organized.

### [New-ConfigurationManifest](New-ConfigurationManifest.md)
Creates a configuration manifest for a PowerShell module.

### [New-ConfigurationModule](New-ConfigurationModule.md)
Provides a way to configure required, external, embedded, or approved modules used in the project.

### [New-ConfigurationModuleSkip](New-ConfigurationModuleSkip.md)
Provides a way to ignore certain commands or modules during build-time dependency validation.

### [New-ConfigurationPackageBuild](New-ConfigurationPackageBuild.md)
Creates inline .NET/NuGet package build configuration from the module-build DSL.

### [New-ConfigurationPlaceHolder](New-ConfigurationPlaceHolder.md)
Helps define custom placeholders replacing content within a script or module during the build process.

### [New-ConfigurationProject](New-ConfigurationProject.md)
Creates a PowerShell-first project/release object for the unified PowerForge release engine.

### [New-ConfigurationProjectBuild](New-ConfigurationProjectBuild.md)
References an existing project.build.json package build from the module-build DSL.

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
Provides a way to configure publishing to PowerShell Gallery, GitHub, JFrog Artifactory, or other private PowerShell module repositories.

### [New-ConfigurationRelease](New-ConfigurationRelease.md)
Creates repo-level release coordination settings for a module and package build.

### [New-ConfigurationTest](New-ConfigurationTest.md)
Configures running Pester tests as part of the build.

### [New-ConfigurationValidation](New-ConfigurationValidation.md)
Creates configuration for module validation checks during build.

### [New-ConfigurationXcodeProjectVersion](New-ConfigurationXcodeProjectVersion.md)
Creates configuration for updating Xcode project version values during a build pipeline.

### [New-DotNetPublishConfig](New-DotNetPublishConfig.md)
Scaffolds a starter powerforge.dotnetpublish.json configuration file.

### [New-ModuleAboutTopic](New-ModuleAboutTopic.md)
Creates an about_*.help.txt template source file for module documentation.

### [New-ModuleRepositoryBootstrap](New-ModuleRepositoryBootstrap.md)
Creates a managed workstation bootstrap package for private module repository onboarding.

### [New-PowerForgeReleaseConfig](New-PowerForgeReleaseConfig.md)
Scaffolds a starter unified release.json configuration file.

### [New-ProjectReleaseConfig](New-ProjectReleaseConfig.md)
Scaffolds a starter project release configuration file for PowerShell-authored project builds.

### [Publish-AppleAppArchive](Publish-AppleAppArchive.md)
Uploads an Apple app .xcarchive to App Store Connect using xcodebuild exportArchive.

### [Publish-AppleAppToDevice](Publish-AppleAppToDevice.md)
Builds, installs, and optionally launches an Apple app on a physical device.

### [Publish-AppStoreConnectApprovedVersion](Publish-AppStoreConnectApprovedVersion.md)
Requests release of an approved App Store Connect version in Pending Developer Release.

### [Publish-AppStoreConnectScreenshot](Publish-AppStoreConnectScreenshot.md)
Uploads and commits an App Store Connect screenshot file to an existing screenshot set.

### [Publish-AppStoreConnectTestFlightBuild](Publish-AppStoreConnectTestFlightBuild.md)
Distributes a processed App Store Connect build to TestFlight beta groups and optional testers.

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

### [Remove-ModuleRepositoryProfile](Remove-ModuleRepositoryProfile.md)
Removes a saved private module repository profile.

### [Remove-ProjectFiles](Remove-ProjectFiles.md)
Removes specific files and folders from a project directory with safety features.

### [Send-GitHubRelease](Send-GitHubRelease.md)
Creates a new release for the given GitHub repository and optionally uploads assets.

### [Set-AppStoreConnectVersionBuild](Set-AppStoreConnectVersionBuild.md)
Creates or finds an App Store version and selects a processed build for Distribution.

### [Set-AppStoreConnectVersionLocalization](Set-AppStoreConnectVersionLocalization.md)
Updates localized metadata fields on an App Store version localization.

### [Set-ModuleDocumentation](Set-ModuleDocumentation.md)
Configures repository access for documentation (stores/revokes tokens).

Stores Personal Access Tokens for GitHub and/or Azure DevOps under the current user profile so module documentation commands can access private repositories. On Windows, tokens are protected with DPAPI; on other platforms they are stored as Base64 (best effort).

### [Set-ModuleRepositoryProfile](Set-ModuleRepositoryProfile.md)
Creates or updates a saved private module repository profile.

### [Set-ProjectVersion](Set-ProjectVersion.md)
Updates version numbers across multiple project files.

### [Set-XcodeProjectVersion](Set-XcodeProjectVersion.md)
Updates version information in an Xcode project.

### [Show-ModuleDocumentation](Show-ModuleDocumentation.md)
Displays module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) in the console.

Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent or when requested, it can fetch files directly from the module's repository specified by PrivateData.PSData.ProjectUri (GitHub or Azure DevOps), optionally using a Personal Access Token.

### [Start-AppleApp](Start-AppleApp.md)
Launches an installed Apple app on a physical device.

### [Step-Version](Step-Version.md)
Steps a version based on an expected version pattern (supports the legacy X placeholder).

### [Submit-AppStoreConnectVersionForReview](Submit-AppStoreConnectVersionForReview.md)
Submits a prepared App Store Connect Distribution version to App Review.

### [Sync-AppStoreConnectScreenshots](Sync-AppStoreConnectScreenshots.md)
Syncs local screenshot folders to App Store Connect screenshot sets.

### [Sync-AppStoreConnectVersionMetadata](Sync-AppStoreConnectVersionMetadata.md)
Syncs localized App Store version metadata from a JSON configuration file.

### [Test-AppleAppReleaseDrift](Test-AppleAppReleaseDrift.md)
Tests local Xcode project version values against App Store Connect.

### [Test-AppStoreConnectReleaseReadiness](Test-AppStoreConnectReleaseReadiness.md)
Checks whether an App Store Connect Distribution version is ready for submission.

### [Test-AppStoreConnectScreenshotSyncConfig](Test-AppStoreConnectScreenshotSyncConfig.md)
Validates an App Store Connect screenshot sync configuration against local files.

### [Test-IsolatedModuleProfile](Test-IsolatedModuleProfile.md)
Validates a curated isolated module profile without importing it.

### [Test-ModuleRepositoryProfile](Test-ModuleRepositoryProfile.md)
Tests saved private module repository profiles and local authentication prerequisites.

### [Update-ModuleRepository](Update-ModuleRepository.md)
Refreshes or repairs an Azure Artifacts private PowerShell module repository registration.

### [Update-PrivateModule](Update-PrivateModule.md)
Updates one or more modules from a private repository, optionally refreshing Azure Artifacts registration first.

## About Topics

### [about_ModuleDependencies](About/about_ModuleDependencies.md)

### [about_ModuleLifecycleActions](About/about_ModuleLifecycleActions.md)

### [about_PrivateGalleries](About/about_PrivateGalleries.md)
