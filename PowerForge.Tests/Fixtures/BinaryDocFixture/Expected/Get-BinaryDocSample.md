---
external help file: BinaryDocFixture-help.xml
Module Name: BinaryDocFixture
online version:
schema: 2.0.0
---
# Get-BinaryDocSample
## SYNOPSIS
Returns a sample binary help object.

## SYNTAX
### __AllParameterSets
```powershell
Get-BinaryDocSample [-Name] <string> [-Mode <BinaryDocMode>] [<CommonParameters>]
```

## DESCRIPTION
First legacy paragraph for the long command description.

Second legacy paragraph for the long command description.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Get-BinaryDocSample `
  -Name 'Alpha' `
  -Mode Advanced
```

Returns a sample output object for documentation tests.

Preserves example formatting and prompt handling.

## PARAMETERS

### -Mode
Selects the sample rendering mode.

```yaml
Type: BinaryDocMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Basic, Advanced

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Name of the requested sample object.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: SampleName
Possible values: 

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String`

## OUTPUTS

- `BinaryDocFixture.BinaryDocOutput` — Represents the output returned by the binary documentation fixture command.

## RELATED LINKS

- [Binary fixture reference](https://example.invalid/binary-doc-sample)

## NOTES

### Important

Only use this command with fixture input.

It exists to validate generated help fidelity.
