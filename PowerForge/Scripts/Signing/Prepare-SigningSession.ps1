[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Directory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string] $Thumbprint,
    [Parameter(Mandatory)][string] $TimestampUrl,
    [Parameter(Mandatory)][string] $ModulePath
)
$ErrorActionPreference = 'Stop'
$WarningPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Signing-provider preparation requires an interactive Windows user session.'
}
$importWarnings = $WarningPreference
try {
    # Nested modules can emit verb-name warnings independently of DisableNameChecking.
    $WarningPreference = 'SilentlyContinue'
    Import-Module $ModulePath -DisableNameChecking -ErrorAction Stop | Out-Null
} finally { $WarningPreference = $importWarnings }
Import-Module Microsoft.PowerShell.Security -ErrorAction Stop | Out-Null
Get-Command -Name Get-AuthenticodeSignature, Set-AuthenticodeSignature -ErrorAction Stop | Out-Null
$artifact = Join-Path $Directory 'signing-proof.ps1'
$service = [PowerForge.AuthenticodeSigningService]::new([PowerForge.NullLogger]::new())
$lookup = $service.SelectCertificateFromStore([PowerForge.CertificateStoreLocation]::CurrentUser, $Thumbprint)
if ($null -eq $lookup.Certificate) { throw 'The exact configured signing certificate is unavailable.' }
try {
    $request = [PowerForge.AuthenticodeSignRequest]::new()
    $request.Certificate = $lookup.Certificate
    $request.FilePaths = [string[]]@($artifact)
    $request.TimeStampServer = $TimestampUrl
    $request.HashAlgorithm = 'SHA256'
    $null = $service.SignFiles($request)
} finally { $lookup.Certificate.Dispose() }
$bytes = [IO.FileStream]::new($artifact, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $artifact -ErrorAction Stop
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw 'The generated proof did not verify with the expected signing certificate.'
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    @{ signatureStatus = $signature.Status.ToString(); thumbprint = $signature.SignerCertificate.Thumbprint; artifactSha256 = $digest } |
        ConvertTo-Json -Compress
} finally {
    $bytes.Dispose()
}
