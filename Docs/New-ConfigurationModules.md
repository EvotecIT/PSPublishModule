---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationModules

## SYNOPSIS
Provides a way to configure Required Modules or External Modules that will be used in the project.

## SYNTAX

```
New-ConfigurationModules [[-Type] <Object>] [-Name] <String> [[-Version] <String>] [[-Guid] <String>]
 [<CommonParameters>]
```

## DESCRIPTION
Provides a way to configure Required Modules or External Modules that will be used in the project.

## EXAMPLES

### EXAMPLE 1
```
An example
```

## PARAMETERS

### -Type
Choose between RequiredModule and ExternalModule, where RequiredModule is the default.

```yaml
Type: Object
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: RequiredModule
Accept pipeline input: False
Accept wildcard characters: False
```

### -Name
Name of PowerShell module that you want your module to depend on.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Version
Version of PowerShell module that you want your module to depend on.
If you don't specify a version, any version of the module is acceptable.
You can also use word 'Latest' to specify that you want to use the latest version of the module, and the module will be pickup up latest version available on the system.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Guid
Guid of PowerShell module that you want your module to depend on.
If you don't specify a Guid, any Guid of the module is acceptable, but it is recommended to specify it.
Alternatively you can use word 'Auto' to specify that you want to use the Guid of the module, and the module GUID

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS
