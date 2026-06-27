---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ModuleState
## SYNOPSIS
Runs the one-stop module-state management workflow.

## SYNTAX
### Modules (Default)
```powershell
Invoke-ModuleState [-ModuleName] <string[]> [-Latest] [-RequiredVersion <string>] [-MinimumVersion <string>] [-VersionPolicy <string>] [-Inventory <ModuleStateInventoryResult>] [-InventoryPath <string>] [-ModulePath <string[]>] [-IncludeLoaded] [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-Scope <string>] [-ProfileName <string>] [-Repository <string>] [-InventoryOutputPath <string>] [-PlanOutputPath <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-SavePath <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Installed
```powershell
Invoke-ModuleState -Installed [-Latest] [-Inventory <ModuleStateInventoryResult>] [-InventoryPath <string>] [-ModulePath <string[]>] [-IncludeLoaded] [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-Scope <string>] [-ProfileName <string>] [-Repository <string>] [-InventoryOutputPath <string>] [-PlanOutputPath <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-SavePath <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### DesiredState
```powershell
Invoke-ModuleState [-DesiredState] <Object> [-Inventory <ModuleStateInventoryResult>] [-InventoryPath <string>] [-ModulePath <string[]>] [-IncludeLoaded] [-MaintenanceReceiptPath <string[]>] [-Repair] [-Cleanup <string>] [-Family <string[]>] [-ProfileName <string>] [-Repository <string>] [-InventoryOutputPath <string>] [-PlanOutputPath <string>] [-ReceiptPath <string>] [-MaintenanceReceiptOutputPath <string>] [-InstallPrerequisites] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-Execute] [-PostApplyModulePath <string[]>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-AllowConflict] [-ShowSummary] [-AsJson] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet inventories modules, creates a plan, evaluates compliance, prepares
private-module delivery, and optionally executes the install/update workflow.
It is the operator-friendly entry point; the lower-level ModuleState cmdlets
remain available when inventory, plan, test, and apply need to be inspected
independently.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Invoke-ModuleState -ModuleName Company.Tools -RequiredVersion 1.2.0 -Repository CompanyModules -Scope CurrentUser -ShowSummary
```

Inventories the current machine, plans the exact module version, and returns a workflow result without mutating the machine.

### EXAMPLE 2
```powershell
PS> Invoke-ModuleState -DesiredState $desired -Repository CompanyModules -Repair -Execute -ShowSummary
```

Runs inventory, repair planning, and private-module delivery in one command.

## PARAMETERS

### -AcceptLicense
Gets or sets whether managed module delivery accepts package licenses.

```yaml
Type: SwitchParameter
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AsJson
Gets or sets whether to return the workflow result as JSON instead of a typed object.

```yaml
Type: SwitchParameter
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DesiredState
Gets or sets a desired module state as a hashtable, PSCustomObject, or array of module objects.

```yaml
Type: Object
Parameter Sets: DesiredState
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Execute
Gets or sets whether the prepared private-module workflow should be executed.

```yaml
Type: SwitchParameter
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Installed
Gets or sets whether all currently installed modules should be maintained.

```yaml
Type: SwitchParameter
Parameter Sets: Installed
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Gets or sets whether prepared private-module commands include InstallPrerequisites.

```yaml
Type: SwitchParameter
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Inventory
Gets or sets an existing inventory object. When omitted, local module paths are inventoried.

```yaml
Type: ModuleStateInventoryResult
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -InventoryOutputPath
Gets or sets the path where the captured inventory artifact should be written.

```yaml
Type: String
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InventoryPath
Gets or sets the path to an inventory artifact. When omitted, local module paths are inventoried.

```yaml
Type: String
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Latest
Gets or sets whether module names should be checked for the latest repository version.

```yaml
Type: SwitchParameter
Parameter Sets: Modules, Installed
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaintenanceReceiptOutputPath
Gets or sets the path where a drift-checkable module-state maintenance receipt should be written.

```yaml
Type: String
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinimumVersion
Gets or sets an optional minimum version used with -ModuleName.

```yaml
Type: String
Parameter Sets: Modules
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleName
Gets or sets module names for the convenience desired-state shape.

```yaml
Type: String[]
Parameter Sets: Modules
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModulePath
Gets or sets explicit module roots to inventory. When omitted, $env:PSModulePath is used.

```yaml
Type: String[]
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PlanOutputPath
Gets or sets the path where the generated plan artifact should be written.

```yaml
Type: String
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PostApplyModulePath
Gets or sets module roots to inventory after execution.

```yaml
Type: String[]
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Gets or sets the private module repository profile used by delivery.

```yaml
Type: String
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Gets or sets the registered private module repository used by delivery.

```yaml
Type: String
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Gets or sets an optional exact required version used with -ModuleName.

```yaml
Type: String
Parameter Sets: Modules
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SavePath
Gets or sets a target module root for save-style managed module delivery.

```yaml
Type: String
Parameter Sets: Modules, Installed
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Gets or sets the target installation scope for the convenience desired-state shape.

```yaml
Type: String
Parameter Sets: Modules, Installed
Aliases: None
Possible values: CurrentUser, AllUsers

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSummary
Gets or sets whether to render Spectre.Console summaries in addition to returning objects.

```yaml
Type: SwitchParameter
Parameter Sets: Modules, Installed, DesiredState
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
Parameter Sets: Modules, Installed, DesiredState
Aliases: None
Possible values: PrivateModule, ManagedModule, Auto

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionPolicy
Gets or sets an optional version policy used with -ModuleName.

```yaml
Type: String
Parameter Sets: Modules
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

- `PSPublishModule.ModuleStateWorkflowResult
System.String`

## RELATED LINKS

- None
