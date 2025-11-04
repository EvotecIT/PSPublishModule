# Complete NuGet Package Signing and Publishing Example
# This script demonstrates the full process from certificate export to package publishing

Clear-Host

# Step 1: Certificate Information
# Replace these with your actual certificate details
$CertificateThumbprint = '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703'
$CertificateSha256 = '769C6B450BE58DC6E15193EE3916282D73BCED16E5E2FF8ACD0850D604DD560C'

# Project path
$ProjectPath = "$PSScriptRoot\..\IISParser"

Write-Host "=== NuGet Package Signing and Publishing Process ===" -ForegroundColor Cyan
Write-Host ""

# Step 2: Export certificate for NuGet.org registration (only needed once)
Write-Host "Step 1: Export certificate for NuGet.org registration" -ForegroundColor Yellow
Write-Host "This step is only needed once per certificate" -ForegroundColor Gray

# Uncomment the following lines to export your certificate
<#
$exportResult = Export-CertificateForNuGet -CertificateSha256 $CertificateSha256 -OutputPath "$env:TEMP\MyCodeSigningCert.cer"
if ($exportResult.Success) {
    Write-Host "Certificate exported successfully!" -ForegroundColor Green
    Write-Host "Please register this certificate at https://www.nuget.org" -ForegroundColor Yellow
    Write-Host "Account Settings > Certificates > Register new" -ForegroundColor Yellow
    Read-Host "Press Enter after you have registered the certificate on NuGet.org"
} else {
    Write-Error "Failed to export certificate: $($exportResult.Error)"
    exit 1
}
#>

Write-Host ""
Write-Host "Step 2: Build and sign the package" -ForegroundColor Yellow

# Step 3: Build and sign the package
try {
    $buildResult = Invoke-DotNetReleaseBuild -ProjectPath $ProjectPath -CertificateThumbprint $CertificateThumbprint -Verbose

    if ($buildResult.Success) {
        Write-Host "Build successful!" -ForegroundColor Green
        Write-Host "Version: $($buildResult.Version)" -ForegroundColor White
        Write-Host "Release Path: $($buildResult.ReleasePath)" -ForegroundColor White
        Write-Host "Packages created:" -ForegroundColor White
        foreach ($pkg in $buildResult.Packages) {
            Write-Host "  - $(Split-Path -Leaf $pkg)" -ForegroundColor Gray
        }
    } else {
        Write-Error "Build failed: $($buildResult.ErrorMessage)"
        exit 1
    }
} catch {
    Write-Error "Build process failed: $_"
    exit 1
}

Write-Host ""
Write-Host "Step 3: Verify package signature" -ForegroundColor Yellow

# Step 4: Verify the package signature
foreach ($pkg in $buildResult.Packages) {
    Write-Host "Verifying signature for: $(Split-Path -Leaf $pkg)" -ForegroundColor White
    dotnet nuget verify $pkg
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Package signature verified successfully" -ForegroundColor Green
    } else {
        Write-Warning "✗ Package signature verification failed"
    }
    Write-Host ""
}

Write-Host ""
Write-Host "Step 4: Publish to NuGet.org" -ForegroundColor Yellow

# Step 5: Publish to NuGet.org
# Make sure you have set your API key first:
# dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
# dotnet nuget setapikey YOUR_API_KEY --source nuget.org

foreach ($pkg in $buildResult.Packages) {
    Write-Host "Publishing: $(Split-Path -Leaf $pkg)" -ForegroundColor White

    # Uncomment the following line when ready to publish
    # dotnet nuget push $pkg --source nuget.org --api-key YOUR_API_KEY

    Write-Host "To publish this package, run:" -ForegroundColor Yellow
    Write-Host "dotnet nuget push `"$pkg`" --source nuget.org --api-key YOUR_API_KEY" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Important Notes ===" -ForegroundColor Cyan
Write-Host "1. Make sure your certificate is registered on NuGet.org BEFORE publishing" -ForegroundColor White
Write-Host "2. Once you register a certificate, ALL future packages must be signed" -ForegroundColor White
Write-Host "3. Use a valid API key from https://www.nuget.org/account/apikeys" -ForegroundColor White
Write-Host "4. The certificate must be from a trusted CA (not self-signed) for NuGet.org" -ForegroundColor White