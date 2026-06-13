---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationXcodeProjectVersion
## SYNOPSIS
Creates configuration for updating Xcode project version values during a build pipeline.

## SYNTAX
### ExplicitVersion (Default)
```powershell
New-ConfigurationXcodeProjectVersion [-Path] <string> -MarketingVersion <string> [-BuildNumber <string>] [-Disabled] [<CommonParameters>]
```

### ResolvedVersion
```powershell
New-ConfigurationXcodeProjectVersion [-Path] <string> -UseResolvedVersion [-BuildNumber <string>] [-Disabled] [<CommonParameters>]
```

## DESCRIPTION
Emits an Xcode project version configuration segment consumed by Invoke-ModuleBuild /
Build-Module. Use it when a release pipeline should keep Apple app project versions in
sync with the build recipe.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> New-ConfigurationXcodeProjectVersion -Path .\Tactra.xcodeproj -MarketingVersion 1.0.0 -BuildNumber 4
```

Updates MARKETING_VERSION and CURRENT_PROJECT_VERSION before the module build is staged.

### EXAMPLE 2
```powershell
PS> New-ConfigurationXcodeProjectVersion -Path .\Tactra.xcodeproj -UseResolvedVersion -BuildNumber 4
```

Uses the build pipeline's resolved version for MARKETING_VERSION.

## PARAMETERS

### -BuildNumber
Optional value to assign to all CURRENT_PROJECT_VERSION entries.

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

### -Path
Path to a .xcodeproj directory or project.pbxproj file.
Relative paths resolve from the pipeline project root.

```yaml
Type: String
Parameter Sets: ExplicitVersion, ResolvedVersion
Aliases: ProjectPath, FullName
Possible values:

Required: True
Position: 0
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
