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
Invoke-PowerForgeRelease [-ConfigPath <string>] [-Plan] [-Validate] [-PackagesOnly] [-ModuleOnly] [-ToolsOnly] [-PublishNuget] [-PublishProjectGitHub] [-PublishToolGitHub] [-Configuration <string>] [-ModuleNoDotnetBuild] [-ModuleVersion <string>] [-ModulePreReleaseTag <string>] [-ModuleNoSign] [-ModuleSignModule] [-SkipWorkspaceValidation] [-WorkspaceConfigPath <string>] [-WorkspaceProfile <string>] [-WorkspaceEnableFeature <string[]>] [-WorkspaceDisableFeature <string[]>] [-SkipRestore] [-SkipBuild] [-Target <string[]>] [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-Flavors <string[]>] [-ToolOutput <string[]>] [-SkipToolOutput <string[]>] [-OutputRoot <string>] [-StageRoot <string>] [-ManifestJsonPath <string>] [-AllowOutputOutsideProjectRoot] [-AllowManifestOutsideProjectRoot] [-ChecksumsPath <string>] [-SkipReleaseChecksums] [-KeepSymbols] [-Sign] [-SignProfile <string>] [-SignToolPath <string>] [-SignThumbprint <string>] [-SignSubjectName <string>] [-SignOnMissingTool <DotNetPublishPolicyMode>] [-SignOnFailure <DotNetPublishPolicyMode>] [-SignTimestampUrl <string>] [-SignDescription <string>] [-SignUrl <string>] [-SignCsp <string>] [-SignKeyContainer <string>] [-PackageSignThumbprint <string>] [-PackageSignStore <string>] [-PackageSignTimestampUrl <string>] [-InstallerProperty <string[]>] [-ExitCode] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Project
```powershell
Invoke-PowerForgeRelease -Project <ConfigurationProject> [-Plan] [-Validate] [-PackagesOnly] [-ModuleOnly] [-ToolsOnly] [-PublishNuget] [-PublishProjectGitHub] [-PublishToolGitHub] [-Configuration <string>] [-ModuleNoDotnetBuild] [-ModuleVersion <string>] [-ModulePreReleaseTag <string>] [-ModuleNoSign] [-ModuleSignModule] [-SkipWorkspaceValidation] [-WorkspaceConfigPath <string>] [-WorkspaceProfile <string>] [-WorkspaceEnableFeature <string[]>] [-WorkspaceDisableFeature <string[]>] [-SkipRestore] [-SkipBuild] [-Target <string[]>] [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-Flavors <string[]>] [-ToolOutput <string[]>] [-SkipToolOutput <string[]>] [-OutputRoot <string>] [-StageRoot <string>] [-ManifestJsonPath <string>] [-AllowOutputOutsideProjectRoot] [-AllowManifestOutsideProjectRoot] [-ChecksumsPath <string>] [-SkipReleaseChecksums] [-KeepSymbols] [-Sign] [-SignProfile <string>] [-SignToolPath <string>] [-SignThumbprint <string>] [-SignSubjectName <string>] [-SignOnMissingTool <DotNetPublishPolicyMode>] [-SignOnFailure <DotNetPublishPolicyMode>] [-SignTimestampUrl <string>] [-SignDescription <string>] [-SignUrl <string>] [-SignCsp <string>] [-SignKeyContainer <string>] [-PackageSignThumbprint <string>] [-PackageSignStore <string>] [-PackageSignTimestampUrl <string>] [-InstallerProperty <string[]>] [-ExitCode] [-WhatIf] [-Confirm] [<CommonParameters>]
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
```

### -Configuration
Optional configuration override.

```yaml
Type: String
Parameter Sets: Config, Project
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
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
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.PowerForgeReleaseResult`

## RELATED LINKS

- None

