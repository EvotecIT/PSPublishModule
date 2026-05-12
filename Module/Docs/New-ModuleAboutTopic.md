---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ModuleAboutTopic
## SYNOPSIS
Creates an about_*.help.txt template source file for module documentation.

## SYNTAX
### __AllParameterSets
```powershell
New-ModuleAboutTopic [-TopicName] <string> [-OutputPath <string>] [-ShortDescription <string>] [-Format <AboutTopicTemplateFormat>] [-Force] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use this cmdlet to scaffold about topic source files that are later converted by
Invoke-ModuleBuild documentation generation into markdown pages under Docs\About.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About'
```


### EXAMPLE 2
```powershell
New-ModuleAboutTopic -TopicName 'about_Configuration' -OutputPath '.\Help\About' -Force
```


### EXAMPLE 3
```powershell
New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About' -Format Markdown
```


## PARAMETERS

### -Force
Overwrite existing file if it already exists.

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

### -Format
Output format for the scaffolded about topic file.

```yaml
Type: AboutTopicTemplateFormat
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: HelpText, Markdown

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Output directory for the source file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Returns the created file path.

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

### -ShortDescription
Optional short description seed for the generated template.

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

### -TopicName
Topic name. The about_ prefix is added automatically when missing.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None

