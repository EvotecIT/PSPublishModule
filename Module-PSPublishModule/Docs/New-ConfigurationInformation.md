---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationInformation

## SYNOPSIS
Describes what to include/exclude in the module build and how libraries are organized.

## SYNTAX

```
New-ConfigurationInformation [[-FunctionsToExportFolder] <String>] [[-AliasesToExportFolder] <String>]
 [[-ExcludeFromPackage] <String[]>] [[-IncludeRoot] <String[]>] [[-IncludePS1] <String[]>]
 [[-IncludeAll] <String[]>] [[-IncludeCustomCode] <ScriptBlock>] [[-IncludeToArray] <IDictionary>]
 [[-LibrariesCore] <String>] [[-LibrariesDefault] <String>] [[-LibrariesStandard] <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Emits a configuration block with folder-level include/exclude rules and optional library
locations that the builder uses to stage content prior to merge/packaging.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationInformation -IncludeAll 'Internals\' -IncludePS1 'Private','Public' -ExcludeFromPackage 'Ignore','Docs'
```

## PARAMETERS

### -FunctionsToExportFolder
Folder name containing public functions to export (e.g., 'Public').

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AliasesToExportFolder
Folder name containing public aliases to export (e.g., 'Public').

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeFromPackage
Paths or patterns to exclude from artefacts (e.g., 'Ignore','Docs','Examples').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRoot
File patterns from the root to include (e.g., '*.psm1','*.psd1','License*').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludePS1
Folder names where PS1 files should be included (e.g., 'Private','Public','Enums','Classes').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeAll
Folder names to include entirely (e.g., 'Images','Resources','Templates','Bin','Lib','Data').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeCustomCode
Scriptblock executed during staging to add custom files/folders.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: False
Position: 7
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeToArray
Advanced form to pass IncludeRoot/IncludePS1/IncludeAll as a single hashtable.

```yaml
Type: IDictionary
Parameter Sets: (All)
Aliases:

Required: False
Position: 8
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesCore
Relative path to libraries compiled for Core (default 'Lib/Core').

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 9
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesDefault
Relative path to libraries for classic .NET (default 'Lib/Default').

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 10
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesStandard
Relative path to libraries for .NET Standard (default 'Lib/Standard').

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 11
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

## RELATED LINKS
