param(
    $GitHubUsername,
    $GitHubRepositoryName,
    $GitHubAccessToken,
    $TagName,
    $ReleaseName,
    $AssetFilePaths,
    $IsPreRelease,
    $GenerateReleaseNotes,
    $ReuseExistingReleaseOnConflict = $true
)

Send-GitHubRelease `
    -GitHubUsername $GitHubUsername `
    -GitHubRepositoryName $GitHubRepositoryName `
    -GitHubAccessToken $GitHubAccessToken `
    -TagName $TagName `
    -ReleaseName $ReleaseName `
    -AssetFilePaths $AssetFilePaths `
    -IsPreRelease:$IsPreRelease `
    -GenerateReleaseNotes:$GenerateReleaseNotes `
    -ReuseExistingReleaseOnConflict:$ReuseExistingReleaseOnConflict

