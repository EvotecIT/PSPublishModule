<#
.SYNOPSIS
Plans or installs private, automatically cleaned temporary storage for a Windows runner.
.DESCRIPTION
Sets TEMP, TMP and SystemTemp for one LocalSystem runner through its .env file.
GitHub hooks are installed outside the application and checkout in a private
Program Files directory. Stop the idle runner before Apply or Restore and restore
its service state afterwards. Plan is the default. No service, scheduler, shared
cache, existing directory permissions or machine environment is changed.
.PARAMETER RunnerRoot
Physical directory containing the runner's .runner registration and .env file.
.PARAMETER NuGetScratchRoot
Existing shared NuGet scratch/lock directory, preserved across jobs.
.PARAMETER ExpectedOrganization
GitHub organization whose registration is allowed to change.
.PARAMETER Mode
Plan, Apply, or Restore. Restore refuses to overwrite later .env edits.
.EXAMPLE
& .\Install-RunnerJobTempCleanup.ps1 -RunnerRoot C:\actions-runner -NuGetScratchRoot C:\Windows\SystemTemp\NuGetScratch -ExpectedOrganization EvotecIT
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RunnerRoot,
    [Parameter(Mandatory)][string] $NuGetScratchRoot,
    [Parameter(Mandatory)][string] $ExpectedOrganization,
    [ValidateSet('Plan', 'Apply', 'Restore')][string] $Mode = 'Plan'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$trustedOwners = @('S-1-5-18', 'S-1-5-32-544',
    'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464') # TrustedInstaller

function Assert-PhysicalPath {
    param([string] $Path)
    $walk = [IO.Path]::GetFullPath($Path)
    while ($walk) {
        if ((Test-Path -LiteralPath $walk) -and
            ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Redirected path is not supported.'
        }
        $parent = Split-Path $walk -Parent
        if ($parent -eq $walk) { break }
        $walk = $parent
    }
}

function Assert-TrustedDirectory {
    param([string] $Path, [switch] $Private)
    $acl = Get-Acl -LiteralPath $Path
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trustedOwners) {
        throw 'Directory ownership is not trusted for SYSTEM hook execution.'
    }
    # Parent deletion/permission rights can replace even a privately ACLed child.
    $namespaceRights = [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [Security.AccessControl.FileSystemRights]::TakeOwnership -bor 0x10000000
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow' -or $rule.IdentityReference.Value -in $trustedOwners) { continue }
        if (-not $Private -and $rule.IdentityReference.Value -eq 'S-1-3-0' -and
            ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly)) { continue }
        if ($Private -or ($rule.FileSystemRights -band $namespaceRights)) {
            throw 'Directory permissions permit an unprivileged hook replacement.'
        }
    }
}

function New-PrivateDirectory {
    param([string] $Path)
    if (Test-Path -LiteralPath $Path) { throw 'New installation directory already exists.' }
    if (-not (Test-Path -LiteralPath (Split-Path $Path -Parent))) { throw 'Trusted parent must exist first.' }
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sid), 'FullControl',
            'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    # Supply the DACL at creation; never create an executable directory with inherited write access.
    if ($PSVersionTable.PSEdition -eq 'Desktop') { $null = [IO.Directory]::CreateDirectory($Path, $acl) }
    else { $null = [IO.FileSystemAclExtensions]::CreateDirectory($acl, $Path) }
    Assert-PhysicalPath $Path
    Assert-TrustedDirectory $Path -Private
}

function Get-BytesHash {
    param([byte[]] $Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Write-AtomicBytes {
    param([string] $Path, [byte[]] $Bytes)
    $temporary = $Path + '.powerforge-' + [guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllBytes($temporary, $Bytes)
        if (Test-Path -LiteralPath $Path) {
            [IO.File]::Replace($temporary, $Path, [System.Management.Automation.Language.NullString]::Value)
        } else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -ErrorAction Stop }
    }
}

function Save-InstallationState {
    param($State)
    Write-AtomicBytes $statePath ($encoding.GetBytes(($State | ConvertTo-Json -Depth 8)))
}

function Restore-OriginalEnvironment {
    param($State)
    $exists = Test-Path -LiteralPath $envPath
    $bytes = [byte[]]@()
    if ($exists) { $bytes = [IO.File]::ReadAllBytes($envPath) }
    $hash = Get-BytesHash $bytes
    if ($exists -eq $State.originalEnvExisted -and $hash -ceq $State.originalEnvHash) { return }
    if (-not $exists -or $hash -cne $State.installedEnvHash) {
        throw 'The runner .env changed; automatic restore is held.'
    }
    if ($State.originalEnvExisted) {
        Write-AtomicBytes $envPath ([Convert]::FromBase64String($State.originalEnvBase64))
    } else { Remove-Item -LiteralPath $envPath -ErrorAction Stop }
}

function Remove-UnactivatedInstallation {
    param($State)
    Restore-OriginalEnvironment $State
    # These destinations were absent in newly created private directories before this attempt.
    foreach ($file in $State.createdFiles) {
        Assert-PhysicalPath $file
        if (Test-Path -LiteralPath $file) {
            if ((Get-Item -LiteralPath $file -Force).PSIsContainer) { throw 'Unexpected installation payload retained.' }
            Remove-Item -LiteralPath $file -ErrorAction Stop
        }
    }
    $directories = @($State.createdDirectories)
    [array]::Reverse($directories)
    foreach ($directory in $directories) {
        Assert-PhysicalPath $directory
        if (Test-Path -LiteralPath $directory) {
            Assert-TrustedDirectory $directory -Private
            if (@(Get-ChildItem -LiteralPath $directory -Force).Count) { throw 'Unexpected installation data retained.' }
            Remove-Item -LiteralPath $directory -ErrorAction Stop
        }
    }
    if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -ErrorAction Stop }
}

if ($env:OS -ne 'Windows_NT') { throw 'Runner temporary storage installation requires Windows.' }
$RunnerRoot = [IO.Path]::GetFullPath($RunnerRoot).TrimEnd('\')
$NuGetScratchRoot = (Resolve-Path -LiteralPath $NuGetScratchRoot).Path
Assert-PhysicalPath $RunnerRoot
Assert-PhysicalPath $NuGetScratchRoot
$identity = Get-Content -LiteralPath (Join-Path $RunnerRoot '.runner') -Raw | ConvertFrom-Json
if ($ExpectedOrganization -notmatch '^[A-Za-z0-9-]+$' -or
    $identity.gitHubUrl -cne "https://github.com/$ExpectedOrganization" -or
    $identity.agentName -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') {
    throw 'Runner organization or name does not match the requested installation.'
}
$agentId = [string]$identity.agentId
if ($agentId -cnotmatch '\A[1-9][0-9]{0,18}\z') {
    throw 'Runner registration agentId must contain positive decimal digits only.'
}
$services = @(Get-CimInstance Win32_Service -ErrorAction Stop | Where-Object {
    $_.PathName -and $_.PathName.Trim('"') -ieq (Join-Path $RunnerRoot 'bin\RunnerService.exe')
})
if ($services.Count -ne 1 -or $services[0].StartName -notin @('LocalSystem', 'NT AUTHORITY\SYSTEM')) {
    throw 'One exact LocalSystem RunnerService must own this installation.'
}
$service = $services[0]
if ($Mode -ne 'Plan') {
    $running = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -in @('Runner.Listener.exe', 'Runner.Worker.exe') -and $_.ExecutablePath -and
        $_.ExecutablePath.StartsWith($RunnerRoot + '\', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($running.Count -or $service.State -ne 'Stopped') { throw 'Stop the idle runner before changing its installation.' }
}

$base = Join-Path $env:ProgramFiles 'PowerForge.RunnerMaintenance'
$installationRoot = Join-Path $base $identity.agentName
$tempParent = Join-Path $env:SystemRoot 'SystemTemp\PFJ'
$tempRoot = Join-Path $tempParent $agentId
$envPath = Join-Path $RunnerRoot '.env'
$statePath = Join-Path $base ($identity.agentName + '.installation.json')
$hook = Join-Path $installationRoot 'Invoke-RunnerJobTempCleanup.ps1'
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($path in @($installationRoot, $tempRoot, $envPath)) { Assert-PhysicalPath $path }
$walk = $base
while ($walk) {
    if (Test-Path -LiteralPath $walk) { Assert-TrustedDirectory $walk }
    $parent = Split-Path $walk -Parent
    if ($parent -eq $walk) { break }
    $walk = $parent
}
foreach ($path in @($base, (Join-Path $env:SystemRoot 'SystemTemp'), $tempParent)) {
    if (Test-Path -LiteralPath $path) { Assert-TrustedDirectory $path -Private }
}
if ($NuGetScratchRoot.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Shared NuGet locks must be outside private job temporary storage.'
}

if ($Mode -eq 'Restore') {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($state.runnerRoot -cne $RunnerRoot -or $state.tempRoot -cne $tempRoot) { throw 'Installation ownership changed.' }
    if ($state.phase -eq 'Preparing') { Remove-UnactivatedInstallation $state }
    else {
        Restore-OriginalEnvironment $state
        $state.phase = 'Restored'
        Save-InstallationState $state
    }
    [pscustomobject]@{ restored = $true; runnerName = $identity.agentName; backupPath = $statePath }
    return
}

# A service can inherit hooks without an assignment in .env. Never displace that configuration.
$hookNames = @('ACTIONS_RUNNER_HOOK_JOB_STARTED', 'ACTIONS_RUNNER_HOOK_JOB_COMPLETED')
$registryPaths = @(
    'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Registry::HKEY_USERS\S-1-5-18\Environment',
    'Registry::HKEY_USERS\.DEFAULT\Environment',
    ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\' + $service.Name)
)
foreach ($registryPath in $registryPaths) {
    try { $settings = Get-ItemProperty -LiteralPath $registryPath -ErrorAction Stop }
    catch [System.Management.Automation.ItemNotFoundException] { continue }
    foreach ($name in $hookNames) {
        $property = $settings.PSObject.Properties[$name]
        if ($property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw 'An existing inherited service hook must be preserved.'
        }
    }
    $environment = $settings.PSObject.Properties['Environment']
    if ($environment) {
        foreach ($entry in @($environment.Value)) {
            $pair = $entry -split '=', 2
            if ($pair.Count -eq 2 -and $pair[0] -in $hookNames -and -not [string]::IsNullOrWhiteSpace($pair[1])) {
                throw 'An existing inherited service hook must be preserved.'
            }
        }
    }
}
$originalExisted = Test-Path -LiteralPath $envPath
$originalBytes = [byte[]]@()
if ($originalExisted) { $originalBytes = [IO.File]::ReadAllBytes($envPath) }
$text = [Text.Encoding]::UTF8.GetString($originalBytes).TrimStart([char]0xfeff)
if ($text.Contains([string][char]0)) { throw 'The runner .env file must use UTF-8 text.' }
$lines = [Collections.Generic.List[string]]::new()
$assignments = [ordered]@{
    TEMP = $tempRoot
    TMP = $tempRoot
    SystemTemp = $tempRoot
    NUGET_SCRATCH = $NuGetScratchRoot
    ACTIONS_RUNNER_HOOK_JOB_STARTED = $hook
    ACTIONS_RUNNER_HOOK_JOB_COMPLETED = $hook
}
foreach ($line in ($text -split '\r?\n')) {
    if (-not $line -and -not $text) { continue }
    $name = ($line -split '=', 2)[0]
    if ($name -in $hookNames -and $line -ne ($name + '=')) { throw 'An existing job hook must be preserved.' }
    if ($name -eq 'NUGET_SCRATCH' -and $line.Substring($name.Length + 1) -ine $NuGetScratchRoot) {
        throw 'The configured NuGet scratch directory differs; preserve its shared locks.'
    }
    if ($name -notin @($assignments.Keys)) { $lines.Add($line) }
}
foreach ($entry in $assignments.GetEnumerator()) { $lines.Add($entry.Key + '=' + $entry.Value) }
$newBytes = $encoding.GetBytes(($lines -join "`r`n") + "`r`n")
$plan = [pscustomobject]@{
    mode = $Mode
    runnerName = $identity.agentName
    runnerRoot = $RunnerRoot
    tempRoot = $tempRoot
    installationRoot = $installationRoot
    nuGetScratchRoot = $NuGetScratchRoot
    assignments = $assignments
    restartRequired = $true
}
if ($Mode -eq 'Plan') { $plan; return }
if ((Test-Path -LiteralPath $statePath) -or (Test-Path -LiteralPath $installationRoot) -or (Test-Path -LiteralPath $tempRoot)) {
    throw 'An installation or recovery record already exists; use its supported Restore first.'
}
$state = [ordered]@{
    schemaVersion = 1
    id = [guid]::NewGuid().ToString('N')
    phase = 'Preparing'
    runnerRoot = $RunnerRoot
    tempRoot = $tempRoot
    originalEnvExisted = $originalExisted
    originalEnvBase64 = [Convert]::ToBase64String($originalBytes)
    originalEnvHash = Get-BytesHash $originalBytes
    installedEnvHash = Get-BytesHash $newBytes
    createdDirectories = [Collections.Generic.List[string]]::new()
    createdFiles = [Collections.Generic.List[string]]::new()
}
try {
    if (-not (Test-Path -LiteralPath $base)) { New-PrivateDirectory $base }
    Save-InstallationState $state
    foreach ($directory in @($tempParent, $tempRoot, $installationRoot)) {
        if (Test-Path -LiteralPath $directory) { continue }
        # Journal the absent destination before mutation so abrupt termination is recoverable.
        $state.createdDirectories.Add($directory)
        Save-InstallationState $state
        New-PrivateDirectory $directory
    }
    foreach ($name in @('Invoke-RunnerJobTempCleanup.ps1', 'Clear-RunnerJobTemp.ps1')) {
        $destination = Join-Path $installationRoot $name
        $state.createdFiles.Add($destination)
        Save-InstallationState $state
        # Create bytes inside the private parent instead of copying a source file's security descriptor.
        Write-AtomicBytes $destination ([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name)))
        if ((Get-BytesHash ([IO.File]::ReadAllBytes($destination))) -cne
            (Get-BytesHash ([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name))))) { throw 'Hook copy hash mismatch.' }
    }
    $configuration = [ordered]@{ schemaVersion = 1; runnerRoot = $RunnerRoot; runnerName = $identity.agentName;
        gitHubUrl = $identity.gitHubUrl; tempRoot = $tempRoot; installationId = $state.id }
    foreach ($name in @('configuration.json', 'installation.json')) {
        $destination = Join-Path $installationRoot $name
        $state.createdFiles.Add($destination)
        Save-InstallationState $state
        Write-AtomicBytes $destination ($encoding.GetBytes(($configuration | ConvertTo-Json)))
    }
    Write-AtomicBytes $envPath $newBytes
    if ((Get-BytesHash ([IO.File]::ReadAllBytes($envPath))) -cne $state.installedEnvHash) { throw 'Environment commit verification failed.' }
    $state.phase = 'Active'
    Save-InstallationState $state
} catch {
    $failure = $_
    try { Remove-UnactivatedInstallation $state }
    catch { throw "Installation failed and recovery is retained at $statePath. Keep the runner stopped and run Restore. $($_.Exception.Message)" }
    throw $failure
}
$plan
