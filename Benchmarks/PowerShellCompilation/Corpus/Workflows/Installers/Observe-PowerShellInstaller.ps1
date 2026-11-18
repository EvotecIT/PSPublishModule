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
$global:PowerForgeInstallerFixture = $fixture
try {
    function global:Get-ComputerInfo {
        [CmdletBinding()]
        param([string[]] $Property)
        if ($Property.Count -ne 1 -or $Property[0] -cne 'OsArchitecture' -or $env:PROCESSOR_ARCHITECTURE -cne 'AMD64') {
            throw 'The portable installer contract requires its declared Windows x64 architecture input.'
        }
        [pscustomobject]@{ OsArchitecture = '64-bit' }
    }
    function global:Invoke-RestMethod {
        [CmdletBinding()]
        param([Parameter(Position=0)][uri] $Uri)
        if ($Uri.AbsoluteUri -cne 'https://raw.githubusercontent.com/PowerShell/PowerShell/master/tools/metadata.json') { throw 'Unexpected installer metadata request.' }
        Microsoft.PowerShell.Utility\Invoke-RestMethod -Uri ($global:PowerForgeInstallerFixture.origin + '/metadata.json')
    }
    function global:Invoke-WebRequest {
        [CmdletBinding()]
        param([uri] $Uri, [string] $OutFile)
        if ($Uri.AbsoluteUri -cne 'https://github.com/PowerShell/PowerShell/releases/download/v7.6.6/PowerShell-7.6.6-win-x64.zip') { throw 'Unexpected installer payload request.' }
        $path = [IO.Path]::GetFullPath($OutFile)
        $root = $global:PowerForgeInstallerFixture.root.TrimEnd('\') + '\'
        if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer download escapes its owned directory.' }
        Microsoft.PowerShell.Utility\Invoke-WebRequest -Uri ($global:PowerForgeInstallerFixture.origin + '/powershell.zip') -OutFile $path -UseBasicParsing
        $pin = @($global:PowerForgeInstallerFixture.state.payloads | Where-Object id -eq powershell)[0]
        if ((Get-FileHash -LiteralPath $path).Hash -ine $pin.sha256) { throw 'Transport changed pinned PowerShell payload bytes.' }
    }
    if ($Surface -eq 'Original') {
        $module = New-Module -Name PowerShellInstaller -ScriptBlock { param($Path) . $Path } -ArgumentList $ImportPath
        Import-Module $module -Force -DisableNameChecking
    } else {
        if ([IO.Path]::GetExtension($ImportPath) -ine '.psm1') { throw 'The advanced-script adapter requires the delivered root module.' }
        $tokens = $null; $parseErrors = $null
        $entry = [Management.Automation.Language.Parser]::ParseFile($ImportPath, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw 'The delivered installer module did not parse.' }
        # Preserve advanced-script binding, including PSCmdlet, for the actual entry.
        $module = New-Module -Name PowerShellInstaller -ScriptBlock { param($Entry) . $Entry } -ArgumentList ($entry.GetScriptBlock())
        Import-Module $module -Force -DisableNameChecking
    }
    $destination = Join-Path $env:LOCALAPPDATA 'Microsoft/powershell'
    $installedHost = Join-Path $destination 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $installedHost -PathType Leaf)) { throw 'The actual application entry did not install the portable payload.' }
    $sourceHost = Join-Path $PSHOME 'pwsh.exe'
    if ($PSVersionTable.PSVersion.Major -ge 7 -and (Get-FileHash -LiteralPath $sourceHost).Hash -cne (Get-FileHash -LiteralPath $installedHost).Hash) {
        throw 'The installed executable differs from the pinned observing host.'
    }
    $version = & $installedHost -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
    if ($LASTEXITCODE -ne 0 -or $version -cne '7.6.6') { throw 'The installed portable PowerShell did not execute successfully.' }
    $scenarios = [Collections.Generic.List[object]]::new()
    $installed = [ordered]@{version=$version;installedExecutableSha256=(Get-FileHash -LiteralPath $installedHost).Hash.ToLowerInvariant()}
    $scenarios.Add((New-InstallerScenario 'portable-install' 'install-powershell.ps1' @('Expand-ArchiveInternal', 'Remove-Destination') $installed))
    $paths = & $module {
        param($Path, $Root)
        if (-not (Test-PathNotInSettings $Path)) { throw 'Fresh portable destination was rejected.' }
        Add-PathTToSettings -Path $Path -Target User
        $machinePath = Join-Path $Root 'machine-path'
        New-Item -ItemType Directory -Path $machinePath | Out-Null
        Add-PathTToSettings -Path $machinePath -Target Machine
        $failure = try { Test-PathNotInSettings $Path; 'unexpected' } catch { $_.Exception.Message }
        if ($failure -notlike '*Already in PATH*') { throw 'Duplicate PATH validation did not reject the installed directory.' }
        'user-and-machine-paths;duplicate-rejected'
    } $destination $fixture.root
    if (-not $fixture.registry.ReadPath($false).EndsWith(';' + $destination, [StringComparison]::OrdinalIgnoreCase) -or
        -not $fixture.registry.ReadPath($true).EndsWith(';' + (Join-Path $fixture.root 'machine-path'), [StringComparison]::OrdinalIgnoreCase)) { throw 'The installer did not update both isolated PATH values.' }
    $scenarios.Add((New-InstallerScenario 'isolated-path-registration' 'install-powershell.ps1' @('Test-PathNotInSettings', 'Add-PathTToSettings') $paths))
    $overwrite = & $module {
        param($Root)
        $path = Join-Path $Root 'previous'
        New-Item -ItemType Directory -Path $path | Out-Null
        [IO.File]::WriteAllText((Join-Path $path 'previous.txt'), 'previous')
        $script:DoNotOverwrite = $true
        $failure = try { Remove-Destination $path; 'unexpected' } catch { $_.Exception.Message }
        if ($failure -notlike '*already exist*' -or -not (Test-Path -LiteralPath $path)) { throw 'Protected overwrite changed the destination.' }
        $script:DoNotOverwrite = $false
        Remove-Destination $path
        if ((Get-Content -LiteralPath (Join-Path ($path + '.old') 'previous.txt') -Raw) -cne 'previous') { throw 'The previous installation was not preserved.' }
        'protected-and-preserved'
    } $fixture.root
    $scenarios.Add((New-InstallerScenario 'overwrite-contract' 'install-powershell.ps1' @('Remove-Destination') $overwrite))
    Write-InstallerObservation $ImportPath $Surface $module @($scenarios) $ResultPath `
        'The unchanged installer entry installs and executes pinned portable PowerShell. All four functions execute. Architecture is fixed to Windows x64 and HTTPS selection is mapped to an owned loopback server; registry writes use process-local replacement hives. MSI, preview selection, global installation and runtime-free execution are not qualified.'
} finally { $fixture.registry.Dispose() }
