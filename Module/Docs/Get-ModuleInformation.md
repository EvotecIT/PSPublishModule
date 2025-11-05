---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-ModuleInformation

## SYNOPSIS
Gets module manifest information from a project directory

## SYNTAX

```
Get-ModuleInformation [-Path] <String> [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Retrieves module manifest (.psd1) file information from the specified path.
Validates that exactly one manifest file exists and returns the parsed information.

## EXAMPLES

### EXAMPLE 1
```
Get-ModuleInformation -Path "C:\MyModule"
```

### EXAMPLE 2
```
$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
Write-Output "Module: $($moduleInfo.ModuleName) Version: $($moduleInfo.ModuleVersion)"
```

## PARAMETERS

### -Path
The path to the directory containing the module manifest file

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
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

## NOTES

## RELATED LINKS
