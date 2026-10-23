---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleRepositoryDiagnostic
## SYNOPSIS
Inspects native repository registrations and authentication inputs without contacting the feed.

## SYNTAX
### __AllParameterSets
```powershell
Get-ModuleRepositoryDiagnostic [[-RepositoryName] <string>] [-Tool <PublishTool>] [-RepositoryUri <string>] [<CommonParameters>]
```

## DESCRIPTION
Reports contain presence and origin metadata. Secret values are never exported or decrypted.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ModuleRepositoryDiagnostic -RepositoryName CompanyFeed -Tool PSResourceGet
```


### EXAMPLE 2
```powershell
Get-ModuleRepositoryDiagnostic -RepositoryName CompanyFeed | ConvertTo-Json -Depth 10 | Set-Content .\repository-diagnostics.json
```


## PARAMETERS

### -RepositoryName
Name registered with the selected native backend.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Name
Possible values:

Required: False
Position: 0
Default value: 'PSGallery'
Accept pipeline input: False
Accept wildcard characters: False
```

### -RepositoryUri
Optional expected endpoint for comparison with the registered repository.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Tool
Native backend to inspect. Inspection does not register repositories or acquire credentials.

```yaml
Type: PublishTool
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: PSResourceGet, PowerShellGet

Required: False
Position: named
Default value: [PowerForge.PublishTool]::PSResourceGet
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.RepositoryDiagnosticReport`

## RELATED LINKS

- None
