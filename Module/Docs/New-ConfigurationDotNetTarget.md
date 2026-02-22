---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetTarget
## SYNOPSIS
Creates a DotNet publish target entry for DotNet publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetTarget -Name <string> -Framework <string> [-ProjectId <string>] [-ProjectPath <string>] [-Kind <DotNetPublishTargetKind>] [-Frameworks <string[]>] [-Runtimes <string[]>] [-Style <DotNetPublishStyle>] [-Styles <DotNetPublishStyle[]>] [-OutputPath <string>] [-RenameTo <string>] [-UseStaging <bool>] [-ClearOutput <bool>] [-Slim <bool>] [-KeepSymbols] [-KeepDocs] [-PruneReferences <bool>] [-Zip] [-ZipPath <string>] [-ZipNameTemplate <string>] [-ReadyToRun <bool>] [-Sign <DotNetPublishSignOptions>] [-Service <DotNetPublishServicePackageOptions>] [-State <DotNetPublishStatePreservationOptions>] [<CommonParameters>]
```

## DESCRIPTION
Creates a DotNet publish target entry for DotNet publish DSL.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetTarget -Name 'My.Service' -ProjectPath 'src/My.Service/My.Service.csproj' -Framework 'net10.0-windows' -Runtimes 'win-x64','win-arm64' -Style PortableCompat -Zip
```

## PARAMETERS

### -ClearOutput
Clear final output before copy.

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

### -Framework
Target framework (for example net10.0 or net10.0-windows).

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

### -Frameworks
Optional framework matrix values.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KeepDocs
Keep documentation files (*.xml, *.pdf).

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

### -KeepSymbols
Keep symbol files (*.pdb).

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

### -Kind
Target kind metadata.

```yaml
Type: DotNetPublishTargetKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Unknown, Cli, Service, Library

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Friendly target name.

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

### -OutputPath
Optional output path template.

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

### -ProjectId
Optional project catalog identifier.

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

### -ProjectPath
Path to target project file (*.csproj).

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

### -PruneReferences
Prune ref/ folder from output.

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

### -ReadyToRun
Optional ReadyToRun toggle for non-AOT styles.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RenameTo
Optional executable rename applied after publish.

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

### -Runtimes
Runtime identifiers.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Service
Optional service packaging settings for this target.

```yaml
Type: DotNetPublishServicePackageOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Sign
Optional signing policy for this target.

```yaml
Type: DotNetPublishSignOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Slim
Apply slimming cleanup.

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

### -State
Optional preserve/restore settings for rebuild-safe deployments.

```yaml
Type: DotNetPublishStatePreservationOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Style
Primary publish style.

```yaml
Type: DotNetPublishStyle
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Styles
Optional style matrix values.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseStaging
Use staging directory before final copy.

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

### -Zip
Create zip artifact for target output.

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

### -ZipNameTemplate
Optional zip file name template.

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

### -ZipPath
Optional explicit zip output path.

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

- `PowerForge.DotNetPublishTarget`

## RELATED LINKS

- None

