---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-ProjectVersion
## SYNOPSIS
Updates version numbers across multiple project files.

## SYNTAX
### __AllParameterSets
```powershell
Set-ProjectVersion [-VersionType <ProjectVersionIncrementKind>] [-NewVersion <string>] [-ModuleName <string>] [-Path <string>] [-ExcludeFolders <string[]>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Updates version numbers in:
C# projects (*.csproj)PowerShell module manifests (*.psd1)PowerShell build scripts that reference Invoke-ModuleBuild

Use -VersionType to increment one component, or -NewVersion to set an explicit version.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Set-ProjectVersion -VersionType Minor -WhatIf
```

Previews the version update for all discovered project files.

### EXAMPLE 2
```powershell
PS>Set-ProjectVersion -NewVersion '2.1.0' -ModuleName 'MyModule' -Path 'C:\Projects'
```

Updates only files related to the selected module name.

## PARAMETERS

### -ExcludeFolders
Path fragments (or folder names) to exclude from the search (in addition to default 'obj' and 'bin'). This matches against the full path, case-insensitively.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleName
Optional module name to filter updates to specific projects/modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NewVersion
Specific version number to set (format: x.x.x or x.x.x.x).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Returns per-file update results when specified.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
The root path to search for project files. Defaults to current directory.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionType
The type of version increment: Major, Minor, Build, or Revision.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
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

- `PSPublishModule.ProjectVersionUpdateResult`

## RELATED LINKS

- None

