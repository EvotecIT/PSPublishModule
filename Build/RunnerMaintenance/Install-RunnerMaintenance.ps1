<#
.SYNOPSIS
Installs prebuilt host-local maintenance for stopped GitHub runner registrations.
.DESCRIPTION
The policy identifies every participating installation and the private payload/state
directory outside runner workspaces. Existing hooks are preserved. Installation
requires an idle service maintenance window. The caller restores the services after
installation. The native schedule runs hourly and after startup with a bounded
process lifetime. Supply an already built payload. Installation does not restore,
build, publish, stop jobs, or restart a machine.
.PARAMETER Configuration
Absolute path to the host's maintenance policy JSON.
.PARAMETER RunAsUser
POSIX service identity. Linux runners normally use root. macOS uses the runner's
existing account. Windows installations use LocalSystem.
.EXAMPLE
./Install-RunnerMaintenance.ps1 -Configuration /var/lib/powerforge-runner-maintenance/enrollment.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Configuration,
    [string] $RunAsUser = 'root'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$policy = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
$state = [IO.Path]::GetFullPath($policy.stateRootPath)
$windows = $env:OS -eq 'Windows_NT'
$paths = @($state, $policy.executablePath) + @($policy.runners | ForEach-Object { $_.tempPath })
foreach ($path in $paths) {
    if (-not [IO.Path]::IsPathRooted($path) -or $path.IndexOfAny([char[]] @('"', "`r", "`n", '%')) -ge 0) {
        throw 'Enrollment requires absolute, single-line local paths without quotes or scheduler substitutions.'
    }
    $entry = [IO.DirectoryInfo]::new($path)
    while ($entry) {
        if ($entry.Exists -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Redirected maintenance paths are unsupported.'
        }
        $entry = $entry.Parent
    }
}
if (-not (Test-Path -LiteralPath $policy.executablePath -PathType Leaf)) {
    throw 'The prebuilt maintenance executable is missing.'
}
foreach ($directory in @($state) + @($policy.runners | ForEach-Object { $_.tempPath })) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    if ($windows) {
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
            $identity = [Security.Principal.SecurityIdentifier]::new($sid)
            $rule = [Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            $acl.AddAccessRule($rule)
        }
        $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
        Set-Acl -LiteralPath $directory -AclObject $acl
    } else {
        & chmod 700 $directory
        if ($LASTEXITCODE -ne 0) { throw 'Private maintenance-directory permissions failed.' }
    }
}

& $policy.executablePath github runner maintenance --config $Configuration --install
if ($LASTEXITCODE -ne 0) { throw 'Runner maintenance enrollment failed.' }
$policy = Get-Content -LiteralPath (Join-Path $state 'policy.json') -Raw | ConvertFrom-Json
$wrapper = Join-Path $state 'Invoke-RunnerMaintenance.ps1'
$scheduled = Join-Path $state 'scheduled.json'
$name = 'PowerForge.RunnerMaintenance'
if ($windows) {
    $hostExecutable = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' })
    $action = New-ScheduledTaskAction -Execute $hostExecutable -Argument ('-NoLogo -NoProfile -NonInteractive -File "' + $wrapper + '" -Configuration "' + $scheduled + '"') -WorkingDirectory $state
    $triggers = @(
        New-ScheduledTaskTrigger -AtStartup
        New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) -RepetitionInterval (New-TimeSpan -Hours 1)
    )
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $triggers -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
} else {
    $hostExecutable = Join-Path $PSHOME 'pwsh'
    if ($RunAsUser -notmatch '^[a-z_][a-z0-9_-]*[$]?$') { throw 'Invalid POSIX service account.' }
    foreach ($directory in @($state) + @($policy.runners | ForEach-Object { $_.tempPath })) {
        & chown -R $RunAsUser $directory
        if ($LASTEXITCODE -ne 0) { throw 'Maintenance service ownership failed.' }
    }
    foreach ($configurationPath in $policy.nuGetConfigPaths) {
        & chown $RunAsUser $configurationPath
        if ($LASTEXITCODE -ne 0) { throw 'NuGet service configuration ownership failed.' }
    }
    $platform = & uname -s
    if ($platform -eq 'Darwin') {
        $escape = { param($text) [Security.SecurityElement]::Escape($text) }
        $arguments = @($hostExecutable, '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $wrapper, '-Configuration', $scheduled)
        $items = ($arguments | ForEach-Object { '<string>' + (& $escape $_) + '</string>' }) -join "`n"
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>Label</key><string>com.evotec.powerforge.runner-maintenance</string>
<key>ProgramArguments</key><array>$items</array>
<key>UserName</key><string>$RunAsUser</string>
<key>RunAtLoad</key><true/><key>StartInterval</key><integer>3600</integer>
<key>ProcessType</key><string>Background</string>
</dict></plist>
"@
        $destination = '/Library/LaunchDaemons/com.evotec.powerforge.runner-maintenance.plist'
        [IO.File]::WriteAllText($destination, $plist, [Text.UTF8Encoding]::new($false))
        & chown root:wheel $destination
        & chmod 644 $destination
        & launchctl print system/com.evotec.powerforge.runner-maintenance 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & launchctl bootout system/com.evotec.powerforge.runner-maintenance
            if ($LASTEXITCODE -ne 0) { throw 'Existing maintenance schedule could not be unloaded.' }
        }
        & launchctl bootstrap system $destination
        if ($LASTEXITCODE -ne 0) { throw 'Maintenance launch daemon registration failed.' }
    } elseif ($platform -eq 'Linux') {
        $unit = @"
[Unit]
Description=PowerForge runner maintenance
[Service]
Type=oneshot
User=$RunAsUser
ExecStart="$hostExecutable" -NoLogo -NoProfile -NonInteractive -File "$wrapper" -Configuration "$scheduled"
TimeoutStartSec=270
KillMode=control-group
UMask=0077
"@
        $timer = @'
[Unit]
Description=Hourly PowerForge runner maintenance
[Timer]
OnBootSec=3min
OnUnitInactiveSec=1h
Persistent=true
[Install]
WantedBy=timers.target
'@
        [IO.File]::WriteAllText('/etc/systemd/system/powerforge-runner-maintenance.service', $unit, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText('/etc/systemd/system/powerforge-runner-maintenance.timer', $timer, [Text.UTF8Encoding]::new($false))
        & systemctl daemon-reload
        if ($LASTEXITCODE -ne 0) { throw 'systemd could not load the maintenance unit.' }
        & systemctl enable --now powerforge-runner-maintenance.timer
        if ($LASTEXITCODE -ne 0) { throw 'Maintenance timer registration failed.' }
    } else {
        throw 'Unsupported native maintenance scheduler.'
    }
}
Write-Output 'Installed hourly and startup maintenance. Restore the enrolled runner services and verify the first local receipt.'
