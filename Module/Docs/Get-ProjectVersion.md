---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ProjectVersion
## SYNOPSIS
Retrieves project version information from .csproj, .psd1, and build scripts.

## SYNTAX
### __AllParameterSets
```powershell
Get-ProjectVersion [-ModuleName <string>] [-Path <string>] [-ExcludeFolders <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Scans the specified path for:
*.csproj files*.psd1 filesPowerShell build scripts (*.ps1) that contain Invoke-ModuleBuild
and returns one record per discovered version entry.

This is useful for multi-project repositories where you want to quickly verify version alignment across projects/modules.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-ProjectVersion
```

Returns entries for discovered .csproj/.psd1/build scripts under the current folder.

### EXAMPLE 2
```powershell
PS>Get-ProjectVersion -ModuleName 'MyModule' -Path 'C:\Projects'
```

Useful when a repository contains multiple modules/projects but you need only one.

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
Optional module name to filter .csproj and .psd1 results.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PSPublishModule.ProjectVersionInfo`

## RELATED LINKS

- None

