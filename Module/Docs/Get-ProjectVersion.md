---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-ProjectVersion

## SYNOPSIS
Retrieves project version information from various project files.

## SYNTAX

```
Get-ProjectVersion [[-ModuleName] <String>] [[-Path] <String>] [[-ExcludeFolders] <String[]>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Scans the specified path for C# projects (.csproj), PowerShell modules (.psd1),
and PowerShell build scripts that contain 'Invoke-ModuleBuild' to extract version information.

## EXAMPLES

### EXAMPLE 1
```
Get-ProjectVersion
Gets version information from all project files in the current directory.
```

### EXAMPLE 2
```
Get-ProjectVersion -ModuleName "MyModule" -Path "C:\Projects"
Gets version information for the specific module from the specified path.
```

## PARAMETERS

### -ModuleName
Optional module name to filter results to specific projects/modules.

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

### -Path
The root path to search for project files.
Defaults to current location.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
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
Position: 3
Default value: @()
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
### Returns objects with Version, Source, and Type properties for each found project file.
## NOTES

## RELATED LINKS
