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

    $configurationProperty =
        $ReleaseConfig.Packages.PSObject.Properties['Configuration']
    $configuration = if ($null -eq $configurationProperty) {
        $null
    } else {
        [string] $configurationProperty.Value
    }
    if ([string]::IsNullOrWhiteSpace($configuration)) {
        $configuration = 'Release'
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

        $evaluation = Get-PowerForgeEvaluatedProjectProperties `
            -ProjectPath $projectPath `
            -Configuration $configuration
        $targetFrameworks = @(
            ([string] $evaluation.TargetFrameworks).Split(
                [char[]] @(';'),
                [StringSplitOptions]::RemoveEmptyEntries) |
                ForEach-Object { $_.Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        $packageId = [string] $evaluation.PackageId
        $frameworkPackageIds = if ($targetFrameworks.Count -eq 0) {
            @()
        } else {
            @(
                foreach ($targetFramework in $targetFrameworks) {
                    $frameworkEvaluation = Get-PowerForgeEvaluatedProjectProperties `
                        -ProjectPath $projectPath `
                        -Configuration $configuration `
                        -TargetFramework $targetFramework
                    [string] $frameworkEvaluation.PackageId
                }
            )
        }
        $distinctFrameworkPackageIds = @(
            $frameworkPackageIds |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Sort-Object -Unique
        )
        if ($distinctFrameworkPackageIds.Count -gt 1) {
            $reportedIds = if ($distinctFrameworkPackageIds.Count -eq 0) {
                '<missing>'
            } else {
                $distinctFrameworkPackageIds -join ', '
            }
            throw "Release project '$projectPath' must evaluate to exactly one package ID across all target frameworks; found '$reportedIds'."
        }
        if ([string]::IsNullOrWhiteSpace($packageId)) {
            throw "Release project '$projectPath' did not evaluate a package-level package ID."
        }
        if ($packageId -notmatch '^[A-Za-z0-9_.-]+$') {
            throw "Release project '$projectPath' resolved unsafe package ID '$packageId'."
        }
        $packageId
    }

    @($packageIds | Sort-Object -Unique)
}

function Get-PowerForgeEvaluatedProjectProperties {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath,

        [Parameter(Mandatory)]
        [string] $Configuration,

        [string] $TargetFramework
    )

    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    $arguments = @(
        'msbuild',
        $ProjectPath,
        '-nologo',
        "-property:Configuration=$Configuration"
    )
    if (-not [string]::IsNullOrWhiteSpace($TargetFramework)) {
        $arguments += "-property:TargetFramework=$TargetFramework"
    }
    $arguments += @(
        '-getProperty:PackageId',
        '-getProperty:TargetFrameworks'
    )

    $output = & $dotnet.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild package identity evaluation failed for '$ProjectPath' with exit code $LASTEXITCODE."
    }
    $result = ConvertFrom-PowerForgeMsBuildPropertyOutput `
        -Output $output `
        -ProjectPath $ProjectPath
    $propertiesProperty = $result.PSObject.Properties['Properties']
    if ($null -eq $propertiesProperty -or $null -eq $propertiesProperty.Value) {
        throw "MSBuild package identity evaluation returned no properties for '$ProjectPath'."
    }
    $properties = $propertiesProperty.Value
    $packageIdProperty = $properties.PSObject.Properties['PackageId']
    $targetFrameworksProperty = $properties.PSObject.Properties['TargetFrameworks']
    [pscustomobject] @{
        PackageId = if ($null -eq $packageIdProperty) {
            $null
        } else {
            [string] $packageIdProperty.Value
        }
        TargetFrameworks = if ($null -eq $targetFrameworksProperty) {
            $null
        } else {
            [string] $targetFrameworksProperty.Value
        }
    }
}

function ConvertFrom-PowerForgeMsBuildPropertyOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]] $Output,

        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    $text = @($Output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
    $jsonStart = $text.IndexOf('{')
    $jsonEnd = $text.LastIndexOf('}')
    if ($jsonStart -lt 0 -or $jsonEnd -lt $jsonStart) {
        throw "MSBuild package identity evaluation returned no JSON payload for '$ProjectPath'."
    }
    $json = $text.Substring($jsonStart, $jsonEnd - $jsonStart + 1)
    try {
        $json | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "MSBuild package identity evaluation returned invalid output for '$ProjectPath': $($_.Exception.Message)"
    }
}
