---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationFormat
## SYNOPSIS
Builds formatting options for code and manifest generation during the build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationFormat -ApplyTo <string[]> [-EnableFormatting] [-Sort <string>] [-RemoveComments] [-RemoveEmptyLines] [-RemoveAllEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-UpdateProjectRoot] [-PlaceOpenBraceEnable] [-PlaceOpenBraceOnSameLine] [-PlaceOpenBraceNewLineAfter] [-PlaceOpenBraceIgnoreOneLineBlock] [-PlaceCloseBraceEnable] [-PlaceCloseBraceNewLineAfter] [-PlaceCloseBraceIgnoreOneLineBlock] [-PlaceCloseBraceNoEmptyLineBefore] [-UseConsistentIndentationEnable] [-UseConsistentIndentationKind <string>] [-UseConsistentIndentationPipelineIndentation <string>] [-UseConsistentIndentationIndentationSize <int>] [-UseConsistentWhitespaceEnable] [-UseConsistentWhitespaceCheckInnerBrace] [-UseConsistentWhitespaceCheckOpenBrace] [-UseConsistentWhitespaceCheckOpenParen] [-UseConsistentWhitespaceCheckOperator] [-UseConsistentWhitespaceCheckPipe] [-UseConsistentWhitespaceCheckSeparator] [-AlignAssignmentStatementEnable] [-AlignAssignmentStatementCheckHashtable] [-UseCorrectCasingEnable] [-PSD1Style <string>] [<CommonParameters>]
```

## DESCRIPTION
Produces a formatting configuration segment used by the build pipeline to normalize generated output
(merged PSM1/PSD1) and optionally apply formatting back to the project root.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> New-ConfigurationFormat -ApplyTo OnMergePSM1,OnMergePSD1 -RemoveComments -RemoveEmptyLines
```

Formats the merged module output and removes comments while keeping readability.

### EXAMPLE 2
```powershell
PS> New-ConfigurationFormat -ApplyTo DefaultPSM1,DefaultPSD1 -EnableFormatting -UpdateProjectRoot
```

Applies formatting rules to the project sources as well as generated output.

## PARAMETERS

### -AlignAssignmentStatementCheckHashtable
For PSAlignAssignmentStatement: align hashtable assignments.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AlignAssignmentStatementEnable
Enable PSAlignAssignmentStatement rule and optionally check hashtable alignment.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApplyTo
Targets to apply formatting to (OnMergePSM1, OnMergePSD1, DefaultPS1, DefaultPSM1, DefaultPSD1).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: OnMergePSM1, OnMergePSD1, DefaultPS1, DefaultPSM1, DefaultPSD1

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableFormatting
Enables formatting for the chosen ApplyTo targets even if no specific rule switches are provided.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceEnable
Enable PSPlaceCloseBrace rule and configure its behavior.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceIgnoreOneLineBlock
For PSPlaceCloseBrace: ignore single-line blocks.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceNewLineAfter
For PSPlaceCloseBrace: enforce a new line after the closing brace.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceNoEmptyLineBefore
For PSPlaceCloseBrace: do not allow an empty line before a closing brace.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceEnable
Enable PSPlaceOpenBrace rule and configure its behavior.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceIgnoreOneLineBlock
For PSPlaceOpenBrace: ignore single-line blocks.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceNewLineAfter
For PSPlaceOpenBrace: enforce a new line after the opening brace.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceOnSameLine
For PSPlaceOpenBrace: place opening brace on the same line.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PSD1Style
Style for generated manifests (PSD1) for the selected ApplyTo targets.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Minimal, Native

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveAllEmptyLines
Remove all empty lines (more aggressive than RemoveEmptyLines).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveComments
Remove comments in the formatted output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveCommentsBeforeParamBlock
Remove comments that appear immediately before the param() block.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveCommentsInParamBlock
Remove comments within the param() block.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveEmptyLines
Remove empty lines while preserving readability.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Sort
Optional ordering hint for internal processing. Accepts None, Asc, or Desc.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, Asc, Desc

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UpdateProjectRoot
When set, formats PowerShell sources in the project root in addition to staging output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationEnable
Enable PSUseConsistentIndentation rule and configure its behavior.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationIndentationSize
Number of spaces for indentation when Kind is space.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationKind
Indentation style for PSUseConsistentIndentation: space or tab.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: space, tab

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationPipelineIndentation
Pipeline indentation mode for PSUseConsistentIndentation.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: IncreaseIndentationAfterEveryPipeline, NoIndentation

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckInnerBrace
For PSUseConsistentWhitespace: check inner brace spacing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckOpenBrace
For PSUseConsistentWhitespace: check open brace spacing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckOpenParen
For PSUseConsistentWhitespace: check open parenthesis spacing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckOperator
For PSUseConsistentWhitespace: check operator spacing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckPipe
For PSUseConsistentWhitespace: check pipeline operator spacing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckSeparator
For PSUseConsistentWhitespace: check separator (comma) spacing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceEnable
Enable PSUseConsistentWhitespace rule and configure which elements to check.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseCorrectCasingEnable
Enable PSUseCorrectCasing rule.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None
