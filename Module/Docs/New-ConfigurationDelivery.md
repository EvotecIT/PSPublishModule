---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationDelivery

## SYNOPSIS
Configures delivery metadata for bundling and installing internal docs/examples.

## SYNTAX

```
New-ConfigurationDelivery [-Enable] [[-InternalsPath] <String>] [-IncludeRootReadme] [-IncludeRootChangelog]
 [-IncludeRootLicense] [[-ReadmeDestination] <String>] [[-ChangelogDestination] <String>]
 [[-LicenseDestination] <String>] [[-ImportantLinks] <IDictionary[]>] [[-IntroText] <String[]>]
 [[-UpgradeText] <String[]>] [[-IntroFile] <String>] [[-UpgradeFile] <String>] [[-RepositoryPaths] <String[]>]
 [[-RepositoryBranch] <String>] [[-DocumentationOrder] <String[]>] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Adds Delivery options to the PSPublishModule configuration so the build embeds
runtime metadata in the manifest (PrivateData.PSData.Delivery and PrivateData.PSData.Repository)
and so the Internals folder is easy to find post-install by helper cmdlets
such as Install-ModuleDocumentation.

Typical usage is to call this in your Build\Manage-Module.ps1 alongside
New-ConfigurationInformation -IncludeAll 'Internals\' so that the Internals
directory is physically included in the shipped module and discoverable later.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -IncludeRootReadme -IncludeRootChangelog
Emits Options.Delivery and causes PrivateData.PSData.Delivery/Repository to be written in the manifest.
```

### EXAMPLE 2
```
New-ConfigurationInformation -IncludeAll 'Internals\'
PS> New-ConfigurationDelivery -Enable
Minimal configuration that bundles Internals and exposes it to the installer.
```

### EXAMPLE 3
```
# Include Internals and configure Docs ordering + repo docs
New-ConfigurationInformation -IncludeAll 'Internals\'
New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -DocumentationOrder '01-Intro.md','02-HowTo.md','99-FAQ.md' -RepositoryPaths 'docs' -RepositoryBranch 'main'
# This causes:
# - Internals\Docs to be bundled and shown in the UI in the given order (remaining files alphabetically)
# - Repository Docs folder to be fetched/displayed when -PreferRepository is used, also respecting DocumentationOrder
```

### EXAMPLE 4
```
# GitHub: configure remote docs
# 1) Set token once (Windows): setx PG_GITHUB_TOKEN "ghp_xxx"
#    or call Set-ModuleDocumentation -GitHubToken 'ghp_xxx'
# 2) In your build script, indicate where docs live in the repo
New-ConfigurationDelivery -Enable -RepositoryPaths 'docs' -RepositoryBranch main
# Then on the viewing side: Show-ModuleDocumentation -Name EFAdminManager -PreferRepository
```

### EXAMPLE 5
```
# Azure DevOps: configure remote docs
# URL form: https://dev.azure.com/{organization}/{project}/_git/{repository}
# PAT scope: Code (Read)
# 1) Set token once (Windows): setx PG_AZDO_PAT "your_pat"
#    or call Set-ModuleDocumentation -AzureDevOpsPat 'your_pat'
# 2) In your build script, indicate repo docs path
New-ConfigurationDelivery -Enable -RepositoryPaths 'Docs/en-US' -RepositoryBranch main
# Then on the viewing side: Show-ModuleDocumentation -Name EFAdminManager -PreferRepository
```

### EXAMPLE 6
```
# Mix Intro/Upgrade from files with ImportantLinks and include LICENSE in both root and Internals
New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -IntroFile 'Docs\\Intro.md' -UpgradeFile 'Docs\\Upgrade.md' -ImportantLinks @(
    @{ Title = 'Home'; Url = 'https://contoso.example/docs' },
    @{ Title = 'Issues'; Url = 'https://contoso.example/issues' }
) -IncludeRootLicense -LicenseDestination Both
```

### EXAMPLE 7
```
# Multiple repository docs roots and custom order
New-ConfigurationDelivery -Enable -RepositoryPaths 'docs','guides' -RepositoryBranch main -DocumentationOrder '01-Intro.md','20-FAQ.md'
```

## PARAMETERS

### -Enable
Enables delivery metadata emission.
If not specified, nothing is emitted.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -InternalsPath
Relative path inside the module that contains internal deliverables
(e.g.
'Internals').
Defaults to 'Internals'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: Internals
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRootReadme
Include module root README.* during installation (if present).

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRootChangelog
Include module root CHANGELOG.* during installation (if present).

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRootLicense
Include module root LICENSE.* during installation (if present).

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReadmeDestination
Where to bundle README.* within the built module.
One of: Internals, Root, Both, None.
Default: Internals.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: Internals
Accept pipeline input: False
Accept wildcard characters: False
```

### -ChangelogDestination
Where to bundle CHANGELOG.* within the built module.
One of: Internals, Root, Both, None.
Default: Internals.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: Internals
Accept pipeline input: False
Accept wildcard characters: False
```

### -LicenseDestination
Where to bundle LICENSE.* within the built module.
One of: Internals, Root, Both, None.
Default: Internals.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: Internals
Accept pipeline input: False
Accept wildcard characters: False
```

### -ImportantLinks
One or more key/value pairs that represent important links to display to the user,
for example @{ Title = 'Docs'; Url = 'https://...' }.

```yaml
Type: IDictionary[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IntroText
Text lines shown to users after Install-ModuleDocumentation completes.
Accepts a string array.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UpgradeText
Text lines with upgrade instructions shown when requested via Show-ModuleDocumentation -Upgrade.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 7
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IntroFile
Relative path (within the module root) to a Markdown/text file to use as the Intro content.
If provided, it is preferred over IntroText for display and is also copied by
Install-ModuleDocumentation.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 8
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UpgradeFile
Relative path (within the module root) to a Markdown/text file to use for Upgrade instructions.
If provided, it is preferred over UpgradeText for display and is also copied by
Install-ModuleDocumentation.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 9
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RepositoryPaths
One or more repository-relative paths (folders) from which to display remote documentation files
directly from the git hosting provider (GitHub/Azure DevOps).
This enables tools such as
PSMaintenance to fetch and present docs straight from the repository when local copies are not
present or when explicitly requested.
Requires PrivateData.PSData.ProjectUri to be set in the manifest.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 10
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RepositoryBranch
Optional branch name to use when fetching remote documentation.
If omitted, providers fall back to
the repository default branch (e.g., main/master).

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 11
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DocumentationOrder
Optional file-name order for Internals\Docs when rendering documentation.
Provide names such as
'Intro.md','HowTo.md','FAQ.md'; files not listed are appended alphabetically.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 12
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
This emits a Type 'Options' object under Options.Delivery so it works with the
existing New-PrepareStructure logic without further changes.

## RELATED LINKS
