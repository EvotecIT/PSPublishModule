---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Show-ModuleDocumentation
## SYNOPSIS
Displays module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) in the console.

Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent or when requested, it can fetch files directly from the module's repository specified by PrivateData.PSData.ProjectUri (GitHub or Azure DevOps), optionally using a Personal Access Token.

## SYNTAX
### ByName (Default)
```powershell
Show-ModuleDocumentation [[-Name] <string>] [-RequiredVersion <version>] [-File <string>] [-PreferInternals] [-Open] [-HeadingRules <string>] [-OutputPath <string>] [-DoNotShow] [-DisableTokenizer] [-SkipDependencies] [-SkipCommands] [-Fast] [-MaxCommands <int>] [-HelpTimeoutSeconds <int>] [-HelpAsCode] [-ExamplesMode <string>] [-ExamplesLayout <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [-Online] [-Mode <string>] [-ShowDuplicates] [<CommonParameters>]
```

### ByModule
```powershell
Show-ModuleDocumentation [-Module <psmoduleinfo>] [-RequiredVersion <version>] [-File <string>] [-PreferInternals] [-Open] [-HeadingRules <string>] [-OutputPath <string>] [-DoNotShow] [-DisableTokenizer] [-SkipDependencies] [-SkipCommands] [-Fast] [-MaxCommands <int>] [-HelpTimeoutSeconds <int>] [-HelpAsCode] [-ExamplesMode <string>] [-ExamplesLayout <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [-Online] [-Mode <string>] [-ShowDuplicates] [<CommonParameters>]
```

### ByPath
```powershell
Show-ModuleDocumentation [-DocsPath <string>] [-File <string>] [-PreferInternals] [-Open] [-HeadingRules <string>] [-OutputPath <string>] [-DoNotShow] [-DisableTokenizer] [-SkipDependencies] [-SkipCommands] [-Fast] [-MaxCommands <int>] [-HelpTimeoutSeconds <int>] [-HelpAsCode] [-ExamplesMode <string>] [-ExamplesLayout <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [-Online] [-Mode <string>] [-ShowDuplicates] [<CommonParameters>]
```

### ByBase
```powershell
Show-ModuleDocumentation [-ModuleBase <string>] [-File <string>] [-PreferInternals] [-Open] [-HeadingRules <string>] [-OutputPath <string>] [-DoNotShow] [-DisableTokenizer] [-SkipDependencies] [-SkipCommands] [-Fast] [-MaxCommands <int>] [-HelpTimeoutSeconds <int>] [-HelpAsCode] [-ExamplesMode <string>] [-ExamplesLayout <string>] [-RepositoryBranch <string>] [-RepositoryToken <string>] [-RepositoryPaths <string[]>] [-Online] [-Mode <string>] [-ShowDuplicates] [<CommonParameters>]
```

## DESCRIPTION
Displays module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) in the console.

Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent or when requested, it can fetch files directly from the module's repository specified by PrivateData.PSData.ProjectUri (GitHub or Azure DevOps), optionally using a Personal Access Token.

## EXAMPLES

### EXAMPLE 1
```powershell
Show-ModuleDocumentation -DocsPath 'C:\Path'
```


## PARAMETERS

### -DisableTokenizer
Disable code tokenizers and render code fences as plain text.

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

### -DoNotShow
Do not open the generated HTML after export.

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

### -ExamplesLayout
Controls how examples are rendered: ProseFirst (remarks then code), MamlDefault (code then remarks), or AllAsCode.
Default is ProseFirst.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: MamlDefault, ProseFirst, AllAsCode

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExamplesMode
Controls how Examples are sourced. Raw = parse the EXAMPLES section from Get-Help text; Maml = use structured help object; Auto = Raw then Maml.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: Auto, Raw, Maml

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Fast
Convenience switch equal to -SkipRemote -SkipDependencies -SkipCommands.

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

### -HeadingRules
Heading rulers style. H1AndH2 draws rules for H1/H2, H1 for H1 only, None disables.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: None, H1, H1AndH2

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -HelpAsCode
Render command help inside fenced code blocks for uniform monospace formatting.

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

### -HelpTimeoutSeconds
Per-command Get-Help timeout in seconds. Default 3.

```yaml
Type: Int32
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaxCommands
Limit the number of commands rendered in the Commands tab. Default 100.

```yaml
Type: Int32
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Mode
Selection policy for standard tabs when Local and Remote exist (applies only when -Online):
PreferLocal (default), PreferRemote, or All.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: None
Possible values: PreferLocal, PreferRemote, All

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
Enable repository access (fetch remote docs using manifest defaults or -Repository* overrides).

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

### -Open
Open the generated HTML in the default shell handler after export.

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

### -OutputPath
Output location for the generated HTML. Accepts a file path or an existing directory. Defaults to temp when omitted.

```yaml
Type: String
Parameter Sets: ByName, ByModule, ByPath, ByBase
Aliases: Path, ExportHtmlPath
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
Repository-relative folders to enumerate and display (e.g., 'docs', 'articles').
Only .md/.markdown/.txt files are rendered.

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
Personal Access Token for private repositories. Alternatively set environment variables:
GitHub: PG_GITHUB_TOKEN or GITHUB_TOKEN; Azure DevOps: PG_AZDO_PAT or AZURE_DEVOPS_EXT_PAT.

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

### -ShowDuplicates
Show both variants even when content is identical (root vs internals and local vs remote).

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

### -SkipCommands
Skip building the Commands tab (fast export).

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

### -SkipDependencies
Skip building the dependency list and graph.

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
