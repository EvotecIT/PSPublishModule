---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationFormat

## SYNOPSIS
{{ Fill in the Synopsis }}

## SYNTAX

```
New-ConfigurationFormat [-ApplyTo] <String[]> [-EnableFormatting] [[-Sort] <String>] [-RemoveComments]
 [-PlaceOpenBraceEnable] [-PlaceOpenBraceOnSameLine] [-PlaceOpenBraceNewLineAfter]
 [-PlaceOpenBraceIgnoreOneLineBlock] [-PlaceCloseBraceEnable] [-PlaceCloseBraceNewLineAfter]
 [-PlaceCloseBraceIgnoreOneLineBlock] [-PlaceCloseBraceNoEmptyLineBefore] [-UseConsistentIndentationEnable]
 [[-UseConsistentIndentationKind] <String>] [[-UseConsistentIndentationPipelineIndentation] <String>]
 [[-UseConsistentIndentationIndentationSize] <Int32>] [-UseConsistentWhitespaceEnable]
 [-UseConsistentWhitespaceCheckInnerBrace] [-UseConsistentWhitespaceCheckOpenBrace]
 [-UseConsistentWhitespaceCheckOpenParen] [-UseConsistentWhitespaceCheckOperator]
 [-UseConsistentWhitespaceCheckPipe] [-UseConsistentWhitespaceCheckSeparator] [-AlignAssignmentStatementEnable]
 [-AlignAssignmentStatementCheckHashtable] [-UseCorrectCasingEnable] [[-PSD1Style] <String>]
 [<CommonParameters>]
```

## DESCRIPTION
{{ Fill in the Description }}

## EXAMPLES

### Example 1
```powershell
PS C:\> {{ Add example code here }}
```

{{ Add example description here }}

## PARAMETERS

### -AlignAssignmentStatementCheckHashtable
{{ Fill AlignAssignmentStatementCheckHashtable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AlignAssignmentStatementEnable
{{ Fill AlignAssignmentStatementEnable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApplyTo
{{ Fill ApplyTo Description }}

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:
Accepted values: OnMergePSM1, OnMergePSD1, DefaultPSM1, DefaultPSD1

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableFormatting
{{ Fill EnableFormatting Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PSD1Style
{{ Fill PSD1Style Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: Minimal, Native

Required: False
Position: 5
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceEnable
{{ Fill PlaceCloseBraceEnable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceIgnoreOneLineBlock
{{ Fill PlaceCloseBraceIgnoreOneLineBlock Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceNewLineAfter
{{ Fill PlaceCloseBraceNewLineAfter Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceCloseBraceNoEmptyLineBefore
{{ Fill PlaceCloseBraceNoEmptyLineBefore Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceEnable
{{ Fill PlaceOpenBraceEnable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceIgnoreOneLineBlock
{{ Fill PlaceOpenBraceIgnoreOneLineBlock Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceNewLineAfter
{{ Fill PlaceOpenBraceNewLineAfter Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PlaceOpenBraceOnSameLine
{{ Fill PlaceOpenBraceOnSameLine Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveComments
{{ Fill RemoveComments Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Sort
{{ Fill Sort Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: None, Asc, Desc

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationEnable
{{ Fill UseConsistentIndentationEnable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationIndentationSize
{{ Fill UseConsistentIndentationIndentationSize Description }}

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationKind
{{ Fill UseConsistentIndentationKind Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: space, tab

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentIndentationPipelineIndentation
{{ Fill UseConsistentIndentationPipelineIndentation Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: IncreaseIndentationAfterEveryPipeline, NoIndentation

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckInnerBrace
{{ Fill UseConsistentWhitespaceCheckInnerBrace Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckOpenBrace
{{ Fill UseConsistentWhitespaceCheckOpenBrace Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckOpenParen
{{ Fill UseConsistentWhitespaceCheckOpenParen Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckOperator
{{ Fill UseConsistentWhitespaceCheckOperator Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckPipe
{{ Fill UseConsistentWhitespaceCheckPipe Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceCheckSeparator
{{ Fill UseConsistentWhitespaceCheckSeparator Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseConsistentWhitespaceEnable
{{ Fill UseConsistentWhitespaceEnable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseCorrectCasingEnable
{{ Fill UseCorrectCasingEnable Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

## OUTPUTS

### System.Object
## NOTES

## RELATED LINKS
