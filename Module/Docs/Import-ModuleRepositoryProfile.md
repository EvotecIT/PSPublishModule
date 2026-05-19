---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-ModuleRepositoryProfile
## SYNOPSIS
Imports private module repository profiles from a non-secret JSON file.

## SYNTAX
### __AllParameterSets
```powershell
Import-ModuleRepositoryProfile [-Path] <string> [-Overwrite] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use this cmdlet on managed workstations, build agents, or administrator consoles to load private gallery profiles
that were created with Export-ModuleRepositoryProfile. Imported profiles still contain only feed identity
and local behavior settings; authentication remains owned by PSResourceGet and the Azure Artifacts Credential
Provider.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-ModuleRepositoryProfile -Path .\profiles.json
```

Imports profiles from the JSON file into the current user's PSPublishModule profile store.

### EXAMPLE 2
```powershell
Import-ModuleRepositoryProfile -Path .\profiles.json -Overwrite
```

Replaces matching profile names with the definitions from the JSON file.

## PARAMETERS

### -Overwrite
Replace saved profiles with the same name.

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
Source JSON file path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 0
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
