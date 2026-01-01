---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationManifest
## SYNOPSIS
Creates a configuration manifest for a PowerShell module.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationManifest -ModuleVersion <string> -Guid <string> -Author <string> [-CompatiblePSEditions <string[]>] [-CompanyName <string>] [-Copyright <string>] [-Description <string>] [-PowerShellVersion <string>] [-Tags <string[]>] [-IconUri <string>] [-ProjectUri <string>] [-DotNetFrameworkVersion <string>] [-LicenseUri <string>] [-RequireLicenseAcceptance] [-Prerelease <string>] [-FunctionsToExport <string[]>] [-CmdletsToExport <string[]>] [-AliasesToExport <string[]>] [-FormatsToProcess <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Emits a manifest configuration segment that is later applied to the module .psd1 during a build.
Use this to define identity and metadata (version, GUID, author, tags, links) in a build script / JSON pipeline.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationManifest -ModuleVersion '1.0.0' -Guid 'eb76426a-1992-40a5-82cd-6480f883ef4d' -Author 'YourName'
```

Defines the core identity fields required for a module manifest.

### EXAMPLE 2
```powershell
PS>New-ConfigurationManifest -ModuleVersion '1.0.X' -Guid 'eb76426a-1992-40a5-82cd-6480f883ef4d' -Author 'YourName' -Tags 'PowerShell','Build' -ProjectUri 'https://github.com/YourOrg/YourRepo' -LicenseUri 'https://opensource.org/licenses/MIT'
```

Populates common PSGallery metadata that shows up on the gallery and in generated docs.

## PARAMETERS

### -AliasesToExport
Overrides aliases to export in the module manifest.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Author
Identifies the module author.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CmdletsToExport
Overrides cmdlets to export in the module manifest.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CompanyName
Identifies the company or vendor who created the module.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CompatiblePSEditions
Specifies the module's compatible PowerShell editions.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Copyright
Specifies a copyright statement for the module.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Description
Describes the module at a high level.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotNetFrameworkVersion
Specifies the minimum version of the Microsoft .NET Framework that the module requires.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FormatsToProcess
Specifies formatting files (.ps1xml) that run when the module is imported.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FunctionsToExport
Overrides functions to export in the module manifest.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Guid
Specifies a unique identifier for the module.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IconUri
Specifies the URI for the module's icon.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LicenseUri
Specifies the URI for the module's license.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleVersion
Specifies the version of the module.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PowerShellVersion
Specifies the minimum version of PowerShell this module requires.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Prerelease
Specifies the prerelease tag for the module.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: PrereleaseTag

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectUri
Specifies the URI for the module's project page.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequireLicenseAcceptance
When set, indicates the module requires explicit user license acceptance (PowerShellGet).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Tags
Specifies tags for the module.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

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

- `System.Object`

## RELATED LINKS

- None

