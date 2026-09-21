[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoBuild,
    [switch] $NoRestore,
    [string] $Workspace,
    [switch] $LegacyWpf
)

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
if ($LegacyWpf -and -not $runningOnWindows) {
    throw 'The legacy PowerForge Studio WPF host can only run on Windows.'
}
if ($LegacyWpf -and -not [string]::IsNullOrWhiteSpace($Workspace)) {
    throw 'The legacy WPF host cannot honor -Workspace. Omit -LegacyWpf to open that workspace in Avalonia.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = if ($LegacyWpf) {
    Join-Path $repoRoot 'PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj'
} else {
    Join-Path $repoRoot 'PowerForgeStudio.Avalonia\PowerForgeStudio.Avalonia.csproj'
}
$framework = if ($LegacyWpf) { 'net10.0-windows' } else { 'net10.0' }

$dotnetArguments = @(
    'run',
    '--project', $project,
    '-c', $Configuration,
    '--framework', $framework
)
if ($NoBuild) {
    $dotnetArguments += '--no-build'
}
if ($NoRestore) {
    $dotnetArguments += '--no-restore'
}
if (-not $LegacyWpf -and -not [string]::IsNullOrWhiteSpace($Workspace)) {
    $workspaceRoot = (Resolve-Path -LiteralPath $Workspace -ErrorAction Stop).Path
    $dotnetArguments += @('--', '--workspace', $workspaceRoot)
}

Write-Host ("dotnet " + ($dotnetArguments -join ' ')) -ForegroundColor Cyan
if (-not $PSCmdlet.ShouldProcess($project, "Run PowerForge Studio with dotnet")) {
    return
}
& dotnet @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
