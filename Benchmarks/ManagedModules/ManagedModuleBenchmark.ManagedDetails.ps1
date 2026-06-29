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

function Get-ManagedDetailSum {
    param(
        [object[]] $Rows,
        [string] $Name
    )

    $measure = @($Rows | Measure-Object $Name -Sum)
    if ($measure.Count -eq 0 -or -not $measure[0].PSObject.Properties['Sum'] -or $null -eq $measure[0].Sum) {
        return 0
    }

    $measure[0].Sum
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
    $elapsedMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.Elapsed
    $downloadMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DownloadElapsed
    $extractionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.ExtractionElapsed
    $extractionCacheLockWaitMilliseconds = if ($null -ne $Result.PSObject.Properties['ExtractionCacheLockWaitElapsed']) {
        ConvertTo-Milliseconds -TimeSpan $Result.ExtractionCacheLockWaitElapsed
    } else {
        0
    }
    $dependencyMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DependencyElapsed
    $promotionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.PromotionElapsed
    $installLockWaitMilliseconds = if ($null -ne $Result.PSObject.Properties['InstallLockWaitElapsed']) {
        ConvertTo-Milliseconds -TimeSpan $Result.InstallLockWaitElapsed
    } else {
        0
    }
    $hasExplicitCoalescedWait = $null -ne $Result.PSObject.Properties['CoalescedWaitElapsed']
    $explicitCoalescedWaitMilliseconds = if ($hasExplicitCoalescedWait) {
        ConvertTo-Milliseconds -TimeSpan $Result.CoalescedWaitElapsed
    } else {
        0
    }
    $coalescedWaitMilliseconds = if ($hasExplicitCoalescedWait) {
        $explicitCoalescedWaitMilliseconds
    } elseif (
        [string] $Result.Status -eq 'AlreadyInstalled' -and
        [double] $elapsedMilliseconds -gt 0 -and
        [double] $downloadMilliseconds -eq 0 -and
        [double] $extractionMilliseconds -eq 0 -and
        [double] $dependencyMilliseconds -eq 0 -and
        [double] $promotionMilliseconds -eq 0) {
        $elapsedMilliseconds
    } else {
        0
    }

    $Rows.Add([pscustomobject]@{
        Name = [string] $Result.Name
        Version = [string] $Result.Version
        Status = [string] $Result.Status
        ModulePath = [string] $Result.ModulePath
        Parent = $Parent
        Depth = $Depth
        ElapsedMilliseconds = $elapsedMilliseconds
        VersionResolutionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.VersionResolutionElapsed
        DownloadMilliseconds = $downloadMilliseconds
        ExtractionMilliseconds = $extractionMilliseconds
        ExtractionCacheLockWaitMilliseconds = $extractionCacheLockWaitMilliseconds
        DependencyMilliseconds = $dependencyMilliseconds
        PromotionMilliseconds = $promotionMilliseconds
        InstallLockWaitMilliseconds = $installLockWaitMilliseconds
        CoalescedWaitMilliseconds = $coalescedWaitMilliseconds
        RepositoryRequestCount = [long] $Result.RepositoryRequestCount
        PackageRepositoryRequestCount = [long] (Get-NumericPropertyValue -InputObject $Result -Name 'PackageRepositoryRequestCount')
        PackageRepositoryRedirectCount = [long] (Get-NumericPropertyValue -InputObject $Result -Name 'PackageRepositoryRedirectCount')
        FileCount = [int] $Result.FileCount
        ExtractedBytes = [long] $Result.ExtractedBytes
        DownloadBytes = if ($download) { [long] $download.BytesWritten } else { 0L }
        DownloadRedirectCount = if ($download) { [long] (Get-NumericPropertyValue -InputObject $download -Name 'RedirectCount') } else { 0L }
        DownloadFromCache = if ($download) { [bool] $download.FromCache } else { $false }
        ExtractionFromCache = [bool] (Get-NumericPropertyValue -InputObject $Result -Name 'ExtractionFromCache')
        AuthenticodeCheckedFiles = if ($authenticode) { [int] $authenticode.CheckedFiles } else { 0 }
        AuthenticodeFiles = if ($authenticode) { @($authenticode.Files) } else { @() }
        AuthenticodeCatalogFiles = if ($authenticode) { [int] (Get-NumericPropertyValue -InputObject $authenticode -Name 'CatalogFiles') } else { 0 }
        AuthenticodeCatalogFilePaths = if ($authenticode -and $authenticode.PSObject.Properties['CatalogFilePaths']) { @($authenticode.CatalogFilePaths) } else { @() }
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
    $coalescedWaits = @(
        $packages |
            Where-Object {
                [double] $_.CoalescedWaitMilliseconds -gt 0
            }
    )
    $installLockWaits = @(
        $packages |
            Where-Object {
                [double] $_.InstallLockWaitMilliseconds -gt 0
            }
    )
    $slowestCoalescedWait = @(
        $coalescedWaits |
            Sort-Object @{ Expression = { [double] $_.CoalescedWaitMilliseconds }; Descending = $true } |
            Select-Object -First 1
    )
    $slowestInstallLockWait = @(
        $installLockWaits |
            Sort-Object @{ Expression = { [double] $_.InstallLockWaitMilliseconds }; Descending = $true } |
            Select-Object -First 1
    )
    $dependencyWork = @(
        $packages |
            Where-Object {
                [int] $_.Depth -gt 0 -and [double] $_.DependencyMilliseconds -gt 0
            }
    )
    $slowestDependencyPackage = @(
        $dependencyWork |
            Sort-Object @{ Expression = { [double] $_.DependencyMilliseconds }; Descending = $true } |
            Select-Object -First 1
    )
    $slowestMaterializedPackage = @(
        $packages |
            Where-Object { $_.Status -eq 'Installed' -and [int] $_.Depth -gt 0 } |
            Sort-Object @{ Expression = { [double] $_.ElapsedMilliseconds }; Descending = $true } |
            Select-Object -First 1
    )
    $summary = [pscustomobject]@{
        PackageCount = $packages.Count
        DependencyCount = [math]::Max(0, $packages.Count - 1)
        UniquePackageCount = $uniquePackages.Count
        UniqueDependencyCount = [math]::Max(0, $uniquePackages.Count - 1)
        InstalledPackageCount = @($packages | Where-Object Status -eq 'Installed').Count
        AlreadyInstalledPackageCount = @($packages | Where-Object Status -eq 'AlreadyInstalled').Count
        RootElapsedMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.Elapsed
        RootDependencyMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DependencyElapsed
        TotalDownloadMilliseconds = [math]::Round((($packages | Measure-Object DownloadMilliseconds -Sum).Sum), 2)
        TotalExtractionMilliseconds = [math]::Round((($packages | Measure-Object ExtractionMilliseconds -Sum).Sum), 2)
        TotalExtractionCacheLockWaitMilliseconds = [math]::Round((($packages | Measure-Object ExtractionCacheLockWaitMilliseconds -Sum).Sum), 2)
        TotalDependencyMilliseconds = [math]::Round((Get-ManagedDetailSum -Rows $dependencyWork -Name 'DependencyMilliseconds'), 2)
        TotalPromotionMilliseconds = [math]::Round((($packages | Measure-Object PromotionMilliseconds -Sum).Sum), 2)
        TotalRepositoryRequestCount = [long] $Result.RepositoryRequestCount
        TotalPackageRepositoryRequestCount = [long] (($packages | Measure-Object PackageRepositoryRequestCount -Sum).Sum)
        TotalPackageRepositoryRedirectCount = [long] (($packages | Measure-Object PackageRepositoryRedirectCount -Sum).Sum)
        TotalDownloadRedirectCount = [long] (($packages | Measure-Object DownloadRedirectCount -Sum).Sum)
        TotalDownloadBytes = [long] (($packages | Measure-Object DownloadBytes -Sum).Sum)
        CacheHitCount = @($packages | Where-Object DownloadFromCache).Count
        ExtractionCacheHitCount = @($packages | Where-Object ExtractionFromCache).Count
        TotalAuthenticodeCheckedFiles = [long] (($packages | Measure-Object AuthenticodeCheckedFiles -Sum).Sum)
        TotalAuthenticodeCatalogFiles = [long] (($packages | Measure-Object AuthenticodeCatalogFiles -Sum).Sum)
        CoalescedWaitCount = $coalescedWaits.Count
        TotalCoalescedWaitMilliseconds = [math]::Round((Get-ManagedDetailSum -Rows $coalescedWaits -Name 'CoalescedWaitMilliseconds'), 2)
        SlowestCoalescedWaitName = if ($slowestCoalescedWait.Count) { [string] $slowestCoalescedWait[0].Name } else { '' }
        SlowestCoalescedWaitMilliseconds = if ($slowestCoalescedWait.Count) { [double] $slowestCoalescedWait[0].CoalescedWaitMilliseconds } else { 0.0 }
        InstallLockWaitCount = $installLockWaits.Count
        TotalInstallLockWaitMilliseconds = [math]::Round((Get-ManagedDetailSum -Rows $installLockWaits -Name 'InstallLockWaitMilliseconds'), 2)
        SlowestInstallLockWaitName = if ($slowestInstallLockWait.Count) { [string] $slowestInstallLockWait[0].Name } else { '' }
        SlowestInstallLockWaitMilliseconds = if ($slowestInstallLockWait.Count) { [double] $slowestInstallLockWait[0].InstallLockWaitMilliseconds } else { 0.0 }
        SlowestDependencyPackageName = if ($slowestDependencyPackage.Count) { [string] $slowestDependencyPackage[0].Name } else { '' }
        SlowestDependencyPackageParent = if ($slowestDependencyPackage.Count) { [string] $slowestDependencyPackage[0].Parent } else { '' }
        SlowestDependencyPackageMilliseconds = if ($slowestDependencyPackage.Count) { [double] $slowestDependencyPackage[0].DependencyMilliseconds } else { 0.0 }
        SlowestMaterializedPackageName = if ($slowestMaterializedPackage.Count) { [string] $slowestMaterializedPackage[0].Name } else { '' }
        SlowestMaterializedPackageMilliseconds = if ($slowestMaterializedPackage.Count) { [double] $slowestMaterializedPackage[0].ElapsedMilliseconds } else { 0.0 }
        SlowestMaterializedPackageExtractionMilliseconds = if ($slowestMaterializedPackage.Count) { [double] $slowestMaterializedPackage[0].ExtractionMilliseconds } else { 0.0 }
        SlowestMaterializedPackageExtractionCacheLockWaitMilliseconds = if ($slowestMaterializedPackage.Count) { [double] $slowestMaterializedPackage[0].ExtractionCacheLockWaitMilliseconds } else { 0.0 }
        SlowestMaterializedPackagePromotionMilliseconds = if ($slowestMaterializedPackage.Count) { [double] $slowestMaterializedPackage[0].PromotionMilliseconds } else { 0.0 }
    }

    $parent = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -Path $parent -ItemType Directory -Force | Out-Null
    }

    $detail = [pscustomobject]@{
        Summary = $summary
        Packages = $packages
    }

    if (Get-Command -Name Write-ManagedBenchmarkJson -ErrorAction SilentlyContinue) {
        Write-ManagedBenchmarkJson -InputObject $detail -Path $Path -Depth 6
        return
    }

    $detail | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding UTF8
}
