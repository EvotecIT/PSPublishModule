---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-ModuleRepositoryProfile
## SYNOPSIS
Tests saved private module repository profiles and local authentication prerequisites.

## SYNTAX
### __AllParameterSets
```powershell
Test-ModuleRepositoryProfile [[-ProfileName] <string>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet performs a non-mutating readiness check for private gallery onboarding. It reads the saved profile,
resolves Azure Artifacts feed endpoints, and reports whether local PSResourceGet, PowerShellGet, and Azure
Artifacts Credential Provider prerequisites are ready for Entra-first module install/update flows.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-ModuleRepositoryProfile -ProfileName Company
```

Returns profile and local prerequisite readiness for the saved Company profile.

### EXAMPLE 2
```powershell
Test-ModuleRepositoryProfile
```

Returns readiness information for every saved private gallery profile.

## PARAMETERS

### -ProfileName
Optional profile name. When omitted, all saved profiles are tested.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Name, Profile
Possible values:

Required: False
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

- `PSPublishModule.ModuleRepositoryProfileReadinessResult` — Readiness information for a saved private module repository profile.

## RELATED LINKS

- None
