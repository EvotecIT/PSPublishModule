---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleInformation
## SYNOPSIS
Gets module manifest information from a project directory.

## SYNTAX
### __AllParameterSets
```powershell
Get-ModuleInformation -Path <string> [<CommonParameters>]
```

## DESCRIPTION
This is a lightweight helper used by build/publish commands.
It finds the module manifest (*.psd1) under -Path and returns a structured object
containing common fields such as module name, version, required modules, and the manifest path.

Use it in build scripts to avoid re-implementing manifest discovery logic.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-ModuleInformation -Path 'C:\Git\MyModule\Module'
```

Returns the parsed manifest and convenience properties such as module name and version.

### EXAMPLE 2
```powershell
PS>$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot; $moduleInfo.ManifestPath
```

Loads the manifest from the folder where the build script resides.

## PARAMETERS

### -Path
The path to the directory containing the module manifest file.

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

- `System.Object`

## RELATED LINKS

- None

