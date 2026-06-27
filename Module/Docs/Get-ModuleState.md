---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleState
## SYNOPSIS
Gets module-state inventory from the local machine or an inventory artifact.

## SYNTAX
### Local (Default)
```powershell
Get-ModuleState [-ModulePath <string[]>] [-IncludeLoaded] [-OutputPath <string>] [-ShowSummary] [-AsJson] [<CommonParameters>]
```

### Path
```powershell
Get-ModuleState [-Path] <string> [-IncludeLoaded] [-OutputPath <string>] [-ShowSummary] [-AsJson] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the inventory entry point for ModuleState. It scans module roots
from -ModulePath or $env:PSModulePath, or reads an existing
inventory artifact supplied through -Path.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Get-ModuleState
```

Scans module roots from $env:PSModulePath and returns installed module entries.

### EXAMPLE 2
```powershell
PS> Get-ModuleState -Path .\inventory.json
```

Reads a previously captured module-state inventory artifact.

## PARAMETERS

### -AsJson
Gets or sets whether to return the inventory as JSON instead of a typed result object.

```yaml
Type: SwitchParameter
Parameter Sets: Local, Path
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeLoaded
Gets or sets whether modules loaded in the current runspace should be marked in the inventory.

```yaml
Type: SwitchParameter
Parameter Sets: Local, Path
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModulePath
Gets or sets explicit module roots to scan. When omitted, $env:PSModulePath is used.

```yaml
Type: String[]
Parameter Sets: Local
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Gets or sets the path where an inventory JSON artifact should be written.

```yaml
Type: String
Parameter Sets: Local, Path
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Gets or sets the path to an inventory JSON file.

```yaml
Type: String
Parameter Sets: Path
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSummary
Gets or sets whether to render a Spectre.Console summary in addition to returning objects.

```yaml
Type: SwitchParameter
Parameter Sets: Local, Path
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

- `PSPublishModule.ModuleStateInventoryResult
System.String`

## RELATED LINKS

- None
