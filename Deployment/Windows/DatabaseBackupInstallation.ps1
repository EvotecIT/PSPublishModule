function Get-DatabaseBackupInstallationPlan {
    param([string] $InstallRoot, [string] $TaskName, [string] $DailyAt, [int] $TimeoutMinutes)
    if (-not [IO.Path]::IsPathRooted($InstallRoot) -or [string]::IsNullOrWhiteSpace($TaskName) -or $TaskName.Contains('\')) {
        throw 'An absolute installation root and a task name without a task folder are required.'
    }
    if ([IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') -eq [IO.Path]::GetPathRoot($InstallRoot).TrimEnd('\')) { throw 'Installation cannot use a volume root.' }
    if ($TimeoutMinutes -lt 1 -or $TimeoutMinutes -gt 1440) { throw 'Invalid backup time budget.' }
    $time = [datetime]::ParseExact($DailyAt, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    [pscustomobject]@{
        schemaVersion = 1
        installRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
        taskName = $TaskName
        dailyAt = $time.ToString('HH:mm')
        timeoutMinutes = $TimeoutMinutes
        taskLimitMinutes = $TimeoutMinutes + 60
    }
}

function Get-DatabaseBackupTreeItems {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $item = Get-Item -LiteralPath $pending.Pop() -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Backup directory contains a reparse point.' }
        $item
        if ($item.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $item.FullName -Force) { $pending.Push($child.FullName) }
        }
    }
}

function Assert-DatabaseBackupTreeTrusted {
    param([string] $Path, [string[]] $TrustedOwners, [string[]] $TrustedWriters)
    foreach ($item in Get-DatabaseBackupTreeItems -Path $Path) {
        $security = Get-Acl -LiteralPath $item.FullName
        $owner = $security.GetOwner([Security.Principal.SecurityIdentifier]).Value
        if ($owner -notin $TrustedOwners) { throw 'Existing backup directory has an untrusted owner; installation is held.' }
        foreach ($rule in $security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            # Write, delete, change-permissions, take-ownership and generic write/all.
            if ($rule.AccessControlType -eq 'Allow' -and ([long]$rule.FileSystemRights -band 0x500D0156) -ne 0 -and
                $rule.IdentityReference.Value -notin $TrustedWriters) {
                throw 'Existing backup directory permits an untrusted writer; installation is held.'
            }
        }
    }
}

function Set-DatabaseBackupTreeSecurity {
    param([string] $Path, [string[]] $TrustedOwners, [string[]] $TrustedWriters,
          [string] $AdditionalFullControl, [string] $RootReadIdentity)
    if (Test-Path -LiteralPath $Path) {
        Assert-DatabaseBackupTreeTrusted -Path $Path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
    } else {
        New-Item -ItemType Directory -Path $Path -ErrorAction Stop | Out-Null
    }
    $administrator = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $security = New-Object Security.AccessControl.DirectorySecurity
    $security.SetOwner($administrator)
    $security.SetAccessRuleProtection($true, $false)
    foreach ($name in @('S-1-5-18', 'S-1-5-32-544') + @($AdditionalFullControl | Where-Object { $_ })) {
        $identity = if ($name -match '^S-1-') { New-Object Security.Principal.SecurityIdentifier($name) } else { New-Object Security.Principal.NTAccount($name) }
        $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    if ($RootReadIdentity) {
        $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($RootReadIdentity, 'ReadAndExecute', 'None', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $Path -AclObject $security
    # Check again after taking control, before traversing or consuming any descendants.
    Assert-DatabaseBackupTreeTrusted -Path $Path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
    $items = @(Get-DatabaseBackupTreeItems -Path $Path)
    foreach ($item in $items | Where-Object { $_.FullName -ne $Path }) {
        $childSecurity = if ($item.PSIsContainer) { New-Object Security.AccessControl.DirectorySecurity } else { New-Object Security.AccessControl.FileSecurity }
        $childSecurity.SetOwner($administrator)
        $childSecurity.SetAccessRuleProtection($false, $false)
        Set-Acl -LiteralPath $item.FullName -AclObject $childSecurity
    }
}
