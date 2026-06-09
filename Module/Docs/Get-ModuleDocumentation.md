---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleDocumentation
## SYNOPSIS
Gets module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) and renders it in the console.

Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent, it will backfill from the module's repository specified by PrivateData.PSData.ProjectUri (GitHub or Azure DevOps), using a token when necessary.

## SYNTAX
### ByName (Default)
```powershell
Get-ModuleDocumentation [[-Name] <string>] [-RequiredVersion <version>] [-Type <DocumentationSelection>] [-Readme] [-Changelog] [-License] [-Intro] [-Upgrade] [-Links] [-All] [-List] [-PreferInternals] [-Raw] [-File <string>] [-Online] [-JsonRenderer <string>] [-DefaultCodeLanguage <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [<CommonParameters>]
```

### ByModule
```powershell
Get-ModuleDocumentation [-Module <psmoduleinfo>] [-RequiredVersion <version>] [-Type <DocumentationSelection>] [-Readme] [-Changelog] [-License] [-Intro] [-Upgrade] [-Links] [-All] [-List] [-PreferInternals] [-Raw] [-File <string>] [-Online] [-JsonRenderer <string>] [-DefaultCodeLanguage <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [<CommonParameters>]
```

### ByPath
```powershell
Get-ModuleDocumentation [-DocsPath <string>] [-Type <DocumentationSelection>] [-Readme] [-Changelog] [-License] [-Intro] [-Upgrade] [-Links] [-All] [-List] [-PreferInternals] [-Raw] [-File <string>] [-Online] [-JsonRenderer <string>] [-DefaultCodeLanguage <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [<CommonParameters>]
```

### ByBase
```powershell
Get-ModuleDocumentation [-ModuleBase <string>] [-Type <DocumentationSelection>] [-Readme] [-Changelog] [-License] [-Intro] [-Upgrade] [-Links] [-All] [-List] [-PreferInternals] [-Raw] [-File <string>] [-Online] [-JsonRenderer <string>] [-DefaultCodeLanguage <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Gets module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) and renders it in the console.

Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent, it will backfill from the module's repository specified by PrivateData.PSData.ProjectUri (GitHub or Azure DevOps), using a token when necessary.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ModuleDocumentation -DocsPath 'C:\Path'
```


## PARAMETERS

### -All
Convenience switch to show Intro, README, CHANGELOG and LICENSE in order.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Changelog
Show CHANGELOG.*.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DefaultCodeLanguage
Default language for unlabeled code fences (Auto, PowerShell, Json, None).

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: Auto, PowerShell, Json, None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DocsPath
Direct path to a documentation folder containing README/CHANGELOG/etc.

```yaml
Type: String
Parameter Sets: ByPath
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -File
Show a specific file by name (relative to module root or Internals) or full path.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Intro
Show configured IntroText/IntroFile (from Delivery metadata).

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JsonRenderer
Select JSON renderer for fenced JSON blocks: Auto, Spectre, or System.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: Auto, Spectre, System

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -License
Show LICENSE.*.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Links
Show configured ImportantLinks from Delivery metadata.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -List
List discovered documentation files (without rendering).

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Module
Module object to display documentation for. Alternative to -Name.

```yaml
Type: PSModuleInfo
Parameter Sets: ByModule
Aliases: InputObject, ModuleInfo
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -ModuleBase
Path to a module root (folder that contains the module manifest). Useful for unpacked builds.

```yaml
Type: String
Parameter Sets: ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Module name to display documentation for.

```yaml
Type: String
Parameter Sets: ByName
Aliases: ModuleName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -Online
Enable repository access for remote documentation fallback and repository path enumeration.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PreferInternals
Prefer Internals folder over module root when both contain the same file kind.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Raw
Print raw file content without Markdown rendering.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Readme
Show README.*.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryBranch
Branch name to use when fetching remote docs. If omitted, the provider default branch is used.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryPaths
Repository-relative folders to enumerate and display (e.g., 'docs', 'articles'). Only .md/.markdown/.txt files are rendered.

```yaml
Type: String[]
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryToken
Personal Access Token for private repositories. Env fallbacks: PG_GITHUB_TOKEN/GITHUB_TOKEN or PG_AZDO_PAT/AZURE_DEVOPS_EXT_PAT.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Exact version to select when multiple module versions are installed.

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

### -Type
High-level selection of which documents to show. Overrides granular switches when specified.

```yaml
Type: DocumentationSelection
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: Default, Intro, Readme, Changelog, License, Upgrade, All, Links

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Upgrade
Show configured UpgradeText/UpgradeFile (from Delivery metadata or UPGRADE.*).

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule, ByPath, ByBase
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
System.Management.Automation.PSModuleInfo`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None
