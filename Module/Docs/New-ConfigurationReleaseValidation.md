---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationReleaseValidation
## SYNOPSIS
Creates a reusable package and runtime validation configuration.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationReleaseValidation [-ProjectRoot <string>] [-Packages <PackageSetValidation>] [-Modules <ModuleArtifactValidation[]>] [-CliArtifacts <CliArtifactValidation>] [-Tools <DotNetToolValidation[]>] [-Consumers <PackageConsumerValidation[]>] [-Commands <ReleaseCommandValidation[]>] [-IncludeSchema] [<CommonParameters>]
```

## DESCRIPTION
Use typed objects or PowerShell hashtables for product expectations. The same configuration can be executed directly or exported as JSON.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationReleaseValidation -Tools @{
    PackageId = 'Example.Tool'; PackageRoot = 'Artifacts/packages'; CommandName = 'example'
    Commands = @(@{ Name = 'Version'; FileName = '{ToolPath}'; Arguments = @('--version'); ExpectedOutput = '{Version}' })
}
```


## PARAMETERS

### -CliArtifacts
CLI manifest and publish matrix expectations.

```yaml
Type: CliArtifactValidation
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Commands
Additional product commands and output expectations.

```yaml
Type: ReleaseCommandValidation[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Consumers
Product-owned projects to run against the staged packages.

```yaml
Type: PackageConsumerValidation[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeSchema
Include the shared JSON schema reference when exporting.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Modules
Module archives or directories and optional product smoke scripts.

```yaml
Type: ModuleArtifactValidation[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Packages
NuGet package identities, payloads, dependencies, and signing expectations.

```yaml
Type: PackageSetValidation
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectRoot
Root for relative input paths.

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

### -Tools
Local .NET tool package installation checks.

```yaml
Type: DotNetToolValidation[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ReleaseValidationSpec`

## RELATED LINKS

- None
