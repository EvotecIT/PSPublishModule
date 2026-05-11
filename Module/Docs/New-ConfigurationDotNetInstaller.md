---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetInstaller
## SYNOPSIS
Creates installer configuration (MSI prepare/build) for DotNet publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetInstaller -Id <string> -PrepareFromTarget <string> [-InstallerProjectId <string>] [-InstallerProjectPath <string>] [-Authoring <PowerForgeInstallerDefinition>] [-StagingPath <string>] [-ManifestPath <string>] [-Harvest <DotNetPublishMsiHarvestMode>] [-HarvestPath <string>] [-HarvestDirectoryRefId <string>] [-HarvestComponentGroupId <string>] [-Sign <DotNetPublishSignOptions>] [-Versioning <DotNetPublishMsiVersionOptions>] [-MsBuildProperties <hashtable>] [-ClientLicense <DotNetPublishMsiClientLicenseOptions>] [<CommonParameters>]
```

## DESCRIPTION
Creates installer configuration (MSI prepare/build) for DotNet publish DSL.

When `Authoring` is set and `InstallerProjectPath` / `InstallerProjectId` are omitted, PowerForge generates a WiX SDK project during `msi.build`. This keeps installer identity, service setup, input properties, shortcuts, and companion folders in the typed DotNet publish configuration while still compiling through the normal WiX toolchain.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetInstaller -Id 'service.msi' -PrepareFromTarget 'My.Service' -InstallerProjectPath 'Installer/My.Service.wixproj' -Harvest Auto
```

### EXAMPLE 2
```powershell
$authoring = [PowerForge.PowerForgeInstallerDefinition]::new()
$authoring.Product.Name = 'My Service'
$authoring.Product.UpgradeCode = '{00000000-0000-0000-0000-000000000000}'
$authoring.CompanyFolderName = 'Evotec'
$authoring.InstallDirectoryName = 'My Service'
$authoring.PayloadComponentGroupId = 'ProductFiles'

New-ConfigurationDotNetInstaller -Id 'service.msi' -PrepareFromTarget 'My.Service' -Harvest Auto -Authoring $authoring
```

## PARAMETERS

### -Authoring
Optional typed installer authoring model used to generate a WiX SDK project during `msi.build`.

```yaml
Type: PowerForgeInstallerDefinition
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ClientLicense
Optional client-license injection policy.

```yaml
Type: DotNetPublishMsiClientLicenseOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Harvest
Harvest behavior for payload tree.

```yaml
Type: DotNetPublishMsiHarvestMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, Auto

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -HarvestComponentGroupId
Optional WiX component group id template for generated harvest fragment.

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

### -HarvestDirectoryRefId
Optional WiX directory reference id for generated harvest fragment.

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

### -HarvestPath
Optional harvest output path template.

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

### -Id
Installer identifier.

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

### -InstallerProjectId
Optional installer project catalog identifier.

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

### -InstallerProjectPath
Optional path to installer project file (*.wixproj).

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

### -ManifestPath
Optional manifest path template for MSI prepare output.

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

### -MsBuildProperties
Optional installer-specific MSBuild properties passed to msi.build.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PrepareFromTarget
Source publish target name used for prepare/build.

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

### -Sign
Optional MSI signing policy.

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

### -StagingPath
Optional staging path template for MSI payload.

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

### -Versioning
Optional MSI version policy.

```yaml
Type: DotNetPublishMsiVersionOptions
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

- `PowerForge.DotNetPublishInstaller`

## RELATED LINKS

- None

