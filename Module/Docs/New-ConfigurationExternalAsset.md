---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationExternalAsset
## SYNOPSIS
Adds an external asset bundle that is prepared before module staging.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationExternalAsset -Name <string> -OutputPath <string> -Files <ExternalAssetFileConfiguration[]> [-Version <string>] [-ManifestPath <string>] [-Source <string>] [-License <string>] [-SkipDownload] [-Disabled] [<CommonParameters>]
```

## DESCRIPTION
External asset bundles are for files that should be carried inside a module package but are not authored as normal
source files, such as offline installers, tooling archives, generated payloads, or mirrored third-party packages.
PowerForge downloads or copies the declared files before staging, computes SHA256 values, and writes a manifest
alongside the bundle.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationExternalAsset -Name VendorTool -OutputPath 'Artefacts\VendorTool' -Files @(New-ConfigurationExternalAssetFile -Runtime win-x64 -FileName tool.zip -Uri 'https://example.test/tool.zip')
```


## PARAMETERS

### -Disabled
When set, disables the bundle while keeping it in configuration.

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

### -Files
Files that make up the external asset bundle.

```yaml
Type: ExternalAssetFileConfiguration[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -License
Optional license expression or label written to the generated manifest.

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

### -ManifestPath
Optional manifest path. Relative paths resolve from the project root; when omitted, manifest.json is written under OutputPath.

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

### -Name
Friendly bundle name written to the generated manifest.

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
Output directory for the downloaded or copied files. Relative paths resolve from the project root.

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

### -SkipDownload
When set, existing files are used and missing files fail the build instead of downloading or copying sources.

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

### -Source
Optional source URI or project URL written to the generated manifest.

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

### -Version
Optional bundle version written to the generated manifest.

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

- `PowerForge.ConfigurationExternalAssetSegment`

## RELATED LINKS

- None
