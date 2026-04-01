---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectOutput
## SYNOPSIS
Creates output-root and staging defaults for a PowerShell-authored project build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectOutput [-OutputRoot <string>] [-StageRoot <string>] [-ManifestJsonPath <string>] [-ChecksumsPath <string>] [-NoChecksums] [<CommonParameters>]
```

## DESCRIPTION
Creates output-root and staging defaults for a PowerShell-authored project build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectOutput -ChecksumsPath 'C:\Path'
```

## PARAMETERS

### -ChecksumsPath
Optional unified release checksums path.

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

### -ManifestJsonPath
Optional unified release manifest path.

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

### -NoChecksums
Disables top-level release checksum generation.

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

### -OutputRoot
Optional DotNetPublish output-root override.

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

### -StageRoot
Optional unified release staging root.

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

- `PowerForge.ConfigurationProjectOutput`

## RELATED LINKS

- None

