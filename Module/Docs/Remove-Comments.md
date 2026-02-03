---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Remove-Comments
## SYNOPSIS
Removes PowerShell comments from a script file or provided content, with optional empty-line normalization.

## SYNTAX
### FilePath (Default)
```powershell
Remove-Comments -SourceFilePath <string> [-DestinationFilePath <string>] [-RemoveAllEmptyLines] [-RemoveEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-DoNotRemoveSignatureBlock] [<CommonParameters>]
```

### Content
```powershell
Remove-Comments -Content <string> [-DestinationFilePath <string>] [-RemoveAllEmptyLines] [-RemoveEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-DoNotRemoveSignatureBlock] [<CommonParameters>]
```

## DESCRIPTION
Uses the PowerShell parser (AST) to remove comments safely rather than relying on fragile regex-only approaches.
Useful as a preprocessing step when producing merged/packed scripts.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Remove-Comments -SourceFilePath '.\Public\Get-Thing.ps1' -DestinationFilePath '.\Public\Get-Thing.nocomments.ps1'
```

Writes the cleaned content to the destination file.

### EXAMPLE 2
```powershell
PS>$clean = Remove-Comments -Content (Get-Content -Raw .\script.ps1)
```

Returns the processed content when no destination file is specified.

## PARAMETERS

### -Content
Raw file content to process.

```yaml
Type: String
Parameter Sets: Content
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DestinationFilePath
File path to the destination file. If not provided, the content is returned.

```yaml
Type: String
Parameter Sets: FilePath, Content
Aliases: Destination, OutputFile, OutputFilePath

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DoNotRemoveSignatureBlock
Do not remove a signature block, if present.

```yaml
Type: SwitchParameter
Parameter Sets: FilePath, Content
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RemoveAllEmptyLines
Remove all empty lines from the content.

```yaml
Type: SwitchParameter
Parameter Sets: FilePath, Content
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RemoveCommentsBeforeParamBlock
Remove comments before the param block. By default comments before the param block are not removed.

```yaml
Type: SwitchParameter
Parameter Sets: FilePath, Content
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RemoveCommentsInParamBlock
Remove comments in the param block. By default comments in the param block are not removed.

```yaml
Type: SwitchParameter
Parameter Sets: FilePath, Content
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RemoveEmptyLines
Remove empty lines if more than one empty line is found.

```yaml
Type: SwitchParameter
Parameter Sets: FilePath, Content
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SourceFilePath
File path to the source file.

```yaml
Type: String
Parameter Sets: FilePath
Aliases: FilePath, Path, LiteralPath

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

