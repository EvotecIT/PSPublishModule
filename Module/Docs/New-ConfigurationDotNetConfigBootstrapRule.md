---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetConfigBootstrapRule
## SYNOPSIS
Creates config bootstrap copy rules for DotNet publish service packages.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetConfigBootstrapRule -SourcePath <string> -DestinationPath <string> [-Overwrite] [-OnMissingSource <DotNetPublishPolicyMode>] [<CommonParameters>]
```

## DESCRIPTION
Creates config bootstrap copy rules for DotNet publish service packages.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetConfigBootstrapRule -SourcePath 'appsettings.example.json' -DestinationPath 'appsettings.json'
```


## PARAMETERS

### -DestinationPath
Destination file path relative to output.

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

### -OnMissingSource
Policy when source file is missing.

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

### -Overwrite
Allows overwriting existing destination file.

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

### -SourcePath
Source file path relative to output.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishConfigBootstrapRule`

## RELATED LINKS

- None

