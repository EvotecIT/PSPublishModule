# Self-build script for PSPublishModule.
# Builds PowerForge.Cli and runs `powerforge pipeline` using repo config discovery (`powerforge.json`).
[CmdletBinding()]
param(
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $Json,
    [switch] $NoSign,
    [string] $CertificateThumbprint = '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703',
    [switch] $SignIncludeBinaries
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
try {
    $pipelineConfig = Join-Path -Path $repoRoot -ChildPath 'powerforge.json'
    if (-not $NoSign -and $Env:COMPUTERNAME -eq 'EVOMONSTER' -and (Test-Path -LiteralPath $pipelineConfig)) {
        $spec = Get-Content -LiteralPath $pipelineConfig -Raw | ConvertFrom-Json -Depth 50
        if (-not $spec.Segments) { $spec | Add-Member -MemberType NoteProperty -Name Segments -Value @() }

        $signing = [ordered]@{ CertificateThumbprint = $CertificateThumbprint }
        if ($SignIncludeBinaries.IsPresent) { $signing.IncludeBinaries = $true }

        $spec.Segments += @(
            [ordered]@{ Type = 'Build'; BuildModule = [ordered]@{ SignMerged = $true } },
            [ordered]@{ Type = 'Options'; Options = [ordered]@{ Signing = $signing } }
        )

        # Keep the generated config in the repo root so relative paths (e.g. "Module") resolve correctly.
        $configPath = Join-Path -Path $repoRoot -ChildPath ("powerforge.pipeline.self.{0}.json" -f [Guid]::NewGuid().ToString('N'))
        $spec | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    $cmd = @('pipeline', '--project-root', $repoRoot)
    if ($configPath) { $cmd += @('--config', $configPath) }
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
