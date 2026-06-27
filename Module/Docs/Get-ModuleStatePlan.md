---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleStatePlan
## SYNOPSIS
Builds a module-state plan from module-state objects or artifacts.

## SYNTAX
### Files
```powershell
Get-ModuleStatePlan [-InventoryPath] <string> [-DesiredStatePath] <string> [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-OutputPath <string>] [-ShowSummary] [-AsJson] [<CommonParameters>]
```

### Objects
```powershell
Get-ModuleStatePlan [-Inventory] <ModuleStateInventoryResult> [-DesiredState] <Object> [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-OutputPath <string>] [-ShowSummary] [-AsJson] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the plan-only entry point for ModuleState. It does not install, update,
remove, or repair modules. Use the returned plan as support evidence or as input for a
later apply workflow.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> $inventory = Get-ModuleState; $desired = @{ Modules = @(@{ Name = 'Company.Tools'; Version = '=1.2.0' }) }; $inventory | Get-ModuleStatePlan -DesiredState $desired
```

Uses normal PowerShell objects for inventory and desired state, then returns a typed plan object.

### EXAMPLE 2
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json
```

Reads inventory and desired-state artifacts, then returns the proposed actions and findings.

### EXAMPLE 3
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json
```

Returns findings when a previously maintained module is missing, has drifted version, source, or scope.

### EXAMPLE 4
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -Repair
```

Returns install or update intents pinned to the receipt-managed version where the current machine drifted.

### EXAMPLE 5
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Cleanup OldVersions
```

Returns cleanup actions for old, unloaded versions of modules that ModuleState already manages.

### EXAMPLE 6
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Family MicrosoftGraph
```

Adds the built-in MicrosoftGraph family policy without creating a separate family cmdlet.

### EXAMPLE 7
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -AsJson
```

Returns the plan as JSON for CI logs or support bundles.

## PARAMETERS

### -AsJson
Gets or sets whether to return the plan as JSON instead of a typed result object.

```yaml
Type: SwitchParameter
Parameter Sets: Files, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Cleanup
Gets or sets optional cleanup planning for managed modules.

```yaml
Type: String
Parameter Sets: Files, Objects
Aliases: None
Possible values: None, OldVersions

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DesiredState
Gets or sets the desired module state as a hashtable, PSCustomObject, or array of module objects.

```yaml
Type: Object
Parameter Sets: Objects
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DesiredStatePath
Gets or sets the path to the module-state desired-state artifact.

```yaml
Type: String
Parameter Sets: Files
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Family
Gets or sets built-in module family policies to include in the plan.

```yaml
Type: String[]
Parameter Sets: Files, Objects
Aliases: None
Possible values: MicrosoftGraph, Graph, Az, ExchangeOnline, Teams

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Inventory
Gets or sets the in-memory module-state inventory object.

```yaml
Type: ModuleStateInventoryResult
Parameter Sets: Objects
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -InventoryPath
Gets or sets the path to the module-state inventory artifact.

```yaml
Type: String
Parameter Sets: Files
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaintenanceReceiptPath
Gets or sets optional module-state maintenance receipt artifacts used for drift checks.

```yaml
Type: String[]
Parameter Sets: Files, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Gets or sets the path where a plan JSON artifact should be written.

```yaml
Type: String
Parameter Sets: Files, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repair
Gets or sets whether receipt drift should produce conservative repair actions.

```yaml
Type: SwitchParameter
Parameter Sets: Files, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSummary
Gets or sets whether to render a Spectre.Console summary in addition to returning objects.

```yaml
Type: SwitchParameter
Parameter Sets: Files, Objects
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

- `PSPublishModule.ModuleStateInventoryResult` — PowerShell-facing module-state inventory result.

## OUTPUTS

- `PSPublishModule.ModuleStatePlanResult
System.String`

## RELATED LINKS

- None
