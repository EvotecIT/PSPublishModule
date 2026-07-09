---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-ManagedModuleCatalog
## SYNOPSIS
Creates or updates local managed module catalog cache settings.

## SYNTAX
### __AllParameterSets
```powershell
Set-ManagedModuleCatalog [[-Name] <string>] [-Source <string>] [-RepositoryKind <ManagedModuleRepositoryKind>] [-Mode <ManagedModuleCatalogCacheMode>] [-MaxStaleness <timespan>] [-IncludePrerelease <bool>] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
The catalog stores local repository metadata only. It does not mirror package blobs unless a later package cache
feature is explicitly enabled. Managed module commands can use this metadata as an opt-in fallback when live
repository metadata is unavailable.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-ManagedModuleCatalog -Name PSGallery -Mode Fallback -MaxStaleness 14.00:00:00
```

Stores user-local catalog settings for the canonical PowerShell Gallery source.

## PARAMETERS

### -IncludePrerelease
Include prerelease versions during catalog refresh.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaxStaleness
Maximum age accepted for stale catalog fallback decisions.

```yaml
Type: TimeSpan
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Mode
Local catalog cache mode.

```yaml
Type: ManagedModuleCatalogCacheMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, ReadThrough, Fallback, PreferCache, Offline

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Catalog name, usually the repository name. Defaults to PSGallery.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Repository, RepositoryName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryKind
Repository kind used by the catalog refresh path.

```yaml
Type: ManagedModuleRepositoryKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, NuGetV3, NuGetV2, LocalFolder

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Catalog storage scope.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: User, Machine, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Source
Repository source URL used to refresh metadata.

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

- `PowerForge.ManagedModuleCatalog`

## RELATED LINKS

- None
