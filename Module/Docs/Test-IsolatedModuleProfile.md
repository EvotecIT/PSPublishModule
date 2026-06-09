---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-IsolatedModuleProfile
## SYNOPSIS
Validates a curated isolated module profile without importing it.

## SYNTAX
### __AllParameterSets
```powershell
Test-IsolatedModuleProfile [-Profile] <string> [-Name <string>] [-Path <string>] [-Quiet] [<CommonParameters>]
```

## DESCRIPTION
Test-IsolatedModuleProfile resolves the selected profile and module source, validates the
profile's minimum version, checks the expected script, manifest, binary, dependency, and
support files, and returns a detailed validation result without copying or importing the
generated wrapper.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-IsolatedModuleProfile -Profile ExchangeOnlineManagement
```

Resolves ExchangeOnlineManagement from PSModulePath and returns validation details.

### EXAMPLE 2
```powershell
Test-IsolatedModuleProfile -Profile MicrosoftGraphAuthentication -Path 'C:\Modules\Microsoft.Graph.Authentication\2.37.0\Microsoft.Graph.Authentication.psd1' | Format-List *
```

Uses the supplied manifest path as the source manifest and its parent as the module base.

### EXAMPLE 3
```powershell
Test-IsolatedModuleProfile -Profile MicrosoftTeams -Quiet
```

Returns True when the resolved profile source passes validation; otherwise returns False.

## PARAMETERS

### -Name
Optional module name override. When omitted, the profile's module name is used.

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

### -Path
Optional module base path or manifest path. When omitted, the profile module is resolved from PSModulePath.

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

### -Profile
Name of the built-in isolation profile to validate.

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

### -Quiet
Return only a Boolean validation result.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.IsolatedModuleProfileValidationResult
System.Boolean`

## RELATED LINKS

- None
