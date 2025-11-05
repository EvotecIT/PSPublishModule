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
 [-GitHubAccessToken] <String> [-IsPreRelease] [[-Version] <String>] [[-TagName] <String>]
 [[-TagTemplate] <String>] [[-ReleaseName] <String>] [-IncludeProjectNameInTag] [[-ZipPath] <String>]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
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
{{ Fill Version Description }}

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
{{ Fill TagName Description }}

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

### -TagTemplate
{{ Fill TagTemplate Description }}

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

### -ReleaseName
{{ Fill ReleaseName Description }}

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

### -IncludeProjectNameInTag
{{ Fill IncludeProjectNameInTag Description }}

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

### -ZipPath
{{ Fill ZipPath Description }}

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
