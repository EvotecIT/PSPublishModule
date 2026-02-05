---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Publish-GitHubReleaseAsset
## SYNOPSIS
Publishes a release asset to GitHub (creates a release and uploads a zip).

## SYNTAX
### __AllParameterSets
```powershell
Publish-GitHubReleaseAsset -ProjectPath <string[]> -GitHubUsername <string> -GitHubRepositoryName <string> -GitHubAccessToken <string> [-IsPreRelease] [-GenerateReleaseNotes] [-Version <string>] [-TagName <string>] [-TagTemplate <string>] [-ReleaseName <string>] [-IncludeProjectNameInTag] [-ZipPath <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Reads project metadata from *.csproj, resolves the release version (unless overridden),
creates a GitHub release, and uploads the specified ZIP asset.

For private repositories, use a token with the minimal required scope and prefer providing it via an environment variable.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Publish-GitHubReleaseAsset -ProjectPath '.\MyProject\MyProject.csproj' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN
```

Creates a GitHub release and uploads bin\Release\<Project>.<Version>.zip.

### EXAMPLE 2
```powershell
PS>Publish-GitHubReleaseAsset -ProjectPath '.\MyProject\MyProject.csproj' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -IsPreRelease -TagTemplate '{Project}-v{Version}'
```

Useful when your repository uses a specific tag naming convention.

## PARAMETERS

### -GenerateReleaseNotes
When set, asks GitHub to generate release notes automatically.

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

### -GitHubAccessToken
Personal access token used for authentication.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubRepositoryName
Name of the GitHub repository.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubUsername
GitHub account name owning the repository.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeProjectNameInTag
When set, generates tag name as <Project>-v<Version>.

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

### -IsPreRelease
Publish the release as a pre-release.

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

### -ProjectPath
Path to the project folder containing the *.csproj file.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ReleaseName
Optional release name override (defaults to TagName).

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

### -TagName
Optional tag name override.

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

### -TagTemplate
Optional tag template (supports {Project} and {Version}).

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

### -Version
Optional version override (otherwise read from VersionPrefix).

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

### -ZipPath
Optional zip path override (defaults to bin/Release/<Project>.<Version>.zip).

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None

