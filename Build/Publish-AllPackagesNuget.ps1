Import-Module PSPublishModule -Force -ErrorAction Stop

$NugetAPI = Get-Content -Raw -LiteralPath "C:\Support\Important\NugetOrgEvotec.txt"
Publish-NugetPackage -Path @(
    "$PSScriptRoot\..\PowerForge\bin\Release"
    "$PSScriptRoot\..\PowerForge.Blazor\bin\Release"
    #"$PSScriptRoot\..\PowerForge.Cli\bin\Release"
) -ApiKey $NugetAPI -SkipDuplicate
