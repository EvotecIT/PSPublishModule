---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-PowerShellAssemblyMetadata
## SYNOPSIS
Gets the cmdlets and aliases in a .NET assembly by scanning for cmdlet-related attributes.

## SYNTAX
### __AllParameterSets
```powershell
Get-PowerShellAssemblyMetadata -Path <string> [<CommonParameters>]
```

## DESCRIPTION
This is typically used by module build tooling to determine which cmdlets and aliases should be exported
for binary modules (compiled cmdlets).

Under the hood it uses System.Reflection.MetadataLoadContext to inspect the assembly in isolation.
Make sure all dependencies of the target assembly are available next to it (or otherwise resolvable),
especially when running under Windows PowerShell 5.1.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-PowerShellAssemblyMetadata -Path '.\bin\Release\net8.0\MyModule.dll'
```

Returns discovered cmdlet and alias names based on PowerShell attributes.

### EXAMPLE 2
```powershell
PS>Get-PowerShellAssemblyMetadata -Path 'C:\Artifacts\MyModule\Bin\MyModule.dll'
```

Useful when validating what will be exported before publishing.

## PARAMETERS

### -Path
The assembly to inspect.

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

