function Invoke-ManagedModuleBenchmarkJsonRequest {
    param(
        [string] $Uri
    )

    try {
        return Invoke-RestMethod -Uri $Uri -UseBasicParsing -Headers @{
            'User-Agent' = 'PowerForge.ManagedModuleBenchmark/1.0'
        }
    } catch {
        throw "Failed to query benchmark package metadata from '$Uri': $($_.Exception.Message)"
    }
}

function Resolve-ManagedModuleBenchmarkRepositorySource {
    param(
        [string] $Repository,
        [string] $RepositoryName
    )

    if ([string]::IsNullOrWhiteSpace($Repository) -or $Repository -eq 'PSGallery' -or $Repository -eq $RepositoryName) {
        return 'https://www.powershellgallery.com/api/v3/index.json'
    }

    $Repository
}

function Invoke-ManagedModuleBenchmarkTextRequest {
    param(
        [string] $Uri
    )

    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -Headers @{
            'User-Agent' = 'PowerForge.ManagedModuleBenchmark/1.0'
        }
        return [string]$response.Content
    } catch {
        throw "Failed to query benchmark package metadata from '$Uri': $($_.Exception.Message)"
    }
}

function Get-ManagedModuleBenchmarkPackageBaseAddress {
    param(
        [string] $RepositorySource
    )

    if ([string]::IsNullOrWhiteSpace($RepositorySource)) {
        throw 'Repository source is required for update baseline discovery.'
    }

    if ($RepositorySource -match '/v3-flatcontainer/?$') {
        return $RepositorySource.TrimEnd('/')
    }

    if ($RepositorySource -notmatch '/index\.json$') {
        throw "Update baseline discovery requires a NuGet v3 repository source. '$RepositorySource' is not a v3 service index."
    }

    $index = Invoke-ManagedModuleBenchmarkJsonRequest -Uri $RepositorySource
    $resource = @($index.resources | Where-Object {
            $_.'@type' -eq 'PackageBaseAddress/3.0.0' -or
            (@($_.'@type') -contains 'PackageBaseAddress/3.0.0')
        } | Select-Object -First 1)

    if ($resource.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$resource[0].'@id')) {
        throw "NuGet v3 repository '$RepositorySource' did not advertise a PackageBaseAddress resource."
    }

    ([string]$resource[0].'@id').TrimEnd('/')
}

function Get-ManagedModuleBenchmarkPackageVersions {
    param(
        [string] $ModuleName,
        [string] $RepositorySource
    )

    if ([string]::IsNullOrWhiteSpace($ModuleName)) {
        throw 'Module name is required for update baseline discovery.'
    }

    if ($RepositorySource -match 'powershellgallery\.com') {
        return Get-ManagedModuleBenchmarkPowerShellGalleryVersions -ModuleName $ModuleName
    }

    $baseAddress = Get-ManagedModuleBenchmarkPackageBaseAddress -RepositorySource $RepositorySource
    $packageId = [Uri]::EscapeDataString($ModuleName.ToLowerInvariant())
    $versionIndex = Invoke-ManagedModuleBenchmarkJsonRequest -Uri "$baseAddress/$packageId/index.json"

    @($versionIndex.versions | ForEach-Object { [string]$_ } | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        })
}

function Get-ManagedModuleBenchmarkPowerShellGalleryVersions {
    param(
        [string] $ModuleName
    )

    $escapedName = $ModuleName.Replace("'", "''")
    $uri = "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='$escapedName'"
    $versions = [Collections.Generic.List[string]]::new()

    while (-not [string]::IsNullOrWhiteSpace($uri)) {
        [xml]$feed = Invoke-ManagedModuleBenchmarkTextRequest -Uri $uri
        foreach ($entry in @($feed.GetElementsByTagName('entry'))) {
            $id = @($entry.GetElementsByTagName('id') | Select-Object -First 1)
            if ($id.Count -eq 0) {
                continue
            }

            $text = [string]$id[0].InnerText
            $match = [regex]::Match($text, "Version='([^']+)'")
            if ($match.Success -and -not $versions.Contains($match.Groups[1].Value)) {
                $versions.Add($match.Groups[1].Value)
            }
        }

        $next = $feed.SelectSingleNode("//*[local-name()='link' and @rel='next']")
        $uri = if ($next -and $next.Attributes['href']) {
            [string]$next.Attributes['href'].Value
        } else {
            ''
        }
    }

    $versions.ToArray()
}

function ConvertTo-ManagedModuleBenchmarkStableVersion {
    param(
        [string] $Version
    )

    if ([string]::IsNullOrWhiteSpace($Version) -or $Version.Contains('-')) {
        return $null
    }

    $parsed = $null
    if ([version]::TryParse($Version, [ref]$parsed)) {
        [pscustomobject]@{
            Text = $Version
            Version = $parsed
        }
    }
}

function Resolve-ManagedModuleBenchmarkUpdateBaseline {
    param(
        [string] $ModuleName,
        [string] $RequestedVersion,
        [string] $RepositorySource
    )

    $versions = @(Get-ManagedModuleBenchmarkPackageVersions -ModuleName $ModuleName -RepositorySource $RepositorySource |
        ForEach-Object { ConvertTo-ManagedModuleBenchmarkStableVersion -Version $_ } |
        Where-Object { $null -ne $_ } |
        Sort-Object -Property Version)

    if ($versions.Count -lt 2) {
        throw "Package '$ModuleName' does not have at least two stable versions in '$RepositorySource'."
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $requested = ConvertTo-ManagedModuleBenchmarkStableVersion -Version $RequestedVersion
        if (-not $requested) {
            throw "Requested update target '$RequestedVersion' is not a stable System.Version-compatible version."
        }

        $target = @($versions | Where-Object { $_.Version -eq $requested.Version } | Select-Object -First 1)
    } else {
        $target = @($versions | Select-Object -Last 1)
    }

    if ($target.Count -eq 0) {
        throw "Requested update target '$RequestedVersion' was not found for package '$ModuleName'."
    }

    $baseline = @($versions | Where-Object { $_.Version -lt $target[0].Version } | Select-Object -Last 1)
    if ($baseline.Count -eq 0) {
        throw "No stable update baseline lower than '$($target[0].Text)' was found for package '$ModuleName'."
    }

    [pscustomobject]@{
        BaselineVersion = [string]$baseline[0].Text
        TargetVersion = [string]$target[0].Text
        RepositorySource = $RepositorySource
    }
}

function Initialize-ManagedModuleBenchmarkUpdateBaseline {
    param(
        [string[]] $Operations,
        [string] $CurrentBaselineVersion,
        [string] $ModuleName,
        [string] $RequestedVersion,
        [string] $RepositorySource
    )

    $requiresBaseline = ($Operations -contains 'Update') -or ($Operations -contains 'RepairPlan')
    if (-not $requiresBaseline -or -not [string]::IsNullOrWhiteSpace($CurrentBaselineVersion)) {
        return [pscustomobject]@{
            BaselineVersion = $CurrentBaselineVersion
            TargetVersion = $RequestedVersion
            Error = ''
            Message = ''
        }
    }

    try {
        $resolution = Resolve-ManagedModuleBenchmarkUpdateBaseline -ModuleName $ModuleName -RequestedVersion $RequestedVersion -RepositorySource $RepositorySource
        return [pscustomobject]@{
            BaselineVersion = [string]$resolution.BaselineVersion
            TargetVersion = [string]$resolution.TargetVersion
            Error = ''
            Message = 'Resolved update baseline for {0}: {1} -> {2}' -f $ModuleName, $resolution.BaselineVersion, $resolution.TargetVersion
        }
    } catch {
        return [pscustomobject]@{
            BaselineVersion = ''
            TargetVersion = $RequestedVersion
            Error = $_.Exception.Message
            Message = ''
        }
    }
}
