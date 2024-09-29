---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationManifest

## SYNOPSIS
Short description

## SYNTAX

```
New-ConfigurationManifest [-ModuleVersion] <String> [[-CompatiblePSEditions] <String[]>] [-GUID] <String>
 [-Author] <String> [[-CompanyName] <String>] [[-Copyright] <String>] [[-Description] <String>]
 [[-PowerShellVersion] <String>] [[-Tags] <String[]>] [[-IconUri] <String>] [[-ProjectUri] <String>]
 [[-DotNetFrameworkVersion] <String>] [[-LicenseUri] <String>] [[-Prerelease] <String>]
 [[-FunctionsToExport] <String[]>] [[-CmdletsToExport] <String[]>] [[-AliasesToExport] <String[]>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Long description

## EXAMPLES

### EXAMPLE 1
```
An example
```

## PARAMETERS

### -ModuleVersion
This setting specifies the version of the module.
When multiple versions of a module exist on a system, the latest version is loaded by default when you run Import-Module

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CompatiblePSEditions
This setting specifies the module's compatible PSEditions.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: @('Desktop', 'Core')
Accept pipeline input: False
Accept wildcard characters: False
```

### -GUID
This setting specifies a unique identifier for the module.
The GUID is used to distinguish between modules with the same name.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Author
This setting identifies the module author.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CompanyName
This setting identifies the company or vendor who created the module.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Copyright
This setting specifies a copyright statement for the module.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Description
This setting describes the module at a high level.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 7
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PowerShellVersion
This setting specifies the minimum version of PowerShell this module requires.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 8
Default value: 5.1
Accept pipeline input: False
Accept wildcard characters: False
```

### -Tags
Parameter description

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 9
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IconUri
Parameter description

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 10
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectUri
Parameter description

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 11
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DotNetFrameworkVersion
This setting specifies the minimum version of the Microsoft .NET Framework that the module requires.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 12
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LicenseUri
Parameter description

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 13
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Prerelease
Parameter description

```yaml
Type: String
Parameter Sets: (All)
Aliases: PrereleaseTag

Required: False
Position: 14
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FunctionsToExport
Allows ability to define functions to export in the module manifest.
By default functions are auto-detected, but this allows you to override that.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 15
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CmdletsToExport
Allows ability to define commands to export in the module manifest.
Currently module is not able to auto-detect commands, so you can use it to define, or module will use wildcard if it detects binary module.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 16
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AliasesToExport
Allows ability to define aliases to export in the module manifest.
By default aliases are auto-detected, but this allows you to override that.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 17
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS
