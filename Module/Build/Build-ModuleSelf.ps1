# Self-build script for PSPublishModule.
# Builds PowerForge.Cli and runs `powerforge pipeline` using repo config discovery (`powerforge.json`).
[CmdletBinding()]
param(
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $Json
)

$repoRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..')))).Path
$cliProject = Join-Path -Path $repoRoot -ChildPath 'PowerForge.Cli\PowerForge.Cli.csproj'

if (-not (Test-Path -LiteralPath $cliProject)) { throw "PowerForge.Cli project not found: $cliProject" }

if ($Framework -eq 'auto') {
    $runtimesText = (dotnet --list-runtimes 2>$null) -join "`n"
    $Framework = if ($runtimesText -match '(?m)^Microsoft\\.NETCore\\.App\\s+10\\.') { 'net10.0' } else { 'net8.0' }
}

if (-not $NoBuild) {
    dotnet build $cliProject -c $Configuration -f $Framework
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$cliDir = Join-Path -Path $repoRoot -ChildPath ("PowerForge.Cli\\bin\\{0}\\{1}" -f $Configuration, $Framework)
$cliExe = Join-Path -Path $cliDir -ChildPath 'PowerForge.Cli.exe'
$cliDll = Join-Path -Path $cliDir -ChildPath 'PowerForge.Cli.dll'

$cmd = @('pipeline', '--project-root', $repoRoot)
if ($Json) { $cmd += @('--output', 'json') }

if ([IO.Path]::DirectorySeparatorChar -eq '\' -and (Test-Path -LiteralPath $cliExe)) {
    & $cliExe @cmd
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $cliDll)) { throw "CLI build output not found: $cliDll" }
dotnet $cliDll @cmd
exit $LASTEXITCODE
