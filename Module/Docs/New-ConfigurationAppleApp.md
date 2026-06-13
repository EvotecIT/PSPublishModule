---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationAppleApp
## SYNOPSIS
Creates configuration for preparing an Apple app target in a release pipeline.

## SYNTAX
### ExplicitVersion (Default)
```powershell
New-ConfigurationAppleApp [-ProjectPath] <string> -MarketingVersion <string> [-Name <string>] [-BundleId <string>] [-Platform <ApplePlatform>] [-Scheme <string>] [-AppStoreConnectAppId <string>] [-BuildNumber <string>] [-BuildNumberPolicy <AppleBuildNumberPolicy>] [-Disabled] [<CommonParameters>]
```

### ResolvedVersion
```powershell
New-ConfigurationAppleApp [-ProjectPath] <string> -UseResolvedVersion [-Name <string>] [-BundleId <string>] [-Platform <ApplePlatform>] [-Scheme <string>] [-AppStoreConnectAppId <string>] [-BuildNumber <string>] [-BuildNumberPolicy <AppleBuildNumberPolicy>] [-Disabled] [<CommonParameters>]
```

## DESCRIPTION
Emits an Apple app configuration segment consumed by Invoke-ModuleBuild / Build-Module.
The segment prepares local Xcode project version metadata. App Store Connect metadata is kept as
configuration for future read-only checks and publish/review commands.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> New-ConfigurationAppleApp -Name Tactra -Platform iOS -ProjectPath .\Tactra.xcodeproj -Scheme Tactra -BundleId com.example.Tactra -UseResolvedVersion -BuildNumberPolicy IncrementExisting
```

Sets MARKETING_VERSION from the pipeline version and increments CURRENT_PROJECT_VERSION.

## PARAMETERS

### -AppStoreConnectAppId
Optional App Store Connect app id for future remote metadata checks.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BuildNumber
Optional explicit build number.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BuildNumberPolicy
Build number policy used when preparing the local Xcode project.

```yaml
Type: AppleBuildNumberPolicy
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values: Explicit, KeepExisting, IncrementExisting

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BundleId
Bundle identifier, e.g. com.example.Tactra.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Disabled
Disable this configuration entry without removing it from a build script.

```yaml
Type: SwitchParameter
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MarketingVersion
The value to assign to all MARKETING_VERSION entries.

```yaml
Type: String
Parameter Sets: ExplicitVersion
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Friendly app name used in logs and reports.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Platform
Apple platform for this app target.

```yaml
Type: ApplePlatform
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values: iOS, iPadOS, macOS, tvOS, watchOS, visionOS

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectPath
Path to a .xcodeproj directory or project.pbxproj file.
Relative paths resolve from the pipeline project root.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: Path, FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scheme
Xcode scheme name for future archive/export automation.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseResolvedVersion
Uses the pipeline resolved version as the MARKETING_VERSION value.

```yaml
Type: SwitchParameter
Parameter Sets: ResolvedVersion
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

- `System.Object`

## RELATED LINKS

- None
