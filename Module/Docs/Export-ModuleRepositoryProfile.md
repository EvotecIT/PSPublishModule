---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Export-ModuleRepositoryProfile
## SYNOPSIS
Exports saved private module repository profiles to a non-secret JSON file.

## SYNTAX
### __AllParameterSets
```powershell
Export-ModuleRepositoryProfile [[-Name] <string[]>] [-Path] <string> [-Force] [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use this cmdlet when preparing managed desktop rollout artifacts for private galleries. The exported file contains
feed identity, registration preferences, trust, priority, and authentication mode metadata only. It does not contain
PATs, passwords, Entra tokens, or Azure Artifacts Credential Provider session caches.

## EXAMPLES

### EXAMPLE 1
```powershell
Export-ModuleRepositoryProfile -Path .\profiles.json -Force
```

Writes every saved private gallery profile to a JSON file that can be deployed to workstations.

### EXAMPLE 2
```powershell
Export-ModuleRepositoryProfile -Name Company -Path .\Company.profile.json -PassThru
```

Exports only the Company profile and returns the exported profile object.

## PARAMETERS

### -Force
Overwrite an existing destination file.

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

### -Name
Optional profile names to export. When omitted, all saved profiles are exported.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Returns the exported profile objects.

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

### -Path
Destination JSON file path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Profile store scope to export.

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
