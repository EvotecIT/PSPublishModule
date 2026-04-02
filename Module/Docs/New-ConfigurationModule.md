---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationModule
## SYNOPSIS
Provides a way to configure required, external, or approved modules used in the project.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationModule -Name <string[]> [-Type <ModuleDependencyKind>] [-Version <string>] [-MinimumVersion <string>] [-RequiredVersion <string>] [-Guid <string>] [<CommonParameters>]
```

## DESCRIPTION
Emits module dependency configuration segments. These are later used to patch the module manifest and (optionally)
install/package dependencies during a build.

Use RequiredModule for dependencies that should appear in the manifest and can also be bundled into build
artefacts when New-ConfigurationArtefact -AddRequiredModules is enabled. Use ExternalModule for
dependencies that must exist on the target system but should not be bundled into artefacts.

RequiredModule entries are written to the manifest RequiredModules. ExternalModule entries
are written to PrivateData.PSData.ExternalModuleDependencies. ApprovedModule entries are used by
merge/missing-function workflows and are not emitted as manifest dependencies.

Built-in Microsoft.PowerShell.* modules are ignored during manifest refresh because they are inbox runtime
modules, not gallery-resolvable dependencies.

Version and Guid values set to Auto or Latest are resolved from installed modules by default. When
New-ConfigurationBuild -ResolveMissingModulesOnline is enabled, repository results can be used without
installing the dependency first.

Choose only one versioning style per dependency: a minimum version (-Version or
-MinimumVersion) or an exact version (-RequiredVersion). Mixing them for the same module is treated
as invalid input.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version '5.6.1'
```

Declares a required dependency that is written into the manifest.

### EXAMPLE 2
```powershell
PS>New-ConfigurationModule -Type ExternalModule -Name 'Az.Accounts' -Version 'Latest'
```

Declares a dependency that is expected to be installed separately (not bundled into artefacts).

### EXAMPLE 3
```powershell
PS>New-ConfigurationModule -Type RequiredModule -Name 'PSWriteColor' -RequiredVersion '1.0.0'
```

Uses RequiredVersion when an exact match is required.

### EXAMPLE 4
```powershell
PS>New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods','PSWriteColor'
```

Allows approved helper functions to be copied into the built module when they are actually used.

### EXAMPLE 5
```powershell
PS>New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version 'Latest' -Guid 'Auto'
```

Pairs well with New-ConfigurationBuild -ResolveMissingModulesOnline when the module is not installed locally.

## PARAMETERS

### -Guid
GUID of the dependency module (or Auto). This is most useful when you want manifest validation to lock
onto a specific module identity across repositories.

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

### -MinimumVersion
Minimum version of the dependency module (preferred over -Version). Use this when any newer compatible
version is acceptable.

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

### -Name
Name of the PowerShell module(s) that your module depends on. Multiple names emit one configuration segment per
module using the same dependency settings.

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

### -RequiredVersion
Required version of the dependency module (exact match). Use this when consumers and packaging must resolve the
exact same version.

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

### -Type
Choose between RequiredModule, ExternalModule, and ApprovedModule.
RequiredModule is used for manifest and optional packaging, ExternalModule is install-only, and
ApprovedModule is merge-only.

```yaml
Type: ModuleDependencyKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: RequiredModule, ExternalModule, ApprovedModule

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Version
Minimum version of the dependency module (or Auto/Latest). This is treated the same as
-MinimumVersion and cannot be combined with -RequiredVersion.

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

