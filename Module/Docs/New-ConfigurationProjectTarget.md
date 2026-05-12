---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectTarget
## SYNOPSIS
Creates a high-level target entry for a PowerShell-authored project build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectTarget -Name <string> -ProjectPath <string> [-Kind <DotNetPublishTargetKind>] [-Framework <string>] [-Frameworks <string[]>] [-Runtimes <string[]>] [-Style <DotNetPublishStyle>] [-Styles <DotNetPublishStyle[]>] [-OutputPath <string>] [-OutputType <ConfigurationProjectTargetOutputType[]>] [-Zip] [-UseStaging <bool>] [-ClearOutput <bool>] [-KeepSymbols] [-KeepDocs] [-ReadyToRun <bool>] [<CommonParameters>]
```

## DESCRIPTION
Creates a high-level target entry for a PowerShell-authored project build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectTarget -Name 'Name' -ProjectPath 'C:\Path'
```


## PARAMETERS

### -ClearOutput
Clears the final output directory before copy.

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

### -Framework
Primary target framework.

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

### -Frameworks
Optional framework matrix values.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KeepDocs
Keeps documentation files.

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

### -KeepSymbols
Keeps symbol files.

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

### -Kind
Optional target kind metadata.

```yaml
Type: DotNetPublishTargetKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Unknown, Cli, Service, Library

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Friendly target name.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Optional output path template.

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

### -OutputType
Requested output categories for this target. Defaults to Tool when omitted.

```yaml
Type: ConfigurationProjectTargetOutputType[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Tool, Portable

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectPath
Path to the project file to publish.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ReadyToRun
Optional ReadyToRun override.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Runtimes
Optional runtime matrix values.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Runtime
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Style
Primary publish style.

```yaml
Type: DotNetPublishStyle
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, FrameworkDependent, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Styles
Optional style matrix values.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, FrameworkDependent, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseStaging
Uses a temporary staging directory before final copy.

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

### -Zip
Creates a zip for the raw publish output.

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

- `PowerForge.ConfigurationProjectTarget`

## RELATED LINKS

- None

