---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationReleaseProtection
## SYNOPSIS
Creates opt-in source-state and provenance protections for module releases.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationReleaseProtection [-RequireCleanSource] [-RequireSourceUnchanged] [-GenerateProvenance] [<CommonParameters>]
```

## DESCRIPTION
All protections are disabled unless explicitly selected. Generating provenance also requires a clean source
snapshot and protects it from changes through packaging.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationReleaseProtection -RequireSourceUnchanged
```


### EXAMPLE 2
```powershell
New-ConfigurationReleaseProtection -GenerateProvenance
```


## PARAMETERS

### -GenerateProvenance
Embeds signed source provenance in eligible signed GitHub module artefacts and implies both source checks.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequireCleanSource
Requires a clean Git source snapshot when the release pipeline is planned.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequireSourceUnchanged
Requires release inputs to remain unchanged after planning and implies a clean source snapshot.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ConfigurationReleaseProtectionSegment`

## RELATED LINKS

- None
