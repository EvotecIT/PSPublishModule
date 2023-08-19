---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Remove-Comments

## SYNOPSIS
Remove comments from PowerShell file

## SYNTAX

### FilePath (Default)
```
Remove-Comments -SourceFilePath <String> [-DestinationFilePath <String>] [-RemoveAllEmptyLines]
 [-RemoveEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock]
 [-DoNotRemoveSignatureBlock] [<CommonParameters>]
```

### Content
```
Remove-Comments -Content <String> [-DestinationFilePath <String>] [-RemoveAllEmptyLines] [-RemoveEmptyLines]
 [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-DoNotRemoveSignatureBlock]
 [<CommonParameters>]
```

## DESCRIPTION
Remove comments from PowerShell file and optionally remove empty lines
By default comments in param block are not removed
By default comments before param block are not removed

## EXAMPLES

### EXAMPLE 1
```
Remove-Comments -SourceFilePath 'C:\Support\GitHub\PSPublishModule\Examples\TestScript.ps1' -DestinationFilePath 'C:\Support\GitHub\PSPublishModule\Examples\TestScript1.ps1' -RemoveAllEmptyLines -RemoveCommentsInParamBlock -RemoveCommentsBeforeParamBlock
```

## PARAMETERS

### -SourceFilePath
File path to the source file

```yaml
Type: String
Parameter Sets: FilePath
Aliases: FilePath, Path, LiteralPath

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Content
Content of the file

```yaml
Type: String
Parameter Sets: Content
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DestinationFilePath
File path to the destination file.
If not provided, the content will be returned

```yaml
Type: String
Parameter Sets: (All)
Aliases: Destination

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveAllEmptyLines
Remove all empty lines from the content

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
Remove empty lines if more than one empty line is found

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
Remove comments in param block.
By default comments in param block are not removed

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
Remove comments before param block.
By default comments before param block are not removed

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

### -DoNotRemoveSignatureBlock
{{ Fill DoNotRemoveSignatureBlock Description }}

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
Most of the work done by Chris Dent, with improvements by Przemyslaw Klys

## RELATED LINKS
