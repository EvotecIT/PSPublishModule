Import-Module .\PSPublishModule.psd1 -Force

$Folder = "C:\Users\przemyslaw.klys\Downloads\MDE\MDE\2016"

$registerCertificateSplat = @{
    LocalStore      = 'CurrentUser'
    Path            = $Folder
    Include         = @('*.ps1', '*.psd1', '*.psm1', '*.dll', '*.cat')
    TimeStampServer = 'http://timestamp.digicert.com'
    Thumbprint      = 'YOUR_CERTIFICATE_THUMBPRINT'
}

Register-Certificate @registerCertificateSplat -Verbose
