---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ManagedModuleCatalog
## SYNOPSIS
Gets local managed module catalog settings or package metadata.

## SYNTAX
### __AllParameterSets
```powershell
Get-ManagedModuleCatalog [[-Name] <string[]>] [-PackageName <string[]>] [-Scope <ModuleRepositoryProfileScope>] [<CommonParameters>]
```

## DESCRIPTION
Gets local managed module catalog settings or package metadata.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ManagedModuleCatalog
```

Returns catalog settings and cached package counts.

### EXAMPLE 2
```powershell
Get-ManagedModuleCatalog -Name PSGallery -PackageName Pester
```

Returns the cached package entry for Pester from the PSGallery catalog.

## PARAMETERS

### -Name
Optional catalog names. When omitted, all visible catalogs are returned.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Repository, RepositoryName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PackageName
Optional package names to return from the selected catalogs.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Package, Module, ModuleName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Catalog storage scope. The default reads user catalogs before machine-wide catalogs.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ManagedModuleCatalog
PowerForge.ManagedModuleCatalogPackage`

## RELATED LINKS

- None
