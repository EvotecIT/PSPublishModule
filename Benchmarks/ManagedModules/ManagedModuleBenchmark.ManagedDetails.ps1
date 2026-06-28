function ConvertTo-Milliseconds {
    param([object] $TimeSpan)

    if ($null -eq $TimeSpan) {
        return 0
    }

    [math]::Round($TimeSpan.TotalMilliseconds, 2)
}

function Get-NumericPropertyValue {
    param(
        [object] $InputObject,
        [string] $Name
    )

    if ($null -eq $InputObject -or -not $InputObject.PSObject.Properties[$Name]) {
        return 0
    }

    $InputObject.$Name
}

function Add-ManagedInstallDetail {
    param(
        [Parameter(Mandatory)]
        [object] $Result,

        [string] $Parent,

        [int] $Depth,

        [System.Collections.Generic.List[object]] $Rows
    )

    $download = $Result.Download
    $authenticode = $Result.AuthenticodeVerification
    $Rows.Add([pscustomobject]@{
        Name = [string] $Result.Name
        Version = [string] $Result.Version
        Status = [string] $Result.Status
        ModulePath = [string] $Result.ModulePath
        Parent = $Parent
        Depth = $Depth
        ElapsedMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.Elapsed
        VersionResolutionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.VersionResolutionElapsed
        DownloadMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DownloadElapsed
        ExtractionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.ExtractionElapsed
        DependencyMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DependencyElapsed
        PromotionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.PromotionElapsed
        RepositoryRequestCount = [long] $Result.RepositoryRequestCount
        PackageRepositoryRequestCount = [long] (Get-NumericPropertyValue -InputObject $Result -Name 'PackageRepositoryRequestCount')
        FileCount = [int] $Result.FileCount
        ExtractedBytes = [long] $Result.ExtractedBytes
        DownloadBytes = if ($download) { [long] $download.BytesWritten } else { 0L }
        DownloadFromCache = if ($download) { [bool] $download.FromCache } else { $false }
        AuthenticodeCheckedFiles = if ($authenticode) { [int] $authenticode.CheckedFiles } else { 0 }
        AuthenticodeFiles = if ($authenticode) { @($authenticode.Files) } else { @() }
    })

    foreach ($dependency in @($Result.DependencyResults)) {
        Add-ManagedInstallDetail -Result $dependency -Parent $Result.Name -Depth ($Depth + 1) -Rows $Rows
    }
}

function Write-ManagedInstallDetail {
    param(
        [object] $Result,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or $null -eq $Result) {
        return
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    Add-ManagedInstallDetail -Result $Result -Parent '' -Depth 0 -Rows $rows
    $packages = @($rows)
    $uniquePackages = @(
        $packages |
            Group-Object -Property {
                if (-not [string]::IsNullOrWhiteSpace([string] $_.ModulePath)) {
                    [string] $_.ModulePath
                } else {
                    '{0}|{1}|{2}' -f $_.Name, $_.Version, $_.Status
                }
            } |
            ForEach-Object {
                $_.Group | Sort-Object Depth | Select-Object -First 1
            }
    )
    $summary = [pscustomobject]@{
        PackageCount = $packages.Count
        DependencyCount = [math]::Max(0, $packages.Count - 1)
        UniquePackageCount = $uniquePackages.Count
        UniqueDependencyCount = [math]::Max(0, $uniquePackages.Count - 1)
        InstalledPackageCount = @($uniquePackages | Where-Object Status -eq 'Installed').Count
        AlreadyInstalledPackageCount = @($uniquePackages | Where-Object Status -eq 'AlreadyInstalled').Count
        RootElapsedMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.Elapsed
        RootDependencyMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DependencyElapsed
        TotalDownloadMilliseconds = [math]::Round((($packages | Measure-Object DownloadMilliseconds -Sum).Sum), 2)
        TotalExtractionMilliseconds = [math]::Round((($packages | Measure-Object ExtractionMilliseconds -Sum).Sum), 2)
        TotalPromotionMilliseconds = [math]::Round((($packages | Measure-Object PromotionMilliseconds -Sum).Sum), 2)
        TotalRepositoryRequestCount = [long] $Result.RepositoryRequestCount
        TotalPackageRepositoryRequestCount = [long] (($packages | Measure-Object PackageRepositoryRequestCount -Sum).Sum)
        TotalDownloadBytes = [long] (($packages | Measure-Object DownloadBytes -Sum).Sum)
        CacheHitCount = @($packages | Where-Object DownloadFromCache).Count
    }

    $parent = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -Path $parent -ItemType Directory -Force | Out-Null
    }

    [pscustomobject]@{
        Summary = $summary
        Packages = $packages
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding UTF8
}
