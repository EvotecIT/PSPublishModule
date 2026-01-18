---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationModule
## SYNOPSIS
Provides a way to configure required, external, or approved modules used in the project.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationModule -Name <string[]> [-Type <ModuleDependencyKind>] [-Version <string>] [-MinimumVersion <string>] [-RequiredVersion <string>] [-Guid <string>] [<CommonParameters>]
```

## DESCRIPTION
Emits module dependency configuration segments. These are later used to patch the module manifest and (optionally)
install/package dependencies during a build.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version '5.6.1'
```

Declares a required dependency that is written into the manifest.

### EXAMPLE 2
```powershell
PS>New-ConfigurationModule -Type ExternalModule -Name 'Az.Accounts' -Version 'Latest'
```

Declares a dependency that is expected to be installed separately (not bundled into artefacts).

### EXAMPLE 3
```powershell
PS>New-ConfigurationModule -Type RequiredModule -Name 'PSWriteColor' -RequiredVersion '1.0.0'
```

Uses RequiredVersion when an exact match is required.

## PARAMETERS

### -Guid
GUID of the dependency module (or 'Auto').

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

### -MinimumVersion
Minimum version of the dependency module (preferred over -Version).

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

### -Name
Name of the PowerShell module(s) that your module depends on.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Required version of the dependency module (exact match).

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

### -Type
Choose between RequiredModule, ExternalModule and ApprovedModule.

```yaml
Type: ModuleDependencyKind
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Version
Minimum version of the dependency module (or 'Latest').

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

- `System.Object`

## RELATED LINKS

- None

