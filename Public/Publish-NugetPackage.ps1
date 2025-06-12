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
    $result = [ordered]@{
        Success = $true
        Pushed  = @()
        Failed  = @()
        ErrorMessage = $null
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        $result.Success = $false
        $result.ErrorMessage = "Path '$Path' not found."
        return [PSCustomObject]$result
    }
    $packages = Get-ChildItem -Path $Path -Recurse -Filter '*.nupkg' -ErrorAction SilentlyContinue
    if (-not $packages) {
        $result.Success = $false
        $result.ErrorMessage = "No packages found in $Path"
        return [PSCustomObject]$result
    }
    foreach ($pkg in $packages) {
        dotnet nuget push $pkg.FullName --api-key $ApiKey --source $Source
        if ($LASTEXITCODE -eq 0) {
            $result.Pushed += $pkg.FullName
        } else {
            $result.Failed += $pkg.FullName
            $result.Success = $false
        }
    }
    return [PSCustomObject]$result
}
