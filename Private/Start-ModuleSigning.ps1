function Start-ModuleSigning {
    [CmdletBinding()]
    param(
        $Configuration,
        $FullModuleTemporaryPath
    )
    if ($Configuration.Steps.BuildModule.SignMerged) {
        Write-TextWithTime -Text 'Applying signature to files' {
            $registerCertificateSplat = @{
                WarningAction   = 'SilentlyContinue'
                WarningVariable = 'Warnings'
                LocalStore      = 'CurrentUser'
                Path            = $FullModuleTemporaryPath
                # Include list will be determined below
                TimeStampServer = 'http://timestamp.digicert.com'
            }

            if ($Configuration.Options.Signing) {
                if ($Configuration.Options.Signing.CertificatePFXBase64) {
                    $Success = Import-ValidCertificate -CertificateAsBase64 $Configuration.Options.Signing.CertificatePFXBase64 -PfxPassword $Configuration.Options.Signing.CertificatePFXPassword
                    if (-not $Success) {
                        return $false
                    }
                    $registerCertificateSplat.Thumbprint = $Success.Thumbprint
                    # Write-Host $Success.Thumbprint
                } elseif ($Configuration.Options.Signing.CertificatePFXPath) {
                    $Success = Import-ValidCertificate -FilePath $Configuration.Options.Signing.CertificatePFXPath -PfxPassword $Configuration.Options.Signing.CertificatePFXPassword
                    if (-not $Success) {
                        return $false
                    }
                    #Write-Host $Success.Thumbprint
                    $registerCertificateSplat.Thumbprint = $Success.Thumbprint
                } else {
                    if ($Configuration.Options.Signing -and $Configuration.Options.Signing.Thumbprint) {
                        $registerCertificateSplat.Thumbprint = $Configuration.Options.Signing.Thumbprint
                    } elseif ($Configuration.Options.Signing -and $Configuration.Options.Signing.CertificateThumbprint) {
                        $registerCertificateSplat.Thumbprint = $Configuration.Options.Signing.CertificateThumbprint
                    }
                }
                # Build include patterns with safe defaults (scripts only)
                if ($Configuration.Options.Signing -and $Configuration.Options.Signing.Include) {
                    $registerCertificateSplat.Include = @($Configuration.Options.Signing.Include)
                } else {
                    $include = @('*.ps1','*.psm1','*.psd1')
                    if ($Configuration.Options.Signing -and $Configuration.Options.Signing.IncludeBinaries) {
                        $include += @('*.dll','*.cat')
                    }
                    if ($Configuration.Options.Signing -and $Configuration.Options.Signing.IncludeExe) {
                        $include += @('*.exe')
                    }
                    $registerCertificateSplat.Include = $include
                }

                # Exclude Internals unless explicitly enabled
                $excludePaths = @()
                if (-not ($Configuration.Options.Signing -and $Configuration.Options.Signing.IncludeInternals)) {
                    $excludePaths += 'Internals'
                }
                if ($Configuration.Options.Signing -and $Configuration.Options.Signing.ExcludePaths) {
                    $excludePaths += @($Configuration.Options.Signing.ExcludePaths)
                }
                if ($excludePaths.Count -gt 0) {
                    $registerCertificateSplat.ExcludePath = $excludePaths
                }

                [Array] $SignedFiles = Register-Certificate @registerCertificateSplat
                if ($Warnings) {
                    foreach ($W in $Warnings) {
                        Write-Text -Text "   [!] $($W.Message)" -Color Red
                    }
                }
                if ($SignedFiles.Count -eq 0) {
                    throw "Please configure certificate for use, or disable signing."
                    return $false
                } else {
                    if ($SignedFiles[0].Thumbprint) {
                        Write-Text -Text "   [i] Multiple certificates found for signing:"
                        foreach ($Certificate in $SignedFiles) {
                            Write-Text "      [>] Certificate $($Certificate.Thumbprint) with subject: $($Certificate.Subject)" -Color Yellow
                        }
                        throw "Please configure single certificate for use or disable signing."
                        return $false
                    } else {
                        foreach ($File in $SignedFiles) {
                            Write-Text "   [>] File $($File.Path) with status: $($File.StatusMessage)" -Color Yellow
                        }
                    }
                }
            }
        } -PreAppend Plus
    }
}
