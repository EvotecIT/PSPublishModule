---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-DotNetPublish
## SYNOPSIS
Executes DotNet publish engine from DSL settings or an existing JSON config.

## SYNTAX
### Settings (Default)
```powershell
Invoke-DotNetPublish -Settings <scriptblock> [-Profile <string>] [-JsonOnly] [-JsonPath <string>] [-Plan] [-Validate] [-NoInteractive] [-ExitCode] [<CommonParameters>]
```

### Config
```powershell
Invoke-DotNetPublish -ConfigPath <string> [-Profile <string>] [-JsonOnly] [-JsonPath <string>] [-Plan] [-Validate] [-NoInteractive] [-ExitCode] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet follows the same authoring pattern as module build cmdlets:
create a config using New-ConfigurationDotNet* and run it directly,
or export config first via -JsonOnly + -JsonPath.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-DotNetPublish -JsonOnly -JsonPath '.\powerforge.dotnetpublish.json' -Settings {
New-ConfigurationDotNetPublish -IncludeSchema -ProjectRoot '.' -Configuration 'Release'
New-ConfigurationDotNetTarget -Name 'PowerForge.Cli' -ProjectPath 'PowerForge.Cli/PowerForge.Cli.csproj' -Framework 'net10.0' -Runtimes 'win-x64' -Style PortableCompat -Zip
}
```

### EXAMPLE 2
```powershell
Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -ExitCode
```

## PARAMETERS

### -ConfigPath
Path to existing DotNet publish JSON config.

```yaml
Type: String
Parameter Sets: Config
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExitCode
Sets host exit code: 0 on success, 1 on failure.

```yaml
Type: SwitchParameter
Parameter Sets: Settings, Config
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JsonOnly
Exports JSON config and exits without running the engine.

```yaml
Type: SwitchParameter
Parameter Sets: Settings, Config
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JsonPath
Output path for JSON config used with JsonOnly.

```yaml
Type: String
Parameter Sets: Settings, Config
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NoInteractive
Disables interactive output mode. Reserved for future UI parity.

```yaml
Type: SwitchParameter
Parameter Sets: Settings, Config
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Plan
Builds and emits resolved plan without executing steps.

```yaml
Type: SwitchParameter
Parameter Sets: Settings, Config
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Profile
Optional profile override.

```yaml
Type: String
Parameter Sets: Settings, Config
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Settings
DSL settings block that emits DotNet publish objects.

```yaml
Type: ScriptBlock
Parameter Sets: Settings
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Validate
Validates configuration by planning only; does not execute run steps.

```yaml
Type: SwitchParameter
Parameter Sets: Settings, Config
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

- `PowerForge.DotNetPublishPlan
PowerForge.DotNetPublishResult`

## RELATED LINKS

- None

