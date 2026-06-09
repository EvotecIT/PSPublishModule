---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-ModuleDocumentation
## SYNOPSIS
Copies a module's bundled documentation (Internals, README/CHANGELOG/LICENSE) to a chosen location.

Resolves the module and copies its Internals folder and selected root files into a destination folder arranged by DocumentationLayout. Repeat runs can merge, overwrite, skip or stop based on OnExistsOption. When successful, returns the destination path.

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

Resolves the module and copies its Internals folder and selected root files into a destination folder arranged by DocumentationLayout. Repeat runs can merge, overwrite, skip or stop based on OnExistsOption. When successful, returns the destination path.

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

Merges content into an existing destination and opens the README (if present) afterwards.

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
Overwrite files during merge or overwrite operations.

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
Output folder structure strategy. Default is ModuleAndVersion.

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
Plan only; output the resolved destination without copying files.

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
Suppress IntroText display during installation.

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
Behavior when the destination folder already exists. Default is Merge.

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
Destination folder where documentation will be written.

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
