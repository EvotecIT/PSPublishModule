function Assert-PowerForgeCommittedReleaseVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [ValidatePattern('^\d+\.\d+\.\d+$')]
        [string] $Version,

        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig
    )

    $manifestPath = Join-Path (Join-Path $RepositoryRoot 'Module') 'PSPublishModule.psd1'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "The committed module manifest is unavailable: $manifestPath"
    }
    $manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
    if ([string] $manifest.ModuleVersion -ne $Version) {
        throw "Publish requires committed module version '$Version'; the manifest contains '$($manifest.ModuleVersion)'."
    }

    $tracks = @($ReleaseConfig.Packages.VersionTracks.PSObject.Properties)
    if ($tracks.Count -eq 0) {
        throw 'The release configuration does not declare any package version tracks.'
    }

    $projectNames = foreach ($track in $tracks) {
        [string] $track.Value.AnchorProject
        @($track.Value.Projects) | ForEach-Object { [string] $_ }
    }
    $projectNames = @($projectNames |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    if ($projectNames.Count -eq 0) {
        throw 'The release configuration does not resolve any package projects.'
    }

    foreach ($projectName in $projectNames) {
        if ($projectName -notmatch '^[A-Za-z0-9_.-]+$') {
            throw "Release project name contains unsupported characters: $projectName"
        }
        $projectPath = Join-Path (Join-Path $RepositoryRoot $projectName) "$projectName.csproj"
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "The committed release project is unavailable: $projectPath"
        }

        [xml] $project = Get-Content -Raw -LiteralPath $projectPath
        $committedVersions = @($project.Project.PropertyGroup |
            ForEach-Object { [string] $_.VersionPrefix } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique)
        if ($committedVersions.Count -ne 1 -or $committedVersions[0] -ne $Version) {
            $actual = if ($committedVersions.Count -eq 0) { '<missing>' } else { $committedVersions -join ', ' }
            throw "Publish requires committed project version '$Version' in '$projectPath'; found '$actual'."
        }
    }
}
