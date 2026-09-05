---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetInstaller
## SYNOPSIS
Creates MSI, Debian, or macOS app-bundle installer configuration for the DotNet publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetInstaller -Id <string> -PrepareFromTarget <string> [-Kind <DotNetPublishInstallerKind>] [-PrepareFromBundleId <string>] [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-InstallerProjectId <string>] [-InstallerProjectPath <string>] [-Authoring <PowerForgeInstallerDefinition>] [-StagingPath <string>] [-ManifestPath <string>] [-OutputPath <string>] [-OutputName <string>] [-Harvest <DotNetPublishMsiHarvestMode>] [-HarvestPath <string>] [-HarvestDirectoryRefId <string>] [-HarvestComponentGroupId <string>] [-HarvestExcludePatterns <string[]>] [-Sign <DotNetPublishSignOptions>] [-SignProfile <string>] [-SignOverrides <DotNetPublishSignPatch>] [-Versioning <DotNetPublishMsiVersionOptions>] [-MsBuildProperties <hashtable>] [-ClientLicense <DotNetPublishMsiClientLicenseOptions>] [-Debian <DotNetPublishDebianOptions>] [-MacApp <DotNetPublishMacAppOptions>] [<CommonParameters>]
```

## DESCRIPTION
Creates MSI, Debian, or macOS app-bundle installer configuration for the DotNet publish DSL.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetInstaller -Id 'service.msi' -PrepareFromTarget 'My.Service' -InstallerProjectPath 'Installer/My.Service.wixproj' -Harvest Auto
```


## PARAMETERS

### -Authoring
Optional PowerForge-owned installer authoring model used to generate a WiX SDK project.

```yaml
Type: PowerForgeInstallerDefinition
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -Debian
Debian package metadata used when Kind is Debian.

```yaml
Type: DotNetPublishDebianOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Frameworks
Optional target-framework filter for installer generation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -HarvestExcludePatterns
Optional wildcard patterns excluded from MSI harvesting.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -Kind
Installer format. Defaults to MSI.

```yaml
Type: DotNetPublishInstallerKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Msi, Debian, MacApp

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MacApp
macOS app-bundle metadata used when Kind is MacApp.

```yaml
Type: DotNetPublishMacAppOptions
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -OutputName
Optional installer output file-name template.

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

### -OutputPath
Optional installer output directory template.

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

### -PrepareFromBundleId
Optional bundle identifier used as the installer payload source.

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
Accept wildcard characters: False
```

### -Runtimes
Optional runtime filter for installer generation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -SignOverrides
Optional signing-profile overrides.

```yaml
Type: DotNetPublishSignPatch
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignProfile
Optional named signing profile.

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
Accept wildcard characters: False
```

### -Styles
Optional publish-style filter for installer generation.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, SelfContained, FrameworkDependent, AotSpeed, AotSize

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishInstaller`

## RELATED LINKS

- None
