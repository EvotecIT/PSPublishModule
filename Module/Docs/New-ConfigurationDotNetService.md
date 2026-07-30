---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetService
## SYNOPSIS
Creates service packaging options for DotNet publish targets.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetService [-ServiceName <string>] [-DisplayName <string>] [-Description <string>] [-ExecutablePath <string>] [-Arguments <string>] [-GenerateInstallScript <bool>] [-GenerateUninstallScript <bool>] [-GenerateRunOnceScript <bool>] [-Lifecycle <DotNetPublishServiceLifecycleOptions>] [-Recovery <DotNetPublishServiceRecoveryOptions>] [-ConfigBootstrap <DotNetPublishConfigBootstrapRule[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates service packaging options for DotNet publish targets.

## EXAMPLES

### EXAMPLE 1
```powershell
$lifecycle = New-ConfigurationDotNetServiceLifecycle -Enabled
New-ConfigurationDotNetService -ServiceName 'My.Service' -GenerateInstallScript -GenerateUninstallScript -Lifecycle $lifecycle
```


## PARAMETERS

### -Arguments
Optional service arguments.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ConfigBootstrap
Optional config bootstrap rules.

```yaml
Type: DotNetPublishConfigBootstrapRule[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Description
Description text.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DisplayName
Display name.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExecutablePath
Executable path relative to output.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -GenerateInstallScript
Generates Install-Service.ps1.

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

### -GenerateRunOnceScript
Generates Run-Once.ps1.

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

### -GenerateUninstallScript
Generates Uninstall-Service.ps1.

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

### -Lifecycle
Optional lifecycle settings.

```yaml
Type: DotNetPublishServiceLifecycleOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Recovery
Optional recovery settings.

```yaml
Type: DotNetPublishServiceRecoveryOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ServiceName
Service name.

```yaml
Type: String
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

- `PowerForge.DotNetPublishServicePackageOptions`

## RELATED LINKS

- None
