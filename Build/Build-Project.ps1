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

$dotnetArgs = @(
    'run', '--project', $project, '-c', 'Release', '--framework', 'net10.0', '--no-launch-profile', '--',
    'release', '--config', $ConfigPath
)
if ($Plan) { $dotnetArgs += '--plan' }
if ($Validate) { $dotnetArgs += '--validate' }
if ($PackagesOnly) { $dotnetArgs += '--packages-only' }
if ($ToolsOnly) { $dotnetArgs += '--tools-only' }
if ($PublishNuget) { $dotnetArgs += '--publish-nuget' }
if ($PublishGitHub) { $dotnetArgs += '--publish-project-github' }
if ($PublishToolGitHub) { $dotnetArgs += '--publish-tool-github' }
foreach ($entry in $Target) { $dotnetArgs += @('--target', $entry) }
foreach ($entry in $Runtime) { $dotnetArgs += @('--rid', $entry) }
foreach ($entry in $Framework) { $dotnetArgs += @('--framework', $entry) }
foreach ($entry in $Flavor) { $dotnetArgs += @('--flavor', $entry) }

dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
