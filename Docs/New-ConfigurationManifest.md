---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationManifest

## SYNOPSIS
Creates a new configuration manifest for a PowerShell module.

## SYNTAX

```
New-ConfigurationManifest [-ModuleVersion] <String> [[-CompatiblePSEditions] <String[]>] [-GUID] <String>
 [-Author] <String> [[-CompanyName] <String>] [[-Copyright] <String>] [[-Description] <String>]
 [[-PowerShellVersion] <String>] [[-Tags] <String[]>] [[-IconUri] <String>] [[-ProjectUri] <String>]
 [[-DotNetFrameworkVersion] <String>] [[-LicenseUri] <String>] [[-Prerelease] <String>]
 [[-FunctionsToExport] <String[]>] [[-CmdletsToExport] <String[]>] [[-AliasesToExport] <String[]>]
 [[-FormatsToProcess] <String[]>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
This function generates a new configuration manifest for a PowerShell module.
The manifest includes metadata about the module such as version, author, company, and other relevant information.
It also allows specifying the functions, cmdlets, and aliases to export.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationManifest -ModuleVersion '1.0.0' -GUID '12345678-1234-1234-1234-1234567890ab' -Author 'John Doe' -CompanyName 'Example Corp' -Description 'This is an example module.'
```

## PARAMETERS

### -ModuleVersion
Specifies the version of the module.
When multiple versions of a module exist on a system, the latest version is loaded by default when you run Import-Module.

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
Specifies the module's compatible PowerShell editions.
Valid values are 'Desktop' and 'Core'.

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
Specifies a unique identifier for the module.
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
Identifies the module author.

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
Identifies the company or vendor who created the module.

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
Specifies a copyright statement for the module.

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
Describes the module at a high level.

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
Specifies the minimum version of PowerShell this module requires.
Default is '5.1'.

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
Specifies tags for the module.

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
Specifies the URI for the module's icon.

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
Specifies the URI for the module's project page.

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
Specifies the minimum version of the Microsoft .NET Framework that the module requires.

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
Specifies the URI for the module's license.

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
Specifies the prerelease tag for the module.

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
Defines functions to export in the module manifest.
By default, functions are auto-detected, but this allows you to override that.

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
Defines cmdlets to export in the module manifest.
By default, cmdlets are auto-detected, but this allows you to override that.

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
Defines aliases to export in the module manifest.
By default, aliases are auto-detected, but this allows you to override that.

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

### -FormatsToProcess
Specifies formatting files (.ps1xml) that run when the module is imported.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 18
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
This function helps in creating a standardized module manifest for PowerShell modules.

## RELATED LINKS
