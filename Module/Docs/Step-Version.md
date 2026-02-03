---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Step-Version
## SYNOPSIS
Steps a version based on an expected version pattern (supports the legacy X placeholder).

## SYNTAX
### __AllParameterSets
```powershell
Step-Version -ExpectedVersion <string> [-Module <string>] [-Advanced] [-LocalPSD1 <string>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet supports two common workflows:

When -ExpectedVersion contains an X placeholder (e.g. 1.2.X),
the cmdlet resolves the next patch version. When an exact version is provided, it is returned as-is.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Step-Version -ExpectedVersion '1.0.X' -LocalPSD1 'C:\Git\MyModule\MyModule.psd1'
```

Reads the current version from the PSD1 and returns the next patch version.

### EXAMPLE 2
```powershell
PS>Step-Version -ExpectedVersion '1.0.X' -LocalPSD1 '.\MyModule.psd1' -Advanced
```

Returns a structured object that includes whether auto-versioning was used.

### EXAMPLE 3
```powershell
PS>Step-Version -ExpectedVersion '1.0.X' -Module 'MyModule'
```

Resolves the next patch version by looking up the current version of the module.

## PARAMETERS

### -Advanced
When set, returns a typed result instead of only the version string.

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

### -ExpectedVersion
Expected version (exact or pattern like 0.1.X).

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

### -LocalPSD1
Optional local PSD1 path used to resolve current version.

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

### -Module
Optional module name used to resolve current version from PSGallery.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.String
PowerForge.ModuleVersionStepResult`

## RELATED LINKS

- None

