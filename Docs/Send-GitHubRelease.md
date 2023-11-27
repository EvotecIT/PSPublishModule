Send-GitHubRelease
------------------

### Synopsis
Creates a new Release for the given GitHub repository.

---

### Description

Uses the GitHub API to create a new Release for a given repository.
Allows you to specify all of the Release properties, such as the Tag, Name, Assets, and if it's a Draft or Prerelease or not.

---

### Related Links
* [Project home: https://github.com/deadlydog/New-GitHubRelease](Project home: https://github.com/deadlydog/New-GitHubRelease)

---

### Examples
Import the module dynamically from the PowerShell Gallery. Use CurrentUser scope to avoid having to run as admin.

```PowerShell
Import-Module -Name New-GitHubRelease -Scope CurrentUser
# Specify the parameters required to create the release. Do it as a hash table for easier readability.
$newGitHubReleaseParameters =
@{
    GitHubUsername = 'deadlydog'
    GitHubRepositoryName = 'New-GitHubRelease'
    GitHubAccessToken = 'SomeLongHexidecimalString'
    ReleaseName = "New-GitHubRelease v1.0.0"
    TagName = "v1.0.0"
    ReleaseNotes = "This release contains the following changes: ..."
    AssetFilePaths = @('C:\MyProject\Installer.exe','C:\MyProject\Documentation.md')
    IsPreRelease = $false
    IsDraft = $true	# Set to true when testing so we don't publish a real release (visible to everyone) by accident.
}

# Try to create the Release on GitHub and save the results.
$result = New-GitHubRelease @newGitHubReleaseParameters

# Provide some feedback to the user based on the results.
if ($result.Succeeded -eq $true)
{
    Write-Output "Release published successfully! View it at $($result.ReleaseUrl)"
}
elseif ($result.ReleaseCreationSucceeded -eq $false)
{
    Write-Error "The release was not created. Error message is: $($result.ErrorMessage)"
}
elseif ($result.AllAssetUploadsSucceeded -eq $false)
{
    Write-Error "The release was created, but not all of the assets were uploaded to it. View it at $($result.ReleaseUrl). Error message is: $($result.ErrorMessage)"
}

Attempt to create a new Release on GitHub, and provide feedback to the user indicating if it succeeded or not.
```

---

### Parameters
#### **GitHubUsername**
The username that the GitHub repository exists under.
e.g. For the repository https://github.com/deadlydog/New-GitHubRelease, the username is 'deadlydog'.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |1       |false        |

#### **GitHubRepositoryName**
The name of the repository to create the Release for.
e.g. For the repository https://github.com/deadlydog/New-GitHubRelease, the repository name is 'New-GitHubRelease'.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |2       |false        |

#### **GitHubAccessToken**
The Access Token to use as credentials for GitHub.
Access tokens can be generated at https://github.com/settings/tokens.
The access token will need to have the repo/public_repo permission on it for it to be allowed to create a new Release.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |3       |false        |

#### **TagName**
The name of the tag to create at the Commitish.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |4       |false        |

#### **ReleaseName**
The name to use for the new release.
If blank, the TagName will be used.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |5       |false        |

#### **ReleaseNotes**
The text describing the contents of the release.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |6       |false        |

#### **AssetFilePaths**
The full paths of the files to include in the release.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |7       |false        |

#### **Commitish**
Specifies the commitish value that determines where the Git tag is created from.
Can be any branch or commit SHA. Unused if the Git tag already exists.
Default: the repository's default branch (usually master).

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |8       |false        |

#### **IsDraft**
True to create a draft (unpublished) release, false to create a published one.
Default: false

|Type       |Required|Position|PipelineInput|
|-----------|--------|--------|-------------|
|`[Boolean]`|false   |9       |false        |

#### **IsPreRelease**
True to identify the release as a prerelease. false to identify the release as a full release.
Default: false

|Type       |Required|Position|PipelineInput|
|-----------|--------|--------|-------------|
|`[Boolean]`|false   |10      |false        |

---

### Outputs
* A hash table with the following properties is returned:

Succeeded = $true if the Release was created successfully and all assets were uploaded to it, $false if some part of the process failed.
ReleaseCreationSucceeded = $true if the Release was created successfully (does not include asset uploads), $false if the Release was not created.
AllAssetUploadsSucceeded = $true if all assets were uploaded to the Release successfully, $false if one of them failed, $null if there were no assets to upload.
ReleaseUrl = The URL of the new Release that was created.
ErrorMessage = A message describing what went wrong in the case that Succeeded is $false.

---

### Notes
Name:   New-GitHubRelease
Author: Daniel Schroeder (originally based on the script at https://github.com/majkinetor/au/blob/master/scripts/Github-CreateRelease.ps1)
GitHub Release API Documentation: https://developer.github.com/v3/repos/releases/#create-a-release
Version: 1.0.2

---

### Syntax
```PowerShell
Send-GitHubRelease [-GitHubUsername] <String> [-GitHubRepositoryName] <String> [-GitHubAccessToken] <String> [-TagName] <String> [[-ReleaseName] <String>] [[-ReleaseNotes] <String>] [[-AssetFilePaths] <String[]>] [[-Commitish] <String>] [[-IsDraft] <Boolean>] [[-IsPreRelease] <Boolean>] [<CommonParameters>]
```
