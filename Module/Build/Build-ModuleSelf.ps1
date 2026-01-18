# Self-build script for PSPublishModule.
# Builds PowerForge.Cli and runs `powerforge pipeline` using the same configuration as Build-Module.ps1.
[CmdletBinding()]
param(
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $Json,
    [switch] $NoSign,
    [switch] $SignModule,
    [string] $ModuleVersion,
    [string] $PreReleaseTag,
    [string] $CertificateThumbprint = '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703',
    [switch] $SignIncludeBinaries,
    [switch] $SignIncludeInternals,
    [switch] $SignIncludeExe
)

$repoRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..')))).Path
$cliProject = Join-Path -Path $repoRoot -ChildPath 'PowerForge.Cli\PowerForge.Cli.csproj'

if (-not (Test-Path -LiteralPath $cliProject)) { throw "PowerForge.Cli project not found: $cliProject" }

if ($Framework -eq 'auto') {
    $runtimesText = (dotnet --list-runtimes 2>$null) -join "`n"
    $Framework = if ($runtimesText -match '(?m)^Microsoft\\.NETCore\\.App\\s+10\\.') { 'net10.0' } else { 'net8.0' }
}

if (-not $NoBuild) {
    Write-Host "ℹ️ Building PowerForge CLI ($Framework, $Configuration)" -ForegroundColor DarkGray

    $buildArgs = @('build', $cliProject, '-c', $Configuration, '-f', $Framework, '--nologo')
    if ($PSBoundParameters.ContainsKey('Verbose')) {
        $buildArgs += @('--verbosity', 'minimal')
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } else {
        $buildArgs += @('--verbosity', 'quiet')
        $buildOutput = & dotnet @buildArgs 2>&1
        if ($LASTEXITCODE -ne 0) { $buildOutput | Out-Host; exit $LASTEXITCODE }
        Write-Host "✅ Built PowerForge CLI ($Framework, $Configuration)" -ForegroundColor Green
    }
}

$cliDir = Join-Path -Path $repoRoot -ChildPath ("PowerForge.Cli\\bin\\{0}\\{1}" -f $Configuration, $Framework)
$cliExe = Join-Path -Path $cliDir -ChildPath 'PowerForge.Cli.exe'
$cliDll = Join-Path -Path $cliDir -ChildPath 'PowerForge.Cli.dll'

$configPath = $null
$buildScript = Join-Path -Path $repoRoot -ChildPath 'Module\Build\Build-Module.ps1'
if (-not (Test-Path -LiteralPath $buildScript)) { throw "Build-Module.ps1 not found: $buildScript" }
try {
    # Keep the generated config in the repo root so relative paths (e.g. "Module") resolve correctly.
    $configPath = Join-Path -Path $repoRoot -ChildPath ("powerforge.pipeline.self.{0}.json" -f [Guid]::NewGuid().ToString('N'))
    $buildArgs = @{
        JsonOnly       = $true
        JsonPath       = $configPath
        Configuration  = $Configuration
        NoDotnetBuild  = $true
    }
    if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $buildArgs.ModuleVersion = $ModuleVersion }
    if ($PSBoundParameters.ContainsKey('PreReleaseTag')) { $buildArgs.PreReleaseTag = $PreReleaseTag }
    if ($PSBoundParameters.ContainsKey('NoSign')) { $buildArgs.NoSign = $NoSign.IsPresent }
    if ($PSBoundParameters.ContainsKey('SignModule')) { $buildArgs.SignModule = $SignModule.IsPresent }
    if ($PSBoundParameters.ContainsKey('CertificateThumbprint')) { $buildArgs.CertificateThumbprint = $CertificateThumbprint }
    if ($PSBoundParameters.ContainsKey('SignIncludeBinaries')) { $buildArgs.SignIncludeBinaries = $SignIncludeBinaries.IsPresent }
    if ($PSBoundParameters.ContainsKey('SignIncludeInternals')) { $buildArgs.SignIncludeInternals = $SignIncludeInternals.IsPresent }
    if ($PSBoundParameters.ContainsKey('SignIncludeExe')) { $buildArgs.SignIncludeExe = $SignIncludeExe.IsPresent }

    & $buildScript @buildArgs
    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Build configuration was not generated: $configPath"
    }

    $cmd = @('pipeline', '--project-root', $repoRoot)
    $cmd += @('--config', $configPath)
    if ($Json) { $cmd += @('--output', 'json') }

    if ([IO.Path]::DirectorySeparatorChar -eq '\' -and (Test-Path -LiteralPath $cliExe)) {
        & $cliExe @cmd
        exit $LASTEXITCODE
    }

    if (-not (Test-Path -LiteralPath $cliDll)) { throw "CLI build output not found: $cliDll" }
    dotnet $cliDll @cmd
    exit $LASTEXITCODE
} finally {
    if ($configPath -and (Test-Path -LiteralPath $configPath)) {
        try { Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue } catch { }
    }
}
