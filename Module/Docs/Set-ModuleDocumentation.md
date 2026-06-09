---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-ModuleDocumentation
## SYNOPSIS
Configures repository access for documentation (stores/revokes tokens).

Stores Personal Access Tokens for GitHub and/or Azure DevOps under the current user profile so module documentation commands can access private repositories. On Windows, tokens are protected with DPAPI; on other platforms they are stored as Base64 (best effort).

## SYNTAX
### __AllParameterSets
```powershell
Set-ModuleDocumentation [-GitHubToken <string>] [-AzureDevOpsPat <string>] [-FromEnvironment] [-Clear] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Configures repository access for documentation (stores/revokes tokens).

Stores Personal Access Tokens for GitHub and/or Azure DevOps under the current user profile so module documentation commands can access private repositories. On Windows, tokens are protected with DPAPI; on other platforms they are stored as Base64 (best effort).

## EXAMPLES

### EXAMPLE 1
```powershell
Set-ModuleDocumentation -AzureDevOpsPat 'Value'
```


## PARAMETERS

### -AzureDevOpsPat
Azure DevOps Personal Access Token (scope: Code (Read)).

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

### -Clear
Remove any stored tokens.

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

### -FromEnvironment
Read tokens from environment variables (PG_GITHUB_TOKEN/GITHUB_TOKEN and PG_AZDO_PAT/AZURE_DEVOPS_EXT_PAT).

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

### -GitHubToken
GitHub token (scope: repo for private repositories).

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None
