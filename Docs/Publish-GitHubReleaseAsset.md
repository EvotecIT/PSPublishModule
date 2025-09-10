---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Publish-GitHubReleaseAsset

## SYNOPSIS
Publishes a release asset to GitHub.

## SYNTAX

```
Publish-GitHubReleaseAsset [-ProjectPath] <String> [-GitHubUsername] <String> [-GitHubRepositoryName] <String>
 [-GitHubAccessToken] <String> [-IsPreRelease] [[-Version] <String>] [[-TagName] <String>] [[-TagTemplate] <String>]
 [[-ReleaseName] <String>] [-IncludeProjectNameInTag] [[-ZipPath] <String>] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## DESCRIPTION
Uses \`Send-GitHubRelease\` to create or update a GitHub release based on the
project version and upload the generated zip archive.

## EXAMPLES

### EXAMPLE 1
```
Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\MyProject' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyRepo' -GitHubAccessToken $Token
Uploads the current project zip to the specified GitHub repository.
```

### EXAMPLE 2
```
# Multiple packages in one repo can share the same version.
# Use a custom tag to avoid conflicts (e.g., include project name).
Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\OfficeIMO\OfficeIMO.Markdown' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'OfficeIMO' -GitHubAccessToken $Token -IncludeProjectNameInTag
```

### EXAMPLE 3
```
# Override version and tag explicitly
Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\OfficeIMO\OfficeIMO.Excel' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'OfficeIMO' -GitHubAccessToken $Token -Version '1.2.3' -TagName 'OfficeIMO.Excel-v1.2.3'
```

### EXAMPLE 4
```
# Use a custom tag template with placeholders {Project} and {Version}
Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\OfficeIMO\OfficeIMO.Word' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'OfficeIMO' -GitHubAccessToken $Token -TagTemplate 'officeimo/{Project}/v{Version}'
```

### EXAMPLE 5
```
# Provide a specific path to the asset zip instead of the default
Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\OfficeIMO\OfficeIMO.Excel' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'OfficeIMO' -GitHubAccessToken $Token -ZipPath 'C:\Git\OfficeIMO\OfficeIMO.Excel\bin\Release\OfficeIMO.Excel.1.2.3.zip'
```

## PARAMETERS

### -ProjectPath
Path to the project folder containing the *.csproj file.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -GitHubUsername
GitHub account name owning the repository.

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

### -GitHubRepositoryName
Name of the GitHub repository.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -GitHubAccessToken
Personal access token used for authentication.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IsPreRelease
Publish the release as a pre-release.

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

### -Version
Override the version discovered from the project file (VersionPrefix). Used to locate the zip and for default tag generation.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TagName
Explicit tag name to use. If omitted, defaults to `v<Version>`, `<ProjectName>-v<Version>` when `-IncludeProjectNameInTag` is specified, or the value produced by `-TagTemplate`.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReleaseName
Optional release display name. Defaults to the tag name when omitted.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 7
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeProjectNameInTag
When set and `-TagName` is not provided, the tag is generated as `<ProjectName>-v<Version>` to prevent tag collisions across packages in the same repository.

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

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
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

## RELATED LINKS
### -TagTemplate
Custom tag pattern with placeholders. Supported tokens:
- `{Project}` replaced with the project name (csproj BaseName)
- `{Version}` replaced with the version (from VersionPrefix or `-Version`)

If both `-TagName` and `-TagTemplate` are provided, `-TagName` takes precedence.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ZipPath
Use a specific zip file path for the release asset, instead of the default `bin/Release/<Project>.<Version>.zip`. The path must exist.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```
