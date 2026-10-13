[CmdletBinding()]
param([Parameter(Mandatory)][string] $TemporaryRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DatabaseBackupInstallation.ps1')
$plan = Get-DatabaseBackupInstallationPlan -InstallRoot 'D:\Custom Backup' -TaskName "Custom 'backup" -DailyAt '04:35' -TimeoutMinutes 600
if ($plan.taskLimitMinutes -ne 660 -or $plan.dailyAt -ne '04:35' -or $plan.installRoot -ne 'D:\Custom Backup') { throw 'Custom installation plan differs.' }
foreach ($timeout in @(0,1441)) {
    $rejected = $false
    try { Get-DatabaseBackupInstallationPlan -InstallRoot 'D:\Backup' -TaskName 'Backup' -DailyAt '03:00' -TimeoutMinutes $timeout | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid time budget was accepted.' }
}
$root = [IO.Path]::GetFullPath((Join-Path $TemporaryRoot ('backup-install-contract-' + [guid]::NewGuid().ToString('N'))))
if (-not $root.StartsWith([IO.Path]::GetFullPath($TemporaryRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture root.' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier($sid)))
    $acl.SetAccessRuleProtection($true,$false)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    Set-Acl -LiteralPath $root -AclObject $acl
    $file = Join-Path $root 'config.json'
    Set-Content -LiteralPath $file -Value '{}'
    Assert-DatabaseBackupTreeTrusted -Path $root -TrustedOwners @($sid) -TrustedWriters @($sid)
    $rejected = $false
    try { Assert-DatabaseBackupTreeTrusted -Path $root -TrustedOwners @('S-1-5-18') -TrustedWriters @($sid) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Untrusted owner was accepted.' }
    $acl = Get-Acl -LiteralPath $file
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-1-0')), 'FullControl', 'Allow')))
    Set-Acl -LiteralPath $file -AclObject $acl
    $rejected = $false
    try { Assert-DatabaseBackupTreeTrusted -Path $root -TrustedOwners @($sid) -TrustedWriters @($sid) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Explicit descendant write permission was accepted.' }
    'INSTALLATION_CONTRACTS_PASSED'
} finally {
    if (@(Get-DatabaseBackupTreeItems -Path $root | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'Fixture contains a reparse point; cleanup held.' }
    Remove-Item -LiteralPath $root -Recurse -ErrorAction Stop
}
