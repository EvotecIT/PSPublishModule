function Test-PowerForgePublicPackageVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('NuGet', 'PowerShellGallery')]
        [string] $RepositoryKind,

        [Parameter(Mandatory)]
        [string] $RepositorySource,

        [Parameter(Mandatory)]
        [ValidatePattern('^[A-Za-z0-9_.-]+$')]
        [string] $PackageId,

        [Parameter(Mandatory)]
        [ValidatePattern('^\d+\.\d+\.\d+$')]
        [string] $Version
    )

    $normalizedSource = $RepositorySource.Trim().TrimEnd('/')
    if ($RepositoryKind -eq 'NuGet') {
        if ($normalizedSource -ine 'https://api.nuget.org/v3/index.json') {
            throw "Public release recovery cannot inspect unsupported NuGet source '$RepositorySource'."
        }
        $lowerId = $PackageId.ToLowerInvariant()
        $lowerVersion = $Version.ToLowerInvariant()
        $uri = "https://api.nuget.org/v3-flatcontainer/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
    } else {
        if ($normalizedSource -inotmatch '^https://www\.powershellgallery\.com/api/v[23](?:/index\.json)?$') {
            throw "Public release recovery cannot inspect unsupported PowerShell Gallery source '$RepositorySource'."
        }
        $lowerId = $PackageId.ToLowerInvariant()
        $lowerVersion = $Version.ToLowerInvariant()
        $uri = "https://cdn.powershellgallery.com/packages/$lowerId.$lowerVersion.nupkg"
    }

    $client = [Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd('PSPublishModule-Release/1.0')
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $uri)
        try {
            $response = $client.SendAsync(
                $request,
                [Net.Http.HttpCompletionOption]::ResponseHeadersRead).ConfigureAwait($false).GetAwaiter().GetResult()
            try {
                if ([int] $response.StatusCode -eq 404) {
                    return $false
                }
                if (-not $response.IsSuccessStatusCode) {
                    throw "Registry recovery probe failed for '$PackageId' $Version ($([int] $response.StatusCode) $($response.ReasonPhrase))."
                }
                return $true
            } finally {
                $response.Dispose()
            }
        } finally {
            $request.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}
