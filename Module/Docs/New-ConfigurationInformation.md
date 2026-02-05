---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationInformation
## SYNOPSIS
Describes what to include/exclude in the module build and how libraries are organized.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationInformation [-FunctionsToExportFolder <string>] [-AliasesToExportFolder <string>] [-ExcludeFromPackage <string[]>] [-IncludeRoot <string[]>] [-IncludePS1 <string[]>] [-IncludeAll <string[]>] [-IncludeCustomCode <scriptblock>] [-IncludeToArray <IncludeToArrayEntry[]>] [-LibrariesCore <string>] [-LibrariesDefault <string>] [-LibrariesStandard <string>] [<CommonParameters>]
```

## DESCRIPTION
This configuration segment controls:

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationInformation -ExcludeFromPackage 'Ignore','Examples','Docs' -IncludeRoot '*.psd1','*.psm1','LICENSE' -IncludeAll 'Bin','Lib','en-US'
```

Controls what ends up in packaged artefacts while keeping staging lean.

### EXAMPLE 2
```powershell
PS>New-ConfigurationInformation -IncludeCustomCode { Copy-Item -Path '.\Extras\*' -Destination $StagingPath -Recurse -Force }
```

Injects additional content into the staging folder before packaging.

## PARAMETERS

### -AliasesToExportFolder
Folder name containing public aliases to export (e.g., 'Public').

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

### -ExcludeFromPackage
Paths or patterns to exclude from artefacts (e.g., 'Ignore','Docs','Examples').

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FunctionsToExportFolder
Folder name containing public functions to export (e.g., 'Public').

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

### -IncludeAll
Folder names to include entirely.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeCustomCode
Scriptblock executed during staging to add custom files/folders.

```yaml
Type: ScriptBlock
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludePS1
Folder names where PS1 files should be included.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeRoot
File patterns from the root to include (e.g., '*.psm1','*.psd1','License*').

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeToArray
Advanced include rules. Accepts legacy hashtable (Key=>Values) or T:PowerForge.IncludeToArrayEntry[].

```yaml
Type: IncludeToArrayEntry[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LibrariesCore
Relative path to libraries compiled for Core (default 'Lib/Core').

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

### -LibrariesDefault
Relative path to libraries for classic .NET (default 'Lib/Default').

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

### -LibrariesStandard
Relative path to libraries for .NET Standard (default 'Lib/Standard').

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

