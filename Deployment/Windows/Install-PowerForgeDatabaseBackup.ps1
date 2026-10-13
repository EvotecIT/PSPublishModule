#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $InstallRoot,
    [Parameter(Mandatory)][string] $BuildSource,
    [Parameter(Mandatory)][string] $ConfigSource,
    [Parameter(Mandatory)][string] $AccessSource,
    [Parameter(Mandatory)][string] $MonitorIdentity,
    [string] $TaskName = 'Evotec Database Backups',
    [string] $DailyAt = '03:00',
    [string] $SqlServer = 'localhost',
    [string] $SqlDatabase,
    [string] $SqlServiceIdentity = 'NT SERVICE\MSSQLSERVER'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DatabaseBackupInstallation.ps1')
foreach ($path in @($InstallRoot, $BuildSource, $ConfigSource, $AccessSource)) {
    if (-not [IO.Path]::IsPathRooted($path)) { throw 'Installation paths must be absolute.' }
    for ($check = $path; $check; $check = Split-Path -Path $check -Parent) {
        if ((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Installation path traverses a reparse point.'
        }
    }
}
$config = Get-Content -LiteralPath $ConfigSource -Raw | ConvertFrom-Json
$planParameters = @{ InstallRoot = $InstallRoot; TaskName = $TaskName; DailyAt = $DailyAt }
if ($config.PSObject.Properties['timeoutMinutes']) { $planParameters.TimeoutMinutes = $config.timeoutMinutes }
$plan = Get-DatabaseBackupInstallationPlan @planParameters
$InstallRoot = $plan.installRoot
Assert-DatabaseBackupRootsSeparate -InstallRoot $InstallRoot -WorkRoot $config.workRoot
if (-not [IO.Path]::IsPathRooted($config.workRoot) -or -not (Test-Path -LiteralPath ([IO.Path]::GetPathRoot($config.workRoot)))) {
    throw 'Configured staging volume is not mounted.'
}
for ($check = $config.workRoot; $check; $check = Split-Path -Path $check -Parent) {
    if ((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Staging path traverses a reparse point.' }
}
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing -and ($existing.Actions.Execute -ne (Join-Path $InstallRoot 'app\PowerForge.Backups.Cli.exe'))) {
    throw 'A task with this name belongs to another installation.'
}
if ((Test-Path -LiteralPath $InstallRoot) -and -not (Test-Path -LiteralPath (Join-Path $InstallRoot '.powerforge-database-backup-install'))) {
    throw 'Existing installation root is not marked.'
}
if (Test-Path -LiteralPath $InstallRoot) {
    $installedConfig = Get-Content -LiteralPath (Join-Path $InstallRoot 'config.json') -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($installedConfig.workRoot).TrimEnd('\') -ine [IO.Path]::GetFullPath($config.workRoot).TrimEnd('\')) {
        throw 'An ordinary installation update cannot change its staging root; migrate it separately.'
    }
}
if ($existing -and $existing.State -eq 'Running') { throw 'This backup task is running; installation is held.' }
$operatorSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$sqlSid = (New-Object Security.Principal.NTAccount($SqlServiceIdentity)).Translate([Security.Principal.SecurityIdentifier]).Value
$trustedOwners = @('S-1-5-18', 'S-1-5-32-544', $operatorSid)
$trustedWriters = $trustedOwners
Assert-DatabaseBackupTreeTrusted -Path $InstallRoot -TrustedOwners $trustedOwners -TrustedWriters $trustedWriters
Assert-DatabaseBackupTreeTrusted -Path $config.workRoot -TrustedOwners ($trustedOwners + $sqlSid) -TrustedWriters ($trustedWriters + $sqlSid)
$wasEnabled = -not $existing -or $existing.State -ne 'Disabled'
if ($existing) { Disable-ScheduledTask -TaskName $TaskName | Out-Null }
if (-not (Test-Path -LiteralPath $config.workRoot)) {
    Set-DatabaseBackupTreeSecurity -Path $config.workRoot -TrustedOwners ($trustedOwners + $sqlSid) -TrustedWriters ($trustedWriters + $sqlSid) -AdditionalFullControl $SqlServiceIdentity
}
try { $workerGuard = [IO.File]::Open((Join-Path $config.workRoot '.run.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch {
    if ($existing -and $wasEnabled) { Enable-ScheduledTask -TaskName $TaskName | Out-Null }
    throw 'A backup or restore worker is active; installation is held.'
}
try {
Set-DatabaseBackupTreeSecurity -Path $InstallRoot -TrustedOwners $trustedOwners -TrustedWriters $trustedWriters -RootReadIdentity $MonitorIdentity
Set-DatabaseBackupTreeSecurity -Path $config.workRoot -TrustedOwners ($trustedOwners + $sqlSid) -TrustedWriters ($trustedWriters + $sqlSid) -AdditionalFullControl $SqlServiceIdentity
Set-Content -LiteralPath (Join-Path $InstallRoot '.powerforge-database-backup-install') -Value 'powerforge-database-backup-v1'
foreach ($name in @('app','access','tools','status')) { New-Item -ItemType Directory -Path (Join-Path $InstallRoot $name) -Force | Out-Null }
Get-ChildItem -LiteralPath $BuildSource -Force | Copy-Item -Destination (Join-Path $InstallRoot 'app') -Recurse -Force
Get-ChildItem -LiteralPath $AccessSource -File | Copy-Item -Destination (Join-Path $InstallRoot 'access') -Force
Copy-Item -LiteralPath $ConfigSource -Destination (Join-Path $InstallRoot 'config.json') -Force
$statusPath = Join-Path $InstallRoot 'status'
$statusAcl = Get-Acl -LiteralPath $statusPath
$statusAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($MonitorIdentity, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -LiteralPath $statusPath -AclObject $statusAcl
$plan | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $statusPath 'installation.json') -Encoding UTF8
if ($SqlDatabase) {
    if ($SqlDatabase -notmatch '^[A-Za-z0-9_]+$') { throw 'SQL database name must use simple identifiers.' }
    $sql = "IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS; GRANT CREATE ANY DATABASE TO [NT AUTHORITY\SYSTEM]; USE [$SqlDatabase]; IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM]; ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];"
    & sqlcmd -S $SqlServer -E -C -b -Q $sql
    if ($LASTEXITCODE -ne 0) { throw 'SQL backup permission setup failed.' }
}
$action = New-ScheduledTaskAction -Execute (Join-Path $InstallRoot 'app\PowerForge.Backups.Cli.exe') -Argument ('run "' + (Join-Path $InstallRoot 'config.json') + '"') -WorkingDirectory (Join-Path $InstallRoot 'app')
$trigger = New-ScheduledTaskTrigger -Daily -At $DailyAt
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -Disable -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes $plan.taskLimitMinutes) -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 15) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Consistent native SQL Server and SQLite backup sets with NAS verification.' -Force | Out-Null
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$registered = $scheduler.GetFolder('\').GetTask($TaskName)
$descriptor = New-Object Security.AccessControl.RawSecurityDescriptor($registered.GetSecurityDescriptor(4))
$monitorSid = (New-Object Security.Principal.NTAccount($MonitorIdentity)).Translate([Security.Principal.SecurityIdentifier])
# Task Scheduler uses file access masks; FileGenericRead excludes task execution and changes.
$readAce = New-Object Security.AccessControl.CommonAce('None', 'AccessAllowed', 0x120089, $monitorSid, $false, $null)
$descriptor.DiscretionaryAcl.InsertAce($descriptor.DiscretionaryAcl.Count, $readAce)
$registered.SetSecurityDescriptor($descriptor.GetSddlForm([Security.AccessControl.AccessControlSections]::Access), 0)
$workerGuard.Dispose()
$workerGuard = $null
if ($wasEnabled) { Enable-ScheduledTask -TaskName $TaskName | Out-Null }
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName,State
}
catch {
    # Keep a failed update disabled; never launch a partially replaced installation.
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { Disable-ScheduledTask -TaskName $TaskName | Out-Null }
    throw
}
finally { if ($null -ne $workerGuard) { $workerGuard.Dispose() } }
