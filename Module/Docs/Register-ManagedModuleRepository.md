---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Register-ManagedModuleRepository
## SYNOPSIS
Registers a managed module repository profile using PSResourceGet-shaped parameters.

## SYNTAX
### Name (Default)
```powershell
Register-ManagedModuleRepository [-Name] <string> [-Uri] <string> [-Trusted] [-Priority <int>] [-ApiVersion <RepositoryApiVersion>] [-Force] [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### PSGallery
```powershell
Register-ManagedModuleRepository -PSGallery [-Trusted] [-Priority <int>] [-Force] [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Repository
```powershell
Register-ManagedModuleRepository -Repository <hashtable[]> [-Force] [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Registers a managed module repository profile using PSResourceGet-shaped parameters.

## EXAMPLES

### EXAMPLE 1
```powershell
Register-ManagedModuleRepository -Name 'Name'
```


### EXAMPLE 2
```powershell
Register-ManagedModuleRepository -PSGallery
```


### EXAMPLE 3
```powershell
Register-ManagedModuleRepository -Repository @(@{})
```


## PARAMETERS

### -ApiVersion
Repository API version metadata. ContainerRegistry is handled by Microsoft Artifact Registry onboarding.

```yaml
Type: RepositoryApiVersion
Parameter Sets: Name
Aliases: None
Possible values: Auto, V2, V3, ContainerRegistry, Local, NugetServer

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Replaces an existing managed repository profile with the same name.

```yaml
Type: SwitchParameter
Parameter Sets: Name, PSGallery, Repository
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Repository profile name.

```yaml
Type: String
Parameter Sets: Name
Aliases: ProfileName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Returns the registered profile. The command is quiet by default, like Register-PSResourceRepository.

```yaml
Type: SwitchParameter
Parameter Sets: Name, PSGallery, Repository
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Priority
Repository priority.

```yaml
Type: Nullable`1
Parameter Sets: Name, PSGallery
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PSGallery
Registers the built-in PowerShell Gallery profile.

```yaml
Type: SwitchParameter
Parameter Sets: PSGallery
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Repository definitions shaped like Register-PSResourceRepository -Repository input.

```yaml
Type: Hashtable[]
Parameter Sets: Repository
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -Scope
Profile store scope to write.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: Name, PSGallery, Repository
Aliases: None
Possible values: User, Machine, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Trusted
Marks the repository profile as trusted.

```yaml
Type: SwitchParameter
Parameter Sets: Name, PSGallery
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Uri
Repository URI or local feed path.

```yaml
Type: String
Parameter Sets: Name
Aliases: RepositoryUri
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.Collections.Hashtable[]`

## OUTPUTS

- `PSPublishModule.ModuleRepositoryProfileResult` — User-facing private module repository profile saved by PSPublishModule.

## RELATED LINKS

- None
