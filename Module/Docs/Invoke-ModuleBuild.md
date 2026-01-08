---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ModuleBuild
## SYNOPSIS
Creates/updates a module structure and triggers the build pipeline (legacy DSL compatible).

## SYNTAX
### Modern (Default)
```powershell
Invoke-ModuleBuild [[-Settings] <scriptblock>] -ModuleName <string> [-Path <string>] [-FunctionsToExportFolder <string>] [-AliasesToExportFolder <string>] [-ExcludeFromPackage <string[]>] [-ExcludeDirectories <string[]>] [-ExcludeFiles <string[]>] [-IncludeRoot <string[]>] [-IncludePS1 <string[]>] [-IncludeAll <string[]>] [-IncludeCustomCode <scriptblock>] [-IncludeToArray <IDictionary>] [-LibrariesCore <string>] [-LibrariesDefault <string>] [-LibrariesStandard <string>] [-Legacy] [-NoInteractive] [-StagingPath <string>] [-CsprojPath <string>] [-DotNetConfiguration <string>] [-DotNetFramework <string[]>] [-SkipInstall] [-InstallStrategy <InstallationStrategy>] [-KeepVersions <int>] [-InstallRoots <string[]>] [-KeepStaging] [-JsonOnly] [-JsonPath <string>] [-ExitCode] [<CommonParameters>]
```

### Configuration
```powershell
Invoke-ModuleBuild -Configuration <IDictionary> [-ExcludeDirectories <string[]>] [-ExcludeFiles <string[]>] [-Legacy] [-NoInteractive] [-JsonOnly] [-JsonPath <string>] [-ExitCode] [<CommonParameters>]
```

## DESCRIPTION
This is the primary entry point for building a PowerShell module using PSPublishModule.
Configuration is provided via a DSL using New-Configuration* cmdlets (typically inside the -Settings
scriptblock) and then executed by the PowerForge pipeline runner.

To generate a reusable powerforge.json configuration file (for the PowerForge CLI) without running any build
steps, use -JsonOnly with -JsonPath.

When running in an interactive terminal, pipeline execution uses a Spectre.Console progress UI.
Redirect output or use -Verbose to force plain, line-by-line output (useful for CI logs).

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -Settings {
New-ConfigurationDocumentation -Enable -UpdateWhenNew -StartClean -Path 'Docs' -PathReadme 'Docs\Readme.md'
}
```

### EXAMPLE 2
```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -JsonOnly -JsonPath 'C:\Git\MyModule\powerforge.json'
```

### EXAMPLE 3
```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -ExitCode -Settings {
New-ConfigurationFileConsistency -Enable -FailOnInconsistency -AutoFix -CreateBackups -ExportReport
New-ConfigurationCompatibility -Enable -RequireCrossCompatibility -FailOnIncompatibility -ExportReport
}
```

### EXAMPLE 4
```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' `
-CsprojPath 'C:\Git\MyModule\src\MyModule\MyModule.csproj' -DotNetFramework net8.0 -DotNetConfiguration Release `
-Settings { New-ConfigurationBuild -Enable -MergeModuleOnBuild }
```

## PARAMETERS

### -AliasesToExportFolder
Folder name containing aliases to export. Default: Public.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Configuration
Legacy configuration dictionary for backwards compatibility.

```yaml
Type: IDictionary
Parameter Sets: Configuration
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CsprojPath
Optional path to a .NET project (.csproj) to publish into the module.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotNetConfiguration
Build configuration for publishing the .NET project (Release or Debug).

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotNetFramework
Target frameworks to publish (e.g., net472, net8.0).

```yaml
Type: String[]
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeDirectories
Directory names excluded from staging copy (matched by directory name, not by path).

```yaml
Type: String[]
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeFiles
File names excluded from staging copy (matched by file name, not by path).

```yaml
Type: String[]
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeFromPackage
Exclude patterns for artefact packaging.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExitCode
When specified, requests the host to exit with code 0 on success and 1 on failure.

```yaml
Type: SwitchParameter
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FunctionsToExportFolder
Folder name containing functions to export. Default: Public.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeAll
Folders from which to include all files in artefacts.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeCustomCode
Optional script block executed during staging that can add custom files/folders to the build.

```yaml
Type: ScriptBlock
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludePS1
Folders from which to include .ps1 files in artefacts.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeRoot
Include patterns for root files in artefacts.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeToArray
Advanced hashtable form for includes (maps IncludeRoot/IncludePS1/IncludeAll etc).

```yaml
Type: IDictionary
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallRoots
Destination module roots for install. When omitted, defaults are used.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallStrategy
Installation strategy used when installing the module.

```yaml
Type: InstallationStrategy
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JsonOnly
Generates a PowerForge pipeline JSON file and exits without running the build pipeline.
Intended for migrating legacy DSL scripts to powerforge CLI configuration.

```yaml
Type: SwitchParameter
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JsonPath
Output path for the generated pipeline JSON file (used with P:PSPublishModule.InvokeModuleBuildCommand.JsonOnly).
Defaults to powerforge.json in the project root.

```yaml
Type: String
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KeepStaging
Keep staging directory after build/install.

```yaml
Type: SwitchParameter
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KeepVersions
Number of versions to keep per module root when installing.

```yaml
Type: Int32
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Legacy
Compatibility switch. Historically forced the PowerShell-script build pipeline; the build now always runs through the C# PowerForge pipeline.

```yaml
Type: SwitchParameter
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LibrariesCore
Alternate relative path for .NET Core-targeted libraries folder. Default: Lib/Core.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LibrariesDefault
Alternate relative path for .NET Framework-targeted libraries folder. Default: Lib/Default.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LibrariesStandard
Alternate relative path for .NET Standard-targeted libraries folder. Default: Lib/Standard.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleName
Name of the module being built.

```yaml
Type: String
Parameter Sets: Modern
Aliases: ProjectName

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NoInteractive
Disables the interactive progress UI and emits plain log output.

```yaml
Type: SwitchParameter
Parameter Sets: Modern, Configuration
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path to the folder where the project exists or should be created. When omitted, uses the parent of the calling script directory.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Settings
Provides settings for the module in the form of a script block (DSL).

```yaml
Type: ScriptBlock
Parameter Sets: Modern
Aliases: None

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipInstall
Skips installing the module after build.

```yaml
Type: SwitchParameter
Parameter Sets: Modern
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -StagingPath
Staging directory for the PowerForge pipeline. When omitted, a temporary folder is generated.

```yaml
Type: String
Parameter Sets: Modern
Aliases: None

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

- `System.Object`

## RELATED LINKS

- None

