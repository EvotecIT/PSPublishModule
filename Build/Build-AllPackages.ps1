Import-Module PSPublishModule -Force -ErrorAction Stop

$certificateThumbprint = '92E95FB58EFFA6A4A75E77A33CDD6BFE6DD30F1A'
Invoke-DotNetReleaseBuild -ProjectPath @(
    "$PSScriptRoot\..\PowerForge"
    "$PSScriptRoot\..\PowerForge.Blazor"
    #"$PSScriptRoot\..\PowerForge.Cli"
) -CertificateThumbprint $certificateThumbprint
