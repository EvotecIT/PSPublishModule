---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Repair-ManagedModule
## SYNOPSIS
Repairs and verifies installed PowerShell modules through the managed module-state engine.

## SYNTAX
### __AllParameterSets
```powershell
Repair-ManagedModule [[-Name] <string[]>] [-InstallMissing] [-RequiredResource <Object>] [-RequiredResourceFile <string>] [-Inventory <ModuleStateInventoryResult>] [-InventoryPath <string>] [-ModulePath <string[]>] [-UserProfilePath <string[]>] [-IncludeAllUserProfiles] [-IncludeLoaded] [-MaintenanceReceiptPath <string[]>] [-Latest] [-Version <string>] [-MinimumVersion <string>] [-VersionPolicy <string>] [-Cleanup <string>] [-Family <string[]>] [-Scope <string>] [-ProfileName <string>] [-Repository <string>] [-Transport <ModuleStateDeliveryTransport>] [-ModuleRoot <string>] [-Prerelease] [-Force] [-AllowClobber] [-AcceptLicense] [-SkipDependencyCheck] [-AllowConflict] [-Plan] [-ShowSummary] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This command is the managed operator surface for module estate maintenance. It
inventories installed modules, plans stale-version, receipt-drift, source,
scope, family, and cleanup actions, and can apply the plan through the
managed delivery engine.

PSResourceGet has no equivalent estate-repair command. Use the lifecycle cmdlets for one requested operation and
this command when the desired outcome spans installed-state discovery, drift analysis, repair, and cleanup.

Repair keeps module copies in separate physical roots, PowerShell editions, scopes, and local user profiles
independent. Missing modules require an explicit ModuleRoot or exactly one eligible scanned root; ambiguous
destinations are reported and blocked.

Live apply performs delivery before exact-path old-version cleanup, revalidates loaded-module and dependency
safety, inventories the same roots again, and returns post-apply plan and convergence evidence. Operational
failures remain visible in the typed result and are also written as nonterminating errors.

## EXAMPLES

### EXAMPLE 1
```powershell
Repair-ManagedModule -Latest -Repository PSGallery -Plan -ShowSummary
```


### EXAMPLE 2
```powershell
Repair-ManagedModule -Family Graph -Repository PSGallery -Plan -ShowSummary
```


### EXAMPLE 3
```powershell
Repair-ManagedModule -Name Company.Tools,Company.Web -InstallMissing -Latest -Repository PSGallery -Plan -ShowSummary
```


### EXAMPLE 4
```powershell
Repair-ManagedModule -RequiredResourceFile .\required-resources.psd1 -Latest -Repository PSGallery -Plan -ShowSummary
```


### EXAMPLE 5
```powershell
Repair-ManagedModule -MaintenanceReceiptPath .\module-maintenance.json -ProfileName CompanyModules -AcceptLicense
```


### EXAMPLE 6
```powershell
Repair-ManagedModule -ModulePath $ps5Root,$ps7Root -Latest -Cleanup OldVersions -Repository PSGallery -Plan -ShowSummary
```


### EXAMPLE 7
```powershell
Repair-ManagedModule -UserProfilePath C:\Users\Alice,C:\Users\Service.PowerShell -Name Company.* -Latest -Repository CompanyModules -Plan -ShowSummary
```


### EXAMPLE 8
```powershell
Repair-ManagedModule -IncludeAllUserProfiles -Latest -Cleanup OldVersions -Repository CompanyModules -Confirm:$false -ShowSummary
```


## PARAMETERS

### -AcceptLicense
Accept package licenses when packages declare license acceptance is required.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowClobber
Allow managed delivery to overwrite exported command conflicts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowConflict
Allow apply preparation to continue when the plan contains error findings.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Cleanup
Optional old-version cleanup. Live apply removes only exact planned paths after delivery and safety revalidation.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, OldVersions

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Credential
Optional repository credential.

```yaml
Type: PSCredential
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecret
Optional repository credential secret.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Password, Token
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecretFilePath
Optional path to a file containing the repository credential secret.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: CredentialPath, TokenPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialUserName
Optional repository credential username.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Family
Built-in module family policies to include in repair planning.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: MicrosoftGraph, Graph, Az, ExchangeOnline, Teams

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force reinstall when repair selects the same installed version.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeAllUserProfiles
Discover standard PowerShell module roots below the current local profile container. Use UserProfilePath or ModulePath for redirected and custom layouts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissing
Plan installs for literal names that are not present in the selected inventory.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Inventory
Existing inventory object. When omitted, local module paths are inventoried.

```yaml
Type: ModuleStateInventoryResult
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -InventoryPath
Path to a previously written inventory JSON artifact.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Latest
Plan latest-version repair/update delivery for selected installed modules.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaintenanceReceiptPath
Optional module-state maintenance receipt artifacts used for drift checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinimumVersion
Minimum version used when repairing named modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModulePath
Explicit required module roots to inventory. Missing or inaccessible roots block apply. When omitted, optional PSModulePath entries are used.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleRoot
Explicit physical module root used to narrow inventory selection and managed delivery. It also resolves missing-module destination ambiguity.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Optional module names to repair. When omitted, all installed modules in scope are considered.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ModuleName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -Plan
Return the full repair and cleanup plan without applying any actions.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Prerelease
Include prerelease versions during managed delivery.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: AllowPrerelease
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved repository profile used by managed delivery.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Repository source or registered repository name used by managed delivery.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Source, RepositoryUri
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredResource
PSResourceGet-style required resource map used as desired module state.

```yaml
Type: Object
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredResourceFile
Path to a PowerShell data file containing a PSResourceGet-style required resource map.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Target installation scope used when selecting installed baseline modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CurrentUser, AllUsers

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSummary
Write a compact Spectre.Console summary.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipDependencyCheck
Skip installing dependencies declared by repaired packages.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: SkipDependenciesCheck
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Transport
Delivery transport used for install/update repair actions.

```yaml
Type: ModuleStateDeliveryTransport
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: PrivateModule, ManagedModule, Auto

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UserProfilePath
Explicit user profile home directories whose standard Windows PowerShell and PowerShell 7 module roots are inventoried.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Version
Exact required version used when repairing named modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: RequiredVersion
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionPolicy
NuGet-style version range policy used when repairing named modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
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

- `System.String[]
PSPublishModule.ModuleStateInventoryResult`

## OUTPUTS

- `PSPublishModule.ModuleStateWorkflowResult` — PowerShell-facing result for a complete module-state management workflow.

## RELATED LINKS

- None
