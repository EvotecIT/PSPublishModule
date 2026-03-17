[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoBuild,
    [switch] $NoRestore
)

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $runningOnWindows) {
    throw 'PowerForge Studio WPF can only run on Windows.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wpfProject = Join-Path $repoRoot 'PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj'

$dotnetArguments = @(
    'run',
    '--project', $wpfProject,
    '-c', $Configuration,
    # Keep this aligned with the WPF target framework in PowerForgeStudio.Wpf.csproj.
    '--framework', 'net10.0-windows'
)
if ($NoBuild) {
    $dotnetArguments += '--no-build'
}
if ($NoRestore) {
    $dotnetArguments += '--no-restore'
}

Write-Host ("dotnet " + ($dotnetArguments -join ' ')) -ForegroundColor Cyan
& dotnet @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
