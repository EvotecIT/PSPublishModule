---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Out-FileUtf8NoBom

## SYNOPSIS
Outputs to a UTF-8-encoded file *without a BOM* (byte-order mark).

## SYNTAX

```
Out-FileUtf8NoBom [-LiteralPath] <String> [-Append] [-NoClobber] [-Width <Int32>] [-InputObject <Object>]
 [<CommonParameters>]
```

## DESCRIPTION
Mimics the most important aspects of Out-File:
* Input objects are sent to Out-String first.
* -Append allows you to append to an existing file, -NoClobber prevents
  overwriting of an existing file.
* -Width allows you to specify the line width for the text representations
   of input objects that aren't strings.
However, it is not a complete implementation of all Out-String parameters:
* Only a literal output path is supported, and only as a parameter.
* -Force is not supported.

Caveat: *All* pipeline input is buffered before writing output starts,
        but the string representations are generated and written to the target
        file one by one.

## EXAMPLES

### Example 1
```powershell
PS C:\> {{ Add example code here }}
```

{{ Add example description here }}

## PARAMETERS

### -LiteralPath
{{ Fill LiteralPath Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Append
{{ Fill Append Description }}

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

### -NoClobber
{{ Fill NoClobber Description }}

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

### -Width
{{ Fill Width Description }}

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: 0
Accept pipeline input: False
Accept wildcard characters: False
```

### -InputObject
{{ Fill InputObject Description }}

```yaml
Type: Object
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
The raison d' ªtre for this advanced function is that, as of PowerShell v5,
Out-File still lacks the ability to write UTF-8 files without a BOM:
using -Encoding UTF8 invariably prepends a BOM.

## RELATED LINKS
