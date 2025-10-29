---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Initialize-PortableModule

## SYNOPSIS
Downloads and/or imports a module and its dependencies as a portable set.

## SYNTAX

```
Initialize-PortableModule [[-Name] <String>] [[-Path] <String>] [-Download] [-Import]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Assists in preparing a portable environment for a module by either downloading it (plus dependencies)
to a specified path, importing those modules from disk, or both.
Generates a convenience script that
imports all discovered module manifests when -Download is used.

## EXAMPLES

### EXAMPLE 1
```
Initialize-PortableModule -Name 'EFAdminManager' -Path 'C:\Portable' -Download
Saves the module and its dependencies into C:\Portable.
```

### EXAMPLE 2
```
Initialize-PortableModule -Name 'EFAdminManager' -Path 'C:\Portable' -Import
Imports the module and its dependencies from C:\Portable.
```

### EXAMPLE 3
```
Initialize-PortableModule -Name 'EFAdminManager' -Path 'C:\Portable' -Download -Import
Saves and then imports the module and dependencies, and creates a helper script.
```

## PARAMETERS

### -Name
Name of the module to download/import.
Alias: ModuleName.

```yaml
Type: String
Parameter Sets: (All)
Aliases: ModuleName

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Filesystem path where modules are saved or imported from.
Defaults to the current script root.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: $PSScriptRoot
Accept pipeline input: False
Accept wildcard characters: False
```

### -Download
Save the module and its dependencies to the specified path.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -Import
Import the module and its dependencies from the specified path.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
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

## RELATED LINKS
