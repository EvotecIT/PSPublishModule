---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-PowerShellAssemblyMetadata

## SYNOPSIS
Gets the cmdlets and aliases in a dotnet assembly.

## SYNTAX

```
Get-PowerShellAssemblyMetadata [-Path] <String> [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
{{ Fill in the Description }}

## EXAMPLES

### EXAMPLE 1
```
Get-PowerShellAssemblyMetadata -Path MyModule.dll
```

## PARAMETERS

### -Path
The assembly to inspect.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
This requires the System.Reflection.MetadataLoadContext assembly to be
loaded through Add-Type.
WinPS (5.1) will also need to load its deps
    System.Memory
    System.Collections.Immutable
    System.Reflection.Metadata
    System.Runtime.CompilerServices.Unsafe

https://www.nuget.org/packages/System.Reflection.MetadataLoadContext

Copyright: (c) 2024, Jordan Borean (@jborean93) \<jborean93@gmail.com\>
MIT License (see LICENSE or https://opensource.org/licenses/MIT)

## RELATED LINKS
