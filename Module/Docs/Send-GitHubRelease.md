---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Send-GitHubRelease
## SYNOPSIS
Creates a new release for the given GitHub repository and optionally uploads assets.

## SYNTAX
### __AllParameterSets
```powershell
Send-GitHubRelease -GitHubUsername <string> -GitHubRepositoryName <string> -GitHubAccessToken <string> -TagName <string> [-ReleaseName <string>] [-ReleaseNotes <string>] [-GenerateReleaseNotes] [-AssetFilePaths <string[]>] [-Commitish <string>] [-IsDraft <bool>] [-IsPreRelease <bool>] [-ReuseExistingReleaseOnConflict <bool>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet uses the GitHub REST API to create a release and upload assets. It is a lower-level building block used by
higher-level helpers (such as Publish-GitHubReleaseAsset) and can also be used directly in CI pipelines.

Provide the token via an environment variable to avoid leaking secrets into logs or history.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Send-GitHubRelease -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -TagName 'v1.2.3' -ReleaseNotes 'Bug fixes' -AssetFilePaths 'C:\Artifacts\MyProject.zip'
```

Creates the release and uploads the specified asset file.

### EXAMPLE 2
```powershell
PS> Send-GitHubRelease -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -TagName 'v1.2.3-preview.1' -IsDraft $true -IsPreRelease $true
```

Creates a draft prerelease that can be reviewed before publishing.

## PARAMETERS

### -AssetFilePaths
The full paths of the files to include as release assets.

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

### -Commitish
Commitish value that determines where the Git tag is created from.

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

### -GenerateReleaseNotes
When set, asks GitHub to generate release notes automatically (cannot be used with ReleaseNotes).

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

### -GitHubAccessToken
GitHub personal access token used for authentication.

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

### -GitHubRepositoryName
GitHub repository name.

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

### -GitHubUsername
GitHub username owning the repository.

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

### -IsDraft
True to create a draft (unpublished) release.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IsPreRelease
True to identify the release as a prerelease.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReleaseName
The name of the release. If omitted, TagName is used.

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

### -ReleaseNotes
The text describing the contents of the release.

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

### -ReuseExistingReleaseOnConflict
When true, a 422 tag conflict deliberately reuses the existing release.
The default is false so normal publishing cannot mutate an older release.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TagName
The tag name used for the release.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None
