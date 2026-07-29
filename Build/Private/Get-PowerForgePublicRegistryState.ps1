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
        [string] $Version
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

    [pscustomobject]@{
        AnyPublished        = $publishedPackageIds.Count -gt 0 -or $modulePublished
        PublishedPackageIds = $publishedPackageIds
        ModulePublished     = $modulePublished
    }
}
