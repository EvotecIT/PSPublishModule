---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Publish-AppleAppArchive
## SYNOPSIS
Uploads an Apple app .xcarchive to App Store Connect using xcodebuild exportArchive.

## SYNTAX
### __AllParameterSets
```powershell
Publish-AppleAppArchive [-ArchivePath] <string> [-TeamId <string>] [-ExportPath <string>] [-ExportOptionsPlistPath <string>] [-XcodeBuild <string>] [-SigningStyle <string>] [-ManageAppVersionAndBuildNumber] [-UploadSymbols] [-GenerateAppStoreInformation] [-AdditionalArgument <string[]>] [-TimeoutMinutes <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Uploads an Apple app .xcarchive to App Store Connect using xcodebuild exportArchive.

## EXAMPLES

### EXAMPLE 1
```powershell
Publish-AppleAppArchive -ArchivePath 'C:\Path'
```


## PARAMETERS

### -AdditionalArgument
Additional structured arguments appended to the export command.

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

### -ArchivePath
Path to the .xcarchive to upload.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -ExportOptionsPlistPath
Path to write the generated export options plist.

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

### -ExportPath
Temporary export path used by xcodebuild.

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

### -GenerateAppStoreInformation
Controls whether xcodebuild generates App Store information.

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

### -ManageAppVersionAndBuildNumber
Controls whether App Store Connect manages app version and build numbers during upload.

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

### -SigningStyle
Signing style passed to xcodebuild exportArchive.

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

### -TeamId
Apple developer team identifier.

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

### -TimeoutMinutes
Maximum upload runtime in minutes.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UploadSymbols
Controls whether debug symbols are uploaded.

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

### -XcodeBuild
xcodebuild executable name or path.

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

- `System.String`

## OUTPUTS

- `PowerForge.AppleAppArchiveUploadResult`

## RELATED LINKS

- None
