---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleRepositoryProfile
## SYNOPSIS
Gets saved private module repository profiles.

## SYNTAX
### __AllParameterSets
```powershell
Get-ModuleRepositoryProfile [[-Name] <string>] [-Scope <ModuleRepositoryProfileScope>] [<CommonParameters>]
```

## DESCRIPTION
Profiles contain non-secret private gallery settings. Use this cmdlet to review the local repository name,
Azure Artifacts feed identity, bootstrap mode, and profile store path before connecting or publishing.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ModuleRepositoryProfile
```

Returns all profiles saved in the current user's PSPublishModule profile store.

### EXAMPLE 2
```powershell
Get-ModuleRepositoryProfile -Name Company
```

Returns the saved Azure Artifacts profile named Company.

## PARAMETERS

### -Name
Optional profile name. When omitted, all profiles are returned.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Profile store scope to read. The default reads user profiles first, then machine-wide profiles.

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

- `PSPublishModule.ModuleRepositoryProfileResult` — User-facing private module repository profile saved by PSPublishModule.

## RELATED LINKS

- None
