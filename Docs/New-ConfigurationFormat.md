---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationFormat

## SYNOPSIS
Builds formatting options for code and manifest generation during the build.

## SYNTAX

```
New-ConfigurationFormat [-ApplyTo] <String[]> [-EnableFormatting] [[-Sort] <String>] [-RemoveComments]
 [-RemoveEmptyLines] [-RemoveAllEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock]
 [-PlaceOpenBraceEnable] [-PlaceOpenBraceOnSameLine] [-PlaceOpenBraceNewLineAfter]
 [-PlaceOpenBraceIgnoreOneLineBlock] [-PlaceCloseBraceEnable] [-PlaceCloseBraceNewLineAfter]
 [-PlaceCloseBraceIgnoreOneLineBlock] [-PlaceCloseBraceNoEmptyLineBefore] [-UseConsistentIndentationEnable]
 [[-UseConsistentIndentationKind] <String>] [[-UseConsistentIndentationPipelineIndentation] <String>]
 [[-UseConsistentIndentationIndentationSize] <Int32>] [-UseConsistentWhitespaceEnable]
 [-UseConsistentWhitespaceCheckInnerBrace] [-UseConsistentWhitespaceCheckOpenBrace]
 [-UseConsistentWhitespaceCheckOpenParen] [-UseConsistentWhitespaceCheckOperator]
 [-UseConsistentWhitespaceCheckPipe] [-UseConsistentWhitespaceCheckSeparator] [-AlignAssignmentStatementEnable]
 [-AlignAssignmentStatementCheckHashtable] [-UseCorrectCasingEnable] [[-PSD1Style] <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Produces a configuration object that controls how script and manifest files are formatted
during merge and in the default (non-merged) module.
You can toggle specific PSScriptAnalyzer
rules, whitespace/indentation behavior, comment removal, and choose PSD1 output style.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationFormat -ApplyTo 'OnMergePSD1','DefaultPSD1' -PSD1Style 'Minimal'
Minimizes PSD1 output during merge and default builds.
```

### EXAMPLE 2
```
New-ConfigurationFormat -ApplyTo 'OnMergePSM1' -EnableFormatting -UseConsistentIndentationEnable -UseConsistentIndentationKind space -UseConsistentIndentationIndentationSize 4
Enables indentation and whitespace rules for merged PSM1.
```

## PARAMETERS

### -ApplyTo
One or more targets to apply formatting to: OnMergePSM1, OnMergePSD1, DefaultPSM1, DefaultPSD1.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableFormatting
When set, enables formatting for the chosen ApplyTo targets even if no specific rule switches are provided.

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

### -Sort
Optional ordering hint for internal processing.
Accepts None, Asc, or Desc.

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

### -RemoveComments
Remove comments in the formatted output.

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

### -RemoveEmptyLines
Remove empty lines while preserving readability.

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

### -RemoveAllEmptyLines
Remove all empty lines (more aggressive than RemoveEmptyLines).

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

### -RemoveCommentsInParamBlock
Remove comments within the param() block.

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

### -RemoveCommentsBeforeParamBlock
Remove comments that appear immediately before the param() block.

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

### -PlaceOpenBraceEnable
Enable PSPlaceOpenBrace rule and configure its behavior.

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

### -PlaceOpenBraceOnSameLine
For PSPlaceOpenBrace: place opening brace on the same line.

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

### -PlaceOpenBraceNewLineAfter
For PSPlaceOpenBrace: enforce a new line after the opening brace.

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

### -PlaceOpenBraceIgnoreOneLineBlock
For PSPlaceOpenBrace: ignore single-line blocks.

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

### -PlaceCloseBraceEnable
Enable PSPlaceCloseBrace rule and configure its behavior.

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

### -PlaceCloseBraceNewLineAfter
For PSPlaceCloseBrace: enforce a new line after the closing brace.

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

### -PlaceCloseBraceIgnoreOneLineBlock
For PSPlaceCloseBrace: ignore single-line blocks.

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

### -PlaceCloseBraceNoEmptyLineBefore
For PSPlaceCloseBrace: do not allow an empty line before a closing brace.

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

### -UseConsistentIndentationEnable
Enable PSUseConsistentIndentation rule and configure its behavior.

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

### -UseConsistentIndentationKind
Indentation style: 'space' or 'tab'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationPipelineIndentation
Pipeline indentation mode: IncreaseIndentationAfterEveryPipeline or NoIndentation.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationIndentationSize
Number of spaces for indentation when Kind is 'space'.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: 0
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceEnable
Enable PSUseConsistentWhitespace rule and configure which elements to check.

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

### -UseConsistentWhitespaceCheckInnerBrace
For PSUseConsistentWhitespace: check inner brace spacing.

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

### -UseConsistentWhitespaceCheckOpenBrace
For PSUseConsistentWhitespace: check open brace spacing.

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

### -UseConsistentWhitespaceCheckOpenParen
For PSUseConsistentWhitespace: check open parenthesis spacing.

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

### -UseConsistentWhitespaceCheckOperator
For PSUseConsistentWhitespace: check operator spacing.

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

### -UseConsistentWhitespaceCheckPipe
For PSUseConsistentWhitespace: check pipeline operator spacing.

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

### -UseConsistentWhitespaceCheckSeparator
For PSUseConsistentWhitespace: check separator (comma) spacing.

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

### -AlignAssignmentStatementEnable
Enable PSAlignAssignmentStatement rule and optionally check hashtable alignment.

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

### -AlignAssignmentStatementCheckHashtable
For PSAlignAssignmentStatement: align hashtable assignments.

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

### -UseCorrectCasingEnable
Enable PSUseCorrectCasing rule.

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

### -PSD1Style
Style for generated manifests (PSD1) for the selected ApplyTo targets.
'Minimal' or 'Native'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
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
