---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetServiceRecovery
## SYNOPSIS
Creates service recovery options for DotNet publish service targets.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetServiceRecovery [-Enabled] [-ResetPeriodSeconds <int>] [-RestartDelaySeconds <int>] [-ApplyToNonCrashFailures <bool>] [-OnFailure <DotNetPublishPolicyMode>] [<CommonParameters>]
```

## DESCRIPTION
Creates service recovery options for DotNet publish service targets.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetServiceRecovery -Enabled -ResetPeriodSeconds 86400 -RestartDelaySeconds 60 -ApplyToNonCrashFailures
```


## PARAMETERS

### -ApplyToNonCrashFailures
Applies recovery actions for non-crash failures.

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
Enables applying recovery policy.

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

### -OnFailure
Policy on recovery command failures.

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

### -ResetPeriodSeconds
Failure reset period in seconds.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RestartDelaySeconds
Restart delay in seconds.

```yaml
Type: Int32
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

- `PowerForge.DotNetPublishServiceRecoveryOptions`

## RELATED LINKS

- None

