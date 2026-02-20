---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDocumentation
## SYNOPSIS
Enables or disables creation of documentation from the module using PowerForge.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDocumentation -Path <string> -PathReadme <string> [-Enable] [-StartClean] [-UpdateWhenNew] [-SyncExternalHelpToProjectRoot] [-SkipExternalHelp] [-SkipAboutTopics] [-SkipFallbackExamples] [-ExternalHelpCulture <string>] [-ExternalHelpFileName <string>] [-Tool <DocumentationTool>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet emits documentation configuration segments that are consumed by Invoke-ModuleBuild / Build-Module.
It controls markdown generation (in -Path), optional external help generation (MAML, e.g. en-US\<ModuleName>-help.xml),
and whether generated documentation should be synced back to the project root.

About topics are supported via about_*.help.txt / about_*.txt files present in the module source. When enabled,
these are converted into markdown pages under Docs\About.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDocumentation -Enable -UpdateWhenNew -StartClean -Path 'Docs' -PathReadme 'Docs\Readme.md' -SyncExternalHelpToProjectRoot
```

### EXAMPLE 2
```powershell
New-ConfigurationDocumentation -Enable -Path 'Docs' -PathReadme 'Docs\Readme.md' -SkipAboutTopics -SkipFallbackExamples
```

## PARAMETERS

### -Enable
Enables creation of documentation from the module.

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

### -ExternalHelpCulture
Culture folder for generated external help (default: en-US).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExternalHelpFileName
Optional file name override for external help (default: <ModuleName>-help.xml).

```yaml
Type: String
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
Path to the folder where documentation will be created.

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

### -PathReadme
Path to the readme file that will be used for the documentation.

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

### -SkipAboutTopics
Disable conversion of about_* topics into markdown pages.

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

### -SkipExternalHelp
Disable external help (MAML) generation.

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

### -SkipFallbackExamples
Disable generating basic fallback examples for cmdlets missing examples.

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

### -StartClean
Removes all files from the documentation folder before creating new documentation.

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

### -SyncExternalHelpToProjectRoot
When enabled and P:PSPublishModule.NewConfigurationDocumentationCommand.UpdateWhenNew is set, the generated external help file is also synced
back to the project root (e.g. en-US\<ModuleName>-help.xml).

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

### -Tool
Documentation engine (legacy parameter; kept for compatibility).

```yaml
Type: DocumentationTool
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: PlatyPS, HelpOut, PowerForge

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UpdateWhenNew
When enabled, generated documentation is also synced back to the project folder
(not only to the staging build output).

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

- `System.Object`

## RELATED LINKS

- None

