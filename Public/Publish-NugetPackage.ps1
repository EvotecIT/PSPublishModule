function Publish-NugetPackage {
    <#
    .SYNOPSIS
    Pushes NuGet packages to a feed.

    .DESCRIPTION
    Finds all *.nupkg files in the specified path and uploads them using
    `dotnet nuget push` with the provided API key and feed URL.

    .PARAMETER Path
    Directory to search for NuGet packages.

    .PARAMETER ApiKey
    API key used to authenticate against the NuGet feed.

    .PARAMETER Source
    NuGet feed URL. Defaults to https://api.nuget.org/v3/index.json.

    .EXAMPLE
    Publish-NugetPackage -Path 'C:\Git\Project\bin\Release' -ApiKey $MyKey
    Uploads all packages in the Release folder to NuGet.org.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Path,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ApiKey,
        [string]$Source = 'https://api.nuget.org/v3/index.json'
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Error "Publish-NugetPackage - Path '$Path' not found."
        return
    }
    $packages = Get-ChildItem -Path $Path -Recurse -Filter '*.nupkg' -ErrorAction SilentlyContinue
    if (-not $packages) {
        Write-Warning "Publish-NugetPackage - No packages found in $Path"
        return
    }
    foreach ($pkg in $packages) {
        dotnet nuget push $pkg.FullName --api-key $ApiKey --source $Source
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Publish-NugetPackage - Failed to push $($pkg.FullName)"
        }
    }
}
