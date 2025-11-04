Import-Module "$PSSCriptRoot\..\PSPublishModule.psd1" -Force

Import-ValidCertificate -CertificateAsBase64 $BasePfx -PfxPassword 'TemporaryPassword'

#$pfxCertFilePath = Join-Path -Path $PSScriptRoot -ChildPath "CodeSigningCertificate.pfx"
#Set-Content -Value $([System.Convert]::FromBase64String($env:BASE64_PFX)) -Path $pfxCertFilePath -Encoding Byte
#$codeSigningCert = Import-PfxCertificate -FilePath $pfxCertFilePath -Password $($env:PFX_PASSWORD | ConvertTo-SecureString -AsPlainText -Force) -CertStoreLocation Cert:\CurrentUser\My

#Register-Certificate -CertificatePFX -Path $PSSCriptRoot\Files
