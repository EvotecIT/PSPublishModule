---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Publish-NugetPackage
## SYNOPSIS
Pushes NuGet packages to a feed using dotnet nuget push.

## SYNTAX
### __AllParameterSets
```powershell
Publish-NugetPackage -Path <string[]> -ApiKey <string> [-Source <string>] [-SkipDuplicate] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Searches the provided -Path roots for *.nupkg files and pushes them using the .NET SDK.

Use -SkipDuplicate for CI-friendly, idempotent runs.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Publish-NugetPackage -Path '.\bin\Release' -ApiKey $env:NUGET_API_KEY -SkipDuplicate
```

Publishes all .nupkg files under the folder; safe to rerun in CI.

### EXAMPLE 2
```powershell
PS>Publish-NugetPackage -Path '.\artifacts' -ApiKey 'YOUR_KEY' -Source 'https://api.nuget.org/v3/index.json'
```

Use a different source URL for private feeds (e.g. GitHub Packages, Azure Artifacts).

## PARAMETERS

### -ApiKey
API key used to authenticate against the NuGet feed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Directory to search for NuGet packages.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipDuplicate
When set, passes --skip-duplicate to dotnet nuget push.
This makes repeated publishing runs idempotent when the package already exists.

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

### -Source
NuGet feed URL.

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

