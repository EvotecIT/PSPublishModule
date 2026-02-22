---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetPublish
## SYNOPSIS
Creates DotNet publish configuration using DSL objects from a settings script block.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetPublish [-Settings <scriptblock>] [-IncludeSchema] [-SchemaVersion <int>] [-Profile <string>] [-ProjectRoot <string>] [-SolutionPath <string>] [-Configuration <string>] [-Runtimes <string[]>] [-Restore <bool>] [-Clean <bool>] [-Build <bool>] [-NoRestoreInPublish <bool>] [-NoBuildInPublish <bool>] [-ManifestJsonPath <string>] [-ManifestTextPath <string>] [-ChecksumsPath <string>] [-RunReportPath <string>] [-Targets <DotNetPublishTarget[]>] [-Installers <DotNetPublishInstaller[]>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the DSL root for DotNet publish authoring in PSPublishModule.
It accepts optional global options and merges child objects emitted by -Settings such as:
New-ConfigurationDotNetTarget, New-ConfigurationDotNetInstaller, and New-ConfigurationDotNetSign.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetPublish -IncludeSchema -ProjectRoot '.' -Configuration 'Release' -Settings {
New-ConfigurationDotNetTarget -Name 'PowerForge.Cli' -ProjectPath 'PowerForge.Cli/PowerForge.Cli.csproj' -Framework 'net10.0' -Runtimes 'win-x64' -Style PortableCompat -Zip
}
```

## PARAMETERS

### -Build
Enables build step.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ChecksumsPath
Optional checksums output path.

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

### -Clean
Enables clean step.

```yaml
Type: Boolean
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
Build configuration used for build/publish.

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

### -IncludeSchema
When set, adds a relative schema reference to generated config.

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

### -Installers
Additional installers to append.

```yaml
Type: DotNetPublishInstaller[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ManifestJsonPath
Optional JSON manifest output path.

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

### -ManifestTextPath
Optional text manifest output path.

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

### -NoBuildInPublish
Uses --no-build during publish.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NoRestoreInPublish
Uses --no-restore during publish.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Profile
Optional active profile name.

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

### -ProjectRoot
Optional project root.

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

### -Restore
Enables restore step.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RunReportPath
Optional run report output path.

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

### -Runtimes
Default runtime identifiers.

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

### -SchemaVersion
Optional schema version value.

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

### -Settings
Optional settings script block that emits DotNet publish DSL objects.

```yaml
Type: ScriptBlock
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SolutionPath
Optional solution path used for restore/build/clean.

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

### -Targets
Additional targets to append.

```yaml
Type: DotNetPublishTarget[]
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

- `PowerForge.DotNetPublishSpec`

## RELATED LINKS

- None

