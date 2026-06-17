---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-ModuleDependency
## SYNOPSIS
Installs a module and its embedded dependencies to an explicit private runtime folder.

## SYNTAX
### ByName (Default)
```powershell
Install-ModuleDependency [-Name] <string> [-Path] <string> [-RequiredVersion <version>] [-DependencyName <string[]>] [-OnExists <OnExistsOption>] [-Force] [-ListOnly] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByModule
```powershell
Install-ModuleDependency [-Path] <string> -Module <psobject> [-RequiredVersion <version>] [-DependencyName <string[]>] [-OnExists <OnExistsOption>] [-Force] [-ListOnly] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
The command reads the dependency manifest produced by New-ConfigurationModule -Type EmbeddedModule
and copies the root module plus each dependency payload to the requested path. Modules are not installed
into PSModulePath unless the chosen path is already part of PSModulePath.

## EXAMPLES

### EXAMPLE 1
```powershell
Install-ModuleDependency -Name EntraIDConfig -Path C:\PrivateDeps
```


### EXAMPLE 2
```powershell
Install-ModuleDependency -Name EntraIDConfig -DependencyName Microsoft.Graph.Authentication -Path C:\PrivateDeps -Force
```


## PARAMETERS

### -DependencyName
Dependency names to install. When omitted, installs all embedded dependencies.

```yaml
Type: String[]
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Overwrite existing dependency folders when merge or overwrite behavior requires it.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ListOnly
Preview planned dependency copies without writing files.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Module
Specific module object containing embedded dependencies.

```yaml
Type: PSObject
Parameter Sets: ByModule
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -Name
Module containing embedded dependencies.

```yaml
Type: String
Parameter Sets: ByName
Aliases: ModuleName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -OnExists
Conflict handling when a dependency version folder already exists.

```yaml
Type: OnExistsOption
Parameter Sets: ByName, ByModule
Aliases: None
Possible values: Merge, Refresh, Overwrite, Skip, Stop

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Destination folder. The root module and dependencies are copied under Name\Version folders.

```yaml
Type: String
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Optional exact source module version.

```yaml
Type: Version
Parameter Sets: ByName, ByModule
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

- `System.String
System.Management.Automation.PSObject`

## OUTPUTS

- `PowerForge.EmbeddedModuleDependencyInstallResult`

## RELATED LINKS

- None
