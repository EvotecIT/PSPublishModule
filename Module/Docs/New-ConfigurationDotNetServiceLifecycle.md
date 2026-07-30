---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetServiceLifecycle
## SYNOPSIS
Creates service lifecycle execution options for DotNet publish service targets.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetServiceLifecycle [-Enabled] [-Mode <DotNetPublishServiceLifecycleMode>] [-StopIfExists <bool>] [-DeleteIfExists <bool>] [-Install <bool>] [-Start <bool>] [-Verify <bool>] [-StopTimeoutSeconds <int>] [-HealthCheck <DotNetPublishServiceHealthCheck[]>] [-WhatIfMode] [-OnUnsupportedPlatform <DotNetPublishPolicyMode>] [-OnExecutionFailure <DotNetPublishPolicyMode>] [<CommonParameters>]
```

## DESCRIPTION
Creates service lifecycle execution options for DotNet publish service targets.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetServiceLifecycle -Enabled -Mode Step -StopIfExists -DeleteIfExists -Install -Start -Verify
```


## PARAMETERS

### -DeleteIfExists
Delete existing service before reinstall.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Enabled
Enables lifecycle execution.

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

### -HealthCheck
Optional HTTP readiness checks executed after service state verification.

```yaml
Type: DotNetPublishServiceHealthCheck[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Install
Install or reinstall service.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Mode
Lifecycle execution mode.

```yaml
Type: DotNetPublishServiceLifecycleMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Step, InlineRebuild

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OnExecutionFailure
Policy on lifecycle execution failures.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OnUnsupportedPlatform
Policy on non-Windows platforms.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Start
Start service after install.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -StopIfExists
Stop existing service before reinstall.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -StopTimeoutSeconds
Stop timeout in seconds.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Verify
Verify service status after actions.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIfMode
Simulates lifecycle actions.

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

- `PowerForge.DotNetPublishServiceLifecycleOptions`

## RELATED LINKS

- None
