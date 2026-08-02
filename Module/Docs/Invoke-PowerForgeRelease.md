---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-PowerForgeRelease
## SYNOPSIS
Executes the unified repository release workflow from a JSON configuration.

## SYNTAX
### Config (Default)
```powershell
Invoke-PowerForgeRelease [-ConfigPath <string>] [-Plan] [-Validate] [-PackagesOnly] [-ModuleOnly] [-ToolsOnly] [-PublishNuget] [-PublishProjectGitHub] [-PublishToolGitHub] [-Configuration <string>] [-ModuleFramework <string>] [-ReleaseVersion <string>] [-ModuleRunMode <ConfigurationGateMode>] [-ModuleNoDotnetBuild] [-ModuleVersion <string>] [-ModulePreReleaseTag <string>] [-ModuleNoSign] [-ModuleSkipInstall] [-ModuleSignModule] [-ModuleTimeoutSeconds <int>] [-ModuleCertificateThumbprint <string>] [-ModuleSignIncludeBinaries <bool>] [-ModuleSignIncludeInternals <bool>] [-ModuleSignIncludeExe <bool>] [-ModuleDiagnosticsBaselinePath <string>] [-ModuleGenerateDiagnosticsBaseline <bool>] [-ModuleUpdateDiagnosticsBaseline <bool>] [-ModuleFailOnNewDiagnostics <bool>] [-ModuleFailOnDiagnosticsSeverity <string>] [-SkipWorkspaceValidation] [-WorkspaceConfigPath <string>] [-WorkspaceProfile <string>] [-WorkspaceEnableFeature <string[]>] [-WorkspaceDisableFeature <string[]>] [-SkipRestore] [-SkipBuild] [-Target <string[]>] [-AppleAction <PowerForgeAppleReleaseAction>] [-ConfirmAppleAction] [-AppleResume] [-NoAppleResume] [-AppleWaitForProcessing] [-NoAppleWaitForProcessing] [-AppleProcessingTimeoutSeconds <int>] [-ApplePollIntervalSeconds <int>] [-AppleSummary] [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-Flavors <string[]>] [-ToolOutput <string[]>] [-SkipToolOutput <string[]>] [-OutputRoot <string>] [-StageRoot <string>] [-ManifestJsonPath <string>] [-AllowOutputOutsideProjectRoot] [-AllowManifestOutsideProjectRoot] [-ChecksumsPath <string>] [-SkipReleaseChecksums] [-KeepSymbols] [-Sign] [-SignProfile <string>] [-SignToolPath <string>] [-SignThumbprint <string>] [-SignSubjectName <string>] [-SignOnMissingTool <DotNetPublishPolicyMode>] [-SignOnFailure <DotNetPublishPolicyMode>] [-SignTimestampUrl <string>] [-SignDescription <string>] [-SignUrl <string>] [-SignCsp <string>] [-SignKeyContainer <string>] [-PackageSignThumbprint <string>] [-PackageSignStore <string>] [-PackageSignTimestampUrl <string>] [-InstallerProperty <string[]>] [-ExitCode] [-NoInteractive] [-SubmitWinget] [-SkipWingetSubmit] [-WingetSubmitMode <string>] [-WingetToolPath <string>] [-WingetTokenEnvName <string>] [-WingetTokenFilePath <string>] [-WingetPullRequestTitle <string>] [-WingetOpenBrowser] [-WingetReplace] [-WingetReplaceVersion <string>] [-WingetAllowInteractiveAuthentication] [-WingetTimeoutSeconds <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Project
```powershell
Invoke-PowerForgeRelease -Project <ConfigurationProject> [-Plan] [-Validate] [-PackagesOnly] [-ModuleOnly] [-ToolsOnly] [-PublishNuget] [-PublishProjectGitHub] [-PublishToolGitHub] [-Configuration <string>] [-ModuleFramework <string>] [-ReleaseVersion <string>] [-ModuleRunMode <ConfigurationGateMode>] [-ModuleNoDotnetBuild] [-ModuleVersion <string>] [-ModulePreReleaseTag <string>] [-ModuleNoSign] [-ModuleSkipInstall] [-ModuleSignModule] [-ModuleTimeoutSeconds <int>] [-ModuleCertificateThumbprint <string>] [-ModuleSignIncludeBinaries <bool>] [-ModuleSignIncludeInternals <bool>] [-ModuleSignIncludeExe <bool>] [-ModuleDiagnosticsBaselinePath <string>] [-ModuleGenerateDiagnosticsBaseline <bool>] [-ModuleUpdateDiagnosticsBaseline <bool>] [-ModuleFailOnNewDiagnostics <bool>] [-ModuleFailOnDiagnosticsSeverity <string>] [-SkipWorkspaceValidation] [-WorkspaceConfigPath <string>] [-WorkspaceProfile <string>] [-WorkspaceEnableFeature <string[]>] [-WorkspaceDisableFeature <string[]>] [-SkipRestore] [-SkipBuild] [-Target <string[]>] [-AppleAction <PowerForgeAppleReleaseAction>] [-ConfirmAppleAction] [-AppleResume] [-NoAppleResume] [-AppleWaitForProcessing] [-NoAppleWaitForProcessing] [-AppleProcessingTimeoutSeconds <int>] [-ApplePollIntervalSeconds <int>] [-AppleSummary] [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-Flavors <string[]>] [-ToolOutput <string[]>] [-SkipToolOutput <string[]>] [-OutputRoot <string>] [-StageRoot <string>] [-ManifestJsonPath <string>] [-AllowOutputOutsideProjectRoot] [-AllowManifestOutsideProjectRoot] [-ChecksumsPath <string>] [-SkipReleaseChecksums] [-KeepSymbols] [-Sign] [-SignProfile <string>] [-SignToolPath <string>] [-SignThumbprint <string>] [-SignSubjectName <string>] [-SignOnMissingTool <DotNetPublishPolicyMode>] [-SignOnFailure <DotNetPublishPolicyMode>] [-SignTimestampUrl <string>] [-SignDescription <string>] [-SignUrl <string>] [-SignCsp <string>] [-SignKeyContainer <string>] [-PackageSignThumbprint <string>] [-PackageSignStore <string>] [-PackageSignTimestampUrl <string>] [-InstallerProperty <string[]>] [-ExitCode] [-NoInteractive] [-SubmitWinget] [-SkipWingetSubmit] [-WingetSubmitMode <string>] [-WingetToolPath <string>] [-WingetTokenEnvName <string>] [-WingetTokenFilePath <string>] [-WingetPullRequestTitle <string>] [-WingetOpenBrowser] [-WingetReplace] [-WingetReplaceVersion <string>] [-WingetAllowInteractiveAuthentication] [-WingetTimeoutSeconds <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the PowerShell entry point for the same unified release engine used by
powerforge release. It can coordinate repository package publishing and downloadable
tool/app artefacts from one configuration file.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-PowerForgeRelease -ConfigPath '.\Build\release.json' -Plan
```


### EXAMPLE 2
```powershell
Invoke-PowerForgeRelease -ConfigPath '.\Build\release.json' -ToolsOnly -PublishToolGitHub -ExitCode
```


## PARAMETERS

### -AllowManifestOutsideProjectRoot
Allows DotNetPublish-backed manifest/report paths to resolve outside the configured project root.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AllowOutputOutsideProjectRoot
Allows DotNetPublish-backed outputs to resolve outside the configured project root.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AppleAction
Selects one explicit Apple release operation. Configured preserves legacy JSON action flags.

```yaml
Type: PowerForgeAppleReleaseAction
Parameter Sets: Config, Project
Aliases: None
Possible values: Configured, Status, Doctor, Version, Archive, Upload, UploadExisting, Prepare, Screenshots, TestFlight, Advance, SubmitTestFlightReview, SubmitAppReview, Release, Cleanup

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApplePollIntervalSeconds
App Store Connect processing poll interval in seconds.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AppleProcessingTimeoutSeconds
Maximum App Store Connect processing wait in seconds.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AppleResume
Forces exact remote-build reuse on this run.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AppleSummary
Requests compact Apple receipt-oriented output from compatible hosts.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AppleWaitForProcessing
Waits for App Store Connect build processing on this run.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ChecksumsPath
Optional release checksums output path override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ConfigPath
Path to the unified release configuration file. When omitted, the cmdlet searches current
and parent directories for standard release config file names.

```yaml
Type: String
Parameter Sets: Config
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Configuration
Optional configuration override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ConfirmAppleAction
Explicitly confirms a risky Apple screenshot replacement, review submission, or public release action.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExitCode
Sets host exit code: 0 on success, 1 on failure.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Flavors
Optional legacy tool flavor filter.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: Flavor
Possible values: SingleContained, SingleFx, Portable, Fx

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Frameworks
Optional framework filter.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: Framework
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallerProperty
Optional installer MSBuild property overrides in Name=Value form.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -KeepSymbols
Keeps symbol files for tool/app artefacts.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ManifestJsonPath
Optional release manifest output path override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleCertificateThumbprint
Optional signing certificate thumbprint for the native module-release lane.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleDiagnosticsBaselinePath
Optional diagnostics baseline path for the native module-release lane.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleFailOnDiagnosticsSeverity
Optional diagnostics severity threshold for the native module-release lane.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values: Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleFailOnNewDiagnostics
Controls failure on new diagnostics in the native module-release lane.

```yaml
Type: Boolean
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleFramework
Target framework used by the native module-release lane.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values: auto, net10.0, net8.0

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleGenerateDiagnosticsBaseline
Controls diagnostics baseline generation in the native module-release lane.

```yaml
Type: Boolean
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleNoDotnetBuild
Skips the dotnet build step inside the native module-release lane.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleNoSign
Disables signing for the native module-release lane.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleOnly
Executes only the module portion of the release.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModulePreReleaseTag
Optional prerelease tag override for the native module-release lane.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleRunMode
Module pipeline gate used by the native module-release lane.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values: Manifest, Documentation, Build, Publish

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleSignIncludeBinaries
Controls whether the native module-release lane signs binaries.

```yaml
Type: Boolean
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleSignIncludeExe
Controls whether the native module-release lane signs executables.

```yaml
Type: Boolean
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleSignIncludeInternals
Controls whether the native module-release lane signs internal files.

```yaml
Type: Boolean
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleSignModule
Enables signing for the native module-release lane.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleSkipInstall
Skips installation after the native module-release lane completes.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleTimeoutSeconds
Maximum runtime in seconds for the native module-release lane.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleUpdateDiagnosticsBaseline
Controls diagnostics baseline updates in the native module-release lane.

```yaml
Type: Boolean
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleVersion
Optional module version override for the native module-release lane.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NoAppleResume
Disables exact remote-build reuse on this run.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NoAppleWaitForProcessing
Returns after upload instead of waiting for build processing.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NoInteractive
Disables the interactive Spectre renderer while preserving structured pipeline output.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputRoot
Optional output root override for tool/app assets.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PackageSignStore
Optional package-signing certificate store override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values: CurrentUser, LocalMachine

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PackageSignThumbprint
Optional package-signing thumbprint override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PackageSignTimestampUrl
Optional package-signing timestamp URL override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PackagesOnly
Executes only the package portion of the release.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Plan
Builds the release plan without executing steps.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Project
PowerShell-authored project/release object that is translated into the unified release engine.

```yaml
Type: ConfigurationProject
Parameter Sets: Project
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PublishNuget
Enables NuGet publishing for this run.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PublishProjectGitHub
Enables project/package GitHub release publishing for this run.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PublishToolGitHub
Enables tool/app GitHub release publishing for this run.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReleaseVersion
Exact x.y.z version override for an explicitly tool-only release.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Runtimes
Optional runtime filter.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: Runtime, Rid
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Sign
Enables signing for tool/app outputs when supported by the release config.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignCsp
Optional signing CSP override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignDescription
Optional signing description override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignKeyContainer
Optional signing key container override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignOnFailure
Optional policy when signing fails.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignOnMissingTool
Optional policy when the configured signing tool is missing.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignProfile
Optional signing profile override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignSubjectName
Optional signing certificate subject name override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignThumbprint
Optional signing thumbprint override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignTimestampUrl
Optional signing timestamp URL override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignToolPath
Optional signing tool path override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignUrl
Optional signing URL override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipBuild
Disables build operations for the tool/app publish flow.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipReleaseChecksums
Skips top-level release checksums generation.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipRestore
Disables restore operations for the tool/app publish flow.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipToolOutput
Optional tool/app output exclusion for DotNetPublish-backed release flows.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: None
Possible values: Tool, Portable, Installer, Store

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipWingetSubmit
Disables Winget submission even when enabled by release configuration.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipWorkspaceValidation
Skips workspace validation defined by the release config.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -StageRoot
Optional staged release root override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Styles
Optional publish style filter.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: Config, Project
Aliases: Style
Possible values: Portable, PortableCompat, PortableSize, FrameworkDependent, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SubmitWinget
Submits generated Winget manifests with wingetcreate after release assets are available.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Target
Optional target-name filter.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: Targets
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ToolOutput
Optional tool/app output selection for DotNetPublish-backed release flows.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: None
Possible values: Tool, Portable, Installer, Store

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ToolsOnly
Executes only the tool/app portion of the release.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Validate
Validates configuration through plan-only execution.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetAllowInteractiveAuthentication
Allows wingetcreate to prompt for GitHub authentication when no token is resolved.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetOpenBrowser
Allows wingetcreate to open the submitted pull request in a browser.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetPullRequestTitle
Pull request title template passed to wingetcreate.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetReplace
Enables wingetcreate replacement mode.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetReplaceVersion
Optional version passed with wingetcreate replacement mode.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetSubmitMode
Winget submission mode used by wingetcreate.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values: Manifest, Update

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetTimeoutSeconds
Timeout in seconds for each wingetcreate invocation.

```yaml
Type: Nullable`1
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetTokenEnvName
Environment variable containing the GitHub token for wingetcreate.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetTokenFilePath
File containing the GitHub token for wingetcreate.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WingetToolPath
Optional wingetcreate executable path.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WorkspaceConfigPath
Optional workspace validation config override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WorkspaceDisableFeature
Optional workspace feature disable list override.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WorkspaceEnableFeature
Optional workspace feature enable list override.

```yaml
Type: String[]
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WorkspaceProfile
Optional workspace validation profile override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.PowerForgeReleaseResult`
- `PowerForge.PowerForgeAppleReleaseReceipt`

## RELATED LINKS

- None
