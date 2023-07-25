function Register-Certificate {
    [cmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory, ParameterSetName = 'PFX')][string] $CertificatePFX,
        [Parameter(Mandatory, ParameterSetName = 'Store')][ValidateSet('LocalMachine', 'CurrentUser')][string] $LocalStore,
        [alias('CertificateThumbprint')][Parameter(ParameterSetName = 'Store')][string] $Thumbprint,
        [Parameter(Mandatory)][string] $Path,
        [string] $TimeStampServer = 'http://timestamp.digicert.com',
        [ValidateSet('All', 'NonRoot', 'Signer')] [string] $IncludeChain = 'All',
        [string[]] $Include = @('*.ps1', '*.psd1', '*.psm1', '*.dll', '*.cat')
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
            Get-ChildItem -Path $Path -Filter * -Include $Include -Recurse -ErrorAction SilentlyContinue | Where-Object {
                ($_ | Get-AuthenticodeSignature).Status -eq 'NotSigned'
            } | Set-AuthenticodeSignature -Certificate $Certificate -TimestampServer $TimeStampServer -IncludeChain $IncludeChain -HashAlgorithm Sha256
        }
    }
}