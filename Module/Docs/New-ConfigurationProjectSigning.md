---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectSigning
## SYNOPSIS
Creates signing defaults for a PowerShell-authored project build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectSigning [-Mode <ConfigurationProjectSigningMode>] [-ToolPath <string>] [-Thumbprint <string>] [-SubjectName <string>] [-OnMissingTool <DotNetPublishPolicyMode>] [-OnFailure <DotNetPublishPolicyMode>] [-TimestampUrl <string>] [-Description <string>] [-Url <string>] [-Csp <string>] [-KeyContainer <string>] [-OverwriteSigned] [<CommonParameters>]
```

## DESCRIPTION
Creates signing defaults for a PowerShell-authored project build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectSigning -ToolPath 'C:\Path'
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -Mode
Signing activation mode.

```yaml
Type: ConfigurationProjectSigningMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Disabled, OnDemand, Enabled

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OnFailure
Policy when signing a file fails.

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

### -OnMissingTool
Policy when the signing tool is missing.

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

### -OverwriteSigned
Explicitly replaces existing signatures instead of preserving already signed files.

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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -ToolPath
Optional path to the signing tool.

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
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ConfigurationProjectSigning`

## RELATED LINKS

- None
