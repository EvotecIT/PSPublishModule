[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('ArtifactPath')][string] $ImportPath,
    [Parameter(Mandatory)][ValidateSet('Original', 'Built', 'Installed')][string] $Surface,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath,
    [ValidateSet('Original', 'Built', 'Installed', 'ProjectTest')][string] $Instance = $Surface
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer.Observer.Common.ps1')
$fixture = Initialize-InstallerObservation $ScenarioRoot $Instance
$env:CI = 'true'; $env:SCOOP_NOINSTALL = 'true'
try {
    if ($Surface -eq 'Original') {
        $module = New-Module -Name ScoopInstaller -ScriptBlock { param($Path) . $Path } -ArgumentList $ImportPath
        Import-Module $module -Force -DisableNameChecking
    } else {
        if ([IO.Path]::GetExtension($ImportPath) -ine '.psm1') { throw 'The downstream installer adapter requires the delivered root module.' }
        $tokens = $null; $parseErrors = $null
        $entry = [Management.Automation.Language.Parser]::ParseFile($ImportPath, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw 'The delivered installer module did not parse.' }
        # Load the delivered root in the source's documented downstream mode.
        $module = New-Module -Name ScoopInstaller -ScriptBlock { param($Entry) . $Entry } -ArgumentList ($entry.GetScriptBlock())
        Import-Module $module -Force -DisableNameChecking
    }
    $scenarios = [Collections.Generic.List[object]]::new()
    $installation = & $module {
        param($Origin)
        # The source explicitly supports loading its functions first and calling
        # Install-Scoop downstream. Select its actual ZIP-download application path.
        $script:SCOOP_PACKAGE_REPO = $Origin + '/scoop.zip'
        $script:SCOOP_MAIN_BUCKET_REPO = $Origin + '/main.zip'
        $script:NoProxy = $true
        function script:Get-Command {
            [CmdletBinding()]
            param([Parameter(Position=0)][string] $Name)
            if ($Name -in @('git', 'scoop')) { return }
            Microsoft.PowerShell.Core\Get-Command @PSBoundParameters
        }
        Test-LanguageMode
        Write-DebugInfo @{}
        if (Test-ShouldRunInstall -InvocationName '.') { throw 'The declared no-install loading mode was ignored.' }
        Install-Scoop
        $config = Use-Config
        if ($config.root_path -cne $SCOOP_DIR) { throw 'Installed Scoop root configuration differs.' }
        if (Test-IsAdministrator) {
            if ((Get-Env SCOOP_GLOBAL -global) -cne $SCOOP_GLOBAL_DIR -or (Get-Env SCOOP_CACHE -global) -cne $SCOOP_CACHE_DIR) {
                throw 'Installed Scoop machine configuration differs from the isolated environment.'
            }
        } elseif ($config.global_path -cne $SCOOP_GLOBAL_DIR -or $config.cache_path -cne $SCOOP_CACHE_DIR) {
            throw 'Installed Scoop configuration differs from its owned directories.'
        }
        foreach ($name in 'scoop', 'scoop.ps1', 'scoop.cmd') {
            if (-not (Test-Path -LiteralPath (Join-Path $SCOOP_SHIMS_DIR $name) -PathType Leaf)) { throw 'The installer did not create all shim formats.' }
        }
        if (-not (Test-Path -LiteralPath (Join-Path $SCOOP_APP_DIR 'bin/scoop.ps1')) -or
            -not (Test-Path -LiteralPath (Join-Path $SCOOP_MAIN_BUCKET_DIR 'bucket/7zip.json'))) { throw 'The application did not extract both real pinned repositories.' }
        $scriptShim = Join-Path $SCOOP_SHIMS_DIR 'scoop.ps1'
        $commandShim = Join-Path $SCOOP_SHIMS_DIR 'scoop.cmd'
        $entryPath = Join-Path $SCOOP_APP_DIR 'bin/scoop.ps1'
        foreach ($name in 'scoop', 'scoop.cmd') {
            if (-not [IO.File]::ReadAllText((Join-Path $SCOOP_SHIMS_DIR $name)).Contains($entryPath)) {
                throw 'A generated shim does not target the actual installed application.'
            }
        }
        $hostPath = if ($PSVersionTable.PSEdition -eq 'Desktop') { Join-Path $PSHOME 'powershell.exe' } else { Join-Path $PSHOME 'pwsh.exe' }
        $help = @(& $hostPath -NoLogo -NoProfile -NonInteractive -File $scriptShim --help 2>&1)
        if ($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch 'Usage:\s+scoop\s+<command>') { throw 'The installed PowerShell shim did not dispatch offline help.' }
        $invalid = @(& $hostPath -NoLogo -NoProfile -NonInteractive -File $scriptShim __powerforge_invalid_command__ 2>&1)
        if ($LASTEXITCODE -ne 1 -or ($invalid -join "`n") -notlike '*isn''t a scoop command*') { throw 'The installed PowerShell shim did not preserve command failure.' }
        $savedPath = $env:PATH
        try {
            $env:PATH = $PSHOME + [IO.Path]::PathSeparator + $savedPath
            $commandHelp = @(& $commandShim --help 2>&1)
            if ($LASTEXITCODE -ne 0 -or ($commandHelp -join "`n") -notmatch 'Usage:\s+scoop\s+<command>') { throw 'The installed CMD shim did not dispatch offline help.' }
        } finally { $env:PATH = $savedPath }
        Remove-Item -LiteralPath Function:\script:Get-Command
        'installed-two-repositories;three-shims;owned-configuration;powershell-and-cmd-help;unknown-command-exit-1'
    } $fixture.origin
    if ('installed-two-repositories;three-shims;owned-configuration;powershell-and-cmd-help;unknown-command-exit-1' -notin $installation -or
        'Scoop was installed successfully!' -notin $installation) { throw 'The complete installer did not report success.' }
    $installedFunctions = @('Install-Scoop', 'Test-Prerequisite', 'Test-ValidateParameter', 'Optimize-SecurityProtocol',
        'Get-Downloader', 'Test-CommandAvailable', 'Expand-ZipArchive', 'Test-isFileLocked', 'Import-ScoopShim',
        'Out-UTF8File', 'Add-ShimsDirToPath', 'Get-Env', 'Write-Env', 'Publish-Env', 'Add-DefaultConfig',
        'Add-Config', 'Use-Config', 'Write-InstallInfo', 'Test-LanguageMode', 'Write-DebugInfo', 'Test-ShouldRunInstall', 'Test-IsAdministrator')
    if (-not $fixture.registry.ReadPath($false).Contains((Join-Path $env:SCOOP 'shims'))) { throw 'Scoop did not update its isolated user PATH.' }
    $messages = @($installation | ForEach-Object { $_.ToString().Replace($fixture.root, '{ROOT}') })
    $scenarios.Add((New-InstallerScenario 'install-from-pinned-repositories' 'install.ps1' $installedFunctions $messages))
    $locked = & $module {
        param($Root)
        $file = Join-Path $Root 'lock-test.txt'
        [IO.File]::WriteAllText($file, 'owned')
        if (Test-isFileLocked $file) { throw 'A free file was reported locked.' }
        $stream = [IO.File]::Open($file, 'Open', 'ReadWrite', 'None')
        try { if (-not (Test-isFileLocked $file)) { throw 'A held exclusive file was reported free.' } }
        finally { $stream.Dispose() }
        if (Test-isFileLocked (Join-Path $Root 'absent.txt')) { throw 'A missing file was reported locked.' }
        'free;exclusive;absent'
    } $fixture.root
    $scenarios.Add((New-InstallerScenario 'file-lock-contract' 'install.ps1' @('Test-isFileLocked') $locked))
    $failure = & $module {
        $trace = [Collections.Generic.List[string]]::new()
        $savedCI = $env:CI
        $env:CI = $null
        $script:IS_EXECUTED_FROM_IEX = $true
        try {
            foreach ($item in 1, 2) {
                [void] $trace.Add("attempt=$item")
                try { Deny-Install 'owned controlled failure' -ErrorCode 19 }
                finally { [void] $trace.Add('finally') }
                [void] $trace.Add('unexpected')
            }
            if ($global:LASTEXITCODE -ne 19 -or ($trace -join ';') -cne 'attempt=1;finally') { throw 'The installer failure did not transfer to its caller loop.' }
            [void] $trace.Add('recovered')
            $trace -join ';'
        } finally { $env:CI = $savedCI; $script:IS_EXECUTED_FROM_IEX = $false; $global:LASTEXITCODE = 0 }
    }
    $scenarios.Add((New-InstallerScenario 'controlled-install-failure' 'install.ps1' @('Deny-Install', 'Exit-Install', 'Write-InstallInfo') $failure))
    Write-InstallerObservation $ImportPath $Surface $module @($scenarios) $ResultPath `
        'The unchanged installer runs its documented downstream Install-Scoop entry against real pinned Scoop and Main archives, executes offline help through its PowerShell and CMD shims, verifies PowerShell-shim failure exit, tests file locks and exercises its caller-directed failure. The Unix shim is created and target-checked but not executed. The delivered root module is parsed into the same downstream module scope; standalone entry behavior is not qualified. Git and pre-existing Scoop commands are unavailable fixtures. Registry effects use process-local replacement hives. Live account installation, every branch and runtime-free execution are not qualified.'
} finally { $fixture.registry.Dispose() }
