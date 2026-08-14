$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$repositoryName = Get-BenchmarkInput RepositoryName PSGallery
$repositoryUri = Get-BenchmarkInput RepositoryUri 'https://www.powershellgallery.com/api/v3/index.json'
$moduleFastSource = Get-BenchmarkInput ModuleFastSource 'https://pwsh.gallery/index.json'
$moduleFastModulePath = Get-BenchmarkInput ModuleFastPath
$managedModulePath = Get-BenchmarkInput ManagedModulePath
$updateReadme = Get-BenchmarkInput UpdateReadme $false -Bool
$comparisonMode = if ($repositoryUri.TrimEnd('/') -eq $moduleFastSource.TrimEnd('/')) { 'IdenticalFeed' } else { 'DefaultSources' }

$managedArtifactPath = if ($managedModulePath -and $managedModulePath.Trim()) {
    [System.IO.Path]::GetFullPath($managedModulePath)
} else {
    [System.IO.Path]::GetFullPath([string] (Get-Command Install-ManagedModule -ErrorAction Stop).DLL)
}
$managedArtifactVersion = $null
$managedArtifactSha256 = $null
if (Test-Path -LiteralPath $managedArtifactPath -PathType Leaf) {
    $managedArtifactVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($managedArtifactPath).ProductVersion
    $managedArtifactSha256 = (Get-FileHash -LiteralPath $managedArtifactPath -Algorithm SHA256).Hash
}

$moduleFastManifestPath = if ($moduleFastModulePath -and $moduleFastModulePath.Trim()) {
    [System.IO.Path]::GetFullPath($moduleFastModulePath)
} else {
    Get-Module -ListAvailable -Name ModuleFast | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty Path
}
$moduleFastVersion = $null
$moduleFastSha256 = $null
if ($moduleFastManifestPath -and (Test-Path -LiteralPath $moduleFastManifestPath -PathType Leaf)) {
    $moduleFastManifest = Import-PowerShellDataFile -LiteralPath $moduleFastManifestPath
    $moduleFastVersion = [string] $moduleFastManifest.ModuleVersion
    $moduleFastPrerelease = $moduleFastManifest.PrivateData.PSData['Prerelease']
    if ($moduleFastPrerelease) {
        $moduleFastVersion += '-' + [string] $moduleFastPrerelease
    }
    $moduleFastBinaryPath = Join-Path (Split-Path -Parent $moduleFastManifestPath) 'ModuleFast.dll'
    if (Test-Path -LiteralPath $moduleFastBinaryPath -PathType Leaf) {
        $moduleFastSha256 = (Get-FileHash -LiteralPath $moduleFastBinaryPath -Algorithm SHA256).Hash
    }
}

New-BenchmarkSuite 'managed-modules' -OutputRoot (Join-Path $repositoryRoot 'Ignore\Benchmarks\ManagedModules') {
    Add-BenchmarkMetadata ComparisonMode $comparisonMode
    Add-BenchmarkMetadata ManagedRepositoryUri $repositoryUri
    Add-BenchmarkMetadata ModuleFastSource $moduleFastSource
    if ($managedArtifactVersion) { Add-BenchmarkMetadata ManagedModuleVersion $managedArtifactVersion }
    if ($managedArtifactSha256) { Add-BenchmarkMetadata ManagedModuleSha256 $managedArtifactSha256 }
    if ($moduleFastVersion) { Add-BenchmarkMetadata ModuleFastVersion $moduleFastVersion }
    if ($moduleFastSha256) { Add-BenchmarkMetadata ModuleFastSha256 $moduleFastSha256 }

    Set-BenchmarkPolicy -Warmup 1 -Iterations 3 -Order Rotated -OutlierMode None
    Set-BenchmarkProfile Current -Cleanup KeepOnFailure
    Add-BenchmarkCaseSource @(
        [pscustomobject]@{ Name = 'SingleModule'; ModuleName = 'PSScriptAnalyzer'; Version = '1.25.0'; AcceptLicense = $false }
        [pscustomobject]@{ Name = 'GraphAuthentication'; ModuleName = 'Microsoft.Graph.Authentication'; Version = '2.29.1'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'Graph'; ModuleName = 'Microsoft.Graph'; Version = '2.29.1'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'AzAccounts'; ModuleName = 'Az.Accounts'; Version = '5.1.0'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'Az'; ModuleName = 'Az'; Version = '14.0.0'; AcceptLicense = $true }
    )
    Add-BenchmarkAxis Host Core, Desktop

    Set-BenchmarkSetup {
        param($case, $run)

        $run.RepositoryName = $repositoryName
        $run.RepositoryUri = $repositoryUri
        $run.ModuleFastSource = $moduleFastSource
        $run.ModuleFastModulePath = $moduleFastModulePath
        $run.ManagedModulePath = $managedModulePath
        $workKey = "$repositoryRoot|$PID|$($run.RunId)|$($run.Iteration)|$($case.ModuleName)|$($case.Engine)|$($case.Operation)|$($case.Host)"
        $hashBytes = [System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($workKey))
        $hash = [System.BitConverter]::ToString($hashBytes).Replace('-', '').Substring(0, 12).ToLowerInvariant()
        $run.WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pf-mm-$hash"
        $run.InstallRoot = Join-Path $run.WorkRoot 'installed'
        $run.SaveRoot = Join-Path $run.WorkRoot 'saved'
        $run.PackageCacheRoot = Join-Path $run.WorkRoot 'package-cache'

        if (Test-Path -LiteralPath $run.WorkRoot) {
            Remove-Item -LiteralPath $run.WorkRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path $run.InstallRoot, $run.SaveRoot, $run.PackageCacheRoot -Force | Out-Null

        if ($case.Engine -eq 'Managed' -and $run.ManagedModulePath -and $run.ManagedModulePath.Trim()) {
            $requestedManagedPath = [System.IO.Path]::GetFullPath($run.ManagedModulePath)
            $run.ManagedExpectedSha256 = (Get-FileHash -LiteralPath $requestedManagedPath -Algorithm SHA256).Hash
            Import-Module -Name $requestedManagedPath -Force -ErrorAction Stop
            $run.ManagedCommandPath = [System.IO.Path]::GetFullPath([string] (Get-Command Install-ManagedModule -ErrorAction Stop).DLL)
            $run.ManagedCommandSha256 = (Get-FileHash -LiteralPath $run.ManagedCommandPath -Algorithm SHA256).Hash
        } elseif ($case.Engine -eq 'ModuleFast') {
            Remove-Module ModuleFast -Force -ErrorAction SilentlyContinue
            if ($run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
                Import-Module -Name $run.ModuleFastModulePath -Force -ErrorAction Stop
            } else {
                Import-Module ModuleFast -Force -ErrorAction Stop
            }
            if ($run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
                $expectedModuleFastBinary = Join-Path (Split-Path -Parent ([System.IO.Path]::GetFullPath($run.ModuleFastModulePath))) 'ModuleFast.dll'
                $loadedModuleFastBinary = Join-Path (Get-Module ModuleFast -ErrorAction Stop).ModuleBase 'ModuleFast.dll'
                $run.ModuleFastExpectedSha256 = (Get-FileHash -LiteralPath $expectedModuleFastBinary -Algorithm SHA256).Hash
                $run.ModuleFastCommandSha256 = (Get-FileHash -LiteralPath $loadedModuleFastBinary -Algorithm SHA256).Hash
            }
            Clear-ModuleFastCache
        }
    }

    Add-BenchmarkSkipRule {
        param($case)

        if ($case.Engine -eq 'ModuleFast' -and $case.Operation -ne 'Install') {
            return $true
        }

        if ($case.Engine -eq 'ModuleFast' -and $case.Host -notlike 'Core*') {
            return $true
        }

        if ($case.Engine -eq 'PSResourceGet' -and $case.Host -notlike 'Core*') {
            return $true
        }

        if ($case.Engine -eq 'PSResourceGet' -and -not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PSResourceGet)) {
            return $true
        }

        if ($case.Engine -eq 'PowerShellGet' -and -not (Get-Module -ListAvailable -Name PowerShellGet)) {
            return $true
        }

        if ($case.Profile -ne 'TemporaryLocalUser' -and
            $case.Operation -eq 'Install' -and
            @('PSResourceGet', 'PowerShellGet') -contains $case.Engine) {
            return $true
        }

        return $false
    }

    Add-BenchmarkEngine Managed {
        Add-BenchmarkOperation Find {
            param($case, $run)

            Find-ManagedModule -Name $case.ModuleName -Repository $run.RepositoryUri -RepositoryName $run.RepositoryName | Out-Null
        }

        Add-BenchmarkOperation Install {
            param($case, $run)

            $run.ManagedResult = Install-ManagedModule `
                -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryUri `
                -RepositoryName $run.RepositoryName `
                -Scope Custom `
                -ModuleRoot $run.InstallRoot `
                -AcceptLicense:$case.AcceptLicense `
                -AllowClobber `
                -Force
        }

        Add-BenchmarkOperation Save {
            param($case, $run)

            $run.ManagedResult = Save-ManagedModule `
                -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryUri `
                -RepositoryName $run.RepositoryName `
                -Path $run.SaveRoot `
                -PackageCacheDirectory $run.PackageCacheRoot `
                -AcceptLicense:$case.AcceptLicense `
                -AllowClobber `
                -Force
        }
    }

    Add-BenchmarkEngine ModuleFast {
        Add-BenchmarkOperation Install {
            param($case, $run)

            Install-ModuleFast "$($case.ModuleName)=$($case.Version)" `
                -Destination $run.InstallRoot `
                -Source $run.ModuleFastSource `
                -DestinationOnly `
                -NoPSModulePathUpdate `
                -Confirm:$false | Out-Null
        }
    }

    Add-BenchmarkEngine PSResourceGet {
        Add-BenchmarkOperation Find {
            param($case, $run)

            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Find-PSResource -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        Add-BenchmarkOperation Install {
            param($case, $run)

            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Install-PSResource -Name $case.ModuleName -Version $case.Version -Repository $run.RepositoryName -TrustRepository -AcceptLicense -Reinstall | Out-Null
        }

        Add-BenchmarkOperation Save {
            param($case, $run)

            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Save-PSResource -Name $case.ModuleName -Version $case.Version -Repository $run.RepositoryName -Path $run.SaveRoot -TrustRepository -AcceptLicense | Out-Null
        }
    }

    Add-BenchmarkEngine PowerShellGet {
        Add-BenchmarkOperation Find {
            param($case, $run)

            Import-Module PowerShellGet -ErrorAction Stop
            Find-Module -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        Add-BenchmarkOperation Install {
            param($case, $run)

            Import-Module PowerShellGet -ErrorAction Stop
            Install-Module -Name $case.ModuleName -RequiredVersion $case.Version -Repository $run.RepositoryName -Scope CurrentUser -AllowClobber -AcceptLicense -Force | Out-Null
        }

        Add-BenchmarkOperation Save {
            param($case, $run)

            Import-Module PowerShellGet -ErrorAction Stop
            Save-Module -Name $case.ModuleName -RequiredVersion $case.Version -Repository $run.RepositoryName -Path $run.SaveRoot -AcceptLicense -Force | Out-Null
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)

        $root = switch ($case.Operation) {
            'Install' {
                if ($case.Engine -notin @('Managed', 'ModuleFast')) { return }
                $run.InstallRoot
            }
            'Save' { $run.SaveRoot }
            default { return }
        }

        $moduleRoot = Join-Path $root $case.ModuleName
        Assert-BenchmarkPath $moduleRoot
        $manifests = @(Get-ChildItem -LiteralPath $moduleRoot -Recurse -File -Filter "$($case.ModuleName).psd1")
        Assert-BenchmarkValue -Actual $manifests.Count -Expected 1 -Message 'Expected exactly one requested module manifest.'
        $manifest = Import-PowerShellDataFile -Path $manifests[0].FullName
        Assert-BenchmarkValue -Actual ([string] $manifest.ModuleVersion) -Expected $case.Version -Message 'Installed manifest version must match the requested exact version.'
        if ($case.Engine -eq 'Managed' -and $run.ManagedModulePath -and $run.ManagedModulePath.Trim()) {
            Assert-BenchmarkValue -Actual $run.ManagedCommandSha256 -Expected $run.ManagedExpectedSha256 -Message 'Managed benchmark must use the pinned PSPublishModule binary bytes.'
        }
        if ($case.Engine -eq 'ModuleFast' -and $run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
            Assert-BenchmarkValue -Actual $run.ModuleFastCommandSha256 -Expected $run.ModuleFastExpectedSha256 -Message 'ModuleFast benchmark must use the pinned ModuleFast binary bytes.'
        }
    }

    Add-BenchmarkMetric DependencyCount {
        param($case, $run)

        if ($null -eq $run.ManagedResult -or $null -eq $run.ManagedResult.DependenciesInstalled) {
            return 0
        }

        return $run.ManagedResult.DependenciesInstalled.Count
    }

    Add-BenchmarkMetric InstalledFileCount {
        param($case, $run)

        $root = if ($case.Operation -eq 'Save') { $run.SaveRoot } else { $run.InstallRoot }
        return @(Get-ChildItem -LiteralPath $root -Recurse -File).Count
    }

    Add-BenchmarkMetric InstalledBytes {
        param($case, $run)

        $root = if ($case.Operation -eq 'Save') { $run.SaveRoot } else { $run.InstallRoot }
        return [long] ((Get-ChildItem -LiteralPath $root -Recurse -File | Measure-Object -Property Length -Sum).Sum)
    }

    Add-BenchmarkComparison Engine -Baseline Managed -Metric MedianMs
    if ($updateReadme) {
        Add-BenchmarkReadmeBlock (Join-Path $repositoryRoot 'README.MD') -Block 'managed-module-benchmark-table' -Renderer ComparisonTable
    }
    Set-BenchmarkArtifacts Json, Csv, Markdown
}
