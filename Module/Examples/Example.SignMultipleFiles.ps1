Import-Module .\PSPublishModule.psd1 -Force

$Folder = "C:\Users\przemyslaw.klys\Downloads\MDE\MDE\2016"

$registerCertificateSplat = @{
    LocalStore      = 'CurrentUser'
    Path            = $Folder
    Include         = @('*.ps1', '*.psd1', '*.psm1', '*.dll', '*.cat')
    TimeStampServer = 'http://timestamp.digicert.com'
    Thumbprint      = '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703'
}

Register-Certificate @registerCertificateSplat -Verbose