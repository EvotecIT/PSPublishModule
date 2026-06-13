---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-ModuleDocumentation
## SYNOPSIS
Copies a module's bundled documentation (Internals, README/CHANGELOG/LICENSE) to a chosen location.

Resolves the module and copies its documentation payload into a destination folder arranged by DocumentationLayout. The payload is the module's delivery Internals folder (or the default Internals folder) plus selected root documentation files such as README, CHANGELOG and LICENSE. Repeat runs can merge, overwrite, skip or stop based on OnExistsOption. The default Merge mode adds missing files and keeps existing files unless -Force is used. When successful, returns the destination path.

## SYNTAX
### ByName (Default)
```powershell
Install-ModuleDocumentation [[-Name] <string>] -Path <string> [-RequiredVersion <version>] [-Layout <DocumentationLayout>] [-OnExists <OnExistsOption>] [-CreateVersionSubfolder] [-Force] [-ListOnly] [-Open] [-NoIntro] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByModule
```powershell
Install-ModuleDocumentation -Path <string> [-Module <psmoduleinfo>] [-RequiredVersion <version>] [-Layout <DocumentationLayout>] [-OnExists <OnExistsOption>] [-CreateVersionSubfolder] [-Force] [-ListOnly] [-Open] [-NoIntro] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Copies a module's bundled documentation (Internals, README/CHANGELOG/LICENSE) to a chosen location.

Resolves the module and copies its documentation payload into a destination folder arranged by DocumentationLayout. The payload is the module's delivery Internals folder (or the default Internals folder) plus selected root documentation files such as README, CHANGELOG and LICENSE. Repeat runs can merge, overwrite, skip or stop based on OnExistsOption. The default Merge mode adds missing files and keeps existing files unless -Force is used. When successful, returns the destination path.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout ModuleAndVersion
```

Copies Internals and selected root files into C:\\Docs\\EFAdminManager\\<Version>.

### EXAMPLE 2
```powershell
PS> Get-Module -ListAvailable EFAdminManager | Install-ModuleDocumentation -Path C:\\Docs -OnExists Merge -Open
```

Merges content into an existing destination, preserves existing files, and opens the README (if present) afterwards.

### EXAMPLE 3
```powershell
PS> Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout Module
```

Targets C:\\Docs\\EFAdminManager. Use -OnExists Merge to keep existing files.

### EXAMPLE 4
```powershell
PS> Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout Direct
```

Copies Internals content straight into C:\\Docs.

### EXAMPLE 5
```powershell
PS> Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -OnExists Overwrite
```

Removes the destination before copying. Alternatively, use -OnExists Merge -Force to overwrite individual files.

### EXAMPLE 6
```powershell
PS> Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout ModuleAndVersion -ListOnly
```

Shows the folder that would be used for the selected module/version without copying bundled documentation.

### EXAMPLE 7
```powershell
PS> New-ConfigurationInformation -IncludeAll 'Internals\\' ; New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -DocumentationOrder '01-Intro.md','02-HowTo.md'
```

Bundles Internals and controls the display order of Docs in viewers.

## PARAMETERS

### -CreateVersionSubfolder
Legacy toggle equivalent to selecting ModuleAndVersion when set; Direct when not set.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Allow replacement of existing files during merge, and clear read-only attributes when overwrite needs to delete an existing destination.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Layout
Output folder structure strategy. The default, ModuleAndVersion, keeps each module version in its own subfolder.

```yaml
Type: DocumentationLayout
Parameter Sets: ByName, ByModule
Aliases: None
Possible values: Direct, Module, ModuleAndVersion

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ListOnly
Plan only; output the resolved destination path without copying files or changing the destination.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Module
Module object to install documentation for. Alternative to -Name.

```yaml
Type: PSModuleInfo
Parameter Sets: ByModule
Aliases: InputObject, ModuleInfo
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -Name
Module name to install documentation for.

```yaml
Type: String
Parameter Sets: ByName
Aliases: ModuleName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -NoIntro
Suppress delivery IntroText or IntroFile output during installation.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnExists
Behavior when the destination folder already exists. The default, Merge, adds missing files and preserves existing files unless -Force is used.

```yaml
Type: OnExistsOption
Parameter Sets: ByName, ByModule
Aliases: None
Possible values: Merge, Overwrite, Skip, Stop

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Open
Open the resulting folder or README after installation.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Base destination folder where documentation will be written. The final folder also depends on Layout.

```yaml
Type: String
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Exact version to select when multiple module versions are installed.

```yaml
Type: Version
Parameter Sets: ByName, ByModule
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

- `System.String
System.Management.Automation.PSModuleInfo`

## OUTPUTS

- `System.String`

## RELATED LINKS

- None
