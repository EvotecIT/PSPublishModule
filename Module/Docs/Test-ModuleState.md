---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-ModuleState
## SYNOPSIS
Tests module state against a desired state or an existing plan.

## SYNTAX
### Files
```powershell
Test-ModuleState [-InventoryPath] <string> [-DesiredStatePath] <string> [-MaintenanceReceiptPath <string[]>] [-Family <string[]>] [-Cleanup <string>] [-PassThru] [-ShowSummary] [-FailOnConflict] [<CommonParameters>]
```

### Objects
```powershell
Test-ModuleState [-Inventory] <ModuleStateInventoryResult> [-DesiredState] <Object> [-MaintenanceReceiptPath <string[]>] [-Family <string[]>] [-Cleanup <string>] [-PassThru] [-ShowSummary] [-FailOnConflict] [<CommonParameters>]
```

### Plan
```powershell
Test-ModuleState [-Plan] <ModuleStatePlanResult> [-PassThru] [-ShowSummary] [-FailOnConflict] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet evaluates the same plan produced by Get-ModuleStatePlan and returns
whether the current inventory is compliant. It does not mutate the machine.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Get-ModuleState | Test-ModuleState -DesiredState @{ Modules = @('Company.Tools') }
```

Tests the current inventory using normal PowerShell objects.

### EXAMPLE 2
```powershell
PS> Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json | Test-ModuleState
```

Tests compliance from a plan already produced by Get-ModuleStatePlan.

### EXAMPLE 3
```powershell
PS> Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json
```

Returns $true when no changes or error findings are required.

### EXAMPLE 4
```powershell
PS> Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -FailOnConflict
```

Throws when receipt-backed module state no longer matches the current inventory.

### EXAMPLE 5
```powershell
PS> Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Family MicrosoftGraph -PassThru
```

Includes built-in family conflict findings in the compliance result.

### EXAMPLE 6
```powershell
PS> Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Cleanup OldVersions -PassThru
```

Returns non-compliant when old managed versions would require cleanup actions.

### EXAMPLE 7
```powershell
PS> Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -PassThru
```

Returns compliance, required-action counts, error counts, and the underlying plan.

## PARAMETERS

### -Cleanup
Gets or sets optional cleanup planning for validation.

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

### -FailOnConflict
Gets or sets whether non-compliant module state should fail with a terminating error.

```yaml
Type: SwitchParameter
Parameter Sets: Files, Objects, Plan
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Family
Gets or sets built-in module family policies to include in validation.

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

### -PassThru
Gets or sets whether to return the detailed test result instead of a Boolean.

```yaml
Type: SwitchParameter
Parameter Sets: Files, Objects, Plan
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Plan
Gets or sets an existing module-state plan object to test.

```yaml
Type: ModuleStatePlanResult
Parameter Sets: Plan
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -ShowSummary
Gets or sets whether to render a Spectre.Console summary in addition to returning objects.

```yaml
Type: SwitchParameter
Parameter Sets: Files, Objects, Plan
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

- `PSPublishModule.ModuleStateInventoryResult
PSPublishModule.ModuleStatePlanResult` — PowerShell-facing module-state plan result.

## OUTPUTS

- `System.Boolean
PSPublishModule.ModuleStateTestResult` — PowerShell-facing module-state test result.

## RELATED LINKS

- None
