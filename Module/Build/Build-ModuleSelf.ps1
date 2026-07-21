# Self-build script for PSPublishModule.
# Builds PowerForge.Cli and runs `powerforge pipeline` using the same configuration as Build-Module.ps1.
[CmdletBinding()]
param(
    [Alias('ConfigurationGateMode')]
    [ValidateSet('Manifest', 'Documentation', 'Build', 'Publish')]
    [string] $RunMode = 'Build',
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $Json,
    [switch] $NoSign,
    [switch] $SignModule,
    [string] $ModuleVersion,
    [string] $PreReleaseTag,
    [string] $CertificateThumbprint = '92e95fb58effa6a4a75e77a33cdd6bfe6dd30f1a',
    [switch] $SignIncludeBinaries,
    [switch] $SignIncludeInternals,
    [switch] $SignIncludeExe,
    [string] $DiagnosticsBaselinePath,
    [switch] $GenerateDiagnosticsBaseline,
    [switch] $UpdateDiagnosticsBaseline,
    [switch] $FailOnNewDiagnostics,
    [ValidateSet('Warning', 'Error')]
    [string] $FailOnDiagnosticsSeverity
)

$oldConsoleOutputEncoding = $null
$oldConsoleInputEncoding = $null
$oldConsoleCodePage = $null

$i = [char]0x2139    # ℹ
$ok = [char]0x2705   # ✅

try {
    # When PowerForge.Cli.exe is invoked from Windows PowerShell, native stdout decoding frequently
    # breaks for Unicode unless the console is switched to UTF-8. Keep this local to self-build.
    if (-not [Console]::IsOutputRedirected -and -not [Console]::IsErrorRedirected) {
        $oldConsoleOutputEncoding = [Console]::OutputEncoding
        $oldConsoleInputEncoding = [Console]::InputEncoding
        $oldConsoleCodePage = $oldConsoleOutputEncoding.CodePage

        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [Console]::OutputEncoding = $utf8
        [Console]::InputEncoding = $utf8

        # Switch the console codepage too (PowerShell native output decoding depends on it).
        & cmd /c "chcp 65001 > nul" | Out-Null
    }
} catch {
    # best effort only
}

$repoRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..')))).Path
$cliProject = Join-Path -Path $repoRoot -ChildPath 'PowerForge.Cli\PowerForge.Cli.csproj'
$moduleProject = Join-Path -Path $repoRoot -ChildPath 'PSPublishModule\PSPublishModule.csproj'

if (-not (Test-Path -LiteralPath $cliProject)) { throw "PowerForge.Cli project not found: $cliProject" }
if (-not (Test-Path -LiteralPath $moduleProject)) { throw "PSPublishModule project not found: $moduleProject" }

if ($Framework -eq 'auto') {
    # Choose the binary that this PowerShell host can load, not merely the
    # newest .NET runtime installed for dotnet.exe.
    $Framework = if ($PSVersionTable.PSEdition -eq 'Core' -and [Environment]::Version.Major -ge 10) { 'net10.0' } else { 'net8.0' }
}

if (-not $NoBuild) {
    Write-Host "$i Building PowerForge CLI ($Framework, $Configuration)" -ForegroundColor DarkGray

    $buildArgs = @('build', $cliProject, '-c', $Configuration, '-f', $Framework, '--nologo')
    if ($PSBoundParameters.ContainsKey('Verbose')) {
        $buildArgs += @('--verbosity', 'minimal')
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } else {
        $buildArgs += @('--verbosity', 'quiet')
        $buildOutput = & dotnet @buildArgs 2>&1
        if ($LASTEXITCODE -ne 0) { $buildOutput | Out-Host; exit $LASTEXITCODE }
        Write-Host "$ok Built PowerForge CLI ($Framework, $Configuration)" -ForegroundColor Green
    }

    Write-Host "$i Building PSPublishModule ($Framework, $Configuration)" -ForegroundColor DarkGray
    $moduleArgs = @('build', $moduleProject, '-c', $Configuration, '-f', $Framework, '--nologo')
    if ($PSBoundParameters.ContainsKey('Verbose')) {
        $moduleArgs += @('--verbosity', 'minimal')
        & dotnet @moduleArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } else {
        $moduleArgs += @('--verbosity', 'quiet')
        $moduleOutput = & dotnet @moduleArgs 2>&1
        if ($LASTEXITCODE -ne 0) { $moduleOutput | Out-Host; exit $LASTEXITCODE }
        Write-Host "$ok Built PSPublishModule ($Framework, $Configuration)" -ForegroundColor Green
    }
}

$buildScript = Join-Path -Path $repoRoot -ChildPath 'Module\Build\Build-Module.ps1'
if (-not (Test-Path -LiteralPath $buildScript)) { throw "Build-Module.ps1 not found: $buildScript" }

$cliDir = Join-Path -Path $repoRoot -ChildPath ("PowerForge.Cli\\bin\\{0}\\{1}" -f $Configuration, $Framework)
$cliSourceDll = Join-Path -Path $cliDir -ChildPath 'PowerForge.Cli.dll'

if (-not (Test-Path -LiteralPath $cliSourceDll)) {
    throw "CLI build output not found: $cliSourceDll"
}

# The coordinated build can rebuild/package PowerForge.Cli itself. Run the coordinator
# from a temporary copy so Windows does not lock the source output that it must replace.
$cliHostDir = Join-Path -Path ([IO.Path]::GetTempPath()) -ChildPath ("PowerForge.Cli.self-build.{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $cliHostDir -Force | Out-Null
    $oldProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Copy-Item -Path (Join-Path $cliDir '*') -Destination $cliHostDir -Recurse -Force
    } finally {
        $ProgressPreference = $oldProgressPreference
    }
} catch {
    if (Test-Path -LiteralPath $cliHostDir) {
        Remove-Item -LiteralPath $cliHostDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}
$cliExe = Join-Path -Path $cliHostDir -ChildPath 'PowerForge.Cli.exe'
$cliDll = Join-Path -Path $cliHostDir -ChildPath 'PowerForge.Cli.dll'

$configPath = $null
try {
    if ($RunMode -eq 'Publish') {
        $releaseConfig = Join-Path -Path $repoRoot -ChildPath 'Build/release.json'
        if (-not (Test-Path -LiteralPath $releaseConfig)) {
            throw "Unified release configuration not found: $releaseConfig"
        }

        $cmd = @(
            'release',
            '--config', $releaseConfig,
            '--configuration', $Configuration,
            '--module-framework', $Framework,
            '--module-run-mode', 'Publish',
            '--module-no-dotnet-build'
        )
        if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $cmd += @('--module-version', $ModuleVersion) }
        if ($PSBoundParameters.ContainsKey('PreReleaseTag')) { $cmd += @('--module-prerelease-tag', $PreReleaseTag) }
        if ($NoSign) { $cmd += '--module-no-sign' }
        if ($SignModule) { $cmd += '--module-sign' }
        if ($PSBoundParameters.ContainsKey('CertificateThumbprint')) { $cmd += @('--module-certificate-thumbprint', $CertificateThumbprint) }
        if ($PSBoundParameters.ContainsKey('SignIncludeBinaries')) { $cmd += $(if ($SignIncludeBinaries) { '--module-sign-include-binaries' } else { '--module-no-sign-include-binaries' }) }
        if ($PSBoundParameters.ContainsKey('SignIncludeInternals')) { $cmd += $(if ($SignIncludeInternals) { '--module-sign-include-internals' } else { '--module-no-sign-include-internals' }) }
        if ($PSBoundParameters.ContainsKey('SignIncludeExe')) { $cmd += $(if ($SignIncludeExe) { '--module-sign-include-exe' } else { '--module-no-sign-include-exe' }) }
        if ($PSBoundParameters.ContainsKey('DiagnosticsBaselinePath')) { $cmd += @('--module-diagnostics-baseline', $DiagnosticsBaselinePath) }
        if ($PSBoundParameters.ContainsKey('GenerateDiagnosticsBaseline')) { $cmd += $(if ($GenerateDiagnosticsBaseline) { '--module-diagnostics-baseline-generate' } else { '--module-no-diagnostics-baseline-generate' }) }
        if ($PSBoundParameters.ContainsKey('UpdateDiagnosticsBaseline')) { $cmd += $(if ($UpdateDiagnosticsBaseline) { '--module-diagnostics-baseline-update' } else { '--module-no-diagnostics-baseline-update' }) }
        if ($PSBoundParameters.ContainsKey('FailOnNewDiagnostics')) { $cmd += $(if ($FailOnNewDiagnostics) { '--module-fail-on-new-diagnostics' } else { '--module-no-fail-on-new-diagnostics' }) }
        if ($PSBoundParameters.ContainsKey('FailOnDiagnosticsSeverity')) { $cmd += @('--module-fail-on-diagnostics-severity', $FailOnDiagnosticsSeverity) }
        if ($Json) { $cmd += @('--output', 'json') }

        if ([IO.Path]::DirectorySeparatorChar -eq '\' -and (Test-Path -LiteralPath $cliExe)) {
            & $cliExe @cmd
            exit $LASTEXITCODE
        }

        if (-not (Test-Path -LiteralPath $cliDll)) { throw "CLI build output not found: $cliDll" }
        dotnet $cliDll @cmd
        exit $LASTEXITCODE
    }

    # Keep the generated config in the repo root so relative paths (e.g. "Module") resolve correctly.
    $configPath = Join-Path -Path $repoRoot -ChildPath ("powerforge.pipeline.self.{0}.json" -f [Guid]::NewGuid().ToString('N'))
    $buildArgs = @{
        JsonOnly       = $true
        JsonPath       = $configPath
        RunMode        = $RunMode
        Configuration  = $Configuration
        Framework      = $Framework
        NoDotnetBuild  = $true
    }

    foreach ($parameterName in @(
            'ModuleVersion',
            'PreReleaseTag',
            'NoSign',
            'SignModule',
            'CertificateThumbprint',
            'SignIncludeBinaries',
            'SignIncludeInternals',
            'SignIncludeExe',
            'DiagnosticsBaselinePath',
            'GenerateDiagnosticsBaseline',
            'UpdateDiagnosticsBaseline',
            'FailOnNewDiagnostics',
            'FailOnDiagnosticsSeverity'
        )) {
        if ($PSBoundParameters.ContainsKey($parameterName)) {
            $buildArgs[$parameterName] = $PSBoundParameters[$parameterName]
        }
    }

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
    if ($cliHostDir -and (Test-Path -LiteralPath $cliHostDir)) {
        try { Remove-Item -LiteralPath $cliHostDir -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }

    # Restore console settings (best-effort).
    try {
        if ($oldConsoleOutputEncoding) { [Console]::OutputEncoding = $oldConsoleOutputEncoding }
        if ($oldConsoleInputEncoding) { [Console]::InputEncoding = $oldConsoleInputEncoding }
        if ($oldConsoleCodePage) { & cmd /c ("chcp {0} > nul" -f $oldConsoleCodePage) | Out-Null }
    } catch { }
}
