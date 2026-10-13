[CmdletBinding()]
param([Parameter(Mandatory)][string] $TemporaryRoot, [switch] $IncludePrivilegedCreation)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DatabaseBackupInstallation.ps1')
$plan = Get-DatabaseBackupInstallationPlan -InstallRoot 'D:\Custom Backup' -TaskName "Custom 'backup" -DailyAt '04:35' -TimeoutMinutes 600
if ($plan.taskLimitMinutes -ne 660 -or $plan.dailyAt -ne '04:35' -or $plan.installRoot -ne 'D:\Custom Backup') { throw 'Custom installation plan differs.' }
$defaultPlan = Get-DatabaseBackupInstallationPlan -InstallRoot 'D:\Backup' -TaskName 'Backup' -DailyAt '03:00'
if ($defaultPlan.timeoutMinutes -ne 240 -or $defaultPlan.taskLimitMinutes -ne 300) { throw 'Omitted timeout did not preserve the runner default.' }
$previousCulture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    $culture = $previousCulture.Clone(); $culture.DateTimeFormat.TimeSeparator = '.'
    [Threading.Thread]::CurrentThread.CurrentCulture = $culture
    $invariantPlan = Get-DatabaseBackupInstallationPlan -InstallRoot 'D:\Backup' -TaskName 'Backup' -DailyAt '04:35'
    if ($invariantPlan.dailyAt -ne '04:35') { throw 'Daily time changed with the current culture.' }
} finally { [Threading.Thread]::CurrentThread.CurrentCulture = $previousCulture }
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
    $unchangedSecurity = (Get-Acl -LiteralPath $root).Sddl
    Assert-DatabaseBackupNamespaceAclTrusted -Security $acl -TrustedOwners @($sid) -TrustedWriters @($sid)
    $everyone = New-Object Security.Principal.SecurityIdentifier('S-1-1-0')
    foreach ($rights in @('Delete', 'DeleteSubdirectoriesAndFiles', 'ChangePermissions', 'TakeOwnership')) {
        $parentAcl = New-Object Security.AccessControl.DirectorySecurity
        $parentAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier($sid)))
        $parentAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($everyone, $rights, 'Allow')))
        $rejected = $false
        try { Assert-DatabaseBackupNamespaceAclTrusted -Security $parentAcl -TrustedOwners @($sid) -TrustedWriters @($sid) }
        catch { $rejected = $true }
        if (-not $rejected -or (Get-Acl -LiteralPath $root).Sddl -ne $unchangedSecurity) { throw 'Unsafe parent authority was accepted or mutated.' }
    }
    $parentAcl = New-Object Security.AccessControl.DirectorySecurity
    $parentAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier($sid)))
    $parentAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($everyone, 'CreateDirectories', 'Allow')))
    $parentAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($everyone, 'Modify', 'ContainerInherit,ObjectInherit', 'InheritOnly', 'Allow')))
    Assert-DatabaseBackupNamespaceAclTrusted -Security $parentAcl -TrustedOwners @($sid) -TrustedWriters @($sid)
    $parentAcl.SetOwner($everyone)
    $rejected = $false
    try { Assert-DatabaseBackupNamespaceAclTrusted -Security $parentAcl -TrustedOwners @($sid) -TrustedWriters @($sid) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Untrusted namespace owner was accepted.' }
    $unrestricted = New-Object Security.AccessControl.DirectorySecurity
    $unrestricted.SetSecurityDescriptorSddlForm(('O:' + $sid + 'D:NO_ACCESS_CONTROL'))
    $rejected = $false
    try { Assert-DatabaseBackupNamespaceAclTrusted -Security $unrestricted -TrustedOwners @($sid) -TrustedWriters @($sid) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Unrestricted parent DACL was accepted.' }
    $trustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
    $parentAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier($trustedInstaller)))
    Assert-DatabaseBackupNamespaceAclTrusted -Security $parentAcl -TrustedOwners @($trustedInstaller) -TrustedWriters @($trustedInstaller)
    $missing = Join-Path $root 'unprovisioned\child'
    $rejected = $false
    try { Assert-DatabaseBackupNamespaceTrusted -Path $missing -TrustedOwners @($sid) -TrustedWriters @($sid) }
    catch { $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath (Split-Path $missing -Parent))) { throw 'Missing parent was accepted or created.' }
    $physical = Join-Path $root 'physical'; New-Item -ItemType Directory -Path $physical | Out-Null
    $physicalAcl = Get-Acl -LiteralPath $physical
    $physicalAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier($sid)))
    Set-Acl -LiteralPath $physical -AclObject $physicalAcl
    $alias = Join-Path $root 'alias'
    & cmd.exe /d /c "mklink /J `"$alias`" `"$physical`"" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create namespace junction fixture.' }
    try {
        $rejected = $false
        try { Assert-DatabaseBackupNamespaceTrusted -Path (Join-Path $alias 'child') -TrustedOwners @($sid) -TrustedWriters @($sid) }
        catch { $rejected = $true }
        if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $physical 'child'))) { throw 'Reparse ancestor was accepted or mutated.' }
    } finally { Remove-Item -LiteralPath $alias -ErrorAction Stop }
    foreach ($layout in @(@($root,$root), @($root,(Join-Path $root 'work')), @((Join-Path $root 'agent'),$root), @($root,[IO.Path]::GetPathRoot($root)))) {
        $rejected = $false
        try { Assert-DatabaseBackupRootsSeparate -InstallRoot $layout[0] -WorkRoot $layout[1] }
        catch { $rejected = $true }
        if (-not $rejected -or (Get-Acl -LiteralPath $root).Sddl -ne $unchangedSecurity) { throw 'Overlapping roots were accepted or mutated.' }
    }
    Assert-DatabaseBackupRootsSeparate -InstallRoot (Join-Path $root 'agent') -WorkRoot (Join-Path $root 'work')
    $file = Join-Path $root 'config.json'
    Set-Content -LiteralPath $file -Value '{}'
    # Elevated hosts may default new files to Administrators ownership.
    $fileAcl = Get-Acl -LiteralPath $file
    $fileAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier($sid)))
    Set-Acl -LiteralPath $file -AclObject $fileAcl
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
    if ($IncludePrivilegedCreation) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Private creation proof requires an administrator.' }
        $privateRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) ('PowerForgeBackupCreationProof-' + [guid]::NewGuid().ToString('N'))
        $trusted = @($sid, 'S-1-5-18', 'S-1-5-32-544')
        try {
            foreach ($variant in @(($privateRoot + '\'), ($privateRoot + '\.'))) {
                Set-DatabaseBackupTreeSecurity -Path $variant -TrustedOwners $trusted -TrustedWriters $trusted -AdditionalFullControl $sid
                $security = Get-Acl -LiteralPath $privateRoot
                $writer = @($security.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | Where-Object {
                    $_.IdentityReference.Value -eq $sid -and $_.AccessControlType -eq 'Allow' -and
                    ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl
                })
                if (-not $security.AreAccessRulesProtected -or $writer.Count -eq 0) { throw 'Noncanonical root lost private permissions or its designated writer.' }
                Assert-DatabaseBackupTreeTrusted -Path $privateRoot -TrustedOwners @('S-1-5-18','S-1-5-32-544') -TrustedWriters $trusted
            }
            $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($everyone,'Modify','Allow')))
            Set-Acl -LiteralPath $privateRoot -AclObject $security
            $unchangedUntrustedAcl = (Get-Acl -LiteralPath $privateRoot).Sddl
            $rejected = $false
            try { Set-DatabaseBackupTreeSecurity -Path $privateRoot -TrustedOwners $trusted -TrustedWriters $trusted }
            catch { $rejected = $true }
            if (-not $rejected -or (Get-Acl -LiteralPath $privateRoot).Sddl -ne $unchangedUntrustedAcl) { throw 'Untrusted existing result was normalized or accepted.' }
            'PRIVATE_CREATION_CONTRACTS_PASSED'
        } finally {
            if (Test-Path -LiteralPath $privateRoot) {
                @(Get-DatabaseBackupTreeItems -Path $privateRoot) | Out-Null
                if (@(Get-ChildItem -LiteralPath $privateRoot -Force).Count) { throw 'Private creation fixture is not empty.' }
                Remove-Item -LiteralPath $privateRoot -ErrorAction Stop
            }
        }
    }
    'INSTALLATION_CONTRACTS_PASSED'
} finally {
    if (@(Get-DatabaseBackupTreeItems -Path $root | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'Fixture contains a reparse point; cleanup held.' }
    Remove-Item -LiteralPath $root -Recurse -ErrorAction Stop
}
