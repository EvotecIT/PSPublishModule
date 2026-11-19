param([Parameter(Mandatory)] [string] $ArtifactPath)
$ErrorActionPreference = 'Stop'
if (!$IsWindows -or $PSVersionTable.PSVersion.ToString() -cne '7.6.6') {
    throw 'Locksmith2 qualification requires PowerShell 7.6.6 on Windows.'
}
if ([Uri]::CheckHostName($env:POWERFORGE_EXTERNAL_AD_AUDIT_TARGET) -ne [UriHostNameType]::Dns -or
    $env:POWERFORGE_EXTERNAL_AD_AUDIT_TARGET -notmatch '\.') {
    throw 'Set POWERFORGE_EXTERNAL_AD_AUDIT_TARGET to the DNS name of an approved read-only AD and AD CS lab.'
}
Import-Module $ArtifactPath -Force -ErrorAction Stop 6>$null
$result = Get-LS2Stores -ErrorAction Stop
if ($null -eq $result) { throw 'The imported module did not return its store snapshot.' }
