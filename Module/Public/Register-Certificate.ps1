function Register-Certificate {
    <#
    .SYNOPSIS
    Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).

    .DESCRIPTION
    Locates a code-signing certificate (by thumbprint from the Windows cert store or from a PFX)
    and applies Authenticode signatures to matching files under -Path.
    On Windows, uses Set-AuthenticodeSignature; on non-Windows, uses OpenAuthenticode module if available.

    .PARAMETER CertificatePFX
    A PFX file to use for signing. Mutually exclusive with -LocalStore/-Thumbprint.

    .PARAMETER LocalStore
    Certificate store to search ('LocalMachine' or 'CurrentUser') when using a certificate from the store.

    .PARAMETER Thumbprint
    Certificate thumbprint to select a single certificate from the chosen -LocalStore.

    .PARAMETER Path
    Root directory containing files to sign.

    .PARAMETER TimeStampServer
    RFC3161 timestamp server URL. Default: http://timestamp.digicert.com

    .PARAMETER IncludeChain
    Which portion of the chain to include in the signature: All, NotRoot, or Signer. Default: All.

    .PARAMETER Include
    File patterns to include during signing. Defaults to scripts only: '*.ps1','*.psd1','*.psm1'.
    You may pass additional patterns if needed (e.g., '*.dll').

    .PARAMETER ExcludePath
    One or more path substrings to exclude from signing. Useful for skipping folders like 'Internals' unless opted-in.

    .PARAMETER HashAlgorithm
    Hash algorithm for the signature. Default: SHA256.
    #>
    [cmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory, ParameterSetName = 'PFX')][string] $CertificatePFX,
        [Parameter(Mandatory, ParameterSetName = 'Store')][ValidateSet('LocalMachine', 'CurrentUser')][string] $LocalStore,
        [alias('CertificateThumbprint')][Parameter(ParameterSetName = 'Store')][string] $Thumbprint,
        [Parameter(Mandatory)][string] $Path,
        [string] $TimeStampServer = 'http://timestamp.digicert.com',
        [ValidateSet('All', 'NotRoot', 'Signer')] [string] $IncludeChain = 'All',
        [string[]] $Include = @('*.ps1', '*.psd1', '*.psm1'),
        [string[]] $ExcludePath,
        [ValidateSet('SHA1', 'SHA256', 'SHA384', 'SHA512')][string] $HashAlgorithm = 'SHA256'
    )
    if ($PSBoundParameters.Keys -contains 'LocalStore') {
        $Cert = Get-ChildItem -Path "Cert:\$LocalStore\My" -CodeSigningCert
        if ($Thumbprint) {
            $Certificate = $Cert | Where-Object { $_.Thumbprint -eq $Thumbprint }
            if (-not $Certificate) {
                Write-Warning -Message "Register-Certificate - No certificates found by that thumbprint"
                return
            }
        } elseif ($Cert.Count -eq 0) {
            Write-Warning -Message "Register-Certificate - No certificates found in store."
            return
        } elseif ($Cert.Count -eq 1) {
            $Certificate = $Cert
        } else {
            if ($Thumbprint) {
                $Certificate = $Cert | Where-Object { $_.Thumbprint -eq $Thumbprint }
                if (-not $Certificate) {
                    Write-Warning -Message "Register-Certificate - No certificates found by that thumbprint"
                    return
                }
            } else {
                $CodeError = "Get-ChildItem -Path Cert:\$LocalStore\My -CodeSigningCert"
                Write-Warning -Message "Register-Certificate - More than one certificate found in store. Provide Thumbprint for expected certificate"
                Write-Warning -Message "Register-Certificate - Use: $CodeError"
                $Cert
                return
            }
        }
    } elseif ($PSBoundParameters.Keys -contains 'CertificatePFX') {
        if (Test-Path -LiteralPath $CertificatePFX) {
            $Certificate = Get-PfxCertificate -FilePath $CertificatePFX
            if (-not $Certificate) {
                Write-Warning -Message "Register-Certificate - No certificates found for PFX"
                return
            }
        }
    }
    if ($Certificate -and $Path) {
        if (Test-Path -LiteralPath $Path) {
            if ($null -ne $IsWindows -and $IsWindows -eq $false) {
                # This is for Linux/MacOS, we need to use OpenAuthenticode module
                $ModuleOpenAuthenticode = Get-Module -ListAvailable -Name 'OpenAuthenticode'
                if ($null -eq $ModuleOpenAuthenticode) {
                    Write-Warning -Message "Register-Certificate - OpenAuthenticode module not found. Please install it from PSGallery"
                    return
                }
                if ($IncludeChain -eq 'All') {
                    $IncludeOption = 'WholeChain'
                } elseif ($IncludeChain -eq 'NotRoot') {
                    $IncludeOption = 'ExcludeRoot'
                } elseif ($IncludeChain -eq 'Signer') {
                    $IncludeOption = 'EndCertOnly'
                } else {
                    $IncludeOption = 'None'
                }
                $items = Get-ChildItem -Path $Path -Filter * -Include $Include -Recurse -ErrorAction SilentlyContinue
                if ($ExcludePath) {
                    $items = $items | Where-Object {
                        $full = $_.FullName
                        ($full -notlike '*\Internals\*' -and $full -notlike '*/Internals/*') -and (
                            ($ExcludePath | ForEach-Object { $full -like ("*" + $_ + "*") }) -notcontains $true)
                    }
                }
                $items | Where-Object { ($_ | Get-OpenAuthenticodeSignature).Status -eq 'NotSigned' } |
                    Set-OpenAuthenticodeSignature -Certificate $Certificate -TimeStampServer $TimeStampServer -IncludeChain $IncludeOption -HashAlgorithm $HashAlgorithm

            } else {
                # This is for Windows, we need to use PKI module, it's usually installed by default
                $ModuleSigning = Get-Command -Name Set-AuthenticodeSignature
                if (-not $ModuleSigning) {
                    Write-Warning -Message "Register-Certificate - Code signing commands not found. Skipping signing."
                    return
                }
                $items = Get-ChildItem -Path $Path -Filter * -Include $Include -Recurse -ErrorAction SilentlyContinue
                if ($ExcludePath) {
                    $items = $items | Where-Object {
                        $full = $_.FullName
                        ($full -notlike '*\Internals\*' -and $full -notlike '*/Internals/*') -and (
                            ($ExcludePath | ForEach-Object { $full -like ("*" + $_ + "*") }) -notcontains $true)
                    }
                }
                $items | Where-Object { ($_ | Get-AuthenticodeSignature).Status -eq 'NotSigned' } |
                    Set-AuthenticodeSignature -Certificate $Certificate -TimestampServer $TimeStampServer -IncludeChain $IncludeChain -HashAlgorithm $HashAlgorithm
            }
        }
    }
}
