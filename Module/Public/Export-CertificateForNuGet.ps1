function Export-CertificateForNuGet {
    <#
    .SYNOPSIS
    Exports a code signing certificate to DER format for NuGet.org registration.

    .DESCRIPTION
    This function finds a code signing certificate by thumbprint or SHA256 hash and exports it
    to a .cer file in DER format, which is required for registering the certificate with NuGet.org.
    After exporting, you need to manually register this .cer file on NuGet.org under your account
    settings in the Certificates section.

    .PARAMETER CertificateThumbprint
    The SHA1 thumbprint of the certificate to export.

    .PARAMETER CertificateSha256
    The SHA256 hash of the certificate to export. Use this instead of thumbprint if you have the SHA256.

    .PARAMETER OutputPath
    The path where the .cer file will be saved. If not specified, saves to current directory
    with filename based on certificate subject.

    .PARAMETER LocalStore
    Certificate store location. Defaults to 'CurrentUser'.

    .EXAMPLE
    Export-CertificateForNuGet -CertificateThumbprint '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703' -OutputPath 'C:\Temp\MyCodeSigningCert.cer'

    Exports the certificate to the specified path for NuGet.org registration.

    .EXAMPLE
    Export-CertificateForNuGet -CertificateSha256 '769C6B450BE58DC6E15193EE3916282D73BCED16E5E2FF8ACD0850D604DD560C'

    Exports the certificate using SHA256 hash to the current directory.

    .NOTES
    After running this function:
    1. Go to https://www.nuget.org
    2. Sign in to your account
    3. Go to Account Settings > Certificates
    4. Click "Register new"
    5. Upload the generated .cer file
    6. Once registered, all future package uploads must be signed with this certificate
    #>
    [CmdletBinding(DefaultParameterSetName = 'Thumbprint')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Thumbprint')]
        [ValidateNotNullOrEmpty()]
        [string]$CertificateThumbprint,

        [Parameter(Mandatory, ParameterSetName = 'Sha256')]
        [ValidateNotNullOrEmpty()]
        [string]$CertificateSha256,

        [Parameter()]
        [string]$OutputPath,

        [Parameter()]
        [ValidateSet('CurrentUser', 'LocalMachine')]
        [string]$LocalStore = 'CurrentUser'
    )

    try {
        # Open certificate store
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', $LocalStore)
        $store.Open('ReadOnly')

        # Find certificate
        $cert = $null
        if ($PSCmdlet.ParameterSetName -eq 'Thumbprint') {
            $cert = $store.Certificates | Where-Object { $_.Thumbprint -eq $CertificateThumbprint }
        } else {
            $cert = $store.Certificates | Where-Object {
                $_.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256) -eq $CertificateSha256
            }
        }

        if (-not $cert) {
            if ($PSCmdlet.ParameterSetName -eq 'Thumbprint') {
                throw "Certificate with thumbprint '$CertificateThumbprint' not found in $LocalStore\My store"
            } else {
                throw "Certificate with SHA256 '$CertificateSha256' not found in $LocalStore\My store"
            }
        }

        # Verify it's a code signing certificate
        $hasCodeSigning = $cert.Extensions | Where-Object {
            $_.Oid.FriendlyName -eq 'Enhanced Key Usage'
        } | ForEach-Object {
            $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$_
            $eku.EnhancedKeyUsages | Where-Object { $_.FriendlyName -eq 'Code Signing' }
        }

        if (-not $hasCodeSigning) {
            Write-Warning "Certificate does not appear to have Code Signing capability. This may not work for NuGet package signing."
        }

        # Generate output path if not provided
        if (-not $OutputPath) {
            $subjectName = ($cert.Subject -split ',')[0] -replace 'CN=', '' -replace '[^\w\s-]', ''
            $fileName = "$subjectName-CodeSigning.cer"
            $OutputPath = Join-Path (Get-Location) $fileName
        }

        # Export certificate to DER format
        $certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
        [System.IO.File]::WriteAllBytes($OutputPath, $certBytes)

        Write-Host "Certificate exported successfully to: $OutputPath" -ForegroundColor Green
        Write-Host ""
        Write-Host "Next steps to register with NuGet.org:" -ForegroundColor Yellow
        Write-Host "1. Go to https://www.nuget.org and sign in" -ForegroundColor White
        Write-Host "2. Go to Account Settings > Certificates" -ForegroundColor White
        Write-Host "3. Click 'Register new'" -ForegroundColor White
        Write-Host "4. Upload the file: $OutputPath" -ForegroundColor White
        Write-Host "5. Once registered, all future packages must be signed with this certificate" -ForegroundColor White
        Write-Host ""
        Write-Host "Certificate details:" -ForegroundColor Cyan
        Write-Host "  Subject: $($cert.Subject)" -ForegroundColor White
        Write-Host "  Issuer: $($cert.Issuer)" -ForegroundColor White
        Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor White
        Write-Host "  SHA256: $($cert.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256))" -ForegroundColor White
        Write-Host "  Valid From: $($cert.NotBefore)" -ForegroundColor White
        Write-Host "  Valid To: $($cert.NotAfter)" -ForegroundColor White

        return @{
            Success = $true
            CertificatePath = $OutputPath
            Certificate = $cert
        }

    } catch {
        Write-Error "Failed to export certificate: $_"
        return @{
            Success = $false
            Error = $_.Exception.Message
        }
    } finally {
        if ($store) {
            $store.Close()
        }
    }
}