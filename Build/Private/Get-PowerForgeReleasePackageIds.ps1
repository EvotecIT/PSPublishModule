function Get-PowerForgeReleasePackageIds {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $tracks = $ReleaseConfig.Packages.VersionTracks
    if ($null -eq $tracks) {
        throw 'The release configuration does not define package version tracks.'
    }

    $projectNames = foreach ($track in @($tracks.PSObject.Properties)) {
        if (-not [string]::IsNullOrWhiteSpace([string] $track.Value.AnchorProject)) {
            [string] $track.Value.AnchorProject
        }
        foreach ($project in @($track.Value.Projects)) {
            if (-not [string]::IsNullOrWhiteSpace([string] $project)) {
                [string] $project
            }
        }
    }

    $packageIds = foreach ($projectName in @($projectNames | Select-Object -Unique)) {
        $relativeProjectPath = if ($projectName.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
            $projectName
        } else {
            $leaf = Split-Path -Leaf $projectName.TrimEnd([char[]] @('/', '\'))
            Join-Path $projectName "$leaf.csproj"
        }
        $projectPath = Join-Path $RepositoryRoot $relativeProjectPath
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Configured release project was not found: $projectPath"
        }

        [xml] $projectXml = Get-Content -Raw -LiteralPath $projectPath
        $packageId = @($projectXml.Project.PropertyGroup.PackageId) |
            ForEach-Object { [string] $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($packageId)) {
            $packageId = [IO.Path]::GetFileNameWithoutExtension($projectPath)
        }
        if ($packageId -notmatch '^[A-Za-z0-9_.-]+$') {
            throw "Release project '$projectPath' resolved unsafe package ID '$packageId'."
        }
        $packageId
    }

    @($packageIds | Sort-Object -Unique)
}
