---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Publish-AppleAppToDevice
## SYNOPSIS
Builds, installs, and optionally launches an Apple app on a physical device.

## SYNTAX
### __AllParameterSets
```powershell
Publish-AppleAppToDevice [-ProjectPath] <string> -Scheme <string> [-Workspace] [-ProductName <string>] [-Configuration <string>] [-Platform <ApplePlatform>] [-Destination <string>] [-DeviceIdentifier <string>] [-Device <string>] [-BundleIdentifier <string>] [-Launch] [-DerivedDataPath <string>] [-AppPath <string>] [-XcodeBuild <string>] [-Xcrun <string>] [-AllowProvisioningUpdates] [-UseBuildMirror] [-BuildRoot <string>] [-BuildMirrorPath <string>] [-Rsync <string>] [-AdditionalArgument <string[]>] [-TimeoutMinutes <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Builds, installs, and optionally launches an Apple app on a physical device.

## EXAMPLES

### EXAMPLE 1
```powershell
Publish-AppleAppToDevice -Scheme 'Value'
```


## PARAMETERS

### -AdditionalArgument
Additional structured arguments appended to the xcodebuild build command.

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
Allows Xcode to create or update signing assets during build.

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

### -AppPath
Expected app path.

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

### -BuildMirrorPath
Mirror directory used when UseBuildMirror is enabled.

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

### -BuildRoot
Root directory to mirror. Defaults to the project/workspace parent.

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

### -BundleIdentifier
Bundle identifier used when launching after install.

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

### -DerivedDataPath
DerivedData path.

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

### -Device
Device name, identifier, or model used when DeviceIdentifier is not supplied.

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

### -DeviceIdentifier
Physical device identifier used for deployment.

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

### -Launch
Launch the app after a successful install.

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

### -Platform
Apple platform used to resolve the product directory.

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

### -ProductName
Built product name. Defaults to Scheme.

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

### -Rsync
rsync executable name or path used for build mirroring.

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

### -Scheme
Xcode scheme to build.

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
Maximum runtime per stage in minutes.

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

### -UseBuildMirror
Mirror the project root to a local folder before running xcodebuild.

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

### -Xcrun
xcrun executable name or path.

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

- `PowerForge.AppleAppDeviceDeploymentResult`

## RELATED LINKS

- None
