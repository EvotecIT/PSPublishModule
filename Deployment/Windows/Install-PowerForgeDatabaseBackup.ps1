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
foreach ($path in @($InstallRoot, $BuildSource, $ConfigSource, $AccessSource)) {
    if (-not [IO.Path]::IsPathRooted($path)) { throw 'Installation paths must be absolute.' }
    for ($check = $path; $check; $check = Split-Path -Path $check -Parent) {
        if ((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Installation path traverses a reparse point.'
        }
    }
}
$config = Get-Content -LiteralPath $ConfigSource -Raw | ConvertFrom-Json
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
if ($existing -and $existing.State -eq 'Running') { throw 'This backup task is running; installation is held.' }
$wasEnabled = -not $existing -or $existing.State -ne 'Disabled'
if ($existing) { Disable-ScheduledTask -TaskName $TaskName | Out-Null }
if ((Test-Path -LiteralPath $InstallRoot) -and -not (Test-Path -LiteralPath (Join-Path $InstallRoot '.powerforge-database-backup-install'))) {
    throw 'Existing installation root is not marked.'
}
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
    $identity = New-Object Security.Principal.SecurityIdentifier($sid)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($MonitorIdentity, 'ReadAndExecute', 'None', 'None', 'Allow')))
Set-Acl -LiteralPath $InstallRoot -AclObject $acl
Set-Content -LiteralPath (Join-Path $InstallRoot '.powerforge-database-backup-install') -Value 'powerforge-database-backup-v1'
foreach ($name in @('app','access','tools','status')) { New-Item -ItemType Directory -Path (Join-Path $InstallRoot $name) -Force | Out-Null }
New-Item -ItemType Directory -Path $config.workRoot -Force | Out-Null
try { $workerGuard = [IO.File]::Open((Join-Path $config.workRoot '.run.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch {
    if ($existing -and $wasEnabled) { Enable-ScheduledTask -TaskName $TaskName | Out-Null }
    throw 'A backup or restore worker is active; installation is held.'
}
try {
Get-ChildItem -LiteralPath $BuildSource -Force | Copy-Item -Destination (Join-Path $InstallRoot 'app') -Recurse -Force
Get-ChildItem -LiteralPath $AccessSource -File | Copy-Item -Destination (Join-Path $InstallRoot 'access') -Force
Copy-Item -LiteralPath $ConfigSource -Destination (Join-Path $InstallRoot 'config.json') -Force
New-Item -ItemType Directory -Path $config.workRoot -Force | Out-Null
$workAcl = New-Object Security.AccessControl.DirectorySecurity
$workAcl.SetAccessRuleProtection($true, $false)
foreach ($identity in @('SYSTEM','BUILTIN\Administrators',$SqlServiceIdentity)) {
    $workAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
Set-Acl -LiteralPath $config.workRoot -AclObject $workAcl
$statusPath = Join-Path $InstallRoot 'status'
$statusAcl = Get-Acl -LiteralPath $statusPath
$statusAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($MonitorIdentity, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -LiteralPath $statusPath -AclObject $statusAcl
if ($SqlDatabase) {
    if ($SqlDatabase -notmatch '^[A-Za-z0-9_]+$') { throw 'SQL database name must use simple identifiers.' }
    $sql = "IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS; GRANT CREATE ANY DATABASE TO [NT AUTHORITY\SYSTEM]; USE [$SqlDatabase]; IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM]; ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];"
    & sqlcmd -S $SqlServer -E -C -b -Q $sql
    if ($LASTEXITCODE -ne 0) { throw 'SQL backup permission setup failed.' }
}
$action = New-ScheduledTaskAction -Execute (Join-Path $InstallRoot 'app\PowerForge.Backups.Cli.exe') -Argument ('run "' + (Join-Path $InstallRoot 'config.json') + '"') -WorkingDirectory (Join-Path $InstallRoot 'app')
$trigger = New-ScheduledTaskTrigger -Daily -At $DailyAt
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 5) -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 15) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Consistent native SQL Server and SQLite backup sets with NAS verification.' -Force | Out-Null
if (-not $wasEnabled) { Disable-ScheduledTask -TaskName $TaskName | Out-Null }
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName,State
}
catch {
    # Keep a failed update disabled; never launch a partially replaced installation.
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { Disable-ScheduledTask -TaskName $TaskName | Out-Null }
    throw
}
finally { $workerGuard.Dispose() }
