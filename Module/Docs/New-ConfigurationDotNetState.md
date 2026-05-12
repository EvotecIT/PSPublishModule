---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetState
## SYNOPSIS
Creates preserve/restore state options for DotNet publish targets.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetState [-Enabled] [-StoragePath <string>] [-ClearStorage <bool>] [-OnMissingSource <DotNetPublishPolicyMode>] [-OnRestoreFailure <DotNetPublishPolicyMode>] [-Rules <DotNetPublishStateRule[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates preserve/restore state options for DotNet publish targets.

## EXAMPLES

### EXAMPLE 1
```powershell
$rule = New-ConfigurationDotNetStateRule -SourcePath 'config.json' -Overwrite
New-ConfigurationDotNetState -Enabled -Rules $rule -StoragePath 'Artifacts/DotNetPublish/State/{target}/{rid}/{framework}/{style}'
```


## PARAMETERS

### -ClearStorage
Clears storage before preserving state.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enabled
Enables state preservation.

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

### -OnMissingSource
Policy for missing source paths.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnRestoreFailure
Policy for restore failures.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Rules
State rules.

```yaml
Type: DotNetPublishStateRule[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -StoragePath
Optional storage path template.

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

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishStatePreservationOptions`

## RELATED LINKS

- None

