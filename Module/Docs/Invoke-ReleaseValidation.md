---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ReleaseValidation
## SYNOPSIS
Validates final artifacts and runs isolated product smoke tests without publishing.

## SYNTAX
### Config (Default)
```powershell
Invoke-ReleaseValidation -ConfigPath <string> [-ProjectRoot <string>] [-Version <string>] [-Variables <hashtable>] [-JsonOnly] [-JsonPath <string>] [<CommonParameters>]
```

### Configuration
```powershell
Invoke-ReleaseValidation -Configuration <ReleaseValidationSpec> [-ProjectRoot <string>] [-Version <string>] [-Variables <hashtable>] [-JsonOnly] [-JsonPath <string>] [<CommonParameters>]
```

### Settings
```powershell
Invoke-ReleaseValidation -Settings <scriptblock> [-ProjectRoot <string>] [-Version <string>] [-Variables <hashtable>] [-JsonOnly] [-JsonPath <string>] [<CommonParameters>]
```

## DESCRIPTION
Configuration can come from JSON, a typed object, or a settings block using New-ConfigurationReleaseValidation.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-ReleaseValidation -ConfigPath './Build/validation.json' -Variables @{ PackageRoot = './Artifacts/packages' }
```


### EXAMPLE 2
```powershell
Invoke-ReleaseValidation -JsonOnly -Settings { New-ConfigurationReleaseValidation -Commands @{ Name = 'CLI help'; FileName = 'example'; Arguments = @('--help') } }
```


## PARAMETERS

### -ConfigPath
Path to a release-validation JSON file.

```yaml
Type: String
Parameter Sets: Config
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Configuration
Typed validation configuration.

```yaml
Type: ReleaseValidationSpec
Parameter Sets: Configuration
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -JsonOnly
Return JSON without executing validation.

```yaml
Type: SwitchParameter
Parameter Sets: Config, Configuration, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -JsonPath
Optional configuration export path. Relative paths use the current PowerShell directory.

```yaml
Type: String
Parameter Sets: Config, Configuration, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectRoot
Optional project-root override.

```yaml
Type: String
Parameter Sets: Config, Configuration, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Settings
Block that emits one New-ConfigurationReleaseValidation object.

```yaml
Type: ScriptBlock
Parameter Sets: Settings
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Variables
Named path and value substitutions used by the configuration.

```yaml
Type: Hashtable
Parameter Sets: Config, Configuration, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Version
Expected artifact version; otherwise inferred from the package or module.

```yaml
Type: String
Parameter Sets: Config, Configuration, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PowerForge.ReleaseValidationSpec`

## OUTPUTS

- `PowerForge.ReleaseValidationReport`
- `System.String`

## RELATED LINKS

- None
