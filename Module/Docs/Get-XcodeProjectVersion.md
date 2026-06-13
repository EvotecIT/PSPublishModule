---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-XcodeProjectVersion
## SYNOPSIS
Reads version information from an Xcode project.

## SYNTAX
### __AllParameterSets
```powershell
Get-XcodeProjectVersion [-Path] <string> [<CommonParameters>]
```

## DESCRIPTION
Reads MARKETING_VERSION and CURRENT_PROJECT_VERSION values from a
.xcodeproj directory or a raw project.pbxproj file.

The returned object includes all distinct values so release scripts can detect drift
across targets or configurations before uploading to App Store Connect.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Get-XcodeProjectVersion -Path .\Tactra.xcodeproj
```

Returns the distinct marketing and build version values from the project file.

## PARAMETERS

### -Path
Path to a .xcodeproj directory or project.pbxproj file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ProjectPath, FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String`

## OUTPUTS

- `PowerForge.XcodeProjectVersionInfo`

## RELATED LINKS

- None
