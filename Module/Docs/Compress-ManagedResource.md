---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Compress-ManagedResource
## SYNOPSIS
Compresses a managed PowerShell resource folder into a NuGet package.

## SYNTAX
### __AllParameterSets
```powershell
Compress-ManagedResource [-Path] <string> [-DestinationPath] <string> [-PassThru] [-SkipModuleManifestValidate] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This PSResourceGet-shaped surface currently packages PowerShell module folders through the managed C# pack service.
Script resources remain intentionally unsupported until the managed script metadata lane exists.

## EXAMPLES

### EXAMPLE 1
```powershell
Compress-ManagedResource -Path C:\Source\Company.Tools -DestinationPath C:\Packages -PassThru
```


## PARAMETERS

### -DestinationPath
Directory where the compressed .nupkg file is written.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: OutputDirectory, OutputPath
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Return the created package as a FileInfo object.

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

### -Path
Path to the module resource folder to compress.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipModuleManifestValidate
Skip managed module manifest metadata validation before package creation.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.IO.FileInfo`

## RELATED LINKS

- None
