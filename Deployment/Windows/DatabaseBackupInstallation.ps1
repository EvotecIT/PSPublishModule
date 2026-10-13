function Get-DatabaseBackupInstallationPlan {
    param([string] $InstallRoot, [string] $TaskName, [string] $DailyAt, [int] $TimeoutMinutes = 240)
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
        dailyAt = $time.ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture)
        timeoutMinutes = $TimeoutMinutes
        taskLimitMinutes = $TimeoutMinutes + 60
    }
}

function Assert-DatabaseBackupRootsSeparate {
    param([string] $InstallRoot, [string] $WorkRoot)
    if (-not [IO.Path]::IsPathRooted($WorkRoot) -or
        [IO.Path]::GetFullPath($WorkRoot).TrimEnd('\') -eq [IO.Path]::GetPathRoot($WorkRoot).TrimEnd('\')) {
        throw 'Staging requires a dedicated absolute directory, not a volume root.'
    }
    $installation = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') + '\'
    $staging = [IO.Path]::GetFullPath($WorkRoot).TrimEnd('\') + '\'
    if ($installation.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -or
        $staging.StartsWith($installation, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installation and staging roots must be separate, without overlap.'
    }
}

function Get-DatabaseBackupSqlPermissionBatch {
    param([string] $Database)
    if ($Database -notmatch '\A[A-Za-z0-9_]+\z') { throw 'SQL database name must use simple identifiers.' }
    @"
SET XACT_ABORT ON;
IF DB_ID(N'$Database') IS NULL THROW 51000, 'The backup target database does not exist.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
    GRANT CREATE ANY DATABASE TO [NT AUTHORITY\SYSTEM];
    USE [$Database];
    IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
    IF IS_ROLEMEMBER(N'db_backupoperator', N'NT AUTHORITY\SYSTEM') <> 1 ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@
}

function Assert-DatabaseBackupNamespaceAclTrusted {
    param([Security.AccessControl.DirectorySecurity] $Security, [string[]] $TrustedOwners, [string[]] $TrustedWriters)
    if ($Security.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $TrustedOwners) {
        throw 'A backup parent directory has an untrusted owner.'
    }
    $descriptor = New-Object Security.AccessControl.RawSecurityDescriptor($Security.GetSecurityDescriptorBinaryForm(), 0)
    if ($null -eq $descriptor.DiscretionaryAcl) { throw 'A backup parent directory has no restrictive DACL.' }
    foreach ($rule in $Security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) { continue }
        # DELETE, DELETE_CHILD, WRITE_DAC, WRITE_OWNER and GENERIC_ALL replace ancestor authority.
        if ($rule.AccessControlType -eq 'Allow' -and ([long]$rule.FileSystemRights -band 0x100D0040) -ne 0 -and
            $rule.IdentityReference.Value -notin $TrustedWriters) {
            throw 'A backup parent directory permits untrusted namespace replacement.'
        }
    }
}

function Assert-DatabaseBackupNamespaceTrusted {
    param([string] $Path, [string[]] $TrustedOwners, [string[]] $TrustedWriters)
    # Windows volume roots normally belong to the TrustedInstaller service.
    $trustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
    for ($parent = Split-Path -Path $Path -Parent; $parent; $parent = Split-Path -Path $parent -Parent) {
        $item = Get-Item -LiteralPath $parent -Force -ErrorAction Stop
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Backup parents must be existing physical directories provisioned by a trusted operator.'
        }
        Assert-DatabaseBackupNamespaceAclTrusted -Security (Get-Acl -LiteralPath $parent -ErrorAction Stop) `
            -TrustedOwners ($TrustedOwners + $trustedInstaller) -TrustedWriters ($TrustedWriters + $trustedInstaller)
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

function Assert-DatabaseBackupTransferInputsTrusted {
    param($Destination, [string[]] $TrustedOwners, [string[]] $TrustedWriters)
    $trustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
    foreach ($path in @($Destination.sshExecutable, $Destination.identityFile, $Destination.knownHostsFile)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Required SSH transfer input is missing.' }
        Assert-DatabaseBackupNamespaceTrusted -Path $path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
        Assert-DatabaseBackupTreeTrusted -Path $path -TrustedOwners ($TrustedOwners + $trustedInstaller) -TrustedWriters ($TrustedWriters + $trustedInstaller)
    }
}

function Set-DatabaseBackupTreeSecurity {
    param([string] $Path, [string[]] $TrustedOwners, [string[]] $TrustedWriters,
          [string] $AdditionalFullControl, [string] $RootReadIdentity)
    $Path = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($Path -eq [IO.Path]::GetPathRoot($Path).TrimEnd('\')) { throw 'Managed backup security cannot target a volume root.' }
    Assert-DatabaseBackupNamespaceTrusted -Path $Path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
    $existed = Test-Path -LiteralPath $Path
    if ($existed) {
        Assert-DatabaseBackupTreeTrusted -Path $Path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
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
    if ($existed) {
        Set-Acl -LiteralPath $Path -AclObject $security
    } elseif ($PSVersionTable.PSEdition -eq 'Desktop') {
        # Supply the private DACL at creation rather than briefly inheriting broad parent grants.
        [IO.Directory]::CreateDirectory($Path, $security) | Out-Null
    } else {
        [IO.FileSystemAclExtensions]::Create((New-Object IO.DirectoryInfo($Path)), $security)
    }
    # Check again after taking control, before traversing or consuming any descendants.
    Assert-DatabaseBackupNamespaceTrusted -Path $Path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
    Assert-DatabaseBackupTreeTrusted -Path $Path -TrustedOwners $TrustedOwners -TrustedWriters $TrustedWriters
    $rootPath = (Get-Item -LiteralPath $Path -Force).FullName.TrimEnd('\')
    $items = @(Get-DatabaseBackupTreeItems -Path $Path)
    foreach ($item in $items | Where-Object { $_.FullName.TrimEnd('\') -ine $rootPath }) {
        $childSecurity = if ($item.PSIsContainer) { New-Object Security.AccessControl.DirectorySecurity } else { New-Object Security.AccessControl.FileSecurity }
        $childSecurity.SetOwner($administrator)
        $childSecurity.SetAccessRuleProtection($false, $false)
        Set-Acl -LiteralPath $item.FullName -AclObject $childSecurity
    }
}
