---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-AppStoreConnectGovernanceConfig
## SYNOPSIS
Validates declarative App Store commercial and compliance state without contacting Apple.

## SYNTAX
### __AllParameterSets
```powershell
Test-AppStoreConnectGovernanceConfig [-ConfigPath] <string> [-PassThru] [<CommonParameters>]
```

## DESCRIPTION
Validates declarative App Store commercial and compliance state without contacting Apple.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-AppStoreConnectGovernanceConfig -ConfigPath 'C:\Path'
```


## PARAMETERS

### -ConfigPath
Path to the governance JSON configuration.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -PassThru
Returns structured findings instead of a Boolean.

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

- `System.String`

## OUTPUTS

- `System.Boolean
PowerForge.AppStoreConnectGovernanceFinding`

## RELATED LINKS

- None
