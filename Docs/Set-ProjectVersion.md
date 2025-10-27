---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Set-ProjectVersion

## SYNOPSIS
Updates version numbers across multiple project files.

## SYNTAX

```
Set-ProjectVersion [[-VersionType] <String>] [[-NewVersion] <String>] [[-ModuleName] <String>]
 [[-Path] <String>] [[-ExcludeFolders] <String[]>] [-PassThru] [-ProgressAction <ActionPreference>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Updates version numbers in C# projects (.csproj), PowerShell modules (.psd1),
and PowerShell build scripts that contain 'Invoke-ModuleBuild'.
For .csproj files, it updates VersionPrefix, AssemblyVersion, and FileVersion when present.
Can increment
version components or set a specific version.

## EXAMPLES

### EXAMPLE 1
```
Set-ProjectVersion -VersionType Minor
Increments the minor version in all project files.
```

### EXAMPLE 2
```
Set-ProjectVersion -NewVersion "2.1.0" -ModuleName "MyModule"
Sets the version to 2.1.0 for the specific module.
```

## PARAMETERS

### -VersionType
The type of version increment: Major, Minor, Build, or Revision.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NewVersion
Specific version number to set (format: x.x.x or x.x.x.x).

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleName
Optional module name to filter updates to specific projects/modules.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
The root path to search for project files.
Defaults to current location.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: (Get-Location).Path
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeFolders
Array of folder names to exclude from the search (in addition to default 'obj' and 'bin').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: @()
Accept pipeline input: False
Accept wildcard characters: False
```

### -PassThru
Returns the update results when specified.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### PSCustomObject[]
### When PassThru is specified, returns update results for each modified file.
## NOTES

## RELATED LINKS
