# this command allows you to copy the certificate to clipboard, to be put as GitHub Action Secret Variable
# keep in mind that this certificate needs to have private key included, and be password protected to be used for signing
$pfxCertFilePath = "C:\Support\Important\EvotecSignGitHubCertificate.pfx"
$pfxContent = [System.IO.File]::ReadAllBytes($pfxCertFilePath)
$BasePfx = [System.Convert]::ToBase64String($pfxContent)
$BasePfx | Set-Clipboard
