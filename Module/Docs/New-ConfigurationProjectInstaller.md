---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectInstaller
## SYNOPSIS
Creates an installer entry for a PowerShell-authored project build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectInstaller -Id <string> -Target <string> -InstallerProjectPath <string> [-FromPublishOutput] [-Harvest <DotNetPublishMsiHarvestMode>] [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-HarvestDirectoryRefId <string>] [-MsBuildProperty <hashtable>] [<CommonParameters>]
```

## DESCRIPTION
Creates an installer entry for a PowerShell-authored project build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectInstaller -Id 'Value' -Target 'Value' -InstallerProjectPath 'C:\Path'
```


## PARAMETERS

### -Frameworks
Optional framework filter.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Framework
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FromPublishOutput
When set, prepares from the raw publish output instead of an auto-generated portable bundle.

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

### -Harvest
Harvest mode used during MSI prepare.

```yaml
Type: DotNetPublishMsiHarvestMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, Auto

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -HarvestDirectoryRefId
Optional WiX DirectoryRef identifier for generated harvest output.

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

### -Id
Installer identifier.

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

### -InstallerProjectPath
Path to the installer project file.

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

### -MsBuildProperty
Optional installer-specific MSBuild properties.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Runtimes
Optional runtime filter.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Runtime
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Styles
Optional style filter.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, FrameworkDependent, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Target
Name of the source target.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ConfigurationProjectInstaller`

## RELATED LINKS

- None

