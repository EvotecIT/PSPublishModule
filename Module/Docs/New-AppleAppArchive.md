---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-AppleAppArchive
## SYNOPSIS
Creates an Apple app .xcarchive using xcodebuild.

## SYNTAX
### __AllParameterSets
```powershell
New-AppleAppArchive [-ProjectPath] <string> -Scheme <string> [-Workspace] [-Configuration <string>] [-Platform <ApplePlatform>] [-Destination <string>] [-ArchivePath <string>] [-ArchiveRoot <string>] [-XcodeBuild <string>] [-AllowProvisioningUpdates] [-AdditionalArgument <string[]>] [-TimeoutMinutes <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Creates an Apple app .xcarchive using xcodebuild.

## EXAMPLES

### EXAMPLE 1
```powershell
New-AppleAppArchive -Scheme 'Value'
```


## PARAMETERS

### -AdditionalArgument
Additional structured arguments appended to the archive command.

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

### -AllowProvisioningUpdates
Allows Xcode to create or update signing assets during archive.

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

### -ArchivePath
Output .xcarchive path.

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

### -ArchiveRoot
Directory used for generated archive paths.

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

### -Configuration
Build configuration.

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

### -Destination
Explicit xcodebuild destination.

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

### -Platform
Apple platform used to resolve the generic destination.

```yaml
Type: ApplePlatform
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: iOS, iPadOS, macOS, tvOS, watchOS, visionOS

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectPath
Path to the Xcode project or workspace.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scheme
Xcode scheme to archive.

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

### -TimeoutMinutes
Maximum archive runtime in minutes.

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

### -Workspace
ProjectPath points to a workspace instead of a project.

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

- `None`

## OUTPUTS

- `PowerForge.AppleAppArchiveResult`

## RELATED LINKS

- None
