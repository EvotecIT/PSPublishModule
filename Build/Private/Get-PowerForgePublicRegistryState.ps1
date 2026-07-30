function Get-PowerForgePublicRegistryState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]] $PackageIds,

        [Parameter(Mandatory)]
        [string] $NuGetSource,

        [Parameter(Mandatory)]
        [string] $ModuleName,

        [Parameter(Mandatory)]
        [string] $ModuleSource,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ExpectedCommit,

        [Parameter(Mandatory)]
        [ValidatePattern('^https://')]
        [string] $ExpectedRepositoryUrl
    )

    if ($PackageIds.Count -eq 0) {
        throw 'Public release recovery requires at least one configured package ID.'
    }

    $publishedPackageIds = @(
        foreach ($packageId in $PackageIds) {
            if (Test-PowerForgePublicPackageVersion `
                    -RepositoryKind NuGet `
                    -RepositorySource $NuGetSource `
                    -PackageId $packageId `
                    -Version $Version) {
                $packageId
            }
        }
    )
    $modulePublished = Test-PowerForgePublicPackageVersion `
        -RepositoryKind PowerShellGallery `
        -RepositorySource $ModuleSource `
        -PackageId $ModuleName `
        -Version $Version

    foreach ($packageId in $publishedPackageIds) {
        Assert-PowerForgePublicRegistryArtifactProvenance `
            -RepositoryKind NuGet `
            -PackageId $packageId `
            -Version $Version `
            -ExpectedCommit $ExpectedCommit `
            -ExpectedRepositoryUrl $ExpectedRepositoryUrl
    }
    if ($modulePublished) {
        Assert-PowerForgePublicRegistryArtifactProvenance `
            -RepositoryKind PowerShellGallery `
            -PackageId $ModuleName `
            -Version $Version `
            -ExpectedCommit $ExpectedCommit `
            -ExpectedRepositoryUrl $ExpectedRepositoryUrl
    }

    [pscustomobject]@{
        AnyPublished        = $publishedPackageIds.Count -gt 0 -or $modulePublished
        PublishedPackageIds = $publishedPackageIds
        ModulePublished     = $modulePublished
        ProvenanceVerified  = $true
    }
}

function Assert-PowerForgePublicRegistryArtifactProvenance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('NuGet', 'PowerShellGallery')]
        [string] $RepositoryKind,

        [Parameter(Mandatory)]
        [string] $PackageId,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $ExpectedCommit,

        [Parameter(Mandatory)]
        [string] $ExpectedRepositoryUrl
    )

    $lowerId = $PackageId.ToLowerInvariant()
    $lowerVersion = $Version.ToLowerInvariant()
    $uri = if ($RepositoryKind -eq 'NuGet') {
        "https://api.nuget.org/v3-flatcontainer/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
    } else {
        "https://cdn.powershellgallery.com/packages/$lowerId.$lowerVersion.nupkg"
    }
    $client = [Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd('PSPublishModule-Release/1.0')
        $bytes = $client.GetByteArrayAsync($uri).ConfigureAwait($false).GetAwaiter().GetResult()
    } finally {
        $client.Dispose()
    }

    $memory = [IO.MemoryStream]::new($bytes, $false)
    $archive = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read, $false)
    try {
        if ($RepositoryKind -eq 'NuGet') {
            $nuspec = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
            if ($nuspec.Count -ne 1) {
                throw "Published package '$PackageId' $Version does not contain one exact nuspec."
            }
            $reader = [IO.StreamReader]::new($nuspec[0].Open())
            try {
                [xml] $document = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }
            $metadata = $document.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
            $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
            $actualVersion = [string] $metadata.SelectSingleNode("*[local-name()='version']").InnerText
            $actualRepository = [string] $repository.GetAttribute('url')
            $actualCommit = [string] $repository.GetAttribute('commit')
        } else {
            $provenance = @($archive.Entries | Where-Object {
                    [IO.Path]::GetFileName($_.FullName) -ieq 'PowerForge.ReleaseProvenance.json'
                })
            if ($provenance.Count -ne 1) {
                throw "Published module '$PackageId' $Version does not contain one exact PowerForge.ReleaseProvenance.json."
            }
            $reader = [IO.StreamReader]::new($provenance[0].Open())
            try {
                $record = $reader.ReadToEnd() | ConvertFrom-Json -Depth 10
            } finally {
                $reader.Dispose()
            }
            if ([string] $record.moduleName -ine $PackageId) {
                throw "Published module provenance names '$($record.moduleName)', expected '$PackageId'."
            }
            $actualVersion = [string] $record.version
            $actualRepository = [string] $record.repository
            $actualCommit = [string] $record.commit
        }
        if ($actualVersion -ine $Version) {
            throw "Published registry provenance version '$actualVersion' does not match '$Version'."
        }
        if ($actualRepository.TrimEnd('/') -ine $ExpectedRepositoryUrl.TrimEnd('/')) {
            throw "Published registry provenance repository '$actualRepository' does not match '$ExpectedRepositoryUrl'."
        }
        if ($actualCommit -ine $ExpectedCommit) {
            throw "Published registry provenance commit '$actualCommit' does not match '$ExpectedCommit'."
        }
    } finally {
        $archive.Dispose()
        $memory.Dispose()
    }
}
