---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Remove-ManagedModuleRepository
## SYNOPSIS
Removes a saved managed module repository profile.

## SYNTAX
### __AllParameterSets
```powershell
Remove-ManagedModuleRepository [-Name] <string> [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Removing a profile deletes only PSPublishModule's non-secret repository settings. It does not unregister native
PowerShell repository state or clear external credential-provider token caches.

## EXAMPLES

### EXAMPLE 1
```powershell
Remove-ManagedModuleRepository -Name Company
```

Deletes the saved Company profile from the current user's profile store.

### EXAMPLE 2
```powershell
Remove-ManagedModuleRepository -Name Company -PassThru
```

Returns True when the profile was removed, otherwise False.

## PARAMETERS

### -Name
Profile name to remove.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Returns true when a profile was removed, otherwise false.

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

### -Scope
Profile store scope to remove from.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: User, Machine, All

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

- `System.Boolean`

## RELATED LINKS

- None
