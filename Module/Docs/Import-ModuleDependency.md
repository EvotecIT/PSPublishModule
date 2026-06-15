---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-ModuleDependency
## SYNOPSIS
Imports embedded or installed module dependencies by exact manifest/path.

## SYNTAX
### ByName (Default)
```powershell
Import-ModuleDependency [-Name] <string> [-RequiredVersion <version>] [-DependencyName <string[]>] [-Force] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByModule
```powershell
Import-ModuleDependency -Module <psobject> [-RequiredVersion <version>] [-DependencyName <string[]>] [-Force] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByPath
```powershell
Import-ModuleDependency [-Path] <string> [-DependencyName <string[]>] [-Force] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use -Name to import dependencies directly from a module's bundled Internals\Modules payload.
Use -Path after Install-ModuleDependency to import from a private dependency folder without
relying on PSModulePath discovery.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-ModuleDependency -Name EntraIDConfig
```


### EXAMPLE 2
```powershell
Import-ModuleDependency -Path C:\PrivateDeps -DependencyName Microsoft.Graph.Authentication
```


## PARAMETERS

### -DependencyName
Dependency names to import. When omitted, imports all dependencies in the receipt.

```yaml
Type: String[]
Parameter Sets: ByName, ByModule, ByPath
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force re-import of modules already loaded in the session.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath
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
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -PassThru
Return imported module information from Import-Module -PassThru.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Installed dependency root or module-dependencies.json receipt path.

```yaml
Type: String
Parameter Sets: ByPath
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Optional exact source module version when using -Name or -Module.

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

- `System.Object`

## RELATED LINKS

- None
