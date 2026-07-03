---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ManagedModule
## SYNOPSIS
Gets installed PowerShell modules from managed module inventory.

## SYNTAX
### Local (Default)
```powershell
Get-ManagedModule [[-Name] <string[]>] [-ModulePath <string[]>] [-IncludeLoaded] [-AsInventory] [-AsJson] [-ShowSummary] [<CommonParameters>]
```

### Path
```powershell
Get-ManagedModule [[-Name] <string[]>] -Path <string> [-IncludeLoaded] [-AsInventory] [-AsJson] [-ShowSummary] [<CommonParameters>]
```

## DESCRIPTION
This command is the PowerShell-native inventory surface for managed module
maintenance. It returns installed module rows by default while reusing the
same inventory engine that powers the advanced ModuleState workflow.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ManagedModule
```


### EXAMPLE 2
```powershell
Get-ManagedModule -Name Microsoft.Graph.* -IncludeLoaded -ShowSummary
```


## PARAMETERS

### -AsInventory
Return the full inventory object instead of installed module rows.

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

### -AsJson
Return JSON instead of typed objects.

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
Include modules loaded in the current runspace as inventory evidence.

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
Explicit module roots to scan. When omitted, PSModulePath is used.

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

### -Name
Optional module name filters. Wildcards are supported.

```yaml
Type: String[]
Parameter Sets: Local, Path
Aliases: ModuleName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path to a previously written inventory JSON artifact.

```yaml
Type: String
Parameter Sets: Path
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSummary
Write a compact Spectre.Console inventory summary.

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

- `PSPublishModule.ModuleStateInstalledModuleResult
PSPublishModule.ModuleStateInventoryResult
System.String`

## RELATED LINKS

- None
