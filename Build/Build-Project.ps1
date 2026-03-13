param(
    [string] $ConfigPath,
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PackagesOnly,
    [switch] $ToolsOnly,
    [switch] $PublishNuget,
    [switch] $PublishGitHub,
    [switch] $PublishToolGitHub,
    [string[]] $Target,
    [string[]] $Runtime,
    [string[]] $Framework,
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string[]] $Flavor
)

if (-not $PSBoundParameters.ContainsKey('ConfigPath') -or [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.json'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'PowerForge.Cli\PowerForge.Cli.csproj'

$args = @(
    'run', '--project', $project, '-c', 'Release', '--framework', 'net10.0', '--no-launch-profile', '--',
    'release', '--config', $ConfigPath
)
if ($Plan) { $args += '--plan' }
if ($Validate) { $args += '--validate' }
if ($PackagesOnly) { $args += '--packages-only' }
if ($ToolsOnly) { $args += '--tools-only' }
if ($PublishNuget) { $args += '--publish-nuget' }
if ($PublishGitHub) { $args += '--publish-project-github' }
if ($PublishToolGitHub) { $args += '--publish-tool-github' }
foreach ($entry in $Target) { $args += @('--target', $entry) }
foreach ($entry in $Runtime) { $args += @('--rid', $entry) }
foreach ($entry in $Framework) { $args += @('--framework', $entry) }
foreach ($entry in $Flavor) { $args += @('--flavor', $entry) }

dotnet @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
