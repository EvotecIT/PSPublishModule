---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDelivery
## SYNOPSIS
Configures delivery metadata for bundling and installing internal docs/examples.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDelivery [-Enable] [-InternalsPath <string>] [-IncludeRootReadme] [-IncludeRootChangelog] [-IncludeRootLicense] [-ReadmeDestination <DeliveryBundleDestination>] [-ChangelogDestination <DeliveryBundleDestination>] [-LicenseDestination <DeliveryBundleDestination>] [-ImportantLinks <DeliveryImportantLink[]>] [-IntroText <string[]>] [-UpgradeText <string[]>] [-IntroFile <string>] [-UpgradeFile <string>] [-RepositoryPaths <string[]>] [-RepositoryBranch <string>] [-DocumentationOrder <string[]>] [-GenerateInstallCommand] [-GenerateUpdateCommand] [-InstallCommandName <string>] [-UpdateCommandName <string>] [<CommonParameters>]
```

## DESCRIPTION
Delivery configuration is used to bundle “internals” (docs, examples, tools, configuration files) into a module and optionally
generate public helper commands (Install-<ModuleName> / Update-<ModuleName>) that can copy these files to a target folder.

This is intended for “script packages” where the module contains additional artifacts that should be deployed alongside it.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -IncludeRootReadme -IncludeRootChangelog -GenerateInstallCommand -GenerateUpdateCommand
```

Generates public Install/Update helpers and bundles README/CHANGELOG into the module.

### EXAMPLE 2
```powershell
PS>New-ConfigurationDelivery -Enable -RepositoryPaths 'docs' -RepositoryBranch 'main' -DocumentationOrder '01-Intro.md','02-HowTo.md'
```

Helps modules expose docs from a repository path in a consistent order.

## PARAMETERS

### -ChangelogDestination
Where to bundle CHANGELOG.* within the built module.

```yaml
Type: DeliveryBundleDestination
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DocumentationOrder
Optional file-name order for Internals\\Docs when rendering documentation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enable
Enables delivery metadata emission.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GenerateInstallCommand
When set, generates a public Install-<ModuleName> helper function during build that copies Internals to a destination folder.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GenerateUpdateCommand
When set, generates a public Update-<ModuleName> helper function during build that delegates to the install command.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ImportantLinks
Important links (Title/Url). Accepts legacy hashtable array (@{ Title='..'; Url='..' }) or T:PowerForge.DeliveryImportantLink[].

```yaml
Type: DeliveryImportantLink[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeRootChangelog
Include module root CHANGELOG.* during installation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeRootLicense
Include module root LICENSE.* during installation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeRootReadme
Include module root README.* during installation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallCommandName
Optional override name for the generated install command. When empty, defaults to Install-<ModuleName>.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InternalsPath
Relative path inside the module that contains internal deliverables.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IntroFile
Relative path (within the module root) to a Markdown/text file to use as Intro content.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IntroText
Text lines shown to users after Install-ModuleDocumentation completes.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LicenseDestination
Where to bundle LICENSE.* within the built module.

```yaml
Type: DeliveryBundleDestination
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ReadmeDestination
Where to bundle README.* within the built module.

```yaml
Type: DeliveryBundleDestination
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryBranch
Optional branch name to use when fetching remote documentation.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryPaths
One or more repository-relative paths from which to display remote documentation files.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UpdateCommandName
Optional override name for the generated update command. When empty, defaults to Update-<ModuleName>.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UpgradeFile
Relative path (within the module root) to a Markdown/text file to use for Upgrade instructions.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UpgradeText
Text lines with upgrade instructions shown when requested.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

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

