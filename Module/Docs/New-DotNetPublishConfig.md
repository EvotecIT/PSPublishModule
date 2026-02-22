---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-DotNetPublishConfig
## SYNOPSIS
Scaffolds a starter powerforge.dotnetpublish.json configuration file.

## SYNTAX
### __AllParameterSets
```powershell
New-DotNetPublishConfig [-ProjectRoot <string>] [-ProjectPath <string>] [-TargetName <string>] [-Framework <string>] [-Runtimes <string[]>] [-Styles <DotNetPublishStyle[]>] [-Configuration <string>] [-OutputPath <string>] [-Force] [-NoSchema] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use this cmdlet for JSON-first onboarding of the DotNet publish engine.
The generated config can be executed with Invoke-DotNetPublish -ConfigPath ....

## EXAMPLES

### EXAMPLE 1
```powershell
New-DotNetPublishConfig -ProjectRoot '.' -PassThru
```

### EXAMPLE 2
```powershell
New-DotNetPublishConfig -Project '.\src\App\App.csproj' -Runtimes 'win-x64','win-arm64' -Styles PortableCompat,AotSpeed -Force
```

## PARAMETERS

### -Configuration
Build configuration (default: Release).

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

### -Force
Overwrite existing config file.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: Overwrite
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Framework
Optional framework override (for example net10.0).

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

### -NoSchema
Omits the $schema property from generated JSON.

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

### -OutputPath
Output config path (default: powerforge.dotnetpublish.json).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, ConfigPath
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Returns detailed scaffold metadata instead of only config path.

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

### -ProjectPath
Optional path to a specific project file. When omitted, the first matching .csproj is used.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Project
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectRoot
Project root used to resolve relative paths.

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
Optional runtime identifiers override.

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

### -Styles
Optional publish styles override.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TargetName
Optional target name override. Defaults to project file name.

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

- `System.String
PowerForge.DotNetPublishConfigScaffoldResult`

## RELATED LINKS

- None

