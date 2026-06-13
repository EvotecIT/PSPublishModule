---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-XcodeProjectVersion
## SYNOPSIS
Updates version information in an Xcode project.

## SYNTAX
### __AllParameterSets
```powershell
Set-XcodeProjectVersion [-Path] <string> -MarketingVersion <string> [-BuildNumber <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Updates all MARKETING_VERSION values in a .xcodeproj directory or
raw project.pbxproj file. When -BuildNumber is provided, it also
updates all CURRENT_PROJECT_VERSION values.

This command intentionally edits local Xcode project metadata only. App Store
Connect metadata and build selection belong to higher-level Apple release commands.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Set-XcodeProjectVersion -Path .\Tactra.xcodeproj -MarketingVersion 1.0.0 -BuildNumber 4 -PassThru
```

Updates all matching version assignments and returns the before/after summary.

## PARAMETERS

### -BuildNumber
Optional value to assign to all CURRENT_PROJECT_VERSION entries.

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

### -MarketingVersion
The value to assign to all MARKETING_VERSION entries.

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

### -PassThru
Returns the before/after update result.

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

- `PowerForge.XcodeProjectVersionUpdateResult`

## RELATED LINKS

- None
