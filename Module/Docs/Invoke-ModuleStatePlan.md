---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ModuleStatePlan
## SYNOPSIS
Prepares private-module delivery commands and an optional receipt from a module-state plan.

## SYNTAX
### Files (Default)
```powershell
Invoke-ModuleStatePlan [-InventoryPath] <string> [-DesiredStatePath] <string> [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-ProfileName <string>] [-Repository <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### PlanPath
```powershell
Invoke-ModuleStatePlan [-PlanPath] <string> [-ProfileName <string>] [-Repository <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### PlanObject
```powershell
Invoke-ModuleStatePlan [-Plan] <ModuleStatePlanResult> [-ProfileName <string>] [-Repository <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Objects
```powershell
Invoke-ModuleStatePlan [-Inventory] <ModuleStateInventoryResult> [-DesiredState] <Object> [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-ProfileName <string>] [-Repository <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
By default, this cmdlet prepares command intents and receipts only. When -Execute is supplied, it
runs grouped install and update actions through the same private-module workflow used by
Install-PrivateModule and Update-PrivateModule.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Get-ModuleState | Get-ModuleStatePlan -DesiredState @{ Modules = @('Company.Tools') } | Invoke-ModuleStatePlan -Repository Company
```

Uses typed PowerShell objects through the full inventory, plan, and apply-preparation flow.

### EXAMPLE 2
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -ProfileName Company
```

Returns the private-module commands needed to reconcile the plan.

### EXAMPLE 3
```powershell
PS> Invoke-ModuleStatePlan -PlanPath .\module-state.plan.json -Repository Company
```

Reads a plan previously written by Get-ModuleStatePlan -AsJson and prepares delivery commands from that approved artifact.

### EXAMPLE 4
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Repository Company -Execute -WhatIf
```

Shows the grouped private-module workflow operations that would reconcile the plan.

### EXAMPLE 5
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -ProfileName Company
```

Includes receipt drift findings in the plan before private-module delivery is prepared.

### EXAMPLE 6
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -Repair -Repository Company
```

Prepares private-module delivery command intents for receipt-managed modules that drifted.

### EXAMPLE 7
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Cleanup OldVersions -ProfileName Company
```

Includes cleanup actions in the returned plan, but does not convert them to private-module delivery commands.

### EXAMPLE 8
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Family MicrosoftGraph -Repository Company
```

Includes built-in family findings before preparing private-module delivery commands.

### EXAMPLE 9
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Repository Company -MaintenanceReceiptOutputPath .\module-state.maintenance.json
```

Writes a maintenance receipt for modules whose maintained version is known from exact policy or satisfied inventory.

### EXAMPLE 10
```powershell
PS> Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -ProfileName Company -ReceiptPath .\module-state.receipt.json
```

Writes a JSON receipt describing the prepared delivery commands and any requested execution evidence.

## PARAMETERS

### -AcceptLicense
Gets or sets whether managed module delivery accepts package licenses.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowClobber
Gets or sets whether managed module delivery may overwrite exported command conflicts.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowConflict
Gets or sets whether apply preparation should continue when the plan contains error findings.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AsJson
Gets or sets whether to return the result as JSON instead of a typed object.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
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

### -CredentialSecret
Gets or sets an optional repository credential secret for repository delivery.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: Password, Token
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecretFilePath
Gets or sets an optional path to a file containing the repository credential secret.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: CredentialPath, TokenPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialUserName
Gets or sets an optional repository credential username for repository delivery.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: UserName
Possible values:

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

### -Execute
Gets or sets whether the prepared private-module workflow should be executed.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
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

### -Force
Gets or sets whether prepared install commands include Force.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Gets or sets whether prepared private-module commands include InstallPrerequisites.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

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

### -MaintenanceReceiptOutputPath
Gets or sets the path where a drift-checkable module-state maintenance receipt should be written.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
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

### -ModuleRoot
Gets or sets a custom module root for managed module delivery.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Plan
Gets or sets an existing module-state plan object.

```yaml
Type: ModuleStatePlanResult
Parameter Sets: PlanObject
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -PlanPath
Gets or sets the path to a module-state plan artifact.

```yaml
Type: String
Parameter Sets: PlanPath
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PostApplyModulePath
Gets or sets module roots to inventory after execution.

```yaml
Type: String[]
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Prerelease
Gets or sets whether prepared private-module commands include Prerelease.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Gets or sets the private module repository profile used by prepared delivery commands.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PromptForCredential
Gets or sets whether to prompt for repository credentials.

```yaml
Type: SwitchParameter
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: Interactive
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ReceiptPath
Gets or sets the path where a module-state receipt should be written.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
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

### -Repository
Gets or sets the registered private module repository used by prepared delivery commands.

```yaml
Type: String
Parameter Sets: Files, PlanPath, PlanObject, Objects
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
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Transport
Gets or sets the delivery transport used for prepared and executed install/update actions.

```yaml
Type: ModuleStateDeliveryTransport
Parameter Sets: Files, PlanPath, PlanObject, Objects
Aliases: None
Possible values: PrivateModule, ManagedModule

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PSPublishModule.ModuleStatePlanResult
PSPublishModule.ModuleStateInventoryResult` — PowerShell-facing module-state inventory result.

## OUTPUTS

- `PSPublishModule.ModuleStateApplyResult
System.String`

## RELATED LINKS

- None
