---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetSign
## SYNOPSIS
Creates signing options for DotNet publish targets and installers.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetSign [-Enabled] [-ToolPath <string>] [-OnMissingTool <DotNetPublishPolicyMode>] [-OnSignFailure <DotNetPublishPolicyMode>] [-Thumbprint <string>] [-SubjectName <string>] [-TimestampUrl <string>] [-Description <string>] [-Url <string>] [-Csp <string>] [-KeyContainer <string>] [<CommonParameters>]
```

## DESCRIPTION
Creates signing options for DotNet publish targets and installers.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetSign -Enabled -Thumbprint '0123456789ABCDEF' -OnMissingTool Fail -OnSignFailure Fail
```

## PARAMETERS

### -Csp
Optional CSP name.

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

### -Description
Optional signature description.

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

### -Enabled
Enables Authenticode signing.

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

### -KeyContainer
Optional key container name.

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

### -OnMissingTool
Policy applied when signing tool cannot be resolved.

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

### -OnSignFailure
Policy applied when a file signing operation fails.

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

### -SubjectName
Optional certificate subject name.

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

### -Thumbprint
Optional certificate thumbprint.

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

### -TimestampUrl
Optional timestamp URL.

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

### -ToolPath
Optional path to signtool.exe.

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

### -Url
Optional signature URL.

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

- `PowerForge.DotNetPublishSignOptions`

## RELATED LINKS

- None

