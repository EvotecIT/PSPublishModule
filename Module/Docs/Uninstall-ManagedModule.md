---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Uninstall-ManagedModule
## SYNOPSIS
Uninstalls installed PowerShell module versions through the managed module engine.

## SYNTAX
### __AllParameterSets
```powershell
Uninstall-ManagedModule [-Name] <string[]> [-Version <string>] [-Prerelease] [-Scope <ManagedModuleInstallScope>] [-ShellEdition <ManagedModuleShellEdition>] [-ModuleRoot <string>] [-SkipDependencyCheck] [-LoadedModule <ManagedModuleLoadedModule[]>] [-AllowLoadedModuleUninstall] [-Plan] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This command removes modules from the selected managed module root without invoking PowerShellGet or
PSResourceGet. It follows PSResourceGet-shaped uninstall selection semantics while adding managed
dependency and loaded-module safety checks.

## EXAMPLES

### EXAMPLE 1
```powershell
Uninstall-ManagedModule -Name Company.Tools
```


### EXAMPLE 2
```powershell
Uninstall-ManagedModule -Name Company.Tools -Version '[1.0.0,2.0.0)' -Plan
```


## PARAMETERS

### -AllowLoadedModuleUninstall
Allow removal of module versions that appear loaded in the current PowerShell session.

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

### -LoadedModule
Loaded module evidence used to block risky in-session uninstalls.

```yaml
Type: ManagedModuleLoadedModule[]
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
Explicit module root. Use with Scope Custom.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -Name
Module names or wildcard patterns to uninstall.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ModuleName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -Plan
Return an inspectable uninstall plan without removing files.

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
Restrict matching to prerelease module versions.

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

### -Scope
Install scope used when ModuleRoot is not supplied.

```yaml
Type: ManagedModuleInstallScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CurrentUser, AllUsers, Custom

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShellEdition
PowerShell path family used when resolving default CurrentUser or AllUsers module roots.

```yaml
Type: ManagedModuleShellEdition
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, Desktop, Core

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipDependencyCheck
Skip checking whether removed modules are still required by other installed modules.

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

### -Version
Exact version or NuGet-style version range to uninstall. When omitted, the latest matching version is selected.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: RequiredVersion
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String[]
System.String`

## OUTPUTS

- `PowerForge.ManagedModuleUninstallResult
PowerForge.ManagedModuleUninstallPlan`

## RELATED LINKS

- None
