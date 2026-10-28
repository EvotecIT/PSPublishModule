---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-PowerForgeBundlePostProcess
## SYNOPSIS
Applies reusable bundle post-process rules from a dotnet publish config to an existing bundle directory.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-PowerForgeBundlePostProcess -BundleId <string> -BundleRoot <string> [-ConfigPath <string>] [-ProjectRoot <string>] [-TargetName <string>] [-Runtime <string>] [-Framework <string>] [-Style <string>] [-Configuration <string>] [-ZipPath <string>] [-SourceOutputPath <string>] [-Token <string[]>] [-DeletePattern <string[]>] [-SkipArchive] [-SkipMetadata] [-Plan] [-ExitCode] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Applies reusable bundle post-process rules from a dotnet publish config to an existing bundle directory.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-PowerForgeBundlePostProcess
```


## PARAMETERS

### -BundleId
Bundle identifier whose post-process rules should be applied.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Bundle
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -BundleRoot
Existing bundle directory that will be post-processed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ConfigPath
Path to the dotnet publish configuration file. When omitted, the cmdlet searches the current
directory and its parents for standard PowerForge dotnet publish config names.

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

### -Configuration
Optional build configuration label.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DeletePattern
Optional additional delete patterns appended to config-defined post-process rules.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExitCode
Sets host exit code: 0 on success, 1 on failure.

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

### -Framework
Optional framework used for metadata and template tokens.

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

### -Plan
Emits the resolved request without applying the post-process rules.

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

### -ProjectRoot
Optional project root override used for path-safety checks.

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

### -Runtime
Optional runtime identifier used for metadata and template tokens.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Rid
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipArchive
Skips archive-directory rules from the config.

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

### -SkipMetadata
Skips metadata emission from the config.

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

### -SourceOutputPath
Optional source output token value.

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

### -Style
Optional publish style label used for metadata and template tokens.

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

### -TargetName
Optional target name used for metadata and template tokens.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Target
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Token
Optional additional template tokens in name=value form.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Tokens
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ZipPath
Optional zip path token value.

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

- `None`

## RELATED LINKS

- None
