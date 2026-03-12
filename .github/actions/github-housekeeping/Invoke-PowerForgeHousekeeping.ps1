[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('runner', 'caches', 'artifacts')]
    [string] $Mode
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $env:GITHUB_ACTION_PATH "../..")).Path
$project = Join-Path $repoRoot "PowerForge.Cli/PowerForge.Cli.csproj"

function Add-ApplyMode {
    param([System.Collections.Generic.List[string]] $Arguments)

    if ($env:INPUT_APPLY -eq 'true') {
        $null = $Arguments.Add('--apply')
    } else {
        $null = $Arguments.Add('--dry-run')
    }
}

function Add-OptionalPair {
    param(
        [System.Collections.Generic.List[string]] $Arguments,
        [string] $Option,
        [string] $Value
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $null = $Arguments.Add($Option)
        $null = $Arguments.Add($Value)
    }
}

function Resolve-Repository {
    if (-not [string]::IsNullOrWhiteSpace($env:INPUT_REPO)) {
        return $env:INPUT_REPO
    }

    return $env:GITHUB_REPOSITORY
}

function Resolve-Token {
    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_GITHUB_TOKEN)) {
        return $env:POWERFORGE_GITHUB_TOKEN
    }

    throw 'GitHub token is required for remote GitHub housekeeping.'
}

$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.AddRange(@('run', '--project', $project, '-c', 'Release', '--no-build', '--'))

switch ($Mode) {
    'runner' {
        $arguments.AddRange(@('github', 'runner', 'cleanup'))
        Add-ApplyMode -Arguments $arguments
        Add-OptionalPair -Arguments $arguments -Option '--min-free-gb' -Value $env:INPUT_MIN_FREE_GB
        Add-OptionalPair -Arguments $arguments -Option '--aggressive-threshold-gb' -Value $env:INPUT_RUNNER_AGGRESSIVE_THRESHOLD_GB

        if ($env:INPUT_ALLOW_SUDO -eq 'true') {
            $null = $arguments.Add('--allow-sudo')
        }
    }
    'caches' {
        $repository = Resolve-Repository
        $token = Resolve-Token

        $arguments.AddRange(@(
            'github', 'caches', 'prune',
            '--repo', $repository,
            '--token', $token,
            '--keep', $env:INPUT_CACHE_KEEP,
            '--max-age-days', $env:INPUT_CACHE_MAX_AGE_DAYS,
            '--max-delete', $env:INPUT_CACHE_MAX_DELETE
        ))

        Add-ApplyMode -Arguments $arguments
        Add-OptionalPair -Arguments $arguments -Option '--key' -Value $env:INPUT_CACHE_KEY
        Add-OptionalPair -Arguments $arguments -Option '--exclude' -Value $env:INPUT_CACHE_EXCLUDE
    }
    'artifacts' {
        $repository = Resolve-Repository
        $token = Resolve-Token

        $arguments.AddRange(@(
            'github', 'artifacts', 'prune',
            '--repo', $repository,
            '--token', $token,
            '--keep', $env:INPUT_ARTIFACT_KEEP,
            '--max-age-days', $env:INPUT_ARTIFACT_MAX_AGE_DAYS,
            '--max-delete', $env:INPUT_ARTIFACT_MAX_DELETE
        ))

        Add-ApplyMode -Arguments $arguments
        Add-OptionalPair -Arguments $arguments -Option '--name' -Value $env:INPUT_ARTIFACT_NAME
        Add-OptionalPair -Arguments $arguments -Option '--exclude' -Value $env:INPUT_ARTIFACT_EXCLUDE
    }
}

dotnet $arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
