---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationExternalAssetFile
## SYNOPSIS
Creates a file entry for an external asset bundle.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationExternalAssetFile -Runtime <string> -FileName <string> -Uri <string> [-Architecture <string>] [-Path <string>] [-Sha256 <string>] [<CommonParameters>]
```

## DESCRIPTION
File entries are passed to New-ConfigurationExternalAsset. Each entry declares where the build pipeline
obtains a file from, where it lands inside the output folder, and which runtime or architecture metadata should be
written to the generated manifest.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationExternalAssetFile -Runtime netcore -Architecture x64 -FileName tool.zip -Uri 'https://example.test/tool.zip'
```


## PARAMETERS

### -Architecture
Optional architecture metadata written to the generated manifest.

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

### -FileName
Destination file name when Path is not specified.

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

### -Path
Destination path relative to the bundle output directory. Defaults to FileName.

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

### -Runtime
Runtime or payload group, such as netcore, netfx, linux-x64, or win-x64.

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

### -Sha256
Optional expected SHA256. When provided, mismatches fail the build.

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

### -Uri
HTTP(S) URI, file URI, rooted local path, or project-relative local path for this file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Url
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

- `PowerForge.ExternalAssetFileConfiguration`

## RELATED LINKS

- None
