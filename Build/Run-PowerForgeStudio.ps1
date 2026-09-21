[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoBuild,
    [switch] $NoRestore,
    [string] $Workspace
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'PowerForgeStudio.Avalonia\PowerForgeStudio.Avalonia.csproj'

$dotnetArguments = @(
    'run',
    '--project', $project,
    '-c', $Configuration,
    '--framework', 'net10.0'
)
if ($NoBuild) {
    $dotnetArguments += '--no-build'
}
if ($NoRestore) {
    $dotnetArguments += '--no-restore'
}
if (-not [string]::IsNullOrWhiteSpace($Workspace)) {
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
